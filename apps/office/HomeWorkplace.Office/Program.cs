using System.Globalization;
using HomeWorkplace.Client;
using HomeWorkplace.Live;
using HomeWorkplace.Office;

// app.json lives beside the executable; a missing file means the dev defaults. The settings screen writes it back.
var configPath = Path.Combine(AppContext.BaseDirectory, "app.json");
var config = AppConfig.Load(configPath);
AppConfigDirectories.ResolveWorkingDirectories(config, AppContext.BaseDirectory);

// Workplaces live under Documents\Home Workplace\<name>; the repo's hiring templates seed a new one.
var repoRoot = AppConfigDirectories.FindRepoRoot(AppContext.BaseDirectory);
var workplaces = new Workplaces(templatesSource: repoRoot is null ? null : Path.Combine(repoRoot, "hiring"));

var runner = new ProcessRunner();
var supervisor = new ServiceSupervisor(config, runner,
    new HttpHealthProbe(new HttpClient { Timeout = TimeSpan.FromSeconds(3) }));
var foreman = new ForemanClient(new HttpClient { BaseAddress = new Uri(config.ForemanUrl), Timeout = TimeSpan.FromSeconds(60) });   // outlives a 30 s long-poll
var context = new ContextApiClient(new HttpClient { BaseAddress = new Uri(config.ContextApiUrl), Timeout = TimeSpan.FromSeconds(30) });
var setup = new CliSetupChecker(runner);
var store = new AppStore();
var pump = new EventPump(foreman, store);

// --scale must be known before the window is sized.
for (var i = 0; i + 1 < args.Length; i++)
    if (args[i] == "--scale") config.Office.Scale = int.Parse(args[i + 1], CultureInfo.InvariantCulture);

using var game = new OfficeGame(config, supervisor, store, pump, foreman, context, setup, workplaces, configPath);

// Dev flags: --workplace NAME   --clock HH:mm   --frames-every SECONDS   --exit-after SECONDS   --smoke-script "walk ada-coder;talk;pick 0;..."   --ui-shot SCENE [PNG]
var smoke = false;
for (var i = 0; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--workplace": game.StartWorkplace = args[i + 1]; break;
        case "--clock": game.ClockOverride = TimeOnly.ParseExact(args[i + 1], "HH:mm", CultureInfo.InvariantCulture); break;
        case "--frames-every": game.FrameEvery = float.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
        case "--exit-after": game.ExitAfter = float.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
        case "--smoke-script": game.RunScript(args[i + 1]); smoke = !args[i + 1].TrimStart().StartsWith("menu", StringComparison.Ordinal); break;   // a script that starts with "menu" drives the menu itself
        case "--scale": break;   // handled above
        case "--ui-shot":
            var hasPath = args.Length > i + 2 && !args[i + 2].StartsWith("--", StringComparison.Ordinal);
            game.UiShot = (args[i + 1], hasPath ? args[i + 2] : Path.Combine(AppContext.BaseDirectory, "frames", args[i + 1] + ".png"));
            if (hasPath) i++;
            break;
    }
}
if (smoke && game.StartWorkplace is null) game.StartWorkplace = config.Office.Name;   // a script needs an office to run in

game.Run();
