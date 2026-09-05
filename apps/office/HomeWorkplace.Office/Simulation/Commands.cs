using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Sim;

/// <summary>What the feed tells the simulation. Derived from store diffs and Foreman events.</summary>
public abstract record SimCommand;

public sealed record EmployeeAppeared(string Id, string Name, EmployeeStatus Status, string? TaskTitle) : SimCommand;
public sealed record EmployeeStatusChanged(string Id, EmployeeStatus Status, string? TaskTitle, string? WaitingOn) : SimCommand;
public sealed record EmployeeLeft(string Id) : SimCommand;
public sealed record HandoffRequested(string FromId, string ToId) : SimCommand;
public sealed record HandoffAnswered(string Id) : SimCommand;
public sealed record HumanNeeded(string Id) : SimCommand;
public sealed record RunFinished(string Id, bool Succeeded) : SimCommand;
public sealed record WrapUpWritten(string Id) : SimCommand;
/// <summary>An employee took a ticket off the board: they walk there, take it, and walk back.</summary>
public sealed record TicketClaimed(string Id) : SimCommand;
/// <summary>How many tickets are pinned right now (drives the board's look).</summary>
public sealed record TicketsChanged(int Count) : SimCommand;
