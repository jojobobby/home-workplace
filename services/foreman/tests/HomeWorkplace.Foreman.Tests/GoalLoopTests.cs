using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class GoalLoopTests
{
    private static async Task<GoalModel> PollGoal(HttpClient c, string id, Func<GoalModel, bool> p, int s = 10)
    { var end = DateTime.UtcNow.AddSeconds(s); while (DateTime.UtcNow < end) { var g = await c.GetFromJsonAsync<GoalModel>($"/goals/{id}", TestJson.Options); if (g is not null && p(g)) return g; await Task.Delay(50); } throw new Xunit.Sdk.XunitException("goal did not reach the expected state"); }

    private static async Task Eventually(Func<bool> probe, int s = 10)
    { var end = DateTime.UtcNow.AddSeconds(s); while (DateTime.UtcNow < end) { if (probe()) return; await Task.Delay(50); } throw new Xunit.Sdk.XunitException("condition not met in time"); }

    private static Task<HttpResponseMessage> CreateGoal(HttpClient c, decimal budget = 5.00m)
        => c.PostAsJsonAsync("/goals", new { title = "Ship the parser", brief = "A JSON parser with tests", manager = "mia", budgetUsd = budget });

    private static ManagerDecision Decide(string summary, params ManagerAction[] actions) => new(summary, actions);
    private static ManagerAction AssignAda(string title = "Write parser") => new("create_task", Assignee: "ada", Title: title, Brief: "do it");

    private static RunResult Worker(RunSpec s, RunOutcome status, string summary, decimal? cost = null) => new()
    {
        RunId = s.RunId, Status = status, Summary = summary, Ask = null, Artifacts = Array.Empty<string>(),
        SessionId = s.SessionId ?? Guid.NewGuid().ToString(), Usage = new Usage(1, null, null, cost, null), RawTail = "",
    };

    private static async Task<(ForemanFactory f, HttpClient c)> Team(bool wakeAda = true)
    {
        var f = ForemanFactory.Create(out var dp);
        GoalTests.WriteEmployee(dp, "mia"); GoalTests.WriteEmployee(dp, "ada", role: "Engineer");
        var c = f.CreateClient();
        await c.PostAsync("/employees/mia/wake", null);
        if (wakeAda) await c.PostAsync("/employees/ada/wake", null);
        return (f, c);
    }

    [Fact]
    public async Task A_worker_finishing_reruns_the_manager_with_the_result_in_its_prompt()
    {
        var (f, c) = await Team(); using var _f = f; using var _c = c;
        f.Provider.EnqueueDecision(Decide("split", AssignAda()));
        f.Provider.Enqueue(s => Worker(s, RunOutcome.Done, "parser written"));
        f.Provider.EnqueueDecision(Decide("all good", new ManagerAction("complete")));

        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        await PollGoal(c, goal!.Id, g => g.Status == GoalState.Done);

        Assert.Equal(2, f.Provider.ManagerSpecs.Count);
        Assert.Contains("parser written", f.Provider.ManagerSpecs[1].Prompt);
        Assert.Contains("done", f.Provider.ManagerSpecs[1].Prompt);
    }

    [Fact]
    public async Task Worker_run_cost_accrues_to_the_goal()
    {
        var (f, c) = await Team(); using var _f = f; using var _c = c;
        f.Provider.EnqueueDecision(Decide("split", AssignAda()));
        f.Provider.Enqueue(s => Worker(s, RunOutcome.Done, "ok", cost: 0.50m));
        f.Provider.EnqueueDecision(Decide("wait", new ManagerAction("wait")));

        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        var after = await PollGoal(c, goal!.Id, g => g.SpentUsd >= 0.50m);

        Assert.Equal(0.50m, after.SpentUsd);
    }

    [Fact]
    public async Task Exceeding_the_budget_blocks_the_goal_and_a_topup_resumes_it()
    {
        var (f, c) = await Team(); using var _f = f; using var _c = c;
        f.Provider.EnqueueDecision(Decide("split", AssignAda()), costUsd: 0.60m);
        f.Provider.Enqueue(s => Worker(s, RunOutcome.Done, "ok", cost: 0.50m));   // 1.10 spent vs 1.00 budget

        var goal = await (await CreateGoal(c, budget: 1.00m)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        var blocked = await PollGoal(c, goal!.Id, g => g.Status == GoalState.Blocked);

        Assert.Equal(1.10m, blocked.SpentUsd);
        Assert.Single(f.Provider.ManagerSpecs);   // no second manager run was spawned
        var events = await c.GetFromJsonAsync<EventPage>("/events?since=0&limit=500", TestJson.Options);
        Assert.Contains(events!.Events, e => e.Type == "goal.blocked");

        f.Provider.EnqueueDecision(Decide("finishing", new ManagerAction("complete")));
        var topup = await c.PostAsJsonAsync($"/goals/{goal.Id}/topup", new { addUsd = 5.00m });
        Assert.Equal(HttpStatusCode.OK, topup.StatusCode);

        var done = await PollGoal(c, goal.Id, g => g.Status == GoalState.Done);
        Assert.Equal(6.00m, done.BudgetUsd);
        Assert.Equal(2, f.Provider.ManagerSpecs.Count);
    }

    [Fact]
    public async Task A_worker_failure_reruns_the_manager_so_it_can_replan()
    {
        var (f, c) = await Team(); using var _f = f; using var _c = c;
        f.Provider.EnqueueDecision(Decide("split", AssignAda()));
        f.Provider.Enqueue(s => Worker(s, RunOutcome.Failed, "boom"));
        f.Provider.EnqueueDecision(Decide("giving up", new ManagerAction("fail", Reason: "worker could not do it")));

        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        await PollGoal(c, goal!.Id, g => g.Status == GoalState.Failed);

        Assert.Equal(2, f.Provider.ManagerSpecs.Count);
        Assert.Contains("failed", f.Provider.ManagerSpecs[1].Prompt);
        Assert.Contains("boom", f.Provider.ManagerSpecs[1].Prompt);
    }

    [Fact]
    public async Task A_skipped_assignee_is_named_in_the_next_manager_prompt()
    {
        var (f, c) = await Team(); using var _f = f; using var _c = c;
        f.Provider.EnqueueDecision(Decide("split",
            new ManagerAction("create_task", Assignee: "nobody", Title: "x", Brief: "y"), AssignAda("real work")));
        f.Provider.Enqueue(s => Worker(s, RunOutcome.Done, "ok"));
        f.Provider.EnqueueDecision(Decide("noted", new ManagerAction("wait")));

        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        await Eventually(() => f.Provider.ManagerSpecs.Count == 2);

        Assert.Contains("nobody", f.Provider.ManagerSpecs[1].Prompt);
    }

    [Fact]
    public async Task Cancelling_a_goal_cancels_its_open_tasks()
    {
        var (f, c) = await Team(wakeAda: false);  // ada asleep, so her task stays queued
        using var _f = f; using var _c = c;
        f.Provider.EnqueueDecision(Decide("split", AssignAda()));

        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        var running = await PollGoal(c, goal!.Id, g => g.TaskIds.Count == 1);

        var resp = await c.PostAsync($"/goals/{goal.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Assert.Equal(GoalState.Cancelled, (await c.GetFromJsonAsync<GoalModel>($"/goals/{goal.Id}", TestJson.Options))!.Status);
        var child = await c.GetFromJsonAsync<TaskModel>($"/tasks/{running.TaskIds[0]}", TestJson.Options);
        Assert.Equal(TaskState.Cancelled, child!.Status);
    }
}
