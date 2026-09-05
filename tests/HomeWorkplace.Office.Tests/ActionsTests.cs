using HomeWorkplace.Client;
using HomeWorkplace.Office.Ui;

namespace HomeWorkplace.Office.Tests;

public class ActionsTests
{
    private readonly FakeForemanApi _foreman = new();
    private readonly FakeContextApi _context = new();
    private readonly Journal _journal = new();
    private readonly Toasts _toasts = new();
    private readonly Actions _actions;

    public ActionsTests()
    {
        _foreman.Employees["ada"] = FakeForemanApi.Employee("ada", EmployeeStatus.Working, taskId: "t1");
        _foreman.Employees["mia"] = FakeForemanApi.Employee("mia", role: "Engineering manager");
        _foreman.Tasks["t1"] = FakeForemanApi.Task("t1", TaskState.Running);
        _foreman.Tasks["t2"] = FakeForemanApi.Task("t2", TaskState.NeedsHuman, assignee: "rex");
        _foreman.Goals["g1"] = FakeForemanApi.Goal("g1");
        _actions = new Actions(_foreman, _context, _journal, _toasts,
            () => new OverlaySnapshot(_foreman.Employees, _foreman.Tasks, _foreman.Goals, Array.Empty<EventDto>(), Array.Empty<CliStatus>()));
    }

    private static TextSubmitted Submit(TextEntry entry, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            foreach (var c in values[i]) entry.Handle(UiKey.Char(c));
            if (i < values.Length - 1) entry.Handle(UiKey.Accept);
        }
        var result = entry.Handle(UiKey.Accept);
        Assert.Equal(LayerResultKind.Submit, result.Kind);
        return Assert.IsType<TextSubmitted>(result.Payload);
    }

    [Fact]
    public async Task Give_a_task_asks_for_title_and_brief_then_creates_it()
    {
        var open = Assert.IsType<OpenText>(await _actions.RunAsync(new GiveTask("ada")));
        Assert.Equal(new[] { "Title", "Brief" }, open.Entry.Fields.Select(f => f.Name));
        Assert.Contains("Ada", open.Entry.Title);

        var outcome = await _actions.SubmitAsync(Submit(open.Entry, "Fix bug", "It crashes"));
        Assert.IsType<Done>(outcome);
        Assert.Contains("createTask:ada:Fix bug:It crashes", _foreman.Calls);
        Assert.Contains(_toasts.Live, t => t.Kind == ToastKind.Success && t.Text.Contains("Fix bug"));
        Assert.Contains(_journal.Entries, e => e.Text.Contains("Fix bug"));
    }

    [Fact]
    public async Task Direct_actions_call_foreman_immediately()
    {
        Assert.IsType<Done>(await _actions.RunAsync(new Wake("ada")));
        Assert.IsType<Done>(await _actions.RunAsync(new Sleep("ada")));
        Assert.IsType<Done>(await _actions.RunAsync(new Approve("t2")));
        Assert.IsType<Done>(await _actions.RunAsync(new Retry("t1")));
        Assert.IsType<Done>(await _actions.RunAsync(new Reassign("t1", "mia")));
        Assert.IsType<Done>(await _actions.RunAsync(new ReloadEmployees()));
        Assert.Equal(new[] { "wake:ada", "sleep:ada", "approve:t2", "retry:t1", "reassign:t1:mia", "reload" }, _foreman.Calls);
    }

    [Fact]
    public async Task Destructive_actions_confirm_first()
    {
        var need = Assert.IsType<NeedConfirm>(await _actions.RunAsync(new CancelTask("t1")));
        Assert.Contains("T t1", need.Confirm.Question);
        Assert.Empty(_foreman.Calls);

        var yes = need.Confirm.Handle(UiKey.Accept);
        Assert.IsType<Done>(await _actions.ConfirmedAsync(yes.Payload!));
        Assert.Equal(new[] { "cancelTask:t1" }, _foreman.Calls);

        Assert.IsType<NeedConfirm>(await _actions.RunAsync(new Reset("ada")));
        Assert.IsType<NeedConfirm>(await _actions.RunAsync(new CancelGoal("g1")));
    }

    [Fact]
    public async Task Answer_and_top_up_take_text_and_a_goal_takes_a_budget()
    {
        var answer = Assert.IsType<OpenText>(await _actions.RunAsync(new Answer("t2")));
        await _actions.SubmitAsync(Submit(answer.Entry, "Yes, merge"));
        Assert.Contains("answer:t2:Yes, merge", _foreman.Calls);

        var topUp = Assert.IsType<OpenText>(await _actions.RunAsync(new TopUp("g1")));
        await _actions.SubmitAsync(Submit(topUp.Entry, "2.5"));
        Assert.Contains("topup:g1:2.5", _foreman.Calls);

        var goal = Assert.IsType<OpenText>(await _actions.RunAsync(new SetGoal("mia")));
        Assert.Equal(new[] { "Title", "Brief", "Budget USD" }, goal.Entry.Fields.Select(f => f.Name));
        var bad = await _actions.SubmitAsync(Submit(goal.Entry, "Launch", "Ship v1", "lots"));
        Assert.IsType<Failed>(bad);
        Assert.DoesNotContain(_foreman.Calls, c => c.StartsWith("createGoal"));

        var goal2 = Assert.IsType<OpenText>(await _actions.RunAsync(new SetGoal("mia")));
        Assert.IsType<Done>(await _actions.SubmitAsync(Submit(goal2.Entry, "Launch", "Ship v1", "5")));
        Assert.Contains("createGoal:mia:Launch:Ship v1:5", _foreman.Calls);
    }

    [Fact]
    public async Task Open_brief_shows_the_current_task_room_or_says_there_is_none()
    {
        _context.Briefs["task-t1"] = "# Room\nAda: started the parser.\nRex: ok";
        var open = Assert.IsType<OpenDialogue>(await _actions.RunAsync(new OpenBrief("ada")));
        Assert.Equal("ada", open.Dialogue.SpeakerId);
        Assert.Contains(open.Dialogue.Lines, l => l.Contains("started the parser"));
        Assert.Equal(new[] { "brief:task-t1" }, _context.Calls);

        var none = Assert.IsType<OpenDialogue>(await _actions.RunAsync(new OpenBrief("mia")));
        Assert.Contains(none.Dialogue.Lines, l => l.Contains("no task", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Leave_and_talk_to_are_not_service_calls()
    {
        Assert.IsType<Nothing>(await _actions.RunAsync(new Leave()));
        Assert.IsType<Nothing>(await _actions.RunAsync(new TalkTo("ada")));
        Assert.Empty(_foreman.Calls);
    }

    [Fact]
    public async Task An_api_error_becomes_a_failed_outcome_a_toast_and_a_journal_line()
    {
        _foreman.Throw = new ApiException(409, "Ada is asleep", null);
        var failed = Assert.IsType<Failed>(await _actions.RunAsync(new Approve("t2")));
        Assert.Contains("asleep", failed.Message);
        Assert.Contains(_toasts.Live, t => t.Kind == ToastKind.Error);
        Assert.Contains(_journal.Entries, e => e.Text.Contains("asleep"));
    }

    [Fact]
    public async Task A_second_action_while_one_is_in_flight_is_refused()
    {
        var hold = new TaskCompletionSource();
        _foreman.Hold = hold;
        var first = _actions.RunAsync(new Wake("ada"));
        Assert.True(_actions.Busy);
        var second = Assert.IsType<Failed>(await _actions.RunAsync(new Sleep("ada")));
        Assert.Contains("busy", second.Message, StringComparison.OrdinalIgnoreCase);

        hold.SetResult();
        Assert.IsType<Done>(await first);
        Assert.False(_actions.Busy);
        Assert.Equal(new[] { "wake:ada" }, _foreman.Calls);
    }

    [Fact]
    public void The_journal_keeps_the_last_fifty()
    {
        var journal = new Journal();
        for (var i = 0; i < 60; i++) journal.Add($"entry {i}");
        Assert.Equal(Journal.Max, journal.Entries.Count);
        Assert.Equal("entry 10", journal.Entries[0].Text);
        Assert.Equal("entry 59", journal.Entries[^1].Text);
    }
}

public class HiringActionsTests
{
    private readonly FakeForemanApi _foreman = new();
    private readonly Toasts _toasts = new();
    private readonly Actions _actions;

    public HiringActionsTests()
    {
        _foreman.Hiring = HiringDialogueTests.Hiring();
        _foreman.Employees["ada"] = FakeForemanApi.Employee("ada");
        var setup = new[] { new CliStatus("claude", CliState.SignedIn, "2.1", null), new CliStatus("codex", CliState.InstalledNotSignedIn, "0.1", null) };
        _actions = new Actions(_foreman, new FakeContextApi(), new Journal(), _toasts,
            () => new OverlaySnapshot(_foreman.Employees, _foreman.Tasks, _foreman.Goals, Array.Empty<EventDto>(), setup));
    }

    [Fact]
    public async Task Opening_the_stand_fetches_the_roles_and_picking_one_shows_its_brains()
    {
        var stand = Assert.IsType<OpenDialogue>(await _actions.RunAsync(new OpenHiring()));
        Assert.Equal("Hiring stand", stand.Dialogue.SpeakerName);
        Assert.Contains("hiring", _foreman.Calls);
        Assert.Contains(stand.Dialogue.Options, o => o.Label.StartsWith("Software engineer") && o.Enabled);

        var brains = Assert.IsType<OpenDialogue>(await _actions.RunAsync(new HireRole("engineer")));
        Assert.Contains(brains.Dialogue.Options, o => o.Label.StartsWith("Claude Fable 5.1") && o.Enabled);
        Assert.Contains(brains.Dialogue.Options, o => o.Label.StartsWith("GPT-5 Codex") && !o.Enabled);   // codex not signed in
    }

    [Fact]
    public async Task Picking_a_brain_asks_for_a_name_then_hires()
    {
        var text = Assert.IsType<OpenText>(await _actions.RunAsync(new HireBrain("engineer", "claude-haiku-4-5-20251001", "Claude Haiku 4.5")));
        Assert.Equal("Name", Assert.Single(text.Entry.Fields).Name);
        Assert.Contains("Claude Haiku 4.5", text.Entry.Title);
        foreach (var c in "Grace") text.Entry.Handle(UiKey.Char(c));
        var submitted = Assert.IsType<TextSubmitted>(text.Entry.Handle(UiKey.Accept).Payload);

        var done = Assert.IsType<Done>(await _actions.SubmitAsync(submitted));
        Assert.Contains("Grace", done.Message);
        Assert.Contains("hire:engineer:claude-haiku-4-5-20251001:Grace", _foreman.Calls);
        Assert.Contains(_toasts.Live, t => t.Kind == ToastKind.Success && t.Text.Contains("Grace"));
    }

    [Fact]
    public async Task Letting_someone_go_confirms_then_fires()
    {
        var need = Assert.IsType<NeedConfirm>(await _actions.RunAsync(new Fire("ada")));
        Assert.Contains("Ada", need.Confirm.Question);
        Assert.IsType<Done>(await _actions.ConfirmedAsync(need.Confirm.Handle(UiKey.Accept).Payload!));
        Assert.Contains("fire:ada", _foreman.Calls);
        Assert.DoesNotContain("ada", _foreman.Employees.Keys);
    }
}
