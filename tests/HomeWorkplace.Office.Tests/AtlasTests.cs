using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Tests;

public class AtlasTests
{
    private static readonly string[] Ids = { "ada", "rex" };

    [Fact]
    public void Generation_is_deterministic_for_the_same_ids()
    {
        var a = SpriteGenerator.Generate(Ids);
        var b = SpriteGenerator.Generate(new[] { "rex", "ada" });

        Assert.Equal(a.Atlas.Width, b.Atlas.Width);
        Assert.True(a.Atlas.Pixels.SequenceEqual(b.Atlas.Pixels));
        Assert.Equal(a.Manifest.Names.OrderBy(n => n), b.Manifest.Names.OrderBy(n => n));
    }

    [Theory]
    [InlineData("floor", 1)] [InlineData("floor2", 1)] [InlineData("wall", 1)]
    [InlineData("desk", 1)] [InlineData("desk_lamp", 1)] [InlineData("desk_monitor", 1)] [InlineData("desk_lamp_monitor", 1)]
    [InlineData("coffee", 1)] [InlineData("whiteboard", 1)] [InlineData("plant", 1)]
    [InlineData("bubble_question", 1)] [InlineData("bubble_exclaim", 1)] [InlineData("bubble_dots", 1)]
    [InlineData("light", 1)]
    public void The_manifest_has_every_prop_and_effect_sprite(string name, int frames)
    {
        var set = SpriteGenerator.Generate(Ids);
        var anim = set.Manifest.Get(name);
        Assert.Equal(frames, anim.Frames.Count);
        Assert.All(anim.Frames, f => Assert.True(f.X >= 0 && f.Y >= 0 && f.X + f.W <= set.Atlas.Width && f.Y + f.H <= set.Atlas.Height, $"{name} frame out of the atlas"));
    }

    [Theory]
    [InlineData(Anim.Idle, 4)] [InlineData(Anim.Walk, 4)] [InlineData(Anim.Type, 2)] [InlineData(Anim.Talk, 2)]
    public void Every_employee_gets_each_animation_with_the_spec_frame_counts(Anim anim, int frames)
    {
        var set = SpriteGenerator.Generate(Ids);
        foreach (var id in Ids)
        {
            var a = set.Manifest.Agent(id, anim);
            Assert.Equal(frames, a.Frames.Count);
            Assert.True(a.Fps > 0);
            Assert.All(a.Frames, f => { Assert.Equal(16, f.W); Assert.Equal(16, f.H); });
        }
    }

    [Fact]
    public void Sprites_do_not_overlap_in_the_atlas()
    {
        var set = SpriteGenerator.Generate(Ids);
        var rects = set.Manifest.Names.SelectMany(n => set.Manifest.Get(n).Frames).ToList();
        for (var i = 0; i < rects.Count; i++)
        for (var j = i + 1; j < rects.Count; j++)
        {
            var a = rects[i]; var b = rects[j];
            var overlap = a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
            Assert.False(overlap, $"frames overlap: {a} and {b}");
        }
    }

    [Fact]
    public void Characters_are_drawn_and_differ_per_employee()
    {
        var set = SpriteGenerator.Generate(Ids);
        var ada = set.Atlas.Crop(set.Manifest.Agent("ada", Anim.Idle).Frames[0]);
        var rex = set.Atlas.Crop(set.Manifest.Agent("rex", Anim.Idle).Frames[0]);

        Assert.True(ada.Count(p => p.A > 0) > 30, "a character should have a body");
        Assert.False(ada.SequenceEqual(rex), "two employees should not look identical");
    }

    [Fact]
    public void Walk_frames_differ_from_each_other()
    {
        var set = SpriteGenerator.Generate(Ids);
        var frames = set.Manifest.Agent("ada", Anim.Walk).Frames.Select(f => set.Atlas.Crop(f)).ToList();
        Assert.False(frames[0].SequenceEqual(frames[1]));
    }

    [Fact]
    public void The_pixel_font_covers_letters_digits_and_punctuation()
    {
        foreach (var ch in "ABCXYZ019 ?!.-:")
            Assert.NotNull(PixelFont.Glyph(ch));
        Assert.Equal(PixelFont.Glyph('?'), PixelFont.Glyph('é'));   // unknown → fallback glyph
        Assert.Equal(6 * 5, PixelFont.Measure("HELLO"));                 // 5 px glyph + 1 px gap
    }
}
