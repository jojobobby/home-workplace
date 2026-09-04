namespace HomeWorkplace.Client;

public enum ServiceName { ContextApi, Foreman }

public sealed record BootProgress(ServiceName Service, string Message, bool Healthy);

public sealed record BootResult(bool Success, string? Error, IReadOnlyList<string> LastOutput);

/// <summary>
/// "The company boots": launches context-api and foreman as child processes with a scrubbed
/// environment, waits for both to answer /health, and stops on exit only what it started.
/// In connect-only mode it launches nothing and just probes.
/// </summary>
public sealed class ServiceSupervisor
{
    private readonly AppConfig _config;
    private readonly IProcessRunner _runner;
    private readonly IHealthProbe _health;
    private readonly List<(ServiceName Service, IProcessHandle Handle)> _started = new();

    public ServiceSupervisor(AppConfig config, IProcessRunner runner, IHealthProbe health)
    {
        _config = config;
        _runner = runner;
        _health = health;
    }

    public event Action<BootProgress>? Progress;

    public async Task<BootResult> StartAsync(CancellationToken ct)
    {
        if (!_config.ConnectOnly)
        {
            var env = EnvironmentScrub.Current();
            Launch(ServiceName.ContextApi, _config.ContextApi, env);
            Launch(ServiceName.Foreman, _config.Foreman, env);
        }

        var deadline = DateTime.UtcNow.AddSeconds(_config.HealthTimeoutSeconds);
        var ctxUp = false;
        var fmUp = false;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (!ctxUp && await _health.IsHealthyAsync(_config.ContextApiUrl, ct))
            {
                ctxUp = true;
                Report(ServiceName.ContextApi, "context-api is up", healthy: true);
            }
            if (!fmUp && await _health.IsHealthyAsync(_config.ForemanUrl, ct))
            {
                fmUp = true;
                Report(ServiceName.Foreman, "foreman is up", healthy: true);
            }
            if (ctxUp && fmUp) return new BootResult(true, null, Array.Empty<string>());

            await Task.Delay(Math.Max(1, _config.HealthPollMs), ct);
        }

        var failed = !ctxUp ? ServiceName.ContextApi : ServiceName.Foreman;
        var name = failed == ServiceName.ContextApi ? "context-api" : "foreman";
        var output = _started.FirstOrDefault(s => s.Service == failed).Handle?.RecentOutput ?? Array.Empty<string>();
        Report(failed, $"{name} did not become healthy within {_config.HealthTimeoutSeconds}s", healthy: false);
        Stop();
        return new BootResult(false, $"{name} did not become healthy within {_config.HealthTimeoutSeconds}s", output);
    }

    /// <summary>Stops the services this supervisor started. Never touches one it merely connected to.</summary>
    public void Stop()
    {
        foreach (var (_, handle) in _started)
        {
            try { handle.Kill(); } catch { }
            handle.Dispose();
        }
        _started.Clear();
    }

    private void Launch(ServiceName service, ServiceCommand cmd, IReadOnlyDictionary<string, string?> env)
    {
        var name = service == ServiceName.ContextApi ? "context-api" : "foreman";
        Report(service, $"starting {name}", healthy: false);
        var handle = _runner.Start(cmd.Command, cmd.Args, cmd.WorkingDirectory, env);
        _started.Add((service, handle));
    }

    private void Report(ServiceName service, string message, bool healthy)
        => Progress?.Invoke(new BootProgress(service, message, healthy));
}
