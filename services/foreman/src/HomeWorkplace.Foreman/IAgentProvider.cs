namespace HomeWorkplace.Foreman;

/// <summary>Adapter over a subscription CLI (claude / codex). Faked in tests.</summary>
public interface IAgentProvider
{
    bool Handles(Vendor vendor);
    Task<RunResult> RunAsync(RunSpec spec, CancellationToken ct);
    Task<WrapUpResult> WrapUpAsync(RunSpec spec, CancellationToken ct);

    /// <summary>A manager run: same CLI, manager prompt, decision schema instead of the worker result.</summary>
    Task<ManagerRunResult> RunManagerAsync(RunSpec spec, CancellationToken ct);
}
