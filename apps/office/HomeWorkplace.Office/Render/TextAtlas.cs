using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// A readable UI font: a system font rasterised once per window scale into a glyph atlas
/// whose cells match the layout's text grid (6×8 native pixels per character). The world
/// keeps the 5×7 pixel font; dialogues, menus and toasts use this. Windows only (GDI+); on
/// anything else <see cref="TryBuild"/> returns null and the HUD falls back to the pixel font.
/// </summary>
public sealed class TextAtlas
{
    public const char First = ' ';
    public const char Last = '~';
    public const int Columns = 16;
    public static int Count => Last - First + 1;
    public static int Rows => (Count + Columns - 1) / Columns;

    private TextAtlas(int cellWidth, int cellHeight, int width, int height, Rgba[] pixels, float fontSize)
    {
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Width = width;
        Height = height;
        Pixels = pixels;
        FontSize = fontSize;
    }

    public int CellWidth { get; }
    public int CellHeight { get; }
    public int Width { get; }
    public int Height { get; }
    /// <summary>Premultiplied white-on-transparent pixels: (a, a, a, a) per pixel, tinted at draw time.</summary>
    public Rgba[] Pixels { get; }
    public float FontSize { get; }

    public SpriteRect Glyph(char c)
    {
        if (c < First || c > Last) c = '?';
        var i = c - First;
        return new SpriteRect(i % Columns * CellWidth, i / Columns * CellHeight, CellWidth, CellHeight);
    }

    /// <summary>Rasterise <paramref name="family"/> so its widest glyph fits a cell; null when GDI+ is unavailable.</summary>
    public static TextAtlas? TryBuild(string family, int cellWidth, int cellHeight)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var width = Columns * cellWidth;
            var height = Rows * cellHeight;
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var size = FitSize(g, family, cellWidth, cellHeight);
            using var font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
            using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            for (var i = 0; i < Count; i++)
            {
                var c = (char)(First + i);
                var cell = new RectangleF(i % Columns * cellWidth, i / Columns * cellHeight, cellWidth, cellHeight);
                g.DrawString(c.ToString(), font, Brushes.White, cell, format);
            }

            var pixels = new Rgba[width * height];
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var raw = new byte[data.Stride * height];
                Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var a = raw[y * data.Stride + x * 4 + 3];
                    pixels[y * width + x] = new Rgba(a, a, a, a);   // white text, premultiplied
                }
            }
            finally { bitmap.UnlockBits(data); }

            return new TextAtlas(cellWidth, cellHeight, width, height, pixels, size);
        }
        catch (Exception)
        {
            return null;   // no GDI+, no font: the caller keeps the pixel font
        }
    }

    /// <summary>The largest pixel size whose widest glyph fits the cell width and whose line height fits the cell height.</summary>
    private static float FitSize(Graphics g, string family, int cellWidth, int cellHeight)
    {
        for (var size = cellHeight * 0.95f; size >= 4f; size -= 0.5f)
        {
            using var font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
            var widest = g.MeasureString("W", font, int.MaxValue, StringFormat.GenericTypographic).Width;
            if (widest <= cellWidth && font.GetHeight(g) <= cellHeight) return size;
        }
        return 4f;
    }
}
