using System.Text.Json;

namespace HomeWorkplace.Foreman;

/// <summary>
/// Runs an employee through the `codex exec` CLI. The final message is read from the
/// --output-last-message file (reliable), and the session id is scraped from the --json
/// event stream. Confirm the session-id field via the Task 13 acceptance script; if it is
/// not found, resume falls back to a fresh session rather than crashing.
/// </summary>
public sealed class CodexCliProvider : IAgentProvider
{
    private const string ResultSchema = ClaudeResultSchema;
    private const string ClaudeResultSchema =
        """{"type":"object","properties":{"status":{"type":"string"},"summary":{"type":"string"},"ask":{"type":"object","properties":{"to":{"type":"string"},"question":{"type":"string"}}},"artifacts":{"type":"array","items":{"type":"string"}}},"required":["status","summary"]}""";

    private readonly ForemanOptions _options;

    public CodexCliProvider(ForemanOptions options) => _options = options;

    public bool Handles(Vendor vendor) => vendor == Vendor.Codex;

    public static IReadOnlyList<string> BuildArgs(RunSpec spec, string schemaFile, string outFile)
    {
        var a = new List<string>();
        if (spec.Mode == SessionMode.Resume && spec.SessionId is not null)
        { a.Add("exec"); a.Add("resume"); a.Add(spec.SessionId); }
        else
        { a.Add("exec"); }
        a.Add("-m"); a.Add(spec.Employee.Model);
        a.Add("-s"); a.Add(spec.Employee.CodexSandbox ?? "workspace-write");
        a.Add("--json");
        a.Add("--output-schema"); a.Add(schemaFile);
        a.Add("-o"); a.Add(outFile);
        return a;
    }

    public static RunResult Parse(string lastMessage, string jsonlStream, string runId, string? requestedSessionId)
    {
        var sessionId = ScrapeSessionId(jsonlStream) ?? requestedSessionId ?? Guid.NewGuid().ToString();
        try
        {
            using var inner = JsonDocument.Parse(lastMessage);
            var i = inner.RootElement;
            var status = i.GetProperty("status").GetString() ?? "failed";
            var summary = i.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
            HandoffAsk? ask = null;
            if (i.TryGetProperty("ask", out var askEl) && askEl.ValueKind == JsonValueKind.Object)
                ask = new HandoffAsk(askEl.GetProperty("to").GetString() ?? "", askEl.GetProperty("question").GetString() ?? "");
            var artifacts = i.TryGetProperty("artifacts", out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray()
                : Array.Empty<string>();

            return new RunResult
            {
                RunId = runId,
                Status = MapStatus(status),
                Summary = summary,
                Ask = ask,
                Artifacts = artifacts,
                SessionId = sessionId,
                Usage = new Usage(0, null, null, null, null),
                RawTail = Tail(lastMessage),
            };
        }
        catch (Exception ex)
        {
            return new RunResult
            {
                RunId = runId, Status = RunOutcome.Failed, Summary = "could not parse result",
                Ask = null, Artifacts = Array.Empty<string>(), SessionId = sessionId,
                Usage = new Usage(0, null, null, null, null), RawTail = Tail($"{ex.Message}\n{lastMessage}"),
            };
        }
    }

    public async Task<RunResult> RunAsync(RunSpec spec, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "foreman-runs", spec.RunId);
        Directory.CreateDirectory(dir);
        var schemaFile = Path.Combine(dir, "schema.json");
        var outFile = Path.Combine(dir, "last.txt");
        File.WriteAllText(schemaFile, ResultSchema);
        try
        {
            var args = BuildArgs(spec, schemaFile, outFile);
            var prompt = spec.SystemPrompt + "\n\n" + spec.Prompt;   // codex has no system-prompt flag
            var (_, stdout, _, timedOut) = await ProcessRunner.RunAsync(
                _options.CodexExecutable, args, spec.Workspace, prompt,
                new Dictionary<string, string?>(), spec.Timeout, ct);
            if (timedOut)
                return new RunResult { RunId = spec.RunId, Status = RunOutcome.Failed, Summary = $"timed out after {spec.Timeout.TotalMinutes} minutes", Ask = null, Artifacts = Array.Empty<string>(), SessionId = spec.SessionId ?? "", Usage = new Usage(0, null, null, null, null), RawTail = "" };
            var last = File.Exists(outFile) ? File.ReadAllText(outFile) : "{}";
            return Parse(last, stdout, spec.RunId, spec.SessionId);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    public async Task<WrapUpResult> WrapUpAsync(RunSpec spec, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "foreman-runs", spec.RunId);
        Directory.CreateDirectory(dir);
        var schemaFile = Path.Combine(dir, "wrap.json");
        var outFile = Path.Combine(dir, "last.txt");
        File.WriteAllText(schemaFile,
            """{"type":"object","properties":{"done":{"type":"array","items":{"type":"string"}},"next":{"type":"array","items":{"type":"string"}}},"required":["done","next"]}""");
        try
        {
            var args = BuildArgs(spec, schemaFile, outFile);
            var prompt = spec.SystemPrompt + "\n\n" + spec.Prompt;
            var (_, stdout, _, _) = await ProcessRunner.RunAsync(
                _options.CodexExecutable, args, spec.Workspace, prompt,
                new Dictionary<string, string?>(), spec.Timeout, ct);
            var sessionId = ScrapeSessionId(stdout) ?? spec.SessionId ?? "";
            var last = File.Exists(outFile) ? File.ReadAllText(outFile) : "{}";
            try
            {
                using var inner = JsonDocument.Parse(last);
                var done = inner.RootElement.TryGetProperty("done", out var d) ? d.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
                var next = inner.RootElement.TryGetProperty("next", out var n) ? n.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
                return new WrapUpResult(done, next, sessionId);
            }
            catch { return new WrapUpResult(Array.Empty<string>(), Array.Empty<string>(), sessionId); }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static string? ScrapeSessionId(string jsonl)
    {
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("session_id", out var s) && s.GetString() is { } v) return v;
                if (doc.RootElement.TryGetProperty("sessionId", out var s2) && s2.GetString() is { } v2) return v2;
            }
            catch { /* not a JSON line */ }
        }
        return null;
    }

    private static RunOutcome MapStatus(string s) => s.ToLowerInvariant() switch
    {
        "done" => RunOutcome.Done,
        "handoff" => RunOutcome.Handoff,
        "needs_human" => RunOutcome.NeedsHuman,
        _ => RunOutcome.Failed,
    };

    private static string Tail(string s) => s.Length <= 4096 ? s : s[^4096..];
}
