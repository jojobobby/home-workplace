using System.Numerics;
using HomeWorkplace.Client;
using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Tests;

public class ShiftTests
{
    private static EmployeeDto Emp(string id, string? wake, string? sleep) => new() { Id = id, Name = id, Wake = wake, Sleep = sleep };

    [Fact]
    public void Shifts_come_from_the_employees_wake_and_sleep_times()
    {
        var shifts = Shifts.From(new[] { Emp("a", "08:00", "17:00"), Emp("b", "10:30", "22:00") });
        Assert.Equal(2, shifts.Count);
        Assert.Contains(new Shift(new TimeOnly(8, 0), new TimeOnly(17, 0)), shifts);
        Assert.Contains(new Shift(new TimeOnly(10, 30), new TimeOnly(22, 0)), shifts);
    }

    [Fact]
    public void Missing_or_malformed_times_fall_back_to_the_default_office_day()
    {
        var shifts = Shifts.From(new[] { Emp("a", null, null), Emp("b", "noon", "17:00") });
        Assert.Equal(new[] { Shifts.Default, Shifts.Default }, shifts);
        Assert.Equal(new[] { Shifts.Default }, Shifts.From(Array.Empty<EmployeeDto>()));
    }
}

public class InputTests
{
    [Fact]
    public void Held_keys_pan_the_camera_at_a_speed_scaled_by_dt_and_zoom()
    {
        var pan = InputMap.PanFor(left: false, right: true, up: true, down: false, dt: 0.5f, zoom: 2);
        Assert.Equal(InputMap.PanSpeed * 0.5f / 2, pan.X, 3);
        Assert.Equal(-InputMap.PanSpeed * 0.5f / 2, pan.Y, 3);
        Assert.Equal(Vector2.Zero, InputMap.PanFor(false, false, false, false, 1f, 1));
    }

    [Fact]
    public void Wheel_notches_become_zoom_steps()
    {
        Assert.Equal(1, InputMap.ZoomStep(120));
        Assert.Equal(-1, InputMap.ZoomStep(-240));
        Assert.Equal(0, InputMap.ZoomStep(0));
    }

    [Fact]
    public void Window_pixels_map_to_native_pixels_through_the_letterbox()
    {
        // 1920×1080 window, native 480×272 → scale 3 (1440×816), offset (240,132)
        var native = InputMap.WindowToNative(new Vector2(240 + 30, 132 + 60), 1920, 1080, scale: 3);
        Assert.Equal(new Vector2(10, 20), native);
    }

    [Fact]
    public void Dragging_moves_the_camera_against_the_mouse()
    {
        var drag = InputMap.DragFor(new Vector2(100, 100), new Vector2(110, 95), zoom: 2);
        Assert.Equal(new Vector2(-5, 2.5f), drag);
    }
}

public class HitTestTests
{
    [Fact]
    public void Clicking_on_an_agent_selects_it_and_empty_floor_selects_nothing()
    {
        var sim = new Simulation(WorldLayout.Generate(new[] { "ada-coder" }), seed: 1);
        sim.Apply(new EmployeeAppeared("ada-coder", "Ada", EmployeeStatus.Awake, null));
        for (var i = 0; i < 60 * 40; i++) sim.Update(1f / 60f);
        var ada = sim.Agents["ada-coder"];

        Assert.Same(ada, HitTest.AgentAt(sim, ada.Position));
        Assert.Same(ada, HitTest.AgentAt(sim, ada.Position + new Vector2(6, -7)));
        Assert.Null(HitTest.AgentAt(sim, ada.Position + new Vector2(40, 40)));
    }
}
