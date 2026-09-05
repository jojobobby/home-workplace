using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeWorkplace.Client;

/// <summary>How to launch one service: the command, its arguments, and where to run it.</summary>
public sealed record ServiceCommand(string Command, IReadOnlyList<string> Args, string? WorkingDirectory);

/// <summary>
/// The app's settings, read from app.json beside the executable. Missing file = these defaults,
/// which launch the two services with `dotnet run` against the repo (dev mode). The settings
/// screen writes the same file back with <see cref="Save"/>.
/// </summary>
public sealed class AppConfig
{
    private static readonly JsonSerializerOptions SaveOptions = new(ApiJson.Options) { WriteIndented = true };

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

    /// <summary>Extra variables both services start with (the office folder for Foreman). Set by the app, not by app.json.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string?> ServiceEnvironment { get; set; } = new Dictionary<string, string?>();

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

    /// <summary>
    /// Write the file <see cref="Load"/> reads. A working directory the app resolved itself (the
    /// repo root above the executable) is left out so the file never pins a path that moved.
    /// </summary>
    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? ".";
        Directory.CreateDirectory(dir);
        var root = AppConfigDirectories.FindRepoRoot(dir);
        var copy = new AppConfig
        {
            ConnectOnly = ConnectOnly, ForemanUrl = ForemanUrl, ContextApiUrl = ContextApiUrl,
            ContextApi = Unresolve(ContextApi, root), Foreman = Unresolve(Foreman, root),
            HealthPollMs = HealthPollMs, HealthTimeoutSeconds = HealthTimeoutSeconds, Office = Office,
        };
        File.WriteAllText(full, JsonSerializer.Serialize(copy, SaveOptions));
    }

    private static ServiceCommand Unresolve(ServiceCommand command, string? root)
        => root is not null && string.Equals(command.WorkingDirectory, root, StringComparison.OrdinalIgnoreCase)
            ? command with { WorkingDirectory = null }
            : command;
}

public enum WindowMode { Windowed, Borderless, Fullscreen }

/// <summary>The office game's settings: what the settings screen edits and app.json keeps.</summary>
public sealed class OfficeConfig
{
    /// <summary>The workplace opened last (and the one dev flags open): its folder under Documents\Home Workplace.</summary>
    public string Name { get; set; } = "Main Office";

    // video
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WindowMode WindowMode { get; set; } = WindowMode.Windowed;
    /// <summary>Integer window scale, 0 = the largest that fits.</summary>
    public int Scale { get; set; }
    public bool VSync { get; set; } = true;
    public bool Lighting { get; set; } = true;
    public bool Particles { get; set; } = true;
    public bool ScreenShake { get; set; } = true;

    // interface
    /// <summary>UI font family, or "Pixel" for the 5×7 pixel font.</summary>
    public string UiFont { get; set; } = "Cascadia Mono";
    public bool ShortcutBar { get; set; } = true;
    public bool ShowDebug { get; set; }

    // audio
    public float Volume { get; set; } = 0.6f;
    public bool Muted { get; set; }

    // general
    public string PlayerName { get; set; } = "You";
    /// <summary>Index into the shirt palette.</summary>
    public int PlayerColour { get; set; }
    /// <summary>Action name → key name; see <see cref="KeyBindings"/>.</summary>
    public Dictionary<string, string> Bindings { get; set; } = new();
}

public static class AppConfigDirectories
{
    /// <summary>
    /// In development the services run from the repo with `dotnet run`, so a command with no
    /// working directory is anchored at the repo root, found by walking up from <paramref name="start"/>
    /// until HomeWorkplace.sln appears. A release build ships explicit directories instead.
    /// </summary>
    public static void ResolveWorkingDirectories(AppConfig config, string start)
    {
        if (config.ContextApi.WorkingDirectory is not null && config.Foreman.WorkingDirectory is not null) return;
        var root = FindRepoRoot(start);
        if (root is null) return;
        config.ContextApi = config.ContextApi with { WorkingDirectory = config.ContextApi.WorkingDirectory ?? root };
        config.Foreman = config.Foreman with { WorkingDirectory = config.Foreman.WorkingDirectory ?? root };
    }

    public static string? FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "HomeWorkplace.sln"))) return dir.FullName;
        return null;
    }
}
