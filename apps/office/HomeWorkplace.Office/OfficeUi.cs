using System.Numerics;
using HomeWorkplace.Client;
using HomeWorkplace.Live;
using HomeWorkplace.Office.Sim;
using HomeWorkplace.Office.Ui;
using ToastKind = HomeWorkplace.Office.Ui.ToastKind;

namespace HomeWorkplace.Office;

/// <summary>
/// The office's management UI, engine-free: owns the layer stack, toasts and journal, opens
/// dialogues for employees and the whiteboard, routes keys and clicks, runs the actions the
/// user picks and applies their outcomes, and turns new Foreman events into toasts.
/// </summary>
public sealed class OfficeUi
{
    private readonly AppStore _store;
    private readonly Func<IReadOnlyList<CliStatus>> _setup;
    private readonly Action<string> _play;
    private readonly Actions _actions;
    private long _lastEventSeq = -1;

    public OfficeUi(AppStore store, IForemanApi foreman, IContextApi context, Func<IReadOnlyList<CliStatus>> setup, Action<string> play)
    {
        _store = store;
        _setup = setup;
        _play = play;
        _actions = new Actions(foreman, context, Journal, Toasts, Snapshot);
    }

    public UiState State { get; } = new();
    public Toasts Toasts { get; } = new();
    public Journal Journal { get; } = new();
    /// <summary>The action in flight, if any. Its outcome is applied on the next <see cref="Update"/> after it completes.</summary>
    public Task<ActionOutcome>? Pending { get; private set; }

    public bool IsOpen => State.IsOpen;
    public bool Typing => State.Top is TextEntry;

    private OverlaySnapshot Snapshot() => new(_store.Employees, _store.Tasks, _store.Goals, _store.RecentEvents, _setup());

    // ---- opening things --------------------------------------------------------------------

    public void OpenEmployee(string id)
    {
        if (!_store.Employees.TryGetValue(id, out var e)) return;
        State.Push(DialogueScript.For(e, _store.Tasks, _store.Goals));
        _play("page");
    }

    public void OpenWhiteboard()
    {
        State.Push(DialogueScript.Whiteboard(_store.Goals, _store.Employees.Values));
        _play("page");
    }

    public void OpenOverlay(OverlayTab tab = OverlayTab.Employees)
    {
        if (State.Layers.OfType<Overlay>().Any()) return;
        State.Push(new Overlay(tab, Snapshot()));
        _play("page");
    }

    public void Interact(Interactable? target)
    {
        switch (target)
        {
            case { Kind: InteractKind.Employee, EmployeeId: { } id }: OpenEmployee(id); break;
            case { Kind: InteractKind.Whiteboard }: OpenWhiteboard(); break;
            case { Kind: InteractKind.HiringStand }: Pending = _actions.RunAsync(new OpenHiring()); break;
        }
    }

    // ---- input -------------------------------------------------------------------------------

    public void Key(UiKey key)
    {
        var typing = State.Top is TextEntry;
        var result = State.Handle(key);
        if (typing && key.Kind == UiKeyKind.Char) _play("keys");
        if (result.Kind == LayerResultKind.Submit) OnSubmit(result.Payload);
    }

    /// <summary>A click at native coordinates. Returns true when the UI consumed it.</summary>
    public bool Click(Vector2 p)
    {
        for (var i = 0; i < Toasts.Live.Count; i++)
        {
            var toast = Toasts.Live[i];
            if (!UiLayout.ToastRect(i, toast.Text).Contains(p)) continue;
            Toasts.Dismiss(toast);
            if (toast.EmployeeId is { } id) OpenEmployee(id);
            return true;
        }

        switch (State.Top)
        {
            case Dialogue d:
                if (!d.IsRevealed) { d.CompleteReveal(); return true; }
                var option = UiLayout.DialogueOptionAt(d, p);
                if (option >= 0) { d.Select(option); Key(UiKey.Accept); return true; }
                return UiLayout.DialogueBox.Contains(p);
            case Overlay o:
                if (UiLayout.OverlayTabAt(p) is { } tab) { o.ShowTab(tab); return true; }
                var row = UiLayout.OverlayRowAt(o, p);
                if (row >= 0)
                {
                    if (row == o.Selected) Key(UiKey.Accept);
                    else o.Select(row);
                }
                return true;
            case null:
                return false;
            default:
                return true;   // text entry and confirm are modal
        }
    }

    private void OnSubmit(object? payload)
    {
        switch (payload)
        {
            case TalkTo t: OpenEmployee(t.EmployeeId); break;
            case Leave: break;
            case UiAction a: Pending = _actions.RunAsync(a); break;
            case TextSubmitted ts: Pending = _actions.SubmitAsync(ts); break;
            case ConfirmedAction c: Pending = _actions.ConfirmedAsync(c); break;
        }
    }

    // ---- per frame ---------------------------------------------------------------------------

    public void Update(float dt)
    {
        Toasts.Update(dt);
        if (State.Top is Dialogue d) d.Update(dt);

        if (Pending is { IsCompleted: true } p)
        {
            Pending = null;
            Apply(p.IsCompletedSuccessfully ? p.Result : new Failed(p.Exception?.GetBaseException().Message ?? "the action failed"));
        }
    }

    private void Apply(ActionOutcome outcome)
    {
        switch (outcome)
        {
            case OpenText t: State.Push(t.Entry); break;
            case OpenDialogue d: State.Push(d.Dialogue); _play("page"); break;
            case NeedConfirm c: State.Push(c.Confirm); break;
            case Done: _play("ding"); break;
            case Failed f:
                _play("buzz");
                if (!Toasts.Live.Any(t => t.Kind == ToastKind.Error && t.Text == f.Message)) Toasts.Add(f.Message, ToastKind.Error, null);
                break;
        }
    }

    /// <summary>Refresh open overlays and toast the events that need you. Call whenever the store changed.</summary>
    public void OnStoreChanged()
    {
        var snapshot = Snapshot();
        foreach (var o in State.Layers.OfType<Overlay>()) o.Refresh(snapshot);

        var events = _store.RecentEvents;
        var newest = events.Count == 0 ? 0 : events.Max(e => e.Seq);
        if (_lastEventSeq < 0) { _lastEventSeq = newest; return; }   // events from before launch never toast

        foreach (var e in events.Where(e => e.Seq > _lastEventSeq).OrderBy(e => e.Seq))
        {
            var who = e.EmployeeId is { } id ? NameOf(id) : "Someone";
            switch (e.Type)
            {
                case "human.needed":
                    Toasts.Add($"{who} needs you", ToastKind.Attention, e.EmployeeId);
                    _play("page");
                    break;
                case "run.finished" when Data(e, "status") == "Failed":
                    Toasts.Add($"{who}'s run failed", ToastKind.Error, e.EmployeeId);
                    break;
                case "run.finished" when Data(e, "status") == "Done" && Data(e, "manager") != "true":
                    Toasts.Add($"{who} finished a task", ToastKind.Success, e.EmployeeId);
                    break;
                case "goal.blocked":
                    Toasts.Add("A goal is over budget: top it up", ToastKind.Attention, e.EmployeeId);
                    break;
            }
        }
        _lastEventSeq = Math.Max(_lastEventSeq, newest);
    }

    private static string? Data(EventDto e, string name)
        => e.Data is { ValueKind: System.Text.Json.JsonValueKind.Object } d && d.TryGetProperty(name, out var v) ? v.ToString() : null;

    private string NameOf(string id) => _store.Employees.TryGetValue(id, out var e) ? e.Name : id;
}
