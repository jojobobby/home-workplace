using HomeWorkplace.Client;
using HomeWorkplace.UI;
using Microsoft.Extensions.Logging;

namespace HomeWorkplace.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		// app.json lives beside the executable; a missing file means the dev defaults.
		var config = AppConfig.Load(Path.Combine(AppContext.BaseDirectory, "app.json"));
		ResolveWorkingDirectories(config);

		builder.Services.AddSingleton(config);
		builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
		builder.Services.AddSingleton<ITerminalLauncher, WindowsTerminalLauncher>();
		builder.Services.AddSingleton<IHealthProbe>(_ => new HttpHealthProbe(new HttpClient { Timeout = TimeSpan.FromSeconds(3) }));
		builder.Services.AddSingleton<IForemanApi>(_ => new ForemanClient(
			new HttpClient { BaseAddress = new Uri(config.ForemanUrl), Timeout = TimeSpan.FromSeconds(60) }));   // outlives a 30 s long-poll
		builder.Services.AddSingleton<IContextApi>(_ => new ContextApiClient(
			new HttpClient { BaseAddress = new Uri(config.ContextApiUrl), Timeout = TimeSpan.FromSeconds(30) }));
		builder.Services.AddSingleton<ServiceSupervisor>();
		builder.Services.AddSingleton<CliSetupChecker>();
		builder.Services.AddSingleton<AppStore>();
		builder.Services.AddSingleton<ShellState>();
		builder.Services.AddSingleton(sp => new EventPump(sp.GetRequiredService<IForemanApi>(), sp.GetRequiredService<AppStore>()));

		return builder.Build();
	}

	/// <summary>
	/// In development the services run from the repo with `dotnet run`, so a command with no
	/// working directory is anchored at the repo root — found by walking up from the exe until
	/// HomeWorkplace.sln appears. A release build ships explicit commands in app.json instead.
	/// </summary>
	private static void ResolveWorkingDirectories(AppConfig config)
	{
		if (config.ContextApi.WorkingDirectory is not null && config.Foreman.WorkingDirectory is not null) return;
		var root = FindRepoRoot(AppContext.BaseDirectory);
		if (root is null) return;
		config.ContextApi = config.ContextApi with { WorkingDirectory = config.ContextApi.WorkingDirectory ?? root };
		config.Foreman = config.Foreman with { WorkingDirectory = config.Foreman.WorkingDirectory ?? root };
	}

	private static string? FindRepoRoot(string start)
	{
		for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
			if (File.Exists(Path.Combine(dir.FullName, "HomeWorkplace.sln"))) return dir.FullName;
		return null;
	}
}
