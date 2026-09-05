using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Tests;

public sealed class WorkplacesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hw-workplaces-" + Guid.NewGuid().ToString("N"));
    private readonly string _templates;
    private DateTimeOffset _now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    public WorkplacesTests()
    {
        _templates = Path.Combine(_root, "templates");
        Directory.CreateDirectory(Path.Combine(_templates, "engineer"));
        File.WriteAllText(Path.Combine(_templates, "engineer", "template.json"), "{}");
    }

    private Workplaces Subject() => new(_root, _templates, () => _now);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Create_makes_the_folders_seeds_hiring_and_keeps_names_unique()
    {
        var w = Subject();
        var a = w.Create("Acme");
        var b = w.Create("Acme");
        Assert.Equal("Acme", a.Name);
        Assert.Equal("Acme 2", b.Name);
        Assert.True(Directory.Exists(Path.Combine(a.Root, "employees")));
        Assert.True(Directory.Exists(Path.Combine(a.Root, "data", "workspaces")));
        Assert.True(File.Exists(Path.Combine(a.Root, "hiring", "engineer", "template.json")));
        Assert.True(File.Exists(Path.Combine(a.Root, Workplaces.MetaFile)));
        Assert.Equal(_now, a.Created);
        Assert.Null(a.LastOpened);
        Assert.Equal(0, a.EmployeeCount);
        Assert.False(a.Favourite);
    }

    [Fact]
    public void List_puts_favourites_first_then_the_most_recently_opened_then_names()
    {
        var w = Subject();
        w.Create("Alpha");
        w.Create("Beta");
        w.Create("Gamma");
        _now = _now.AddMinutes(5);
        w.Open("Beta");
        _now = _now.AddMinutes(5);
        w.Open("Alpha");
        w.SetFavourite("Gamma", true);

        var list = w.List();
        Assert.Equal(new[] { "Gamma", "Alpha", "Beta" }, list.Select(x => x.Name));
        Assert.True(list[0].Favourite);
        Assert.Equal(_now, list[1].LastOpened);
    }

    [Fact]
    public void Open_returns_the_service_paths_and_stamps_last_opened()
    {
        var w = Subject();
        w.Create("Acme");
        var paths = w.Open("Acme");
        Assert.Equal(Path.Combine(_root, OfficePaths.ProductFolder, "Acme"), paths.Root);
        Assert.Equal(paths.Employees, paths.ForemanEnvironment()["Foreman__EmployeesPath"]);
        Assert.Equal(_now, w.Get("Acme").LastOpened);
    }

    [Fact]
    public void Employees_are_counted_from_employee_json_folders_only()
    {
        var w = Subject();
        var a = w.Create("Acme");
        Employee(a.Root, "ada-coder");
        Employee(a.Root, "mia-manager");
        Directory.CreateDirectory(Path.Combine(a.Root, "employees", ".former"));
        Directory.CreateDirectory(Path.Combine(a.Root, "employees", "notes"));
        Assert.Equal(2, w.Get("Acme").EmployeeCount);
    }

    [Fact]
    public void Rename_moves_the_folder_and_keeps_everything_in_it()
    {
        var w = Subject();
        var a = w.Create("Acme");
        Employee(a.Root, "ada-coder");
        var renamed = w.Rename("Acme", "Acme Corp");
        Assert.Equal("Acme Corp", renamed.Name);
        Assert.False(Directory.Exists(a.Root));
        Assert.Equal(1, renamed.EmployeeCount);
        Assert.Equal(new[] { "Acme Corp" }, w.List().Select(x => x.Name));
    }

    [Fact]
    public void Duplicate_copies_the_tree_under_a_copy_name_that_was_never_opened()
    {
        var w = Subject();
        var a = w.Create("Acme");
        Employee(a.Root, "ada-coder");
        w.Open("Acme");
        w.SetFavourite("Acme", true);

        var copy = w.Duplicate("Acme");
        Assert.Equal("Acme copy", copy.Name);
        Assert.Equal(1, copy.EmployeeCount);
        Assert.Null(copy.LastOpened);
        Assert.False(copy.Favourite);
        Assert.Equal("Acme copy 2", w.Duplicate("Acme").Name);
        Assert.True(w.Get("Acme").Favourite);   // the original is untouched
    }

    [Fact]
    public void Delete_moves_the_folder_to_the_trash_which_is_never_listed()
    {
        var w = Subject();
        var a = w.Create("Acme");
        Employee(a.Root, "ada-coder");
        var trash = w.Delete("Acme");
        Assert.False(Directory.Exists(a.Root));
        Assert.StartsWith(Path.Combine(w.Root, Workplaces.TrashFolder), trash);
        Assert.True(File.Exists(Path.Combine(trash, "employees", "ada-coder", "employee.json")));
        Assert.Empty(w.List());
        Assert.False(w.Exists("Acme"));
    }

    [Fact]
    public void An_office_folder_from_before_the_menu_is_listed_and_gains_metadata_when_opened()
    {
        var w = Subject();
        var root = Path.Combine(w.Root, "Main Office");
        Employee(root, "tidan-manager");

        var listed = Assert.Single(w.List());
        Assert.Equal("Main Office", listed.Name);
        Assert.Equal(1, listed.EmployeeCount);
        Assert.Null(listed.LastOpened);

        w.Open("Main Office");
        Assert.True(File.Exists(Path.Combine(root, Workplaces.MetaFile)));
        Assert.Equal(_now, w.Get("Main Office").LastOpened);
    }

    [Fact]
    public void Names_are_made_safe_for_the_file_system()
    {
        var w = Subject();
        Assert.Equal("Acme Co", w.Create("Acme: Co?").Name);
        Assert.Equal(OfficePaths.DefaultOfficeName, w.Create("   ").Name);
        Assert.Equal("x", Workplaces.UniqueName("x", _ => false));
        Assert.Equal("x 3", Workplaces.UniqueName("x", n => n is "x" or "x 2"));
    }

    private static void Employee(string root, string id)
    {
        var dir = Path.Combine(root, "employees", id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "employee.json"), "{\"id\":\"" + id + "\"}");
    }
}
