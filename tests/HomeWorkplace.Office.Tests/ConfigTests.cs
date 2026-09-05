using HomeWorkplace.Client;
using HomeWorkplace.Live;

namespace HomeWorkplace.Office.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hw-config-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Settings_round_trip_through_app_json()
    {
        var path = Path.Combine(_dir, "app.json");
        var config = new AppConfig();
        config.Office.Name = "Acme";
        config.Office.WindowMode = WindowMode.Borderless;
        config.Office.Scale = 2;
        config.Office.VSync = false;
        config.Office.Lighting = false;
        config.Office.Particles = false;
        config.Office.ScreenShake = false;
        config.Office.UiFont = "Consolas";
        config.Office.ShortcutBar = false;
        config.Office.ShowDebug = true;
        config.Office.Volume = 0.3f;
        config.Office.Muted = true;
        config.Office.PlayerName = "Raph";
        config.Office.PlayerColour = 5;
        config.Office.Bindings["Talk"] = "F";
        config.Save(path);

        var loaded = AppConfig.Load(path);
        Assert.Equal("Acme", loaded.Office.Name);
        Assert.Equal(WindowMode.Borderless, loaded.Office.WindowMode);
        Assert.Equal(2, loaded.Office.Scale);
        Assert.False(loaded.Office.VSync);
        Assert.False(loaded.Office.Lighting);
        Assert.False(loaded.Office.Particles);
        Assert.False(loaded.Office.ScreenShake);
        Assert.Equal("Consolas", loaded.Office.UiFont);
        Assert.False(loaded.Office.ShortcutBar);
        Assert.True(loaded.Office.ShowDebug);
        Assert.Equal(0.3, loaded.Office.Volume, 3);
        Assert.True(loaded.Office.Muted);
        Assert.Equal("Raph", loaded.Office.PlayerName);
        Assert.Equal(5, loaded.Office.PlayerColour);
        Assert.Equal("F", KeyBindings.KeyFor(loaded.Office.Bindings, GameAction.Talk));
        Assert.Contains("\"windowMode\": \"Borderless\"", File.ReadAllText(path));   // readable by hand
    }

    [Fact]
    public void Defaults_match_what_the_game_shipped_with()
    {
        var office = new OfficeConfig();
        Assert.Equal("Main Office", office.Name);
        Assert.Equal(WindowMode.Windowed, office.WindowMode);
        Assert.Equal(0, office.Scale);
        Assert.True(office.VSync && office.Lighting && office.Particles && office.ScreenShake && office.ShortcutBar);
        Assert.Equal("Cascadia Mono", office.UiFont);
        Assert.Equal(0.6f, office.Volume);
        Assert.Equal("You", office.PlayerName);
        Assert.Empty(office.Bindings);
    }

    [Fact]
    public void Saving_does_not_pin_the_repo_root_the_app_resolved_at_start()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "HomeWorkplace.sln"), "");
        var exeDir = Path.Combine(_dir, "bin");
        var config = new AppConfig();
        AppConfigDirectories.ResolveWorkingDirectories(config, exeDir);
        Assert.Equal(_dir, config.Foreman.WorkingDirectory);

        config.Save(Path.Combine(exeDir, "app.json"));
        var json = File.ReadAllText(Path.Combine(exeDir, "app.json"));
        Assert.DoesNotContain("workingDirectory", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HomeWorkplace.Foreman", json);   // the command itself is kept
    }

    [Fact]
    public void Bindings_fall_back_to_the_defaults()
    {
        var bindings = new Dictionary<string, string> { ["Talk"] = "F", ["Menu"] = "" };
        Assert.Equal("F", KeyBindings.KeyFor(bindings, GameAction.Talk));
        Assert.Equal("Tab", KeyBindings.KeyFor(bindings, GameAction.Menu));
        Assert.Equal("W", KeyBindings.KeyFor(bindings, GameAction.WalkUp));
        Assert.Equal(9, KeyBindings.All.Count);
        Assert.Equal("Talk / use", KeyBindings.Label(GameAction.Talk));
    }

    [Fact]
    public void Clearing_the_store_forgets_the_workplace()
    {
        var store = new AppStore();
        store.SetEmployee(new EmployeeDto { Id = "ada-coder", Name = "Ada" });
        store.SetTask(new TaskDto { Id = "t1", Title = "x" });
        store.SetServicesUp(true);
        var raised = 0;
        store.Changed += () => raised++;

        store.Clear();
        Assert.Empty(store.Employees);
        Assert.Empty(store.Tasks);
        Assert.False(store.ServicesUp);
        Assert.Equal(1, raised);
    }
}
