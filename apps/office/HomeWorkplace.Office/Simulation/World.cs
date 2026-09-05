namespace HomeWorkplace.Office.Sim;

public enum PropKind { Desk, CoffeeMachine, Whiteboard, Plant, HiringStand }

/// <summary>A placed prop; size in tiles. Desks carry their owner.</summary>
public sealed record Prop(PropKind Kind, TilePos Pos, int Width, int Height, string? OwnerId);

/// <summary>A 2×1 desk and the floor tile in front of it where its owner sits.</summary>
public sealed record Desk(string OwnerId, TilePos Pos, TilePos Seat);

public sealed class World
{
    public World(TileMap map, IReadOnlyList<Desk> desks, IReadOnlyList<Prop> props,
                 TilePos spawn, TilePos coffeeSpot, TilePos whiteboardSpot, TilePos hiringSpot)
    {
        Map = map;
        Desks = desks;
        Props = props;
        Spawn = spawn;
        CoffeeSpot = coffeeSpot;
        WhiteboardSpot = whiteboardSpot;
        HiringSpot = hiringSpot;
    }

    /// <summary>The floor tile in front of the hiring stand.</summary>
    public TilePos HiringSpot { get; }

    public TileMap Map { get; }
    public IReadOnlyList<Desk> Desks { get; }
    public IReadOnlyList<Prop> Props { get; }
    /// <summary>Where agents enter and leave: the floor tile inside the door, bottom-left.</summary>
    public TilePos Spawn { get; }
    /// <summary>The floor tile in front of the coffee machine.</summary>
    public TilePos CoffeeSpot { get; }
    /// <summary>The floor tile in front of the whiteboard.</summary>
    public TilePos WhiteboardSpot { get; }

    public Desk? DeskOf(string employeeId) => Desks.FirstOrDefault(d => d.OwnerId == employeeId);
}

/// <summary>
/// Generates the office from the employee list, deterministically: sorted ids, a border of
/// walls, desks in rows of six (y = 4, 9, 14), a coffee corner top-right, a whiteboard on
/// the top wall, plants in the top corners, the door bottom-left. Same ids ⇒ same world, so
/// golden images are reproducible.
/// </summary>
public static class WorldLayout
{
    public const int Width = 30;
    public const int Height = 17;
    public const int DesksPerRow = 6;
    public const int MaxEmployees = 18;

    /// <summary>
    /// Desk rows spaced evenly in the band between the coffee corner and the door row, so a
    /// small team sits mid-room rather than under an empty field. Seats are the row below.
    /// </summary>
    private static int[] RowYs(int rows) => rows switch
    {
        1 => new[] { 9 },
        2 => new[] { 6, 12 },
        _ => new[] { 5, 9, 13 },
    };

    public static World Generate(IEnumerable<string> employeeIds)
    {
        var ids = employeeIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (ids.Count > MaxEmployees)
            throw new ArgumentException($"The office seats at most {MaxEmployees} employees; {ids.Count} given.");

        var map = new TileMap(Width, Height);
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            map[x, y] = x == 0 || y == 0 || x == Width - 1 || y == Height - 1 ? TileKind.Wall : TileKind.Floor;

        var props = new List<Prop>();
        var desks = new List<Desk>();
        var rowYs = RowYs(Math.Max(1, (ids.Count + DesksPerRow - 1) / DesksPerRow));

        for (var i = 0; i < ids.Count; i++)
        {
            var row = i / DesksPerRow;
            var col = i % DesksPerRow;
            var pos = new TilePos(3 + col * 4, rowYs[row]);
            desks.Add(new Desk(ids[i], pos, pos.Offset(0, 1)));
            props.Add(new Prop(PropKind.Desk, pos, 2, 1, ids[i]));
        }

        var coffee = new Prop(PropKind.CoffeeMachine, new TilePos(26, 1), 2, 1, null);
        var whiteboard = new Prop(PropKind.Whiteboard, new TilePos(12, 0), 6, 1, null);   // hangs on the top wall
        var stand = new Prop(PropKind.HiringStand, new TilePos(3, 15), 2, 1, null);      // by the door
        props.Add(coffee);
        props.Add(whiteboard);
        props.Add(stand);
        props.Add(new Prop(PropKind.Plant, new TilePos(1, 1), 1, 1, null));
        props.Add(new Prop(PropKind.Plant, new TilePos(28, 1), 1, 1, null));

        foreach (var p in props)
            for (var dx = 0; dx < p.Width; dx++)
            for (var dy = 0; dy < p.Height; dy++)
                map.Block(p.Pos.Offset(dx, dy));

        return new World(map, desks, props,
            spawn: new TilePos(1, 15),
            coffeeSpot: new TilePos(26, 2),
            whiteboardSpot: new TilePos(14, 1),
            hiringSpot: new TilePos(3, 14));
    }
}
