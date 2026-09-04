using Bunit;
using HomeWorkplace.Client;
using HomeWorkplace.UI;
using HomeWorkplace.Live;
using Microsoft.Extensions.DependencyInjection;

namespace HomeWorkplace.UI.Tests;

public class BootTests : TestContext
{
    private FakeProcessRunner Wire(bool foremanComesUp = true, bool connectOnly = false)
    {
        var runner = new FakeProcessRunner();
        var health = new FakeHealthProbe();
        if (!foremanComesUp) health.Never("http://localhost:5172");
        var cfg = new AppConfig
        {
            ConnectOnly = connectOnly,
            ContextApi = new ServiceCommand("ctx", Array.Empty<string>(), null),
            Foreman = new ServiceCommand("fm", Array.Empty<string>(), null),
            HealthPollMs = 1,
            HealthTimeoutSeconds = 1,
        };
        var api = new FakeForemanApi();
        var store = new AppStore();
        Services.AddSingleton(cfg);
        Services.AddSingleton(new ServiceSupervisor(cfg, runner, health));
        Services.AddSingleton(store);
        Services.AddSingleton(new ShellState());
        Services.AddSingleton<IForemanApi>(api);
        Services.AddSingleton<IContextApi>(new FakeContextApi());
        Services.AddSingleton(new EventPump(api, store, backoffBaseMs: 1));
        Services.AddSingleton(new CliSetupChecker(runner));
        Services.AddSingleton<ITerminalLauncher>(new FakeTerminalLauncher());
        return runner;
    }

    [Fact]
    public void Boots_both_services_then_shows_the_app()
    {
        var runner = Wire();
        var cut = RenderComponent<Boot>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("nav .nav-link")), TimeSpan.FromSeconds(5));
        Assert.Equal(2, runner.Starts.Count);
        Assert.Empty(cut.FindAll(".boot-error"));
    }

    [Fact]
    public void Shows_the_error_and_a_retry_when_a_service_never_comes_up()
    {
        Wire(foremanComesUp: false);
        var cut = RenderComponent<Boot>();

        cut.WaitForAssertion(() => Assert.Contains("foreman", cut.Find(".boot-error").TextContent, StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(5));
        Assert.NotNull(cut.Find("button.retry"));
        Assert.Empty(cut.FindAll("nav"));
    }

    [Fact]
    public void Connect_only_shows_the_app_without_launching_anything()
    {
        var runner = Wire(connectOnly: true);
        var cut = RenderComponent<Boot>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("nav .nav-link")), TimeSpan.FromSeconds(5));
        Assert.Empty(runner.Starts);
    }
}
