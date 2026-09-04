using HomeWorkplace.Client;

namespace HomeWorkplace.UI.Tests;

public sealed record StartCall(string Command, IReadOnlyList<string> Args, string? WorkingDirectory, IReadOnlyDictionary<string, string?> Environment);

public sealed class FakeHandle : IProcessHandle
{
    private static int _next = 1000;
    public int Id { get; } = Interlocked.Increment(ref _next);
    public bool HasExited { get; private set; }
    public bool Killed { get; private set; }
    public IReadOnlyList<string> RecentOutput { get; } = new[] { "fake output" };
    public void Kill() { Killed = true; HasExited = true; }
    public void Dispose() { }
}

/// <summary>Scripts RunAsync results by command name and records every Start.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, Func<IReadOnlyList<string>, ProcessResult>> _scripts = new(StringComparer.OrdinalIgnoreCase);
    public List<StartCall> Starts { get; } = new();
    public List<FakeHandle> Handles { get; } = new();
    public int RunCalls { get; private set; }

    public FakeProcessRunner Script(string command, Func<IReadOnlyList<string>, ProcessResult> respond)
    { _scripts[command] = respond; return this; }

    public FakeProcessRunner Script(string command, ProcessResult result) => Script(command, _ => result);

    public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        RunCalls++;
        if (!_scripts.TryGetValue(command, out var respond))
            throw new System.ComponentModel.Win32Exception(2, $"{command}: not found");   // what a missing exe looks like
        return Task.FromResult(respond(args));
    }

    public IProcessHandle Start(string command, IReadOnlyList<string> args, string? workingDirectory, IReadOnlyDictionary<string, string?> environment)
    {
        Starts.Add(new StartCall(command, args, workingDirectory, environment));
        var h = new FakeHandle();
        Handles.Add(h);
        return h;
    }
}

/// <summary>Scripts health per base URL: healthy after N probes (or never).</summary>
public sealed class FakeHealthProbe : IHealthProbe
{
    private readonly Dictionary<string, int> _healthyAfter = new();
    private readonly Dictionary<string, int> _probes = new();

    public FakeHealthProbe HealthyAfter(string baseUrl, int probes) { _healthyAfter[baseUrl] = probes; return this; }
    public FakeHealthProbe Never(string baseUrl) { _healthyAfter[baseUrl] = int.MaxValue; return this; }
    public int Probes(string baseUrl) => _probes.TryGetValue(baseUrl, out var n) ? n : 0;

    public Task<bool> IsHealthyAsync(string baseUrl, CancellationToken ct)
    {
        _probes[baseUrl] = Probes(baseUrl) + 1;
        var threshold = _healthyAfter.TryGetValue(baseUrl, out var t) ? t : 1;
        return Task.FromResult(_probes[baseUrl] >= threshold);
    }
}
