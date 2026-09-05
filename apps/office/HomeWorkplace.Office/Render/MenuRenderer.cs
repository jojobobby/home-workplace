using HomeWorkplace.Office.Ui;
using Microsoft.Xna.Framework;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// Draws the menu layers in native coordinates: the big title and its column, the workplace
/// list with its button strip, the settings tabs, and the pause panel. Behind any menu the
/// scene dims, like Terraria's world behind its menu.
/// </summary>
public sealed class MenuRenderer
{
    private const int W = UiLayout.W;
    private const int H = UiLayout.H;
    private const int Line = UiLayout.Line;
    public const string Title = "HOME WORKPLACE";

    private readonly Hud _hud;

    public MenuRenderer(Hud hud) => _hud = hud;

    /// <summary>Behind any menu the scene dims; under the main menu the big title appears too.</summary>
    public void DrawBackdrop(UiState ui)
    {
        MenuScreen? root = null;
        foreach (var layer in ui.Layers)
            if (layer is MenuScreen m) { root = m; break; }
        if (root is null) return;
        _hud.Fill(0, 0, W, H, Color.Black * (root.Style == MenuStyle.Title ? 0.5f : 0.35f));
        if (root.Style == MenuStyle.Title)
        {
            var x = (W - Hud.PixelTextWidth(Title, MenuLayout.TitleZoom)) / 2;
            _hud.PixelTextBig(Title, x, MenuLayout.TitleY, MenuLayout.TitleZoom, UiPalette.Gold, UiPalette.Ink);
        }
    }

    public void Draw(MenuScreen m)
    {
        if (m.Style == MenuStyle.Panel)
        {
            var box = MenuLayout.PanelBox(m);
            _hud.Panel(box.X, box.Y, box.W, box.H);
            _hud.Text(m.Title, box.X + 12, box.Y + 8, UiPalette.Gold);
        }
        else if (m.Closable)
            Centred(m.Title, MenuLayout.TitleItemsY - Line - 8, UiPalette.Gold);   // a sub-menu's heading; the main menu has the big title

        for (var i = 0; i < m.Items.Count; i++)
        {
            var item = m.Items[i];
            var rect = MenuLayout.ItemRect(m, i);
            var selected = i == m.Selected;
            if (selected) _hud.Fill(rect.X - 4, rect.Y - 1, rect.W + 8, rect.H, UiPalette.Field);
            var label = item.Hint is { } hint ? $"{item.Label}  [small dim]{hint}[/]" : item.Label;
            var ink = !item.Enabled ? UiPalette.Dim : selected ? UiPalette.Gold : UiPalette.Text;
            if (m.Style == MenuStyle.Panel)
            {
                if (selected) _hud.Text(">", rect.X, rect.Y, UiPalette.Gold);
                _hud.Text(label, rect.X + 10, rect.Y, ink);
            }
            else
            {
                var x = rect.X + (rect.W - Markup.Measure(label)) / 2;
                if (selected) _hud.Text(">", x - 12, rect.Y, UiPalette.Gold);
                _hud.Text(label, x, rect.Y, ink);
            }
        }

        if (m.Style == MenuStyle.Title)
            Centred(m.Closable ? "Up/Down: pick   Enter: choose   Esc: back" : "Up/Down: pick   Enter: choose", MenuLayout.HintY, UiPalette.Dim);
    }

    public void Draw(WorkplaceSelect s)
    {
        var box = MenuLayout.WorkplaceBox;
        _hud.Panel(box.X, box.Y, box.W, box.H);
        var first = MenuLayout.WorkplaceFirstRow(s);
        var last = Math.Min(s.Rows.Count, first + MenuLayout.WorkplaceVisibleRows);
        var count = s.Rows.Count > MenuLayout.WorkplaceVisibleRows ? $"{first + 1}-{last} of {s.Rows.Count}"
                  : s.Rows.Count > 0 ? $"{s.Rows.Count} workplace{(s.Rows.Count == 1 ? "" : "s")}" : "";
        _hud.Text(count.Length == 0 ? "Workplaces" : $"Workplaces  [small dim]{count}[/]", 20, 16, UiPalette.Gold);   // toasts own the top right
        if (s.Rows.Count == 0)
            _hud.Text("No workplaces yet. Make one below.", 20, MenuLayout.WorkplaceRowsY + 2, UiPalette.Dim);

        var now = s.Clock();
        for (var i = first; i < last; i++)
        {
            var w = s.Rows[i];
            var rect = MenuLayout.WorkplaceRowRect(i - first);
            var selected = i == s.Selected;
            if (selected) _hud.Fill(rect.X, rect.Y, rect.W, rect.H, UiPalette.Field);
            var name = (w.Favourite ? "[gold]*[/] " : "") + w.Name;
            _hud.Text(name, rect.X + 6, rect.Y + 1, selected ? UiPalette.Gold : UiPalette.Text, maxChars: 40);
            _hud.Text($"[small dim]{WorkplaceSelect.Details(w, now)}[/]", rect.X + 6, rect.Y + 1 + Line, UiPalette.Dim);
            if (!selected) continue;
            foreach (var b in WorkplaceSelect.Buttons)
            {
                var br = MenuLayout.WorkplaceButtonRect(rect, b);
                var on = b == s.Button;
                _hud.Fill(br.X, br.Y, br.W, br.H, on ? UiPalette.Highlight : UiPalette.Ink);
                var label = WorkplaceSelect.Label(b);
                _hud.Text(label, br.X + (br.W - Markup.Measure(label)) / 2, br.Y, on ? UiPalette.Ink : UiPalette.Text);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            var fr = MenuLayout.FooterRect(i);
            var on = s.OnFooter && s.Footer == i;
            _hud.Fill(fr.X, fr.Y, fr.W, fr.H, on ? UiPalette.Highlight : UiPalette.Ink);
            var label = i == WorkplaceSelect.FooterNew ? "New workplace" : "Back";
            _hud.Text(label, fr.X + (fr.W - Markup.Measure(label)) / 2, fr.Y + 1, on ? UiPalette.Ink : UiPalette.Text);
        }
        _hud.Text("Up/Down: pick   Left/Right: buttons   Enter: choose   Esc: back", 20, MenuLayout.HintY, UiPalette.Dim);
    }

    public void Draw(SettingsScreen s)
    {
        var box = UiLayout.OverlayBox;
        _hud.Panel(box.X, box.Y, box.W, box.H);
        foreach (var tab in SettingsScreen.Tabs)
        {
            var rect = MenuLayout.SettingsTabRect(tab);
            var selected = tab == s.Tab;
            if (selected) _hud.Fill(rect.X, rect.Y, rect.W, rect.H, UiPalette.Highlight);
            _hud.Text(tab.ToString(), rect.X + 4, rect.Y + 2, selected ? UiPalette.Ink : UiPalette.Dim);
        }
        _hud.Fill(16, 32, W - 32, 1, UiPalette.Highlight);

        var rows = s.Rows;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rect = MenuLayout.SettingsRowRect(i);
            var selected = i == s.Selected;
            if (selected) _hud.Fill(rect.X, rect.Y, rect.W, rect.H, UiPalette.Field);
            _hud.Text(row.Label, rect.X + 4, rect.Y + 2, selected ? UiPalette.Gold : UiPalette.Text);

            var x = MenuLayout.SettingsValueX;
            var y = rect.Y + 2;
            var steppable = row.Kind is SettingKind.Choice or SettingKind.Toggle or SettingKind.Slider or SettingKind.Colour;
            var valueWidth = Markup.Measure(row.Value) + (row.Kind == SettingKind.Colour ? 14 : 0);
            if (selected && steppable)
            {
                _hud.Text("<", x - 12, y, UiPalette.Dim);
                _hud.Text(">", x + valueWidth + 6, y, UiPalette.Dim);
            }
            if (row.Kind == SettingKind.Colour)
            {
                var index = Math.Max(0, SettingsModel.Colours.ToList().IndexOf(row.Value));
                var shirt = SpriteGenerator.Shirts[index % SpriteGenerator.Shirts.Length];
                _hud.Fill(x, y + 1, 9, 9, new Color(shirt.R, shirt.G, shirt.B));
                _hud.Text(row.Value, x + 14, y, selected ? UiPalette.Gold : UiPalette.Text);
            }
            else if (row.Kind == SettingKind.Key && selected && s.Capturing)
                _hud.Text("press a key...", x, y, UiPalette.Gold);
            else
                _hud.Text(row.Value, x, y, selected ? UiPalette.Gold : UiPalette.Text);
        }
        _hud.Text("Tab: next tab   Up/Down: row   Left/Right: change   Enter: set   Esc: back", 20, MenuLayout.HintY, UiPalette.Dim);
    }

    private void Centred(string text, int y, Color ink) => _hud.Text(text, (W - Markup.Measure(text)) / 2, y, ink);

    private void Right(string text, int right, int y) => _hud.Text(text, right - Markup.Measure(text), y, UiPalette.Dim);
}
