using HomeWorkplace.Office.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// Text and fills drawn straight onto the back buffer at the presentation scale: the boot
/// screen, the debug overlay, the fade, and every UI layer. UI text comes from a system font
/// rasterised per window scale (see <see cref="TextAtlas"/>); the world keeps its pixel font.
/// </summary>
public sealed class Hud : IDisposable
{
    /// <summary>Fonts UI text is drawn with, most wanted first: the terminal's default, then the console classic. The world keeps its pixel font.</summary>
    public static string[] FontFamilies { get; set; } = { "Cascadia Mono SemiBold", "Cascadia Mono", "Cascadia Code", "Consolas", "Lucida Console" };

    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _batch;
    private readonly Texture2D _pixel;
    /// <summary>One atlas per (scale, small): normal text sits in a 6-wide cell, small text in a 4-wide one, both a line tall.</summary>
    private readonly Dictionary<(int Scale, bool Small), (Texture2D Texture, TextAtlas Atlas)?> _fonts = new();
    private Texture2D? _atlas;

    /// <summary>Force the 5×7 pixel font for UI text (also what happens when no system font can be rasterised).</summary>
    public bool PixelText { get; set; }
    private SpriteRect _panel, _panelDark;
    private int _scale = 1;

    /// <summary>Give the HUD the atlas so it can draw nine-slice panels and sprites.</summary>
    public void SetAtlas(Texture2D atlas, SpriteRect panel, SpriteRect panelDark)
    {
        _atlas = atlas;
        _panel = panel;
        _panelDark = panelDark;
    }

    /// <summary>A nine-slice panel (3 px border) at native coordinates.</summary>
    public void Panel(int x, int y, int w, int h, bool dark = false)
    {
        if (_atlas is null) { Fill(x, y, w, h, dark ? new Color(0x0d, 0x0f, 0x22) : new Color(0x2b, 0x30, 0x55)); return; }
        var src = dark ? _panelDark : _panel;
        const int b = 3;
        int[] sx = { src.X, src.X + b, src.X + src.W - b };
        int[] sw = { b, src.W - 2 * b, b };
        int[] dx = { x, x + b, x + w - b };
        int[] dw = { b, w - 2 * b, b };
        int[] sy = { src.Y, src.Y + b, src.Y + src.H - b };
        int[] sh = { b, src.H - 2 * b, b };
        int[] dy = { y, y + b, y + h - b };
        int[] dh = { b, h - 2 * b, b };
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            _batch.Draw(_atlas, new Rectangle(dx[i] * _scale, dy[j] * _scale, dw[i] * _scale, dh[j] * _scale),
                new Rectangle(sx[i], sy[j], sw[i], sh[j]), Color.White);
    }

    /// <summary>An atlas sprite at native coordinates, magnified <paramref name="zoom"/> times.</summary>
    public void Sprite(SpriteRect src, int x, int y, int zoom = 1, bool flip = false)
    {
        if (_atlas is null) return;
        _batch.Draw(_atlas, new Rectangle(x * _scale, y * _scale, src.W * zoom * _scale, src.H * zoom * _scale),
            new Rectangle(src.X, src.Y, src.W, src.H), Color.White, 0f, Vector2.Zero,
            flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
    }

    public int Scale => _scale;

    public Hud(GraphicsDevice device)
    {
        _device = device;
        _batch = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public int LineHeight => UiLayout.Line;

    public void Begin(int scale)
    {
        _scale = Math.Max(1, scale);
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    public void End() => _batch.End();

    /// <summary>Fill a rectangle given in native pixels.</summary>
    public void Fill(int x, int y, int w, int h, Color colour)
        => _batch.Draw(_pixel, new Rectangle(x * _scale, y * _scale, w * _scale, h * _scale), colour);

    /// <summary>Fill the whole window (native coordinates ignored).</summary>
    public void FillWindow(int windowWidth, int windowHeight, Color colour)
        => _batch.Draw(_pixel, new Rectangle(0, 0, windowWidth, windowHeight), colour);

    /// <summary>
    /// Draw text on the layout grid with an optional backdrop. Inline <see cref="Markup"/> tags
    /// colour a run or make it small (4 native px per character instead of 6), so important
    /// details can be picked out without shouting; <paramref name="maxChars"/> clips by visible
    /// characters. <paramref name="literal"/> draws the text exactly as given (what the user
    /// typed); <paramref name="monochrome"/> keeps the sizes but paints every run in
    /// <paramref name="ink"/> (a selected, inverted row).
    /// </summary>
    public void Text(string text, int x, int y, Color ink, Color? backdrop = null, int maxChars = int.MaxValue, bool literal = false, bool monochrome = false)
    {
        IReadOnlyList<Run> runs;
        if (literal)
        {
            if (text.Length > maxChars) text = text[..Math.Max(0, maxChars)];
            runs = new[] { new Run(text, null, false) };
        }
        else
        {
            if (maxChars != int.MaxValue) text = Markup.Clip(text, Math.Max(0, maxChars));
            runs = Markup.Parse(text);
        }

        var pixel = PixelText || FontFor(_scale, small: false) is null;
        if (backdrop is { } bg)
        {
            var width = pixel
                ? runs.Sum(r => r.Text.Length) * PixelFont.Advance
                : runs.Sum(r => r.Text.Length * (r.Small ? Markup.SmallAdvance : Markup.Advance));
            Fill(x - 1, y - 1, width + 1, pixel ? PixelFont.GlyphHeight + 2 : UiLayout.Line - 1, bg);
        }

        var cx = x;
        foreach (var run in runs)
        {
            var colour = monochrome ? ink : RunColor(run.Color) ?? ink;
            if (!pixel && FontFor(_scale, run.Small) is { } f)
            {
                var advance = run.Small ? Markup.SmallAdvance : Markup.Advance;
                foreach (var ch in run.Text)
                {
                    var src = f.Atlas.Glyph(ch);
                    _batch.Draw(f.Texture, new Rectangle(cx * _scale, (y - 1) * _scale, f.Atlas.CellWidth, f.Atlas.CellHeight), ToXna(src), colour);
                    cx += advance;
                }
                continue;
            }
            cx = PixelRun(run.Text, cx, y, colour);   // the pixel font has one size: small runs draw at the normal advance
        }
    }

    /// <summary>The colour a markup name stands for, or null for plain ink.</summary>
    public static Color? RunColor(string? name) => name switch
    {
        "gold" => new Color(0xf0, 0xd7, 0x8c),
        "green" => new Color(0x7b, 0xd8, 0x8f),
        "red" => new Color(0xf0, 0x8c, 0x7b),
        "blue" => new Color(0x8f, 0xb8, 0xf0),
        "dim" => new Color(0xb9, 0xb7, 0xc9),
        "white" => new Color(0xf4, 0xf1, 0xe8),
        _ => null,
    };

    private int PixelRun(string text, int cx, int y, Color ink)
    {
        foreach (var ch in text.ToUpperInvariant())
        {
            var g = PixelFont.Glyph(ch);
            for (var row = 0; row < PixelFont.GlyphHeight; row++)
            for (var col = 0; col < PixelFont.GlyphWidth; col++)
                if (g[row][col] == '#')
                    _batch.Draw(_pixel, new Rectangle((cx + col) * _scale, (y + row) * _scale, _scale, _scale), ink);
            cx += PixelFont.Advance;
        }
        return cx;
    }

    /// <summary>
    /// The system font rasterised for this scale: a cell 6 (or 4, for small text) native px wide
    /// and a line tall per character, drawn one native pixel above the baseline row.
    /// </summary>
    private (Texture2D Texture, TextAtlas Atlas)? FontFor(int scale, bool small)
    {
        if (_fonts.TryGetValue((scale, small), out var cached)) return cached;
        var family = TextAtlas.PickFamily(FontFamilies);
        var cell = (small ? Markup.SmallAdvance : Markup.Advance) * scale;
        var atlas = family is null ? null : TextAtlas.TryBuild(family, cell, UiLayout.Line * scale);
        (Texture2D, TextAtlas)? entry = null;
        if (atlas is not null)
        {
            var texture = new Texture2D(_device, atlas.Width, atlas.Height);
            texture.SetData(atlas.Pixels.Select(p => new Color(p.R, p.G, p.B, p.A)).ToArray());
            entry = (texture, atlas);
        }
        _fonts[(scale, small)] = entry;
        return entry;
    }

    private static Rectangle ToXna(SpriteRect r) => new(r.X, r.Y, r.W, r.H);

    public void Dispose()
    {
        foreach (var f in _fonts.Values) f?.Texture.Dispose();
        _fonts.Clear();
        _pixel.Dispose();
        _batch.Dispose();
    }
}
