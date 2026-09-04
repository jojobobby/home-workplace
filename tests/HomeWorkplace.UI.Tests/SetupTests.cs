using Bunit;
using HomeWorkplace.Client;
using HomeWorkplace.UI.Screens;
using Microsoft.Extensions.DependencyInjection;

namespace HomeWorkplace.UI.Tests;

public class SetupTests : TestContext
{
    private static ProcessResult Ok(string s) => new(0, s, "", false);

    private (FakeProcessRunner Runner, FakeTerminalLauncher Terminal) Wire(Action<FakeProcessRunner> script)
    {
        var runner = new FakeProcessRunner();
        script(runner);
        var terminal = new FakeTerminalLauncher();
        Services.AddSingleton(new CliSetupChecker(runner));
        Services.AddSingleton<ITerminalLauncher>(terminal);
        return (runner, terminal);
    }

    [Fact]
    public void Renders_a_card_per_cli_with_its_state()
    {
        Wire(r => r.Script("claude", a => a[0] == "--version" ? Ok("2.1.241 (Claude Code)") : Ok("""{"loggedIn": true, "subscriptionType": "max"}""")));
        // codex: not scripted → not installed
        var cut = RenderComponent<Setup>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Signed in", cut.Find("[data-cli=claude] .state").TextContent);
            Assert.Contains("Not installed", cut.Find("[data-cli=codex] .state").TextContent);
        });
    }

    [Fact]
    public void Sign_in_opens_the_cli_login_in_a_terminal()
    {
        var (_, terminal) = Wire(r => r.Script("codex", a => a[0] == "--version" ? Ok("codex-cli 0.139.0") : new ProcessResult(1, "", "not logged in", false)));
        var cut = RenderComponent<Setup>();

        cut.WaitForAssertion(() => Assert.Contains("Not signed in", cut.Find("[data-cli=codex] .state").TextContent));
        cut.Find("[data-cli=codex] button.sign-in").Click();

        var call = Assert.Single(terminal.Opened);
        Assert.Equal("codex", call.Command);
        Assert.Equal(new[] { "login" }, call.Args);
    }

    [Fact]
    public void Refresh_rechecks_the_clis()
    {
        var (runner, _) = Wire(r => r.Script("claude", _ => Ok("""{"loggedIn": true}""")));
        var cut = RenderComponent<Setup>();
        cut.WaitForAssertion(() => Assert.True(runner.RunCalls > 0));
        var before = runner.RunCalls;

        cut.Find("button.refresh").Click();

        cut.WaitForAssertion(() => Assert.True(runner.RunCalls > before));
    }
}
