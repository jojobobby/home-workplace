using System.Globalization;
using HomeWorkplace.Client;
using HomeWorkplace.Live;
using HomeWorkplace.Office;

// app.json lives beside the executable; a missing file means the dev defaults.
var config = AppConfig.Load(Path.Combine(AppContext.BaseDirectory, "app.json"));
AppConfigDirectories.ResolveWorkingDirectories(config, AppContext.BaseDirectory);

var runner = new ProcessRunner();
var supervisor = new ServiceSupervisor(config, runner,
    new HttpHealthProbe(new HttpClient { Timeout = TimeSpan.FromSeconds(3) }));
var foreman = new ForemanClient(new HttpClient { BaseAddress = new Uri(config.ForemanUrl), Timeout = TimeSpan.FromSeconds(60) });   // outlives a 30 s long-poll
var context = new ContextApiClient(new HttpClient { BaseAddress = new Uri(config.ContextApiUrl), Timeout = TimeSpan.FromSeconds(30) });
var setup = new CliSetupChecker(runner);
var store = new AppStore();
var pump = new EventPump(foreman, store);

using var game = new OfficeGame(config, supervisor, store, pump, foreman, context, setup);

// Dev flags: --clock HH:mm   --frames-every SECONDS   --exit-after SECONDS   --smoke-script "walk ada-coder;talk;pick 0;..."
for (var i = 0; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--clock": game.ClockOverride = TimeOnly.ParseExact(args[i + 1], "HH:mm", CultureInfo.InvariantCulture); break;
        case "--frames-every": game.FrameEvery = float.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
        case "--exit-after": game.ExitAfter = float.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
        case "--smoke-script": game.RunScript(args[i + 1]); break;
    }
}

game.Run();
