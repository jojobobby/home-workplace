using System.Numerics;
using HomeWorkplace.Client;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Render;

public sealed record Shift(TimeOnly Wake, TimeOnly Sleep);

public enum DayPhase { Day, Dusk, Night }

/// <summary>
/// The room's base light by the clock: full during office hours (the team's earliest wake
/// to latest sleep), a warm fade for thirty minutes either side, blue-dark night otherwise.
/// The light map is cleared to this colour before the lights are added.
/// </summary>
public static class Ambient
{
    // A touch under white: additive lights cannot exceed white, so at 0xffffff lamps, monitors
    // and their shadows would be invisible by day. At 85% they still read in daylight.
    public static readonly Rgba Day = Rgba.Hex(0xd8d8d8);
    public static readonly Rgba Night = Rgba.Hex(0x30365a);
    private static readonly TimeSpan DuskSpan = TimeSpan.FromMinutes(30);

    public static (Rgba Colour, DayPhase Phase) For(TimeOnly now, IReadOnlyList<Shift> shifts)
    {
        var open = shifts.Count == 0 ? new TimeOnly(9, 0) : shifts.Min(s => s.Wake);
        var close = shifts.Count == 0 ? new TimeOnly(20, 0) : shifts.Max(s => s.Sleep);

        if (InWindow(now, open, close)) return (Day, DayPhase.Day);

        var dawnStart = open.Add(-DuskSpan);
        if (InWindow(now, dawnStart, open))
            return (Lerp(Night, Day, Fraction(now, dawnStart, DuskSpan)), DayPhase.Dusk);

        var duskEnd = close.Add(DuskSpan);
        if (InWindow(now, close, duskEnd))
            return (Lerp(Day, Night, Fraction(now, close, DuskSpan)), DayPhase.Dusk);

        return (Night, DayPhase.Night);
    }

    /// <summary>[start, end), wrapping midnight when start is later than end.</summary>
    private static bool InWindow(TimeOnly now, TimeOnly start, TimeOnly end)
        => start <= end ? now >= start && now < end : now >= start || now < end;

    private static float Fraction(TimeOnly now, TimeOnly start, TimeSpan span)
    {
        var elapsed = now - start;   // TimeOnly subtraction wraps midnight
        return Math.Clamp((float)(elapsed.TotalMinutes / span.TotalMinutes), 0f, 1f);
    }

    private static Rgba Lerp(Rgba a, Rgba b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), 255);
}

public enum LightKind { Lamp, Monitor, Ceiling, Coffee }

/// <summary>A point light in world pixels; radius is the falloff sprite's half-size.</summary>
public sealed record LightSource(LightKind Kind, Vector2 Position, float Radius, Rgba Colour, float Intensity);

/// <summary>Which lights are on, from what the employees are doing and the time of day.</summary>
public static class Lights
{
    private static readonly Rgba Warm = Rgba.Hex(0xffd27a);
    private static readonly Rgba Cool = Rgba.Hex(0x8fb8f0);
    private static readonly Rgba Neutral = Rgba.Hex(0xfff4dc);
    private static readonly Rgba CoffeeRed = Rgba.Hex(0xf08c7b);

    public static IReadOnlyList<LightSource> For(Simulation sim, DayPhase phase, float time)
    {
        var lights = new List<LightSource>();

        foreach (var desk in sim.World.Desks)
        {
            if (!sim.Agents.TryGetValue(desk.OwnerId, out var owner) || !owner.Visible) continue;
            var px = new Vector2(desk.Pos.X * Agent.TileSize, desk.Pos.Y * Agent.TileSize);
            if (owner.Status != EmployeeStatus.Asleep)
                lights.Add(new LightSource(LightKind.Lamp, px + new Vector2(5, 2), 56, Warm, 0.95f));
            if (owner.Activity == Activity.Typing)
            {
                var flicker = 0.85f + 0.15f * (0.5f + 0.5f * MathF.Sin(time * 37f));
                lights.Add(new LightSource(LightKind.Monitor, px + new Vector2(24, 3), 36, Cool, flicker));
            }
        }

        if (phase != DayPhase.Night)
            for (var x = 4; x < WorldLayout.Width; x += 8)
                lights.Add(new LightSource(LightKind.Ceiling,
                    new Vector2(x * Agent.TileSize + 8, 8 * Agent.TileSize + 8), 96, Neutral, phase == DayPhase.Day ? 0.35f : 0.2f));

        var coffee = sim.World.Props.First(p => p.Kind == PropKind.CoffeeMachine);
        lights.Add(new LightSource(LightKind.Coffee,
            new Vector2(coffee.Pos.X * Agent.TileSize + 9, coffee.Pos.Y * Agent.TileSize + 2), 20, CoffeeRed, 0.6f));

        return lights;
    }
}

/// <summary>
/// Shadow geometry. An occluder edge that faces a light casts a quad: the edge itself, pushed
/// away from the light out to the reach. Edges between two occluders never face open floor
/// and are skipped.
/// </summary>
public static class Shadows
{
    public static Vector2[] QuadFor(Vector2 a, Vector2 b, Vector2 light, float reach)
    {
        var da = Vector2.Normalize(a - light);
        var db = Vector2.Normalize(b - light);
        return new[] { a, b, b + db * reach, a + da * reach };
    }

    public static IEnumerable<(Vector2 A, Vector2 B)> CastingEdges(TileMap map, Vector2 light, float radius)
    {
        const int ts = Agent.TileSize;
        var reachTiles = (int)MathF.Ceiling(radius / ts) + 1;
        var centreTile = new TilePos((int)(light.X / ts), (int)(light.Y / ts));

        for (var y = centreTile.Y - reachTiles; y <= centreTile.Y + reachTiles; y++)
        for (var x = centreTile.X - reachTiles; x <= centreTile.X + reachTiles; x++)
        {
            var tile = new TilePos(x, y);
            if (!map.IsOccluder(tile)) continue;
            var centre = new Vector2(x * ts + ts / 2f, y * ts + ts / 2f);
            if (Vector2.Distance(centre, light) > radius + ts * 0.75f) continue;

            float x0 = x * ts, y0 = y * ts, x1 = x0 + ts, y1 = y0 + ts;
            var edges = new (Vector2 A, Vector2 B, Vector2 Normal, TilePos Beyond)[]
            {
                (new(x0, y0), new(x0, y1), new(-1, 0), tile.Offset(-1, 0)),
                (new(x1, y0), new(x1, y1), new(1, 0), tile.Offset(1, 0)),
                (new(x0, y0), new(x1, y0), new(0, -1), tile.Offset(0, -1)),
                (new(x0, y1), new(x1, y1), new(0, 1), tile.Offset(0, 1)),
            };
            foreach (var (a, b, normal, beyond) in edges)
            {
                if (map.IsOccluder(beyond)) continue;
                var mid = (a + b) / 2;
                if (Vector2.Dot(normal, light - mid) > 0) yield return (a, b);
            }
        }
    }
}
