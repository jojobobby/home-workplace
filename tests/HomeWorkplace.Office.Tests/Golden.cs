using HomeWorkplace.Office.Render;

namespace HomeWorkplace.Office.Tests;

/// <summary>
/// Golden-image comparison. Goldens live in the test project's goldens/ folder (found by
/// walking up from the test binary). A missing golden is written and the test FAILS: the
/// image must be reviewed by a person before it is the standard. A mismatch writes
/// <name>.actual.png beside the golden for diffing.
/// </summary>
public static class Golden
{
    public static string Dir
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "HomeWorkplace.Office.Tests.csproj")))
                    return Path.Combine(dir.FullName, "goldens");
            throw new InvalidOperationException("could not find the test project directory");
        }
    }

    public static void Check(GoldenHost host, string name, Frame actual, double tolerance)
    {
        Directory.CreateDirectory(Dir);
        var goldenPath = Path.Combine(Dir, name + ".png");
        var actualPath = Path.Combine(Dir, name + ".actual.png");

        if (!File.Exists(goldenPath))
        {
            host.SavePng(actual, goldenPath);
            Assert.Fail($"Golden created at {goldenPath} — look at it, then rerun to make it the standard.");
        }

        var expected = host.LoadPng(goldenPath);
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));

        var differing = 0;
        for (var i = 0; i < actual.Pixels.Length; i++)
            if (!Close(expected.Pixels[i], actual.Pixels[i])) differing++;

        var ratio = differing / (double)actual.Pixels.Length;
        if (ratio > tolerance)
        {
            host.SavePng(actual, actualPath);
            Assert.Fail($"{ratio:P2} of pixels differ from golden '{name}' (tolerance {tolerance:P2}); see {actualPath}");
        }
        else if (File.Exists(actualPath))
        {
            File.Delete(actualPath);
        }
    }

    private static bool Close(Rgba a, Rgba b)
        => Math.Abs(a.R - b.R) <= 2 && Math.Abs(a.G - b.G) <= 2 && Math.Abs(a.B - b.B) <= 2 && Math.Abs(a.A - b.A) <= 2;
}
