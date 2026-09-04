using System.Globalization;
using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Ui;

/// <summary>What running an action produced: done, a layer to open, a confirmation to ask, or a failure.</summary>
public abstract record ActionOutcome;
public sealed record Done(string Message) : ActionOutcome;
public sealed record OpenText(TextEntry Entry) : ActionOutcome;
public sealed record OpenDialogue(Dialogue Dialogue) : ActionOutcome;
public sealed record NeedConfirm(Confirm Confirm) : ActionOutcome;
public sealed record Failed(string Message) : ActionOutcome;
/// <summary>Nothing to run: the game handles it (Leave, TalkTo).</summary>
public sealed record Nothing : ActionOutcome;

/// <summary>The payload a Confirm carries: the action to run once the user says yes.</summary>
public sealed record ConfirmedAction(UiAction Action);

/// <summary>
/// Runs UI actions against Foreman and the context API: direct calls, text-backed calls
/// (a TextEntry first, then <see cref="SubmitAsync"/>), confirmed calls for destructive
/// ones. One at a time; errors become a Failed outcome, a toast, and a journal line.
/// </summary>
public sealed class Actions
{
    private readonly IForemanApi _foreman;
    private readonly IContextApi _context;
    private readonly Journal _journal;
    private readonly Toasts _toasts;
    private readonly Func<OverlaySnapshot> _snapshot;
    private int _inFlight;

    public Actions(IForemanApi foreman, IContextApi context, Journal journal, Toasts toasts, Func<OverlaySnapshot> snapshot)
    {
        _foreman = foreman;
        _context = context;
        _journal = journal;
        _toasts = toasts;
        _snapshot = snapshot;
    }

    public bool Busy => Volatile.Read(ref _inFlight) != 0;

    public Task<ActionOutcome> RunAsync(UiAction action) => action switch
    {
        Leave or TalkTo => Task.FromResult<ActionOutcome>(new Nothing()),
        GiveTask a => Task.FromResult<ActionOutcome>(new OpenText(new TextEntry($"New task for {NameOf(a.EmployeeId)}",
            new[] { new Field("Title", false, 60), new Field("Brief", true, 600) }, action))),
        Answer a => Task.FromResult<ActionOutcome>(new OpenText(new TextEntry($"Answer for \"{TaskTitle(a.TaskId)}\"",
            new[] { new Field("Answer", true, 600) }, action))),
        SetGoal a => Task.FromResult<ActionOutcome>(new OpenText(new TextEntry($"New goal for {NameOf(a.ManagerId)}",
            new[] { new Field("Title", false, 60), new Field("Brief", true, 600), new Field("Budget USD", false, 10) }, action))),
        TopUp a => Task.FromResult<ActionOutcome>(new OpenText(new TextEntry($"Top up \"{GoalTitle(a.GoalId)}\"",
            new[] { new Field("Amount USD", false, 10) }, action))),
        CancelTask a => Confirm($"Cancel \"{TaskTitle(a.TaskId)}\"? Its runs stop.", action),
        CancelGoal a => Confirm($"Cancel \"{GoalTitle(a.GoalId)}\" and its open tasks?", action),
        Reset a => Confirm($"Reset {NameOf(a.EmployeeId)}? Today's memory is written up and forgotten.", action),
        _ => Guarded(() => Direct(action)),
    };

    /// <summary>Second step of a text-backed action: the entry's payload is the action, its values the fields.</summary>
    public Task<ActionOutcome> SubmitAsync(TextSubmitted submitted) => Guarded(async () =>
    {
        var v = submitted.Values;
        switch (submitted.Payload)
        {
            case GiveTask a:
                var task = await _foreman.CreateTaskAsync(new CreateTaskRequest(v[0].Trim(), v[1].Trim(), a.EmployeeId));
                return Ok($"Task \"{task.Title}\" given to {NameOf(a.EmployeeId)}", ToastKind.Success);
            case Answer a:
                await _foreman.AnswerAsync(a.TaskId, v[0].Trim());
                return Ok($"Answered \"{TaskTitle(a.TaskId)}\"", ToastKind.Success);
            case SetGoal a:
                if (!TryMoney(v[2], out var budget)) return Fail("Budget must be a number of dollars, like 5 or 12.50");
                var goal = await _foreman.CreateGoalAsync(new CreateGoalRequest(v[0].Trim(), v[1].Trim(), a.ManagerId, budget));
                return Ok($"Goal \"{goal.Title}\" set for {NameOf(a.ManagerId)} with ${budget:0.00}", ToastKind.Success);
            case TopUp a:
                if (!TryMoney(v[0], out var amount)) return Fail("Amount must be a number of dollars, like 5 or 12.50");
                await _foreman.TopUpAsync(a.GoalId, amount);
                return Ok($"Topped up \"{GoalTitle(a.GoalId)}\" by ${amount:0.00}", ToastKind.Success);
            default:
                return Fail("nothing to submit");
        }
    });

    /// <summary>Second step of a confirmed action: the Confirm's payload is a <see cref="ConfirmedAction"/>.</summary>
    public Task<ActionOutcome> ConfirmedAsync(object payload)
        => payload is ConfirmedAction c ? Guarded(() => Direct(c.Action)) : Task.FromResult<ActionOutcome>(new Failed("nothing to confirm"));

    private async Task<ActionOutcome> Direct(UiAction action)
    {
        switch (action)
        {
            case Wake a: await _foreman.WakeAsync(a.EmployeeId); return Ok($"Woke {NameOf(a.EmployeeId)}", ToastKind.Info);
            case Sleep a: await _foreman.SleepAsync(a.EmployeeId); return Ok($"Sent {NameOf(a.EmployeeId)} to sleep", ToastKind.Info);
            case Reset a: await _foreman.ResetAsync(a.EmployeeId); return Ok($"Reset {NameOf(a.EmployeeId)}", ToastKind.Info);
            case Approve a: await _foreman.ApproveAsync(a.TaskId); return Ok($"Approved \"{TaskTitle(a.TaskId)}\"", ToastKind.Success);
            case Retry a: await _foreman.RetryAsync(a.TaskId); return Ok($"Retrying \"{TaskTitle(a.TaskId)}\"", ToastKind.Info);
            case Reassign a: await _foreman.ReassignAsync(a.TaskId, a.Assignee); return Ok($"Reassigned \"{TaskTitle(a.TaskId)}\" to {NameOf(a.Assignee)}", ToastKind.Info);
            case CancelTask a: await _foreman.CancelTaskAsync(a.TaskId); return Ok($"Cancelled \"{TaskTitle(a.TaskId)}\"", ToastKind.Info);
            case CancelGoal a: await _foreman.CancelGoalAsync(a.GoalId); return Ok($"Cancelled goal \"{GoalTitle(a.GoalId)}\"", ToastKind.Info);
            case ReloadEmployees: await _foreman.ReloadEmployeesAsync(); return Ok("Employees reloaded from disk", ToastKind.Info);
            case OpenBrief a: return await Brief(a.EmployeeId);
            default: return Fail($"unknown action {action.GetType().Name}");
        }
    }

    private async Task<ActionOutcome> Brief(string employeeId)
    {
        var snap = _snapshot();
        var room = snap.Employees.TryGetValue(employeeId, out var e) && e.CurrentTaskId is { } id && snap.Tasks.TryGetValue(id, out var t) ? t.Room : null;
        if (room is null)
            return new OpenDialogue(new Dialogue(employeeId, NameOf(employeeId), new[] { $"{NameOf(employeeId)} has no task room open right now." }, new[] { new DialogueOption("Leave", new Leave()) }));

        var brief = await _context.GetBriefAsync(room);
        var lines = brief.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).SelectMany(l => TextEntry.Wrap(l, 64)).Take(14).ToList();
        if (lines.Count == 0) lines.Add("(the room is quiet)");
        return new OpenDialogue(new Dialogue(employeeId, $"Room: {room}", lines, new[] { new DialogueOption("Leave", new Leave()) }));
    }

    private async Task<ActionOutcome> Guarded(Func<Task<ActionOutcome>> run)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return new Failed("Still busy with the last action");
        try
        {
            return await run();
        }
        catch (ApiException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return Fail("Foreman is unreachable: " + ex.Message);
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }

    private Task<ActionOutcome> Confirm(string question, UiAction action)
        => Task.FromResult<ActionOutcome>(new NeedConfirm(new Confirm(question, new ConfirmedAction(action))));

    private ActionOutcome Ok(string message, ToastKind kind)
    {
        _journal.Add(message);
        _toasts.Add(message, kind, null);
        return new Done(message);
    }

    private ActionOutcome Fail(string message)
    {
        _journal.Add("Failed: " + message);
        _toasts.Add(message, ToastKind.Error, null);
        return new Failed(message);
    }

    private static bool TryMoney(string text, out decimal value)
        => decimal.TryParse(text.Trim().TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value > 0;

    private string NameOf(string employeeId) => _snapshot().Employees.TryGetValue(employeeId, out var e) ? e.Name : employeeId;
    private string TaskTitle(string taskId) => _snapshot().Tasks.TryGetValue(taskId, out var t) ? t.Title : taskId;
    private string GoalTitle(string goalId) => _snapshot().Goals.TryGetValue(goalId, out var g) ? g.Title : goalId;
}
