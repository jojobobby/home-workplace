namespace HomeWorkplace.Client;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

/// <summary>A process the supervisor started and may need to stop.</summary>
public interface IProcessHandle : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    /// <summary>The last lines the process wrote, for the boot screen when something fails.</summary>
    IReadOnlyList<string> RecentOutput { get; }
    void Kill();
}

/// <summary>
/// Everything process-shaped the app does — CLI checks, launching services — goes through
/// this seam so it is testable with a fake. The real implementation is <see cref="ProcessRunner"/>.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Run to completion and capture output. A missing executable throws; the caller decides what that means.</summary>
    Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct);

    /// <summary>Start a long-running process with exactly the given environment.</summary>
    IProcessHandle Start(string command, IReadOnlyList<string> args, string? workingDirectory, IReadOnlyDictionary<string, string?> environment);
}
