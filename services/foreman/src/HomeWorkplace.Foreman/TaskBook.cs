using System.Collections.Concurrent;

namespace HomeWorkplace.Foreman;

/// <summary>
/// The single owner and writer of task state. Later tasks add the run-lifecycle methods
/// (MarkRunning, ApplyResult, Approve, Answer, Reassign, …); Task 5 covers create/read/list
/// plus the shared persistence + event plumbing every later method reuses.
/// </summary>
public sealed class TaskBook
{
    private readonly ForemanOptions _options;
    private readonly TimeProvider _clock;
    private readonly EventLog _events;
    private readonly IContextApiClient _rooms;
    private readonly FileStore _store;
    private readonly ConcurrentDictionary<string, TaskModel> _tasks = new(StringComparer.Ordinal);

    public TaskBook(ForemanOptions options, TimeProvider clock, EventLog events, IContextApiClient rooms, FileStore store)
    {
        _options = options;
        _clock = clock;
        _events = events;
        _rooms = rooms;
        _store = store;
    }

    public async Task<TaskModel> CreateAsync(CreateTaskRequest req, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var id = Guid.NewGuid().ToString("N")[..8];
        var task = new TaskModel
        {
            Id = id,
            Title = req.Title!.Trim(),
            Brief = req.Brief!.Trim(),
            Assignee = req.Assignee!.Trim(),
            Status = TaskState.Queued,
            RequiresApproval = req.RequiresApproval,
            Room = $"task-{id}",
            Workspace = Path.Combine(_options.DataPath, "workspaces", id),
            CreatedAt = now,
            UpdatedAt = now,
        };
        Directory.CreateDirectory(task.Workspace);
        _tasks[id] = task;
        Save(task);
        await _rooms.PostAsync(task.Room, "foreman", "Foreman", null,
            $"Task created: {task.Title} — assigned to {task.Assignee}", ct);
        return task;
    }

    public TaskModel? Get(string id) => _tasks.TryGetValue(id, out var t) ? t : null;

    public IReadOnlyList<TaskModel> List(TaskState? status, string? assignee)
        => _tasks.Values
            .Where(t => (status is null || t.Status == status) &&
                        (string.IsNullOrEmpty(assignee) || t.Assignee == assignee))
            .OrderBy(t => t.CreatedAt)
            .ToArray();

    public IEnumerable<TaskModel> Queued()
        => _tasks.Values.Where(t => t.Status == TaskState.Queued).OrderBy(t => t.CreatedAt).ToArray();

    /// <summary>Persist a task and announce its status on the event stream.</summary>
    public void Save(TaskModel task)
    {
        task.UpdatedAt = _clock.GetUtcNow();
        _tasks[task.Id] = task;
        _store.SaveTask(task);
        _events.Emit("task.state", taskId: task.Id, data: new { task.Status, task.Assignee });
    }

    /// <summary>Load tasks from disk at startup (used by restart recovery in a later task).</summary>
    public void SeedFrom(IEnumerable<TaskModel> tasks)
    {
        foreach (var t in tasks) _tasks[t.Id] = t;
    }
}
