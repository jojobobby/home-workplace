namespace HomeWorkplace.Foreman;

/// <summary>
/// Rebuilds in-memory state from disk at startup so a restart loses nothing. Tasks that were
/// mid-run when the process died come back Queued (their run is gone); employees come back
/// Asleep (they "went home" on the crash) and the DayCycle wakes them on schedule; goals
/// reload as saved; the event cursor continues where it left off.
/// </summary>
public sealed class StateRecovery
{
    private readonly FileStore _store;
    private readonly TaskBook _tasks;
    private readonly GoalBook _goals;
    private readonly EmployeeCatalog _employees;
    private readonly EventLog _events;
    private readonly ForemanOptions _options;
    private readonly TimeProvider _clock;

    public StateRecovery(FileStore store, TaskBook tasks, GoalBook goals, EmployeeCatalog employees,
        EventLog events, ForemanOptions options, TimeProvider clock)
    {
        _store = store;
        _tasks = tasks;
        _goals = goals;
        _employees = employees;
        _events = events;
        _options = options;
        _clock = clock;
    }

    public void Recover()
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().Date);

        _employees.SeedStates(_store.LoadStates()
            .Select(s => s with { Status = EmployeeStatus.Asleep, CurrentTaskId = null, AwakeOverrideUntil = null }));

        var tasks = new List<TaskModel>();
        foreach (var t in _store.LoadTasks())
        {
            if (t.Status == TaskState.Running) t.Status = TaskState.Queued;   // its run is gone
            if (t.Session is { } s && s.Day != today) t.Session = null;       // yesterday's session is dead
            tasks.Add(t);
        }
        _tasks.SeedFrom(tasks);

        _goals.SeedFrom(_store.LoadGoals());

        _events.Seed(_store.LoadEvents(_options.EventsCapacity));
    }
}
