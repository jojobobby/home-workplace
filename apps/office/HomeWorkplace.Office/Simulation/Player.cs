using System.Numerics;

namespace HomeWorkplace.Office.Sim;

public enum InteractKind { Employee, Whiteboard, HiringStand, TicketBoard }

/// <summary>What the player can talk to from where they stand.</summary>
public readonly record struct Interactable(InteractKind Kind, string? EmployeeId);

/// <summary>
/// You, in the office: a sprite moved directly by the keys with tile collision (sliding along
/// walls), and a talk target — the nearest employee in reach, else the whiteboard when you
/// stand in front of it. Pure C#, stepped by the game.
/// </summary>
public sealed class Player
{
    public const string Id = "you";
    public const float Speed = 60f;                          // px/s
    public const float Reach = 1.5f * Agent.TileSize;
    private const float HalfBox = 6f;                        // collision box half-extent
    private const float StepInterval = 0.35f;

    private readonly World _world;
    private float _stepTimer = StepInterval / 2;

    public Player(World world)
    {
        _world = world;
        Position = Agent.Centre(world.Spawn);
    }

    public Vector2 Position { get; private set; }
    public bool FacingLeft { get; private set; }
    public Anim Anim { get; private set; } = Anim.Idle;
    public float AnimTime { get; private set; }
    public TilePos Tile => new((int)MathF.Floor(Position.X / Agent.TileSize), (int)MathF.Floor(Position.Y / Agent.TileSize));

    public void Teleport(Vector2 position) => Position = position;

    /// <summary>Move along <paramref name="dir"/> for <paramref name="dt"/>. Returns true when a footstep is due.</summary>
    public bool Move(Vector2 dir, float dt)
    {
        AnimTime += dt;
        if (dir == Vector2.Zero)
        {
            Anim = Anim.Idle;
            _stepTimer = StepInterval / 2;
            return false;
        }

        dir = Vector2.Normalize(dir);
        Anim = Anim.Walk;
        if (dir.X < 0) FacingLeft = true;
        else if (dir.X > 0) FacingLeft = false;

        var step = dir * Speed * dt;
        var next = Position with { X = Position.X + step.X };
        if (Fits(next)) Position = next;
        next = Position with { Y = Position.Y + step.Y };
        if (Fits(next)) Position = next;

        _stepTimer -= dt;
        if (_stepTimer > 0) return false;
        _stepTimer += StepInterval;
        return true;
    }

    public Interactable? Target(Simulation sim)
    {
        var nearest = sim.Agents.Values
            .Where(a => a.Visible)
            .Select(a => (Agent: a, Distance: Vector2.Distance(a.Position, Position)))
            .Where(x => x.Distance <= Reach)
            .OrderBy(x => x.Distance)
            .Select(x => x.Agent)
            .FirstOrDefault();
        if (nearest is not null) return new Interactable(InteractKind.Employee, nearest.Id);

        if (Vector2.Distance(Position, Agent.Centre(_world.HiringSpot)) <= Reach)
            return new Interactable(InteractKind.HiringStand, null);
        if (Vector2.Distance(Position, Agent.Centre(_world.TicketSpot)) <= Reach)
            return new Interactable(InteractKind.TicketBoard, null);
        if (Vector2.Distance(Position, Agent.Centre(_world.WhiteboardSpot)) <= Reach)
            return new Interactable(InteractKind.Whiteboard, null);
        return null;
    }

    private bool Fits(Vector2 centre)
    {
        foreach (var (dx, dy) in new[] { (-HalfBox, -HalfBox), (HalfBox, -HalfBox), (-HalfBox, HalfBox), (HalfBox, HalfBox) })
        {
            var tile = new TilePos((int)MathF.Floor((centre.X + dx) / Agent.TileSize), (int)MathF.Floor((centre.Y + dy) / Agent.TileSize));
            if (!_world.Map.IsWalkable(tile)) return false;
        }
        return true;
    }
}
