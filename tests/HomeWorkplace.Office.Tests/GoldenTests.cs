using HomeWorkplace.Client;
using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Tests;

/// <summary>
/// Golden-image tests need a GPU and one live graphics device per test run. The first run
/// for a new scene writes the PNG and FAILS on purpose: the image must be looked at before
/// it becomes the standard. Commit the PNG under goldens/ once it looks right.
/// </summary>
[Collection("gpu")]
public class GoldenTests
{
    private static readonly Shift[] Office = { new(new TimeOnly(9, 0), new TimeOnly(20, 0)) };
    private readonly GoldenHost _host;

    public GoldenTests(GoldenHost host) => _host = host;

    private static Simulation SeededOffice()
    {
        var sim = new Simulation(WorldLayout.Generate(new[] { "ada-coder", "mia-manager", "rex-reviewer", "vfx-artist" }), seed: 7);
        sim.Apply(new EmployeeAppeared("ada-coder", "Ada", EmployeeStatus.Working, "Build the parser"));
        sim.Apply(new EmployeeAppeared("mia-manager", "Mia", EmployeeStatus.Awake, null));
        sim.Apply(new EmployeeAppeared("rex-reviewer", "Rex", EmployeeStatus.Awake, null));
        sim.Apply(new EmployeeAppeared("vfx-artist", "Vex", EmployeeStatus.Asleep, null));
        for (var i = 0; i < 60 * 40; i++) sim.Update(1f / 60f);   // everyone reaches their desk
        sim.Apply(new HumanNeeded("rex-reviewer"));
        return sim;
    }

    [Fact]
    public void The_office_renders_at_the_native_resolution()
    {
        var frame = _host.Render(SeededOffice(), new TimeOnly(10, 0), Office);
        Assert.Equal(480, frame.Width);
        Assert.Equal(272, frame.Height);
        Assert.True(frame.Pixels.Count(p => p.A > 0) > 480 * 272 / 2, "most of the frame should be drawn");
    }

    [Fact]
    public void The_office_at_ten_matches_its_golden()
    {
        var frame = _host.Render(SeededOffice(), new TimeOnly(10, 0), Office);
        Golden.Check(_host, "office-10am", frame, tolerance: 0.005);
    }

    [Fact]
    public void The_office_at_night_matches_its_golden()
    {
        var frame = _host.Render(SeededOffice(), new TimeOnly(20, 30), Office);
        Golden.Check(_host, "office-2030", frame, tolerance: 0.005);
    }

    [Fact]
    public void Night_is_darker_than_day()
    {
        var day = _host.Render(SeededOffice(), new TimeOnly(10, 0), Office);
        var night = _host.Render(SeededOffice(), new TimeOnly(23, 0), Office);
        double Brightness(Frame f) => f.Pixels.Average(p => (p.R + p.G + p.B) / 3.0);
        Assert.True(Brightness(night) < Brightness(day) * 0.6, "night should be well under day brightness");
    }
}
