namespace HomeWorkplace.Foreman;

public sealed class ForemanOptions
{
    public const string SectionName = "Foreman";

    public string EmployeesPath { get; set; } = "../../employees";
    /// <summary>Role templates the hiring stand offers (template.json + skills.md + life.md per folder).</summary>
    public string HiringPath { get; set; } = "../../hiring";

    /// <summary>The brains an employee can be hired with, by vendor. Edit to match what your subscriptions unlock.</summary>
    public List<Brain> Brains { get; set; } = new()
    {
        new("claude-haiku-4-5-20251001", Vendor.Claude, "Claude Haiku 4.5"),
        new("claude-sonnet-5", Vendor.Claude, "Claude Sonnet 5"),
        new("claude-opus-4-8", Vendor.Claude, "Claude Opus 4.8"),
        new("claude-opus-5", Vendor.Claude, "Claude Opus 5"),
        new("claude-fable-5-1", Vendor.Claude, "Claude Fable 5.1"),
        new("gpt-5-codex", Vendor.Codex, "GPT-5 Codex"),
    };
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
        ["claude-haiku-4-5-20251001"] = new ModelPrice(1.00m, 5.00m),
        ["claude-sonnet-5"] = new ModelPrice(3.00m, 15.00m),
        ["claude-opus-4-8"] = new ModelPrice(5.00m, 25.00m),
        ["claude-opus-5"] = new ModelPrice(5.00m, 25.00m),
        ["claude-fable-5-1"] = new ModelPrice(15.00m, 75.00m),
        ["gpt-5-codex"] = new ModelPrice(1.25m, 10.00m),
    };
}
