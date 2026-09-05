using System.Numerics;
using HomeWorkplace.Client;
using HomeWorkplace.Live;
using HomeWorkplace.Office;
using HomeWorkplace.Office.Sim;
using HomeWorkplace.Office.Ui;
using ToastKind = HomeWorkplace.Office.Ui.ToastKind;

namespace HomeWorkplace.Office.Tests;

public class OfficeUiTests
{
    private readonly FakeForemanApi _foreman = new();
    private readonly FakeContextApi _context = new();
    private readonly AppStore _store = new();
    private readonly List<string> _sounds = new();
    private readonly OfficeUi _ui;

    public OfficeUiTests()
    {
        var ada = FakeForemanApi.Employee("ada", EmployeeStatus.Working, taskId: "t1");
        var rex = FakeForemanApi.Employee("rex", EmployeeStatus.Waiting, taskId: "t2");
        var mia = FakeForemanApi.Employee("mia", role: "Engineering manager");
        _foreman.Employees["ada"] = ada; _foreman.Employees["rex"] = rex; _foreman.Employees["mia"] = mia;
        var t1 = FakeForemanApi.Task("t1", TaskState.Running);
        var t2 = FakeForemanApi.Task("t2", TaskState.NeedsHuman, assignee: "rex") with { AwaitingApproval = true };
        _foreman.Tasks["t1"] = t1; _foreman.Tasks["t2"] = t2;
        _store.SetAll(new[] { ada, rex, mia }, new[] { t1, t2 }, Array.Empty<GoalDto>());
        _ui = new OfficeUi(_store, _foreman, _context, () => Array.Empty<CliStatus>(), _sounds.Add);
        _ui.OnStoreChanged();   // baseline: existing events never toast
    }

    private async Task Settle()
    {
        for (var i = 0; i < 20 && _ui.Pending is not null; i++)
        {
            await Task.Yield();
            _ui.Update(0.016f);
        }
    }

    [Fact]
    public async Task Giving_a_task_through_the_dialogue_reaches_foreman()
    {
        _ui.OpenEmployee("ada");
        var dialogue = Assert.IsType<Dialogue>(_ui.State.Top);
        Assert.Equal("ada", dialogue.SpeakerId);
        Assert.Contains("page", _sounds);

        dialogue.CompleteReveal();
        _ui.Key(UiKey.Accept);                     // "Give a task" is first (no human needed)
        await Settle();
        var entry = Assert.IsType<TextEntry>(_ui.State.Top);
        Assert.Contains("Ada", entry.Title);

        foreach (var c in "Fix bug") _ui.Key(UiKey.Char(c));
        _ui.Key(UiKey.Accept);
        foreach (var c in "It crashes") _ui.Key(UiKey.Char(c));
        _ui.Key(UiKey.Accept);
        await Settle();

        Assert.Contains("createTask:ada:Fix bug:It crashes", _foreman.Calls);
        Assert.False(_ui.State.IsOpen);
        Assert.Contains(_ui.Toasts.Live, t => t.Kind == ToastKind.Success);
        Assert.Contains("ding", _sounds);
    }

    [Fact]
    public async Task Approving_from_rex_runs_directly_and_a_failure_buzzes()
    {
        _ui.OpenEmployee("rex");
        ((Dialogue)_ui.State.Top!).CompleteReveal();
        _ui.Key(UiKey.Accept);                     // Approve
        await Settle();
        Assert.Contains("approve:t2", _foreman.Calls);

        _foreman.Throw = new ApiException(409, "already approved", null);
        _ui.OpenEmployee("rex");
        ((Dialogue)_ui.State.Top!).CompleteReveal();
        _ui.Key(UiKey.Accept);
        await Settle();
        Assert.Contains("buzz", _sounds);
        Assert.Contains(_ui.Toasts.Live, t => t.Kind == ToastKind.Error);
    }

    [Fact]
    public async Task Destructive_actions_confirm_and_talk_to_opens_a_dialogue()
    {
        _ui.OpenOverlay(OverlayTab.Employees);
        _ui.Key(UiKey.Accept);                     // Ada's row actions
        var actions = Assert.IsType<Dialogue>(_ui.State.Top);
        Assert.Equal("Talk to Ada", actions.Options[0].Label);
        _ui.Key(UiKey.Accept);
        await Settle();
        var talk = Assert.IsType<Dialogue>(_ui.State.Top);
        Assert.Equal("ada", talk.SpeakerId);
        Assert.IsType<Overlay>(_ui.State.Layers[0]);   // still underneath

        talk.CompleteReveal();
        talk.Select(talk.Options.ToList().FindIndex(o => o.Action is Reset));
        _ui.Key(UiKey.Accept);
        await Settle();
        Assert.IsType<Confirm>(_ui.State.Top);
        _ui.Key(UiKey.Accept);                     // yes
        await Settle();
        Assert.Contains("reset:ada", _foreman.Calls);
    }

    [Fact]
    public void Interact_opens_the_target_and_the_whiteboard_lists_goals()
    {
        _ui.Interact(new Interactable(InteractKind.Whiteboard, null));
        var board = Assert.IsType<Dialogue>(_ui.State.Top);
        Assert.Null(board.SpeakerId);
        Assert.Contains(board.Options, o => o.Action is SetGoal { ManagerId: "mia" });

        _ui.State.Clear();
        _ui.Interact(new Interactable(InteractKind.Employee, "rex"));
        Assert.Equal("rex", ((Dialogue)_ui.State.Top!).SpeakerId);
        _ui.Interact(null);
        Assert.Single(_ui.State.Layers);
    }

    [Fact]
    public void A_new_human_needed_event_toasts_and_clicking_the_toast_opens_the_employee()
    {
        _store.AddEvent(new EventDto { Seq = 5, Type = "human.needed", EmployeeId = "rex", Timestamp = DateTimeOffset.UtcNow });
        _ui.OnStoreChanged();
        var toast = Assert.Single(_ui.Toasts.Live);
        Assert.Equal(ToastKind.Attention, toast.Kind);
        Assert.Contains("Rex", toast.Text);

        _ui.OnStoreChanged();                      // same event again: no second toast
        Assert.Single(_ui.Toasts.Live);

        var rect = UiLayout.ToastRect(0, toast.Text);
        Assert.True(_ui.Click(new Vector2(rect.X + 2, rect.Y + 2)));
        Assert.Equal("rex", ((Dialogue)_ui.State.Top!).SpeakerId);
    }

    [Fact]
    public async Task Clicking_a_dialogue_option_picks_it()
    {
        _ui.OpenEmployee("ada");
        var d = (Dialogue)_ui.State.Top!;
        d.CompleteReveal();
        var leave = d.Options.ToList().FindIndex(o => o.Action is Leave);
        var rect = UiLayout.DialogueOptionRect(leave);
        Assert.True(_ui.Click(new Vector2(rect.X + 1, rect.Y + 1)));
        await Settle();
        Assert.False(_ui.State.IsOpen);
    }

    [Fact]
    public void Overlay_refreshes_when_the_store_changes()
    {
        _ui.OpenOverlay(OverlayTab.Employees);
        Assert.Equal(3, ((Overlay)_ui.State.Top!).Rows.Count);
        _store.SetEmployee(FakeForemanApi.Employee("zed"));
        _ui.OnStoreChanged();
        Assert.Equal(4, ((Overlay)_ui.State.Top!).Rows.Count);
    }
}

public class UiKeyMappingTests
{
    [Theory]
    [InlineData(Microsoft.Xna.Framework.Input.Keys.Enter, UiKeyKind.Accept)]
    [InlineData(Microsoft.Xna.Framework.Input.Keys.Escape, UiKeyKind.Back)]
    [InlineData(Microsoft.Xna.Framework.Input.Keys.Tab, UiKeyKind.Tab)]
    [InlineData(Microsoft.Xna.Framework.Input.Keys.Back, UiKeyKind.Backspace)]
    [InlineData(Microsoft.Xna.Framework.Input.Keys.Up, UiKeyKind.Up)]
    [InlineData(Microsoft.Xna.Framework.Input.Keys.PageDown, UiKeyKind.PageDown)]
    public void Keys_map_to_ui_keys(Microsoft.Xna.Framework.Input.Keys key, UiKeyKind expected)
        => Assert.Equal(expected, InputMap.UiKeyFor(key)!.Value.Kind);

    [Fact]
    public void Letters_do_not_map_they_arrive_as_text_input()
        => Assert.Null(InputMap.UiKeyFor(Microsoft.Xna.Framework.Input.Keys.A));
}

public class HiringInteractionTests
{
    [Fact]
    public async Task Interacting_with_the_stand_opens_the_hiring_dialogue()
    {
        var foreman = new FakeForemanApi { Hiring = HiringDialogueTests.Hiring() };
        var store = new AppStore();
        var ui = new OfficeUi(store, foreman, new FakeContextApi(), () => new[] { new CliStatus("claude", CliState.SignedIn, "2.1", null) }, _ => { });

        ui.Interact(new Interactable(InteractKind.HiringStand, null));
        for (var i = 0; i < 20 && ui.Pending is not null; i++) { await Task.Yield(); ui.Update(0.016f); }

        var d = Assert.IsType<Dialogue>(ui.State.Top);
        Assert.Equal("Hiring stand", d.SpeakerName);
    }
}

public class TicketInteractionTests
{
    [Fact]
    public async Task The_board_opens_on_interact_and_a_claim_toasts()
    {
        var foreman = new FakeForemanApi();
        foreman.Employees["ada"] = FakeForemanApi.Employee("ada", role: "Software engineer");
        var store = new AppStore();
        store.SetAll(new[] { foreman.Employees["ada"] }, Array.Empty<TaskDto>(), Array.Empty<GoalDto>());
        var sounds = new List<string>();
        var ui = new OfficeUi(store, foreman, new FakeContextApi(), () => Array.Empty<CliStatus>(), sounds.Add);
        ui.OnStoreChanged();

        ui.Interact(new Interactable(InteractKind.TicketBoard, null));
        for (var i = 0; i < 20 && ui.Pending is not null; i++) { await Task.Yield(); ui.Update(0.016f); }
        Assert.Equal("Ticket board", ((Dialogue)ui.State.Top!).SpeakerName);

        store.AddEvent(new EventDto { Seq = 7, Type = "task.claimed", EmployeeId = "ada", TaskId = "t1", Timestamp = DateTimeOffset.UtcNow });
        ui.OnStoreChanged();
        Assert.Contains(ui.Toasts.Live, t => t.Text.Contains("Ada") && t.Text.Contains("ticket"));
    }
}
