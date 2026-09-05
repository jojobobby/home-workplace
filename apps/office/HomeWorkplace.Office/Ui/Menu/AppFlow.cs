namespace HomeWorkplace.Office.Ui;

public enum AppPhase { Menu, Booting, Running, Failed }

/// <summary>
/// Where the app is: at the menu, booting a workplace, in the office, or stuck on a failed
/// boot. The game runs the side effects (start and stop the services, build the world); this
/// only guards the transitions so they cannot happen out of order.
/// </summary>
public sealed class AppFlow
{
    public AppPhase Phase { get; private set; } = AppPhase.Menu;
    /// <summary>The workplace being booted or played, null at the menu.</summary>
    public string? Workplace { get; private set; }

    public void Play(string workplace)
    {
        Require(AppPhase.Menu, AppPhase.Failed);
        Workplace = workplace;
        Phase = AppPhase.Booting;
    }

    public void BootSucceeded()
    {
        Require(AppPhase.Booting);
        Phase = AppPhase.Running;
    }

    public void BootFailed()
    {
        Require(AppPhase.Booting);
        Phase = AppPhase.Failed;
    }

    public void Retry()
    {
        Require(AppPhase.Failed);
        Phase = AppPhase.Booting;
    }

    public void Leave()
    {
        Require(AppPhase.Booting, AppPhase.Running, AppPhase.Failed);
        Workplace = null;
        Phase = AppPhase.Menu;
    }

    private void Require(params AppPhase[] allowed)
    {
        if (!allowed.Contains(Phase)) throw new InvalidOperationException($"cannot do that while {Phase}");
    }
}
