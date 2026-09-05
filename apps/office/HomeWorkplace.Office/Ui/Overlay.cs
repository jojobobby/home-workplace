using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Ui;

public enum OverlayTab { Employees, Tasks, Goals, Activity, Setup }

/// <summary>One line in the overlay and what can be done to it.</summary>
public sealed record OverlayRow(string Id, string Text, IReadOnlyList<DialogueOption> Actions);

/// <summary>Everything the overlay lists, captured from the store at one moment.</summary>
public sealed record OverlaySnapshot(
    IReadOnlyDictionary<string, EmployeeDto> Employees,
    IReadOnlyDictionary<string, TaskDto> Tasks,
    IReadOnlyDictionary<string, GoalDto> Goals,
    IReadOnlyList<EventDto> Events,
    IReadOnlyList<CliStatus> Setup);

/// <summary>
/// The Tab overlay: five tabs of rows. Tab/Left/Right switch tabs, Up/Down (PageUp/Down)
/// move, Enter opens the selected row's actions as a small dialogue, Esc closes.
/// </summary>
public sealed class Overlay : ILayer
{
    public const int ActivityRows = 40;
    private static readonly OverlayTab[] TabOrder = Enum.GetValues<OverlayTab>();

    private OverlaySnapshot _snapshot;
    private List<OverlayRow> _rows = new();

    public Overlay(OverlayTab tab, OverlaySnapshot snapshot)
    {
        Tab = tab;
        _snapshot = snapshot;
        Rebuild(keepId: null);
    }

    public OverlayTab Tab { get; private set; }
    public IReadOnlyList<OverlayRow> Rows => _rows;
    public int Selected { get; private set; }
    public OverlayRow? SelectedRow => _rows.Count == 0 ? null : _rows[Selected];

    public void Refresh(OverlaySnapshot snapshot)
    {
        _snapshot = snapshot;
        Rebuild(keepId: SelectedRow?.Id);
    }

    public void ShowTab(OverlayTab tab)
    {
        Tab = tab;
        Rebuild(keepId: null);
    }

    public void Select(int index) => Selected = _rows.Count == 0 ? 0 : Math.Clamp(index, 0, _rows.Count - 1);

    public LayerResult Handle(UiKey key)
    {
        switch (key.Kind)
        {
            case UiKeyKind.Tab: case UiKeyKind.Right: ShowTab(TabOrder[(Array.IndexOf(TabOrder, Tab) + 1) % TabOrder.Length]); break;
            case UiKeyKind.Left: ShowTab(TabOrder[(Array.IndexOf(TabOrder, Tab) + TabOrder.Length - 1) % TabOrder.Length]); break;
            case UiKeyKind.Up: Select(Selected - 1); break;
            case UiKeyKind.Down: Select(Selected + 1); break;
            case UiKeyKind.PageUp: Select(Selected - 10); break;
            case UiKeyKind.PageDown: Select(Selected + 10); break;
            case UiKeyKind.Accept:
                if (SelectedRow is { } row && row.Actions.Count > 0)
                {
                    var menu = new Dialogue(null, row.Text, new[] { row.Text }, row.Actions.Append(new DialogueOption("Leave", new Leave())).ToList());
                    menu.CompleteReveal();   // a menu, not a speech: no typewriter
                    return LayerResult.Push(menu);
                }
                break;
            case UiKeyKind.Back:
                return LayerResult.Pop();
        }
        return LayerResult.None();
    }

    private void Rebuild(string? keepId)
    {
        _rows = Tab switch
        {
            OverlayTab.Employees => EmployeeRows().ToList(),
            OverlayTab.Tasks => TaskRows().ToList(),
            OverlayTab.Goals => GoalRows().ToList(),
            OverlayTab.Activity => ActivityRowsOf().ToList(),
            _ => SetupRows().ToList(),
        };
        var index = keepId is null ? -1 : _rows.FindIndex(r => r.Id == keepId);
        Selected = index >= 0 ? index : Math.Clamp(Selected, 0, Math.Max(0, _rows.Count - 1));
    }

    private IEnumerable<OverlayRow> EmployeeRows()
    {
        foreach (var e in _snapshot.Employees.Values.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            var title = e.CurrentTaskId is { } id && _snapshot.Tasks.TryGetValue(id, out var t) ? t.Title : "-";
            var actions = new List<DialogueOption>
            {
                new($"Talk to {e.Name}", new TalkTo(e.Id)),
                new("Give a task", new GiveTask(e.Id)),
                e.Status == EmployeeStatus.Asleep ? new("Wake", new Wake(e.Id)) : new("Sleep", new Sleep(e.Id)),
                new("Reset", new Reset(e.Id)),
            };
            yield return new OverlayRow(e.Id, $"{e.Name}  {e.Status}  {title}", actions);
        }
    }

    private IEnumerable<OverlayRow> TaskRows()
    {
        var others = _snapshot.Employees.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        foreach (var t in _snapshot.Tasks.Values.OrderByDescending(t => t.UpdatedAt).ThenBy(t => t.Id, StringComparer.Ordinal))
        {
            var actions = new List<DialogueOption>();
            switch (t.Status)
            {
                case TaskState.NeedsHuman:
                    if (t.AwaitingApproval) actions.Add(new("Approve", new Approve(t.Id)));
                    if (t.PendingQuestion is not null) actions.Add(new("Answer", new Answer(t.Id)));
                    actions.Add(new("Cancel task", new CancelTask(t.Id)));
                    break;
                case TaskState.Failed:
                    actions.Add(new("Retry", new Retry(t.Id)));
                    actions.AddRange(others.Where(o => o != t.Assignee).Select(o => new DialogueOption($"Reassign to {o}", new Reassign(t.Id, o))));
                    break;
                case TaskState.Queued: case TaskState.Running: case TaskState.Waiting:
                    actions.AddRange(others.Where(o => o != t.Assignee).Select(o => new DialogueOption($"Reassign to {o}", new Reassign(t.Id, o))));
                    actions.Add(new("Cancel task", new CancelTask(t.Id)));
                    break;
            }
            yield return new OverlayRow(t.Id, $"{t.Title}  {t.Status}  {t.Assignee}", actions);
        }
    }

    private IEnumerable<OverlayRow> GoalRows()
    {
        foreach (var g in _snapshot.Goals.Values.OrderByDescending(DialogueScript.IsActive).ThenByDescending(g => g.CreatedAt))
        {
            var actions = DialogueScript.IsActive(g)
                ? new List<DialogueOption> { new("Top up", new TopUp(g.Id)), new("Cancel goal", new CancelGoal(g.Id)) }
                : new List<DialogueOption>();
            yield return new OverlayRow(g.Id, $"{g.Title}  {g.Status}  ${g.SpentUsd:0.00}/${g.BudgetUsd:0.00}  {g.Manager}", actions);
        }
    }

    private IEnumerable<OverlayRow> ActivityRowsOf()
    {
        foreach (var e in _snapshot.Events.OrderByDescending(e => e.Seq).Take(ActivityRows))
            yield return new OverlayRow(e.Seq.ToString(), $"{e.Timestamp.ToLocalTime():HH:mm}  {e.Type}  {e.EmployeeId ?? ""}".TrimEnd(), Array.Empty<DialogueOption>());
    }

    private IEnumerable<OverlayRow> SetupRows()
    {
        foreach (var s in _snapshot.Setup)
            yield return new OverlayRow("cli:" + s.Cli, $"{s.Cli}  {s.State}  {s.Version ?? s.Detail ?? ""}".TrimEnd(), Array.Empty<DialogueOption>());
        yield return new OverlayRow("reload", "Reload employees", new[] { new DialogueOption("Reload employees", new ReloadEmployees()) });
    }
}
