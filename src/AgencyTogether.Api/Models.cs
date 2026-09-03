namespace AgencyTogether.Api;

public sealed record PostMessageRequest
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Goal { get; init; }
    public string? Content { get; init; }
}

public sealed record ChatMessage
{
    public required long Seq { get; init; }
    public required long GlobalSeq { get; init; }
    public required string Room { get; init; }
    public required string AgentId { get; init; }
    public required string Name { get; init; }
    public string? Goal { get; init; }
    public required string Content { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed record AgentPresence
{
    public required string AgentId { get; init; }
    public required string Name { get; init; }
    public string? Goal { get; init; }
    public required int MessageCount { get; init; }
    public required DateTimeOffset FirstSeen { get; init; }
    public required DateTimeOffset LastSeen { get; init; }
}

public sealed record RoomSnapshot(
    long Cursor,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<AgentPresence> Agents,
    bool Truncated);

public sealed record RoomReadResponse
{
    public required string Room { get; init; }
    public required long Cursor { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required IReadOnlyList<AgentPresence> Agents { get; init; }
    public required bool Truncated { get; init; }
}

public sealed record PostMessageResponse
{
    public required string Room { get; init; }
    public required ChatMessage Posted { get; init; }
    public required long Cursor { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required IReadOnlyList<AgentPresence> Agents { get; init; }
    public required bool Truncated { get; init; }
}

public sealed record FirehoseSnapshot(
    long Cursor,
    IReadOnlyList<ChatMessage> Messages,
    bool Truncated);

public sealed record FirehoseResponse
{
    public required long Cursor { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required bool Truncated { get; init; }
}
