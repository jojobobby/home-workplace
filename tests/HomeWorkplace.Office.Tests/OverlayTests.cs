using HomeWorkplace.Client;
using HomeWorkplace.Office.Ui;

namespace HomeWorkplace.Office.Tests;

public class OverlayTests
{
    private static EmployeeDto Emp(string id, string name, EmployeeStatus status, string? taskId = null, string role = "Engineer")
        => new() { Id = id, Name = name, Role = role, Status = status, CurrentTaskId = taskId };

    private static OverlaySnapshot Snapshot()
    {
        var t1 = new TaskDto { Id = "t1", Title = "Build parser", Assignee = "ada", Status = TaskState.Running, UpdatedAt = DateTimeOffset.Parse("2026-09-04T10:00:00Z") };
        var t2 = new TaskDto { Id = "t2", Title = "Review PR", Assignee = "rex", Status = TaskState.NeedsHuman, AwaitingApproval = true, PendingQuestion = "Merge?", UpdatedAt = DateTimeOffset.Parse("2026-09-04T11:00:00Z") };
        var t3 = new TaskDto { Id = "t3", Title = "Old thing", Assignee = "ada", Status = TaskState.Failed, UpdatedAt = DateTimeOffset.Parse("2026-09-04T09:00:00Z") };
        var g1 = new GoalDto { Id = "g1", Title = "Launch v1", Manager = "mia", Status = GoalState.Running, BudgetUsd = 10, SpentUsd = 2.5m };
        var events = new[]
        {
            new EventDto { Seq = 1, Timestamp = DateTimeOffset.Parse("2026-09-04T10:00:00Z"), Type = "run.started", EmployeeId = "ada" },
            new EventDto { Seq = 2, Timestamp = DateTimeOffset.Parse("2026-09-04T10:05:00Z"), Type = "human.needed", EmployeeId = "rex" },
        };
        var setup = new[] { new CliStatus("claude", CliState.SignedIn, "2.1.0", null), new CliStatus("codex", CliState.NotInstalled, null, "not on PATH") };
        return new OverlaySnapshot(
            new Dictionary<string, EmployeeDto> { ["ada"] = Emp("ada", "Ada", EmployeeStatus.Working, "t1"), ["rex"] = Emp("rex", "Rex", EmployeeStatus.Waiting, "t2"), ["mia"] = Emp("mia", "Mia", EmployeeStatus.Asleep, role: "Engineering manager") },
            new Dictionary<string, TaskDto> { ["t1"] = t1, ["t2"] = t2, ["t3"] = t3 },
            new Dictionary<string, GoalDto> { ["g1"] = g1 },
            events, setup);
    }

    [Fact]
    public void Tab_cycles_the_tabs_and_wraps()
    {
        var o = new Overlay(OverlayTab.Employees, Snapshot());
        o.Handle(UiKey.Tab);
        Assert.Equal(OverlayTab.Tasks, o.Tab);
        o.Handle(UiKey.Left);
        Assert.Equal(OverlayTab.Employees, o.Tab);
        o.Handle(UiKey.Left);
        Assert.Equal(OverlayTab.Setup, o.Tab);
        o.Handle(UiKey.Right);
        Assert.Equal(OverlayTab.Employees, o.Tab);
    }

    [Fact]
    public void Each_tab_lists_its_rows()
    {
        var o = new Overlay(OverlayTab.Employees, Snapshot());
        Assert.Equal(new[] { "ada", "mia", "rex" }, o.Rows.Select(r => r.Id));
        Assert.Contains("Ada", o.Rows[0].Text);
        Assert.Contains("Working", o.Rows[0].Text);
        Assert.Contains("Build parser", o.Rows[0].Text);

        o.Handle(UiKey.Tab);   // Tasks, newest first
        Assert.Equal(new[] { "t2", "t1", "t3" }, o.Rows.Select(r => r.Id));
        Assert.Contains("NeedsHuman", o.Rows[0].Text);
        var board = new Overlay(OverlayTab.Tasks, Snapshot() with { Tasks = new Dictionary<string, TaskDto> { ["t9"] = new TaskDto { Id = "t9", Title = "Pinned", Assignee = "", Status = TaskState.Queued } } });
        Assert.Contains("(board)", board.Rows[0].Text);

        o.Handle(UiKey.Tab);   // Goals
        Assert.Single(o.Rows);
        Assert.Contains("2.50", o.Rows[0].Text);
        Assert.Contains("10.00", o.Rows[0].Text);

        o.Handle(UiKey.Tab);   // Activity, newest first
        Assert.Equal(2, o.Rows.Count);
        Assert.StartsWith(DateTimeOffset.Parse("2026-09-04T10:05:00Z").ToLocalTime().ToString("HH:mm"), o.Rows[0].Text);
        Assert.Contains("human.needed", o.Rows[0].Text);
        Assert.Empty(o.Rows[0].Actions);

        o.Handle(UiKey.Tab);   // Setup
        Assert.Equal(3, o.Rows.Count);
        Assert.Contains("claude", o.Rows[0].Text);
        Assert.Contains("SignedIn", o.Rows[0].Text);
        Assert.Contains("not on PATH", o.Rows[1].Text);
        Assert.Equal("Reload employees", o.Rows[2].Text);
        Assert.IsType<ReloadEmployees>(o.Rows[2].Actions.Single().Action);
    }

    [Fact]
    public void Enter_opens_a_dialogue_with_the_rows_actions()
    {
        var o = new Overlay(OverlayTab.Tasks, Snapshot());
        var result = o.Handle(UiKey.Accept);           // t2 needs a human
        Assert.Equal(LayerResultKind.Push, result.Kind);
        var dialogue = Assert.IsType<Dialogue>(result.Layer);
        Assert.Equal(new[] { "Approve", "Answer", "Cancel task", "Leave" }, dialogue.Options.Select(x => x.Label));
        Assert.Equal(new Approve("t2"), dialogue.Options[0].Action);

        o.Handle(UiKey.Down); o.Handle(UiKey.Down);    // t3 failed
        var failed = Assert.IsType<Dialogue>(o.Handle(UiKey.Accept).Layer);
        Assert.Contains(failed.Options, x => x.Action is Retry { TaskId: "t3" });
        Assert.Contains(failed.Options, x => x.Action is Reassign { TaskId: "t3", Assignee: "rex" });
        Assert.DoesNotContain(failed.Options, x => x.Action is Reassign { Assignee: "ada" });   // not to itself

        o.Handle(UiKey.Tab); o.Handle(UiKey.Tab);      // Activity has no actions
        Assert.Equal(LayerResultKind.None, o.Handle(UiKey.Accept).Kind);
    }

    [Fact]
    public void Employee_rows_offer_talk_task_wake_or_sleep_and_reset()
    {
        var o = new Overlay(OverlayTab.Employees, Snapshot());
        var ada = Assert.IsType<Dialogue>(o.Handle(UiKey.Accept).Layer);
        Assert.Equal(new[] { "Talk to Ada", "Give a task", "Sleep", "Reset", "Let go", "Leave" }, ada.Options.Select(x => x.Label));
        o.Handle(UiKey.Down);
        var mia = Assert.IsType<Dialogue>(o.Handle(UiKey.Accept).Layer);
        Assert.Contains("Wake", mia.Options.Select(x => x.Label));
    }

    [Fact]
    public void Selection_clamps_and_survives_a_refresh_by_id()
    {
        var o = new Overlay(OverlayTab.Employees, Snapshot());
        o.Handle(UiKey.Up);
        Assert.Equal(0, o.Selected);
        o.Handle(UiKey.Down); o.Handle(UiKey.Down); o.Handle(UiKey.Down);
        Assert.Equal(2, o.Selected);
        Assert.Equal("rex", o.SelectedRow!.Id);

        var snap = Snapshot();
        var employees = new Dictionary<string, EmployeeDto>(snap.Employees) { ["zed"] = Emp("zed", "Zed", EmployeeStatus.Awake) };
        o.Refresh(snap with { Employees = employees });
        Assert.Equal("rex", o.SelectedRow!.Id);
        Assert.Equal(4, o.Rows.Count);
    }

    [Fact]
    public void Esc_closes_the_overlay()
        => Assert.Equal(LayerResultKind.Pop, new Overlay(OverlayTab.Goals, Snapshot()).Handle(UiKey.Back).Kind);
}
