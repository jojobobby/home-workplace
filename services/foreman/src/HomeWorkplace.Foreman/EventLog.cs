using System.Text.Json;

namespace HomeWorkplace.Foreman;

/// <summary>
/// Append-only runtime event stream with a bounded ring, a monotonic cursor that never
/// resets while the process lives, and long-poll waiters released on emit. This is the
/// feed the office UI animates from. Same cursor/waiter contract as context-api's firehose.
/// </summary>
public sealed class EventLog
{
    private readonly ForemanOptions _options;
    private readonly TimeProvider _clock;
    private readonly FileStore? _store;
    private readonly object _gate = new();
    private readonly Queue<RuntimeEvent> _events = new();
    private readonly List<TaskCompletionSource> _waiters = new();

    private long _seq;
    private long _firstAvailableSeq = 1;

    public EventLog(ForemanOptions options, TimeProvider clock, FileStore? store = null)
    {
        _options = options;
        _clock = clock;
        _store = store;
    }

    public void Emit(string type, string? employeeId = null, string? taskId = null, string? runId = null, object? data = null)
    {
        lock (_gate)
        {
            var evt = new RuntimeEvent
            {
                Seq = ++_seq,
                Timestamp = _clock.GetUtcNow(),
                Type = type,
                EmployeeId = employeeId,
                TaskId = taskId,
                RunId = runId,
                Data = data is null ? null : JsonSerializer.SerializeToElement(data),
            };
            _events.Enqueue(evt);
            _store?.AppendEvent(evt);
            while (_events.Count > _options.EventsCapacity)
            {
                _firstAvailableSeq = _events.Dequeue().Seq + 1;
            }
            foreach (var w in _waiters) w.TrySetResult();
            _waiters.Clear();
        }
    }

    /// <summary>Replay persisted events at startup so the cursor continues above them.</summary>
    public void Seed(IReadOnlyList<RuntimeEvent> events)
    {
        lock (_gate)
        {
            foreach (var e in events) _events.Enqueue(e);
            if (events.Count > 0) _seq = events.Max(e => e.Seq);
            while (_events.Count > _options.EventsCapacity)
                _firstAvailableSeq = _events.Dequeue().Seq + 1;
        }
    }

    public EventPage Read(long since, int limit)
    {
        lock (_gate)
        {
            var events = _events.Where(e => e.Seq > since).Take(limit).ToArray();
            return new EventPage { Cursor = _seq, Events = events, Truncated = since + 1 < _firstAvailableSeq };
        }
    }

    public async Task<EventPage> ReadWithWaitAsync(long since, int limit, TimeSpan wait, CancellationToken cancellationToken)
    {
        var deadline = _clock.GetUtcNow() + wait;
        while (true)
        {
            var page = Read(since, limit);
            if (page.Events.Count > 0 || wait <= TimeSpan.Zero) return page;

            var remaining = deadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) return page;

            Task signal;
            lock (_gate)
            {
                if (_seq > since) continue;
                var w = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(w);
                signal = w.Task;
            }
            var completed = await Task.WhenAny(signal, Task.Delay(remaining, cancellationToken));
            if (completed != signal) return Read(since, limit);
        }
    }

    public IReadOnlyList<RuntimeEvent> Snapshot()
    {
        lock (_gate) { return _events.ToArray(); }
    }
}
