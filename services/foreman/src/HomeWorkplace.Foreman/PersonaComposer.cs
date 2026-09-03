using System.Text;

namespace HomeWorkplace.Foreman;

/// <summary>Builds the system prompt (identity + skills + life + house rules) and the run
/// prompt (task + progress bullets + current room context, or just the returned answer).</summary>
public sealed class PersonaComposer
{
    private readonly IContextApiClient _rooms;
    private readonly ForemanOptions _options;

    public PersonaComposer(IContextApiClient rooms, ForemanOptions options)
    {
        _rooms = rooms;
        _options = options;
    }

    public string BuildSystemPrompt(EmployeeDefinition e, TaskModel t)
    {
        var b = new StringBuilder();
        b.Append("You are ").Append(e.Name).Append(", ").Append(e.Role).AppendLine(".");
        b.AppendLine().AppendLine("## Your skills").AppendLine(e.SkillsMd);
        b.AppendLine().AppendLine("## Your life").AppendLine(e.LifeMd);
        b.AppendLine().AppendLine("## House rules");
        b.Append("- Your team room is '").Append(t.Room).Append("' on ").Append(_options.ContextApiBaseUrl).AppendLine(".");
        b.AppendLine("- Read it before you act; post progress after each meaningful step, with your id and name.");
        b.AppendLine("- Share files through the room folder, not by pasting them into chat.");
        b.AppendLine("- Your FINAL message must be the JSON result object you were asked for — nothing after it.");
        return b.ToString();
    }

    public async Task<string> BuildRunPromptAsync(EmployeeDefinition e, TaskModel t, CancellationToken ct)
    {
        if (t.PendingAnswer is { } ans)
            return $"Answer from {ans.From}: {ans.Text}\n\nContinue the task.";

        var b = new StringBuilder();
        b.Append("# Task: ").AppendLine(t.Title);
        b.AppendLine(t.Brief).AppendLine();
        foreach (var p in t.Progress)
        {
            b.Append("Done on ").Append(p.Date).Append(" by ").Append(p.Author).AppendLine(":");
            foreach (var d in p.Done) b.Append("  - ").AppendLine(d);
            if (p.Next.Count > 0) { b.AppendLine("Next:"); foreach (var n in p.Next) b.Append("  - ").AppendLine(n); }
        }
        b.AppendLine().AppendLine("## Current room context");
        b.AppendLine(await _rooms.GetBriefAsync(t.Room, ct));
        return b.ToString();
    }

    public string BuildWrapUpPrompt(TaskModel t)
        => $"Your day is ending. For the task \"{t.Title}\", list what you completed today as short bullets, then what should happen next. Respond only as the requested JSON object.";
}
