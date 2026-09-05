using System.Numerics;

namespace HomeWorkplace.Office.Ui;

/// <summary>Title: a centred column under the big title (the main menu). Panel: a small box over whatever is behind (the pause menu).</summary>
public enum MenuStyle { Title, Panel }

/// <summary>One line of a menu. A disabled item is shown dim with its hint and cannot be chosen.</summary>
public sealed record MenuItem(string Label, MenuAction Action, bool Enabled = true, string? Hint = null);

/// <summary>
/// A column of choices. Up/Down (and the mouse) pick, Enter emits the item's action and the
/// screen stays open, Esc closes it unless it is the root menu.
/// </summary>
public sealed class MenuScreen : ILayer
{
    public MenuScreen(string title, IReadOnlyList<MenuItem> items, MenuStyle style = MenuStyle.Title, bool closable = true)
    {
        Title = title;
        Items = items;
        Style = style;
        Closable = closable;
    }

    public string Title { get; }
    public IReadOnlyList<MenuItem> Items { get; }
    public MenuStyle Style { get; }
    public bool Closable { get; }
    public int Selected { get; private set; }

    /// <summary>Wraps around, like the dialogue options.</summary>
    public void Select(int index)
    {
        if (Items.Count > 0) Selected = ((index % Items.Count) + Items.Count) % Items.Count;
    }

    public LayerResult Handle(UiKey key)
    {
        switch (key.Kind)
        {
            case UiKeyKind.Up: Select(Selected - 1); break;
            case UiKeyKind.Down: case UiKeyKind.Tab: Select(Selected + 1); break;
            case UiKeyKind.Accept:
                if (Items.Count > 0 && Items[Selected].Enabled) return LayerResult.Emit(Items[Selected].Action);
                break;
            case UiKeyKind.Back:
                return Closable ? LayerResult.Pop() : LayerResult.None();
        }
        return LayerResult.None();
    }

    public void Hover(Vector2 p)
    {
        if (MenuLayout.ItemAt(this, p) is { } i) Select(i);
    }

    public ClickResult Click(Vector2 p)
    {
        if (MenuLayout.ItemAt(this, p) is not { } i) return ClickResult.Miss;
        Select(i);
        return Items[i].Enabled ? ClickResult.Activate : ClickResult.Selected;
    }
}
