namespace HomeWorkplace.Office.Sim;

/// <summary>Grid A* with four-neighbour moves and a Manhattan heuristic. Returns the path from
/// <c>from</c> to <c>to</c> inclusive, or null when no path exists.</summary>
public static class AStar
{
    private static readonly (int dx, int dy)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    public static IReadOnlyList<TilePos>? FindPath(TileMap map, TilePos from, TilePos to)
    {
        if (!map.IsWalkable(from) || !map.IsWalkable(to)) return null;
        if (from == to) return new[] { from };

        var open = new PriorityQueue<TilePos, int>();
        var cameFrom = new Dictionary<TilePos, TilePos>();
        var gScore = new Dictionary<TilePos, int> { [from] = 0 };
        var closed = new HashSet<TilePos>();
        open.Enqueue(from, from.ManhattanTo(to));

        while (open.TryDequeue(out var current, out _))
        {
            if (current == to) return Reconstruct(cameFrom, current);
            if (!closed.Add(current)) continue;

            foreach (var (dx, dy) in Steps)
            {
                var next = current.Offset(dx, dy);
                if (!map.IsWalkable(next) || closed.Contains(next)) continue;

                var tentative = gScore[current] + 1;
                if (gScore.TryGetValue(next, out var known) && tentative >= known) continue;

                gScore[next] = tentative;
                cameFrom[next] = current;
                open.Enqueue(next, tentative + next.ManhattanTo(to));
            }
        }

        return null;
    }

    private static IReadOnlyList<TilePos> Reconstruct(Dictionary<TilePos, TilePos> cameFrom, TilePos end)
    {
        var path = new List<TilePos> { end };
        while (cameFrom.TryGetValue(end, out var prev))
        {
            path.Add(prev);
            end = prev;
        }
        path.Reverse();
        return path;
    }
}
