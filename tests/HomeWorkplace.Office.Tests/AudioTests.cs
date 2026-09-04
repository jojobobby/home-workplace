using System.Text;
using HomeWorkplace.Client;
using HomeWorkplace.Office.Audio;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Tests;

public class SfxSynthTests
{
    [Theory]
    [InlineData("footstep")]
    [InlineData("keys")]
    [InlineData("pour")]
    [InlineData("chime")]
    [InlineData("ding")]
    [InlineData("buzz")]
    [InlineData("door")]
    [InlineData("page")]
    public void Every_named_sound_is_a_valid_mono_16bit_wav_of_its_duration(string name)
    {
        var spec = SfxSynth.Sound(name);
        var wav = SfxSynth.Render(spec);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));                   // PCM
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));                   // mono
        Assert.Equal(SfxSynth.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));

        var dataBytes = BitConverter.ToInt32(wav, 40);
        var expectedSamples = (int)(spec.Duration * SfxSynth.SampleRate);
        Assert.Equal(expectedSamples * 2, dataBytes);
        Assert.Equal(44 + dataBytes, wav.Length);
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Contains(wav.Skip(44), b => b != 0);                       // not silence
    }

    [Fact]
    public void Rendering_is_deterministic_even_for_noise()
    {
        var a = SfxSynth.Render(SfxSynth.Sound("footstep"));
        var b = SfxSynth.Render(SfxSynth.Sound("footstep"));
        Assert.Equal(a, b);
    }

    [Fact]
    public void The_envelope_starts_and_ends_quiet()
    {
        var wav = SfxSynth.Render(SfxSynth.Sound("chime"));
        short First() => BitConverter.ToInt16(wav, 44);
        short Last() => BitConverter.ToInt16(wav, wav.Length - 2);
        Assert.InRange(Math.Abs((int)First()), 0, 2000);
        Assert.InRange(Math.Abs((int)Last()), 0, 2000);
    }

    [Fact]
    public void Unknown_sound_names_throw()
        => Assert.Throws<ArgumentException>(() => SfxSynth.Sound("kazoo"));
}

public class JukeboxTests
{
    private sealed class FakePlayer : ISoundPlayer
    {
        public readonly List<(string Name, float Volume, float Pan)> Played = new();
        public void Play(string name, float volume, float pan) => Played.Add((name, volume, pan));
    }

    private static Moment Sound(string detail, int x = 15, float ttl = 0.5f) => new(MomentKind.Sound, detail, new TilePos(x, 8), ttl);

    [Fact]
    public void Each_sound_moment_plays_once_at_master_volume()
    {
        var player = new FakePlayer();
        var jukebox = new Jukebox(player) { Volume = 0.5f };
        var chime = Sound("chime");

        jukebox.Consume(new[] { chime });
        jukebox.Consume(new[] { chime });   // still alive next frame: not replayed

        var play = Assert.Single(player.Played);
        Assert.Equal("chime", play.Name);
        Assert.Equal(0.5f, play.Volume, 3);
    }

    [Fact]
    public void Non_sound_moments_are_ignored()
    {
        var player = new FakePlayer();
        new Jukebox(player).Consume(new[] { new Moment(MomentKind.Particles, "steam", new TilePos(1, 1), 1f) });
        Assert.Empty(player.Played);
    }

    [Fact]
    public void Cooldowns_stop_the_same_sound_stacking_within_a_frame_or_two()
    {
        var player = new FakePlayer();
        var jukebox = new Jukebox(player);

        jukebox.Consume(new[] { Sound("footstep"), Sound("footstep"), Sound("footstep") });
        Assert.Single(player.Played);

        jukebox.Update(Jukebox.CooldownFor("footstep") + 0.01f);
        jukebox.Consume(new[] { Sound("footstep") });
        Assert.Equal(2, player.Played.Count);
    }

    [Fact]
    public void Mute_drops_sounds_and_unmute_restores_them()
    {
        var player = new FakePlayer();
        var jukebox = new Jukebox(player) { Muted = true };

        jukebox.Consume(new[] { Sound("ding") });
        Assert.Empty(player.Played);

        jukebox.Muted = false;
        jukebox.Consume(new[] { Sound("buzz") });
        Assert.Single(player.Played);
    }

    [Fact]
    public void Pan_follows_the_tile_across_the_office()
    {
        var player = new FakePlayer();
        var jukebox = new Jukebox(player);

        jukebox.Consume(new[] { Sound("door", x: 1) });
        jukebox.Update(1f);
        jukebox.Consume(new[] { Sound("door", x: WorldLayout.Width - 2) });

        Assert.True(player.Played[0].Pan < -0.5f, "left side pans left");
        Assert.True(player.Played[1].Pan > 0.5f, "right side pans right");
    }

    [Fact]
    public void Volume_is_clamped_to_the_unit_range()
    {
        var jukebox = new Jukebox(new FakePlayer()) { Volume = 3f };
        Assert.Equal(1f, jukebox.Volume);
        jukebox.Volume = -1f;
        Assert.Equal(0f, jukebox.Volume);
    }
}

public class TypingSoundTests
{
    [Fact]
    public void A_typing_agent_emits_key_clicks()
    {
        var sim = new Simulation(WorldLayout.Generate(new[] { "ada-coder" }), seed: 3);
        sim.Apply(new EmployeeAppeared("ada-coder", "Ada", EmployeeStatus.Working, "Build"));
        for (var i = 0; i < 60 * 40; i++) sim.Update(1f / 60f);
        Assert.Equal(Activity.Typing, sim.Agents["ada-coder"].Activity);

        var keys = 0;
        for (var i = 0; i < 60 * 5; i++)
        {
            sim.Update(1f / 60f);
            keys += sim.Moments.Count(m => m.Kind == MomentKind.Sound && m.Detail == "keys" && m.Age == 0f);
        }
        Assert.InRange(keys, 10, 120);
    }
}

public class OfficeConfigTests
{
    [Fact]
    public void Office_settings_default_and_load_from_app_json()
    {
        Assert.Equal(0.6f, new AppConfig().Office.Volume, 3);
        Assert.Equal(0, new AppConfig().Office.Scale);
        Assert.False(new AppConfig().Office.ShowDebug);

        var path = Path.Combine(Path.GetTempPath(), "hw-office-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{ "office": { "volume": 0.25, "scale": 2, "showDebug": true } }""");
        try
        {
            var cfg = AppConfig.Load(path);
            Assert.Equal(0.25f, cfg.Office.Volume, 3);
            Assert.Equal(2, cfg.Office.Scale);
            Assert.True(cfg.Office.ShowDebug);
        }
        finally { File.Delete(path); }
    }
}
