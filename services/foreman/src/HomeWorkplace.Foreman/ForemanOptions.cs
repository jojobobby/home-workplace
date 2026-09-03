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
}
