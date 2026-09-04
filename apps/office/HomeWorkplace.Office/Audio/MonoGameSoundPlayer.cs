using Microsoft.Xna.Framework.Audio;

namespace HomeWorkplace.Office.Audio;

/// <summary>
/// The real player: every named sound is synthesized once at startup and loaded as a
/// MonoGame <see cref="SoundEffect"/>. A machine without an audio device just plays nothing.
/// </summary>
public sealed class MonoGameSoundPlayer : ISoundPlayer, IDisposable
{
    private readonly Dictionary<string, SoundEffect> _sounds = new();

    public MonoGameSoundPlayer()
    {
        foreach (var name in SfxSynth.Names)
        {
            try
            {
                using var stream = new MemoryStream(SfxSynth.Render(SfxSynth.Sound(name)));
                _sounds[name] = SoundEffect.FromStream(stream);
            }
            catch (Exception ex) when (ex is NoAudioHardwareException or InvalidOperationException or DllNotFoundException)
            {
                // no audio device: the office is silent, not broken
            }
        }
    }

    public int Loaded => _sounds.Count;

    public void Play(string name, float volume, float pan)
    {
        if (_sounds.TryGetValue(name, out var sound))
            sound.Play(volume, 0f, pan);
    }

    public void Dispose()
    {
        foreach (var s in _sounds.Values) s.Dispose();
        _sounds.Clear();
    }
}
