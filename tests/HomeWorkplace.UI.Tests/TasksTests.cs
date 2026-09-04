using Bunit;
using HomeWorkplace.Client;
using HomeWorkplace.UI;
using HomeWorkplace.Live;
using HomeWorkplace.UI.Screens;
using Microsoft.Extensions.DependencyInjection;

namespace HomeWorkplace.UI.Tests;

public class TasksTests : TestContext
{
    private (FakeForemanApi Api, FakeContextApi Ctx, AppStore Store, ShellState Shell) Wire()
    {
        var api = new FakeForemanApi();
        var ctx = new FakeContextApi();
        var store = new AppStore();
        var shell = new ShellState();
        store.SetEmployees(new[] { FakeForemanApi.Employee("ada"), FakeForemanApi.Employee("rex") });
        store.SetTask(FakeForemanApi.Task("t1", TaskState.NeedsHuman) with { AwaitingApproval = true });
        store.SetTask(FakeForemanApi.Task("t2", TaskState.NeedsHuman) with { PendingQuestion = "which format?" });
        store.SetTask(FakeForemanApi.Task("t3", TaskState.Failed));
        store.SetTask(FakeForemanApi.Task("t4", TaskState.Done));
        foreach (var t in store.Tasks.Values) api.Tasks[t.Id] = t;
        Services.AddSingleton(store); Services.AddSingleton(shell);
        Services.AddSingleton<IForemanApi>(api); Services.AddSingleton<IContextApi>(ctx);
        return (api, ctx, store, shell);
    }

    [Fact]
    public void The_list_shows_tasks_and_filters_by_status()
    {
        Wire();
        var cut = RenderComponent<Tasks>();
        Assert.Equal(4, cut.FindAll(".task-row").Count);

        cut.Find("select.status-filter").Change("NeedsHuman");
        Assert.Equal(2, cut.FindAll(".task-row").Count);
    }

    [Fact]
    public void Detail_actions_depend_on_status()
    {
        Wire();
        var cut = RenderComponent<Tasks>();

        cut.Find(".task-row[data-id=t1]").Click();    // needs-human, awaiting approval
        Assert.NotNull(cut.Find(".detail button.approve"));
        Assert.Empty(cut.FindAll(".detail button.answer"));

        cut.Find(".task-row[data-id=t2]").Click();    // needs-human with a question
        Assert.NotNull(cut.Find(".detail textarea.answer"));
        Assert.NotNull(cut.Find(".detail button.answer"));
        Assert.Empty(cut.FindAll(".detail button.approve"));

        cut.Find(".task-row[data-id=t3]").Click();    // failed
        Assert.NotNull(cut.Find(".detail button.retry"));
        Assert.NotNull(cut.Find(".detail button.reassign"));

        cut.Find(".task-row[data-id=t4]").Click();    // done: nothing to do
        Assert.Empty(cut.FindAll(".detail .actions button"));
    }

    [Fact]
    public void Actions_reach_the_api()
    {
        var (api, _, _, _) = Wire();
        var cut = RenderComponent<Tasks>();

        cut.Find(".task-row[data-id=t1]").Click();
        cut.Find(".detail button.approve").Click();
        cut.WaitForAssertion(() => Assert.Contains("approve:t1", api.Calls));
        cut.Find(".detail select.reassign-to").Change("rex");
        cut.Find(".detail button.reassign").Click();
        cut.WaitForAssertion(() => Assert.Contains("reassign:t1:rex", api.Calls));
        cut.Find(".detail button.cancel").Click();
        cut.WaitForAssertion(() => Assert.Contains("cancelTask:t1", api.Calls));

        cut.Find(".task-row[data-id=t2]").Click();
        cut.Find(".detail textarea.answer").Change("use JSON");
        cut.Find(".detail button.answer").Click();
        cut.WaitForAssertion(() => Assert.Contains("answer:t2:use JSON", api.Calls));

        cut.Find(".task-row[data-id=t3]").Click();
        cut.Find(".detail button.retry").Click();
        cut.WaitForAssertion(() => Assert.Contains("retry:t3", api.Calls));
    }

    [Fact]
    public void The_create_form_posts_a_task()
    {
        var (api, _, _, _) = Wire();
        var cut = RenderComponent<Tasks>();

        cut.Find("button.new-task").Click();
        cut.Find("form.create-task input.title").Change("Build it");
        cut.Find("form.create-task textarea.brief").Change("Do the thing");
        cut.Find("form.create-task select.assignee").Change("ada");
        cut.Find("form.create-task button.create").Click();

        cut.WaitForAssertion(() => Assert.Contains("createTask:Build it", api.Calls));
    }

    [Fact]
    public void Selecting_a_task_loads_its_room_brief()
    {
        var (_, ctx, _, _) = Wire();
        ctx.Briefs["task-t1"] = "# Agency room: task-t1\n[1] Ada: starting";
        var cut = RenderComponent<Tasks>();

        cut.Find(".task-row[data-id=t1]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Ada: starting", cut.Find(".detail .brief-room").TextContent));
        Assert.Contains("brief:task-t1", ctx.Calls);
    }
}
