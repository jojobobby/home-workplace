using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// Text and fills drawn straight onto the back buffer at the presentation scale: the boot
/// screen, the debug overlay, the fade. Uses the 5×7 pixel font so it matches the office.
/// </summary>
public sealed class Hud : IDisposable
{
    private readonly SpriteBatch _batch;
    private readonly Texture2D _pixel;
    private int _scale = 1;

    public Hud(GraphicsDevice device)
    {
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

    /// <summary>Draw upper-cased text at native pixel coordinates, with an optional backdrop.</summary>
    public void Text(string text, int x, int y, Color ink, Color? backdrop = null)
    {
        text = text.ToUpperInvariant();
        if (backdrop is { } bg)
            Fill(x - 1, y - 1, PixelFont.Measure(text) + 1, PixelFont.GlyphHeight + 2, bg);
        var cx = x;
        foreach (var ch in text)
        {
            var g = PixelFont.Glyph(ch);
            for (var row = 0; row < PixelFont.GlyphHeight; row++)
            for (var col = 0; col < PixelFont.GlyphWidth; col++)
                if (g[row][col] == '#')
                    _batch.Draw(_pixel, new Rectangle((cx + col) * _scale, (y + row) * _scale, _scale, _scale), ink);
            cx += PixelFont.Advance;
        }
    }

    public void Dispose()
    {
        _pixel.Dispose();
        _batch.Dispose();
    }
}
