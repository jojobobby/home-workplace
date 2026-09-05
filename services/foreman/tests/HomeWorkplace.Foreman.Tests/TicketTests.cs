using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class TicketTests
{
    private static Task<HttpResponseMessage> PostTicket(HttpClient c, string title = "Fix the parser", string? role = "Software engineer")
        => c.PostAsJsonAsync("/tickets", new { title, brief = "It crashes on empty input", role });

    private static async Task<TaskModel> Poll(HttpClient c, string id, Func<TaskModel, bool> p, int seconds = 10)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        TaskModel? last = null;
        while (DateTime.UtcNow < end)
        {
            last = await c.GetFromJsonAsync<TaskModel>($"/tasks/{id}", TestJson.Options);
            if (last is not null && p(last)) return last;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException($"task {id} did not reach the expected state (last: {last?.Status} / '{last?.Assignee}')");
    }

    [Fact]
    public async Task Posting_a_ticket_queues_it_unassigned_with_its_role_and_lists_it()
    {
        using var factory = ForemanFactory.Create(out _);
        using var c = factory.CreateClient();

        var resp = await PostTicket(c);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var t = await resp.Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        Assert.Equal(TaskState.Queued, t!.Status);
        Assert.Equal("", t.Assignee);
        Assert.Equal("Software engineer", t.Role);
        Assert.Contains(factory.ContextApi.Posts, p => p.Room == t.Room && p.Content.Contains("Ticket posted"));

        var tickets = await c.GetFromJsonAsync<List<TaskModel>>("/tickets", TestJson.Options);
        Assert.Contains(tickets!, x => x.Id == t.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/tickets", new { title = "", brief = "", role = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task An_idle_engineer_claims_the_oldest_ticket_runs_it_then_takes_the_next()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "ada", role: "Software engineer");
        factory.Provider.EnqueueDone(); factory.Provider.EnqueueDone();
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null);

        var first = await (await PostTicket(c, "First")).Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var second = await (await PostTicket(c, "Second")).Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var claimed = await Poll(c, first!.Id, t => t.Assignee == "ada");
        Assert.Contains(factory.ContextApi.Posts, p => p.Room == claimed.Room && p.Content.Contains("took the ticket"));
        await Poll(c, first.Id, t => t.Status == TaskState.Done);
        await Poll(c, second!.Id, t => t.Assignee == "ada" && t.Status == TaskState.Done);

        var events = await c.GetFromJsonAsync<EventPage>("/events?since=0&limit=200", TestJson.Options);
        Assert.Equal(2, events!.Events.Count(e => e.Type == "task.claimed" && e.EmployeeId == "ada"));
        Assert.Empty((await c.GetFromJsonAsync<List<TaskModel>>("/tickets", TestJson.Options))!);
    }

    [Fact]
    public async Task Roles_gate_claims_and_a_roleless_ticket_goes_to_anyone()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "rex", role: "Code reviewer");
        factory.Provider.EnqueueDone();
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/rex/wake", null);

        var engineerTicket = await (await PostTicket(c, "Engineer only")).Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        await Task.Delay(400);
        Assert.Equal("", (await c.GetFromJsonAsync<TaskModel>($"/tasks/{engineerTicket!.Id}", TestJson.Options))!.Assignee);

        var anyone = await (await PostTicket(c, "Anyone", role: null)).Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        await Poll(c, anyone!.Id, t => t.Assignee == "rex");
        Assert.Equal("", (await c.GetFromJsonAsync<TaskModel>($"/tasks/{engineerTicket.Id}", TestJson.Options))!.Assignee);
    }

    [Fact]
    public async Task Two_engineers_take_two_tickets_at_once()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "ada", role: "Software engineer");
        GoalTests.WriteEmployee(dp, "bob", role: "Software engineer");
        var gate = new TaskCompletionSource();
        factory.Provider.Enqueue(s => { gate.Task.Wait(); return FakeAgentProvider.Done(s); });
        factory.Provider.Enqueue(s => { gate.Task.Wait(); return FakeAgentProvider.Done(s); });
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null);
        await c.PostAsync("/employees/bob/wake", null);

        var t1 = await (await PostTicket(c, "One")).Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var t2 = await (await PostTicket(c, "Two")).Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var a = await Poll(c, t1!.Id, t => t.Assignee.Length > 0);
        var b = await Poll(c, t2!.Id, t => t.Assignee.Length > 0);

        Assert.Equal(new[] { "ada", "bob" }, new[] { a.Assignee, b.Assignee }.OrderBy(x => x));
        gate.SetResult();
    }
}
