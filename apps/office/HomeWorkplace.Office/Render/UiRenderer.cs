using HomeWorkplace.Office.Sim;
using HomeWorkplace.Office.Ui;
using Microsoft.Xna.Framework;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// Draws the UI model with the HUD in native (480×272) coordinates: the dialogue box at the
/// bottom, text entries and confirms centred, the overlay over everything, toasts top-right.
/// Layers draw bottom to top; only the top one is live, but the ones under it stay visible.
/// </summary>
public sealed class UiRenderer
{
    public const int W = SceneRenderer.NativeWidth;
    public const int H = SceneRenderer.NativeHeight;
    private const int Line = PixelFont.GlyphHeight + 3;      // 10 px per text line

    private static readonly Color Text = new(0xf4, 0xf1, 0xe8), Dim = new(0xb9, 0xb7, 0xc9), Gold = new(0xf0, 0xd7, 0x8c);
    private static readonly Color Red = new(0xf0, 0x8c, 0x7b), Green = new(0x7b, 0xd8, 0x8f), Ink = new(0x0d, 0x0f, 0x22);
    private static readonly Color Highlight = new(0x7b, 0x85, 0xc9), Field = new(0x1b, 0x1f, 0x3a);

    private readonly Hud _hud;
    private readonly Manifest _manifest;

    public UiRenderer(Hud hud, SceneRenderer scene)
    {
        _hud = hud;
        _manifest = scene.Manifest;
        hud.SetAtlas(scene.AtlasTexture, _manifest.Get("panel").Frames[0], _manifest.Get("panel_dark").Frames[0]);
    }

    /// <summary>Draw everything; <paramref name="time"/> drives the caret blink.</summary>
    public void Draw(UiState ui, Toasts toasts, int scale, float time)
    {
        _hud.Begin(scale);
        foreach (var layer in ui.Layers)
        {
            switch (layer)
            {
                case Dialogue d: DrawDialogue(d); break;
                case TextEntry t: DrawTextEntry(t, time); break;
                case Confirm c: DrawConfirm(c); break;
                case Overlay o: DrawOverlay(o); break;
            }
        }
        DrawToasts(toasts);
        _hud.End();
    }

    // ---- dialogue ------------------------------------------------------------------------

    public const int DialogueHeight = 112;

    private void DrawDialogue(Dialogue d)
    {
        var y0 = H - 8 - DialogueHeight;
        _hud.Panel(8, y0, W - 16, DialogueHeight);

        // portrait
        _hud.Panel(16, y0 + 8, 40, 40, dark: true);
        if (d.SpeakerId is { } id && _manifest.TryAgent(id, Anim.Talk, out var anim))
            _hud.Sprite(anim.Frames[0], 20, y0 + 12, zoom: 2);
        else
            _hud.Sprite(_manifest.Get("whiteboard").Frames[0], 18, y0 + 20, zoom: 1);

        _hud.Text(d.SpeakerName, 64, y0 + 8, Gold);

        // lines, typewriter-revealed
        var remaining = d.Revealed;
        var ly = y0 + 20;
        var shown = 0;
        foreach (var line in d.Lines.SelectMany(l => TextEntry.Wrap(l, 64)))
        {
            if (shown >= 4 || remaining <= 0) break;
            _hud.Text(line, 64, ly, Text, maxChars: remaining);
            remaining -= line.Length;
            ly += Line;
            shown++;
        }

        if (!d.IsRevealed) return;

        // options in two columns of four, paged so the selection is visible
        const int perPage = UiLayout.DialogueOptionsPerPage;
        var page = d.Selected / perPage;
        var first = page * perPage;
        for (var i = first; i < Math.Min(d.Options.Count, first + perPage); i++)
        {
            var rect = UiLayout.DialogueOptionRect(i);
            var selected = i == d.Selected;
            if (selected) _hud.Text(">", rect.X, rect.Y, Gold);
            _hud.Text(d.Options[i].Label, rect.X + 8, rect.Y, selected ? Gold : Text, maxChars: 30);
        }
        if (d.Options.Count > perPage)
            _hud.Text($"{page + 1}/{(d.Options.Count + perPage - 1) / perPage}", W - 40, y0 + DialogueHeight - 12, Dim);
    }

    // ---- text entry ----------------------------------------------------------------------

    private void DrawTextEntry(TextEntry t, float time)
    {
        const int width = 336;
        const int cols = (width - 32) / PixelFont.Advance;
        var rows = t.Fields.Sum(f => (f.Multiline ? 5 : 1) + 1);
        var height = 24 + rows * Line + 20;
        var x0 = (W - width) / 2;
        var y0 = (H - height) / 2;
        _hud.Panel(x0, y0, width, height);
        _hud.Text(t.Title, x0 + 12, y0 + 8, Gold);

        var y = y0 + 22;
        for (var i = 0; i < t.Fields.Count; i++)
        {
            var f = t.Fields[i];
            var current = i == t.Current;
            _hud.Text(f.Name, x0 + 12, y, current ? Gold : Dim);
            y += Line;
            var lines = f.Multiline ? 5 : 1;
            _hud.Fill(x0 + 12, y - 2, width - 24, lines * Line + 2, Field);
            var wrapped = TextEntry.Wrap(t.Values[i], cols);
            for (var l = 0; l < Math.Min(lines, wrapped.Count); l++)
                _hud.Text(wrapped[l], x0 + 16, y + l * Line, Text);

            if (current && (time % 1f) < 0.5f)
            {
                var before = TextEntry.Wrap(t.Values[i][..Math.Min(t.Cursor, t.Values[i].Length)], cols);
                var cl = Math.Min(lines - 1, before.Count - 1);
                var cc = before[^1].Length;
                if (t.Values[i].Length > 0 && t.Cursor == t.Values[i].Length && wrapped.Count > before.Count) cc = wrapped[^1].Length;
                _hud.Fill(x0 + 16 + cc * PixelFont.Advance, y + cl * Line, 1, PixelFont.GlyphHeight, Gold);
            }
            y += lines * Line + 2;
        }

        if (t.Error is { } err) _hud.Text(err, x0 + 12, y0 + height - 12, Red);
        else _hud.Text("Enter: next / submit   Esc: cancel", x0 + 12, y0 + height - 12, Dim);
    }

    // ---- confirm -------------------------------------------------------------------------

    private void DrawConfirm(Confirm c)
    {
        const int width = 264;
        var lines = TextEntry.Wrap(c.Question, (width - 24) / PixelFont.Advance);
        var height = 16 + lines.Count * Line + 20;
        var x0 = (W - width) / 2;
        var y0 = (H - height) / 2;
        _hud.Panel(x0, y0, width, height);
        var y = y0 + 8;
        foreach (var line in lines) { _hud.Text(line, x0 + 12, y, Text); y += Line; }
        y += 4;
        _hud.Text((c.Selected == 0 ? "> " : "  ") + "Yes", x0 + 12, y, c.Selected == 0 ? Gold : Text);
        _hud.Text((c.Selected == 1 ? "> " : "  ") + "No", x0 + 72, y, c.Selected == 1 ? Gold : Text);
    }

    // ---- overlay -------------------------------------------------------------------------

    public const int OverlayRowHeight = Line;
    public const int OverlayVisibleRows = 20;

    private void DrawOverlay(Overlay o)
    {
        _hud.Panel(8, 8, W - 16, H - 16);

        foreach (var tab in Enum.GetValues<OverlayTab>())
        {
            var rect = UiLayout.OverlayTabRect(tab);
            var selected = tab == o.Tab;
            if (selected) _hud.Fill(rect.X, rect.Y, rect.W, rect.H, Highlight);
            _hud.Text(tab.ToString(), rect.X + 4, rect.Y + 2, selected ? Ink : Dim);
        }
        _hud.Fill(16, 32, W - 32, 1, Highlight);

        var first = UiLayout.OverlayFirstRow(o);
        for (var i = first; i < Math.Min(o.Rows.Count, first + UiLayout.OverlayVisibleRows); i++)
        {
            var rect = UiLayout.OverlayRowRect(i - first);
            var selected = i == o.Selected;
            if (selected) _hud.Fill(rect.X, rect.Y, rect.W, rect.H, Highlight);
            _hud.Text(o.Rows[i].Text, rect.X + 4, rect.Y + 2, selected ? Ink : Text, maxChars: 74);
        }
        if (o.Rows.Count == 0) _hud.Text("nothing here yet", 20, 38, Dim);

        _hud.Text("Tab/arrows: tabs   Up/Down: rows   Enter: act   Esc: close", 20, H - 22, Dim);
    }

    // ---- toasts --------------------------------------------------------------------------

    private void DrawToasts(Toasts toasts)
    {
        for (var i = 0; i < toasts.Live.Count; i++)
        {
            var t = toasts.Live[i];
            var text = UiLayout.ToastText(t.Text);
            var rect = UiLayout.ToastRect(i, t.Text);
            _hud.Panel(rect.X, rect.Y, rect.W, rect.H, dark: t.Kind == ToastKind.Error);
            var ink = t.Kind switch { ToastKind.Success => Green, ToastKind.Error => Red, ToastKind.Attention => Gold, _ => Text };
            _hud.Text(text, rect.X + 6, rect.Y + 4, ink);
        }
    }
}
