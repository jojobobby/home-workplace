using System.Text.Json;

namespace HomeWorkplace.Client;

public enum CliState { NotInstalled, InstalledNotSignedIn, SignedIn }

public sealed record CliStatus(string Cli, CliState State, string? Version, string? Detail);

/// <summary>
/// The "login" screen's facts: is each CLI installed, and is it signed in? Uses the CLIs' own
/// status commands (`claude auth status`, `codex login status`); the app never holds a credential.
/// </summary>
public sealed class CliSetupChecker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly IProcessRunner _runner;

    public CliSetupChecker(IProcessRunner runner) => _runner = runner;

    public async Task<IReadOnlyList<CliStatus>> CheckAllAsync(CancellationToken ct)
        => new[] { await CheckClaudeAsync(ct), await CheckCodexAsync(ct) };

    public async Task<CliStatus> CheckClaudeAsync(CancellationToken ct)
    {
        var version = await VersionAsync("claude", ct);
        if (version is null) return new CliStatus("claude", CliState.NotInstalled, null, null);

        var status = await TryRunAsync("claude", new[] { "auth", "status" }, ct);
        if (status is null || status.ExitCode != 0)
            return new CliStatus("claude", CliState.InstalledNotSignedIn, version, status?.Stderr.Trim());

        try
        {
            using var doc = JsonDocument.Parse(status.Stdout);
            var root = doc.RootElement;
            var loggedIn = root.TryGetProperty("loggedIn", out var li) && li.ValueKind == JsonValueKind.True;
            var detail = string.Join(" · ", new[] { Str(root, "subscriptionType"), Str(root, "email") }.Where(s => s is not null));
            return new CliStatus("claude", loggedIn ? CliState.SignedIn : CliState.InstalledNotSignedIn, version, detail.Length > 0 ? detail : null);
        }
        catch (JsonException)
        {
            return new CliStatus("claude", CliState.InstalledNotSignedIn, version, status.Stdout.Trim());
        }
    }

    public async Task<CliStatus> CheckCodexAsync(CancellationToken ct)
    {
        var version = await VersionAsync("codex", ct);
        if (version is null) return new CliStatus("codex", CliState.NotInstalled, null, null);

        var status = await TryRunAsync("codex", new[] { "login", "status" }, ct);
        var signedIn = status is { ExitCode: 0 } && status.Stdout.Contains("logged in", StringComparison.OrdinalIgnoreCase);
        var detail = (status?.Stdout ?? status?.Stderr ?? "").Trim();
        return new CliStatus("codex", signedIn ? CliState.SignedIn : CliState.InstalledNotSignedIn, version, detail.Length > 0 ? detail : null);
    }

    private async Task<string?> VersionAsync(string cli, CancellationToken ct)
    {
        var r = await TryRunAsync(cli, new[] { "--version" }, ct);
        if (r is null || r.ExitCode != 0) return null;
        var first = r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(first) ? "(unknown version)" : first;
    }

    /// <summary>Null when the executable cannot be started at all (not on PATH).</summary>
    private async Task<ProcessResult?> TryRunAsync(string cli, string[] args, CancellationToken ct)
    {
        try { return await _runner.RunAsync(cli, args, Timeout, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; }
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
