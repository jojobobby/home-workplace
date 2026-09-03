using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeWorkplace.Foreman;

/// <summary>
/// Loads employees from folder definitions (employee.json + skills.md + life.md), holds
/// their runtime state in memory, and reloads on demand. A malformed definition is skipped
/// with a catalog.error event rather than crashing startup.
/// </summary>
public sealed class EmployeeCatalog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ForemanOptions _options;
    private readonly EventLog _events;
    private readonly TimeProvider _clock;
    private readonly FileStore _store;
    private readonly ConcurrentDictionary<string, EmployeeDefinition> _defs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EmployeeState> _states = new(StringComparer.Ordinal);

    public EmployeeCatalog(ForemanOptions options, EventLog events, TimeProvider clock, FileStore store)
    {
        _options = options;
        _events = events;
        _clock = clock;
        _store = store;
        Load();
    }

    public void Load()
    {
        _defs.Clear();
        if (!Directory.Exists(_options.EmployeesPath))
        {
            _events.Emit("catalog.reloaded", data: new { count = 0, path = _options.EmployeesPath });
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(_options.EmployeesPath))
        {
            var jsonPath = Path.Combine(dir, "employee.json");
            if (!File.Exists(jsonPath)) continue;
            try
            {
                var def = JsonSerializer.Deserialize<EmployeeDefinition>(File.ReadAllText(jsonPath), Json)
                          ?? throw new JsonException("employee.json deserialized to null");
                def = def with
                {
                    SkillsMd = ReadSibling(dir, "skills.md"),
                    LifeMd = ReadSibling(dir, "life.md"),
                };
                _defs[def.Id] = def;
                _states.TryAdd(def.Id, EmployeeState.Initial(def.Id));
            }
            catch (Exception ex)
            {
                _events.Emit("catalog.error", data: new { folder = dir, error = ex.Message });
            }
        }

        _events.Emit("catalog.reloaded", data: new { count = _defs.Count });
    }

    public IReadOnlyList<EmployeeDefinition> Definitions => _defs.Values.ToArray();

    public EmployeeDefinition? Find(string id) => _defs.TryGetValue(id, out var d) ? d : null;

    public EmployeeState GetState(string id) => _states.GetOrAdd(id, EmployeeState.Initial);

    public void SetState(EmployeeState state)
    {
        _states[state.Id] = state;
        _store.SaveState(state);
        _events.Emit("employee.state", employeeId: state.Id,
            data: new { state.Status, state.CurrentTaskId, state.RunsToday });
    }

    /// <summary>Overlay persisted states at startup (restart recovery).</summary>
    public void SeedStates(IEnumerable<EmployeeState> states)
    {
        foreach (var s in states) _states[s.Id] = s;
    }

    public void MarkWorking(string id, string taskId)
        => SetState(GetState(id) with { Status = EmployeeStatus.Working, CurrentTaskId = taskId });

    public void MarkWaiting(string id)
        => SetState(GetState(id) with { Status = EmployeeStatus.Waiting });

    /// <summary>A run finished: back to Awake, task cleared, a run counted toward the day.</summary>
    public void Free(string id)
    {
        var s = GetState(id);
        SetState(s with
        {
            Status = EmployeeStatus.Awake,
            CurrentTaskId = null,
            RunsToday = s.RunsToday + 1,
            LastRunAt = _clock.GetUtcNow(),
        });
    }

    public void Wake(string id, DateTimeOffset? until)
        => SetState(GetState(id) with { Status = EmployeeStatus.Awake, AwakeOverrideUntil = until });

    public IReadOnlyList<EmployeeView> List()
        => _defs.Values
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .Select(d => ToView(d, GetState(d.Id)))
            .ToArray();

    public EmployeeView? View(string id)
        => Find(id) is { } d ? ToView(d, GetState(id)) : null;

    private static EmployeeView ToView(EmployeeDefinition d, EmployeeState s) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Role = d.Role,
        Vendor = d.Vendor,
        Model = d.Model,
        Status = s.Status,
        CurrentTaskId = s.CurrentTaskId,
        RunsToday = s.RunsToday,
        Energy = Math.Max(0, 100 - 10 * s.RunsToday),
    };

    private static string ReadSibling(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
