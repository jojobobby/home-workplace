using HomeWorkplace.Client;

namespace HomeWorkplace.UI.Tests;

public class OfficePathsTests
{
    private static string Temp()
    {
        var d = Path.Combine(Path.GetTempPath(), "hw-office-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void The_company_lives_under_documents_home_workplace_office_name()
    {
        var docs = Temp();
        var p = OfficePaths.For("Main Office", docs);

        Assert.Equal(Path.Combine(docs, "Home Workplace", "Main Office"), p.Root);
        Assert.Equal(Path.Combine(p.Root, "employees"), p.Employees);
        Assert.Equal(Path.Combine(p.Root, "hiring"), p.Hiring);
        Assert.Equal(Path.Combine(p.Root, "data"), p.Data);
        Assert.Equal(Path.Combine(p.Root, "data", "workspaces"), p.Workspaces);

        var env = p.ForemanEnvironment();
        Assert.Equal(p.Employees, env["Foreman__EmployeesPath"]);
        Assert.Equal(p.Hiring, env["Foreman__HiringPath"]);
        Assert.Equal(p.Data, env["Foreman__DataPath"]);
    }

    [Fact]
    public void Prepare_creates_the_folders_seeds_templates_once_and_migrates_legacy_data_once()
    {
        var docs = Temp();
        var repo = Temp();
        Write(Path.Combine(repo, "hiring", "engineer", "template.json"), "{\"id\":\"engineer\"}");
        Write(Path.Combine(repo, "employees", "mia", "employee.json"), "{\"id\":\"mia\"}");
        Write(Path.Combine(repo, "employees", ".former", "old", "employee.json"), "{}");
        Write(Path.Combine(repo, "data", "tasks", "t1.json"), "{\"Id\":\"t1\"}");
        Write(Path.Combine(repo, "data", "workspaces", "t1", "pacman.py"), "print('hi')");

        var p = OfficePaths.Prepare("Main Office", Path.Combine(repo, "hiring"), Path.Combine(repo, "employees"), Path.Combine(repo, "data"), docs);

        Assert.True(File.Exists(Path.Combine(p.Hiring, "engineer", "template.json")));
        Assert.True(File.Exists(Path.Combine(p.Employees, "mia", "employee.json")));
        Assert.True(File.Exists(Path.Combine(p.Employees, ".former", "old", "employee.json")));
        Assert.True(File.Exists(Path.Combine(p.Data, "tasks", "t1.json")));
        Assert.True(File.Exists(Path.Combine(p.Workspaces, "t1", "pacman.py")), "the agents' work travels with the company");
        Assert.True(Directory.Exists(p.Workspaces));
        Assert.True(File.Exists(Path.Combine(repo, "employees", "mia", "employee.json")), "the repo copy is left alone");

        // The office copy is now the truth: later edits there survive, and new repo files are not pulled in.
        File.WriteAllText(Path.Combine(p.Employees, "mia", "employee.json"), "edited");
        File.WriteAllText(Path.Combine(p.Hiring, "engineer", "template.json"), "edited");
        Write(Path.Combine(repo, "employees", "late", "employee.json"), "{}");
        var again = OfficePaths.Prepare("Main Office", Path.Combine(repo, "hiring"), Path.Combine(repo, "employees"), Path.Combine(repo, "data"), docs);
        Assert.Equal("edited", File.ReadAllText(Path.Combine(again.Employees, "mia", "employee.json")));
        Assert.Equal("edited", File.ReadAllText(Path.Combine(again.Hiring, "engineer", "template.json")));
        Assert.False(Directory.Exists(Path.Combine(again.Employees, "late")));
    }

    [Fact]
    public void Prepare_without_sources_just_creates_the_folders()
    {
        var docs = Temp();
        var p = OfficePaths.Prepare("Side Office", null, null, null, docs);
        Assert.True(Directory.Exists(p.Employees));
        Assert.True(Directory.Exists(p.Hiring));
        Assert.True(Directory.Exists(p.Workspaces));
        Assert.Empty(Directory.GetFileSystemEntries(p.Employees));
    }

    [Fact]
    public void The_office_name_is_kept_as_a_safe_folder_name()
    {
        var docs = Temp();
        var p = OfficePaths.For("  Ada's Lab: v2/beta  ", docs);
        Assert.Equal(Path.Combine(docs, "Home Workplace", "Ada's Lab v2beta"), p.Root);
        Assert.Equal(Path.Combine(docs, "Home Workplace", "Main Office"), OfficePaths.For("", docs).Root);
    }
}
