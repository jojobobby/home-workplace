using System.Collections.Concurrent;

namespace HomeWorkplace.Foreman;

/// <summary>
/// The single owner and writer of goal state. A goal is a manager-owned objective with a
/// dollar budget; every run made in its service accrues cost here. Tasks 3–4 add the
/// manager-run trigger, budget block, top-up, and cancel.
/// </summary>
public sealed class GoalBook
{
    private readonly ForemanOptions _options;
    private readonly TimeProvider _clock;
    private readonly EventLog _events;
    private readonly IContextApiClient _rooms;
    private readonly FileStore _store;
    private readonly ConcurrentDictionary<string, GoalModel> _goals = new(StringComparer.Ordinal);

    public GoalBook(ForemanOptions options, TimeProvider clock, EventLog events, IContextApiClient rooms, FileStore store)
    {
        _options = options;
        _clock = clock;
        _events = events;
        _rooms = rooms;
        _store = store;
    }

    public async Task<GoalModel> CreateAsync(CreateGoalRequest req, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var id = Guid.NewGuid().ToString("N")[..8];
        var goal = new GoalModel
        {
            Id = id,
            Title = req.Title!.Trim(),
            Brief = req.Brief!.Trim(),
            Manager = req.Manager!.Trim(),
            BudgetUsd = req.BudgetUsd,
            SpentUsd = 0m,
            Status = GoalState.Planning,
            Room = $"goal-{id}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _goals[id] = goal;
        Save(goal);
        await _rooms.PostAsync(goal.Room, "foreman", "Foreman", null,
            $"Goal created: {goal.Title} — manager {goal.Manager}, budget ${goal.BudgetUsd:0.00}", ct);
        return goal;
    }

    public GoalModel? Get(string id) => _goals.TryGetValue(id, out var g) ? g : null;

    public IReadOnlyList<GoalModel> List() => _goals.Values.OrderBy(g => g.CreatedAt).ToArray();

    /// <summary>Persist a goal and announce its state on the event stream.</summary>
    public void Save(GoalModel goal)
    {
        goal.UpdatedAt = _clock.GetUtcNow();
        _goals[goal.Id] = goal;
        _store.SaveGoal(goal);
        _events.Emit("goal.state", taskId: null, data: new { goalId = goal.Id, goal.Status, goal.SpentUsd, goal.BudgetUsd });
    }

    /// <summary>Accrue a run's dollar cost to the goal.</summary>
    public void AddCost(string goalId, decimal usd)
    {
        if (usd <= 0m || Get(goalId) is not { } g) return;
        g.SpentUsd += usd;
        Save(g);
    }

    public bool IsOverBudget(string goalId)
        => Get(goalId) is { } g && g.SpentUsd >= g.BudgetUsd;

    /// <summary>Mark that the manager must look again (a task settled, a top-up landed). Persisted.</summary>
    public void FlagAttention(string goalId)
    {
        if (Get(goalId) is not { } g || IsTerminal(g.Status) || g.NeedsManagerAttention) return;
        g.NeedsManagerAttention = true;
        Save(g);
    }

    /// <summary>A manager run has started and will see the current state; the flag is consumed.</summary>
    public void ClearAttention(string goalId)
    {
        if (Get(goalId) is not { NeedsManagerAttention: true } g) return;
        g.NeedsManagerAttention = false;
        Save(g);
    }

    private static bool IsTerminal(GoalState s) => s is GoalState.Done or GoalState.Failed or GoalState.Cancelled;

    /// <summary>Budget exhausted: stop spawning and ask a human for a top-up or a cancel. Idempotent.</summary>
    public void Block(string goalId)
    {
        if (Get(goalId) is not { } g || g.Status == GoalState.Blocked || IsTerminal(g.Status)) return;
        g.Status = GoalState.Blocked;
        Save(g);
        _events.Emit("goal.blocked", employeeId: g.Manager, data: new { goalId, g.SpentUsd, g.BudgetUsd });
        _events.Emit("human.needed", employeeId: g.Manager, data: new { goalId, reason = "budget exhausted" });
        _ = _rooms.PostAsync(g.Room, "foreman", "Foreman", null,
            $"Budget exhausted: ${g.SpentUsd:0.00} of ${g.BudgetUsd:0.00} spent. Waiting for a top-up or a cancel.", CancellationToken.None);
    }

    /// <summary>Raise the budget; a blocked goal resumes. False when the goal is finished or the amount is not positive.</summary>
    public bool TopUp(string goalId, decimal addUsd)
    {
        if (addUsd <= 0m || Get(goalId) is not { } g || IsTerminal(g.Status)) return false;
        g.BudgetUsd += addUsd;
        if (g.Status == GoalState.Blocked) g.Status = GoalState.Running;
        Save(g);
        _ = _rooms.PostAsync(g.Room, "foreman", "Foreman", null,
            $"Budget topped up by ${addUsd:0.00} to ${g.BudgetUsd:0.00}.", CancellationToken.None);
        return true;
    }

    /// <summary>Cancel the goal and every open task it created.</summary>
    public bool Cancel(string goalId, TaskBook tasks, RunSupervisor supervisor)
    {
        if (Get(goalId) is not { } g || IsTerminal(g.Status)) return false;
        g.Status = GoalState.Cancelled;
        Save(g);
        foreach (var tid in g.TaskIds) tasks.Cancel(tid, supervisor);   // no-op for tasks already finished
        _ = _rooms.PostAsync(g.Room, "foreman", "Foreman", null, "Goal cancelled.", CancellationToken.None);
        return true;
    }

    /// <summary>Load goals from disk at startup.</summary>
    public void SeedFrom(IEnumerable<GoalModel> goals)
    {
        foreach (var g in goals) _goals[g.Id] = g;
    }
}
