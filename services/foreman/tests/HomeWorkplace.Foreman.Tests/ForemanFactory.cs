using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HomeWorkplace.Foreman.Tests;

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}

public sealed class ForemanFactory : WebApplicationFactory<Program>
{
    private readonly string _dataPath;
    private readonly string _employeesPath;

    private ForemanFactory(string dataPath, string employeesPath)
    {
        _dataPath = dataPath;
        _employeesPath = employeesPath;
    }

    public static ForemanFactory Create(out string dataPath)
    {
        dataPath = Path.Combine(Path.GetTempPath(), "foreman-tests", Guid.NewGuid().ToString("N"));
        var employeesPath = Path.Combine(dataPath, "employees");
        Directory.CreateDirectory(employeesPath);
        return new ForemanFactory(dataPath, employeesPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Foreman:DataPath", _dataPath);
        builder.UseSetting("Foreman:EmployeesPath", _employeesPath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true); } catch { }
    }
}
