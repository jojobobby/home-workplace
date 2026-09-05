using System.Numerics;
using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Sim;

/// <summary>
/// The office, alive. Deterministic for a seed: agents walk the tile map, sit, type, fetch
/// coffee, talk to teammates, and go home, driven by commands from the feed; moments record
/// the effects the renderer and jukebox should play. No engine types anywhere in here.
/// </summary>
public sealed class Simulation
{
    public const float WalkSpeed = 40f;          // px/s
    private const float StepInterval = 0.35f;
    private const float CoffeeTime = 3f;
    private const float TalkTime = 3f;
    private const float SparkInterval = 2f;
    private const float KeyInterval = 0.09f;
    private const float BoardTime = 1.2f;         // taking a ticket off the board
    private const float SteamInterval = 1f;

    private readonly Dictionary<string, Agent> _agents = new(StringComparer.Ordinal);
    private readonly List<Moment> _moments = new();
    private readonly Random _rng;

    public Simulation(World world, int seed)
    {
        World = world;
        _rng = new Random(seed);
    }

    public World World { get; }
    public IReadOnlyDictionary<string, Agent> Agents => _agents;
    public IReadOnlyList<Moment> Moments => _moments;
    public float Elapsed { get; private set; }

    // ---- commands --------------------------------------------------------------------

    public void Apply(SimCommand command)
    {
        switch (command)
        {
            case EmployeeAppeared a:
                var agent = new Agent(a.Id, a.Name, a.Status, a.TaskTitle, World.Spawn);
                _agents[a.Id] = agent;
                if (a.Status == EmployeeStatus.Asleep) agent.Activity = Activity.Absent;
                else Arrive(agent);
                break;

            case EmployeeStatusChanged c when _agents.TryGetValue(c.Id, out var ag):
                ag.Status = c.Status;
                ag.TaskTitle = c.TaskTitle;
                ag.WaitingOn = c.WaitingOn;
                if (ag.Bubble is { Persistent: true }) ag.Bubble = null;   // "needs you" resolved by the state change
                if (!ag.OnErrand) Retarget(ag);
                break;

            case EmployeeLeft l:
                _agents.Remove(l.Id);
                break;

            case HandoffRequested h when _agents.TryGetValue(h.FromId, out var from) && from.Visible:
                from.OnErrand = true;
                from.Bubble = new Bubble(BubbleKind.Question, TalkTime + 1f, persistent: false);
                WalkTo(from, BesideDeskOf(h.ToId), Activity.WalkingToTeammate);
                break;

            case HandoffAnswered ans when _agents.TryGetValue(ans.Id, out var to):
                to.Bubble = new Bubble(BubbleKind.Exclaim, 3f, persistent: false);
                Emit(MomentKind.Sound, "chime", At(to), 0.5f);
                break;

            case HumanNeeded hn when _agents.TryGetValue(hn.Id, out var who):
                who.Bubble = new Bubble(BubbleKind.Exclaim, 0f, persistent: true);
                Emit(MomentKind.Sound, "chime", At(who), 0.5f);
                break;

            case RunFinished r:
                var desk = World.DeskOf(r.Id)?.Pos ?? World.Spawn;
                if (r.Succeeded)
                {
                    Emit(MomentKind.Particles, "sparkle", desk, 1.5f);
                    Emit(MomentKind.Sound, "ding", desk, 0.5f);
                }
                else
                {
                    Emit(MomentKind.Shake, "fail", desk, 0.4f);
                    Emit(MomentKind.Particles, "smoke", desk, 1.5f);
                    Emit(MomentKind.Sound, "buzz", desk, 0.5f);
                }
                break;

            case WrapUpWritten w when _agents.TryGetValue(w.Id, out var wr):
                Emit(MomentKind.Sound, "page", At(wr), 0.5f);
                break;

            case TicketClaimed tc when _agents.TryGetValue(tc.Id, out var taker) && taker.Visible:
                taker.OnErrand = true;
                taker.Bubble = null;
                WalkTo(taker, World.TicketSpot, Activity.WalkingToBoard);
                break;

            case TicketsChanged t:
                OpenTickets = Math.Max(0, t.Count);
                break;
        }
    }

    /// <summary>Tickets pinned on the board right now (the renderer picks the board sprite by it).</summary>
    public int OpenTickets { get; private set; }

    // ---- tick ------------------------------------------------------------------------

    public void Update(float dt)
    {
        Elapsed += dt;

        foreach (var m in _moments) m.Age += dt;
        _moments.RemoveAll(m => m.Expired);

        foreach (var agent in _agents.Values)
        {
            agent.AnimTime += dt;
            if (agent.Bubble is { Persistent: false } b)
            {
                b.Ttl -= dt;
                if (b.Ttl <= 0) agent.Bubble = null;
            }

            switch (agent.Activity)
            {
                case Activity.Arriving:
                case Activity.WalkingToCoffee:
                case Activity.WalkingBack:
                case Activity.WalkingToTeammate:
                case Activity.WalkingHome:
                case Activity.WalkingToBoard:
                    Walk(agent, dt);
                    break;

                case Activity.IdleAtDesk:
                    agent.WanderTimer -= dt;
                    if (agent.WanderTimer <= 0 && agent.Status == EmployeeStatus.Awake)
                        WalkTo(agent, World.CoffeeSpot, Activity.WalkingToCoffee);
                    break;

                case Activity.AtCoffee:
                    agent.ActivityTimer -= dt;
                    agent.EffectTimer -= dt;
                    if (agent.EffectTimer <= 0)
                    {
                        Emit(MomentKind.Particles, "steam", CoffeeMachineTile(), 1f);
                        agent.EffectTimer = SteamInterval;
                    }
                    if (agent.ActivityTimer <= 0) WalkTo(agent, SeatOf(agent), Activity.WalkingBack);
                    break;

                case Activity.AtBoard:
                    agent.ActivityTimer -= dt;
                    if (agent.ActivityTimer <= 0)
                    {
                        agent.OnErrand = false;
                        agent.Bubble = null;
                        Retarget(agent);   // back to whatever the status calls for (typing, usually)
                    }
                    break;

                case Activity.Typing:
                    agent.EffectTimer -= dt;
                    if (agent.EffectTimer <= 0)
                    {
                        Emit(MomentKind.Particles, "sparks", World.DeskOf(agent.Id)?.Pos ?? agent.Tile, 0.6f);
                        agent.EffectTimer = SparkInterval;
                    }
                    agent.KeyTimer -= dt;
                    if (agent.KeyTimer <= 0)
                    {
                        Emit(MomentKind.Sound, "keys", agent.Tile, 0.2f);
                        // bursts of typing with pauses between: mostly quick, sometimes a breath
                        agent.KeyTimer = _rng.NextDouble() < 0.15 ? 0.6f + (float)_rng.NextDouble() * 0.8f : KeyInterval + (float)_rng.NextDouble() * KeyInterval;
                    }
                    break;

                case Activity.Talking when agent.OnErrand:
                    agent.ActivityTimer -= dt;
                    if (agent.ActivityTimer <= 0)
                    {
                        agent.OnErrand = false;
                        agent.Bubble = null;
                        WalkTo(agent, SeatOf(agent), Activity.WalkingBack);
                    }
                    break;
            }
        }
    }

    // ---- behaviour -------------------------------------------------------------------

    private void Arrive(Agent agent)
    {
        agent.Position = Agent.Centre(World.Spawn);
        WalkTo(agent, SeatOf(agent), Activity.Arriving);
    }

    /// <summary>Re-aim an agent after its Foreman status changed.</summary>
    private void Retarget(Agent agent)
    {
        switch (agent.Status)
        {
            case EmployeeStatus.Asleep:
                if (agent.Activity != Activity.Absent) WalkTo(agent, World.Spawn, Activity.WalkingHome);
                break;
            case EmployeeStatus.Waiting:
                if (agent.Activity == Activity.Absent) Arrive(agent);
                else WalkTo(agent, BesideDeskOf(agent.WaitingOn ?? agent.Id), Activity.WalkingToTeammate);
                break;
            default:   // Awake, Working
                if (agent.Activity == Activity.Absent) Arrive(agent);
                else if (agent.Tile == SeatOf(agent)) Settle(agent);
                else WalkTo(agent, SeatOf(agent), Activity.WalkingBack);
                break;
        }
    }

    /// <summary>At the desk: the activity the status calls for.</summary>
    private void Settle(Agent agent)
    {
        agent.Path.Clear();
        switch (agent.Status)
        {
            case EmployeeStatus.Working:
                agent.Activity = Activity.Typing;
                agent.Anim = Anim.Type;
                agent.EffectTimer = SparkInterval;
                break;
            case EmployeeStatus.Waiting:
                WalkTo(agent, BesideDeskOf(agent.WaitingOn ?? agent.Id), Activity.WalkingToTeammate);
                break;
            case EmployeeStatus.Asleep:
                WalkTo(agent, World.Spawn, Activity.WalkingHome);
                break;
            default:
                agent.Activity = Activity.IdleAtDesk;
                agent.Anim = Anim.Idle;
                agent.WanderTimer = 20f + (float)_rng.NextDouble() * 40f;
                break;
        }
        agent.FacingLeft = false;
    }

    private void OnArrived(Agent agent)
    {
        switch (agent.Activity)
        {
            case Activity.Arriving:
            case Activity.WalkingBack:
                Settle(agent);
                break;
            case Activity.WalkingToCoffee:
                agent.Activity = Activity.AtCoffee;
                agent.Anim = Anim.Idle;
                agent.ActivityTimer = CoffeeTime;
                agent.EffectTimer = 0f;
                Emit(MomentKind.Sound, "pour", CoffeeMachineTile(), 0.5f);
                break;
            case Activity.WalkingToTeammate:
                agent.Activity = Activity.Talking;
                agent.Anim = Anim.Talk;
                agent.ActivityTimer = TalkTime;
                if (!agent.OnErrand) agent.Bubble = new Bubble(BubbleKind.Dots, 0f, persistent: true);
                break;
            case Activity.WalkingHome:
                agent.Activity = Activity.Absent;
                agent.Anim = Anim.Idle;
                agent.Bubble = null;
                Emit(MomentKind.Sound, "door", World.Spawn, 0.5f);
                break;
            case Activity.WalkingToBoard:
                agent.Activity = Activity.AtBoard;
                agent.Anim = Anim.Idle;
                agent.ActivityTimer = BoardTime;
                agent.Bubble = new Bubble(BubbleKind.Exclaim, BoardTime + 0.5f, persistent: false);
                Emit(MomentKind.Sound, "page", World.TicketSpot, 0.5f);
                break;
        }
    }

    private void WalkTo(Agent agent, TilePos target, Activity walking)
    {
        var path = AStar.FindPath(World.Map, agent.Tile, target);
        agent.Path = path is null ? new List<TilePos> { target } : new List<TilePos>(path.Skip(1));
        agent.PathIndex = 0;
        agent.Activity = walking;
        agent.Anim = Anim.Walk;
        agent.StepTimer = 0f;
        if (agent.Bubble is { Persistent: true, Kind: BubbleKind.Dots }) agent.Bubble = null;
        if (agent.Path.Count == 0) OnArrived(agent);
    }

    private void Walk(Agent agent, float dt)
    {
        if (agent.PathIndex >= agent.Path.Count) { OnArrived(agent); return; }

        var target = Agent.Centre(agent.Path[agent.PathIndex]);
        var delta = target - agent.Position;
        var distance = delta.Length();
        var step = WalkSpeed * dt;

        if (delta.X < -0.01f) agent.FacingLeft = true;
        else if (delta.X > 0.01f) agent.FacingLeft = false;

        if (distance <= step)
        {
            agent.Position = target;
            agent.PathIndex++;
            if (agent.PathIndex >= agent.Path.Count) OnArrived(agent);
        }
        else
        {
            agent.Position += Vector2.Normalize(delta) * step;
        }

        agent.StepTimer -= dt;
        if (agent.StepTimer <= 0)
        {
            Emit(MomentKind.Sound, "footstep", agent.Tile, 0.3f);
            agent.StepTimer = StepInterval;
        }
    }

    // ---- helpers ---------------------------------------------------------------------

    private TilePos SeatOf(Agent agent) => World.DeskOf(agent.Id)?.Seat ?? World.Spawn;

    /// <summary>A walkable tile next to a teammate's seat — beside it, else in front of it.</summary>
    private TilePos BesideDeskOf(string employeeId)
    {
        var seat = World.DeskOf(employeeId)?.Seat ?? World.WhiteboardSpot;
        foreach (var candidate in new[] { seat.Offset(1, 0), seat.Offset(-1, 0), seat.Offset(0, 1) })
            if (World.Map.IsWalkable(candidate)) return candidate;
        return seat;
    }

    private TilePos CoffeeMachineTile()
        => World.Props.First(p => p.Kind == PropKind.CoffeeMachine).Pos;

    private TilePos At(Agent agent) => agent.Visible ? agent.Tile : (World.DeskOf(agent.Id)?.Pos ?? World.Spawn);

    private void Emit(MomentKind kind, string detail, TilePos at, float ttl)
        => _moments.Add(new Moment(kind, detail, at, ttl));
}
