using System.Text.Json;

namespace HomeWorkplace.Foreman;

public sealed record RuntimeEvent
{
    public required long Seq { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Type { get; init; }
    public string? EmployeeId { get; init; }
    public string? TaskId { get; init; }
    public string? RunId { get; init; }
    public JsonElement? Data { get; init; }
}

public sealed record EventPage
{
    public required long Cursor { get; init; }
    public required IReadOnlyList<RuntimeEvent> Events { get; init; }
    public required bool Truncated { get; init; }
}

public enum Vendor { Claude, Codex }

public enum EmployeeStatus { Awake, Asleep, Working, Waiting }

public sealed record Schedule(string Wake, string Sleep)
{
    public TimeOnly WakeTime => TimeOnly.Parse(Wake);
    public TimeOnly SleepTime => TimeOnly.Parse(Sleep);
}

public sealed record EmployeeDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
    public required Vendor Vendor { get; init; }
    public required string Model { get; init; }
    public string? Effort { get; init; }
    public IReadOnlyList<string> ClaudeAllowedTools { get; init; } = Array.Empty<string>();
    public string? CodexSandbox { get; init; }
    public required Schedule Schedule { get; init; }
    public int? MaxRunMinutes { get; init; }
    public string SkillsMd { get; init; } = "";
    public string LifeMd { get; init; } = "";
}

public sealed record EmployeeState
{
    public required string Id { get; init; }
    public EmployeeStatus Status { get; init; } = EmployeeStatus.Asleep;
    public string? CurrentTaskId { get; init; }
    public int RunsToday { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public DateTimeOffset? AwakeOverrideUntil { get; init; }

    public static EmployeeState Initial(string id) => new() { Id = id };
}

public sealed record EmployeeView
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
    public required Vendor Vendor { get; init; }
    public required string Model { get; init; }
    public required EmployeeStatus Status { get; init; }
    public string? CurrentTaskId { get; init; }
    public required int RunsToday { get; init; }
    public required int Energy { get; init; }
    /// <summary>Shift, local "HH:mm" — the office game lights the room by the team's hours.</summary>
    public required string Wake { get; init; }
    public required string Sleep { get; init; }
}

public enum TaskState { Queued, Running, Waiting, NeedsHuman, Done, Failed, Cancelled }

public sealed record ProgressEntry(string Author, DateOnly Date, IReadOnlyList<string> Done, IReadOnlyList<string> Next);

public sealed record Usage(long DurationMs, long? InputTokens, long? OutputTokens, decimal? CostUsd, int? Turns);

public sealed record RunRecord
{
    public required string Id { get; init; }
    public required string Employee { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; set; }
    public string Status { get; set; } = "running";
    public Usage? Usage { get; set; }
    public string? ResultSummary { get; set; }
}

public sealed record HandoffAsk(string To, string Question);

public sealed record PendingAnswer(string From, string Text);

public sealed record SessionRef(string Vendor, string SessionId, DateOnly Day);

public sealed class TaskModel
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Brief { get; set; }
    /// <summary>Empty while the task is a ticket on the board waiting for someone to claim it.</summary>
    public required string Assignee { get; set; }
    /// <summary>The role a ticket is meant for (matches an employee's role, case-insensitive); null = anyone.</summary>
    public string? Role { get; set; }
    /// <summary>A ticket's budget, used when a manager turns it into a goal; null = the default.</summary>
    public decimal? BudgetUsd { get; set; }
    public TaskState Status { get; set; } = TaskState.Queued;
    public bool RequiresApproval { get; set; }
    public bool AwaitingApproval { get; set; }
    public string? PendingQuestion { get; set; }
    public string? ParentId { get; set; }
    public string? GoalId { get; set; }
    public List<string> ChildIds { get; set; } = new();
    public required string Room { get; set; }
    public required string Workspace { get; set; }
    public SessionRef? Session { get; set; }
    public List<ProgressEntry> Progress { get; set; } = new();
    public List<RunRecord> Runs { get; set; } = new();
    public PendingAnswer? PendingAnswer { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record CreateTaskRequest(string? Title, string? Brief, string? Assignee, bool RequiresApproval = false);
public sealed record CreateTicketRequest(string? Title, string? Brief, string? Role, bool RequiresApproval = false, decimal? BudgetUsd = null);
public sealed record AnswerRequest(string? Text);
public sealed record ReassignRequest(string? Assignee);

public enum RunOutcome { Done, Handoff, NeedsHuman, Failed }

public enum SessionMode { New, Resume }

public sealed record RunSpec
{
    public required string RunId { get; init; }
    public required EmployeeDefinition Employee { get; init; }
    public required string TaskId { get; init; }
    public required string Workspace { get; init; }
    public required string SystemPrompt { get; init; }
    public required string Prompt { get; init; }
    public required SessionMode Mode { get; init; }
    public string? SessionId { get; init; }
    public required TimeSpan Timeout { get; init; }
}

public sealed record RunResult
{
    public required string RunId { get; init; }
    public required RunOutcome Status { get; init; }
    public required string Summary { get; init; }
    public HandoffAsk? Ask { get; init; }
    public IReadOnlyList<string> Artifacts { get; init; } = Array.Empty<string>();
    public required string SessionId { get; init; }
    public required Usage Usage { get; init; }
    public string RawTail { get; init; } = "";
}

public sealed record WrapUpResult(IReadOnlyList<string> Done, IReadOnlyList<string> Next, string SessionId);
