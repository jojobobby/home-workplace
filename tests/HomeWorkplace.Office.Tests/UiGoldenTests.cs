using System.Numerics;
using HomeWorkplace.Client;
using HomeWorkplace.Office.Sim;
using HomeWorkplace.Office.Ui;

namespace HomeWorkplace.Office.Tests;

[Collection("gpu")]
public class UiGoldenTests
{
    private readonly GoldenHost _host;

    public UiGoldenTests(GoldenHost host) => _host = host;

    private static Simulation SeededOffice()
    {
        var sim = new Simulation(WorldLayout.Generate(new[] { "ada-coder", "mia-manager", "rex-reviewer", "vfx-artist" }), seed: 7);
        sim.Apply(new EmployeeAppeared("ada-coder", "Ada", EmployeeStatus.Working, "Build the parser"));
        sim.Apply(new EmployeeAppeared("mia-manager", "Mia", EmployeeStatus.Awake, null));
        sim.Apply(new EmployeeAppeared("rex-reviewer", "Rex", EmployeeStatus.Awake, null));
        sim.Apply(new EmployeeAppeared("vfx-artist", "Vex", EmployeeStatus.Asleep, null));
        for (var i = 0; i < 60 * 40; i++) sim.Update(1f / 60f);
        sim.Apply(new HumanNeeded("rex-reviewer"));
        return sim;
    }

    private static EmployeeDto Rex() => new() { Id = "rex-reviewer", Name = "Rex", Role = "Code reviewer", Status = EmployeeStatus.Waiting, CurrentTaskId = "t2", Energy = 70, RunsToday = 3 };

    private static Dictionary<string, TaskDto> Tasks() => new()
    {
        ["t1"] = new TaskDto { Id = "t1", Title = "Build the parser", Assignee = "ada-coder", Status = TaskState.Running, UpdatedAt = DateTimeOffset.Parse("2026-09-04T10:00:00Z") },
        ["t2"] = new TaskDto { Id = "t2", Title = "Review the parser PR", Assignee = "rex-reviewer", Status = TaskState.NeedsHuman, AwaitingApproval = true, PendingQuestion = "Merge to main now?", UpdatedAt = DateTimeOffset.Parse("2026-09-04T11:00:00Z") },
        ["t3"] = new TaskDto { Id = "t3", Title = "Sprite sheet v1", Assignee = "vfx-artist", Status = TaskState.Failed, UpdatedAt = DateTimeOffset.Parse("2026-09-04T09:00:00Z") },
    };

    [Fact]
    public void A_dialogue_with_rex_matches_its_golden()
    {
        var sim = SeededOffice();
        var you = new Player(sim.World);
        you.Teleport(sim.Agents["rex-reviewer"].Position + new Vector2(Agent.TileSize, 0));
        var dialogue = DialogueScript.For(Rex(), Tasks(), new Dictionary<string, GoalDto>());
        dialogue.CompleteReveal();
        var ui = new UiState();
        ui.Push(dialogue);

        Golden.Check(_host, "ui-dialogue", _host.RenderUi(sim, ui, new Toasts(), you), tolerance: 0.005);
    }

    [Fact]
    public void The_overlay_tasks_tab_matches_its_golden()
    {
        var employees = new Dictionary<string, EmployeeDto> { ["rex-reviewer"] = Rex() };
        var overlay = new Overlay(OverlayTab.Tasks, new OverlaySnapshot(employees, Tasks(), new Dictionary<string, GoalDto>(), Array.Empty<EventDto>(), Array.Empty<CliStatus>()));
        overlay.Handle(UiKey.Down);
        var ui = new UiState();
        ui.Push(overlay);

        Golden.Check(_host, "ui-overlay-tasks", _host.RenderUi(SeededOffice(), ui, new Toasts()), tolerance: 0.005);
    }

    [Fact]
    public void A_text_entry_with_a_caret_matches_its_golden()
    {
        var entry = new TextEntry("New task for Ada", new[] { new Field("Title", false, 60), new Field("Brief", true, 600) }, payload: null);
        foreach (var c in "Fix the parser") entry.Handle(UiKey.Char(c));
        entry.Handle(UiKey.Accept);
        foreach (var c in "It crashes on empty input. Add a test and fix the loop.") entry.Handle(UiKey.Char(c));
        var ui = new UiState();
        ui.Push(entry);

        Golden.Check(_host, "ui-textentry", _host.RenderUi(SeededOffice(), ui, new Toasts(), time: 0f), tolerance: 0.005);
    }

    [Fact]
    public void A_confirm_and_toasts_match_their_golden()
    {
        var ui = new UiState();
        ui.Push(new Confirm("Cancel \"Review the parser PR\"? Its runs stop.", payload: null));
        var toasts = new Toasts();
        toasts.Add("Rex needs you", ToastKind.Attention, "rex-reviewer");
        toasts.Add("Task \"Fix the parser\" given to Ada", ToastKind.Success, null);
        toasts.Add("Foreman is unreachable", ToastKind.Error, null);

        Golden.Check(_host, "ui-confirm-toasts", _host.RenderUi(SeededOffice(), ui, toasts), tolerance: 0.005);
    }
}

[Collection("gpu")]
public class HiringGoldenTests
{
    private readonly GoldenHost _host;
    public HiringGoldenTests(GoldenHost host) => _host = host;

    [Fact]
    public void The_brain_dialogue_matches_its_golden()
    {
        var sim = new Simulation(WorldLayout.Generate(Array.Empty<string>()), seed: 7);
        var you = new Player(sim.World);
        you.Teleport(Agent.Centre(sim.World.HiringSpot));
        var d = DialogueScript.Brains(HiringDialogueTests.Hiring().Templates[0], new HashSet<Vendor> { Vendor.Claude });
        d.CompleteReveal();
        var ui = new UiState();
        ui.Push(d);
        Golden.Check(_host, "ui-hiring-brains", _host.RenderUi(sim, ui, new Toasts(), you), tolerance: 0.005);
    }
}

[Collection("gpu")]
public class TicketGoldenTests
{
    private readonly GoldenHost _host;
    public TicketGoldenTests(GoldenHost host) => _host = host;

    [Fact]
    public void The_ticket_board_dialogue_matches_its_golden()
    {
        var sim = new Simulation(WorldLayout.Generate(Array.Empty<string>()), seed: 7);
        sim.Apply(new TicketsChanged(2));
        var you = new Player(sim.World);
        you.Teleport(Agent.Centre(sim.World.TicketSpot));
        var now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var d = DialogueScript.Tickets(new[] { TicketDialogueTests.Ticket("t1", "Fix the parser", "Software engineer", 3), TicketDialogueTests.Ticket("t2", "Write the docs", null, 125) }, now);
        d.CompleteReveal();
        var ui = new UiState();
        ui.Push(d);
        Golden.Check(_host, "ui-tickets", _host.RenderUi(sim, ui, new Toasts(), you), tolerance: 0.005);
    }
}
