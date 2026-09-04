using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class EmployeeCatalogTests
{
    private static void WriteEmployee(string employeesPath, string id, string json, string skills = "skills", string life = "life")
    {
        var dir = Path.Combine(employeesPath, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "employee.json"), json);
        File.WriteAllText(Path.Combine(dir, "skills.md"), skills);
        File.WriteAllText(Path.Combine(dir, "life.md"), life);
    }

    private const string AdaJson = """
    { "id": "ada-coder", "name": "Ada", "role": "Engineer", "vendor": "claude",
      "model": "claude-haiku-4-5-20251001", "effort": "low",
      "claudeAllowedTools": ["Read","Edit"], "schedule": { "wake": "09:00", "sleep": "20:00" } }
    """;

    [Fact]
    public async Task Loads_an_employee_from_disk_with_its_md_files()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteEmployee(Path.Combine(dataPath, "employees"), "ada-coder", AdaJson, skills: "TDD always", life: "sleeps at 8");
        using var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<List<EmployeeView>>("/employees", TestJson.Options);

        var ada = Assert.Single(list!);
        Assert.Equal("Ada", ada.Name);
        Assert.Equal(Vendor.Claude, ada.Vendor);
        Assert.Equal(EmployeeStatus.Asleep, ada.Status);
        Assert.Equal(100, ada.Energy);
        Assert.Equal("09:00", ada.Wake);     // the office game lights the room by the team's shifts
        Assert.Equal("20:00", ada.Sleep);
    }

    [Fact]
    public async Task A_malformed_employee_json_does_not_crash_startup()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteEmployee(Path.Combine(dataPath, "employees"), "broken", "{ not json ");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/employees");

        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<List<EmployeeView>>(TestJson.Options);
        Assert.DoesNotContain(list!, e => e.Id == "broken");
    }

    [Fact]
    public async Task Reload_picks_up_a_newly_added_employee()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        using var client = factory.CreateClient();
        Assert.Empty((await client.GetFromJsonAsync<List<EmployeeView>>("/employees", TestJson.Options))!);

        WriteEmployee(Path.Combine(dataPath, "employees"), "ada-coder", AdaJson);
        await client.PostAsync("/employees/reload", content: null);

        var list = await client.GetFromJsonAsync<List<EmployeeView>>("/employees", TestJson.Options);
        Assert.Single(list!);
    }
}
