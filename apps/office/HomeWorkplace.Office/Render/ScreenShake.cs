using System.Numerics;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Render;

/// <summary>A decaying random camera offset, triggered by shake moments (a failed run). Exactly zero when idle.</summary>
public sealed class ScreenShake
{
    private const float MomentStrength = 4f;

    private readonly Random _rng;
    private readonly HashSet<Moment> _seen = new(ReferenceEqualityComparer.Instance);
    private float _strength;
    private float _duration;
    private float _remaining;

    public ScreenShake(int seed) => _rng = new Random(seed);

    public Vector2 Offset { get; private set; }

    public void Trigger(float strength, float duration)
    {
        _strength = Math.Max(_strength, strength);
        _duration = Math.Max(_duration, duration);
        _remaining = Math.Max(_remaining, duration);
    }

    /// <summary>Trigger from every shake moment not yet seen. Returns how many were new.</summary>
    public int Consume(IReadOnlyList<Moment> moments)
    {
        _seen.RemoveWhere(m => m.Expired);
        var triggered = 0;
        foreach (var m in moments)
        {
            if (m.Kind != MomentKind.Shake || !_seen.Add(m)) continue;
            Trigger(MomentStrength, m.Ttl);
            triggered++;
        }
        return triggered;
    }

    public void Update(float dt)
    {
        if (_remaining <= 0f)
        {
            Offset = Vector2.Zero;
            _strength = 0f;
            _duration = 0f;
            return;
        }

        _remaining -= dt;
        if (_remaining <= 0f)
        {
            Offset = Vector2.Zero;
            _strength = 0f;
            _duration = 0f;
            return;
        }

        var t = _duration <= 0f ? 0f : Math.Clamp(_remaining / _duration, 0f, 1f);
        Offset = new Vector2(
            ((float)_rng.NextDouble() * 2f - 1f) * _strength * t,
            ((float)_rng.NextDouble() * 2f - 1f) * _strength * t);
    }
}
