namespace HomeWorkplace.Client;

/// <summary>What the UI needs from Foreman. <see cref="ForemanClient"/> is the HTTP implementation; tests use a fake.</summary>
public interface IForemanApi
{
    Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(CancellationToken ct = default);
    Task<EmployeeDto> GetEmployeeAsync(string id, CancellationToken ct = default);
    Task ReloadEmployeesAsync(CancellationToken ct = default);
    Task WakeAsync(string id, string? until = null, CancellationToken ct = default);
    Task SleepAsync(string id, CancellationToken ct = default);
    Task ResetAsync(string id, CancellationToken ct = default);

    // ---- hiring ----
    Task<HiringDto> GetHiringAsync(CancellationToken ct = default);
    Task<EmployeeDto> HireAsync(HireRequest request, CancellationToken ct = default);
    Task FireAsync(string id, CancellationToken ct = default);

    Task<TaskDto> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TaskDto>> GetTasksAsync(TaskState? status = null, string? assignee = null, CancellationToken ct = default);
    Task<TaskDto> GetTaskAsync(string id, CancellationToken ct = default);
    Task<TaskDto> ApproveAsync(string id, CancellationToken ct = default);
    Task<TaskDto> AnswerAsync(string id, string text, CancellationToken ct = default);
    Task<TaskDto> ReassignAsync(string id, string assignee, CancellationToken ct = default);
    Task<TaskDto> RetryAsync(string id, CancellationToken ct = default);
    Task<TaskDto> CancelTaskAsync(string id, CancellationToken ct = default);

    Task<GoalDto> CreateGoalAsync(CreateGoalRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<GoalDto>> GetGoalsAsync(CancellationToken ct = default);
    Task<GoalDto> GetGoalAsync(string id, CancellationToken ct = default);
    Task<GoalDto> TopUpAsync(string id, decimal addUsd, CancellationToken ct = default);
    Task<GoalDto> CancelGoalAsync(string id, CancellationToken ct = default);

    Task<EventPageDto> GetEventsAsync(long since = 0, int wait = 0, int limit = 200, CancellationToken ct = default);
    Task<HealthDto> GetHealthAsync(CancellationToken ct = default);
}
