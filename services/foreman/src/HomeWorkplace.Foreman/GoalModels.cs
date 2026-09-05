namespace HomeWorkplace.Foreman;

public enum GoalState { Planning, Running, Blocked, Done, Failed, Cancelled }

/// <summary>$ per million tokens, input and output.</summary>
public sealed record ModelPrice(decimal In, decimal Out);

public sealed record Decision(DateTimeOffset At, string Summary);

public sealed class GoalModel
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Brief { get; set; }
    public required string Manager { get; set; }
    public decimal BudgetUsd { get; set; }
    public decimal SpentUsd { get; set; }
    public GoalState Status { get; set; } = GoalState.Planning;
    public required string Room { get; set; }
    public List<string> TaskIds { get; set; } = new();
    public List<string> PendingNotes { get; set; } = new();
    /// <summary>A settle (or top-up/approval) happened and the manager has not looked yet. Persisted, so it survives a restart.</summary>
    public bool NeedsManagerAttention { get; set; }
    public Decision? LastDecision { get; set; }
    /// <summary>Why the last manager run could not happen (an API refusal, a crash); cleared by the next run that does.</summary>
    public string? LastError { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }
    /// <summary>The board ticket this goal was made from, closed when the goal ends.</summary>
    public string? TicketId { get; set; }
    public SessionRef? Session { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record CreateGoalRequest(string? Title, string? Brief, string? Manager, decimal BudgetUsd);
public sealed record TopUpRequest(decimal AddUsd);

/// <summary>One instruction from a manager run. Only <see cref="Kind"/> is required; the rest depend on the kind.</summary>
public sealed record ManagerAction(
    string Kind,
    string? Assignee = null,
    string? Title = null,
    string? Brief = null,
    string? To = null,
    string? Text = null,
    string? Reason = null,
    string? Role = null);

public sealed record ManagerDecision(string Summary, IReadOnlyList<ManagerAction> Actions);

/// <summary>What a manager run yields: the decision, what it cost, and the session to resume.</summary>
/// <summary><paramref name="Error"/> is set when the run never produced a decision (the CLI reported an API error); the decision is then a placeholder.</summary>
public sealed record ManagerRunResult(ManagerDecision Decision, Usage Usage, string SessionId, string? Error = null);
