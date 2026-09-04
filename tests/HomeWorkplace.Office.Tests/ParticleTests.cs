using System.Numerics;
using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Tests;

public class ParticleTests
{
    private static Moment ParticlesAt(string detail, int x = 5, int y = 5) => new(MomentKind.Particles, detail, new TilePos(x, y), 1.5f);

    [Fact]
    public void Steam_rises_and_sparkle_is_additive_while_smoke_is_not()
    {
        var ps = new ParticleSystem(seed: 1);

        ps.Spawn("steam", new Vector2(100, 100));
        Assert.InRange(ps.Live.Count, 4, 30);
        Assert.All(ps.Live, p => Assert.True(p.Velocity.Y < 0, "steam rises"));

        var before = ps.Live.Count;
        ps.Spawn("sparkle", new Vector2(50, 50));
        Assert.All(ps.Live.Skip(before), p => Assert.True(p.Additive));

        before = ps.Live.Count;
        ps.Spawn("smoke", new Vector2(50, 50));
        Assert.All(ps.Live.Skip(before), p => Assert.False(p.Additive));
    }

    [Fact]
    public void Particles_age_fade_and_die()
    {
        var ps = new ParticleSystem(seed: 1);
        ps.Spawn("sparks", new Vector2(10, 10));
        var longest = ps.Live.Max(p => p.Life);

        ps.Update(longest / 2);
        Assert.NotEmpty(ps.Live);
        Assert.All(ps.Live, p => Assert.InRange(p.Alpha, 0f, 1f));

        ps.Update(longest);
        Assert.Empty(ps.Live);
    }

    [Fact]
    public void Each_moment_spawns_exactly_once()
    {
        var ps = new ParticleSystem(seed: 1);
        var moments = new[] { ParticlesAt("sparkle"), ParticlesAt("smoke", 8, 8) };

        Assert.Equal(2, ps.Consume(moments));
        var count = ps.Live.Count;
        Assert.Equal(0, ps.Consume(moments));
        Assert.Equal(count, ps.Live.Count);
    }

    [Fact]
    public void Sound_and_shake_moments_are_not_particles()
    {
        var ps = new ParticleSystem(seed: 1);
        Assert.Equal(0, ps.Consume(new[] { new Moment(MomentKind.Sound, "ding", new TilePos(1, 1), 0.5f), new Moment(MomentKind.Shake, "fail", new TilePos(1, 1), 0.4f) }));
        Assert.Empty(ps.Live);
    }

    [Fact]
    public void The_pool_is_capped()
    {
        var ps = new ParticleSystem(seed: 1);
        for (var i = 0; i < 500; i++) ps.Spawn("sparks", new Vector2(i, i));
        Assert.True(ps.Live.Count <= ParticleSystem.MaxParticles);
        Assert.Equal(ParticleSystem.MaxParticles, ps.Live.Count);
    }

    [Fact]
    public void Dust_drifts_in_over_time_only_when_enabled()
    {
        var ps = new ParticleSystem(seed: 1) { DustArea = new DustArea(16, 16, 448, 240) };
        for (var i = 0; i < 120; i++) ps.Update(1f / 60f);
        Assert.Empty(ps.Live);

        ps.DustEnabled = true;
        for (var i = 0; i < 120; i++) ps.Update(1f / 60f);
        Assert.NotEmpty(ps.Live);
        Assert.All(ps.Live, p => { Assert.InRange(p.Position.X, 16, 464); Assert.InRange(p.Position.Y, 16, 256); });
    }

    [Fact]
    public void Shake_is_bounded_and_decays_to_exactly_zero()
    {
        var shake = new ScreenShake(seed: 1);
        shake.Trigger(strength: 4f, duration: 0.4f);

        shake.Update(0.1f);
        Assert.NotEqual(Vector2.Zero, shake.Offset);
        Assert.True(shake.Offset.Length() <= 4f * 1.5f);

        shake.Update(0.5f);
        Assert.Equal(Vector2.Zero, shake.Offset);
    }

    [Fact]
    public void A_shake_moment_triggers_once()
    {
        var shake = new ScreenShake(seed: 1);
        var moments = new[] { new Moment(MomentKind.Shake, "fail", new TilePos(3, 3), 0.4f) };
        Assert.Equal(1, shake.Consume(moments));
        Assert.Equal(0, shake.Consume(moments));
    }
}
