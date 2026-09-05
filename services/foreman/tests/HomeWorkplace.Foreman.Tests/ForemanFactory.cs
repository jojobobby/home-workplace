using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}

public sealed class ForemanFactory : WebApplicationFactory<Program>
{
    private readonly string _dataPath;
    private readonly string _employeesPath;
    private readonly bool _ownsDataPath;
    private readonly TimeProvider? _clock;

    private ForemanFactory(string dataPath, string employeesPath, bool ownsDataPath, TimeProvider? clock)
    {
        _dataPath = dataPath;
        _employeesPath = employeesPath;
        _ownsDataPath = ownsDataPath;
        _clock = clock;
    }

    public FakeContextApi ContextApi { get; } = new();
    public FakeAgentProvider Provider { get; } = new();

    public static ForemanFactory Create(out string dataPath, TimeProvider? clock = null)
    {
        dataPath = Path.Combine(Path.GetTempPath(), "foreman-tests", Guid.NewGuid().ToString("N"));
        var employeesPath = Path.Combine(dataPath, "employees");
        Directory.CreateDirectory(employeesPath);
        return new ForemanFactory(dataPath, employeesPath, ownsDataPath: true, clock);
    }

    public static ForemanFactory Existing(string dataPath, string employeesPath, Action<FakeAgentProvider>? provider = null)
    {
        var f = new ForemanFactory(dataPath, employeesPath, ownsDataPath: false, clock: null);
        provider?.Invoke(f.Provider);
        return f;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Foreman:DataPath", _dataPath);
        builder.UseSetting("Foreman:EmployeesPath", _employeesPath);
        builder.UseSetting("Foreman:HiringPath", Path.Combine(_dataPath, "hiring"));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IContextApiClient>();
            services.AddSingleton<IContextApiClient>(ContextApi);
            services.RemoveAll<IAgentProvider>();
            services.AddSingleton<IAgentProvider>(Provider);
            if (_clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_clock);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (_ownsDataPath)
            try { if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true); } catch { }
    }
}
