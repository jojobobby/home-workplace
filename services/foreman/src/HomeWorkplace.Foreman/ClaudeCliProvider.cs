using System.Text.Json;

namespace HomeWorkplace.Foreman;

/// <summary>
/// Runs an employee through the `claude` CLI in print mode. The result envelope fields
/// (result, session_id, usage, total_cost_usd, num_turns, duration_ms) are Claude Code's
/// documented --output-format json schema; confirm against your CLI version via the Task 13
/// acceptance script if a field ever comes back null.
/// </summary>
public sealed class ClaudeCliProvider : IAgentProvider
{
    private const string ResultSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["done","handoff","needs_human","failed"]},"summary":{"type":"string"},"ask":{"type":"object","properties":{"to":{"type":"string"},"question":{"type":"string"}}},"artifacts":{"type":"array","items":{"type":"string"}}},"required":["status","summary"]}""";

    private const string WrapUpSchema =
        """{"type":"object","properties":{"done":{"type":"array","items":{"type":"string"}},"next":{"type":"array","items":{"type":"string"}}},"required":["done","next"]}""";

    private readonly ForemanOptions _options;

    public ClaudeCliProvider(ForemanOptions options) => _options = options;

    public bool Handles(Vendor vendor) => vendor == Vendor.Claude;

    public static IReadOnlyList<string> BuildArgs(RunSpec spec, string schema, string systemFile)
    {
        var a = new List<string> { "-p", "--model", spec.Employee.Model };
        if (!string.IsNullOrWhiteSpace(spec.Employee.Effort)) { a.Add("--effort"); a.Add(spec.Employee.Effort!); }
        if (spec.Employee.ClaudeAllowedTools.Count > 0) { a.Add("--allowedTools"); a.Add(string.Join(' ', spec.Employee.ClaudeAllowedTools)); }
        a.Add("--append-system-prompt-file"); a.Add(systemFile);
        a.Add("--output-format"); a.Add("json");
        a.Add("--json-schema"); a.Add(schema);
        if (spec.Mode == SessionMode.Resume && spec.SessionId is not null) { a.Add("--resume"); a.Add(spec.SessionId); }
        else { a.Add("--session-id"); a.Add(spec.SessionId ?? Guid.NewGuid().ToString()); }
        return a;
    }

    public static RunResult Parse(string json, string runId, string? requestedSessionId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sessionId = root.TryGetProperty("session_id", out var sid) ? sid.GetString() ?? requestedSessionId ?? "" : requestedSessionId ?? "";
            var usage = ParseUsage(root);
            if (IsError(root, out var message))
                return Failed(runId, sessionId, usage, root.ToString(), message);

            var resultText = root.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "";
            using var inner = JsonDocument.Parse(resultText);
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
                Usage = usage,
                RawTail = Tail(json),
            };
        }
        catch (Exception ex)
        {
            return Failed(runId, requestedSessionId ?? "", new Usage(0, null, null, null, null), $"{ex.Message}\n{Tail(json)}");
        }
    }

    public async Task<RunResult> RunAsync(RunSpec spec, CancellationToken ct)
    {
        var tmp = CreateTemp(spec, out var systemFile);
        try
        {
            var args = BuildArgs(spec, ResultSchema, systemFile);
            var (_, stdout, _, timedOut) = await ProcessRunner.RunAsync(
                _options.ClaudeExecutable, args, spec.Workspace, spec.Prompt,
                new Dictionary<string, string?>(), spec.Timeout, ct);
            if (timedOut)
                return Failed(spec.RunId, spec.SessionId ?? "", new Usage(0, null, null, null, null), $"timed out after {spec.Timeout.TotalMinutes} minutes");
            return Parse(stdout, spec.RunId, spec.SessionId);
        }
        finally { TryDelete(tmp); }
    }

    public async Task<WrapUpResult> WrapUpAsync(RunSpec spec, CancellationToken ct)
    {
        var tmp = CreateTemp(spec, out var systemFile);
        try
        {
            var args = BuildArgs(spec, WrapUpSchema, systemFile);
            var (_, stdout, _, _) = await ProcessRunner.RunAsync(
                _options.ClaudeExecutable, args, spec.Workspace, spec.Prompt,
                new Dictionary<string, string?>(), spec.Timeout, ct);
            try
            {
                using var doc = JsonDocument.Parse(stdout);
                var resultText = doc.RootElement.TryGetProperty("result", out var r) ? r.GetString() ?? "{}" : "{}";
                using var inner = JsonDocument.Parse(resultText);
                var done = inner.RootElement.TryGetProperty("done", out var d) ? d.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
                var next = inner.RootElement.TryGetProperty("next", out var n) ? n.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
                var sid = doc.RootElement.TryGetProperty("session_id", out var s) ? s.GetString() ?? spec.SessionId ?? "" : spec.SessionId ?? "";
                return new WrapUpResult(done, next, sid);
            }
            catch { return new WrapUpResult(Array.Empty<string>(), Array.Empty<string>(), spec.SessionId ?? ""); }
        }
        finally { TryDelete(tmp); }
    }

    public async Task<ManagerRunResult> RunManagerAsync(RunSpec spec, CancellationToken ct)
    {
        var tmp = CreateTemp(spec, out var systemFile);
        try
        {
            var args = BuildArgs(spec, ManagerActions.Schema, systemFile);
            var (_, stdout, _, timedOut) = await ProcessRunner.RunAsync(
                _options.ClaudeExecutable, args, spec.Workspace, spec.Prompt,
                new Dictionary<string, string?>(), spec.Timeout, ct);
            if (timedOut)
                return new ManagerRunResult(
                    new ManagerDecision($"manager run timed out after {spec.Timeout.TotalMinutes} minutes", new[] { new ManagerAction("wait") }),
                    new Usage(0, null, null, null, null), spec.SessionId ?? "");
            return ParseManager(stdout, spec.SessionId);
        }
        finally { TryDelete(tmp); }
    }

    /// <summary>A manager envelope: an API error becomes <see cref="ManagerRunResult.Error"/> with a placeholder wait decision.</summary>
    public static ManagerRunResult ParseManager(string json, string? requestedSessionId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sessionId = root.TryGetProperty("session_id", out var sid) ? sid.GetString() ?? requestedSessionId ?? "" : requestedSessionId ?? "";
            var usage = ParseUsage(root);
            if (IsError(root, out var message))
                return new ManagerRunResult(new ManagerDecision(message, new[] { new ManagerAction("wait") }), usage, sessionId, Error: message);
            var resultText = root.TryGetProperty("result", out var r) ? r.GetString() ?? "{}" : "{}";
            return new ManagerRunResult(ManagerActions.Parse(resultText), usage, sessionId);
        }
        catch (Exception ex)
        {
            return new ManagerRunResult(ManagerActions.Parse($"<<{ex.Message}>> {Tail(json)}"),
                new Usage(0, null, null, null, null), requestedSessionId ?? "");
        }
    }

    /// <summary>True when the envelope reports an API-level error; <paramref name="message"/> is what the CLI printed as its result.</summary>
    public static bool IsError(JsonElement root, out string message)
    {
        message = "";
        if (!(root.TryGetProperty("is_error", out var err) && err.ValueKind == JsonValueKind.True)) return false;
        message = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
        if (root.TryGetProperty("api_error_status", out var status) && status.TryGetInt32(out var code)) message = $"{message} (HTTP {code})".Trim();
        if (message.Length == 0) message = "the claude CLI reported an error";
        if (message.Length > 400) message = message[..400];
        return true;
    }

    private static Usage ParseUsage(JsonElement root)
    {
        long dur = root.TryGetProperty("duration_ms", out var d) && d.TryGetInt64(out var dv) ? dv : 0;
        long? inTok = root.TryGetProperty("usage", out var u) && u.TryGetProperty("input_tokens", out var it) && it.TryGetInt64(out var iv) ? iv : null;
        long? outTok = root.TryGetProperty("usage", out var u2) && u2.TryGetProperty("output_tokens", out var ot) && ot.TryGetInt64(out var ov) ? ov : null;
        decimal? cost = root.TryGetProperty("total_cost_usd", out var c) && c.TryGetDecimal(out var cv) ? cv : null;
        int? turns = root.TryGetProperty("num_turns", out var t) && t.TryGetInt32(out var tv) ? tv : null;
        return new Usage(dur, inTok, outTok, cost, turns);
    }

    private static RunOutcome MapStatus(string s) => s.ToLowerInvariant() switch
    {
        "done" => RunOutcome.Done,
        "handoff" => RunOutcome.Handoff,
        "needs_human" => RunOutcome.NeedsHuman,
        _ => RunOutcome.Failed,
    };

    private static RunResult Failed(string runId, string sessionId, Usage usage, string tail, string? summary = null) => new()
    {
        RunId = runId, Status = RunOutcome.Failed, Summary = summary ?? "run failed", Ask = null,
        Artifacts = Array.Empty<string>(), SessionId = sessionId, Usage = usage, RawTail = Tail(tail),
    };

    private static string Tail(string s) => s.Length <= 4096 ? s : s[^4096..];

    private static string CreateTemp(RunSpec spec, out string systemFile)
    {
        var dir = Path.Combine(Path.GetTempPath(), "foreman-runs", spec.RunId);
        Directory.CreateDirectory(dir);
        systemFile = Path.Combine(dir, "system.txt");
        File.WriteAllText(systemFile, spec.SystemPrompt);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
}
