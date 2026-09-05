using System.Collections;

namespace HomeWorkplace.Client;

/// <summary>
/// Removes every CLAUDE* / ANTHROPIC* variable. A process that inherits them — most
/// importantly CLAUDE_CODE_CHILD_SESSION — is treated as a nested Claude Code session and is
/// refused subscription access, so anything that will (transitively) spawn `claude` must
/// start clean. Same rule Foreman applies to its own children.
/// </summary>
public static class EnvironmentScrub
{
    /// <summary>The API-key family survives the scrub: it is the sanctioned way to run headless when subscription access is refused.</summary>
    public static readonly HashSet<string> KeptForApiKeyUse = new(StringComparer.OrdinalIgnoreCase) { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL" };

    public static IReadOnlyDictionary<string, string?> Scrub(IDictionary<string, string?> source)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            if (KeptForApiKeyUse.Contains(key)) { result[key] = value; continue; }
            if (key.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase))
                continue;
            result[key] = value;
        }
        return result;
    }

    /// <summary>A scrubbed copy of this process's environment.</summary>
    public static IReadOnlyDictionary<string, string?> Current()
    {
        var source = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
            source[(string)e.Key] = e.Value?.ToString();
        return Scrub(source);
    }
}
