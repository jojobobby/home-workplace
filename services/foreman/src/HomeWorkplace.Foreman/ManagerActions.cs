using System.Text.Json;

namespace HomeWorkplace.Foreman;

/// <summary>
/// Parses a manager's structured decision and executes its actions against the books. The
/// manager (a model) decides; this class only carries out what it said, within the caps.
/// </summary>
public static class ManagerActions
{
    public const string Schema =
        """{"type":"object","properties":{"summary":{"type":"string"},"actions":{"type":"array","items":{"type":"object","properties":{"kind":{"type":"string","enum":["create_task","message","wait","complete","fail"]},"assignee":{"type":"string"},"title":{"type":"string"},"brief":{"type":"string"},"to":{"type":"string"},"text":{"type":"string"},"reason":{"type":"string"}},"required":["kind"]}}},"required":["summary","actions"]}""";

    public static ManagerDecision Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
            var actions = new List<ManagerAction>();
            if (root.TryGetProperty("actions", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    if (!a.TryGetProperty("kind", out var k) || k.GetString() is not { Length: > 0 } kind) continue;
                    actions.Add(new ManagerAction(kind,
                        Str(a, "assignee"), Str(a, "title"), Str(a, "brief"), Str(a, "to"), Str(a, "text"), Str(a, "reason")));
                }
            }
            return new ManagerDecision(summary, actions);
        }
        catch (Exception ex)
        {
            var tail = json.Length <= 500 ? json : json[^500..];
            return new ManagerDecision($"unparseable manager output ({ex.Message}): {tail}", new[] { new ManagerAction("wait") });
        }
    }

    /// <summary>Apply a decision. Unknown assignees are skipped and noted for the next prompt; extra actions past the cap are ignored.</summary>
    public static async Task ExecuteAsync(
        GoalModel goal, ManagerDecision decision, TaskBook tasks, GoalBook goals, EmployeeCatalog employees,
        IContextApiClient rooms, ForemanOptions options, RunSupervisor supervisor, DateTimeOffset now, CancellationToken ct)
    {
        goal.LastDecision = new Decision(now, decision.Summary);
        var managerName = employees.Find(goal.Manager)?.Name ?? goal.Manager;
        var terminal = false;

        foreach (var a in decision.Actions.Take(options.MaxActionsPerRun))
        {
            switch (a.Kind.Trim().ToLowerInvariant())
            {
                case "create_task":
                    if (string.IsNullOrWhiteSpace(a.Assignee) || employees.Find(a.Assignee) is null)
                    {
                        goal.PendingNotes.Add($"Skipped create_task: no employee '{a.Assignee}' on the team.");
                        break;
                    }
                    var t = await tasks.CreateAsync(
                        new CreateTaskRequest(a.Title ?? "Untitled", a.Brief ?? a.Title ?? "", a.Assignee), ct, goalId: goal.Id);
                    goal.TaskIds.Add(t.Id);
                    break;

                case "message":
                    var target = a.To is { } to && employees.GetState(to).CurrentTaskId is { } tid && tasks.Get(tid) is { } tt
                        ? tt.Room : goal.Room;
                    await rooms.PostAsync(target, goal.Manager, managerName, null, a.Text ?? "", ct);
                    break;

                case "complete":
                    goal.Status = GoalState.Done;
                    terminal = true;
                    await rooms.PostAsync(goal.Room, goal.Manager, managerName, null, $"Goal complete: {decision.Summary}", ct);
                    break;

                case "fail":
                    goal.Status = GoalState.Failed;
                    terminal = true;
                    await rooms.PostAsync(goal.Room, goal.Manager, managerName, null, $"Goal failed: {a.Reason ?? decision.Summary}", ct);
                    break;

                default: // "wait" and anything unknown
                    break;
            }
            if (terminal) break;
        }

        if (!terminal && goal.Status == GoalState.Planning) goal.Status = GoalState.Running;
        goals.Save(goal);
        supervisor.Pump();
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
