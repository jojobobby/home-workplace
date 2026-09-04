using HomeWorkplace.Client;
using HomeWorkplace.UI;

namespace HomeWorkplace.UI.Tests;

public class StoreAndPumpTests
{
    private static (FakeForemanApi Api, AppStore Store, EventPump Pump) Rig()
    {
        var api = new FakeForemanApi();
        api.Employees["ada"] = FakeForemanApi.Employee("ada");
        api.Tasks["t1"] = FakeForemanApi.Task("t1");
        api.Goals["g1"] = FakeForemanApi.Goal("g1");
        var store = new AppStore();
        return (api, store, new EventPump(api, store, backoffBaseMs: 1));
    }

    [Fact]
    public void Changed_fires_when_the_store_is_updated()
    {
        var store = new AppStore();
        var fired = 0;
        store.Changed += () => fired++;
        store.SetTask(FakeForemanApi.Task("t1"));
        Assert.Equal(1, fired);
        Assert.Single(store.Tasks);
    }

    [Fact]
    public void HumanNeeded_counts_needs_human_tasks_and_blocked_goals()
    {
        var store = new AppStore();
        store.SetTask(FakeForemanApi.Task("t1", TaskState.NeedsHuman));
        store.SetTask(FakeForemanApi.Task("t2", TaskState.Running));
        store.SetGoal(FakeForemanApi.Goal("g1", GoalState.Blocked));
        Assert.Equal(2, store.HumanNeeded);
    }

    [Fact]
    public async Task LoadAll_fetches_all_three_collections_into_the_store()
    {
        var (api, store, pump) = Rig();
        await pump.LoadAllAsync(CancellationToken.None);
        Assert.Single(store.Employees); Assert.Single(store.Tasks); Assert.Single(store.Goals);
        Assert.True(store.ServicesUp);
        Assert.Equal(new[] { "employees", "tasks", "goals" }, api.Calls);
    }

    [Fact]
    public async Task An_employee_event_refetches_that_employee_and_a_catalog_reload_refetches_all()
    {
        var (api, store, pump) = Rig();
        api.Pages.Enqueue(_ => new EventPageDto { Cursor = 2, Events = new[] {
            FakeForemanApi.Ev(1, "employee.state", employeeId: "ada"),
            FakeForemanApi.Ev(2, "catalog.reloaded") } });
        await pump.PumpOnceAsync(CancellationToken.None);
        Assert.Contains("employee:ada", api.Calls);
        Assert.Contains("employees", api.Calls);
        Assert.Equal(2, store.RecentEvents.Count);
    }

    [Fact]
    public async Task Task_events_refetch_the_task_and_goal_events_refetch_the_goal()
    {
        var (api, store, pump) = Rig();
        api.Pages.Enqueue(_ => new EventPageDto { Cursor = 3, Events = new[] {
            FakeForemanApi.Ev(1, "task.state", taskId: "t1"),
            FakeForemanApi.Ev(2, "run.finished", employeeId: "ada", taskId: "t1"),
            FakeForemanApi.Ev(3, "goal.state", data: new { goalId = "g1" }) } });
        await pump.PumpOnceAsync(CancellationToken.None);
        Assert.Equal(2, api.Calls.Count(c => c == "task:t1"));
        Assert.Contains("goal:g1", api.Calls);
    }

    [Fact]
    public async Task A_manager_run_event_refetches_the_goal_not_a_task()
    {
        var (api, store, pump) = Rig();
        api.Pages.Enqueue(_ => new EventPageDto { Cursor = 1, Events = new[] {
            FakeForemanApi.Ev(1, "run.started", employeeId: "mia", taskId: "g1", data: new { manager = true }) } });
        await pump.PumpOnceAsync(CancellationToken.None);
        Assert.Contains("goal:g1", api.Calls);
        Assert.DoesNotContain(api.Calls, c => c.StartsWith("task:"));
    }

    [Fact]
    public async Task The_cursor_is_carried_forward_between_polls()
    {
        var (api, store, pump) = Rig();
        api.Pages.Enqueue(_ => new EventPageDto { Cursor = 7, Events = Array.Empty<EventDto>() });
        api.Pages.Enqueue(_ => new EventPageDto { Cursor = 9, Events = Array.Empty<EventDto>() });
        await pump.PumpOnceAsync(CancellationToken.None);
        await pump.PumpOnceAsync(CancellationToken.None);
        Assert.Equal(new long[] { 0, 7 }, api.SinceValues);
        Assert.Equal(9, pump.Cursor);
    }

    [Fact]
    public async Task A_truncated_page_triggers_a_full_refetch()
    {
        var (api, store, pump) = Rig();
        api.Pages.Enqueue(_ => new EventPageDto { Cursor = 50, Truncated = true, Events = Array.Empty<EventDto>() });
        await pump.PumpOnceAsync(CancellationToken.None);
        Assert.Contains("tasks", api.Calls); Assert.Contains("goals", api.Calls); Assert.Contains("employees", api.Calls);
    }

    [Fact]
    public async Task Human_needed_raises_a_toast_and_refetches_the_item()
    {
        var (api, store, pump) = Rig();
        api.Tasks["t1"] = FakeForemanApi.Task("t1", TaskState.NeedsHuman);
        api.Pages.Enqueue(_ => new EventPageDto { Cursor = 1, Events = new[] {
            FakeForemanApi.Ev(1, "human.needed", employeeId: "ada", taskId: "t1") } });
        await pump.PumpOnceAsync(CancellationToken.None);
        Assert.Single(store.Toasts);
        Assert.Equal(ToastKind.Warning, store.Toasts[0].Kind);
        Assert.Equal(1, store.HumanNeeded);
    }

    [Fact]
    public async Task An_unknown_id_in_an_event_does_not_crash_the_pump()
    {
        var (api, store, pump) = Rig();
        api.Pages.Enqueue(_ => new EventPageDto { Cursor = 1, Events = new[] { FakeForemanApi.Ev(1, "task.state", taskId: "ghost") } });
        await pump.PumpOnceAsync(CancellationToken.None);   // GetTask("ghost") throws 404 inside
        Assert.Equal(1, pump.Cursor);
    }

    [Fact]
    public async Task The_run_loop_marks_services_down_on_error_and_up_again_on_recovery()
    {
        var (api, store, pump) = Rig();
        api.ThrowOnEvents = new HttpRequestException("down");
        var seenDown = false;
        store.Changed += () => { if (!store.ServicesUp) seenDown = true; };
        using var cts = new CancellationTokenSource(400);
        try { await pump.RunAsync(cts.Token); } catch (OperationCanceledException) { }
        Assert.True(seenDown);
        Assert.True(store.ServicesUp);
    }
}
