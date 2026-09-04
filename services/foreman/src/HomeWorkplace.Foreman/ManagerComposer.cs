using System.Text;

namespace HomeWorkplace.Foreman;

/// <summary>Builds the manager's system prompt and per-run prompt: goal, roster, children, budget.</summary>
public sealed class ManagerComposer
{
    private readonly ForemanOptions _options;

    public ManagerComposer(ForemanOptions options) => _options = options;

    public string BuildSystemPrompt(EmployeeDefinition manager, GoalModel goal)
    {
        var b = new StringBuilder();
        b.Append("You are ").Append(manager.Name).Append(", ").Append(manager.Role).AppendLine(", managing a team of AI employees.");
        b.AppendLine().AppendLine("## Your skills").AppendLine(manager.SkillsMd);
        b.AppendLine().AppendLine("## Your life").AppendLine(manager.LifeMd);
        b.AppendLine().AppendLine("## How managing works here");
        b.Append("- You own the goal '").Append(goal.Title).Append("'. Its room is '").Append(goal.Room).Append("' on ").Append(_options.ContextApiBaseUrl).AppendLine(".");
        b.AppendLine("- You do not do the work yourself. You decompose the goal into tasks, assign each to the right teammate, verify what comes back, and re-plan when something fails.");
        b.AppendLine("- Every run — yours and your team's — spends the goal's dollar budget. Spend it deliberately; prefer fewer, well-briefed tasks.");
        b.AppendLine("- You are re-run each time a task you created finishes, so decide only what is needed now, then 'wait'.");
        b.AppendLine("- Your FINAL message must be the JSON decision object: {\"summary\": string, \"actions\": [{\"kind\": create_task|message|wait|complete|fail, ...}]}. Nothing after it.");
        return b.ToString();
    }

    public string BuildRunPrompt(GoalModel goal, IReadOnlyList<EmployeeView> roster, IReadOnlyList<TaskModel> children)
    {
        var b = new StringBuilder();
        b.Append("# Goal: ").AppendLine(goal.Title);
        b.AppendLine(goal.Brief).AppendLine();

        var remaining = goal.BudgetUsd - goal.SpentUsd;
        b.Append("## Budget: $").Append(goal.SpentUsd.ToString("0.00")).Append(" / $").Append(goal.BudgetUsd.ToString("0.00"))
         .Append("  (remaining $").Append(remaining.ToString("0.00")).AppendLine(")").AppendLine();

        b.AppendLine("## Team");
        foreach (var e in roster.Where(e => e.Id != goal.Manager))
            b.Append("- ").Append(e.Id).Append(" — ").Append(e.Name).Append(", ").Append(e.Role)
             .Append(" (").Append(e.Vendor.ToString().ToLowerInvariant()).Append(", ").Append(e.Status.ToString().ToLowerInvariant()).AppendLine(")");
        if (!roster.Any(e => e.Id != goal.Manager)) b.AppendLine("- (no other employees)");
        b.AppendLine();

        b.AppendLine("## Tasks so far");
        if (children.Count == 0) b.AppendLine("- none yet");
        foreach (var t in children)
        {
            var last = t.Runs.Count > 0 ? t.Runs[^1].ResultSummary : null;
            b.Append("- [").Append(t.Id).Append("] ").Append(t.Title).Append(" → ").Append(t.Assignee)
             .Append(" — ").Append(t.Status.ToString().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(last)) b.Append(" — last: ").Append(last);
            b.AppendLine();
        }
        b.AppendLine();

        if (goal.PendingNotes.Count > 0)
        {
            b.AppendLine("## Notes from Foreman");
            foreach (var n in goal.PendingNotes) b.Append("- ").AppendLine(n);
            b.AppendLine();
        }

        if (goal.LastDecision is { } d)
            b.Append("## Your last decision: ").AppendLine(d.Summary).AppendLine();

        b.AppendLine("Decide what happens next. Respond only with the JSON decision object.");
        return b.ToString();
    }
}
