namespace HomeWorkplace.Foreman;

/// <summary>
/// Drives runs: at most one live run per employee. Pump() starts eligible queued tasks;
/// each run applies its result to the task, frees or parks the employee, then pumps again.
/// </summary>
public sealed partial class RunSupervisor
{
    private readonly TaskBook _tasks;
    private readonly GoalBook _goals;
    private readonly EmployeeCatalog _employees;
    private readonly PersonaComposer _composer;
    private readonly ManagerComposer _managerComposer;
    private readonly IEnumerable<IAgentProvider> _providers;
    private readonly IContextApiClient _rooms;
    private readonly EventLog _events;
    private readonly ForemanOptions _options;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _cancelled = new(StringComparer.Ordinal);

    /// <summary>Discard the result of an in-flight run (used by task cancel/reassign).</summary>
    public void MarkCancelled(string runId) => _cancelled[runId] = 1;

    public RunSupervisor(TaskBook tasks, GoalBook goals, EmployeeCatalog employees, PersonaComposer composer,
        ManagerComposer managerComposer, IEnumerable<IAgentProvider> providers, IContextApiClient rooms,
        EventLog events, ForemanOptions options, TimeProvider clock)
    {
        _tasks = tasks;
        _goals = goals;
        _employees = employees;
        _composer = composer;
        _managerComposer = managerComposer;
        _providers = providers;
        _rooms = rooms;
        _events = events;
        _options = options;
        _clock = clock;
    }

    public bool IsBusy(string employeeId)
    {
        lock (_gate) return _busy.Contains(employeeId);
    }

    /// <summary>End-of-day wrap-up: summarize the employee's active task into progress bullets, then drop the session.</summary>
    public async Task WrapUpAsync(string employeeId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().Date);
        if (_tasks.ActiveToday(employeeId, today) is not { } task) return;
        var def = _employees.Find(employeeId);
        if (def is null) return;
        var provider = _providers.First(p => p.Handles(def.Vendor));
        var spec = new RunSpec
        {
            RunId = Guid.NewGuid().ToString("N")[..8],
            Employee = def,
            TaskId = task.Id,
            Workspace = task.Workspace,
            SystemPrompt = _composer.BuildSystemPrompt(def, task),
            Prompt = _composer.BuildWrapUpPrompt(task),
            Mode = SessionMode.Resume,
            SessionId = task.Session!.SessionId,
            Timeout = TimeSpan.FromMinutes(def.MaxRunMinutes ?? _options.MaxRunMinutes),
        };
        var result = await provider.WrapUpAsync(spec, ct);
        _tasks.WriteProgressAndClearSession(task.Id, new ProgressEntry(employeeId, today, result.Done, result.Next));
        _events.Emit("wrapup.written", employeeId, task.Id, data: new { result.Done, result.Next });
    }

    public void Pump()
    {
        lock (_gate)
        {
            foreach (var task in _tasks.Queued())
            {
                if (_busy.Contains(task.Assignee)) continue;
                if (_employees.GetState(task.Assignee).Status != EmployeeStatus.Awake) continue;
                // A goal task must not spend past its goal's budget: block the goal, leave the task queued.
                if (task.GoalId is { } gid && _goals.IsOverBudget(gid)) { _goals.Block(gid); continue; }
                _busy.Add(task.Assignee);
                _employees.MarkWorking(task.Assignee, task.Id);
                _ = RunAsync(task.Id, task.Assignee);
            }
        }
    }

    private async Task RunAsync(string taskId, string employeeId)
    {
        var runId = Guid.NewGuid().ToString("N")[..8];
        try
        {
            var def = _employees.Find(employeeId)!;
            var task = _tasks.Get(taskId)!;
            var provider = _providers.First(p => p.Handles(def.Vendor));
            var spec = new RunSpec
            {
                RunId = runId,
                Employee = def,
                TaskId = taskId,
                Workspace = task.Workspace,
                SystemPrompt = _composer.BuildSystemPrompt(def, task),
                Prompt = await _composer.BuildRunPromptAsync(def, task, CancellationToken.None),
                Mode = task.Session is null ? SessionMode.New : SessionMode.Resume,
                SessionId = task.Session?.SessionId,
                Timeout = TimeSpan.FromMinutes(def.MaxRunMinutes ?? _options.MaxRunMinutes),
            };
            _tasks.MarkRunning(taskId, runId, employeeId, _clock.GetUtcNow());
            _events.Emit("run.started", employeeId, taskId, runId);

            var result = await provider.RunAsync(spec, CancellationToken.None);

            if (_cancelled.TryRemove(runId, out _)) return;   // task was cancelled/reassigned mid-run

            _tasks.ApplyResult(taskId, employeeId, runId, result, _clock.GetUtcNow());
            _events.Emit("run.finished", employeeId, taskId, runId, new { status = result.Status.ToString(), result.Summary });

            // Goal bookkeeping: the run's dollars accrue to the goal, and a settled task
            // (done or failed) is the manager's cue to look again.
            if (_tasks.Get(taskId) is { GoalId: { } goalId } settled)
            {
                _goals.AddCost(goalId, Cost.Of(result.Usage, def.Model, _options.Pricing));
                if (settled.Status is TaskState.Done or TaskState.Failed) RequestManagerRun(goalId);
            }
        }
        catch (Exception ex)
        {
            _tasks.FailRun(taskId, runId, ex.Message, _clock.GetUtcNow());
            _events.Emit("run.finished", employeeId, taskId, runId, new { status = "Failed", summary = ex.Message });
        }
        finally
        {
            lock (_gate) _busy.Remove(employeeId);
            Pump();
        }
    }
}
