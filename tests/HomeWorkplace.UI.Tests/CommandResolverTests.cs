using HomeWorkplace.Client;

namespace HomeWorkplace.UI.Tests;

public class CommandResolverTests
{
    [Fact]
    public async Task A_cmd_shim_on_path_is_found_and_runs_through_cmd_exe()
    {
        if (!OperatingSystem.IsWindows()) return;

        var dir = Path.Combine(Path.GetTempPath(), "hw-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var shim = Path.Combine(dir, "hwshim.cmd");
        await File.WriteAllTextAsync(shim, "@echo hello %1\r\n");
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", dir + ";" + oldPath);
        try
        {
            var (file, leading) = CommandResolver.Resolve("hwshim");
            Assert.Equal("cmd.exe", file);
            Assert.Equal(new[] { "/d", "/c", shim }, leading);

            var result = await new ProcessRunner().RunAsync("hwshim", new[] { "world" }, TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("hello world", result.Stdout.Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Rooted_paths_and_names_with_extensions_are_left_alone()
    {
        Assert.Equal(("dotnet.exe", Array.Empty<string>()), CommandResolver.Resolve("dotnet.exe"));
        var rooted = Path.Combine(Path.GetTempPath(), "x");
        Assert.Equal((rooted, Array.Empty<string>()), CommandResolver.Resolve(rooted));
        Assert.Equal(("definitely-not-a-command-xyz", Array.Empty<string>()), CommandResolver.Resolve("definitely-not-a-command-xyz"));
    }
}
