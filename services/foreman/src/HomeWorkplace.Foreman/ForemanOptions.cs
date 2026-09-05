namespace HomeWorkplace.Foreman;

public sealed class ForemanOptions
{
    public const string SectionName = "Foreman";

    public string EmployeesPath { get; set; } = "../../employees";
    public string DataPath { get; set; } = "./data";
    public string ContextApiBaseUrl { get; set; } = "http://localhost:5171";
    public string ClaudeExecutable { get; set; } = "claude";
    public string CodexExecutable { get; set; } = "codex";
    public int MaxRunMinutes { get; set; } = 30;
    public int SchedulerTickSeconds { get; set; } = 30;
    public int EventsCapacity { get; set; } = 5000;
    public int MaxHandoffDepth { get; set; } = 5;

    /// <summary>Cap on actions a single manager run may emit; extras are ignored.</summary>
    public int MaxActionsPerRun { get; set; } = 5;

    /// <summary>After a manager run fails at the API, PumpGoals leaves the goal alone this long; explicit requests (top-up, approve, wake) still retry.</summary>
    public int ManagerErrorBackoffMinutes { get; set; } = 10;

    /// <summary>
    /// $ per million tokens by model, used when a CLI does not report a cost. "default" is
    /// the fallback for unknown models. Tune to current list prices; wrong prices mis-budget
    /// but never break anything.
    /// </summary>
    public Dictionary<string, ModelPrice> Pricing { get; set; } = new()
    {
        ["default"] = new ModelPrice(3.00m, 15.00m),
        ["claude-haiku-4-5-20251001"] = new ModelPrice(0.80m, 4.00m),
        ["gpt-5-codex"] = new ModelPrice(1.25m, 10.00m),
    };
}
