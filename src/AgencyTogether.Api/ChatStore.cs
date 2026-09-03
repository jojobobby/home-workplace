using System.Collections.Concurrent;

namespace AgencyTogether.Api;

public sealed class ChatStore
{
    public const string GlobalRoomId = "global";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly ChatOptions _options;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly object _globalGate = new();

    private long _globalSeq;

    public ChatStore(ChatOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
        _rooms[GlobalRoomId] = new Room(GlobalRoomId);
    }

    /// <summary>Appends a message. Returns null when the room cap would be exceeded.</summary>
    public ChatMessage? Post(string roomId, string agentId, string? name, string? goal, string content)
    {
        lock (_globalGate)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                if (_rooms.Count >= _options.MaxRooms)
                {
                    return null;
                }

                room = new Room(roomId);
                _rooms[roomId] = room;
            }

            var globalSeq = ++_globalSeq;
            return room.Append(
                agentId, name, goal, content, globalSeq,
                _clock.GetUtcNow(), _options.MaxMessagesPerRoom);
        }
    }

    /// <summary>Reads a room. A room that does not exist reads as empty; it is never created.</summary>
    public RoomSnapshot Read(string roomId, long since, int limit)
        => _rooms.TryGetValue(roomId, out var room)
            ? room.Read(since, limit)
            : new RoomSnapshot(0, Array.Empty<ChatMessage>(), Array.Empty<AgentPresence>(), false);

    public async Task<RoomSnapshot> ReadWithWaitAsync(
        string roomId, long since, int limit, TimeSpan wait, CancellationToken cancellationToken)
    {
        var deadline = _clock.GetUtcNow() + wait;

        while (true)
        {
            var snapshot = Read(roomId, since, limit);
            if (snapshot.Messages.Count > 0 || wait <= TimeSpan.Zero)
            {
                return snapshot;
            }

            var remaining = deadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return snapshot;
            }

            // The room may not exist yet. A read must never create one, and there is no
            // waiter set to park on, so poll at a short interval until it appears or the
            // deadline passes. Sleeping the full remaining time here would be a bug: an
            // agent polling an empty room would miss the message that creates it.
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                var slice = remaining < PollInterval ? remaining : PollInterval;
                await Task.Delay(slice, cancellationToken);
                continue;
            }

            if (!room.TryRegisterWaiter(since, out var signal))
            {
                continue;
            }

            var completed = await Task.WhenAny(signal, Task.Delay(remaining, cancellationToken));
            if (completed != signal)
            {
                return Read(roomId, since, limit);
            }
        }
    }
}
