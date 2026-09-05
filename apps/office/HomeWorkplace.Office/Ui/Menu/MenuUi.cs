using System.Numerics;
using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Ui;

/// <summary>
/// The menus, engine-free: the main menu over the showroom, the workplace list, the settings,
/// and the pause menu in the office. Screens emit <see cref="MenuAction"/>s; this carries out
/// the ones about workplaces and settings itself and raises <see cref="Requested"/> for the
/// ones only the game can do: play a workplace, leave it, quit.
/// </summary>
public sealed class MenuUi
{
    private readonly Workplaces _workplaces;
    private readonly Action<string> _openFolder;

    public MenuUi(Workplaces workplaces, SettingsModel settings, Action<string>? openFolder = null)
    {
        _workplaces = workplaces;
        Settings = settings;
        _openFolder = openFolder ?? (_ => { });
    }

    public UiState State { get; } = new();
    public Toasts Toasts { get; } = new();
    public SettingsModel Settings { get; }

    /// <summary>Play, leave, quit: the game does these.</summary>
    public event Action<MenuAction>? Requested;

    public bool IsOpen => State.IsOpen;
    public bool Typing => State.Top is TextEntry;
    /// <summary>A Controls row is waiting for the next key; the game feeds it through <see cref="KeyCaptured"/>.</summary>
    public bool Capturing => State.Top is SettingsScreen { Capturing: true };

    public static MenuScreen MainMenu() => new("Home Workplace", new MenuItem[]
    {
        new("Single Player", new OpenSinglePlayer()),
        new("Multiplayer", new OpenMultiplayer()),
        new("Settings", new OpenSettings()),
        new("Exit", new QuitGame()),
    }, MenuStyle.Title, closable: false);

    public static MenuScreen MultiplayerMenu() => new("Multiplayer", new MenuItem[]
    {
        new("Host & Play", new HostAndPlay(), Enabled: false, Hint: "next update"),
        new("Join via IP", new JoinViaIp(), Enabled: false, Hint: "next update"),
        new("Back", new GoBack()),
    });

    public static MenuScreen PauseMenu() => new("Paused", new MenuItem[]
    {
        new("Resume", new ResumeOffice()),
        new("Settings", new OpenSettings()),
        new("Leave the office", new LeaveOffice()),
        new("Quit", new QuitGame()),
    }, MenuStyle.Panel);

    public void OpenMain()
    {
        State.Clear();
        State.Push(MainMenu());
    }

    public void OpenPause()
    {
        if (!State.IsOpen) State.Push(PauseMenu());
    }

    public void Close() => State.Clear();

    public void Key(UiKey key) => Apply(State.Handle(key));

    public void Hover(Vector2 p)
    {
        switch (State.Top)
        {
            case MenuScreen m: m.Hover(p); break;
            case WorkplaceSelect w: w.Hover(p); break;
            case SettingsScreen s: s.Hover(p); break;
        }
    }

    /// <summary>A click at native coordinates; true when a menu consumed it.</summary>
    public bool Click(Vector2 p)
    {
        var result = State.Top switch
        {
            MenuScreen m => m.Click(p),
            WorkplaceSelect w => w.Click(p),
            SettingsScreen s => s.Click(p),
            Confirm c => ConfirmClick(c, p),
            null => ClickResult.Miss,
            _ => ClickResult.Selected,   // a text entry is modal
        };
        if (result == ClickResult.Activate) Key(UiKey.Accept);
        return State.IsOpen;
    }

    /// <summary>The key pressed while a Controls row waits for one.</summary>
    public void KeyCaptured(string keyName)
    {
        if (State.Top is SettingsScreen s) s.Bind(keyName);
    }

    private static ClickResult ConfirmClick(Confirm c, Vector2 p)
    {
        var box = UiLayout.ConfirmBox(c);
        var y = UiLayout.ConfirmButtonsY(c);
        if (UiLayout.ConfirmYesRect(box, y).Contains(p)) { c.Handle(UiKey.Left); return ClickResult.Activate; }
        if (UiLayout.ConfirmNoRect(box, y).Contains(p)) { c.Handle(UiKey.Right); return ClickResult.Activate; }
        return ClickResult.Selected;
    }

    private void Apply(LayerResult result)
    {
        if (result.Kind is not (LayerResultKind.Emit or LayerResultKind.Submit)) return;
        switch (result.Payload)
        {
            case OpenSinglePlayer: State.Push(new WorkplaceSelect(_workplaces.List())); break;
            case OpenMultiplayer: State.Push(MultiplayerMenu()); break;
            case OpenSettings: State.Push(new SettingsScreen(Settings)); break;
            case GoBack: State.Pop(); break;
            case ResumeOffice: Close(); break;
            case NewWorkplace:
                State.Push(new TextEntry("New workplace", new[] { new Field("Name", false, 32) }, payload: new NewWorkplace()));
                break;
            case RenameWorkplace r:
                State.Push(new TextEntry("Rename workplace", new[] { new Field("Name", false, 32) }, payload: r, initial: new[] { r.Name }));
                break;
            case DuplicateWorkplace d:
                var copy = _workplaces.Duplicate(d.Name);
                Refresh(copy.Name);
                Toasts.Add($"Copied to {copy.Name}", ToastKind.Success, null);
                break;
            case DeleteWorkplace d:
                State.Push(new Confirm($"Delete \"{d.Name}\"? It moves to the trash folder, nothing is lost.", payload: new ConfirmedDelete(d.Name)));
                break;
            case ConfirmedDelete d:
                _workplaces.Delete(d.Name);
                Refresh(null);
                Toasts.Add($"Moved {d.Name} to the trash", ToastKind.Info, null);
                break;
            case OpenWorkplaceFolder f:
                _openFolder(_workplaces.Get(f.Name).Root);
                Toasts.Add($"Opened {f.Name}", ToastKind.Info, null);
                break;
            case ToggleFavourite t:
                var info = _workplaces.Get(t.Name);
                Refresh(_workplaces.SetFavourite(t.Name, !info.Favourite).Name);
                break;
            case TextSubmitted { Payload: NewWorkplace } ts:
                var created = _workplaces.Create(ts.Values[0]);
                Refresh(created.Name);
                Toasts.Add($"Created {created.Name}", ToastKind.Success, null);
                break;
            case TextSubmitted { Payload: RenameWorkplace r } ts:
                Refresh(_workplaces.Rename(r.Name, ts.Values[0]).Name);
                break;
            case TextSubmitted { Payload: EditPlayerName } ts:
                Settings.SetName(ts.Values[0]);
                break;
            case PlayWorkplace or QuitGame or LeaveOffice:
                Requested?.Invoke((MenuAction)result.Payload!);
                break;
        }
    }

    private void Refresh(string? select)
    {
        if (State.Top is WorkplaceSelect w) w.Refresh(_workplaces.List(), select);
    }
}
