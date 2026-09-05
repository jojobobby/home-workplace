using System.Text.Json;

namespace HomeWorkplace.Client;

/// <summary>One row of the workplace list: the folder plus what workplace.json remembers about it.</summary>
public sealed record WorkplaceInfo(string Name, string Root, int EmployeeCount, DateTimeOffset Created, DateTimeOffset? LastOpened, bool Favourite);

/// <summary>workplace.json: the things a folder cannot say for itself.</summary>
public sealed class WorkplaceMeta
{
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? LastOpened { get; set; }
    public bool Favourite { get; set; }
}

/// <summary>
/// The workplaces on this machine: every office folder under Documents\Home Workplace. Create,
/// rename, duplicate, delete (to a trash folder) and list them, favourites first. A folder
/// without workplace.json (an office from before the menu) is listed too and gains one when
/// it is opened.
/// </summary>
public sealed class Workplaces
{
    public const string TrashFolder = ".trash";
    public const string MetaFile = "workplace.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private readonly string _documentsRoot;
    private readonly string? _templatesSource;
    private readonly Func<DateTimeOffset> _now;

    /// <param name="documentsRoot">Where "Home Workplace" lives; the user's Documents by default.</param>
    /// <param name="templatesSource">The repo's hiring templates, copied into a workplace whose hiring folder is empty.</param>
    public Workplaces(string? documentsRoot = null, string? templatesSource = null, Func<DateTimeOffset>? now = null)
    {
        _documentsRoot = documentsRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _templatesSource = templatesSource;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Documents\Home Workplace.</summary>
    public string Root => Path.Combine(_documentsRoot, OfficePaths.ProductFolder);

    public bool Exists(string name) => Directory.Exists(OfficePaths.For(name, _documentsRoot).Root);

    /// <summary>Favourites first, then the most recently opened, then by name; the trash is never listed.</summary>
    public IReadOnlyList<WorkplaceInfo> List()
    {
        if (!Directory.Exists(Root)) return Array.Empty<WorkplaceInfo>();
        return Directory.EnumerateDirectories(Root)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Select(Info)
            .OrderByDescending(w => w.Favourite)
            .ThenByDescending(w => w.LastOpened ?? DateTimeOffset.MinValue)
            .ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public WorkplaceInfo Get(string name) => Info(OfficePaths.For(name, _documentsRoot).Root);

    /// <summary>A new, empty workplace; a taken name gets " 2", " 3", …</summary>
    public WorkplaceInfo Create(string name)
    {
        var unique = UniqueName(OfficePaths.SafeName(name), Exists);
        var paths = OfficePaths.Prepare(unique, _templatesSource, null, null, _documentsRoot);
        WriteMeta(paths.Root, new WorkplaceMeta { Created = _now() });
        return Info(paths.Root);
    }

    /// <summary>Prepare the folders (seeding hiring when it is empty), stamp lastOpened, and return the paths the services need.</summary>
    public OfficePaths Open(string name)
    {
        var paths = OfficePaths.Prepare(name, _templatesSource, null, null, _documentsRoot);
        var meta = ReadMeta(paths.Root) ?? new WorkplaceMeta { Created = Directory.GetCreationTimeUtc(paths.Root) };
        meta.LastOpened = _now();
        WriteMeta(paths.Root, meta);
        return paths;
    }

    public WorkplaceInfo Rename(string name, string newName)
    {
        var from = OfficePaths.For(name, _documentsRoot).Root;
        var wanted = OfficePaths.SafeName(newName);
        if (string.Equals(wanted, Path.GetFileName(from), StringComparison.OrdinalIgnoreCase)) return Info(from);
        var to = OfficePaths.For(UniqueName(wanted, Exists), _documentsRoot).Root;
        Directory.Move(from, to);
        return Info(to);
    }

    /// <summary>A full copy under "&lt;name&gt; copy" (or " copy 2", …) that has never been opened.</summary>
    public WorkplaceInfo Duplicate(string name)
    {
        var from = OfficePaths.For(name, _documentsRoot).Root;
        var to = OfficePaths.For(UniqueName(OfficePaths.SafeName(name + " copy"), Exists), _documentsRoot).Root;
        CopyTree(from, to);
        WriteMeta(to, new WorkplaceMeta { Created = _now() });
        return Info(to);
    }

    /// <summary>Move the folder to Documents\Home Workplace\.trash\&lt;name&gt;-&lt;stamp&gt;; returns where it went.</summary>
    public string Delete(string name)
    {
        var from = OfficePaths.For(name, _documentsRoot).Root;
        var trash = Path.Combine(Root, TrashFolder);
        Directory.CreateDirectory(trash);
        var stamp = $"{Path.GetFileName(from)}-{_now():yyyyMMdd-HHmmss}";
        var to = Path.Combine(trash, stamp);
        for (var n = 2; Directory.Exists(to); n++) to = Path.Combine(trash, $"{stamp}-{n}");
        Directory.Move(from, to);
        return to;
    }

    public WorkplaceInfo SetFavourite(string name, bool favourite)
    {
        var root = OfficePaths.For(name, _documentsRoot).Root;
        var meta = ReadMeta(root) ?? new WorkplaceMeta { Created = Directory.GetCreationTimeUtc(root) };
        meta.Favourite = favourite;
        WriteMeta(root, meta);
        return Info(root);
    }

    /// <summary>The name itself, or the first "name 2", "name 3", … that <paramref name="taken"/> accepts.</summary>
    public static string UniqueName(string wanted, Func<string, bool> taken)
    {
        if (!taken(wanted)) return wanted;
        for (var n = 2; ; n++)
            if (!taken($"{wanted} {n}")) return $"{wanted} {n}";
    }

    private WorkplaceInfo Info(string root)
    {
        var meta = ReadMeta(root);
        var employees = Path.Combine(root, "employees");
        var count = Directory.Exists(employees)
            ? Directory.EnumerateDirectories(employees).Count(d => !Path.GetFileName(d).StartsWith('.') && File.Exists(Path.Combine(d, "employee.json")))
            : 0;
        return new WorkplaceInfo(Path.GetFileName(root), root, count,
            meta?.Created ?? Directory.GetCreationTimeUtc(root), meta?.LastOpened, meta?.Favourite ?? false);
    }

    private static WorkplaceMeta? ReadMeta(string root)
    {
        var path = Path.Combine(root, MetaFile);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<WorkplaceMeta>(File.ReadAllText(path), Json); }
        catch (JsonException) { return null; }
    }

    private static void WriteMeta(string root, WorkplaceMeta meta)
        => File.WriteAllText(Path.Combine(root, MetaFile), JsonSerializer.Serialize(meta, Json));

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
