namespace HomeWorkplace.Office.Ui;

/// <summary>Something the user chose to do. Dialogue options carry one; the Actions dispatcher runs it.</summary>
public abstract record UiAction;

public sealed record GiveTask(string EmployeeId) : UiAction;
public sealed record Wake(string EmployeeId) : UiAction;
public sealed record Sleep(string EmployeeId) : UiAction;
public sealed record Reset(string EmployeeId) : UiAction;
public sealed record OpenBrief(string EmployeeId) : UiAction;
public sealed record Approve(string TaskId) : UiAction;
public sealed record Answer(string TaskId) : UiAction;
public sealed record CancelTask(string TaskId) : UiAction;
public sealed record Retry(string TaskId) : UiAction;
public sealed record Reassign(string TaskId, string Assignee) : UiAction;
public sealed record SetGoal(string ManagerId) : UiAction;
public sealed record TopUp(string GoalId) : UiAction;
public sealed record CancelGoal(string GoalId) : UiAction;
public sealed record TalkTo(string EmployeeId) : UiAction;
public sealed record ReloadEmployees : UiAction;
public sealed record Leave : UiAction;

// hiring stand
public sealed record OpenHiring : UiAction;
public sealed record HireRole(string TemplateId) : UiAction;
public sealed record HireBrain(string TemplateId, string Model, string Label) : UiAction;
public sealed record Fire(string EmployeeId) : UiAction;

// your desk
public sealed record OpenFolder(string Path) : UiAction;

// ticket board
public sealed record OpenTicketBoard : UiAction;
public sealed record PickTicketRole : UiAction;
public sealed record PostTicket(string? Role) : UiAction;
