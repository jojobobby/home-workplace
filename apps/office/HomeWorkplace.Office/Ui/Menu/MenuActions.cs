namespace HomeWorkplace.Office.Ui;

/// <summary>What a menu asks for. Screens emit these; <see cref="MenuUi"/> carries out the ones about workplaces and settings and hands the rest to the game.</summary>
public abstract record MenuAction;

public sealed record OpenSinglePlayer : MenuAction;
public sealed record OpenMultiplayer : MenuAction;
public sealed record OpenSettings : MenuAction;
public sealed record QuitGame : MenuAction;
public sealed record GoBack : MenuAction;

public sealed record PlayWorkplace(string Name) : MenuAction;
public sealed record NewWorkplace : MenuAction;
public sealed record RenameWorkplace(string Name) : MenuAction;
public sealed record DuplicateWorkplace(string Name) : MenuAction;
public sealed record DeleteWorkplace(string Name) : MenuAction;
/// <summary>The confirm said yes.</summary>
public sealed record ConfirmedDelete(string Name) : MenuAction;
public sealed record OpenWorkplaceFolder(string Name) : MenuAction;
public sealed record ToggleFavourite(string Name) : MenuAction;

public sealed record ResumeOffice : MenuAction;
public sealed record LeaveOffice : MenuAction;

public sealed record HostAndPlay : MenuAction;
public sealed record JoinViaIp : MenuAction;

public sealed record EditPlayerName : MenuAction;

/// <summary>What a click did to a menu layer: nothing, moved the selection, or landed on something to activate (the caller sends Accept).</summary>
public enum ClickResult { Miss, Selected, Activate }
