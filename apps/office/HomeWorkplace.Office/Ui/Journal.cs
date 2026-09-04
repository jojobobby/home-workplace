namespace HomeWorkplace.Office.Ui;

public sealed record JournalEntry(DateTimeOffset At, string Text);

/// <summary>What you did in the office, most recent last. Shown in Activity beside Foreman's events.</summary>
public sealed class Journal
{
    public const int Max = 50;

    private readonly List<JournalEntry> _entries = new();
    private readonly TimeProvider _clock;

    public Journal(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public IReadOnlyList<JournalEntry> Entries => _entries;

    public void Add(string text)
    {
        _entries.Add(new JournalEntry(_clock.GetUtcNow(), text));
        while (_entries.Count > Max) _entries.RemoveAt(0);
    }
}
