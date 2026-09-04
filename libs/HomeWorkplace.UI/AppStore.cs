using HomeWorkplace.Client;

namespace HomeWorkplace.UI;

/// <summary>
/// Everything the screens render, in one observable place. Mutated by the event pump on a
/// background thread and read by Blazor on the UI thread, so writes take a lock and reads
/// return snapshots. <see cref="Changed"/> is raised outside the lock after every mutation.
/// </summary>
public sealed class AppStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, EmployeeDto> _employees = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskDto> _tasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GoalDto> _goals = new(StringComparer.Ordinal);
    private readonly List<EventDto> _events = new();
    private readonly List<Toast> _toasts = new();
    private const int MaxEvents = 500;

    public event Action? Changed;

    public IReadOnlyDictionary<string, EmployeeDto> Employees { get { lock (_gate) return new Dictionary<string, EmployeeDto>(_employees); } }
    public IReadOnlyDictionary<string, TaskDto> Tasks { get { lock (_gate) return new Dictionary<string, TaskDto>(_tasks); } }
    public IReadOnlyDictionary<string, GoalDto> Goals { get { lock (_gate) return new Dictionary<string, GoalDto>(_goals); } }
    public IReadOnlyList<EventDto> RecentEvents { get { lock (_gate) return _events.ToArray(); } }
    public IReadOnlyList<Toast> Toasts { get { lock (_gate) return _toasts.ToArray(); } }
    public bool ServicesUp { get; private set; }

    /// <summary>Things waiting on a person: tasks parked needs-human plus goals blocked on budget.</summary>
    public int HumanNeeded
    {
        get
        {
            lock (_gate)
                return _tasks.Values.Count(t => t.Status == TaskState.NeedsHuman)
                     + _goals.Values.Count(g => g.Status == GoalState.Blocked);
        }
    }

    public void SetEmployee(EmployeeDto e) { lock (_gate) _employees[e.Id] = e; Raise(); }
    public void SetTask(TaskDto t) { lock (_gate) _tasks[t.Id] = t; Raise(); }
    public void SetGoal(GoalDto g) { lock (_gate) _goals[g.Id] = g; Raise(); }

    public void SetEmployees(IReadOnlyList<EmployeeDto> employees)
    {
        lock (_gate) { _employees.Clear(); foreach (var e in employees) _employees[e.Id] = e; }
        Raise();
    }

    public void SetAll(IReadOnlyList<EmployeeDto> employees, IReadOnlyList<TaskDto> tasks, IReadOnlyList<GoalDto> goals)
    {
        lock (_gate)
        {
            _employees.Clear(); foreach (var e in employees) _employees[e.Id] = e;
            _tasks.Clear(); foreach (var t in tasks) _tasks[t.Id] = t;
            _goals.Clear(); foreach (var g in goals) _goals[g.Id] = g;
        }
        Raise();
    }

    public void AddEvent(EventDto e)
    {
        lock (_gate)
        {
            _events.Add(e);
            if (_events.Count > MaxEvents) _events.RemoveRange(0, _events.Count - MaxEvents);
        }
        Raise();
    }

    public void SetServicesUp(bool up)
    {
        if (ServicesUp == up) return;
        ServicesUp = up;
        Raise();
    }

    public void Notify(string text, ToastKind kind)
    {
        lock (_gate) _toasts.Add(new Toast(Guid.NewGuid(), text, kind, DateTimeOffset.UtcNow));
        Raise();
    }

    public void DismissToast(Guid id)
    {
        lock (_gate) _toasts.RemoveAll(t => t.Id == id);
        Raise();
    }

    private void Raise() => Changed?.Invoke();
}
