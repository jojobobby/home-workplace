namespace AgencyTogether.Api;

public static class ChatEndpoints
{
    public static WebApplication MapChatEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapPost("/messages",
            (PostMessageRequest request, long? since, ChatStore store, ChatOptions options)
                => PostMessage(ChatStore.GlobalRoomId, request, since, store, options));

        app.MapPost("/rooms/{roomId}/messages",
            (string roomId, PostMessageRequest request, long? since, ChatStore store, ChatOptions options)
                => PostMessage(roomId, request, since, store, options));

        app.MapGet("/rooms/{roomId}/messages",
            (string roomId, long? since, int? limit, ChatStore store, ChatOptions options)
                => ReadRoom(roomId, since ?? 0, ClampLimit(limit, options), store));

        return app;
    }

    internal static int ClampLimit(int? limit, ChatOptions options)
        => limit is null or <= 0
            ? options.DefaultLimit
            : Math.Min(limit.Value, options.MaxLimit);

    internal static TimeSpan ClampWait(int? wait, ChatOptions options)
        => wait is null or <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Min(wait.Value, options.MaxWaitSeconds));

    private static IResult PostMessage(
        string roomId, PostMessageRequest request, long? since, ChatStore store, ChatOptions options)
    {
        var posted = store.Post(
            roomId, request.Id!.Trim(), request.Name, request.Goal, request.Content!);

        if (posted is null)
        {
            return Results.Problem(
                detail: $"Room limit of {options.MaxRooms} reached.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var snapshot = store.Read(roomId, since ?? posted.Seq, options.DefaultLimit);

        return Results.Created($"/rooms/{roomId}/messages/{posted.Seq}", new PostMessageResponse
        {
            Room = roomId,
            Posted = posted,
            Cursor = snapshot.Cursor,
            Messages = since is null ? Array.Empty<ChatMessage>() : snapshot.Messages,
            Agents = snapshot.Agents,
            Truncated = since is not null && snapshot.Truncated,
        });
    }

    private static IResult ReadRoom(string roomId, long since, int limit, ChatStore store)
    {
        var snapshot = store.Read(roomId, since, limit);

        return Results.Ok(new RoomReadResponse
        {
            Room = roomId,
            Cursor = snapshot.Cursor,
            Messages = snapshot.Messages,
            Agents = snapshot.Agents,
            Truncated = snapshot.Truncated,
        });
    }
}
