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

public sealed record RoomSummary
{
    public required string Room { get; init; }
    public required int MessageCount { get; init; }
    public required int FileCount { get; init; }
    public required long Cursor { get; init; }
    public required IReadOnlyList<string> Agents { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
}

public sealed record SharedFile
{
    public required string Path { get; init; }
    public required string Content { get; init; }
    public required long Bytes { get; init; }
    public required int Version { get; init; }
    public required string UpdatedBy { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record FileSummary
{
    public required string Path { get; init; }
    public required long Bytes { get; init; }
    public required int Version { get; init; }
    public required string UpdatedBy { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record FileWriteResponse
{
    public required string Room { get; init; }
    public required string Path { get; init; }
    public required int Version { get; init; }
    public required long Bytes { get; init; }
}

public sealed record FileListResponse
{
    public required string Room { get; init; }
    public required IReadOnlyList<FileSummary> Files { get; init; }
}

public sealed record RoomListResponse
{
    public required IReadOnlyList<RoomSummary> Rooms { get; init; }
}

public sealed record ContextResponse
{
    public required string Room { get; init; }
    public required long Cursor { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required IReadOnlyList<AgentPresence> Agents { get; init; }
    public required bool Truncated { get; init; }
    public required string Brief { get; init; }
}
