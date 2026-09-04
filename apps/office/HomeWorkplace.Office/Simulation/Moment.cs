namespace HomeWorkplace.Office.Sim;

public enum MomentKind { Bubble, Particles, Shake, Sound }

/// <summary>
/// A short-lived effect the simulation asks for: a particle burst, a sound, a screen shake.
/// The renderer and jukebox observe moments; the simulation expires them by age. Reference
/// identity on purpose — the same effect requested twice is two moments.
/// </summary>
public sealed class Moment
{
    public Moment(MomentKind kind, string detail, TilePos at, float ttl)
    {
        Kind = kind;
        Detail = detail;
        At = at;
        Ttl = ttl;
    }

    public MomentKind Kind { get; }
    /// <summary>sparkle | smoke | steam | sparks | footstep | chime | ding | buzz | door | pour | page</summary>
    public string Detail { get; }
    public TilePos At { get; }
    /// <summary>Total lifetime in seconds.</summary>
    public float Ttl { get; }
    public float Age { get; internal set; }
    public bool Expired => Age >= Ttl;
}
