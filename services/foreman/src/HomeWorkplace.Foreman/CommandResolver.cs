namespace HomeWorkplace.Foreman;

/// <summary>
/// Turns a bare command name into something <c>Process.Start</c> can launch. On Windows a
/// bare name is searched on PATH with PATHEXT, and a <c>.cmd</c>/<c>.bat</c> hit (npm's
/// <c>claude.cmd</c> shim, for one) runs through <c>cmd.exe /d /c</c>. Elsewhere, and for
/// rooted paths or names with an extension, the command is returned unchanged.
/// </summary>
public static class CommandResolver
{
    public static (string FileName, IReadOnlyList<string> LeadingArgs) Resolve(string command)
    {
        if (!OperatingSystem.IsWindows() || Path.IsPathRooted(command) || Path.HasExtension(command))
            return (command, Array.Empty<string>());

        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs.Prepend(Environment.CurrentDirectory))
        foreach (var ext in exts)
        {
            string? candidate;
            try
            {
                var folder = dir.Trim('"');
                if (!Directory.Exists(folder)) continue;
                candidate = Directory.EnumerateFiles(folder, command + ext).FirstOrDefault();   // the on-disk name, real casing
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { continue; }
            if (candidate is null) continue;
            return ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                ? ("cmd.exe", new[] { "/d", "/c", candidate })
                : (candidate, Array.Empty<string>());
        }
        return (command, Array.Empty<string>());
    }
}
