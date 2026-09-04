using System.Numerics;
using HomeWorkplace.Office.Render;

namespace HomeWorkplace.Office.Tests;

public class CameraTests
{
    private static Camera NewCamera() => new(worldWidth: 480, worldHeight: 272);

    [Fact]
    public void At_zoom_one_the_view_is_the_whole_world_wherever_the_centre_is()
    {
        var cam = NewCamera();
        cam.Pan(new Vector2(999, -999));
        Assert.Equal(1, cam.Zoom);
        Assert.Equal(new Vector2(0, 0), cam.ViewTopLeft);
        Assert.Equal(new Vector2(480, 272), cam.ViewSize);
    }

    [Fact]
    public void Zooming_in_shrinks_the_view_and_clamps_it_inside_the_world()
    {
        var cam = NewCamera();
        cam.ZoomAt(new Vector2(0, 0), +1);       // zoom 2 at the top-left corner
        Assert.Equal(2, cam.Zoom);
        Assert.Equal(new Vector2(240, 136), cam.ViewSize);
        Assert.Equal(new Vector2(0, 0), cam.ViewTopLeft);   // clamped: cannot show outside the world

        cam.Pan(new Vector2(10_000, 10_000));
        Assert.Equal(new Vector2(240, 136), cam.ViewTopLeft); // clamped at the far corner
    }

    [Fact]
    public void World_and_screen_coordinates_round_trip()
    {
        var cam = NewCamera();
        cam.ZoomAt(new Vector2(240, 136), +1);
        var world = new Vector2(300, 150);
        var screen = cam.WorldToScreen(world);
        Assert.Equal(world, cam.ScreenToWorld(screen));
    }

    [Fact]
    public void Zoom_is_bounded_between_one_and_four()
    {
        var cam = NewCamera();
        for (var i = 0; i < 10; i++) cam.ZoomAt(Vector2.Zero, +1);
        Assert.Equal(4, cam.Zoom);
        for (var i = 0; i < 10; i++) cam.ZoomAt(Vector2.Zero, -1);
        Assert.Equal(1, cam.Zoom);
    }
}
