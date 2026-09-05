using System.Numerics;

namespace HomeWorkplace.Office.Ui;

/// <summary>
/// Where the menus put things on the 480×272 screen. The renderer draws with these and the
/// clicks hit-test with them, so a click always lands on what was drawn.
/// </summary>
public static class MenuLayout
{
    public const int W = UiLayout.W;
    public const int H = UiLayout.H;
    public const int Line = UiLayout.Line;

    // ---- the title and the main column ----
    public const int TitleZoom = 4;
    public const int TitleY = 40;
    public const int TitleItemsY = 124;
    public const int TitleItemPitch = Line + 7;
    public const int TitleItemWidth = 200;
    public const int HintY = H - 24;

    public static UiRect PanelBox(MenuScreen m)
    {
        var h = 26 + m.Items.Count * (Line + 2) + 10;
        return new UiRect((W - 220) / 2, (H - h) / 2, 220, h);
    }

    public static UiRect ItemRect(MenuScreen m, int i)
    {
        if (m.Style == MenuStyle.Panel)
        {
            var box = PanelBox(m);
            return new UiRect(box.X + 12, box.Y + 26 + i * (Line + 2), box.W - 24, Line);
        }
        return new UiRect((W - TitleItemWidth) / 2, TitleItemsY + i * TitleItemPitch, TitleItemWidth, Line);
    }

    public static int? ItemAt(MenuScreen m, Vector2 p)
    {
        for (var i = 0; i < m.Items.Count; i++)
            if (ItemRect(m, i).Contains(p)) return i;
        return null;
    }

    // ---- the workplace list ----
    public const int WorkplaceVisibleRows = 5;
    public const int WorkplaceRowHeight = 2 * Line + 2;
    public const int WorkplaceRowPitch = WorkplaceRowHeight + 4;
    public const int WorkplaceRowsY = 36;
    public const int WorkplaceButtonWidth = 46;
    public const int FooterY = H - 44;
    public static UiRect WorkplaceBox => UiLayout.OverlayBox;

    /// <summary>Index of the first visible row so the selection stays on screen.</summary>
    public static int WorkplaceFirstRow(WorkplaceSelect s)
    {
        var anchor = Math.Min(s.Selected, Math.Max(0, s.Rows.Count - 1));
        return Math.Max(0, Math.Min(anchor - WorkplaceVisibleRows + 1, s.Rows.Count - WorkplaceVisibleRows));
    }

    public static UiRect WorkplaceRowRect(int visibleIndex)
        => new(16, WorkplaceRowsY + visibleIndex * WorkplaceRowPitch, W - 32, WorkplaceRowHeight);

    public static int? WorkplaceRowAt(WorkplaceSelect s, Vector2 p)
    {
        var first = WorkplaceFirstRow(s);
        for (var i = first; i < Math.Min(s.Rows.Count, first + WorkplaceVisibleRows); i++)
            if (WorkplaceRowRect(i - first).Contains(p)) return i;
        return null;
    }

    /// <summary>The button strip sits on the second line of the selected row, right-aligned.</summary>
    public static UiRect WorkplaceButtonRect(UiRect row, WorkplaceButton button)
    {
        var count = WorkplaceSelect.Buttons.Count;
        var x = row.X + row.W - 4 - (count - (int)button) * (WorkplaceButtonWidth + 2);
        return new UiRect(x, row.Y + Line + 1, WorkplaceButtonWidth, Line);
    }

    public static WorkplaceButton? WorkplaceButtonAt(WorkplaceSelect s, Vector2 p)
    {
        if (s.OnFooter) return null;
        var row = WorkplaceRowRect(s.Selected - WorkplaceFirstRow(s));
        foreach (var b in WorkplaceSelect.Buttons)
            if (WorkplaceButtonRect(row, b).Contains(p)) return b;
        return null;
    }

    public static UiRect FooterRect(int i)
        => i == WorkplaceSelect.FooterNew ? new UiRect(20, FooterY, 100, Line + 2) : new UiRect(132, FooterY, 44, Line + 2);

    public static int? FooterAt(Vector2 p)
    {
        for (var i = 0; i < 2; i++)
            if (FooterRect(i).Contains(p)) return i;
        return null;
    }

    // ---- settings ----
    public const int SettingsRowsY = 38;
    public const int SettingsValueX = 236;

    public static UiRect SettingsTabRect(SettingsTab tab)
    {
        var x = 20;
        foreach (var t in SettingsScreen.Tabs)
        {
            var width = Markup.Measure(t.ToString());
            if (t == tab) return new UiRect(x - 4, 16, width + 7, Line + 2);
            x += width + 16;
        }
        return default;
    }

    public static SettingsTab? SettingsTabAt(Vector2 p)
    {
        foreach (var t in SettingsScreen.Tabs)
            if (SettingsTabRect(t).Contains(p)) return t;
        return null;
    }

    public static UiRect SettingsRowRect(int i) => new(16, SettingsRowsY + i * (Line + 2) - 2, W - 32, Line + 2);

    public static int? SettingsRowAt(SettingsScreen s, Vector2 p)
    {
        for (var i = 0; i < s.Rows.Count; i++)
            if (SettingsRowRect(i).Contains(p)) return i;
        return null;
    }
}
