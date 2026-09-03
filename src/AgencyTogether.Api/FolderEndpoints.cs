using System.Text;
using System.Text.RegularExpressions;

namespace AgencyTogether.Api;

public static partial class FolderEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9._/-]{1,256}$")]
    private static partial Regex PathPattern();

    public static WebApplication MapFolderEndpoints(this WebApplication app)
    {
        app.MapGet("/rooms/{roomId}/files", (string roomId, ChatStore store) =>
        {
            if (!ChatEndpoints.TryNormalizeRoomId(roomId, out var room))
            {
                return ChatEndpoints.InvalidRoomId();
            }

            return Results.Ok(new FileListResponse { Room = room, Files = store.ListFiles(room) });
        });

        app.MapGet("/rooms/{roomId}/files/{*path}", (string roomId, string path, ChatStore store) =>
        {
            if (!ChatEndpoints.TryNormalizeRoomId(roomId, out var room))
            {
                return ChatEndpoints.InvalidRoomId();
            }

            if (!TryValidatePath(path, out var filePath))
            {
                return InvalidPath();
            }

            var file = store.GetFile(room, filePath);
            return file is null
                ? Results.Problem(
                    detail: $"No file '{filePath}' in room '{room}'.",
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Text(file.Content, "text/plain");
        });

        app.MapPut("/rooms/{roomId}/files/{*path}",
            async (string roomId, string path, string? id, string? name, HttpRequest request,
                   ChatStore store, ChatOptions options, CancellationToken cancellationToken) =>
            {
                if (!ChatEndpoints.TryNormalizeRoomId(roomId, out var room))
                {
                    return ChatEndpoints.InvalidRoomId();
                }

                if (!TryValidatePath(path, out var filePath))
                {
                    return InvalidPath();
                }

                var agentErrors = ValidateAgent(id, name, options);
                if (agentErrors is not null)
                {
                    return Results.ValidationProblem(agentErrors);
                }

                // Trust the declared length only as a fast reject; the read below enforces
                // the cap on what actually arrives, since a client can under-declare.
                if (request.ContentLength > options.MaxFileBytes)
                {
                    return FileTooLarge(options);
                }

                var (bytes, tooLarge) = await ReadBodyAsync(request.Body, options.MaxFileBytes, cancellationToken);
                if (tooLarge)
                {
                    return FileTooLarge(options);
                }

                var content = Encoding.UTF8.GetString(bytes);
                var (file, error) = store.PutFile(room, filePath, content, bytes.Length, id!.Trim(), name);
                if (file is null)
                {
                    return Results.Problem(detail: error, statusCode: StatusCodes.Status400BadRequest);
                }

                return Results.Ok(new FileWriteResponse
                {
                    Room = room,
                    Path = file.Path,
                    Version = file.Version,
                    Bytes = file.Bytes,
                });
            });

        app.MapDelete("/rooms/{roomId}/files/{*path}",
            (string roomId, string path, string? id, string? name, ChatStore store, ChatOptions options) =>
            {
                if (!ChatEndpoints.TryNormalizeRoomId(roomId, out var room))
                {
                    return ChatEndpoints.InvalidRoomId();
                }

                if (!TryValidatePath(path, out var filePath))
                {
                    return InvalidPath();
                }

                var agentErrors = ValidateAgent(id, name, options);
                if (agentErrors is not null)
                {
                    return Results.ValidationProblem(agentErrors);
                }

                store.DeleteFile(room, filePath, id!.Trim(), name);
                return Results.NoContent();
            });

        return app;
    }

    /// <summary>
    /// A file path is one or more slash-separated segments of [A-Za-z0-9._-], at most 256
    /// characters overall, with no empty, ".", or ".." segments and no leading slash.
    /// </summary>
    public static bool TryValidatePath(string? raw, out string path)
    {
        path = raw ?? string.Empty;

        if (!PathPattern().IsMatch(path) || path.StartsWith('/'))
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, string[]>? ValidateAgent(string? id, string? name, ChatOptions options)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(id))
        {
            errors["id"] = new[] { "id is required (query parameter)." };
        }
        else if (id.Trim().Length > options.MaxAgentIdLength)
        {
            errors["id"] = new[] { $"id must be at most {options.MaxAgentIdLength} characters." };
        }

        if (name is { Length: > 0 } && name.Trim().Length > options.MaxNameLength)
        {
            errors["name"] = new[] { $"name must be at most {options.MaxNameLength} characters." };
        }

        return errors.Count == 0 ? null : errors;
    }

    private static IResult InvalidPath()
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["path"] = new[] { "path must be slash-separated segments of [A-Za-z0-9._-], max 256 chars, with no '.', '..', empty segments, or leading slash." },
        });

    private static IResult FileTooLarge(ChatOptions options)
        => Results.Problem(
            detail: $"File exceeds the limit of {options.MaxFileBytes} bytes.",
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Reads at most <paramref name="max"/> bytes; reports true when the body is larger.</summary>
    private static async Task<(byte[] Bytes, bool TooLarge)> ReadBodyAsync(
        Stream body, int max, CancellationToken cancellationToken)
    {
        var buffer = new byte[max + 1];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total > max
            ? (Array.Empty<byte>(), true)
            : (buffer[..total], false);
    }
}
