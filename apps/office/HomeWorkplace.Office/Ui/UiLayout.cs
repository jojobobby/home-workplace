using System.Numerics;
using HomeWorkplace.Office.Render;

namespace HomeWorkplace.Office.Ui;

public readonly record struct UiRect(int X, int Y, int W, int H)
{
    public bool Contains(Vector2 p) => p.X >= X && p.Y >= Y && p.X < X + W && p.Y < Y + H;
}

/// <summary>
/// Where things are on the 480×272 screen. The renderer draws with these and the click
/// handling hit-tests with them, so a click always lands on what was drawn.
/// </summary>
public static class UiLayout
{
    public const int W = 480;
    public const int H = 272;
    public const int Line = PixelFont.GlyphHeight + 3;       // 10 px per text line

    // ---- dialogue ----
    public const int DialogueHeight = 112;
    public const int DialogueOptionsPerPage = 8;
    public static UiRect DialogueBox => new(8, H - 8 - DialogueHeight, W - 16, DialogueHeight);

    /// <summary>The rect of option <paramref name="index"/> on its own page (two columns of four).</summary>
    public static UiRect DialogueOptionRect(int index)
    {
        var slot = index % DialogueOptionsPerPage;
        return new UiRect(64 + (slot / 4) * 200, DialogueBox.Y + 62 + (slot % 4) * Line, 190, Line);
    }

    /// <summary>Which option of <paramref name="d"/> is under <paramref name="p"/>, or -1.</summary>
    public static int DialogueOptionAt(Dialogue d, Vector2 p)
    {
        var page = d.Selected / DialogueOptionsPerPage;
        for (var i = page * DialogueOptionsPerPage; i < Math.Min(d.Options.Count, (page + 1) * DialogueOptionsPerPage); i++)
            if (DialogueOptionRect(i).Contains(p)) return i;
        return -1;
    }

    // ---- overlay ----
    public const int OverlayVisibleRows = 20;
    public static UiRect OverlayBox => new(8, 8, W - 16, H - 16);

    public static UiRect OverlayTabRect(OverlayTab tab)
    {
        var x = 20;
        foreach (var t in Enum.GetValues<OverlayTab>())
        {
            var width = PixelFont.Measure(t.ToString());
            if (t == tab) return new UiRect(x - 4, 16, width + 7, Line + 2);
            x += width + 16;
        }
        return default;
    }

    public static OverlayTab? OverlayTabAt(Vector2 p)
    {
        foreach (var t in Enum.GetValues<OverlayTab>())
            if (OverlayTabRect(t).Contains(p)) return t;
        return null;
    }

    /// <summary>Index of the first visible row so the selection stays on screen.</summary>
    public static int OverlayFirstRow(Overlay o)
        => Math.Max(0, Math.Min(o.Selected - OverlayVisibleRows + 1, o.Rows.Count - OverlayVisibleRows));

    public static UiRect OverlayRowRect(int visibleIndex) => new(16, 38 + visibleIndex * Line - 2, W - 32, Line);

    /// <summary>Which row of <paramref name="o"/> is under <paramref name="p"/>, or -1.</summary>
    public static int OverlayRowAt(Overlay o, Vector2 p)
    {
        var first = OverlayFirstRow(o);
        for (var i = first; i < Math.Min(o.Rows.Count, first + OverlayVisibleRows); i++)
            if (OverlayRowRect(i - first).Contains(p)) return i;
        return -1;
    }

    // ---- toasts ----
    public const int ToastMaxChars = 44;
    public static string ToastText(string text) => text.Length > ToastMaxChars ? text[..ToastMaxChars] : text;

    public static UiRect ToastRect(int index, string text)
    {
        var w = PixelFont.Measure(ToastText(text)) + 12;
        return new UiRect(W - 8 - w, 8 + index * 17, w, 15);
    }

    // ---- confirm ----
    public static UiRect ConfirmYesRect(UiRect box, int y) => new(box.X + 12, y, 40, Line);
    public static UiRect ConfirmNoRect(UiRect box, int y) => new(box.X + 72, y, 40, Line);
}
