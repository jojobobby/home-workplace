using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Ui;

/// <summary>Builds what an employee (or the whiteboard) says and offers, from Foreman's state.</summary>
public static class DialogueScript
{
    public static bool IsManager(EmployeeDto e) => e.Role.Contains("manager", StringComparison.OrdinalIgnoreCase);

    public static Dialogue For(EmployeeDto e, IReadOnlyDictionary<string, TaskDto> tasks, IReadOnlyDictionary<string, GoalDto> goals)
    {
        var current = e.CurrentTaskId is { } id && tasks.TryGetValue(id, out var t) ? t : null;
        var mine = tasks.Values.Where(x => x.Assignee == e.Id).OrderByDescending(x => x.UpdatedAt).ToList();
        var needsHuman = current is { Status: TaskState.NeedsHuman } ? current : mine.FirstOrDefault(x => x.Status == TaskState.NeedsHuman);
        var failed = mine.FirstOrDefault(x => x.Status == TaskState.Failed);

        var lines = new List<string>();
        lines.Add(e.Status switch
        {
            EmployeeStatus.Asleep => $"Zzz... {e.Name} is asleep. Wake them to bring them in.",
            EmployeeStatus.Working when current is not null => $"Hi, I'm {e.Name}. I'm working on \"{current.Title}\".",
            EmployeeStatus.Waiting when current is not null => $"Hi, I'm {e.Name}. I'm waiting on a teammate for \"{current.Title}\".",
            _ => $"Hi, I'm {e.Name}, {e.Role.ToLowerInvariant()}. I'm free. Got something for me?",
        });
        lines.Add($"Energy {e.Energy}%, {e.RunsToday} run{(e.RunsToday == 1 ? "" : "s")} today.");
        if (needsHuman is not null)
        {
            if (needsHuman.AwaitingApproval) lines.Add($"\"{needsHuman.Title}\" is done and needs your approval.");
            if (needsHuman.PendingQuestion is { } q) lines.Add($"I need you: {q}");
        }
        if (failed is not null && needsHuman is null)
        {
            var reason = failed.Runs.LastOrDefault(r => r.Status == "Failed")?.ResultSummary;
            lines.Add(reason is { Length: > 0 } && reason != "run failed"
                ? $"My last task, \"{failed.Title}\", failed: {reason}"
                : $"My last task, \"{failed.Title}\", failed.");
        }

        var options = new List<DialogueOption>();
        if (needsHuman is not null)
        {
            if (needsHuman.AwaitingApproval) options.Add(new("Approve", new Approve(needsHuman.Id)));
            if (needsHuman.PendingQuestion is not null) options.Add(new("Answer", new Answer(needsHuman.Id)));
            options.Add(new("Cancel task", new CancelTask(needsHuman.Id)));
        }
        options.Add(new("Give a task", new GiveTask(e.Id)));
        options.Add(e.Status == EmployeeStatus.Asleep ? new("Wake", new Wake(e.Id)) : new("Sleep", new Sleep(e.Id)));
        options.Add(new("Open room brief", new OpenBrief(e.Id)));
        if (IsManager(e))
        {
            options.Add(new("Set a goal", new SetGoal(e.Id)));
            foreach (var g in goals.Values.Where(g => g.Manager == e.Id && IsActive(g)).OrderBy(g => g.CreatedAt))
                options.Add(new($"Top up: {g.Title}", new TopUp(g.Id)));
        }
        if (failed is not null) options.Add(new("Retry last task", new Retry(failed.Id)));
        options.Add(new("Let go", new Fire(e.Id)));
        options.Add(new("Reset", new Reset(e.Id)));
        options.Add(new("Leave", new Leave()));

        return new Dialogue(e.Id, e.Name, lines, options);
    }

    public static Dialogue Whiteboard(IReadOnlyDictionary<string, GoalDto> goals, IEnumerable<EmployeeDto> employees)
    {
        var ordered = goals.Values.OrderByDescending(IsActive).ThenByDescending(g => g.CreatedAt).ToList();
        var staff = employees.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var manager = staff.Values.Where(IsManager).OrderBy(e => e.Id, StringComparer.Ordinal).FirstOrDefault();

        var lines = new List<string>();
        if (ordered.Count == 0) lines.Add("The board is empty. Set a goal for a manager to run.");
        else
        {
            lines.Add("Goals on the board:");
            foreach (var g in ordered.Take(4))
            {
                lines.Add($"- {g.Title}: {g.Status}, ${g.SpentUsd:0.00} of ${g.BudgetUsd:0.00}");
                if (g.LastError is { Length: > 0 } err && IsActive(g))
                    lines.Add($"  manager run failed: {(err.Length > 70 ? err[..70] + "..." : err)}");
            }
        }
        if (manager is null && ordered.Count == 0) lines.Add("Hire a manager to set goals.");

        var sleeping = ordered.Where(IsActive)
            .Select(g => staff.TryGetValue(g.Manager, out var m) && m.Status == EmployeeStatus.Asleep ? m : null)
            .Where(m => m is not null).Select(m => m!).DistinctBy(m => m.Id).ToList();
        foreach (var m in sleeping)
            lines.Add($"{m.Name} is asleep; their goals wait until they wake. Wake them to start now.");

        var options = new List<DialogueOption>();
        foreach (var m in sleeping) options.Add(new($"Wake {m.Name}", new Wake(m.Id)));
        foreach (var g in ordered.Where(IsActive))
        {
            options.Add(new($"Top up: {g.Title}", new TopUp(g.Id)));
            options.Add(new($"Cancel: {g.Title}", new CancelGoal(g.Id)));
        }
        if (manager is not null) options.Add(new("Set a goal", new SetGoal(manager.Id)));
        options.Add(new("Leave", new Leave()));

        return new Dialogue(null, "Whiteboard", lines, options);
    }

    public static bool IsActive(GoalDto g) => g.Status is GoalState.Planning or GoalState.Running or GoalState.Blocked;

    // ---- the ticket board ----

    /// <summary>What is pinned: each ticket with the role it is for and how long it has waited.</summary>
    public static Dialogue Tickets(IReadOnlyList<TaskDto> tickets, DateTimeOffset now)
    {
        var lines = new List<string>();
        if (tickets.Count == 0) lines.Add("The board is empty. Post a ticket and an idle employee of that role takes it.");
        else
        {
            lines.Add($"{tickets.Count} ticket{(tickets.Count == 1 ? "" : "s")} pinned. Idle employees of the right role take them.");
            foreach (var t in tickets.Take(3))
                lines.Add($"- {t.Title} ({t.Role ?? "any role"}, {Age(now - t.CreatedAt)})");
        }

        var options = new List<DialogueOption> { new("Post a ticket", new PickTicketRole()) };
        foreach (var t in tickets) options.Add(new($"Take down: {t.Title}", new CancelTask(t.Id)));
        options.Add(new("Leave", new Leave()));
        return new Dialogue(null, "Ticket board", lines, options) { Portrait = "tickets" };
    }

    /// <summary>Who should take the ticket: anyone, or one of the roles the company has.</summary>
    public static Dialogue TicketRoles(IEnumerable<EmployeeDto> employees)
    {
        var options = new List<DialogueOption> { new("Any role", new PostTicket(null)) };
        foreach (var role in employees.Select(e => e.Role).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            options.Add(new(role, new PostTicket(role)));
        options.Add(new("Leave", new Leave()));
        return new Dialogue(null, "Ticket board", new[] { "Who should take it?" }, options) { Portrait = "tickets" };
    }

    private static string Age(TimeSpan age)
        => age < TimeSpan.FromMinutes(1) ? "just now"
         : age < TimeSpan.FromHours(1) ? $"{(int)age.TotalMinutes} min"
         : age < TimeSpan.FromDays(1) ? $"{(int)age.TotalHours} h"
         : $"{(int)age.TotalDays} d";

    // ---- the hiring stand ----

    /// <summary>Step one at the stand: which role. A role whose brains all need a sign-in is shown but disabled.</summary>
    public static Dialogue Hiring(HiringDto hiring, IReadOnlySet<Vendor> signedIn)
    {
        var lines = new List<string>
        {
            "Who do you need? Prices are about what a day costs at API list",
            "prices; on a subscription they are notional. Pick a role, a brain, a name.",
        };
        if (hiring.Templates.Count == 0) lines.Add("No role templates found under hiring/.");

        var options = new List<DialogueOption>();
        foreach (var t in hiring.Templates)
        {
            var available = t.Brains.Where(b => signedIn.Contains(b.Vendor)).ToList();
            options.Add(available.Count > 0
                ? new($"{t.Role}  ${available.Min(b => b.UsdPerDay):0.00}+/day", new HireRole(t.Id))
                : new($"{t.Role} (sign in)", new HireRole(t.Id), Enabled: false));
        }
        options.Add(new("Leave", new Leave()));
        return new Dialogue(null, "Hiring stand", lines, options) { Portrait = "hiring" };
    }

    /// <summary>Step two: which brain, priced per run and per day; a brain whose CLI is not signed in cannot be picked.</summary>
    public static Dialogue Brains(HiringTemplateDto t, IReadOnlySet<Vendor> signedIn)
    {
        var lines = new List<string>
        {
            $"{t.Role}: {t.Description}",
            "Pick a brain. Prices are about one working day at API list prices.",
        };
        var options = new List<DialogueOption>();
        foreach (var b in t.Brains)
        {
            options.Add(signedIn.Contains(b.Vendor)
                ? new($"{b.Label}  ${b.UsdPerDay:0.00}/day", new HireBrain(t.Id, b.Model, b.Label))
                : new($"{b.Label} (sign in)", new HireBrain(t.Id, b.Model, b.Label), Enabled: false));
        }
        options.Add(new("Back", new OpenHiring()));
        options.Add(new("Leave", new Leave()));
        return new Dialogue(null, "Hiring stand", lines, options) { Portrait = "hiring" };
    }
}
