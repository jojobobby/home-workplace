using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Audio;

/// <summary>Plays a named sound. Volume 0..1, pan -1 (left) .. 1 (right).</summary>
public interface ISoundPlayer
{
    void Play(string name, float volume, float pan);
}

/// <summary>
/// Turns the simulation's sound moments into plays: each moment once, per-sound cooldowns so
/// a crowd of footsteps is one footstep, a master volume, and a mute. Pan follows where in
/// the office the sound happened.
/// </summary>
public sealed class Jukebox
{
    private static readonly Dictionary<string, float> Cooldowns = new()
    {
        ["footstep"] = 0.12f,
        ["keys"] = 0.05f,
        ["pour"] = 1.0f,
        ["chime"] = 0.4f,
        ["ding"] = 0.4f,
        ["buzz"] = 0.4f,
        ["door"] = 0.3f,
        ["page"] = 0.3f,
    };

    private readonly ISoundPlayer _player;
    private readonly HashSet<Moment> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, float> _remaining = new();
    private float _volume = 0.6f;

    public Jukebox(ISoundPlayer player) => _player = player;

    public float Volume { get => _volume; set => _volume = Math.Clamp(value, 0f, 1f); }
    public bool Muted { get; set; }

    public static float CooldownFor(string name) => Cooldowns.TryGetValue(name, out var c) ? c : 0.2f;

    /// <summary>Play every sound moment not yet seen. Returns how many were played.</summary>
    public int Consume(IReadOnlyList<Moment> moments)
    {
        _seen.RemoveWhere(m => m.Expired);
        var played = 0;
        foreach (var m in moments)
        {
            if (m.Kind != MomentKind.Sound || !_seen.Add(m)) continue;
            if (Muted || _volume <= 0f) continue;
            if (_remaining.TryGetValue(m.Detail, out var left) && left > 0f) continue;

            _remaining[m.Detail] = CooldownFor(m.Detail);
            _player.Play(m.Detail, _volume, PanFor(m.At));
            played++;
        }
        return played;
    }

    public void Update(float dt)
    {
        foreach (var key in _remaining.Keys.ToList())
            _remaining[key] = Math.Max(0f, _remaining[key] - dt);
    }

    private static float PanFor(TilePos at)
        => Math.Clamp((at.X / (float)(WorldLayout.Width - 1)) * 2f - 1f, -1f, 1f);
}
