using System.Text.Json;

namespace HomeWorkplace.Client;

// Enum member ORDER matches the services' enums exactly: the APIs emit these as numbers.
public enum Vendor { Claude, Codex }
public enum EmployeeStatus { Awake, Asleep, Working, Waiting }
public enum TaskState { Queued, Running, Waiting, NeedsHuman, Done, Failed, Cancelled }
public enum GoalState { Planning, Running, Blocked, Done, Failed, Cancelled }

public sealed record EmployeeDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Role { get; init; } = "";
    public Vendor Vendor { get; init; }
    public string Model { get; init; } = "";
    public EmployeeStatus Status { get; init; }
    public string? CurrentTaskId { get; init; }
    public int RunsToday { get; init; }
    public int Energy { get; init; }
}

public sealed record UsageDto(long DurationMs, long? InputTokens, long? OutputTokens, decimal? CostUsd, int? Turns);

public sealed record RunDto
{
    public string Id { get; init; } = "";
    public string Employee { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public string Status { get; init; } = "";
    public UsageDto? Usage { get; init; }
    public string? ResultSummary { get; init; }
}

public sealed record ProgressDto(string Author, DateOnly Date, IReadOnlyList<string> Done, IReadOnlyList<string> Next);
public sealed record SessionDto(string Vendor, string SessionId, DateOnly Day);
public sealed record PendingAnswerDto(string From, string Text);
public sealed record DecisionDto(DateTimeOffset At, string Summary);

public sealed record TaskDto
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Brief { get; init; } = "";
    public string Assignee { get; init; } = "";
    public TaskState Status { get; init; }
    public bool RequiresApproval { get; init; }
    public bool AwaitingApproval { get; init; }
    public string? PendingQuestion { get; init; }
    public string? ParentId { get; init; }
    public string? GoalId { get; init; }
    public IReadOnlyList<string> ChildIds { get; init; } = Array.Empty<string>();
    public string Room { get; init; } = "";
    public string Workspace { get; init; } = "";
    public SessionDto? Session { get; init; }
    public IReadOnlyList<ProgressDto> Progress { get; init; } = Array.Empty<ProgressDto>();
    public IReadOnlyList<RunDto> Runs { get; init; } = Array.Empty<RunDto>();
    public PendingAnswerDto? PendingAnswer { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record GoalDto
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Brief { get; init; } = "";
    public string Manager { get; init; } = "";
    public decimal BudgetUsd { get; init; }
    public decimal SpentUsd { get; init; }
    public GoalState Status { get; init; }
    public string Room { get; init; } = "";
    public IReadOnlyList<string> TaskIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PendingNotes { get; init; } = Array.Empty<string>();
    public bool NeedsManagerAttention { get; init; }
    public DecisionDto? LastDecision { get; init; }
    public SessionDto? Session { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record EventDto
{
    public long Seq { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Type { get; init; } = "";
    public string? EmployeeId { get; init; }
    public string? TaskId { get; init; }
    public string? RunId { get; init; }
    public JsonElement? Data { get; init; }
}

public sealed record EventPageDto
{
    public long Cursor { get; init; }
    public IReadOnlyList<EventDto> Events { get; init; } = Array.Empty<EventDto>();
    public bool Truncated { get; init; }
}

public sealed record HealthDto(string Status, string? ContextApi);

public sealed record FileDto(string Path, long Bytes, int Version, string UpdatedBy, DateTimeOffset UpdatedAt);
public sealed record RoomFilesDto(string Room, IReadOnlyList<FileDto> Files);

public sealed record ProblemDetailsDto(string? Type, string? Title, int? Status, string? Detail, Dictionary<string, string[]>? Errors);

public sealed record CreateTaskRequest(string Title, string Brief, string Assignee, bool RequiresApproval = false);
public sealed record CreateGoalRequest(string Title, string Brief, string Manager, decimal BudgetUsd);
