namespace AgencyTogether.Api;

public sealed class Room
{
    private readonly object _gate = new();
    private readonly List<ChatMessage> _messages = new();
    private readonly Dictionary<string, AgentPresence> _agents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SharedFile> _files = new(StringComparer.Ordinal);
    private readonly List<TaskCompletionSource> _waiters = new();

    private long _seq;
    private long _firstAvailableSeq = 1;

    public Room(string id) => Id = id;

    public string Id { get; }

    public ChatMessage Append(
        string agentId,
        string? name,
        string? goal,
        string content,
        long globalSeq,
        DateTimeOffset now,
        int maxMessages)
    {
        lock (_gate)
        {
            _agents.TryGetValue(agentId, out var existing);

            var effectiveName = !string.IsNullOrWhiteSpace(name)
                ? name!.Trim()
                : existing?.Name ?? agentId;

            var effectiveGoal = !string.IsNullOrWhiteSpace(goal)
                ? goal!.Trim()
                : existing?.Goal;

            var message = new ChatMessage
            {
                Seq = ++_seq,
                GlobalSeq = globalSeq,
                Room = Id,
                AgentId = agentId,
                Name = effectiveName,
                Goal = effectiveGoal,
                Content = content,
                Timestamp = now,
            };

            _messages.Add(message);

            _agents[agentId] = new AgentPresence
            {
                AgentId = agentId,
                Name = effectiveName,
                Goal = effectiveGoal,
                MessageCount = (existing?.MessageCount ?? 0) + 1,
                FirstSeen = existing?.FirstSeen ?? now,
                LastSeen = now,
            };

            while (_messages.Count > maxMessages)
            {
                _firstAvailableSeq = _messages[0].Seq + 1;
                _messages.RemoveAt(0);
            }

            ReleaseWaiters();
            return message;
        }
    }

    public RoomSnapshot Read(long since, int limit)
    {
        lock (_gate)
        {
            var messages = _messages
                .Where(m => m.Seq > since)
                .Take(limit)
                .ToArray();

            return new RoomSnapshot(_seq, messages, OrderedAgents(), since + 1 < _firstAvailableSeq);
        }
    }

    public RoomSummary Summarize()
    {
        lock (_gate)
        {
            return new RoomSummary
            {
                Room = Id,
                MessageCount = _messages.Count,
                FileCount = _files.Count,
                Cursor = _seq,
                Agents = OrderedAgents().Select(a => a.AgentId).ToArray(),
                LastActivity = _messages.Count == 0 ? null : _messages[^1].Timestamp,
            };
        }
    }

    /// <summary>
    /// Writes or overwrites a file. Returns null when the path is new and the room is
    /// already at <paramref name="maxFiles"/>; overwriting an existing path is always allowed.
    /// </summary>
    public SharedFile? PutFile(
        string path, string content, long bytes, string agentId, DateTimeOffset now, int maxFiles)
    {
        lock (_gate)
        {
            _files.TryGetValue(path, out var existing);

            if (existing is null && _files.Count >= maxFiles)
            {
                return null;
            }

            var file = new SharedFile
            {
                Path = path,
                Content = content,
                Bytes = bytes,
                Version = (existing?.Version ?? 0) + 1,
                UpdatedBy = agentId,
                UpdatedAt = now,
            };

            _files[path] = file;
            return file;
        }
    }

    public SharedFile? GetFile(string path)
    {
        lock (_gate)
        {
            return _files.TryGetValue(path, out var file) ? file : null;
        }
    }

    public bool DeleteFile(string path)
    {
        lock (_gate)
        {
            return _files.Remove(path);
        }
    }

    public IReadOnlyList<FileSummary> ListFiles()
    {
        lock (_gate)
        {
            return _files.Values
                .OrderBy(f => f.Path, StringComparer.Ordinal)
                .Select(f => new FileSummary
                {
                    Path = f.Path,
                    Bytes = f.Bytes,
                    Version = f.Version,
                    UpdatedBy = f.UpdatedBy,
                    UpdatedAt = f.UpdatedAt,
                })
                .ToArray();
        }
    }

    /// <summary>
    /// Drops every message, the roster, and the folder. The seq counter is deliberately
    /// left alone so cursors held by polling agents stay valid; the retention floor moves
    /// past the old head so those agents are told they have a gap rather than silently
    /// missing it.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _messages.Clear();
            _agents.Clear();
            _files.Clear();
            _firstAvailableSeq = _seq + 1;
            ReleaseWaiters();
        }
    }

    /// <summary>
    /// Registers a waiter for new messages. Returns false (with a completed signal) when
    /// something newer than <paramref name="since"/> already exists. The check and the
    /// registration happen under one lock, so a write cannot slip between them.
    /// </summary>
    public bool TryRegisterWaiter(long since, out Task signal)
    {
        lock (_gate)
        {
            if (_seq > since)
            {
                signal = Task.CompletedTask;
                return false;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add(waiter);
            signal = waiter.Task;
            return true;
        }
    }

    private AgentPresence[] OrderedAgents()
        => _agents.Values
            .OrderBy(a => a.FirstSeen)
            .ThenBy(a => a.AgentId, StringComparer.Ordinal)
            .ToArray();

    private void ReleaseWaiters()
    {
        foreach (var waiter in _waiters)
        {
            waiter.TrySetResult();
        }

        _waiters.Clear();
    }
}
