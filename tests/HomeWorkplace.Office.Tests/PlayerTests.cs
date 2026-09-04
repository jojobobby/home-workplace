using System.Numerics;
using HomeWorkplace.Client;
using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Tests;

public class PlayerTests
{
    private static (World World, Simulation Sim) Office()
    {
        var world = WorldLayout.Generate(new[] { "ada-coder", "mia-manager" });
        var sim = new Simulation(world, seed: 1);
        sim.Apply(new EmployeeAppeared("ada-coder", "Ada", EmployeeStatus.Working, "Build"));
        sim.Apply(new EmployeeAppeared("mia-manager", "Mia", EmployeeStatus.Awake, null));
        for (var i = 0; i < 60 * 40; i++) sim.Update(1f / 60f);
        return (world, sim);
    }

    [Fact]
    public void Spawns_at_the_door_facing_right_and_idle()
    {
        var (world, _) = Office();
        var player = new Player(world);
        Assert.Equal(Agent.Centre(world.Spawn), player.Position);
        Assert.False(player.FacingLeft);
        Assert.Equal(Anim.Idle, player.Anim);
    }

    [Fact]
    public void Moves_at_its_speed_and_animates_walking()
    {
        var (world, _) = Office();
        var player = new Player(world);
        var start = player.Position;

        player.Move(new Vector2(1, 0), 0.1f);
        Assert.Equal(start.X + Player.Speed * 0.1f, player.Position.X, 3);
        Assert.Equal(Anim.Walk, player.Anim);
        Assert.False(player.FacingLeft);

        player.Move(new Vector2(-1, 0), 0.1f);
        Assert.True(player.FacingLeft);

        player.Move(Vector2.Zero, 0.1f);
        Assert.Equal(Anim.Idle, player.Anim);
    }

    [Fact]
    public void A_wall_stops_one_axis_while_the_other_slides()
    {
        var (world, _) = Office();
        var player = new Player(world);
        var start = player.Position;                 // the door tile is against the left wall

        player.Move(Vector2.Normalize(new Vector2(-1, -1)), 0.1f);

        Assert.Equal(start.X, player.Position.X, 3);  // blocked by the wall
        Assert.True(player.Position.Y < start.Y, "slides up along the wall");
    }

    [Fact]
    public void Desks_block_the_player()
    {
        var (world, _) = Office();
        var player = new Player(world);
        var desk = world.DeskOf("ada-coder")!;
        player.Teleport(Agent.Centre(desk.Seat));    // standing in front of the desk

        for (var i = 0; i < 30; i++) player.Move(new Vector2(0, -1), 0.05f);

        Assert.True(player.Tile.Y >= desk.Seat.Y, "cannot walk through the desk");
    }

    [Fact]
    public void The_nearest_employee_in_reach_is_the_talk_target()
    {
        var (world, sim) = Office();
        var player = new Player(world);
        var ada = sim.Agents["ada-coder"];

        player.Teleport(ada.Position + new Vector2(Agent.TileSize, 0));
        Assert.Equal(new Interactable(InteractKind.Employee, "ada-coder"), player.Target(sim));

        player.Teleport(ada.Position + new Vector2(Player.Reach + 4, 0));
        Assert.Null(player.Target(sim));
    }

    [Fact]
    public void The_whiteboard_is_a_target_when_standing_in_front_of_it()
    {
        var (world, sim) = Office();
        var player = new Player(world);
        player.Teleport(Agent.Centre(world.WhiteboardSpot));
        Assert.Equal(new Interactable(InteractKind.Whiteboard, null), player.Target(sim));
    }

    [Fact]
    public void Footsteps_are_due_every_step_interval_while_walking()
    {
        var (world, _) = Office();
        var player = new Player(world);
        var steps = 0;
        for (var i = 0; i < 60; i++) if (player.Move(new Vector2(1, 0), 1f / 60f)) steps++;
        Assert.InRange(steps, 2, 4);
        Assert.False(player.Move(Vector2.Zero, 1f));
    }
}

public class CameraFollowTests
{
    [Fact]
    public void Follow_centres_on_the_point_within_the_world()
    {
        var camera = new Camera(480, 272);
        camera.ZoomAt(new Vector2(240, 136), +1);   // zoom 2: view 240×136
        camera.Follow(new Vector2(100, 60));
        Assert.Equal(new Vector2(0, 0), camera.ViewTopLeft);   // clamped at the corner
        camera.Follow(new Vector2(240, 136));
        Assert.Equal(new Vector2(120, 68), camera.ViewTopLeft);
    }
}
