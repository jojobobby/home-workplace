using HomeWorkplace.Client;

namespace HomeWorkplace.UI;

public enum Screen { Office, Employees, Tasks, Goals, Activity, Setup }

/// <summary>Where the user is in the app: the current screen, selections, and list filters. No router; the shell switches on this.</summary>
public sealed class ShellState
{
    public Screen Current { get; private set; } = Screen.Office;
    public string? SelectedEmployeeId { get; private set; }
    public string? SelectedTaskId { get; private set; }
    public string? SelectedGoalId { get; private set; }
    public TaskState? TaskFilter { get; private set; }
    public GoalState? GoalFilter { get; private set; }

    public event Action? Changed;

    public void Go(Screen screen) { Current = screen; Changed?.Invoke(); }
    public void SelectEmployee(string? id) { SelectedEmployeeId = id; Changed?.Invoke(); }
    public void SelectTask(string? id) { SelectedTaskId = id; Changed?.Invoke(); }
    public void SelectGoal(string? id) { SelectedGoalId = id; Changed?.Invoke(); }
    public void SetTaskFilter(TaskState? filter) { TaskFilter = filter; Changed?.Invoke(); }
    public void SetGoalFilter(GoalState? filter) { GoalFilter = filter; Changed?.Invoke(); }

    /// <summary>The badge click: tasks parked on a person and goals blocked on budget.</summary>
    public void ShowWhatNeedsMe()
    {
        TaskFilter = TaskState.NeedsHuman;
        GoalFilter = GoalState.Blocked;
        Current = Screen.Tasks;
        Changed?.Invoke();
    }
}
