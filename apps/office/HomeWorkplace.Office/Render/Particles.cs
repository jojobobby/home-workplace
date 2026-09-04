using System.Numerics;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Render;

/// <summary>One particle. Plain data; the renderer draws it as a scaled pixel.</summary>
public sealed class Particle
{
    public Vector2 Position;
    public Vector2 Velocity;
    /// <summary>Pixels per second squared, downward positive; negative floats up.</summary>
    public float Gravity;
    public float Life;
    public float Age;
    public Rgba Colour;
    public float Size;
    public bool Additive;

    public float Alpha => Math.Clamp(1f - Age / Life, 0f, 1f);
    public bool Dead => Age >= Life;
}

/// <summary>Where ambient dust may appear, in world pixels.</summary>
public readonly record struct DustArea(int X, int Y, int W, int H);

/// <summary>
/// A pooled particle system fed by the simulation's moments. Each moment instance spawns
/// exactly once; particles age, fade, and die; the pool is capped by dropping the oldest.
/// Deterministic for a seed, so golden frames are reproducible.
/// </summary>
public sealed class ParticleSystem
{
    public const int MaxParticles = 2000;
    private const float DustInterval = 0.25f;
    private const int DustInset = 8;

    private readonly Random _rng;
    private readonly List<Particle> _live = new();
    private readonly HashSet<Moment> _seen = new(ReferenceEqualityComparer.Instance);
    private float _dustTimer;

    public ParticleSystem(int seed) => _rng = new Random(seed);

    public IReadOnlyList<Particle> Live => _live;
    public bool DustEnabled { get; set; }
    public DustArea? DustArea { get; set; }

    /// <summary>Spawn from every particle moment not yet seen. Returns how many moments spawned.</summary>
    public int Consume(IReadOnlyList<Moment> moments)
    {
        _seen.RemoveWhere(m => m.Expired);
        var spawned = 0;
        foreach (var m in moments)
        {
            if (m.Kind != MomentKind.Particles || !_seen.Add(m)) continue;
            Spawn(m.Detail, Agent.Centre(m.At));
            spawned++;
        }
        return spawned;
    }

    public void Spawn(string detail, Vector2 at)
    {
        switch (detail)
        {
            case "steam":
                Burst(8, at, vx: (-6, 6), vy: (-30, -18), gravity: -4, life: (1.2f, 1.8f), Rgba.Hex(0xffffff, 140), size: (1, 2), additive: false);
                break;
            case "sparks":
                Burst(6, at + new Vector2(14, 8), vx: (-30, 30), vy: (-40, -10), gravity: 120, life: (0.3f, 0.6f), Rgba.Hex(0xf0d78c), size: (1, 1), additive: true);
                break;
            case "sparkle":
                for (var i = 0; i < 14; i++)
                {
                    var angle = (float)(_rng.NextDouble() * Math.PI * 2);
                    var speed = Range(20, 45);
                    Add(at + new Vector2(16, 8), new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed, 0, Range(0.6f, 1.0f),
                        i % 3 == 0 ? Rgba.Hex(0xffffff) : Rgba.Hex(0xf0d78c), Range(1, 2), additive: true);
                }
                break;
            case "smoke":
                Burst(10, at + new Vector2(16, 6), vx: (-8, 8), vy: (-20, -12), gravity: -3, life: (1.5f, 2.5f), Rgba.Hex(0x8c8c9a, 160), size: (2, 3), additive: false);
                break;
            case "footstep":
                Burst(3, at + new Vector2(8, 15), vx: (-10, 10), vy: (-8, -8), gravity: 30, life: (0.25f, 0.25f), Rgba.Hex(0x9c7248, 120), size: (1, 1), additive: false);
                break;
            case "dust":
                Add(at, new Vector2(Range(-3, 3), Range(-2, 2)), 0, Range(6, 10), Rgba.Hex(0xffffff, 60), 1, additive: false);
                break;
        }
    }

    public void Update(float dt)
    {
        foreach (var p in _live)
        {
            p.Velocity.Y += p.Gravity * dt;
            p.Position += p.Velocity * dt;
            p.Age += dt;
        }
        _live.RemoveAll(p => p.Dead);

        if (DustEnabled && DustArea is { } area)
        {
            _dustTimer -= dt;
            while (_dustTimer <= 0)
            {
                Spawn("dust", new Vector2(
                    Range(area.X + DustInset, area.X + area.W - DustInset),
                    Range(area.Y + DustInset, area.Y + area.H - DustInset)));
                _dustTimer += DustInterval;
            }
        }
    }

    private void Burst(int count, Vector2 at, (float, float) vx, (float, float) vy, float gravity, (float, float) life, Rgba colour, (float, float) size, bool additive)
    {
        for (var i = 0; i < count; i++)
            Add(at, new Vector2(Range(vx.Item1, vx.Item2), Range(vy.Item1, vy.Item2)), gravity, Range(life.Item1, life.Item2), colour, Range(size.Item1, size.Item2), additive);
    }

    private void Add(Vector2 position, Vector2 velocity, float gravity, float life, Rgba colour, float size, bool additive)
    {
        if (_live.Count >= MaxParticles) _live.RemoveAt(0);
        _live.Add(new Particle { Position = position, Velocity = velocity, Gravity = gravity, Life = life, Colour = colour, Size = size, Additive = additive });
    }

    private float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);
}
