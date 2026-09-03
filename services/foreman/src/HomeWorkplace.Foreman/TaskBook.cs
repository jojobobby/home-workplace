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
    private readonly EmployeeCatalog _employees;
    private readonly ConcurrentDictionary<string, TaskModel> _tasks = new(StringComparer.Ordinal);

    public TaskBook(ForemanOptions options, TimeProvider clock, EventLog events, IContextApiClient rooms, FileStore store, EmployeeCatalog employees)
    {
        _options = options;
        _clock = clock;
        _events = events;
        _rooms = rooms;
        _store = store;
        _employees = employees;
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

    /// <summary>Mark a task running and open a run record. Posts to the task room.</summary>
    public void MarkRunning(string taskId, string runId, string employeeId, DateTimeOffset now)
    {
        var t = _tasks[taskId];
        t.Status = TaskState.Running;
        t.Runs.Add(new RunRecord { Id = runId, Employee = employeeId, StartedAt = now });
        Save(t);
        _ = _rooms.PostAsync(t.Room, "foreman", "Foreman", null, $"Run started by {employeeId}.", CancellationToken.None);
    }

    /// <summary>Apply a finished run to the task and settle the employee. Handoff arrives in Task 8.</summary>
    public void ApplyResult(string taskId, string employeeId, string runId, RunResult result, DateTimeOffset now)
    {
        var t = _tasks[taskId];
        var run = t.Runs.FirstOrDefault(r => r.Id == runId);
        if (run is not null)
        {
            run.EndedAt = now;
            run.Status = result.Status.ToString();
            run.Usage = result.Usage;
            run.ResultSummary = result.Summary;
        }
        t.Session = new SessionRef(t.Assignee, result.SessionId, DateOnly.FromDateTime(now.UtcDateTime));
        t.PendingAnswer = null;

        switch (result.Status)
        {
            case RunOutcome.Done:
                if (t.RequiresApproval) { t.Status = TaskState.NeedsHuman; t.AwaitingApproval = true; }
                else t.Status = TaskState.Done;
                _employees.Free(employeeId);
                break;
            case RunOutcome.NeedsHuman:
                t.Status = TaskState.NeedsHuman;
                t.AwaitingApproval = false;
                t.PendingQuestion = result.Summary;
                _employees.Free(employeeId);
                _events.Emit("human.needed", employeeId, taskId, runId);
                break;
            case RunOutcome.Failed:
                t.Status = TaskState.Failed;
                _employees.Free(employeeId);
                break;
            case RunOutcome.Handoff:
                throw new NotSupportedException("handoff arrives in Task 8");
        }

        Save(t);
        _ = _rooms.PostAsync(t.Room, "foreman", "Foreman", null,
            $"Run finished ({result.Status}): {result.Summary}", CancellationToken.None);
    }

    /// <summary>Sign off a task parked for approval.</summary>
    public bool Approve(string id)
    {
        if (Get(id) is not { Status: TaskState.NeedsHuman, AwaitingApproval: true } t) return false;
        t.Status = TaskState.Done;
        t.AwaitingApproval = false;
        Save(t);
        _ = _rooms.PostAsync(t.Room, "foreman", "Foreman", null, "Approved by a human.", CancellationToken.None);
        return true;
    }

    /// <summary>Deliver a human's answer to a task parked on a question; re-queue it to resume.</summary>
    public bool Answer(string id, string text, RunSupervisor supervisor)
    {
        if (Get(id) is not { Status: TaskState.NeedsHuman, AwaitingApproval: false } t) return false;
        t.PendingAnswer = new PendingAnswer("human", text);
        t.PendingQuestion = null;
        t.Status = TaskState.Queued;
        Save(t);
        supervisor.Pump();
        return true;
    }

    /// <summary>An exception escaped the run: close it failed and free the employee.</summary>
    public void FailRun(string taskId, string runId, string error, DateTimeOffset now)
    {
        var t = _tasks[taskId];
        var run = t.Runs.FirstOrDefault(r => r.Id == runId);
        if (run is not null) { run.EndedAt = now; run.Status = "Failed"; run.ResultSummary = error; }
        t.Status = TaskState.Failed;
        _employees.Free(t.Assignee);
        Save(t);
    }

    /// <summary>Load tasks from disk at startup (used by restart recovery in a later task).</summary>
    public void SeedFrom(IEnumerable<TaskModel> tasks)
    {
        foreach (var t in tasks) _tasks[t.Id] = t;
    }
}
