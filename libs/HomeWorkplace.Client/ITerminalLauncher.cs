using System.Diagnostics;

namespace HomeWorkplace.Client;

/// <summary>Opens an interactive terminal running a command — for the CLIs' own login flows, which are interactive and browser-based.</summary>
public interface ITerminalLauncher
{
    void Open(string command, IReadOnlyList<string> args);
}

/// <summary>
/// Windows: a fresh console window running `cmd /k command args`, so it stays open after the
/// command finishes and the user can read what happened. The environment is scrubbed, so a
/// `claude` started here is not mistaken for a nested Claude Code session.
/// </summary>
public sealed class WindowsTerminalLauncher : ITerminalLauncher
{
    public void Open(string command, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,   // required to set the environment; a GUI parent still gets a new console window
            CreateNoWindow = false,
        };
        psi.ArgumentList.Add("/k");
        psi.ArgumentList.Add(command);
        foreach (var a in args) psi.ArgumentList.Add(a);

        psi.Environment.Clear();
        foreach (var (k, v) in EnvironmentScrub.Current()) psi.Environment[k] = v;

        using var _ = Process.Start(psi);
    }
}
