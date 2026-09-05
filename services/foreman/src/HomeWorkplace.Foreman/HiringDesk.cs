using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeWorkplace.Foreman;

/// <summary>A model an employee can run on, and which CLI (subscription) it needs.</summary>
public sealed record Brain(string Model, Vendor Vendor, string Label);

public sealed record TokenEstimate(long In, long Out);

/// <summary>A role you can hire into: everything an employee.json needs except the person (name, vendor, model).</summary>
public sealed record HiringTemplate
{
    public required string Id { get; init; }
    public required string Role { get; init; }
    public string Description { get; init; } = "";
    public string? Effort { get; init; }
    public IReadOnlyList<string> ClaudeAllowedTools { get; init; } = Array.Empty<string>();
    public string? CodexSandbox { get; init; }
    public required Schedule Schedule { get; init; }
    public int? MaxRunMinutes { get; init; }
    public TokenEstimate TypicalTokensPerRun { get; init; } = new(40_000, 5_000);
    public int RunsPerDay { get; init; } = 6;
    public string SkillsMd { get; init; } = "";
    public string LifeMd { get; init; } = "";
}

public sealed record BrainCost(string Model, Vendor Vendor, string Label, decimal UsdPerRun, decimal UsdPerDay);
public sealed record HiringTemplateView(string Id, string Role, string Description, IReadOnlyList<BrainCost> Brains);
public sealed record HiringView(IReadOnlyList<HiringTemplateView> Templates, IReadOnlyList<Brain> Brains);
public sealed record HireRequest(string TemplateId, string Model, string Name);
public enum FireResult { Ok, Busy, NotFound }

public sealed class HiringException : Exception
{
    public HiringException(string message) : base(message) { }
}

/// <summary>
/// The hiring stand's back office: role templates under <see cref="ForemanOptions.HiringPath"/>,
/// the brains Foreman knows, approximate costs (template tokens × list price), and the two
/// moves — hire (write an employee folder, reload, wake) and fire (archive the folder, reload).
/// The employee folder stays the only truth for who works here.
/// </summary>
public sealed class HiringDesk
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ForemanOptions _options;
    private readonly EmployeeCatalog _catalog;
    private readonly EventLog _events;
    private readonly TimeProvider _clock;

    public HiringDesk(ForemanOptions options, EmployeeCatalog catalog, EventLog events, TimeProvider clock)
    {
        _options = options;
        _catalog = catalog;
        _events = events;
        _clock = clock;
    }

    public IReadOnlyList<HiringTemplate> Templates()
    {
        if (!Directory.Exists(_options.HiringPath)) return Array.Empty<HiringTemplate>();
        var list = new List<HiringTemplate>();
        foreach (var dir in Directory.EnumerateDirectories(_options.HiringPath))
        {
            var path = Path.Combine(dir, "template.json");
            if (!File.Exists(path)) continue;
            try
            {
                var t = JsonSerializer.Deserialize<HiringTemplate>(File.ReadAllText(path), Json) ?? throw new JsonException("template.json deserialized to null");
                list.Add(t with { SkillsMd = ReadSibling(dir, "skills.md"), LifeMd = ReadSibling(dir, "life.md") });
            }
            catch (Exception ex)
            {
                _events.Emit("catalog.error", data: new { folder = dir, error = ex.Message });
            }
        }
        return list.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
    }

    public HiringView List()
    {
        var brains = _options.Brains;
        var templates = Templates().Select(t => new HiringTemplateView(t.Id, t.Role, t.Description,
            brains.Select(b => Cost(t, b)).ToList())).ToList();
        return new HiringView(templates, brains);
    }

    private BrainCost Cost(HiringTemplate t, Brain b)
    {
        var usage = new Usage(0, t.TypicalTokensPerRun.In, t.TypicalTokensPerRun.Out, null, null);
        var perRun = Math.Round(Foreman.Cost.Of(usage, b.Model, _options.Pricing), 2, MidpointRounding.AwayFromZero);
        var perDay = Math.Round(perRun * Math.Max(1, t.RunsPerDay), 2, MidpointRounding.AwayFromZero);
        return new BrainCost(b.Model, b.Vendor, b.Label, perRun, perDay);
    }

    /// <summary>Write the employee folder from the template and the brain, reload the catalog, and wake the new hire.</summary>
    public EmployeeView Hire(HireRequest req)
    {
        var template = Templates().FirstOrDefault(t => t.Id == req.TemplateId)
            ?? throw new HiringException($"Unknown role '{req.TemplateId}'.");
        var brain = _options.Brains.FirstOrDefault(b => b.Model == req.Model)
            ?? throw new HiringException($"Unknown brain '{req.Model}'.");
        var name = (req.Name ?? "").Trim();
        if (name.Length is < 1 or > 24) throw new HiringException("A name of 1 to 24 characters is required.");

        Directory.CreateDirectory(_options.EmployeesPath);
        var id = UniqueId($"{Slug(name)}-{Slug(template.Id)}");
        var dir = Path.Combine(_options.EmployeesPath, id);
        Directory.CreateDirectory(dir);

        var definition = new
        {
            id,
            name,
            role = template.Role,
            vendor = brain.Vendor,
            model = brain.Model,
            effort = template.Effort,
            claudeAllowedTools = template.ClaudeAllowedTools,
            codexSandbox = template.CodexSandbox,
            schedule = new { wake = template.Schedule.Wake, sleep = template.Schedule.Sleep },
            maxRunMinutes = template.MaxRunMinutes,
            hiredFrom = template.Id,
        };
        File.WriteAllText(Path.Combine(dir, "employee.json"), JsonSerializer.Serialize(definition, Json));
        File.WriteAllText(Path.Combine(dir, "skills.md"), template.SkillsMd);
        File.WriteAllText(Path.Combine(dir, "life.md"), template.LifeMd);

        _catalog.Load();
        var now = _clock.GetLocalNow();
        var inShift = EmployeeCatalog.WithinShift(TimeOnly.FromDateTime(now.DateTime), template.Schedule.WakeTime, template.Schedule.SleepTime);
        _catalog.Wake(id, inShift ? null : now.AddHours(8));   // hired after hours: they still come in today
        _events.Emit("employee.hired", employeeId: id, data: new { template = template.Id, brain.Model, name });

        return _catalog.List().First(e => e.Id == id);
    }

    /// <summary>Archive the employee folder under employees/.former and reload. Refused while they are working.</summary>
    public FireResult Fire(string id)
    {
        if (_catalog.Find(id) is null) return FireResult.NotFound;
        var status = _catalog.GetState(id).Status;
        if (status is EmployeeStatus.Working or EmployeeStatus.Waiting) return FireResult.Busy;

        var dir = Path.Combine(_options.EmployeesPath, id);
        if (Directory.Exists(dir))
        {
            var former = Path.Combine(_options.EmployeesPath, ".former");
            Directory.CreateDirectory(former);
            Directory.Move(dir, Path.Combine(former, $"{id}-{_clock.GetUtcNow():yyyyMMdd-HHmmss}"));
        }
        _catalog.Load();
        _events.Emit("employee.fired", employeeId: id);
        return FireResult.Ok;
    }

    private string UniqueId(string baseId)
    {
        var id = baseId;
        for (var n = 2; Directory.Exists(Path.Combine(_options.EmployeesPath, id)) || _catalog.Find(id) is not null; n++)
            id = $"{baseId}-{n}";
        return id;
    }

    public static string Slug(string text)
    {
        var sb = new StringBuilder();
        var dash = false;
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128) { sb.Append(ch); dash = false; }
            else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
        }
        var slug = sb.ToString().TrimEnd('-');
        return slug.Length == 0 ? "hire" : slug;
    }

    private static string ReadSibling(string dir, string file)
    {
        var path = Path.Combine(dir, file);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
