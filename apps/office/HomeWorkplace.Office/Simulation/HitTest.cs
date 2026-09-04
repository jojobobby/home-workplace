using System.Numerics;

namespace HomeWorkplace.Office.Sim;

/// <summary>Which visible agent is under a world point (their 16×16 sprite box), topmost first.</summary>
public static class HitTest
{
    public static Agent? AgentAt(Simulation sim, Vector2 world)
    {
        var half = Agent.TileSize / 2f;
        return sim.Agents.Values
            .Where(a => a.Visible)
            .Where(a => Math.Abs(world.X - a.Position.X) <= half && Math.Abs(world.Y - a.Position.Y) <= half)
            .OrderByDescending(a => a.Position.Y)
            .FirstOrDefault();
    }
}
