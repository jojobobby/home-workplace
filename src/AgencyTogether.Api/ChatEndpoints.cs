using System.Text.RegularExpressions;

namespace AgencyTogether.Api;

public static partial class ChatEndpoints
{
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex RoomIdPattern();

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
            (string roomId, long? since, int? limit, int? wait,
             ChatStore store, ChatOptions options, CancellationToken cancellationToken)
                => ReadRoomAsync(roomId, since ?? 0, ClampLimit(limit, options),
                                 ClampWait(wait, options), store, cancellationToken));

        app.MapGet("/firehose",
            async (long? since, int? limit, int? wait,
                   ChatStore store, ChatOptions options, CancellationToken cancellationToken) =>
            {
                var snapshot = await store.ReadFirehoseWithWaitAsync(
                    since ?? 0, ClampLimit(limit, options), ClampWait(wait, options), cancellationToken);

                return Results.Ok(new FirehoseResponse
                {
                    Cursor = snapshot.Cursor,
                    Messages = snapshot.Messages,
                    Truncated = snapshot.Truncated,
                });
            });

        return app;
    }

    internal static bool TryNormalizeRoomId(string raw, out string roomId)
    {
        roomId = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return RoomIdPattern().IsMatch(roomId);
    }

    internal static Dictionary<string, string[]>? ValidateRequest(
        PostMessageRequest request, ChatOptions options)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Id))
        {
            errors["id"] = new[] { "id is required." };
        }
        else if (request.Id.Trim().Length > options.MaxAgentIdLength)
        {
            errors["id"] = new[] { $"id must be at most {options.MaxAgentIdLength} characters." };
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            errors["content"] = new[] { "content is required." };
        }
        else if (request.Content.Length > options.MaxContentLength)
        {
            errors["content"] = new[] { $"content must be at most {options.MaxContentLength} characters." };
        }

        if (request.Name is { Length: > 0 } && request.Name.Trim().Length > options.MaxNameLength)
        {
            errors["name"] = new[] { $"name must be at most {options.MaxNameLength} characters." };
        }

        if (request.Goal is { Length: > 0 } && request.Goal.Trim().Length > options.MaxGoalLength)
        {
            errors["goal"] = new[] { $"goal must be at most {options.MaxGoalLength} characters." };
        }

        return errors.Count == 0 ? null : errors;
    }

    internal static int ClampLimit(int? limit, ChatOptions options)
        => limit is null or <= 0
            ? options.DefaultLimit
            : Math.Min(limit.Value, options.MaxLimit);

    internal static TimeSpan ClampWait(int? wait, ChatOptions options)
        => wait is null or <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Min(wait.Value, options.MaxWaitSeconds));

    private static IResult InvalidRoomId()
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["roomId"] = new[] { "roomId must match ^[a-z0-9][a-z0-9_-]{0,63}$." },
        });

    private static IResult PostMessage(
        string roomId, PostMessageRequest request, long? since, ChatStore store, ChatOptions options)
    {
        if (!TryNormalizeRoomId(roomId, out var normalizedRoom))
        {
            return InvalidRoomId();
        }

        var errors = ValidateRequest(request, options);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        var posted = store.Post(
            normalizedRoom, request.Id!.Trim(), request.Name, request.Goal, request.Content!);

        if (posted is null)
        {
            return Results.Problem(
                detail: $"Room limit of {options.MaxRooms} reached.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var snapshot = store.Read(normalizedRoom, since ?? posted.Seq, options.DefaultLimit);

        return Results.Created($"/rooms/{normalizedRoom}/messages/{posted.Seq}", new PostMessageResponse
        {
            Room = normalizedRoom,
            Posted = posted,
            Cursor = snapshot.Cursor,
            Messages = since is null ? Array.Empty<ChatMessage>() : snapshot.Messages,
            Agents = snapshot.Agents,
            Truncated = since is not null && snapshot.Truncated,
        });
    }

    private static async Task<IResult> ReadRoomAsync(
        string roomId, long since, int limit, TimeSpan wait,
        ChatStore store, CancellationToken cancellationToken)
    {
        if (!TryNormalizeRoomId(roomId, out var normalizedRoom))
        {
            return InvalidRoomId();
        }

        var snapshot = await store.ReadWithWaitAsync(
            normalizedRoom, since, limit, wait, cancellationToken);

        return Results.Ok(new RoomReadResponse
        {
            Room = normalizedRoom,
            Cursor = snapshot.Cursor,
            Messages = snapshot.Messages,
            Agents = snapshot.Agents,
            Truncated = snapshot.Truncated,
        });
    }
}
