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
