using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Ui;

/// <summary>
/// The markup colour each state reads in, so a status means the same thing everywhere:
/// blue is busy, gold wants you, green is fine or done, red is trouble, dim is nothing to see.
/// </summary>
public static class StatusColor
{
    public static string Of(TaskState s) => s switch
    {
        TaskState.Running or TaskState.Waiting => "blue",
        TaskState.NeedsHuman => "gold",
        TaskState.Done => "green",
        TaskState.Failed => "red",
        _ => "dim",   // Queued, Cancelled
    };

    public static string Of(GoalState s) => s switch
    {
        GoalState.Planning or GoalState.Running => "blue",
        GoalState.Blocked => "gold",
        GoalState.Done => "green",
        GoalState.Failed => "red",
        _ => "dim",   // Cancelled
    };

    public static string Of(EmployeeStatus s) => s switch
    {
        EmployeeStatus.Working => "blue",
        EmployeeStatus.Waiting => "gold",
        EmployeeStatus.Awake => "green",
        _ => "dim",   // Asleep
    };

    public static string Of(CliState s) => s switch
    {
        CliState.SignedIn => "green",
        CliState.InstalledNotSignedIn => "gold",
        _ => "red",
    };

    /// <summary>Energy at a glance: green while fresh, gold when flagging, red when nearly spent.</summary>
    public static string Energy(int energy) => energy >= 50 ? "green" : energy >= 25 ? "gold" : "red";
}
