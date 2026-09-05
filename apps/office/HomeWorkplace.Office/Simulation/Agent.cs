using System.Numerics;
using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Sim;

public enum Activity { Absent, Arriving, IdleAtDesk, WalkingToCoffee, AtCoffee, WalkingBack, Typing, WalkingToTeammate, Talking, WalkingHome, WalkingToBoard, AtBoard }

public enum Anim { Idle, Walk, Type, Talk }

public enum BubbleKind { Question, Exclaim, Dots }

public sealed class Bubble
{
    public Bubble(BubbleKind kind, float ttl, bool persistent) { Kind = kind; Ttl = ttl; Persistent = persistent; }
    public BubbleKind Kind { get; }
    public float Ttl { get; internal set; }
    /// <summary>A persistent bubble stays until the employee's state changes (used for "needs you").</summary>
    public bool Persistent { get; }
}

/// <summary>One employee in the office. Positions are pixels (tile = 16 px); no engine types here.</summary>
public sealed class Agent
{
    public const int TileSize = 16;

    public Agent(string id, string name, EmployeeStatus status, string? taskTitle, TilePos at)
    {
        Id = id;
        Name = name;
        Status = status;
        TaskTitle = taskTitle;
        Position = Centre(at);
    }

    public string Id { get; }
    public string Name { get; }
    public EmployeeStatus Status { get; internal set; }
    public string? TaskTitle { get; internal set; }
    public string? WaitingOn { get; internal set; }

    public Vector2 Position { get; internal set; }
    public TilePos Tile => new((int)MathF.Floor(Position.X / TileSize), (int)MathF.Floor(Position.Y / TileSize));

    public Activity Activity { get; internal set; } = Activity.Absent;
    public Anim Anim { get; internal set; } = Anim.Idle;
    public bool FacingLeft { get; internal set; }
    /// <summary>Seconds in the current animation, for frame selection.</summary>
    public float AnimTime { get; internal set; }
    public Bubble? Bubble { get; internal set; }
    public bool Visible => Activity != Activity.Absent;

    // ---- movement and timers, owned by the simulation ----
    internal List<TilePos> Path { get; set; } = new();
    internal int PathIndex { get; set; }
    internal float WanderTimer { get; set; }
    internal float ActivityTimer { get; set; }
    internal float StepTimer { get; set; }
    internal float KeyTimer { get; set; }
    internal float EffectTimer { get; set; }
    /// <summary>True while on a hand-off errand; the status behaviour resumes after it.</summary>
    internal bool OnErrand { get; set; }

    public static Vector2 Centre(TilePos t) => new(t.X * TileSize + TileSize / 2f, t.Y * TileSize + TileSize / 2f);
}
