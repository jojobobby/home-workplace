using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Sim;

/// <summary>
/// The office behind the main menu and in the canned scenes: four employees settled after
/// forty seconds, Rex waiting on a human, the same every time. No service is involved.
/// </summary>
public static class Showroom
{
    public static readonly string[] Ids = { "ada-coder", "mia-manager", "rex-reviewer", "vfx-artist" };

    public static Simulation Build(int seed = 7)
    {
        var sim = new Simulation(WorldLayout.Generate(Ids), seed);
        sim.Apply(new EmployeeAppeared("ada-coder", "Ada", EmployeeStatus.Working, "Build the parser"));
        sim.Apply(new EmployeeAppeared("mia-manager", "Mia", EmployeeStatus.Awake, null));
        sim.Apply(new EmployeeAppeared("rex-reviewer", "Rex", EmployeeStatus.Awake, null));
        sim.Apply(new EmployeeAppeared("vfx-artist", "Vex", EmployeeStatus.Asleep, null));
        for (var i = 0; i < 60 * 40; i++) sim.Update(1f / 60f);
        sim.Apply(new HumanNeeded("rex-reviewer"));
        return sim;
    }
}
