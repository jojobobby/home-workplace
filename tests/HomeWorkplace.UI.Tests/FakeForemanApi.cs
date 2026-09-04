using HomeWorkplace.Client;

namespace HomeWorkplace.UI.Tests;

/// <summary>Records every call; answers from dictionaries and a queue of event pages.</summary>
public sealed class FakeForemanApi : IForemanApi
{
    public List<string> Calls { get; } = new();
    public Dictionary<string, EmployeeDto> Employees { get; } = new();
    public Dictionary<string, TaskDto> Tasks { get; } = new();
    public Dictionary<string, GoalDto> Goals { get; } = new();
    public Queue<Func<long, EventPageDto>> Pages { get; } = new();
    public Exception? ThrowOnEvents { get; set; }
    public List<long> SinceValues { get; } = new();

    public static EmployeeDto Employee(string id, EmployeeStatus status = EmployeeStatus.Awake)
        => new() { Id = id, Name = id, Role = "r", Vendor = Vendor.Claude, Model = "m", Status = status, Energy = 100 };
    public static TaskDto Task(string id, TaskState status = TaskState.Queued, string assignee = "ada")
        => new() { Id = id, Title = "T " + id, Brief = "b", Assignee = assignee, Status = status, Room = "task-" + id };
    public static GoalDto Goal(string id, GoalState status = GoalState.Running)
        => new() { Id = id, Title = "G " + id, Brief = "b", Manager = "mia", Status = status, BudgetUsd = 5m, Room = "goal-" + id };
    public static EventDto Ev(long seq, string type, string? employeeId = null, string? taskId = null, object? data = null)
        => new() { Seq = seq, Type = type, EmployeeId = employeeId, TaskId = taskId, Timestamp = DateTimeOffset.UtcNow,
                   Data = data is null ? null : System.Text.Json.JsonSerializer.SerializeToElement(data) };

    private T Rec<T>(string call, T value) { Calls.Add(call); return value; }

    public Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("employees", (IReadOnlyList<EmployeeDto>)Employees.Values.ToList()));
    public Task<EmployeeDto> GetEmployeeAsync(string id, CancellationToken ct = default) => Employees.TryGetValue(id, out var e) ? System.Threading.Tasks.Task.FromResult(Rec("employee:" + id, e)) : throw new ApiException(404, "Not Found", null);
    public Task ReloadEmployeesAsync(CancellationToken ct = default) { Calls.Add("reload"); return System.Threading.Tasks.Task.CompletedTask; }
    public Task WakeAsync(string id, string? until = null, CancellationToken ct = default) { Calls.Add("wake:" + id + (until is null ? "" : "@" + until)); return System.Threading.Tasks.Task.CompletedTask; }
    public Task SleepAsync(string id, CancellationToken ct = default) { Calls.Add("sleep:" + id); return System.Threading.Tasks.Task.CompletedTask; }
    public Task ResetAsync(string id, CancellationToken ct = default) { Calls.Add("reset:" + id); return System.Threading.Tasks.Task.CompletedTask; }

    public Task<TaskDto> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default) { var t = Task("new", assignee: request.Assignee); Tasks[t.Id] = t; return System.Threading.Tasks.Task.FromResult(Rec("createTask:" + request.Title, t)); }
    public Task<IReadOnlyList<TaskDto>> GetTasksAsync(TaskState? status = null, string? assignee = null, CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("tasks", (IReadOnlyList<TaskDto>)Tasks.Values.ToList()));
    public Task<TaskDto> GetTaskAsync(string id, CancellationToken ct = default) => Tasks.TryGetValue(id, out var t) ? System.Threading.Tasks.Task.FromResult(Rec("task:" + id, t)) : throw new ApiException(404, "Not Found", null);
    public Task<TaskDto> ApproveAsync(string id, CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("approve:" + id, Tasks[id]));
    public Task<TaskDto> AnswerAsync(string id, string text, CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("answer:" + id + ":" + text, Tasks[id]));
    public Task<TaskDto> ReassignAsync(string id, string assignee, CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("reassign:" + id + ":" + assignee, Tasks[id]));
    public Task<TaskDto> RetryAsync(string id, CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("retry:" + id, Tasks[id]));
    public Task<TaskDto> CancelTaskAsync(string id, CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("cancelTask:" + id, Tasks[id]));

    public Task<GoalDto> CreateGoalAsync(CreateGoalRequest request, CancellationToken ct = default) { var g = Goal("newgoal"); Goals[g.Id] = g; return System.Threading.Tasks.Task.FromResult(Rec("createGoal:" + request.Title, g)); }
    public Task<IReadOnlyList<GoalDto>> GetGoalsAsync(CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("goals", (IReadOnlyList<GoalDto>)Goals.Values.ToList()));
    public Task<GoalDto> GetGoalAsync(string id, CancellationToken ct = default) => Goals.TryGetValue(id, out var g) ? System.Threading.Tasks.Task.FromResult(Rec("goal:" + id, g)) : throw new ApiException(404, "Not Found", null);
    public Task<GoalDto> TopUpAsync(string id, decimal addUsd, CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("topup:" + id + ":" + addUsd, Goals[id]));
    public Task<GoalDto> CancelGoalAsync(string id, CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("cancelGoal:" + id, Goals[id]));

    public Task<EventPageDto> GetEventsAsync(long since = 0, int wait = 0, int limit = 200, CancellationToken ct = default)
    {
        Calls.Add("events");
        SinceValues.Add(since);
        if (ThrowOnEvents is { } ex) { ThrowOnEvents = null; throw ex; }
        var page = Pages.Count > 0 ? Pages.Dequeue()(since) : new EventPageDto { Cursor = since, Events = Array.Empty<EventDto>() };
        return System.Threading.Tasks.Task.FromResult(page);
    }

    public Task<HealthDto> GetHealthAsync(CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(Rec("health", new HealthDto("ok", null)));
}

public sealed class FakeTerminalLauncher : ITerminalLauncher
{
    public List<(string Command, IReadOnlyList<string> Args)> Opened { get; } = new();
    public void Open(string command, IReadOnlyList<string> args) => Opened.Add((command, args));
}
