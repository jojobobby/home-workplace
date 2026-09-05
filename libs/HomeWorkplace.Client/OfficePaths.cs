namespace HomeWorkplace.Client;

/// <summary>
/// Where a company lives on disk: <c>Documents\Home Workplace\&lt;office&gt;\</c> with the
/// employees, the hiring templates, and Foreman's data (tasks, goals, events, state and the
/// workspaces the agents work in). The folder, not the repo, is the truth once it exists.
/// </summary>
public sealed record OfficePaths(string Root, string Employees, string Hiring, string Data)
{
    public const string ProductFolder = "Home Workplace";
    public const string DefaultOfficeName = "Main Office";

    public string Workspaces => Path.Combine(Data, "workspaces");

    public static OfficePaths For(string officeName, string? documentsRoot = null)
    {
        var docs = documentsRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var root = Path.Combine(docs, ProductFolder, SafeName(officeName));
        return new OfficePaths(root, Path.Combine(root, "employees"), Path.Combine(root, "hiring"), Path.Combine(root, "data"));
    }

    /// <summary>
    /// Create the folders; seed <c>hiring</c> from <paramref name="templatesSource"/> when it is
    /// empty; copy a legacy <paramref name="legacyEmployees"/> / <paramref name="legacyData"/>
    /// tree in once, only when the office's own folder is still empty. Nothing is ever
    /// overwritten or deleted.
    /// </summary>
    public static OfficePaths Prepare(string officeName, string? templatesSource, string? legacyEmployees, string? legacyData, string? documentsRoot = null)
    {
        var paths = For(officeName, documentsRoot);
        Directory.CreateDirectory(paths.Employees);
        Directory.CreateDirectory(paths.Hiring);
        Directory.CreateDirectory(paths.Workspaces);
        SeedIfEmpty(templatesSource, paths.Hiring);
        SeedIfEmpty(legacyEmployees, paths.Employees);
        SeedIfEmpty(legacyData, paths.Data, ignoreEntries: new[] { "workspaces" }, allowExisting: new[] { "workspaces" });
        return paths;
    }

    /// <summary>The configuration Foreman needs to use these folders (ASP.NET reads <c>Section__Key</c> from the environment).</summary>
    public IReadOnlyDictionary<string, string?> ForemanEnvironment() => new Dictionary<string, string?>
    {
        ["Foreman__EmployeesPath"] = Employees,
        ["Foreman__HiringPath"] = Hiring,
        ["Foreman__DataPath"] = Data,
    };

    /// <summary>A folder name Windows accepts: invalid characters dropped, trimmed, never empty.</summary>
    public static string SafeName(string officeName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((officeName ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? DefaultOfficeName : cleaned;
    }

    private static void SeedIfEmpty(string? source, string target, string[]? ignoreEntries = null, string[]? allowExisting = null)
    {
        if (source is null || !Directory.Exists(source)) return;
        Directory.CreateDirectory(target);
        var existing = Directory.EnumerateFileSystemEntries(target)
            .Select(Path.GetFileName)
            .Where(n => allowExisting is null || !allowExisting.Contains(n, StringComparer.OrdinalIgnoreCase));
        if (existing.Any()) return;   // the office already has its own: leave it alone
        CopyTree(source, target, ignoreEntries ?? Array.Empty<string>());
    }

    private static void CopyTree(string source, string target, string[] ignoreTopLevel)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (ignoreTopLevel.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(target, name), overwrite: false);
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (ignoreTopLevel.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            CopyTree(dir, Path.Combine(target, name), Array.Empty<string>());
        }
    }
}
