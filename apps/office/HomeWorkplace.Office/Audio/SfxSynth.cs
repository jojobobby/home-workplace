namespace HomeWorkplace.Office.Audio;

public enum Wave { Square, Triangle, Saw, Noise }

/// <summary>
/// One synthesized sound: an oscillator with a linear pitch sweep and an attack/decay
/// envelope. <see cref="Duration"/> is seconds; frequencies are Hz; <see cref="Attack"/> and
/// <see cref="Decay"/> are seconds inside the duration.
/// </summary>
public sealed record SoundSpec(
    string Name, Wave Wave, float Duration, float StartHz, float EndHz,
    float Attack, float Decay, float Gain, int Seed = 0);

/// <summary>
/// Writes tiny 8-bit-flavoured sound effects as 16-bit mono 22.05 kHz WAV buffers. Everything
/// is deterministic for a spec, noise included, so a sound renders the same bytes every run.
/// </summary>
public static class SfxSynth
{
    public const int SampleRate = 22050;

    public static readonly IReadOnlyList<string> Names = new[] { "footstep", "keys", "pour", "chime", "ding", "buzz", "door", "page" };

    public static SoundSpec Sound(string name) => name switch
    {
        "footstep" => new(name, Wave.Noise, 0.06f, 220, 80, 0.005f, 0.05f, 0.25f, Seed: 11),
        "keys" => new(name, Wave.Square, 0.03f, 1800, 1200, 0.002f, 0.025f, 0.12f),
        "pour" => new(name, Wave.Noise, 0.8f, 900, 400, 0.15f, 0.5f, 0.2f, Seed: 23),
        "chime" => new(name, Wave.Triangle, 0.45f, 880, 1320, 0.01f, 0.4f, 0.5f),
        "ding" => new(name, Wave.Triangle, 0.6f, 1760, 1760, 0.005f, 0.55f, 0.5f),
        "buzz" => new(name, Wave.Saw, 0.5f, 140, 90, 0.01f, 0.4f, 0.4f),
        "door" => new(name, Wave.Square, 0.18f, 300, 180, 0.005f, 0.15f, 0.35f),
        "page" => new(name, Wave.Square, 0.12f, 1200, 1600, 0.005f, 0.1f, 0.3f),
        _ => throw new ArgumentException($"unknown sound '{name}'", nameof(name)),
    };

    public static byte[] Render(SoundSpec spec)
    {
        var samples = (int)(spec.Duration * SampleRate);
        var pcm = new short[samples];
        var rng = new Random(spec.Seed);
        var phase = 0.0;
        var noise = 0.0;

        for (var i = 0; i < samples; i++)
        {
            var t = i / (float)SampleRate;
            var u = samples <= 1 ? 0f : i / (float)(samples - 1);
            var hz = spec.StartHz + (spec.EndHz - spec.StartHz) * u;
            phase += hz / SampleRate;
            if (phase >= 1.0) { phase -= 1.0; noise = rng.NextDouble() * 2 - 1; }

            var value = spec.Wave switch
            {
                Wave.Square => phase < 0.5 ? 1.0 : -1.0,
                Wave.Triangle => 4.0 * Math.Abs(phase - 0.5) - 1.0,
                Wave.Saw => 2.0 * phase - 1.0,
                _ => noise,
            };

            var env = Envelope(t, spec);
            pcm[i] = (short)Math.Clamp(value * env * spec.Gain * short.MaxValue, short.MinValue, short.MaxValue);
        }

        return Wav(pcm);
    }

    private static float Envelope(float t, SoundSpec spec)
    {
        var attack = spec.Attack <= 0 ? 1f : Math.Clamp(t / spec.Attack, 0f, 1f);
        var release = spec.Duration - t;
        var decay = spec.Decay <= 0 ? 1f : Math.Clamp(release / spec.Decay, 0f, 1f);
        return Math.Min(attack, decay);
    }

    private static byte[] Wav(short[] pcm)
    {
        var dataBytes = pcm.Length * 2;
        using var ms = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);            // PCM
        w.Write((short)1);            // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);      // byte rate
        w.Write((short)2);            // block align
        w.Write((short)16);           // bits per sample
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in pcm) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
