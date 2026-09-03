namespace HomeWorkplace.Foreman;

/// <summary>Adapter over a subscription CLI (claude / codex). Faked in tests.</summary>
public interface IAgentProvider
{
    bool Handles(Vendor vendor);
    Task<RunResult> RunAsync(RunSpec spec, CancellationToken ct);
    Task<WrapUpResult> WrapUpAsync(RunSpec spec, CancellationToken ct);
}
