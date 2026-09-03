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

    private ForemanFactory(string dataPath, string employeesPath, bool ownsDataPath)
    {
        _dataPath = dataPath;
        _employeesPath = employeesPath;
        _ownsDataPath = ownsDataPath;
    }

    public FakeContextApi ContextApi { get; } = new();
    public FakeAgentProvider Provider { get; } = new();

    public static ForemanFactory Create(out string dataPath)
    {
        dataPath = Path.Combine(Path.GetTempPath(), "foreman-tests", Guid.NewGuid().ToString("N"));
        var employeesPath = Path.Combine(dataPath, "employees");
        Directory.CreateDirectory(employeesPath);
        return new ForemanFactory(dataPath, employeesPath, ownsDataPath: true);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Foreman:DataPath", _dataPath);
        builder.UseSetting("Foreman:EmployeesPath", _employeesPath);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IContextApiClient>();
            services.AddSingleton<IContextApiClient>(ContextApi);
            services.RemoveAll<IAgentProvider>();
            services.AddSingleton<IAgentProvider>(Provider);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (_ownsDataPath)
            try { if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true); } catch { }
    }
}
