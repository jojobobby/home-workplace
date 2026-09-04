using System.Numerics;
using HomeWorkplace.Client;
using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Tests;

public class LightingTests
{
    private static readonly Shift[] Office = { new(new TimeOnly(9, 0), new TimeOnly(20, 0)) };

    [Theory]
    [InlineData(10, 0, DayPhase.Day)]
    [InlineData(19, 59, DayPhase.Day)]
    [InlineData(20, 15, DayPhase.Dusk)]
    [InlineData(21, 0, DayPhase.Night)]
    [InlineData(8, 45, DayPhase.Dusk)]
    [InlineData(8, 0, DayPhase.Night)]
    [InlineData(3, 30, DayPhase.Night)]
    public void The_ambient_phase_follows_the_shift_with_half_an_hour_of_dusk_either_side(int h, int m, DayPhase phase)
    {
        Assert.Equal(phase, Ambient.For(new TimeOnly(h, m), Office).Phase);
    }

    [Fact]
    public void Day_is_full_brightness_night_is_dim_blue_and_dusk_sits_between()
    {
        var day = Ambient.For(new TimeOnly(12, 0), Office).Colour;
        var dusk = Ambient.For(new TimeOnly(20, 15), Office).Colour;
        var night = Ambient.For(new TimeOnly(23, 0), Office).Colour;

        Assert.Equal(Ambient.Day, day);
        Assert.Equal(Ambient.Night, night);
        Assert.True(night.R < dusk.R && dusk.R < day.R, "dusk should be between night and day");
        Assert.True(night.B > night.R, "night leans blue");
    }

    [Fact]
    public void Office_hours_span_the_earliest_wake_to_the_latest_sleep_of_the_team()
    {
        var team = new[] { new Shift(new TimeOnly(8, 30), new TimeOnly(19, 30)), new Shift(new TimeOnly(9, 0), new TimeOnly(20, 0)) };
        Assert.Equal(DayPhase.Day, Ambient.For(new TimeOnly(8, 45), team).Phase);
        Assert.Equal(DayPhase.Day, Ambient.For(new TimeOnly(19, 45), team).Phase);
        Assert.Equal(DayPhase.Day, Ambient.For(new TimeOnly(10, 0), Array.Empty<Shift>()).Phase);   // no team → default hours
    }

    [Fact]
    public void A_shadow_quad_is_the_edge_pushed_away_from_the_light()
    {
        var quad = Shadows.QuadFor(new Vector2(16, 0), new Vector2(16, 16), light: new Vector2(0, 8), reach: 100);

        Assert.Equal(4, quad.Length);
        Assert.Equal(new Vector2(16, 0), quad[0]);
        Assert.Equal(new Vector2(16, 16), quad[1]);
        Assert.True(quad[2].X > 16 && quad[3].X > 16, "the far points lie away from the light");
        Assert.True(quad[2].Y > quad[3].Y, "the quad fans out with the edge");
    }

    [Fact]
    public void Only_occluder_edges_facing_the_light_cast_shadows_and_only_within_reach()
    {
        var map = new TileMap(5, 5);
        map.Block(new TilePos(3, 2));
        map.Block(new TilePos(4, 4));                                      // too far
        var light = Agent.Centre(new TilePos(1, 2));                       // (24, 40)

        var edges = Shadows.CastingEdges(map, light, radius: 40).ToList();

        var edge = Assert.Single(edges);
        Assert.Equal(48, edge.A.X);                                        // the blocked tile's left face
        Assert.Equal(48, edge.B.X);
        Assert.Equal(new[] { 32f, 48f }, new[] { edge.A.Y, edge.B.Y }.OrderBy(y => y));
    }

    private static Simulation SeatedTeam()
    {
        var sim = new Simulation(WorldLayout.Generate(new[] { "ada", "rex", "vex" }), seed: 7);
        sim.Apply(new EmployeeAppeared("ada", "Ada", EmployeeStatus.Working, "T"));
        sim.Apply(new EmployeeAppeared("rex", "Rex", EmployeeStatus.Awake, null));
        sim.Apply(new EmployeeAppeared("vex", "Vex", EmployeeStatus.Asleep, null));
        for (var i = 0; i < 60 * 40; i++) sim.Update(1f / 60f);
        return sim;
    }

    [Fact]
    public void Lights_follow_what_the_employees_are_doing()
    {
        var sim = SeatedTeam();
        var day = Lights.For(sim, DayPhase.Day, time: 0f);

        Assert.Equal(2, day.Count(l => l.Kind == LightKind.Lamp));         // ada + rex; vex is asleep
        Assert.Single(day.Where(l => l.Kind == LightKind.Monitor));        // only the one typing
        Assert.True(day.Count(l => l.Kind == LightKind.Ceiling) > 0);
        Assert.Single(day.Where(l => l.Kind == LightKind.Coffee));

        var night = Lights.For(sim, DayPhase.Night, time: 0f);
        Assert.Empty(night.Where(l => l.Kind == LightKind.Ceiling));
        Assert.Equal(2, night.Count(l => l.Kind == LightKind.Lamp));
    }

    [Fact]
    public void Monitors_flicker_over_time()
    {
        var sim = SeatedTeam();
        var a = Lights.For(sim, DayPhase.Day, 0f).Single(l => l.Kind == LightKind.Monitor).Intensity;
        var b = Lights.For(sim, DayPhase.Day, 0.05f).Single(l => l.Kind == LightKind.Monitor).Intensity;
        Assert.NotEqual(a, b);
        Assert.InRange(a, 0.7f, 1.0f);
    }
}
