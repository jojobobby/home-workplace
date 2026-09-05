using System.Text.Json;
using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Sim;

/// <summary>
/// Turns what the store knows into simulation commands: employees appearing, changing
/// status, leaving, and the events that deserve a moment. Stateful — it remembers the last
/// employee snapshot and the last event seq, so each change is issued exactly once.
/// </summary>
public sealed class ForemanFeed
{
    private Dictionary<string, EmployeeDto> _last = new(StringComparer.Ordinal);
    private long _lastSeq;

    public IReadOnlyList<SimCommand> Next(
        IReadOnlyDictionary<string, EmployeeDto> employees,
        IReadOnlyDictionary<string, TaskDto> tasks,
        IReadOnlyList<EventDto> events)
    {
        var commands = new List<SimCommand>();

        foreach (var e in employees.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var title = TaskTitle(e, tasks);
            if (!_last.TryGetValue(e.Id, out var prev))
                commands.Add(new EmployeeAppeared(e.Id, e.Name, e.Status, title));
            else if (prev.Status != e.Status || prev.CurrentTaskId != e.CurrentTaskId)
                commands.Add(new EmployeeStatusChanged(e.Id, e.Status, title, WaitingOn(e, tasks)));
        }

        foreach (var gone in _last.Keys.Where(id => !employees.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal))
            commands.Add(new EmployeeLeft(gone));

        foreach (var ev in events.Where(ev => ev.Seq > _lastSeq).OrderBy(ev => ev.Seq))
        {
            _lastSeq = ev.Seq;
            if (ev.EmployeeId is not { } who || !employees.ContainsKey(who)) continue;
            if (DataBool(ev, "manager")) continue;   // goal-level runs are not desk moments

            switch (ev.Type)
            {
                case "handoff.requested" when DataString(ev, "to") is { } to:
                    commands.Add(new HandoffRequested(who, to));
                    break;
                case "handoff.answered":
                    commands.Add(new HandoffAnswered(who));
                    break;
                case "human.needed":
                    commands.Add(new HumanNeeded(who));
                    break;
                case "run.finished":
                    var status = DataString(ev, "status");
                    if (status == "Done") commands.Add(new RunFinished(who, true));
                    else if (status == "Failed") commands.Add(new RunFinished(who, false));
                    break;
                case "wrapup.written":
                    commands.Add(new WrapUpWritten(who));
                    break;
                case "task.claimed":
                    commands.Add(new TicketClaimed(who));
                    break;
            }
        }

        var open = tasks.Values.Count(t => t.Status == TaskState.Queued && t.Assignee.Length == 0);
        if (open != _lastTickets)
        {
            commands.Add(new TicketsChanged(open));
            _lastTickets = open;
        }

        _last = new Dictionary<string, EmployeeDto>(employees, StringComparer.Ordinal);
        return commands;
    }

    private int _lastTickets;   // the simulation starts at zero, so a first empty board is not news

    private static string? TaskTitle(EmployeeDto e, IReadOnlyDictionary<string, TaskDto> tasks)
        => e.CurrentTaskId is { } id && tasks.TryGetValue(id, out var t) ? t.Title : null;

    /// <summary>For a Waiting employee: the assignee of the child task it is waiting on.</summary>
    private static string? WaitingOn(EmployeeDto e, IReadOnlyDictionary<string, TaskDto> tasks)
    {
        if (e.Status != EmployeeStatus.Waiting || e.CurrentTaskId is not { } id || !tasks.TryGetValue(id, out var parent)) return null;
        var child = parent.ChildIds.LastOrDefault();
        return child is not null && tasks.TryGetValue(child, out var c) ? c.Assignee : null;
    }

    private static string? DataString(EventDto e, string name)
        => e.Data is { } d && d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool DataBool(EventDto e, string name)
        => e.Data is { } d && d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
