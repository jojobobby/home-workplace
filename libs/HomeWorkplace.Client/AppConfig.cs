using System.Text.Json;

namespace HomeWorkplace.Client;

/// <summary>How to launch one service: the command, its arguments, and where to run it.</summary>
public sealed record ServiceCommand(string Command, IReadOnlyList<string> Args, string? WorkingDirectory);

/// <summary>
/// The app's settings, read from app.json beside the executable. Missing file = these defaults,
/// which launch the two services with `dotnet run` against the repo (dev mode).
/// </summary>
public sealed class AppConfig
{
    public bool ConnectOnly { get; set; }
    public string ForemanUrl { get; set; } = "http://localhost:5172";
    public string ContextApiUrl { get; set; } = "http://localhost:5171";
    public ServiceCommand ContextApi { get; set; } =
        new("dotnet", new[] { "run", "--project", "services/context-api/src/HomeWorkplace.ContextApi" }, null);
    public ServiceCommand Foreman { get; set; } =
        new("dotnet", new[] { "run", "--project", "services/foreman/src/HomeWorkplace.Foreman" }, null);
    public int HealthPollMs { get; set; } = 500;
    public int HealthTimeoutSeconds { get; set; } = 60;
    public OfficeConfig Office { get; set; } = new();

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path)) return new AppConfig();
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), ApiJson.Options) ?? new AppConfig();
        }
        catch (JsonException)
        {
            return new AppConfig();   // a malformed file must not stop the app from booting
        }
    }
}

/// <summary>The office game's settings: master volume, integer scale (0 = largest fit), debug overlay.</summary>
public sealed class OfficeConfig
{
    public float Volume { get; set; } = 0.6f;
    public int Scale { get; set; }
    public bool ShowDebug { get; set; }
}
