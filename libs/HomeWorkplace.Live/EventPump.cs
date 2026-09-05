using System.Text.Json;
using HomeWorkplace.Client;

namespace HomeWorkplace.Live;

/// <summary>
/// Keeps the store live. Long-polls /events and, for each event, refetches the thing that
/// changed — the stream says WHAT changed, Foreman stays the truth. Never replays event
/// payloads into state. Tolerates unknown ids (a 404 on refetch is ignored) and reconnects
/// with backoff when the service is down.
/// </summary>
public sealed class EventPump
{
    private readonly IForemanApi _api;
    private readonly AppStore _store;
    private readonly int _backoffBaseMs;
    private readonly int _waitSeconds;

    public EventPump(IForemanApi api, AppStore store, int backoffBaseMs = 1000, int waitSeconds = 30)
    {
        _api = api;
        _store = store;
        _backoffBaseMs = backoffBaseMs;
        _waitSeconds = waitSeconds;
    }

    public long Cursor { get; private set; }

    /// <summary>Start over from the first event (another workplace's Foreman).</summary>
    public void Reset() => Cursor = 0;

    /// <summary>Full refetch of every collection. Also the initial load.</summary>
    public async Task LoadAllAsync(CancellationToken ct)
    {
        var employees = await _api.GetEmployeesAsync(ct);
        var tasks = await _api.GetTasksAsync(ct: ct);
        var goals = await _api.GetGoalsAsync(ct);
        _store.SetAll(employees, tasks, goals);
        _store.SetServicesUp(true);
    }

    /// <summary>One poll: fetch events after the cursor, apply each, advance the cursor.</summary>
    public async Task PumpOnceAsync(CancellationToken ct)
    {
        var page = await _api.GetEventsAsync(Cursor, _waitSeconds, 200, ct);
        Cursor = page.Cursor;
        if (page.Truncated) await LoadAllAsync(ct);
        foreach (var e in page.Events)
        {
            _store.AddEvent(e);
            await ApplyAsync(e, ct);
        }
    }

    /// <summary>Initial load, then poll forever; on failure mark services down and back off up to 30 s.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var backoff = _backoffBaseMs;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_store.ServicesUp) await LoadAllAsync(ct);
                await PumpOnceAsync(ct);
                _store.SetServicesUp(true);
                backoff = _backoffBaseMs;
                await Task.Yield();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception)
            {
                _store.SetServicesUp(false);
                try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { break; }
                backoff = Math.Min(backoff * 2, 30_000);
            }
        }
        ct.ThrowIfCancellationRequested();
    }

    private async Task ApplyAsync(EventDto e, CancellationToken ct)
    {
        try
        {
            switch (e.Type)
            {
                case "employee.state":
                    if (e.EmployeeId is { } eid) _store.SetEmployee(await _api.GetEmployeeAsync(eid, ct));
                    break;

                case "catalog.reloaded":
                    _store.SetEmployees(await _api.GetEmployeesAsync(ct));
                    break;

                case "goal.state":
                case "goal.decision":
                case "goal.blocked":
                    await RefetchGoalAsync(DataString(e, "goalId") ?? e.TaskId, ct);
                    break;

                case "run.started":
                case "run.finished":
                    if (DataBool(e, "manager")) await RefetchGoalAsync(e.TaskId, ct);   // a manager run carries the goal id in taskId
                    else await RefetchTaskAsync(e.TaskId, ct);
                    break;

                case "human.needed":
                    _store.Notify(DataString(e, "goalId") is { } g ? $"A goal needs you ({g})" : $"A task needs you ({e.TaskId})", ToastKind.Warning);
                    if (DataString(e, "goalId") is { } gid) await RefetchGoalAsync(gid, ct);
                    else await RefetchTaskAsync(e.TaskId, ct);
                    break;

                case "task.state":
                case "task.reassigned":
                case "task.cancelled":
                case "handoff.requested":
                case "handoff.answered":
                case "wrapup.written":
                    await RefetchTaskAsync(e.TaskId, ct);
                    break;
            }
        }
        catch (ApiException)
        {
            // An id we don't know (deleted, or a race with a fresh boot) is not a reason to stop pumping.
        }
    }

    private async Task RefetchTaskAsync(string? id, CancellationToken ct)
    {
        if (id is null) return;
        _store.SetTask(await _api.GetTaskAsync(id, ct));
    }

    private async Task RefetchGoalAsync(string? id, CancellationToken ct)
    {
        if (id is null) return;
        _store.SetGoal(await _api.GetGoalAsync(id, ct));
    }

    private static string? DataString(EventDto e, string name)
        => e.Data is { } d && d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static bool DataBool(EventDto e, string name)
        => e.Data is { } d && d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
