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
    private readonly Queue<ChatMessage> _firehose = new();
    private readonly List<TaskCompletionSource> _globalWaiters = new();

    private long _globalSeq;
    private long _firstAvailableGlobalSeq = 1;

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
            var message = room.Append(
                agentId, name, goal, content, globalSeq,
                _clock.GetUtcNow(), _options.MaxMessagesPerRoom);

            _firehose.Enqueue(message);
            while (_firehose.Count > _options.FirehoseCapacity)
            {
                _firstAvailableGlobalSeq = _firehose.Dequeue().GlobalSeq + 1;
            }

            foreach (var waiter in _globalWaiters)
            {
                waiter.TrySetResult();
            }

            _globalWaiters.Clear();

            return message;
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

    public IReadOnlyList<RoomSummary> ListRooms()
    {
        lock (_globalGate)
        {
            return _rooms.Values
                .Select(r => r.Summarize())
                .OrderBy(r => r.Room, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// Clears a room's messages and roster. The room's seq counter is deliberately
    /// left alone so cursors held by polling agents stay valid. The global room is
    /// cleared but never removed.
    /// </summary>
    public void ClearRoom(string roomId)
    {
        lock (_globalGate)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                room.Clear();
            }
        }
    }

    public FirehoseSnapshot ReadFirehose(long since, int limit)
    {
        lock (_globalGate)
        {
            var messages = _firehose
                .Where(m => m.GlobalSeq > since)
                .Take(limit)
                .ToArray();

            return new FirehoseSnapshot(_globalSeq, messages, since + 1 < _firstAvailableGlobalSeq);
        }
    }

    public async Task<FirehoseSnapshot> ReadFirehoseWithWaitAsync(
        long since, int limit, TimeSpan wait, CancellationToken cancellationToken)
    {
        var deadline = _clock.GetUtcNow() + wait;

        while (true)
        {
            var snapshot = ReadFirehose(since, limit);
            if (snapshot.Messages.Count > 0 || wait <= TimeSpan.Zero)
            {
                return snapshot;
            }

            var remaining = deadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return snapshot;
            }

            Task signal;
            lock (_globalGate)
            {
                if (_globalSeq > since)
                {
                    continue;
                }

                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _globalWaiters.Add(waiter);
                signal = waiter.Task;
            }

            var completed = await Task.WhenAny(signal, Task.Delay(remaining, cancellationToken));
            if (completed != signal)
            {
                return ReadFirehose(since, limit);
            }
        }
    }
}
