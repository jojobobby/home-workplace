namespace HomeWorkplace.Office.Sim;

public enum TileKind { Floor, Wall }

/// <summary>A tile coordinate. Tiles are 16 px; the world is 30×17 of them.</summary>
public readonly record struct TilePos(int X, int Y)
{
    public TilePos Offset(int dx, int dy) => new(X + dx, Y + dy);
    public int ManhattanTo(TilePos other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// The walkable grid: a tile is walkable when it is in bounds, is floor, and no prop sits on
/// it. Walls and props are also the occluders the lighting pass casts shadows from.
/// </summary>
public sealed class TileMap
{
    private readonly TileKind[,] _tiles;
    private readonly HashSet<TilePos> _blocked = new();

    public TileMap(int width, int height)
    {
        Width = width;
        Height = height;
        _tiles = new TileKind[width, height];
    }

    public int Width { get; }
    public int Height { get; }

    public TileKind this[int x, int y]
    {
        get => _tiles[x, y];
        set => _tiles[x, y] = value;
    }

    public bool InBounds(TilePos t) => t.X >= 0 && t.Y >= 0 && t.X < Width && t.Y < Height;

    /// <summary>Mark a floor tile as occupied by a prop.</summary>
    public void Block(TilePos t) => _blocked.Add(t);

    public bool IsBlocked(TilePos t) => _blocked.Contains(t);

    public bool IsWalkable(TilePos t)
        => InBounds(t) && _tiles[t.X, t.Y] == TileKind.Floor && !_blocked.Contains(t);

    /// <summary>True when the tile stops light: a wall or a prop.</summary>
    public bool IsOccluder(TilePos t)
        => InBounds(t) && (_tiles[t.X, t.Y] == TileKind.Wall || _blocked.Contains(t));
}
