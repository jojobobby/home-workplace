using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class ManagerTicketTests
{
    private static Task<HttpResponseMessage> PostTicket(HttpClient c, string title, string? role, decimal? budget = null)
        => c.PostAsJsonAsync("/tickets", new { title, brief = "Do it well", role, budgetUsd = budget });

    private static async Task<TaskModel> PollTask(HttpClient c, string id, Func<TaskModel, bool> p, int s = 10)
    {
        var end = DateTime.UtcNow.AddSeconds(s); TaskModel? last = null;
        while (DateTime.UtcNow < end) { last = await c.GetFromJsonAsync<TaskModel>($"/tasks/{id}", TestJson.Options); if (last is not null && p(last)) return last; await Task.Delay(50); }
        throw new Xunit.Sdk.XunitException($"task {id} not in the expected state (status {last?.Status}, assignee '{last?.Assignee}', goal {last?.GoalId})");
    }

    private static async Task<GoalModel> PollGoal(HttpClient c, string id, Func<GoalModel, bool> p, int s = 10)
    {
        var end = DateTime.UtcNow.AddSeconds(s); GoalModel? last = null;
        while (DateTime.UtcNow < end) { last = await c.GetFromJsonAsync<GoalModel>($"/goals/{id}", TestJson.Options); if (last is not null && p(last)) return last; await Task.Delay(50); }
        throw new Xunit.Sdk.XunitException($"goal {id} not in the expected state (status {last?.Status}, tasks {last?.TaskIds.Count})");
    }

    [Fact]
    public async Task A_manager_claim_turns_the_ticket_into_a_linked_goal_and_the_plan_reaches_the_team()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia", role: "Engineering manager");
        GoalTests.WriteEmployee(dp, "ada", role: "Software engineer");
        factory.Provider.EnqueueDecision(new ManagerDecision("splitting it",
            new[] { new ManagerAction("create_task", Assignee: "ada", Title: "Part A", Brief: "a"), new ManagerAction("post_ticket", Title: "Part B", Brief: "b", Role: "Software engineer") }));
        factory.Provider.EnqueueDone(); factory.Provider.EnqueueDone();
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);
        await c.PostAsync("/employees/ada/wake", null);

        var ticket = await (await PostTicket(c, "Build the sprite tool", "Engineering manager", 7m)).Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var handed = await PollTask(c, ticket!.Id, t => t.GoalId is not null);
        Assert.Equal("mia", handed.Assignee);
        Assert.Equal(TaskState.Waiting, handed.Status);

        var goal = await PollGoal(c, handed.GoalId!, g => g.TaskIds.Count == 2);
        Assert.Equal("Build the sprite tool", goal.Title);
        Assert.Equal("mia", goal.Manager);
        Assert.Equal(7m, goal.BudgetUsd);
        Assert.Equal(ticket.Id, goal.TicketId);

        var tasks = await c.GetFromJsonAsync<List<TaskModel>>("/tasks", TestJson.Options);
        var partA = tasks!.Single(t => t.Title == "Part A");
        var partB = tasks.Single(t => t.Title == "Part B");
        Assert.Equal("ada", partA.Assignee); Assert.Equal(goal.Id, partA.GoalId);
        Assert.Equal("Software engineer", partB.Role); Assert.Equal(goal.Id, partB.GoalId);
        await PollTask(c, partB.Id, t => t.Assignee == "ada" && t.Status == TaskState.Done);   // pinned, then claimed by the idle engineer

        var prompt = factory.Provider.ManagerSpecs[0].Prompt;
        Assert.Contains("post_ticket", prompt);
        Assert.Contains("Software engineer", prompt);
        Assert.Contains("post_ticket", factory.Provider.ManagerSpecs[0].SystemPrompt);
    }

    [Fact]
    public async Task Completing_the_goal_closes_the_ticket_and_a_missing_budget_uses_the_default()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia", role: "Engineering manager");
        factory.Provider.EnqueueDecision(new ManagerDecision("nothing to do here", new[] { new ManagerAction("complete") }));
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);

        var ticket = await (await PostTicket(c, "Tiny", "Engineering manager")).Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var done = await PollTask(c, ticket!.Id, t => t.Status == TaskState.Done);
        var goal = await c.GetFromJsonAsync<GoalModel>($"/goals/{done.GoalId}", TestJson.Options);
        Assert.Equal(GoalState.Done, goal!.Status);
        Assert.Equal(5m, goal.BudgetUsd);
        Assert.Contains(factory.ContextApi.Posts, p => p.Room == ticket.Room && p.Content.Contains("closed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cancelling_the_goal_cancels_the_ticket()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia", role: "Engineering manager");
        factory.Provider.EnqueueDecision(new ManagerDecision("thinking", new[] { new ManagerAction("wait") }));
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);

        var ticket = await (await PostTicket(c, "Long one", "Engineering manager")).Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var handed = await PollTask(c, ticket!.Id, t => t.GoalId is not null);
        await c.PostAsync($"/goals/{handed.GoalId}/cancel", null);
        await PollTask(c, ticket.Id, t => t.Status == TaskState.Cancelled);
    }
}

public class ManagerTicketParsingTests
{
    [Fact]
    public void The_schema_and_parser_carry_post_ticket_and_its_role()
    {
        Assert.Contains("post_ticket", ManagerActions.Schema);
        Assert.Contains("\"role\"", ManagerActions.Schema);
        var decision = ManagerActions.Parse("""{"summary":"split","actions":[{"kind":"post_ticket","title":"Part B","brief":"b","role":"Software engineer"}]}""");
        var action = Assert.Single(decision.Actions);
        Assert.Equal("post_ticket", action.Kind);
        Assert.Equal("Software engineer", action.Role);
    }
}
