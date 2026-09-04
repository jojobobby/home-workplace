using System.Text.Json;
using HomeWorkplace.Client;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Tests;

public class FeedTests
{
    private static EmployeeDto Emp(string id, EmployeeStatus s, string? task = null)
        => new() { Id = id, Name = id.ToUpperInvariant(), Role = "r", Vendor = Vendor.Claude, Model = "m", Status = s, CurrentTaskId = task, Energy = 100 };

    private static TaskDto Task(string id, string assignee, string title = "T", string? parent = null, params string[] children)
        => new() { Id = id, Title = title, Brief = "b", Assignee = assignee, Room = "task-" + id, ParentId = parent, ChildIds = children };

    private static EventDto Ev(long seq, string type, string? employeeId = null, string? taskId = null, object? data = null)
        => new() { Seq = seq, Type = type, EmployeeId = employeeId, TaskId = taskId, Timestamp = DateTimeOffset.UtcNow,
                   Data = data is null ? null : JsonSerializer.SerializeToElement(data) };

    private static Dictionary<string, T> D<T>(params (string, T)[] items) => items.ToDictionary(i => i.Item1, i => i.Item2);

    [Fact]
    public void New_employees_appear_with_their_status_and_task_title()
    {
        var feed = new ForemanFeed();
        var cmds = feed.Next(D(("ada", Emp("ada", EmployeeStatus.Working, "t1"))), D(("t1", Task("t1", "ada", "Build it"))), Array.Empty<EventDto>());

        var appeared = Assert.IsType<EmployeeAppeared>(Assert.Single(cmds));
        Assert.Equal("ada", appeared.Id);
        Assert.Equal("ADA", appeared.Name);
        Assert.Equal(EmployeeStatus.Working, appeared.Status);
        Assert.Equal("Build it", appeared.TaskTitle);
    }

    [Fact]
    public void Status_changes_are_diffed_and_waiting_carries_the_teammate_from_the_child_task()
    {
        var feed = new ForemanFeed();
        feed.Next(D(("ada", Emp("ada", EmployeeStatus.Awake))), new Dictionary<string, TaskDto>(), Array.Empty<EventDto>());

        var tasks = D(("p", Task("p", "ada", "Parent", children: "c")), ("c", Task("c", "rex", "Q", parent: "p")));
        var cmds = feed.Next(D(("ada", Emp("ada", EmployeeStatus.Waiting, "p"))), tasks, Array.Empty<EventDto>());

        var changed = Assert.IsType<EmployeeStatusChanged>(Assert.Single(cmds));
        Assert.Equal(EmployeeStatus.Waiting, changed.Status);
        Assert.Equal("Parent", changed.TaskTitle);
        Assert.Equal("rex", changed.WaitingOn);
    }

    [Fact]
    public void Unchanged_employees_produce_nothing_and_removed_ones_leave()
    {
        var feed = new ForemanFeed();
        var emps = D(("ada", Emp("ada", EmployeeStatus.Awake)), ("rex", Emp("rex", EmployeeStatus.Asleep)));
        feed.Next(emps, new Dictionary<string, TaskDto>(), Array.Empty<EventDto>());

        Assert.Empty(feed.Next(emps, new Dictionary<string, TaskDto>(), Array.Empty<EventDto>()));

        var left = feed.Next(D(("ada", Emp("ada", EmployeeStatus.Awake))), new Dictionary<string, TaskDto>(), Array.Empty<EventDto>());
        Assert.Equal("rex", Assert.IsType<EmployeeLeft>(Assert.Single(left)).Id);
    }

    [Fact]
    public void Events_map_to_commands_exactly_once_by_seq()
    {
        var feed = new ForemanFeed();
        var emps = D(("ada", Emp("ada", EmployeeStatus.Working)), ("rex", Emp("rex", EmployeeStatus.Awake)));
        feed.Next(emps, new Dictionary<string, TaskDto>(), Array.Empty<EventDto>());

        var events = new[]
        {
            Ev(1, "handoff.requested", employeeId: "ada", taskId: "p", data: new { to = "rex", childId = "c" }),
            Ev(2, "handoff.answered", employeeId: "ada", taskId: "p"),
            Ev(3, "human.needed", employeeId: "rex", taskId: "t9"),
            Ev(4, "run.finished", employeeId: "rex", taskId: "t9", data: new { status = "Done", summary = "ok" }),
            Ev(5, "run.finished", employeeId: "ada", taskId: "t8", data: new { status = "Failed", summary = "boom" }),
            Ev(6, "run.finished", employeeId: "mia", taskId: "g1", data: new { status = "Done", manager = true }),   // manager run: no moment
            Ev(7, "wrapup.written", employeeId: "ada", taskId: "t8"),
            Ev(8, "task.state", employeeId: null, taskId: "t8"),                                                    // not an agent moment
        };
        var cmds = feed.Next(emps, new Dictionary<string, TaskDto>(), events);

        Assert.Collection(cmds,
            c => { var h = Assert.IsType<HandoffRequested>(c); Assert.Equal("ada", h.FromId); Assert.Equal("rex", h.ToId); },
            c => Assert.Equal("ada", Assert.IsType<HandoffAnswered>(c).Id),
            c => Assert.Equal("rex", Assert.IsType<HumanNeeded>(c).Id),
            c => { var r = Assert.IsType<RunFinished>(c); Assert.Equal("rex", r.Id); Assert.True(r.Succeeded); },
            c => { var r = Assert.IsType<RunFinished>(c); Assert.Equal("ada", r.Id); Assert.False(r.Succeeded); },
            c => Assert.Equal("ada", Assert.IsType<WrapUpWritten>(c).Id));

        Assert.Empty(feed.Next(emps, new Dictionary<string, TaskDto>(), events));   // same events again: already consumed
    }

    [Fact]
    public void Events_for_unknown_employees_are_ignored()
    {
        var feed = new ForemanFeed();
        var emps = D(("ada", Emp("ada", EmployeeStatus.Awake)));
        feed.Next(emps, new Dictionary<string, TaskDto>(), Array.Empty<EventDto>());

        var cmds = feed.Next(emps, new Dictionary<string, TaskDto>(), new[] { Ev(1, "human.needed", employeeId: "ghost") });

        Assert.Empty(cmds);
    }
}
