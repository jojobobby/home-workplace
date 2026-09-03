namespace AgencyTogether.Api;

public static class ChatEndpoints
{
    public static WebApplication MapChatEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapPost("/messages", (PostMessageRequest request, ChatStore store, ChatOptions options)
            => PostMessage(ChatStore.GlobalRoomId, request, store, options));

        app.MapPost("/rooms/{roomId}/messages", (string roomId, PostMessageRequest request, ChatStore store, ChatOptions options)
            => PostMessage(roomId, request, store, options));

        app.MapGet("/rooms/{roomId}/messages", (string roomId, ChatStore store, ChatOptions options)
            => ReadRoom(roomId, since: 0, limit: options.DefaultLimit, store));

        return app;
    }

    private static IResult PostMessage(
        string roomId, PostMessageRequest request, ChatStore store, ChatOptions options)
    {
        var posted = store.Post(
            roomId, request.Id!.Trim(), request.Name, request.Goal, request.Content!);

        if (posted is null)
        {
            return Results.Problem(
                detail: $"Room limit of {options.MaxRooms} reached.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var snapshot = store.Read(roomId, since: posted.Seq, limit: options.DefaultLimit);

        return Results.Created($"/rooms/{roomId}/messages/{posted.Seq}", new PostMessageResponse
        {
            Room = roomId,
            Posted = posted,
            Cursor = snapshot.Cursor,
            Messages = Array.Empty<ChatMessage>(),
            Agents = snapshot.Agents,
            Truncated = false,
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
