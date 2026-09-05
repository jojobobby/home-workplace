using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class CommandResolverTests
{
    [Fact]
    public void A_cmd_shim_on_path_runs_through_cmd_exe()
    {
        if (!OperatingSystem.IsWindows()) return;

        var dir = Path.Combine(Path.GetTempPath(), "hw-fm-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var shim = Path.Combine(dir, "fmshim.cmd");
        File.WriteAllText(shim, "@echo hi\r\n");
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", dir + ";" + oldPath);
        try
        {
            var (file, leading) = CommandResolver.Resolve("fmshim");
            Assert.Equal("cmd.exe", file);
            Assert.Equal(new[] { "/d", "/c", shim }, leading);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Unknown_names_and_rooted_paths_pass_through()
    {
        Assert.Equal(("no-such-thing-abc", Array.Empty<string>()), CommandResolver.Resolve("no-such-thing-abc"));
        Assert.Equal(("tool.exe", Array.Empty<string>()), CommandResolver.Resolve("tool.exe"));
    }
}
