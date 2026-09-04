using Bunit;
using HomeWorkplace.Client;
using HomeWorkplace.UI;
using Microsoft.Extensions.DependencyInjection;

namespace HomeWorkplace.UI.Tests;

public class ShellTests : TestContext
{
    private (FakeForemanApi Api, AppStore Store, ShellState Shell) Wire()
    {
        var api = new FakeForemanApi();
        var store = new AppStore();
        var shell = new ShellState();
        Services.AddSingleton(store);
        Services.AddSingleton(shell);
        Services.AddSingleton<IForemanApi>(api);
        Services.AddSingleton<IContextApi>(new FakeContextApi());   // the Tasks screen needs it once the badge jumps there
        Services.AddSingleton(new CliSetupChecker(new FakeProcessRunner()));
        Services.AddSingleton<ITerminalLauncher>(new FakeTerminalLauncher());
        return (api, store, shell);
    }

    [Fact]
    public void Nav_has_six_links_and_office_is_the_default_screen()
    {
        Wire();
        var cut = RenderComponent<App>();

        Assert.Equal(6, cut.FindAll("nav .nav-link").Count);
        Assert.NotNull(cut.Find("#office"));
    }

    [Fact]
    public void The_badge_counts_things_that_need_a_human_and_hides_at_zero()
    {
        var (_, store, _) = Wire();
        var cut = RenderComponent<App>();
        Assert.Empty(cut.FindAll("nav .badge"));

        store.SetTask(FakeForemanApi.Task("t1", TaskState.NeedsHuman));
        store.SetGoal(FakeForemanApi.Goal("g1", GoalState.Blocked));

        cut.WaitForAssertion(() => Assert.Equal("2", cut.Find("nav .badge").TextContent.Trim()));
    }

    [Fact]
    public void Clicking_the_badge_jumps_to_tasks_filtered_to_what_needs_you()
    {
        var (_, store, shell) = Wire();
        store.SetTask(FakeForemanApi.Task("t1", TaskState.NeedsHuman));
        var cut = RenderComponent<App>();

        cut.Find("nav .badge").Click();

        Assert.Equal(Screen.Tasks, shell.Current);
        Assert.Equal(TaskState.NeedsHuman, shell.TaskFilter);
    }

    [Fact]
    public void Office_renders_one_desk_per_employee_with_its_status_class()
    {
        var (_, store, _) = Wire();
        store.SetEmployees(new[] { FakeForemanApi.Employee("ada", EmployeeStatus.Awake), FakeForemanApi.Employee("rex", EmployeeStatus.Asleep) });
        var cut = RenderComponent<App>();

        Assert.Equal(2, cut.FindAll(".desk").Count);
        Assert.Contains("status-awake", cut.Find(".desk[data-id=ada]").ClassList);
        Assert.Contains("status-asleep", cut.Find(".desk[data-id=rex]").ClassList);
    }

    [Fact]
    public void Clicking_a_nav_link_switches_the_screen()
    {
        Wire();
        var cut = RenderComponent<App>();

        cut.Find("nav .nav-link[data-screen=setup]").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".setup")));
        Assert.Empty(cut.FindAll("#office"));
    }

    [Fact]
    public void A_toast_is_shown_and_can_be_dismissed()
    {
        var (_, store, _) = Wire();
        var cut = RenderComponent<App>();

        store.Notify("Ada needs you", ToastKind.Warning);
        cut.WaitForAssertion(() => Assert.Contains("Ada needs you", cut.Find(".toast").TextContent));

        cut.Find(".toast .dismiss").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".toast")));
    }
}
