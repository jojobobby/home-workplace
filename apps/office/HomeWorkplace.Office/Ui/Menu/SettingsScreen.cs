using System.Numerics;
using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Ui;

/// <summary>
/// Five tabs of rows over a <see cref="SettingsModel"/>. Tab or PageDown/PageUp switch tabs,
/// Up/Down pick a row, Left/Right change its value, Enter steps it (or waits for a key on a
/// Controls row, or opens a text entry for the player name), Esc goes back.
/// </summary>
public sealed class SettingsScreen : ILayer
{
    public static readonly SettingsTab[] Tabs = Enum.GetValues<SettingsTab>();

    private readonly SettingsModel _model;

    public SettingsScreen(SettingsModel model) => _model = model;

    public SettingsTab Tab { get; private set; }
    public int Selected { get; private set; }
    /// <summary>A Controls row is waiting for the next key.</summary>
    public bool Capturing { get; private set; }
    public IReadOnlyList<SettingRow> Rows => _model.Rows(Tab);
    public SettingRow? Current => Rows.Count == 0 ? null : Rows[Math.Min(Selected, Rows.Count - 1)];

    public void ShowTab(SettingsTab tab)
    {
        Tab = tab;
        Selected = 0;
        Capturing = false;
    }

    public void Select(int index)
    {
        var n = Rows.Count;
        Selected = n == 0 ? 0 : ((index % n) + n) % n;
    }

    public LayerResult Handle(UiKey key)
    {
        if (Capturing)
        {
            if (key.Kind == UiKeyKind.Back) Capturing = false;   // the key itself arrives through Bind
            return LayerResult.None();
        }
        switch (key.Kind)
        {
            case UiKeyKind.Tab: case UiKeyKind.PageDown: ShowTab(Tabs[(Array.IndexOf(Tabs, Tab) + 1) % Tabs.Length]); break;
            case UiKeyKind.PageUp: ShowTab(Tabs[(Array.IndexOf(Tabs, Tab) + Tabs.Length - 1) % Tabs.Length]); break;
            case UiKeyKind.Up: Select(Selected - 1); break;
            case UiKeyKind.Down: Select(Selected + 1); break;
            case UiKeyKind.Left: Step(-1); break;
            case UiKeyKind.Right: Step(+1); break;
            case UiKeyKind.Accept: return Activate();
            case UiKeyKind.Back: return LayerResult.Pop();
        }
        return LayerResult.None();
    }

    /// <summary>The key the game saw while a Controls row waited; binds it and stops waiting.</summary>
    public void Bind(string keyName)
    {
        var row = Current;
        Capturing = false;
        if (row is null || !row.Key.StartsWith("key:", StringComparison.Ordinal)) return;
        _model.Bind(Enum.Parse<GameAction>(row.Key[4..]), keyName);
    }

    public void Hover(Vector2 p)
    {
        if (MenuLayout.SettingsRowAt(this, p) is { } r) Selected = r;
    }

    public ClickResult Click(Vector2 p)
    {
        if (MenuLayout.SettingsTabAt(p) is { } t) { ShowTab(t); return ClickResult.Selected; }
        if (MenuLayout.SettingsRowAt(this, p) is not { } r) return ClickResult.Miss;
        Selected = r;
        return ClickResult.Activate;
    }

    private void Step(int direction)
    {
        if (Current is { } row && row.Kind is not (SettingKind.Key or SettingKind.Text)) _model.Step(row.Key, direction);
    }

    private LayerResult Activate()
    {
        if (Current is not { } row) return LayerResult.None();
        switch (row.Kind)
        {
            case SettingKind.Key:
                Capturing = true;
                return LayerResult.None();
            case SettingKind.Text:
                return LayerResult.Push(new TextEntry("Player name", new[] { new Field("Name", false, SettingsModel.MaxNameLength) },
                    payload: new EditPlayerName(), initial: new[] { _model.Config.PlayerName }));
            default:
                _model.Step(row.Key, +1);
                return LayerResult.None();
        }
    }
}
