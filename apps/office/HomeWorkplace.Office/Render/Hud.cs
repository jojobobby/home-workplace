using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// Text and fills drawn straight onto the back buffer at the presentation scale: the boot
/// screen, the debug overlay, the fade. Uses the 5×7 pixel font so it matches the office.
/// </summary>
public sealed class Hud : IDisposable
{
    /// <summary>The system font UI text is drawn with; the world keeps its pixel font.</summary>
    public static string FontFamily { get; set; } = "Consolas";

    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _batch;
    private readonly Texture2D _pixel;
    private readonly Dictionary<int, (Texture2D Texture, TextAtlas Atlas)?> _fonts = new();
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

    public int LineHeight => PixelFont.GlyphHeight + 3;

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

    /// <summary>Draw upper-cased text at native pixel coordinates, with an optional backdrop; <paramref name="maxChars"/> clips.</summary>
    /// <summary>Draw text on the layout grid (6 native px per character, 8 tall) with an optional backdrop; <paramref name="maxChars"/> clips.</summary>
    public void Text(string text, int x, int y, Color ink, Color? backdrop = null, int maxChars = int.MaxValue)
    {
        if (text.Length > maxChars) text = text[..Math.Max(0, maxChars)];
        if (backdrop is { } bg)
            Fill(x - 1, y - 1, PixelFont.Measure(text) + 1, PixelFont.GlyphHeight + 2, bg);

        var font = PixelText ? null : FontFor(_scale);
        var cx = x;
        if (font is { } f)
        {
            foreach (var ch in text)
            {
                var src = f.Atlas.Glyph(ch);
                _batch.Draw(f.Texture, new Rectangle(cx * _scale, y * _scale, f.Atlas.CellWidth, f.Atlas.CellHeight), ToXna(src), ink);
                cx += PixelFont.Advance;
            }
            return;
        }

        foreach (var ch in text.ToUpperInvariant())
        {
            var g = PixelFont.Glyph(ch);
            for (var row = 0; row < PixelFont.GlyphHeight; row++)
            for (var col = 0; col < PixelFont.GlyphWidth; col++)
                if (g[row][col] == '#')
                    _batch.Draw(_pixel, new Rectangle((cx + col) * _scale, (y + row) * _scale, _scale, _scale), ink);
            cx += PixelFont.Advance;
        }
    }

    /// <summary>The system font rasterised for this scale: 6×8 native cells become (6·scale)×(8·scale) glyph boxes.</summary>
    private (Texture2D Texture, TextAtlas Atlas)? FontFor(int scale)
    {
        if (_fonts.TryGetValue(scale, out var cached)) return cached;
        var atlas = TextAtlas.TryBuild(FontFamily, PixelFont.Advance * scale, (PixelFont.GlyphHeight + 1) * scale);
        (Texture2D, TextAtlas)? entry = null;
        if (atlas is not null)
        {
            var texture = new Texture2D(_device, atlas.Width, atlas.Height);
            texture.SetData(atlas.Pixels.Select(p => new Color(p.R, p.G, p.B, p.A)).ToArray());
            entry = (texture, atlas);
        }
        _fonts[scale] = entry;
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
