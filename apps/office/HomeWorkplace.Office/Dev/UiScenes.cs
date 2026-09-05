using System.Numerics;
using HomeWorkplace.Client;
using HomeWorkplace.Office.Sim;
using HomeWorkplace.Office.Ui;

namespace HomeWorkplace.Office.Dev;

/// <summary>
/// Canned UI scenes for screenshots and goldens: a seeded office plus the layers to show,
/// built from fixed data so no service has to run. `--ui-shot &lt;scene&gt; &lt;png&gt;` renders one.
/// </summary>
public static class UiScenes
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "office", "dialogue", "overlay-employees", "overlay-tasks", "textentry", "confirm", "hiring", "hiring-brains", "tickets", "desk",
    };

    public sealed record Scene(Simulation Sim, Player You, UiState Ui, Toasts Toasts);

    public static Scene Build(string name)
    {
        var sim = SeededOffice();
        var you = new Player(sim.World);
        var ui = new UiState();
        var toasts = new Toasts();
        var employees = Employees();
        var tasks = Tasks();
        var goals = new Dictionary<string, GoalDto>();
        var signedIn = new HashSet<Vendor> { Vendor.Claude };

        switch (name)
        {
            case "office":
                you.Teleport(Agent.Centre(sim.World.Spawn) + new Vector2(0, -48));
                toasts.Add("Rex needs you", ToastKind.Attention, "rex-reviewer");
                break;
            case "dialogue":
                you.Teleport(sim.Agents["rex-reviewer"].Position + new Vector2(Agent.TileSize, 0));
                Push(ui, DialogueScript.For(employees["rex-reviewer"], tasks, goals));
                break;
            case "overlay-employees":
            case "overlay-tasks":
                var tab = name.EndsWith("tasks") ? OverlayTab.Tasks : OverlayTab.Employees;
                var overlay = new Overlay(tab, new OverlaySnapshot(employees, tasks, goals, Events(), Setup()));
                overlay.Handle(UiKey.Down);
                ui.Push(overlay);
                break;
            case "textentry":
                var entry = new TextEntry("New task for Ada", new[] { new Field("Title", false, 60), new Field("Brief", true, 600) }, payload: null);
                foreach (var c in "Fix the parser") entry.Handle(UiKey.Char(c));
                entry.Handle(UiKey.Accept);
                foreach (var c in "It crashes on empty input. Add a test and fix the loop.") entry.Handle(UiKey.Char(c));
                ui.Push(entry);
                break;
            case "confirm":
                ui.Push(new Confirm("Cancel \"Review the parser PR\"? Its runs stop.", payload: null));
                toasts.Add("Rex needs you", ToastKind.Attention, "rex-reviewer");
                toasts.Add("Task \"Fix the parser\" given to Ada", ToastKind.Success, null);
                toasts.Add("Foreman is unreachable", ToastKind.Error, null);
                break;
            case "hiring":
                you.Teleport(Agent.Centre(sim.World.HiringSpot));
                Push(ui, DialogueScript.Hiring(Hiring(), signedIn));
                break;
            case "hiring-brains":
                you.Teleport(Agent.Centre(sim.World.HiringSpot));
                Push(ui, DialogueScript.Brains(Hiring().Templates[0], signedIn));
                break;
            case "tickets":
                you.Teleport(Agent.Centre(sim.World.TicketSpot));
                sim.Apply(new TicketsChanged(2));
                Push(ui, DialogueScript.Tickets(new[] { Ticket("t7", "Fix the parser", "Software engineer", 3), Ticket("t8", "Write the docs", null, 125) }, Now));
                break;
            case "desk":
                you.Teleport(Agent.Centre(sim.World.BossSpot));
                Push(ui, DialogueScript.BossDesk(OfficePaths.For("Main Office", @"C:\Users\you\Documents")));
                break;
            default:
                throw new ArgumentException($"unknown scene '{name}'; scenes: {string.Join(", ", Names)}", nameof(name));
        }
        return new Scene(sim, you, ui, toasts);
    }

    private static void Push(UiState ui, Dialogue d)
    {
        d.CompleteReveal();
        ui.Push(d);
    }

    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");

    /// <summary>Four employees seated after forty seconds, Rex waiting on a human. Same seed every time.</summary>
    public static Simulation SeededOffice()
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

    public static Dictionary<string, EmployeeDto> Employees() => new()
    {
        ["ada-coder"] = new() { Id = "ada-coder", Name = "Ada", Role = "Software engineer", Status = EmployeeStatus.Working, CurrentTaskId = "t1", Energy = 90, RunsToday = 2 },
        ["mia-manager"] = new() { Id = "mia-manager", Name = "Mia", Role = "Engineering manager", Status = EmployeeStatus.Awake, Energy = 100 },
        ["rex-reviewer"] = new() { Id = "rex-reviewer", Name = "Rex", Role = "Code reviewer", Status = EmployeeStatus.Waiting, CurrentTaskId = "t2", Energy = 70, RunsToday = 3 },
        ["vfx-artist"] = new() { Id = "vfx-artist", Name = "Vex", Role = "Pixel/VFX artist", Status = EmployeeStatus.Asleep, Energy = 100 },
    };

    public static Dictionary<string, TaskDto> Tasks() => new()
    {
        ["t1"] = new() { Id = "t1", Title = "Build the parser", Assignee = "ada-coder", Status = TaskState.Running, UpdatedAt = Now.AddHours(-2) },
        ["t2"] = new() { Id = "t2", Title = "Review the parser PR", Assignee = "rex-reviewer", Status = TaskState.NeedsHuman, AwaitingApproval = true, PendingQuestion = "Merge to main now?", UpdatedAt = Now.AddHours(-1) },
        ["t3"] = new() { Id = "t3", Title = "Sprite sheet v1", Assignee = "vfx-artist", Status = TaskState.Failed, UpdatedAt = Now.AddHours(-3) },
        ["t7"] = Ticket("t7", "Fix the parser", "Software engineer", 3),
    };

    public static TaskDto Ticket(string id, string title, string? role, int minutesAgo)
        => new() { Id = id, Title = title, Assignee = "", Role = role, Status = TaskState.Queued, CreatedAt = Now.AddMinutes(-minutesAgo) };

    public static HiringDto Hiring() => new(
        new[]
        {
            new HiringTemplateDto("engineer", "Software engineer", "Builds features test-first.", new[]
            {
                new BrainCostDto("claude-haiku-4-5-20251001", Vendor.Claude, "Claude Haiku 4.5", 0.10m, 0.60m),
                new BrainCostDto("claude-sonnet-5", Vendor.Claude, "Claude Sonnet 5", 0.30m, 1.80m),
                new BrainCostDto("claude-opus-5", Vendor.Claude, "Claude Opus 5", 0.50m, 3.00m),
                new BrainCostDto("claude-fable-5-1", Vendor.Claude, "Claude Fable 5.1", 1.50m, 9.00m),
                new BrainCostDto("gpt-5-codex", Vendor.Codex, "GPT-5 Codex", 0.16m, 0.93m),
            }),
            new HiringTemplateDto("manager", "Engineering manager", "Runs goals on a budget.", new[]
            {
                new BrainCostDto("claude-opus-5", Vendor.Claude, "Claude Opus 5", 0.18m, 1.40m),
            }),
        },
        Array.Empty<BrainDto>());

    public static IReadOnlyList<EventDto> Events() => new[]
    {
        new EventDto { Seq = 1, Timestamp = Now.AddMinutes(-30), Type = "run.started", EmployeeId = "ada-coder" },
        new EventDto { Seq = 2, Timestamp = Now.AddMinutes(-5), Type = "human.needed", EmployeeId = "rex-reviewer" },
    };

    public static IReadOnlyList<CliStatus> Setup() => new[]
    {
        new CliStatus("claude", CliState.SignedIn, "2.1.241 (Claude Code)", null),
        new CliStatus("codex", CliState.InstalledNotSignedIn, "codex-cli 0.139.0", null),
    };
}
