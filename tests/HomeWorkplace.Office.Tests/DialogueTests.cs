using HomeWorkplace.Client;
using HomeWorkplace.Office.Ui;

namespace HomeWorkplace.Office.Tests;

public class DialogueTests
{
    private static EmployeeDto Emp(string id, string name, EmployeeStatus status, string role = "Software engineer", string? taskId = null)
        => new() { Id = id, Name = name, Role = role, Status = status, CurrentTaskId = taskId, Energy = 80, RunsToday = 2 };

    private static Dictionary<string, TaskDto> Tasks(params TaskDto[] tasks) => tasks.ToDictionary(t => t.Id);
    private static Dictionary<string, GoalDto> Goals(params GoalDto[] goals) => goals.ToDictionary(g => g.Id);
    private static string[] Labels(Dialogue d) => d.Options.Select(o => o.Label).ToArray();

    [Fact]
    public void An_asleep_employee_offers_wake()
    {
        var d = DialogueScript.For(Emp("ada", "Ada", EmployeeStatus.Asleep), Tasks(), Goals());
        Assert.Equal("ada", d.SpeakerId);
        Assert.Contains(d.Lines, l => l.Contains("asleep", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "Give a task", "Wake", "Open room brief", "Let go", "Reset", "Leave" }, Labels(d));
        Assert.IsType<Wake>(d.Options[1].Action);
    }

    [Fact]
    public void A_working_employee_names_the_task_and_offers_sleep()
    {
        var task = new TaskDto { Id = "t1", Title = "Build the parser", Assignee = "ada", Status = TaskState.Running };
        var d = DialogueScript.For(Emp("ada", "Ada", EmployeeStatus.Working, taskId: "t1"), Tasks(task), Goals());
        Assert.Contains(d.Lines, l => l.Contains("Build the parser"));
        Assert.Equal(new[] { "Give a task", "Sleep", "Open room brief", "Let go", "Reset", "Leave" }, Labels(d));
        Assert.Equal(new GiveTask("ada"), d.Options[0].Action);
    }

    [Fact]
    public void A_task_needing_a_human_puts_approve_answer_and_cancel_first()
    {
        var task = new TaskDto { Id = "t2", Title = "Review PR", Assignee = "rex", Status = TaskState.NeedsHuman, AwaitingApproval = true, PendingQuestion = "Merge to main?" };
        var d = DialogueScript.For(Emp("rex", "Rex", EmployeeStatus.Waiting, taskId: "t2"), Tasks(task), Goals());
        Assert.Contains(d.Lines, l => l.Contains("Merge to main?"));
        Assert.Equal(new[] { "Approve", "Answer", "Cancel task" }, Labels(d).Take(3));
        Assert.Equal(new Approve("t2"), d.Options[0].Action);
        Assert.Equal(new Answer("t2"), d.Options[1].Action);
        Assert.Equal(new CancelTask("t2"), d.Options[2].Action);
    }

    [Fact]
    public void A_failed_last_task_adds_retry()
    {
        var task = new TaskDto { Id = "t3", Title = "Ship it", Assignee = "ada", Status = TaskState.Failed };
        var d = DialogueScript.For(Emp("ada", "Ada", EmployeeStatus.Awake), Tasks(task), Goals());
        Assert.Contains("Retry last task", Labels(d));
        Assert.Equal(new Retry("t3"), d.Options.Single(o => o.Label == "Retry last task").Action);
    }

    [Fact]
    public void A_manager_can_set_goals_and_top_up_active_ones()
    {
        var active = new GoalDto { Id = "g1", Title = "Launch v1", Manager = "mia", Status = GoalState.Blocked, BudgetUsd = 5, SpentUsd = 5 };
        var done = new GoalDto { Id = "g2", Title = "Old goal", Manager = "mia", Status = GoalState.Done };
        var d = DialogueScript.For(Emp("mia", "Mia", EmployeeStatus.Awake, role: "Engineering manager"), Tasks(), Goals(active, done));
        var labels = Labels(d);
        Assert.Contains("Set a goal", labels);
        Assert.Contains("Top up: Launch v1", labels);
        Assert.DoesNotContain("Top up: Old goal", labels);
        Assert.Equal(new TopUp("g1"), d.Options.Single(o => o.Label == "Top up: Launch v1").Action);
        Assert.Equal(new SetGoal("mia"), d.Options.Single(o => o.Label == "Set a goal").Action);
    }

    [Fact]
    public void The_whiteboard_lists_goals_with_spend_and_offers_actions()
    {
        var goal = new GoalDto { Id = "g1", Title = "Launch v1", Manager = "mia", Status = GoalState.Running, BudgetUsd = 10, SpentUsd = 2.5m };
        var d = DialogueScript.Whiteboard(Goals(goal), new[] { Emp("ada", "Ada", EmployeeStatus.Awake), Emp("mia", "Mia", EmployeeStatus.Awake, role: "Engineering manager") });
        Assert.Contains(d.Lines, l => l.Contains("Launch v1") && l.Contains("2.50") && l.Contains("10.00"));
        Assert.Equal(new[] { "Top up: Launch v1", "Cancel: Launch v1", "Set a goal", "Leave" }, Labels(d));
        Assert.Equal(new SetGoal("mia"), d.Options[2].Action);

        var empty = DialogueScript.Whiteboard(Goals(), new[] { Emp("ada", "Ada", EmployeeStatus.Awake) });
        Assert.Contains(empty.Lines, l => l.Contains("empty", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "Leave" }, Labels(empty));   // no manager, no goal setting
    }

    [Fact]
    public void Text_reveals_over_time_and_any_key_completes_it_before_options_work()
    {
        var d = new Dialogue("ada", "Ada", new[] { "Hello there." }, new[] { new DialogueOption("A", new Leave()), new DialogueOption("B", new Leave()) });
        Assert.False(d.IsRevealed);
        d.Update(0.1f);
        Assert.Equal(4, d.Revealed);               // 40 chars/s

        Assert.Equal(LayerResultKind.None, d.Handle(UiKey.Down).Kind);
        Assert.True(d.IsRevealed);
        Assert.Equal(0, d.Selected);               // the key only completed the reveal

        d.Handle(UiKey.Down);
        Assert.Equal(1, d.Selected);
        d.Handle(UiKey.Down);
        Assert.Equal(0, d.Selected);               // wraps
        var result = d.Handle(UiKey.Accept);
        Assert.Equal(LayerResultKind.Submit, result.Kind);
        Assert.IsType<Leave>(result.Payload);
        Assert.Equal(LayerResultKind.Pop, d.Handle(UiKey.Back).Kind);
    }
}

public class DialogueReasonTests
{
    private static EmployeeDto Emp(string id, string name, EmployeeStatus status, string role = "Software engineer")
        => new() { Id = id, Name = name, Role = role, Status = status };

    [Fact]
    public void A_failed_task_tells_you_why()
    {
        var task = new TaskDto
        {
            Id = "t3", Title = "Ship it", Assignee = "ada", Status = TaskState.Failed,
            Runs = new[] { new RunDto { Id = "r1", Employee = "ada", Status = "Failed", ResultSummary = "Your organization has disabled Claude subscription access for Claude Code" } },
        };
        var d = DialogueScript.For(Emp("ada", "Ada", EmployeeStatus.Awake), new Dictionary<string, TaskDto> { ["t3"] = task }, new Dictionary<string, GoalDto>());
        Assert.Contains(d.Lines, l => l.Contains("organization has disabled"));
    }

    [Fact]
    public void The_whiteboard_says_when_a_goals_manager_is_asleep_and_offers_to_wake_them()
    {
        var goal = new GoalDto { Id = "g1", Title = "Launch v1", Manager = "mia", Status = GoalState.Planning, BudgetUsd = 10 };
        var d = DialogueScript.Whiteboard(new Dictionary<string, GoalDto> { ["g1"] = goal }, new[] { Emp("mia", "Mia", EmployeeStatus.Asleep, role: "Engineering manager") });
        Assert.Contains(d.Lines, l => l.Contains("asleep", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(d.Options, o => o.Label == "Wake Mia" && o.Action is Wake { EmployeeId: "mia" });
    }

    [Fact]
    public void The_whiteboard_shows_a_goals_last_manager_error()
    {
        var goal = new GoalDto { Id = "g1", Title = "Launch v1", Manager = "mia", Status = GoalState.Planning, BudgetUsd = 10, LastError = "Claude refused: subscription access disabled" };
        var d = DialogueScript.Whiteboard(new Dictionary<string, GoalDto> { ["g1"] = goal }, new[] { Emp("mia", "Mia", EmployeeStatus.Awake, role: "Engineering manager") });
        Assert.Contains(d.Lines, l => l.Contains("subscription access disabled"));
    }
}

public class HiringDialogueTests
{
    internal static HiringDto Hiring() => new(
        new[]
        {
            new HiringTemplateDto("engineer", "Software engineer", "Builds features test-first.", new[]
            {
                new BrainCostDto("claude-haiku-4-5-20251001", Vendor.Claude, "Claude Haiku 4.5", 0.10m, 0.60m),
                new BrainCostDto("claude-fable-5-1", Vendor.Claude, "Claude Fable 5.1", 1.50m, 9.00m),
                new BrainCostDto("gpt-5-codex", Vendor.Codex, "GPT-5 Codex", 0.16m, 0.93m),
            }),
            new HiringTemplateDto("manager", "Engineering manager", "Runs goals on a budget.", new[]
            {
                new BrainCostDto("claude-opus-5", Vendor.Claude, "Claude Opus 5", 0.18m, 1.40m),
            }),
        },
        Array.Empty<BrainDto>());

    [Fact]
    public void The_stand_lists_roles_with_the_cheapest_available_daily_cost()
    {
        var d = DialogueScript.Hiring(Hiring(), new HashSet<Vendor> { Vendor.Codex });   // only Codex signed in
        Assert.Equal("Hiring stand", d.SpeakerName);
        Assert.Contains(d.Lines, l => l.Contains("notional", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "Software engineer  $0.93+/day", "Engineering manager (sign in)", "Leave" }, d.Options.Select(o => o.Label));
        Assert.Equal(new HireRole("engineer"), d.Options[0].Action);
        Assert.False(d.Options[1].Enabled);
    }

    [Fact]
    public void The_brain_dialogue_prices_each_brain_and_gates_by_sign_in()
    {
        var d = DialogueScript.Brains(Hiring().Templates[0], new HashSet<Vendor> { Vendor.Claude });
        Assert.Contains(d.Lines, l => l.Contains("Builds features"));
        Assert.Equal("Claude Haiku 4.5  $0.60/day", d.Options[0].Label);
        Assert.Equal(new HireBrain("engineer", "claude-haiku-4-5-20251001", "Claude Haiku 4.5"), d.Options[0].Action);
        Assert.True(d.Options[0].Enabled);
        Assert.Equal("GPT-5 Codex (sign in)", d.Options[2].Label);
        Assert.False(d.Options[2].Enabled);
        Assert.Equal("Back", d.Options[3].Label);
        Assert.IsType<OpenHiring>(d.Options[3].Action);
        Assert.Equal("Leave", d.Options[4].Label);
    }

    [Fact]
    public void A_disabled_option_cannot_be_picked()
    {
        var d = DialogueScript.Brains(Hiring().Templates[0], new HashSet<Vendor>());
        d.CompleteReveal();
        Assert.Equal(LayerResultKind.None, d.Handle(UiKey.Accept).Kind);
        d.Select(3);   // Back is always enabled
        Assert.Equal(LayerResultKind.Submit, d.Handle(UiKey.Accept).Kind);
    }

    [Fact]
    public void An_employee_can_be_let_go_from_their_dialogue()
    {
        var e = new EmployeeDto { Id = "ada", Name = "Ada", Role = "Software engineer", Status = EmployeeStatus.Awake };
        var d = DialogueScript.For(e, new Dictionary<string, TaskDto>(), new Dictionary<string, GoalDto>());
        var letGo = d.Options.Single(o => o.Label == "Let go");
        Assert.Equal(new Fire("ada"), letGo.Action);
        Assert.True(d.Options.ToList().FindIndex(o => o.Label == "Let go") < d.Options.ToList().FindIndex(o => o.Label == "Reset"));
    }
}
