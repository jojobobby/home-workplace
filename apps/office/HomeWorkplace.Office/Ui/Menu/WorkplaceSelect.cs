using System.Numerics;
using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Ui;

/// <summary>The strip on the selected row, Terraria's world buttons.</summary>
public enum WorkplaceButton { Play, Rename, Duplicate, Delete, Folder, Favourite }

/// <summary>
/// The workplace list: one row per workplace, a button strip on the selected one, and a footer
/// with New workplace and Back. Up/Down move through rows and on to the footer, Left/Right move
/// along the buttons, Enter presses, Esc goes back. Clicking a row plays it; its buttons do
/// what they say.
/// </summary>
public sealed class WorkplaceSelect : ILayer
{
    public static readonly IReadOnlyList<WorkplaceButton> Buttons = Enum.GetValues<WorkplaceButton>();
    public const int FooterNew = 0;
    public const int FooterBack = 1;

    public WorkplaceSelect(IReadOnlyList<WorkplaceInfo> rows)
    {
        Rows = rows;
        if (rows.Count == 0) Selected = 0;
    }

    public IReadOnlyList<WorkplaceInfo> Rows { get; private set; }
    /// <summary>A row index, or <c>Rows.Count</c> for the footer.</summary>
    public int Selected { get; private set; }
    public WorkplaceButton Button { get; private set; } = WorkplaceButton.Play;
    public int Footer { get; private set; } = FooterNew;
    public bool OnFooter => Selected >= Rows.Count;
    public WorkplaceInfo? Current => OnFooter ? null : Rows[Selected];

    /// <summary>New rows after a change; the selection follows <paramref name="select"/> when it names a row.</summary>
    public void Refresh(IReadOnlyList<WorkplaceInfo> rows, string? select = null)
    {
        Rows = rows;
        var index = select is null ? -1 : IndexOf(rows, select);
        Selected = index >= 0 ? index : Math.Clamp(Selected, 0, rows.Count);
    }

    public void SelectRow(int index) => Selected = Math.Clamp(index, 0, Rows.Count);

    public LayerResult Handle(UiKey key)
    {
        switch (key.Kind)
        {
            case UiKeyKind.Up: SelectRow(Selected - 1); break;
            case UiKeyKind.Down: SelectRow(Selected + 1); break;
            case UiKeyKind.PageUp: SelectRow(Selected - MenuLayout.WorkplaceVisibleRows); break;
            case UiKeyKind.PageDown: SelectRow(Selected + MenuLayout.WorkplaceVisibleRows); break;
            case UiKeyKind.Left: case UiKeyKind.Right:
                var step = key.Kind == UiKeyKind.Right ? 1 : -1;
                if (OnFooter) Footer = Math.Clamp(Footer + step, FooterNew, FooterBack);
                else Button = (WorkplaceButton)(((int)Button + step + Buttons.Count) % Buttons.Count);
                break;
            case UiKeyKind.Tab:
                if (!OnFooter) Button = (WorkplaceButton)(((int)Button + 1) % Buttons.Count);
                break;
            case UiKeyKind.Accept:
                if (OnFooter) return Footer == FooterNew ? LayerResult.Emit(new NewWorkplace()) : LayerResult.Pop();
                return LayerResult.Emit(ActionFor(Button, Rows[Selected]));
            case UiKeyKind.Back:
                return LayerResult.Pop();
        }
        return LayerResult.None();
    }

    public static MenuAction ActionFor(WorkplaceButton button, WorkplaceInfo w) => button switch
    {
        WorkplaceButton.Play => new PlayWorkplace(w.Name),
        WorkplaceButton.Rename => new RenameWorkplace(w.Name),
        WorkplaceButton.Duplicate => new DuplicateWorkplace(w.Name),
        WorkplaceButton.Delete => new DeleteWorkplace(w.Name),
        WorkplaceButton.Folder => new OpenWorkplaceFolder(w.Name),
        _ => new ToggleFavourite(w.Name),
    };

    public static string Label(WorkplaceButton button) => button switch
    {
        WorkplaceButton.Play => "Play",
        WorkplaceButton.Rename => "Rename",
        WorkplaceButton.Duplicate => "Copy",
        WorkplaceButton.Delete => "Delete",
        WorkplaceButton.Folder => "Folder",
        _ => "Fav",
    };

    /// <summary>The second line of a row: how many work there and when it was last opened.</summary>
    public static string Details(WorkplaceInfo w, DateTimeOffset now)
    {
        var staff = w.EmployeeCount == 1 ? "1 employee" : $"{w.EmployeeCount} employees";
        var opened = w.LastOpened is { } t ? "opened " + Ago(now - t) : "never opened";
        return $"{staff}   {opened}";
    }

    private static string Ago(TimeSpan age)
        => age < TimeSpan.FromMinutes(1) ? "just now"
         : age < TimeSpan.FromHours(1) ? $"{(int)age.TotalMinutes} min ago"
         : age < TimeSpan.FromDays(1) ? $"{(int)age.TotalHours} h ago"
         : age < TimeSpan.FromDays(30) ? $"{(int)age.TotalDays} d ago"
         : "long ago";

    public void Hover(Vector2 p)
    {
        if (MenuLayout.FooterAt(p) is { } f) { Selected = Rows.Count; Footer = f; return; }
        if (MenuLayout.WorkplaceRowAt(this, p) is not { } row) return;
        Selected = row;
        if (MenuLayout.WorkplaceButtonAt(this, p) is { } b) Button = b;
    }

    public ClickResult Click(Vector2 p)
    {
        if (MenuLayout.FooterAt(p) is { } f) { Selected = Rows.Count; Footer = f; return ClickResult.Activate; }
        if (MenuLayout.WorkplaceRowAt(this, p) is not { } row) return ClickResult.Miss;
        if (row != Selected) { Selected = row; return ClickResult.Selected; }
        Button = MenuLayout.WorkplaceButtonAt(this, p) ?? WorkplaceButton.Play;   // the row itself plays
        return ClickResult.Activate;
    }

    private static int IndexOf(IReadOnlyList<WorkplaceInfo> rows, string name)
    {
        for (var i = 0; i < rows.Count; i++)
            if (string.Equals(rows[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
