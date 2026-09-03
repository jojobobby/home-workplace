using System.Collections.Concurrent;

namespace AgencyTogether.Api;

public sealed class ChatStore
{
    public const string GlobalRoomId = "global";

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
}
