using Bunit;
using HomeWorkplace.Client;
using HomeWorkplace.UI;
using HomeWorkplace.Live;
using HomeWorkplace.UI.Screens;
using Microsoft.Extensions.DependencyInjection;

namespace HomeWorkplace.UI.Tests;

public class GoalsTests : TestContext
{
    private (FakeForemanApi Api, AppStore Store) Wire()
    {
        var api = new FakeForemanApi();
        var store = new AppStore();
        store.SetEmployees(new[] { FakeForemanApi.Employee("mia"), FakeForemanApi.Employee("ada") });
        store.SetGoal(FakeForemanApi.Goal("g1", GoalState.Running) with
        {
            SpentUsd = 2.5m, BudgetUsd = 5m,
            LastDecision = new DecisionDto(DateTimeOffset.UtcNow, "split it in two"),
            TaskIds = new[] { "t1", "t2" },
        });
        store.SetGoal(FakeForemanApi.Goal("g2", GoalState.Blocked) with { SpentUsd = 5m, BudgetUsd = 5m });
        store.SetGoal(FakeForemanApi.Goal("g3", GoalState.Done));
        store.SetTask(FakeForemanApi.Task("t1") with { GoalId = "g1" });
        store.SetTask(FakeForemanApi.Task("t2", TaskState.Done) with { GoalId = "g1" });
        foreach (var g in store.Goals.Values) api.Goals[g.Id] = g;
        Services.AddSingleton(store); Services.AddSingleton(new ShellState());
        Services.AddSingleton<IForemanApi>(api);
        return (api, store);
    }

    [Fact]
    public void Detail_shows_the_budget_bar_last_decision_and_child_tasks()
    {
        Wire();
        var cut = RenderComponent<Goals>();
        Assert.Equal(3, cut.FindAll(".goal-row").Count);

        cut.Find(".goal-row[data-id=g1]").Click();

        Assert.Contains("width:50%", cut.Find(".detail .fill").GetAttribute("style")!.Replace(" ", ""));
        Assert.Contains("split it in two", cut.Find(".detail .decision").TextContent);
        Assert.Equal(2, cut.FindAll(".detail .child-task").Count);
    }

    [Fact]
    public void Actions_depend_on_status()
    {
        Wire();
        var cut = RenderComponent<Goals>();

        cut.Find(".goal-row[data-id=g2]").Click();    // blocked → top-up, cancel
        Assert.NotNull(cut.Find(".detail button.topup"));
        Assert.NotNull(cut.Find(".detail button.cancel-goal"));

        cut.Find(".goal-row[data-id=g3]").Click();    // done → nothing
        Assert.Empty(cut.FindAll(".detail .actions button"));
    }

    [Fact]
    public void Actions_reach_the_api()
    {
        var (api, _) = Wire();
        var cut = RenderComponent<Goals>();

        cut.Find(".goal-row[data-id=g2]").Click();
        cut.Find(".detail input.topup-amount").Change("2.5");
        cut.Find(".detail button.topup").Click();
        cut.WaitForAssertion(() => Assert.Contains("topup:g2:2.5", api.Calls));

        cut.Find(".detail button.cancel-goal").Click();
        cut.WaitForAssertion(() => Assert.Contains("cancelGoal:g2", api.Calls));
    }

    [Fact]
    public void The_create_form_posts_a_goal()
    {
        var (api, _) = Wire();
        var cut = RenderComponent<Goals>();

        cut.Find("button.new-goal").Click();
        cut.Find("form.create-goal input.title").Change("Ship it");
        cut.Find("form.create-goal textarea.brief").Change("All of it");
        cut.Find("form.create-goal select.manager").Change("mia");
        cut.Find("form.create-goal input.budget").Change("5");
        cut.Find("form.create-goal button.create").Click();

        cut.WaitForAssertion(() => Assert.Contains("createGoal:Ship it", api.Calls));
    }
}
