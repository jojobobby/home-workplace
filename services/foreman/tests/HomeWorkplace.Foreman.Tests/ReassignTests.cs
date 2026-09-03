using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class ReassignTests
{
    private static void Write(string dp, string id, string vendor)
    {
        var dir = Path.Combine(dp, "employees", id); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "employee.json"),
          $$$"""{"id":"{{{id}}}","name":"{{{id}}}","role":"r","vendor":"{{{vendor}}}","model":"m","claudeAllowedTools":["Read"],"codexSandbox":"read-only","schedule":{"wake":"09:00","sleep":"20:00"}}""");
        File.WriteAllText(Path.Combine(dir, "skills.md"), "s"); File.WriteAllText(Path.Combine(dir, "life.md"), "l");
    }
    private static async Task<TaskModel> Poll(HttpClient c, string id, Func<TaskModel, bool> p, int s = 8)
    { var end = DateTime.UtcNow.AddSeconds(s); while (DateTime.UtcNow < end) { var t = await c.GetFromJsonAsync<TaskModel>($"/tasks/{id}", TestJson.Options); if (t is not null && p(t)) return t; await Task.Delay(50); } throw new Xunit.Sdk.XunitException("timeout"); }

    [Fact]
    public async Task Reassigning_a_failed_task_across_vendors_reruns_it_under_the_new_employee()
    {
        using var factory = ForemanFactory.Create(out var dp);
        Write(dp, "ada", "claude"); Write(dp, "rex", "codex");
        factory.Provider.Enqueue(s => new RunResult
        {
            RunId = s.RunId, Status = RunOutcome.Failed, Summary = "boom", Ask = null,
            Artifacts = Array.Empty<string>(), SessionId = s.SessionId ?? Guid.NewGuid().ToString(),
            Usage = new Usage(1, null, null, null, null), RawTail = "",
        });
        factory.Provider.EnqueueDone("rex fixed it");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null); await c.PostAsync("/employees/rex/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks", new { title = "T", brief = "b", assignee = "ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        await Poll(c, t!.Id, x => x.Status == TaskState.Failed);

        await c.PostAsJsonAsync($"/tasks/{t.Id}/reassign", new { assignee = "rex" });
        var done = await Poll(c, t.Id, x => x.Status == TaskState.Done);

        Assert.Equal("rex", done.Assignee);
        var rexSpec = factory.Provider.Specs.Last();
        Assert.Equal(Vendor.Codex, rexSpec.Employee.Vendor);
        Assert.Equal(SessionMode.New, rexSpec.Mode);
    }

    [Fact]
    public async Task Reassigning_to_an_unknown_employee_is_400()
    {
        using var factory = ForemanFactory.Create(out var dp); Write(dp, "ada", "claude");
        using var c = factory.CreateClient();
        var t = await (await c.PostAsJsonAsync("/tasks", new { title = "T", brief = "b", assignee = "ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var resp = await c.PostAsJsonAsync($"/tasks/{t!.Id}/reassign", new { assignee = "ghost" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Cancel_stops_a_queued_task()
    {
        using var factory = ForemanFactory.Create(out var dp); Write(dp, "ada", "claude");
        using var c = factory.CreateClient(); // ada left asleep so the task stays queued
        var t = await (await c.PostAsJsonAsync("/tasks", new { title = "T", brief = "b", assignee = "ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var resp = await c.PostAsync($"/tasks/{t!.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(TaskState.Cancelled, (await c.GetFromJsonAsync<TaskModel>($"/tasks/{t.Id}", TestJson.Options))!.Status);
    }
}
