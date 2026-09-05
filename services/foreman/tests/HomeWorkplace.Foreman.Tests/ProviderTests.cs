using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

// Fixtures reflect Claude Code's documented --output-format json envelope (result,
// session_id, usage.input_tokens/output_tokens, total_cost_usd, num_turns, duration_ms)
// and codex's --output-last-message file. Confirm against a real CLI run via
// scripts/acceptance.ps1 (Task 13) if a field ever parses to null.
public class ProviderTests
{
    private static EmployeeDefinition Ada(Vendor v) => new()
    {
        Id = "ada", Name = "Ada", Role = "Eng", Vendor = v, Model = "m", Effort = "low",
        ClaudeAllowedTools = new[] { "Read", "Edit" }, CodexSandbox = "workspace-write",
        Schedule = new Schedule("09:00", "20:00"), MaxRunMinutes = 10, SkillsMd = "s", LifeMd = "l",
    };
    private static RunSpec Spec(EmployeeDefinition e, SessionMode mode) => new()
    {
        RunId = "r1", Employee = e, TaskId = "t1", Workspace = "/w", SystemPrompt = "SYS", Prompt = "DO IT",
        Mode = mode, SessionId = mode == SessionMode.Resume ? "sess-9" : null, Timeout = TimeSpan.FromMinutes(10),
    };
    private static string Fx(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Claude_new_run_argv_has_model_effort_tools_and_session_id()
    {
        var argv = ClaudeCliProvider.BuildArgs(Spec(Ada(Vendor.Claude), SessionMode.New), "SCHEMA", "/tmp/sys.txt");
        Assert.Contains("-p", argv);
        Assert.Contains("--model", argv); Assert.Contains("m", argv);
        Assert.Contains("--effort", argv); Assert.Contains("low", argv);
        Assert.Contains("--allowedTools", argv);
        Assert.Contains("--append-system-prompt-file", argv);
        Assert.Contains("--session-id", argv);
        Assert.DoesNotContain("--resume", argv);
    }

    [Fact]
    public void Claude_resume_run_uses_resume_not_session_id()
    {
        var argv = ClaudeCliProvider.BuildArgs(Spec(Ada(Vendor.Claude), SessionMode.Resume), "SCHEMA", "/tmp/sys.txt");
        Assert.Contains("--resume", argv); Assert.Contains("sess-9", argv);
        Assert.DoesNotContain("--session-id", argv);
    }

    [Fact]
    public void Codex_argv_has_model_and_sandbox()
    {
        var argv = CodexCliProvider.BuildArgs(Spec(Ada(Vendor.Codex), SessionMode.New), "/tmp/schema.json", "/tmp/out.txt");
        Assert.Contains("exec", argv);
        Assert.Contains("-m", argv); Assert.Contains("-s", argv); Assert.Contains("workspace-write", argv);
        Assert.Contains("--json", argv);
    }

    [Fact]
    public void The_environment_scrub_removes_claude_and_anthropic_variables()
    {
        var src = new Dictionary<string, string?> { ["PATH"] = "x", ["CLAUDECODE"] = "1", ["CLAUDE_CODE_CHILD_SESSION"] = "1", ["ANTHROPIC_LOG"] = "y", ["HOME"] = "h" };
        var scrubbed = ProcessRunner.Scrub(src);
        Assert.True(scrubbed.ContainsKey("PATH")); Assert.True(scrubbed.ContainsKey("HOME"));
        Assert.DoesNotContain(scrubbed.Keys, k => k.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase) || k.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Claude_fixture_parses_into_a_run_result()
    {
        var json = File.ReadAllText(Fx("claude-run.json"));
        var result = ClaudeCliProvider.Parse(json, runId: "r1", requestedSessionId: null);
        Assert.Equal(RunOutcome.Done, result.Status);
        Assert.Equal("hello from claude", result.Summary);
        Assert.Equal("claude-sess-abc", result.SessionId);
        Assert.Equal(100, result.Usage.InputTokens);
    }

    [Fact]
    public void Codex_fixture_parses_last_message_and_scrapes_session_id()
    {
        var last = File.ReadAllText(Fx("codex-last.txt"));
        var stream = File.ReadAllText(Fx("codex-stream.jsonl"));
        var result = CodexCliProvider.Parse(last, stream, runId: "r1", requestedSessionId: null);
        Assert.Equal(RunOutcome.Done, result.Status);
        Assert.Equal("hello from codex", result.Summary);
        Assert.Equal("codex-sess-xyz", result.SessionId);
    }
}

public class ProviderErrorTests
{
    private const string Error403 = """{"type":"result","subtype":"success","is_error":true,"duration_ms":322,"num_turns":1,"session_id":"848a42d4","total_cost_usd":0,"usage":{"input_tokens":0,"output_tokens":0},"api_error_status":403,"result":"Your organization has disabled Claude subscription access for Claude Code · Use an Anthropic API key instead, or ask your admin to enable access"}""";

    [Fact]
    public void A_claude_api_error_envelope_is_a_failed_run_whose_summary_is_the_error()
    {
        var result = ClaudeCliProvider.Parse(Error403, runId: "r1", requestedSessionId: null);
        Assert.Equal(RunOutcome.Failed, result.Status);
        Assert.Contains("organization has disabled", result.Summary);
        Assert.Equal("848a42d4", result.SessionId);
    }

    [Fact]
    public void A_claude_api_error_envelope_is_a_manager_error_not_a_decision()
    {
        var result = ClaudeCliProvider.ParseManager(Error403, requestedSessionId: null);
        Assert.NotNull(result.Error);
        Assert.Contains("organization has disabled", result.Error);
        Assert.Equal("wait", Assert.Single(result.Decision.Actions).Kind);
    }

    [Fact]
    public void The_environment_scrub_keeps_api_key_variables_but_drops_session_markers()
    {
        var src = new Dictionary<string, string?> { ["ANTHROPIC_API_KEY"] = "sk", ["ANTHROPIC_AUTH_TOKEN"] = "t", ["ANTHROPIC_BASE_URL"] = "u", ["ANTHROPIC_LOG"] = "debug", ["CLAUDECODE"] = "1", ["CLAUDE_CODE_CHILD_SESSION"] = "1" };
        var scrubbed = ProcessRunner.Scrub(src);
        Assert.Equal("sk", scrubbed["ANTHROPIC_API_KEY"]);
        Assert.Equal("t", scrubbed["ANTHROPIC_AUTH_TOKEN"]);
        Assert.Equal("u", scrubbed["ANTHROPIC_BASE_URL"]);
        Assert.False(scrubbed.ContainsKey("ANTHROPIC_LOG"));
        Assert.False(scrubbed.ContainsKey("CLAUDECODE"));
        Assert.False(scrubbed.ContainsKey("CLAUDE_CODE_CHILD_SESSION"));
    }
}
