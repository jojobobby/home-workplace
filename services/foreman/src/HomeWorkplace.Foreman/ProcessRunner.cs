using System.Diagnostics;
using System.Text;

namespace HomeWorkplace.Foreman;

/// <summary>
/// Launches a child process with a SCRUBBED environment — every CLAUDE*, CLAUDECODE*, and
/// ANTHROPIC* variable is removed so a spawned `claude`/`codex` is not treated as a nested
/// session and refused subscription access. Captures stdio and enforces a wall-clock timeout.
/// </summary>
public static class ProcessRunner
{
    public static IDictionary<string, string?> Scrub(IDictionary<string, string?> source)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in source)
        {
            if (k.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase))
                continue;
            result[k] = v;
        }
        return result;
    }

    public static async Task<(int ExitCode, string Stdout, string Stderr, bool TimedOut)> RunAsync(
        string exe, IReadOnlyList<string> args, string workingDir, string stdin,
        IReadOnlyDictionary<string, string?> extraEnv, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = workingDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        psi.Environment.Clear();
        var parent = Environment.GetEnvironmentVariables();
        var scrubbed = Scrub(parent.Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value?.ToString(), StringComparer.OrdinalIgnoreCase));
        foreach (var (k, v) in scrubbed) psi.Environment[k] = v;
        foreach (var (k, v) in extraEnv) psi.Environment[k] = v;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return (process.ExitCode, stdout.ToString(), stderr.ToString(), false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, stdout.ToString(), stderr.ToString(), true);
        }
    }
}
