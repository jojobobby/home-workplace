namespace AgencyTogether.Api;

public sealed class Room
{
    private readonly object _gate = new();
    private readonly List<ChatMessage> _messages = new();
    private readonly Dictionary<string, AgentPresence> _agents = new(StringComparer.Ordinal);

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

            var agents = _agents.Values
                .OrderBy(a => a.FirstSeen)
                .ThenBy(a => a.AgentId, StringComparer.Ordinal)
                .ToArray();

            return new RoomSnapshot(_seq, messages, agents, since + 1 < _firstAvailableSeq);
        }
    }
}
