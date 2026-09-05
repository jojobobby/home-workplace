using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class GoalTests
{
    internal static void WriteEmployee(string dp, string id, string role = "Manager", string vendor = "claude")
    {
        var dir = Path.Combine(dp, "employees", id); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "employee.json"),
          $$$"""{"id":"{{{id}}}","name":"{{{id}}}","role":"{{{role}}}","vendor":"{{{vendor}}}","model":"m","claudeAllowedTools":["Read"],"codexSandbox":"read-only","schedule":{"wake":"09:00","sleep":"20:00"}}""");
        File.WriteAllText(Path.Combine(dir, "skills.md"), "s"); File.WriteAllText(Path.Combine(dir, "life.md"), "l");
    }

    private static Task<HttpResponseMessage> CreateGoal(HttpClient c, string manager = "mia", decimal budget = 5.00m)
        => c.PostAsJsonAsync("/goals", new { title = "Ship the parser", brief = "A JSON parser with tests", manager, budgetUsd = budget });

    [Fact]
    public async Task Creating_a_goal_returns_planning_with_a_room_and_announces_it()
    {
        using var factory = ForemanFactory.Create(out var dp); WriteEmployee(dp, "mia");
        using var c = factory.CreateClient();

        var resp = await CreateGoal(c);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var goal = await resp.Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);
        Assert.Equal(GoalState.Planning, goal!.Status);
        Assert.Equal("mia", goal.Manager);
        Assert.Equal(5.00m, goal.BudgetUsd);
        Assert.Equal(0m, goal.SpentUsd);
        Assert.Equal($"goal-{goal.Id}", goal.Room);
        Assert.Contains(factory.ContextApi.Posts, p => p.Room == goal.Room && p.Content.Contains("Ship the parser"));
    }

    [Fact]
    public async Task Creating_a_goal_for_an_unknown_manager_is_400()
    {
        using var factory = ForemanFactory.Create(out _);
        using var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateGoal(c, manager: "ghost")).StatusCode);
    }

    [Fact]
    public async Task A_non_positive_budget_is_400()
    {
        using var factory = ForemanFactory.Create(out var dp); WriteEmployee(dp, "mia");
        using var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateGoal(c, budget: 0m)).StatusCode);
    }

    [Fact]
    public async Task Get_and_list_return_created_goals()
    {
        using var factory = ForemanFactory.Create(out var dp); WriteEmployee(dp, "mia");
        using var c = factory.CreateClient();
        var goal = await (await CreateGoal(c)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);

        var fetched = await c.GetFromJsonAsync<GoalModel>($"/goals/{goal!.Id}", TestJson.Options);
        var listed = await c.GetFromJsonAsync<List<GoalModel>>("/goals", TestJson.Options);

        Assert.Equal(goal.Id, fetched!.Id);
        Assert.Single(listed!);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/goals/nope")).StatusCode);
    }

    [Fact]
    public async Task Goals_survive_a_restart()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "foreman-goal-restart", Guid.NewGuid().ToString("N"));
        var employeesPath = Path.Combine(dataPath, "employees");
        Directory.CreateDirectory(employeesPath);
        WriteEmployee(dataPath, "mia");
        string id;
        await using (var f1 = ForemanFactory.Existing(dataPath, employeesPath))
        {
            using var c1 = f1.CreateClient();
            id = (await (await CreateGoal(c1, budget: 7.50m)).Content.ReadFromJsonAsync<GoalModel>(TestJson.Options))!.Id;
        }

        await using var f2 = ForemanFactory.Existing(dataPath, employeesPath);
        using var c2 = f2.CreateClient();
        var recovered = await c2.GetFromJsonAsync<GoalModel>($"/goals/{id}", TestJson.Options);

        Assert.Equal(7.50m, recovered!.BudgetUsd);
        Assert.Equal(GoalState.Planning, recovered.Status);
        try { Directory.Delete(dataPath, true); } catch { }
    }
}

public class GoalManagerAsleepTests
{
    [Fact]
    public async Task Creating_a_goal_while_its_manager_sleeps_says_so_in_the_room_and_starts_no_run()
    {
        using var factory = ForemanFactory.Create(out var dp); GoalTests.WriteEmployee(dp, "mia");
        using var c = factory.CreateClient();   // nobody woke mia

        var resp = await c.PostAsJsonAsync("/goals", new { title = "Ship the parser", brief = "A JSON parser with tests", manager = "mia", budgetUsd = 5.00m });
        var goal = await resp.Content.ReadFromJsonAsync<GoalModel>(TestJson.Options);

        Assert.Contains(factory.ContextApi.Posts, p => p.Room == goal!.Room && p.Content.Contains("asleep", StringComparison.OrdinalIgnoreCase) && p.Content.Contains("mia"));
        Assert.Empty(factory.Provider.ManagerSpecs);
    }
}
