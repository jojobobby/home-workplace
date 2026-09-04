using System.Collections.Concurrent;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public sealed class FakeAgentProvider : IAgentProvider
{
    private readonly ConcurrentQueue<Func<RunSpec, RunResult>> _scripted = new();
    private readonly ConcurrentQueue<WrapUpResult> _wrapUps = new();
    public List<RunSpec> Specs { get; } = new();
    public List<RunSpec> ManagerSpecs { get; } = new();
    private readonly ConcurrentQueue<(ManagerDecision Decision, decimal CostUsd)> _decisions = new();

    public bool Handles(Vendor vendor) => true;

    public void EnqueueDecision(ManagerDecision decision, decimal costUsd = 0m) => _decisions.Enqueue((decision, costUsd));

    public Task<ManagerRunResult> RunManagerAsync(RunSpec spec, CancellationToken ct)
    {
        lock (ManagerSpecs) ManagerSpecs.Add(spec);
        var (decision, cost) = _decisions.TryDequeue(out var x)
            ? x
            : (new ManagerDecision("nothing to do", new[] { new ManagerAction("wait") }), 0m);
        var usage = new Usage(1, null, null, cost > 0m ? cost : null, null);
        return Task.FromResult(new ManagerRunResult(decision, usage, spec.SessionId ?? Guid.NewGuid().ToString()));
    }

    public void Enqueue(Func<RunSpec, RunResult> f) => _scripted.Enqueue(f);
    public void EnqueueDone(string summary = "done") => Enqueue(s => Done(s, summary));

    public void EnqueueHandoff(string to, string q) => Enqueue(s => new RunResult
    {
        RunId = s.RunId, Status = RunOutcome.Handoff, Summary = "asking", Ask = new HandoffAsk(to, q),
        Artifacts = Array.Empty<string>(), SessionId = s.SessionId ?? Guid.NewGuid().ToString(),
        Usage = new Usage(1, null, null, null, null), RawTail = "",
    });

    public void EnqueueWrapUp(string[] done, string[] next)
        => _wrapUps.Enqueue(new WrapUpResult(done, next, "sess-wrap"));

    public static RunResult Done(RunSpec s, string summary = "done") => new()
    {
        RunId = s.RunId, Status = RunOutcome.Done, Summary = summary, Ask = null,
        Artifacts = Array.Empty<string>(), SessionId = s.SessionId ?? Guid.NewGuid().ToString(),
        Usage = new Usage(1, null, null, null, null), RawTail = "",
    };

    public Task<RunResult> RunAsync(RunSpec spec, CancellationToken ct)
    {
        lock (Specs) Specs.Add(spec);
        var f = _scripted.TryDequeue(out var x) ? x : (s => Done(s));
        return Task.Run(() => f(spec), ct);
    }

    public Task<WrapUpResult> WrapUpAsync(RunSpec spec, CancellationToken ct)
    {
        var w = _wrapUps.TryDequeue(out var x) ? x : new WrapUpResult(Array.Empty<string>(), Array.Empty<string>(), spec.SessionId ?? "sess");
        return Task.FromResult(w);
    }
}
