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
}
