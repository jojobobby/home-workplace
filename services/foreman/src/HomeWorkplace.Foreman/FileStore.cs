using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeWorkplace.Foreman;

/// <summary>Atomic per-record JSON persistence for tasks, employee state, and events, with replay on load.</summary>
public sealed class FileStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _tasks, _states;
    private readonly string _eventsFile;

    public FileStore(ForemanOptions options)
    {
        _tasks = Path.Combine(options.DataPath, "tasks");
        _states = Path.Combine(options.DataPath, "state");
        Directory.CreateDirectory(_tasks);
        Directory.CreateDirectory(_states);
        _eventsFile = Path.Combine(options.DataPath, "events.jsonl");
    }

    public void SaveTask(TaskModel t) => WriteAtomic(Path.Combine(_tasks, $"{t.Id}.json"), t);
    public void SaveState(EmployeeState s) => WriteAtomic(Path.Combine(_states, $"{s.Id}.json"), s);

    public IReadOnlyList<TaskModel> LoadTasks() => LoadAll<TaskModel>(_tasks);
    public IReadOnlyList<EmployeeState> LoadStates() => LoadAll<EmployeeState>(_states);

    public void AppendEvent(RuntimeEvent evt)
        => File.AppendAllText(_eventsFile, JsonSerializer.Serialize(evt, Json).ReplaceLineEndings("") + Environment.NewLine);

    public IReadOnlyList<RuntimeEvent> LoadEvents(int max)
    {
        if (!File.Exists(_eventsFile)) return Array.Empty<RuntimeEvent>();
        var lines = File.ReadAllLines(_eventsFile);
        var take = lines.Length > max ? lines[^max..] : lines;
        var list = new List<RuntimeEvent>(take.Length);
        foreach (var line in take)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { if (JsonSerializer.Deserialize<RuntimeEvent>(line, Json) is { } e) list.Add(e); }
            catch { /* skip a corrupt line */ }
        }
        return list;
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }

    private static IReadOnlyList<T> LoadAll<T>(string dir)
    {
        var list = new List<T>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            try { if (JsonSerializer.Deserialize<T>(File.ReadAllText(f), Json) is { } v) list.Add(v); }
            catch { /* skip a corrupt file rather than fail startup */ }
        }
        return list;
    }
}
