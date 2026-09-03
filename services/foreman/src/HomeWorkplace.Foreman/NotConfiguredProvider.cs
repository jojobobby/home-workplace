namespace HomeWorkplace.Foreman;

/// <summary>
/// Placeholder provider registered in production until the real claude/codex providers
/// arrive (Task 12). It handles nothing, so a run attempt fails with a clear message
/// rather than silently doing nothing. Tests replace the provider list with a fake.
/// </summary>
public sealed class NotConfiguredProvider : IAgentProvider
{
    public bool Handles(Vendor vendor) => true;

    public Task<RunResult> RunAsync(RunSpec spec, CancellationToken ct)
        => throw new InvalidOperationException(
            "No agent provider is configured yet. The claude/codex CLI providers arrive in Task 12.");

    public Task<WrapUpResult> WrapUpAsync(RunSpec spec, CancellationToken ct)
        => throw new InvalidOperationException("No agent provider is configured yet.");
}
