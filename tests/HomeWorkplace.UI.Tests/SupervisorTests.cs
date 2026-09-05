using HomeWorkplace.Client;

namespace HomeWorkplace.UI.Tests;

public class SupervisorTests
{
    private const string Ctx = "http://localhost:5171";
    private const string Fm = "http://localhost:5172";

    private static AppConfig Config(bool connectOnly = false, int timeoutSeconds = 5) => new()
    {
        ConnectOnly = connectOnly,
        ContextApiUrl = Ctx,
        ForemanUrl = Fm,
        ContextApi = new ServiceCommand("dotnet", new[] { "run", "--project", "services/context-api/src/HomeWorkplace.ContextApi" }, "/repo"),
        Foreman = new ServiceCommand("dotnet", new[] { "run", "--project", "services/foreman/src/HomeWorkplace.Foreman" }, "/repo"),
        HealthPollMs = 1,
        HealthTimeoutSeconds = timeoutSeconds,
    };

    [Fact]
    public async Task Starts_both_services_with_a_scrubbed_environment_and_waits_for_health()
    {
        var runner = new FakeProcessRunner();
        var health = new FakeHealthProbe().HealthyAfter(Ctx, 2).HealthyAfter(Fm, 3);
        var sup = new ServiceSupervisor(Config(), runner, health);
        var progress = new List<BootProgress>();
        sup.Progress += p => progress.Add(p);

        var result = await sup.StartAsync(CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, runner.Starts.Count);
        Assert.Contains("context-api", runner.Starts[0].Args[2], StringComparison.Ordinal);
        Assert.Contains("foreman", runner.Starts[1].Args[2], StringComparison.Ordinal);
        Assert.All(runner.Starts, s => Assert.DoesNotContain(s.Environment.Keys,
            k => k.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase) ||
                 (k.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase) && !EnvironmentScrub.KeptForApiKeyUse.Contains(k))));
        Assert.Contains(progress, p => p.Service == ServiceName.ContextApi && p.Healthy);
        Assert.Contains(progress, p => p.Service == ServiceName.Foreman && p.Healthy);
        Assert.True(health.Probes(Fm) >= 3);
    }

    [Fact]
    public async Task Connect_only_starts_nothing_and_just_probes()
    {
        var runner = new FakeProcessRunner();
        var health = new FakeHealthProbe();
        var sup = new ServiceSupervisor(Config(connectOnly: true), runner, health);

        var result = await sup.StartAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(runner.Starts);
        sup.Stop();
        Assert.Empty(runner.Handles);
    }

    [Fact]
    public async Task A_service_that_never_becomes_healthy_fails_the_boot_and_stops_what_was_started()
    {
        var runner = new FakeProcessRunner();
        var health = new FakeHealthProbe().HealthyAfter(Ctx, 1).Never(Fm);
        var sup = new ServiceSupervisor(Config(timeoutSeconds: 1), runner, health);

        var result = await sup.StartAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("foreman", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.All(runner.Handles, h => Assert.True(h.Killed));
    }

    [Fact]
    public async Task Stop_kills_only_what_it_started()
    {
        var runner = new FakeProcessRunner();
        var sup = new ServiceSupervisor(Config(), runner, new FakeHealthProbe());
        await sup.StartAsync(CancellationToken.None);

        sup.Stop();

        Assert.Equal(2, runner.Handles.Count);
        Assert.All(runner.Handles, h => Assert.True(h.Killed));
    }

    [Fact]
    public void Config_loads_defaults_when_the_file_is_missing()
    {
        var cfg = AppConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json"));

        Assert.False(cfg.ConnectOnly);
        Assert.Equal("http://localhost:5172", cfg.ForemanUrl);
        Assert.Equal("http://localhost:5171", cfg.ContextApiUrl);
        Assert.Equal("dotnet", cfg.Foreman.Command);
    }
}

public class SupervisorEnvironmentTests
{
    [Fact]
    public async Task Extra_service_environment_reaches_both_services_on_top_of_the_scrub()
    {
        var runner = new FakeProcessRunner();
        var health = new FakeHealthProbe().HealthyAfter("http://localhost:5171", 1).HealthyAfter("http://localhost:5172", 1);
        var cfg = new AppConfig
        {
            ContextApi = new ServiceCommand("dotnet", new[] { "run", "--project", "services/context-api/src/HomeWorkplace.ContextApi" }, "/repo"),
            Foreman = new ServiceCommand("dotnet", new[] { "run", "--project", "services/foreman/src/HomeWorkplace.Foreman" }, "/repo"),
            HealthPollMs = 1, HealthTimeoutSeconds = 5,
            ServiceEnvironment = new Dictionary<string, string?> { ["Foreman__DataPath"] = @"C:\office\data", ["Foreman__EmployeesPath"] = @"C:\office\employees" },
        };
        var sup = new ServiceSupervisor(cfg, runner, health);

        var result = await sup.StartAsync(CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.All(runner.Starts, s =>
        {
            Assert.Equal(@"C:\office\data", s.Environment["Foreman__DataPath"]);
            Assert.Equal(@"C:\office\employees", s.Environment["Foreman__EmployeesPath"]);
            Assert.DoesNotContain(s.Environment.Keys, k => k.StartsWith("CLAUDECODE", StringComparison.OrdinalIgnoreCase));
        });
    }
}
