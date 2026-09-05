using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Tests;

/// <summary>Records every call and answers from dictionaries. <see cref="Hold"/> parks the next mutating call until released.</summary>
public sealed class FakeForemanApi : IForemanApi
{
    public List<string> Calls { get; } = new();
    public Dictionary<string, EmployeeDto> Employees { get; } = new();
    public Dictionary<string, TaskDto> Tasks { get; } = new();
    public Dictionary<string, GoalDto> Goals { get; } = new();
    public TaskCompletionSource? Hold { get; set; }
    public Exception? Throw { get; set; }

    public static EmployeeDto Employee(string id, EmployeeStatus status = EmployeeStatus.Awake, string? taskId = null, string role = "Engineer")
        => new() { Id = id, Name = char.ToUpperInvariant(id[0]) + id[1..], Role = role, Status = status, CurrentTaskId = taskId, Energy = 100 };
    public static TaskDto Task(string id, TaskState status = TaskState.Queued, string assignee = "ada")
        => new() { Id = id, Title = "T " + id, Brief = "b", Assignee = assignee, Status = status, Room = "task-" + id };
    public static GoalDto Goal(string id, GoalState status = GoalState.Running)
        => new() { Id = id, Title = "G " + id, Brief = "b", Manager = "mia", Status = status, BudgetUsd = 5m, Room = "goal-" + id };

    private async Task<T> Rec<T>(string call, T value)
    {
        if (Hold is { } hold) { Hold = null; await hold.Task; }
        if (Throw is { } ex) { Throw = null; throw ex; }
        Calls.Add(call);
        return value;
    }
    private Task Rec(string call) => Rec(call, 0);

    public Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(CancellationToken ct = default) => Rec("employees", (IReadOnlyList<EmployeeDto>)Employees.Values.ToList());
    public Task<EmployeeDto> GetEmployeeAsync(string id, CancellationToken ct = default) => Rec("employee:" + id, Employees[id]);
    public Task ReloadEmployeesAsync(CancellationToken ct = default) => Rec("reload");
    public Task WakeAsync(string id, string? until = null, CancellationToken ct = default) => Rec("wake:" + id);
    public Task SleepAsync(string id, CancellationToken ct = default) => Rec("sleep:" + id);
    public Task ResetAsync(string id, CancellationToken ct = default) => Rec("reset:" + id);
    public HiringDto Hiring { get; set; } = new(Array.Empty<HiringTemplateDto>(), Array.Empty<BrainDto>());
    public Task<HiringDto> GetHiringAsync(CancellationToken ct = default) => Rec("hiring", Hiring);
    public Task<EmployeeDto> HireAsync(HireRequest r, CancellationToken ct = default)
    {
        var e = Employee(r.Name.ToLowerInvariant() + "-" + r.TemplateId) with { Name = r.Name, Model = r.Model };
        Employees[e.Id] = e;
        return Rec($"hire:{r.TemplateId}:{r.Model}:{r.Name}", e);
    }
    public Task FireAsync(string id, CancellationToken ct = default) { Employees.Remove(id); return Rec("fire:" + id); }
    public Task<TaskDto> CreateTaskAsync(CreateTaskRequest r, CancellationToken ct = default) => Rec($"createTask:{r.Assignee}:{r.Title}:{r.Brief}", Task("new", assignee: r.Assignee) with { Title = r.Title });
    public Task<IReadOnlyList<TaskDto>> GetTasksAsync(TaskState? status = null, string? assignee = null, CancellationToken ct = default) => Rec("tasks", (IReadOnlyList<TaskDto>)Tasks.Values.ToList());
    public Task<TaskDto> GetTaskAsync(string id, CancellationToken ct = default) => Rec("task:" + id, Tasks[id]);
    public Task<TaskDto> ApproveAsync(string id, CancellationToken ct = default) => Rec("approve:" + id, Tasks[id]);
    public Task<TaskDto> AnswerAsync(string id, string text, CancellationToken ct = default) => Rec($"answer:{id}:{text}", Tasks[id]);
    public Task<TaskDto> ReassignAsync(string id, string assignee, CancellationToken ct = default) => Rec($"reassign:{id}:{assignee}", Tasks[id]);
    public Task<TaskDto> RetryAsync(string id, CancellationToken ct = default) => Rec("retry:" + id, Tasks[id]);
    public Task<TaskDto> CancelTaskAsync(string id, CancellationToken ct = default) => Rec("cancelTask:" + id, Tasks[id]);
    public Task<GoalDto> CreateGoalAsync(CreateGoalRequest r, CancellationToken ct = default) => Rec($"createGoal:{r.Manager}:{r.Title}:{r.Brief}:{r.BudgetUsd}", Goal("newgoal") with { Title = r.Title });
    public Task<IReadOnlyList<GoalDto>> GetGoalsAsync(CancellationToken ct = default) => Rec("goals", (IReadOnlyList<GoalDto>)Goals.Values.ToList());
    public Task<GoalDto> GetGoalAsync(string id, CancellationToken ct = default) => Rec("goal:" + id, Goals[id]);
    public Task<GoalDto> TopUpAsync(string id, decimal addUsd, CancellationToken ct = default) => Rec($"topup:{id}:{addUsd}", Goals[id]);
    public Task<GoalDto> CancelGoalAsync(string id, CancellationToken ct = default) => Rec("cancelGoal:" + id, Goals[id]);
    public Task<EventPageDto> GetEventsAsync(long since = 0, int wait = 0, int limit = 200, CancellationToken ct = default) => Rec("events", new EventPageDto { Cursor = since });
    public Task<HealthDto> GetHealthAsync(CancellationToken ct = default) => Rec("health", new HealthDto("ok", null));
}

public sealed class FakeContextApi : IContextApi
{
    public Dictionary<string, string> Briefs { get; } = new();
    public List<string> Calls { get; } = new();

    public Task<string> GetBriefAsync(string room, CancellationToken ct = default)
    {
        Calls.Add("brief:" + room);
        return System.Threading.Tasks.Task.FromResult(Briefs.TryGetValue(room, out var b) ? b : $"# Agency room: {room}\n_No messages yet._");
    }

    public Task<RoomFilesDto> ListFilesAsync(string room, CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(new RoomFilesDto(room, Array.Empty<FileDto>()));
    public Task<string> GetFileAsync(string room, string path, CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult("");
}
