namespace HomeWorkplace.Foreman;

/// <summary>The manager-run path: same providers and one-run-per-employee latch, manager prompt and decision schema.</summary>
public sealed partial class RunSupervisor
{
    /// <summary>
    /// Run the goal's manager once: build its prompt from goal/roster/children/budget, get a
    /// decision, accrue the run's cost, execute the actions. Skips silently when the manager is
    /// busy, asleep, or the goal is terminal or over budget — a skipped run is retried by
    /// PumpGoals() when the manager next frees up.
    /// </summary>
    public async Task RunManagerAsync(string goalId)
    {
        var goal = _goals.Get(goalId);
        if (goal is null || goal.Status is GoalState.Done or GoalState.Failed or GoalState.Cancelled or GoalState.Blocked) return;
        var def = _employees.Find(goal.Manager);
        if (def is null) return;
        if (_goals.IsOverBudget(goalId)) return;

        lock (_gate)
        {
            if (_busy.Contains(goal.Manager)) return;
            if (_employees.GetState(goal.Manager).Status != EmployeeStatus.Awake) return;
            _busy.Add(goal.Manager);
        }
        _employees.MarkWorking(goal.Manager, goalId);

        var runId = Guid.NewGuid().ToString("N")[..8];
        try
        {
            var today = DateOnly.FromDateTime(_clock.GetLocalNow().Date);
            var resume = goal.Session is { } s && s.Day == today;
            var workspace = Path.Combine(_options.DataPath, "workspaces", goalId);
            Directory.CreateDirectory(workspace);
            var children = _tasks.List(null, null).Where(t => t.GoalId == goalId).ToArray();

            var spec = new RunSpec
            {
                RunId = runId,
                Employee = def,
                TaskId = goalId,
                Workspace = workspace,
                SystemPrompt = _managerComposer.BuildSystemPrompt(def, goal),
                Prompt = _managerComposer.BuildRunPrompt(goal, _employees.List(), children),
                Mode = resume ? SessionMode.Resume : SessionMode.New,
                SessionId = resume ? goal.Session!.SessionId : null,
                Timeout = TimeSpan.FromMinutes(def.MaxRunMinutes ?? _options.MaxRunMinutes),
            };
            goal.PendingNotes.Clear();   // surfaced in this prompt; do not repeat
            _events.Emit("run.started", goal.Manager, goalId, runId, new { manager = true });

            var provider = _providers.First(p => p.Handles(def.Vendor));
            var result = await provider.RunManagerAsync(spec, CancellationToken.None);

            goal.Session = new SessionRef(def.Vendor.ToString(), result.SessionId, today);
            _goals.AddCost(goalId, Cost.Of(result.Usage, def.Model, _options.Pricing));

            await ManagerActions.ExecuteAsync(goal, result.Decision, _tasks, _goals, _employees, _rooms, _options, this,
                _clock.GetUtcNow(), CancellationToken.None);

            _events.Emit("goal.decision", goal.Manager, goalId, runId,
                new { result.Decision.Summary, actions = result.Decision.Actions.Select(a => a.Kind).ToArray() });
        }
        catch (Exception ex)
        {
            _events.Emit("run.finished", goal.Manager, goalId, runId, new { status = "Failed", summary = ex.Message, manager = true });
        }
        finally
        {
            _employees.Free(goal.Manager);
            lock (_gate) _busy.Remove(goal.Manager);
            Pump();
            PumpGoals();
        }
    }

    /// <summary>Start a manager run for every goal still in Planning whose manager can take it.</summary>
    public void PumpGoals()
    {
        foreach (var g in _goals.List().Where(g => g.Status == GoalState.Planning))
            _ = RunManagerAsync(g.Id);
    }
}
