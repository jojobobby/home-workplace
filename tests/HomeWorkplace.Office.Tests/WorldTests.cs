using HomeWorkplace.Office.Simulation;

namespace HomeWorkplace.Office.Tests;

public class WorldTests
{
    [Fact]
    public void Layout_places_one_desk_per_employee_and_is_stable_regardless_of_id_order()
    {
        var a = WorldLayout.Generate(new[] { "rex", "ada", "mia" });
        var b = WorldLayout.Generate(new[] { "mia", "rex", "ada" });

        Assert.Equal(3, a.Desks.Count);
        Assert.True(a.Desks.SequenceEqual(b.Desks));
        Assert.True(a.Props.SequenceEqual(b.Props));
        Assert.Equal(new[] { "ada", "mia", "rex" }, a.Desks.Select(d => d.OwnerId));
    }

    [Fact]
    public void Nothing_overlaps_and_everything_is_inside_the_walls()
    {
        var w = WorldLayout.Generate(Enumerable.Range(0, 12).Select(i => $"e{i:00}"));
        var occupied = new HashSet<TilePos>();

        foreach (var p in w.Props)
        {
            for (var dx = 0; dx < p.Width; dx++)
            for (var dy = 0; dy < p.Height; dy++)
            {
                var t = new TilePos(p.Pos.X + dx, p.Pos.Y + dy);
                Assert.True(w.Map.InBounds(t), $"{p.Kind} at {t} is out of bounds");
                Assert.True(occupied.Add(t), $"{p.Kind} overlaps at {t}");
            }
        }
        foreach (var d in w.Desks)
        {
            Assert.True(w.Map.IsWalkable(d.Seat), $"seat {d.Seat} of {d.OwnerId} must be walkable");
            Assert.False(w.Map.IsWalkable(d.Pos), "a desk tile must block");
        }
    }

    [Fact]
    public void Desks_wrap_to_a_second_row_past_six()
    {
        var w = WorldLayout.Generate(Enumerable.Range(0, 8).Select(i => $"e{i}"));
        Assert.Equal(2, w.Desks.Select(d => d.Pos.Y).Distinct().Count());
        Assert.Throws<ArgumentException>(() => WorldLayout.Generate(Enumerable.Range(0, 19).Select(i => $"e{i}")));
    }

    [Fact]
    public void Tile_map_walkability()
    {
        var w = WorldLayout.Generate(new[] { "ada" });
        Assert.False(w.Map.IsWalkable(new TilePos(0, 5)));          // wall
        Assert.True(w.Map.IsWalkable(new TilePos(15, 8)));          // floor
        Assert.False(w.Map.IsWalkable(w.Desks[0].Pos));             // under a desk
        Assert.False(w.Map.IsWalkable(new TilePos(-1, 3)));         // out of bounds
    }

    [Fact]
    public void AStar_routes_around_a_desk_with_four_neighbour_steps()
    {
        var w = WorldLayout.Generate(new[] { "ada" });
        var desk = w.Desks[0];
        var from = new TilePos(desk.Pos.X - 1, desk.Pos.Y);
        var to = new TilePos(desk.Pos.X + 2, desk.Pos.Y);

        var path = AStar.FindPath(w.Map, from, to);

        Assert.NotNull(path);
        Assert.Equal(from, path![0]);
        Assert.Equal(to, path[^1]);
        Assert.All(path, t => Assert.True(w.Map.IsWalkable(t)));
        for (var i = 1; i < path.Count; i++)
            Assert.Equal(1, Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y));
    }

    [Fact]
    public void AStar_returns_null_when_the_target_is_enclosed()
    {
        var map = new TileMap(7, 7);
        foreach (var (x, y) in new[] { (2, 2), (3, 2), (4, 2), (2, 3), (4, 3), (2, 4), (3, 4), (4, 4) })
            map[x, y] = TileKind.Wall;

        Assert.Null(AStar.FindPath(map, new TilePos(0, 0), new TilePos(3, 3)));
        Assert.NotNull(AStar.FindPath(map, new TilePos(0, 0), new TilePos(6, 6)));
    }

    [Fact]
    public void Every_seat_and_spot_is_reachable_from_the_door()
    {
        var w = WorldLayout.Generate(Enumerable.Range(0, 18).Select(i => $"e{i:00}"));
        foreach (var d in w.Desks)
            Assert.NotNull(AStar.FindPath(w.Map, w.Spawn, d.Seat));
        Assert.NotNull(AStar.FindPath(w.Map, w.Spawn, w.CoffeeSpot));
        Assert.NotNull(AStar.FindPath(w.Map, w.Spawn, w.WhiteboardSpot));
    }
}
