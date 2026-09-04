using HomeWorkplace.Client;

namespace HomeWorkplace.UI.Tests;

public class SetupCheckerTests
{
    private static ProcessResult Ok(string stdout) => new(0, stdout, "", false);
    private static ProcessResult Fail(string stderr = "") => new(1, "", stderr, false);

    [Fact]
    public async Task Claude_missing_executable_is_NotInstalled()
    {
        var checker = new CliSetupChecker(new FakeProcessRunner());   // nothing scripted → "not found"
        var s = await checker.CheckClaudeAsync(CancellationToken.None);
        Assert.Equal(CliState.NotInstalled, s.State);
    }

    [Fact]
    public async Task Claude_installed_but_not_signed_in()
    {
        var runner = new FakeProcessRunner().Script("claude", args =>
            args[0] == "--version" ? Ok("2.1.241 (Claude Code)") : Ok("""{"loggedIn": false}"""));
        var s = await new CliSetupChecker(runner).CheckClaudeAsync(CancellationToken.None);
        Assert.Equal(CliState.InstalledNotSignedIn, s.State);
        Assert.Equal("2.1.241 (Claude Code)", s.Version);
    }

    [Fact]
    public async Task Claude_signed_in_reads_the_auth_status_json()
    {
        var runner = new FakeProcessRunner().Script("claude", args =>
            args[0] == "--version" ? Ok("2.1.241 (Claude Code)")
            : Ok("""{"loggedIn": true, "authMethod": "claude.ai", "subscriptionType": "max", "email": "x@y"}"""));
        var s = await new CliSetupChecker(runner).CheckClaudeAsync(CancellationToken.None);
        Assert.Equal(CliState.SignedIn, s.State);
        Assert.Contains("max", s.Detail);
    }

    [Fact]
    public async Task Codex_signed_in_reads_login_status_text()
    {
        var runner = new FakeProcessRunner().Script("codex", args =>
            args[0] == "--version" ? Ok("codex-cli 0.139.0") : Ok("Logged in using ChatGPT"));
        var s = await new CliSetupChecker(runner).CheckCodexAsync(CancellationToken.None);
        Assert.Equal(CliState.SignedIn, s.State);
        Assert.Equal("codex-cli 0.139.0", s.Version);
    }

    [Fact]
    public async Task Codex_installed_but_login_status_fails_is_not_signed_in()
    {
        var runner = new FakeProcessRunner().Script("codex", args =>
            args[0] == "--version" ? Ok("codex-cli 0.139.0") : Fail("Not logged in"));
        var s = await new CliSetupChecker(runner).CheckCodexAsync(CancellationToken.None);
        Assert.Equal(CliState.InstalledNotSignedIn, s.State);
    }

    [Fact]
    public async Task CheckAll_returns_both_in_order()
    {
        var runner = new FakeProcessRunner()
            .Script("claude", _ => Ok("""{"loggedIn": true}"""))
            .Script("codex", _ => Ok("Logged in using ChatGPT"));
        var all = await new CliSetupChecker(runner).CheckAllAsync(CancellationToken.None);
        Assert.Equal(new[] { "claude", "codex" }, all.Select(s => s.Cli));
    }
}
