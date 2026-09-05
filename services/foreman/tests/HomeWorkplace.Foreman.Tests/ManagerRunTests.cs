using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class ManagerRunTests
{
    private static async Task<GoalModel> Poll(HttpClient c, string id, Func<GoalModel, bool> p, int s = 10)
    { var end = DateTime.UtcNow.AddSeconds(s); while (DateTime.UtcNow < end) { var g = await c.GetFromJsonAsync<GoalModel>($"/goals/{id}", TestJson.Options); if (g is not null && p(g)) return g; await Task.Delay(50); } throw new Xunit.Sdk.XunitException("goal did not reach the expected state"); }

    private static async Task<T> Eventually<T>(Func<T?> probe, int s = 10) where T : class
    { var end = DateTime.UtcNow.AddSeconds(s); while (DateTime.UtcNow < end) { if (probe() is { } v) return v; await Task.Delay(50); } throw new Xunit.Sdk.XunitException("condition not met in time"); }

    private static Task<HttpResponseMessage> CreateGoal(HttpClient c, decimal budget = 5.00m)
        => c.PostAsJsonAsync("/goals", new { title = "Ship the parser", brief = "A JSON parser with tests", manager = "mia", budgetUsd = budget });

    private static ManagerDecision Decide(string summary, params ManagerAction[] actions) => new(summary, actions);

    [Fact]
    public async Task Creating_a_goal_runs_the_manager_with_goal_roster_and_budget_in_its_prompt()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia"); GoalTests.WriteEmployee(dp, "ada", role: "Engineer");
        factory.Provider.EnqueueDecision(Decide("looking", new ManagerAction("wait")));
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);

        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        await Poll(c, goal!.Id, g => g.Status == GoalState.Running);

        var spec = Assert.Single(factory.Provider.ManagerSpecs);
        Assert.Equal("mia", spec.Employee.Id);
        Assert.Contains("Ship the parser", spec.Prompt);
        Assert.Contains("ada", spec.Prompt);
        Assert.Contains("$0.00 / $5.00", spec.Prompt);
        Assert.Contains("mia", spec.SystemPrompt);
    }

    [Fact]
    public async Task Create_task_actions_spawn_worker_tasks_linked_to_the_goal()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia"); GoalTests.WriteEmployee(dp, "ada", role: "Engineer"); GoalTests.WriteEmployee(dp, "rex", role: "Reviewer", vendor: "codex");
        factory.Provider.EnqueueDecision(Decide("splitting it",
            new ManagerAction("create_task", Assignee: "ada", Title: "Write parser", Brief: "do it"),
            new ManagerAction("create_task", Assignee: "rex", Title: "Review parser", Brief: "check it")));
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);

        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        var running = await Poll(c, goal!.Id, g => g.TaskIds.Count == 2);

        var tasks = await c.GetFromJsonAsync<List<TaskModel>>("/tasks", TestJson.Options);
        Assert.Equal(2, tasks!.Count);
        Assert.All(tasks, t => Assert.Equal(goal.Id, t.GoalId));
        Assert.Contains(tasks, t => t.Assignee == "ada" && t.Title == "Write parser");
        Assert.Contains(tasks, t => t.Assignee == "rex" && t.Title == "Review parser");
        Assert.Equal(GoalState.Running, running.Status);
    }

    [Fact]
    public async Task Complete_closes_the_goal_and_fail_fails_it()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia");
        factory.Provider.EnqueueDecision(Decide("trivial, done", new ManagerAction("complete")));
        factory.Provider.EnqueueDecision(Decide("impossible", new ManagerAction("fail", Reason: "no budget for this")));
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);

        var g1 = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        var done = await Poll(c, g1!.Id, g => g.Status == GoalState.Done);
        Assert.Equal("trivial, done", done.LastDecision!.Summary);

        var g2 = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        await Poll(c, g2!.Id, g => g.Status == GoalState.Failed);
    }

    [Fact]
    public async Task An_unknown_assignee_is_skipped_and_recorded_on_the_goal()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia");
        factory.Provider.EnqueueDecision(Decide("delegating",
            new ManagerAction("create_task", Assignee: "nobody", Title: "x", Brief: "y")));
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);

        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        var after = await Poll(c, goal!.Id, g => g.Status == GoalState.Running);

        Assert.Empty(after.TaskIds);
        Assert.Contains(after.PendingNotes, n => n.Contains("nobody"));
    }

    [Fact]
    public async Task A_manager_run_accrues_its_own_cost_to_the_goal()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia");
        factory.Provider.EnqueueDecision(Decide("thinking", new ManagerAction("wait")), costUsd: 0.37m);
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);

        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        var after = await Poll(c, goal!.Id, g => g.SpentUsd > 0m);

        Assert.Equal(0.37m, after.SpentUsd);
    }
}

public class ManagerErrorTests
{
    private static Task<HttpResponseMessage> CreateGoal(HttpClient c)
        => c.PostAsJsonAsync("/goals", new { title = "Ship the parser", brief = "A JSON parser with tests", manager = "mia", budgetUsd = 5.00m });

    [Fact]
    public async Task A_manager_run_refused_by_the_api_is_posted_to_the_room_recorded_on_the_goal_and_not_retried_in_a_loop()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia");
        factory.Provider.EnqueueManagerError("Your organization has disabled Claude subscription access for Claude Code");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);

        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        var end = DateTime.UtcNow.AddSeconds(10);
        GoalModel? after = null;
        while (DateTime.UtcNow < end && (after = await c.GetFromJsonAsync<GoalModel>($"/goals/{goal!.Id}", TestJson.Options))?.LastError is null)
            await Task.Delay(50);

        Assert.NotNull(after?.LastError);
        Assert.Contains("organization has disabled", after!.LastError);
        Assert.Equal(GoalState.Planning, after.Status);
        Assert.Null(after.LastDecision);
        Assert.Contains(factory.ContextApi.Posts, p => p.Room == goal!.Room && p.Content.Contains("Manager run failed") && p.Content.Contains("organization has disabled"));

        await Task.Delay(400);   // PumpGoals must not spin on a goal whose manager just failed
        Assert.Single(factory.Provider.ManagerSpecs);

        var events = await c.GetFromJsonAsync<EventPage>("/events?since=0&limit=200", TestJson.Options);
        Assert.Contains(events!.Events, e => e.Type == "human.needed" && e.EmployeeId == "mia");
    }

    [Fact]
    public async Task A_top_up_retries_a_goal_whose_manager_had_failed()
    {
        using var factory = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia");
        factory.Provider.EnqueueManagerError("api down");
        factory.Provider.EnqueueDecision(new ManagerDecision("back", new[] { new ManagerAction("wait") }));
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);
        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        var end = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < end && (await c.GetFromJsonAsync<GoalModel>($"/goals/{goal!.Id}", TestJson.Options))?.LastError is null) await Task.Delay(50);

        await c.PostAsJsonAsync($"/goals/{goal!.Id}/topup", new { addUsd = 1m });
        end = DateTime.UtcNow.AddSeconds(10);
        GoalModel? after = null;
        while (DateTime.UtcNow < end && (after = await c.GetFromJsonAsync<GoalModel>($"/goals/{goal.Id}", TestJson.Options))?.LastDecision is null) await Task.Delay(50);

        Assert.NotNull(after?.LastDecision);
        Assert.Null(after!.LastError);
        Assert.Equal(2, factory.Provider.ManagerSpecs.Count);
    }
}
