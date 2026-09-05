using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class HiringTests
{
    internal static void WriteTemplate(string dp, string id, string role = "Software engineer", long tokensIn = 60_000, long tokensOut = 8_000, int runsPerDay = 6)
    {
        var dir = Path.Combine(dp, "hiring", id); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "template.json"),
            $$$"""{"id":"{{{id}}}","role":"{{{role}}}","description":"Builds things","effort":"low","claudeAllowedTools":["Read"],"codexSandbox":"read-only","schedule":{"wake":"09:00","sleep":"20:00"},"maxRunMinutes":30,"typicalTokensPerRun":{"in":{{{tokensIn}}},"out":{{{tokensOut}}}},"runsPerDay":{{{runsPerDay}}}}""");
        File.WriteAllText(Path.Combine(dir, "skills.md"), "# Skills\nwork carefully");
        File.WriteAllText(Path.Combine(dir, "life.md"), "# Life\nsteady");
    }

    [Fact]
    public async Task The_hiring_desk_lists_templates_with_a_cost_per_brain()
    {
        using var factory = ForemanFactory.Create(out var dp);
        WriteTemplate(dp, "engineer");
        WriteTemplate(dp, "manager", role: "Engineering manager", tokensIn: 20_000, tokensOut: 3_000, runsPerDay: 8);
        using var c = factory.CreateClient();

        var view = await c.GetFromJsonAsync<HiringView>("/hiring", TestJson.Options);

        Assert.Equal(new[] { "engineer", "manager" }, view!.Templates.Select(t => t.Id));
        var engineer = view.Templates[0];
        Assert.Equal("Software engineer", engineer.Role);
        Assert.Equal("Builds things", engineer.Description);
        var haiku = engineer.Brains.Single(b => b.Model == "claude-haiku-4-5-20251001");
        Assert.Equal(Vendor.Claude, haiku.Vendor);
        Assert.Equal("Claude Haiku 4.5", haiku.Label);
        Assert.Equal(0.10m, haiku.UsdPerRun);
        Assert.Equal(0.60m, haiku.UsdPerDay);
        Assert.Contains(engineer.Brains, b => b.Vendor == Vendor.Codex && b.Model == "gpt-5-codex");
        Assert.Contains(engineer.Brains, b => b.Model == "claude-fable-5-1");
        Assert.Contains(engineer.Brains, b => b.Model == "claude-opus-5");
        Assert.All(engineer.Brains, b => Assert.True(b.UsdPerRun > 0m && b.UsdPerDay >= b.UsdPerRun));
    }

    [Fact]
    public async Task A_missing_hiring_folder_means_no_templates_not_an_error()
    {
        using var factory = ForemanFactory.Create(out _);
        using var c = factory.CreateClient();
        var view = await c.GetFromJsonAsync<HiringView>("/hiring", TestJson.Options);
        Assert.Empty(view!.Templates);
    }
}

public class HireAndFireTests
{
    private static Task<HttpResponseMessage> Hire(HttpClient c, string name, string template = "engineer", string model = "claude-haiku-4-5-20251001")
        => c.PostAsJsonAsync("/hiring", new { templateId = template, model, name });

    [Fact]
    public async Task Hiring_writes_the_folder_reloads_wakes_and_announces()
    {
        using var factory = ForemanFactory.Create(out var dp);
        HiringTests.WriteTemplate(dp, "engineer");
        using var c = factory.CreateClient();

        var resp = await Hire(c, "Ada Lovelace");
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var hired = await resp.Content.ReadFromJsonAsync<EmployeeView>(TestJson.Options);
        Assert.Equal("ada-lovelace-engineer", hired!.Id);
        Assert.Equal("Ada Lovelace", hired.Name);
        Assert.Equal(EmployeeStatus.Awake, hired.Status);

        var dir = Path.Combine(dp, "employees", "ada-lovelace-engineer");
        var json = File.ReadAllText(Path.Combine(dir, "employee.json"));
        Assert.Contains("\"claude-haiku-4-5-20251001\"", json);
        Assert.Contains("\"claude\"", json);
        Assert.Contains("Software engineer", json);
        Assert.Contains("work carefully", File.ReadAllText(Path.Combine(dir, "skills.md")));
        Assert.Contains("steady", File.ReadAllText(Path.Combine(dir, "life.md")));

        var employees = await c.GetFromJsonAsync<List<EmployeeView>>("/employees", TestJson.Options);
        Assert.Contains(employees!, e => e.Id == "ada-lovelace-engineer" && e.Role == "Software engineer" && e.Model == "claude-haiku-4-5-20251001");
        var events = await c.GetFromJsonAsync<EventPage>("/events?since=0&limit=200", TestJson.Options);
        Assert.Contains(events!.Events, e => e.Type == "employee.hired" && e.EmployeeId == "ada-lovelace-engineer");
    }

    [Fact]
    public async Task Two_hires_with_the_same_name_get_distinct_ids_and_codex_brains_set_the_vendor()
    {
        using var factory = ForemanFactory.Create(out var dp);
        HiringTests.WriteTemplate(dp, "engineer");
        using var c = factory.CreateClient();

        var first = await (await Hire(c, "Sam")).Content.ReadFromJsonAsync<EmployeeView>(TestJson.Options);
        var second = await (await Hire(c, "Sam", model: "gpt-5-codex")).Content.ReadFromJsonAsync<EmployeeView>(TestJson.Options);

        Assert.Equal("sam-engineer", first!.Id);
        Assert.Equal("sam-engineer-2", second!.Id);
        Assert.Equal(Vendor.Codex, second.Vendor);
        Assert.Equal("gpt-5-codex", second.Model);
    }

    [Theory]
    [InlineData("ghost", "claude-haiku-4-5-20251001", "Sam")]
    [InlineData("engineer", "gpt-2", "Sam")]
    [InlineData("engineer", "claude-haiku-4-5-20251001", "   ")]
    [InlineData("engineer", "claude-haiku-4-5-20251001", "this name is far too long for a badge")]
    public async Task Bad_hires_are_400(string template, string model, string name)
    {
        using var factory = ForemanFactory.Create(out var dp);
        HiringTests.WriteTemplate(dp, "engineer");
        using var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await Hire(c, name, template, model)).StatusCode);
        Assert.Empty(Directory.Exists(Path.Combine(dp, "employees")) ? Directory.GetDirectories(Path.Combine(dp, "employees")) : Array.Empty<string>());
    }

    [Fact]
    public async Task Firing_an_idle_employee_archives_the_folder_and_removes_them()
    {
        using var factory = ForemanFactory.Create(out var dp);
        HiringTests.WriteTemplate(dp, "engineer");
        using var c = factory.CreateClient();
        await Hire(c, "Sam");

        var resp = await c.PostAsync("/employees/sam-engineer/fire", null);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(dp, "employees", "sam-engineer")));
        Assert.Single(Directory.GetDirectories(Path.Combine(dp, "employees", ".former")), d => Path.GetFileName(d).StartsWith("sam-engineer-"));
        var employees = await c.GetFromJsonAsync<List<EmployeeView>>("/employees", TestJson.Options);
        Assert.DoesNotContain(employees!, e => e.Id == "sam-engineer");
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/employees/sam-engineer/fire", null)).StatusCode);
        var events = await c.GetFromJsonAsync<EventPage>("/events?since=0&limit=200", TestJson.Options);
        Assert.Contains(events!.Events, e => e.Type == "employee.fired" && e.EmployeeId == "sam-engineer");
    }

    [Fact]
    public async Task Firing_a_working_employee_is_refused()
    {
        using var factory = ForemanFactory.Create(out var dp);
        HiringTests.WriteTemplate(dp, "engineer");
        var gate = new TaskCompletionSource();
        factory.Provider.Enqueue(s => { gate.Task.Wait(); return FakeAgentProvider.Done(s); });
        using var c = factory.CreateClient();
        await Hire(c, "Sam");
        await c.PostAsJsonAsync("/tasks", new { title = "T", brief = "b", assignee = "sam-engineer" });
        var end = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < end && (await c.GetFromJsonAsync<EmployeeView>("/employees/sam-engineer", TestJson.Options))!.Status != EmployeeStatus.Working) await Task.Delay(50);

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/employees/sam-engineer/fire", null)).StatusCode);
        Assert.True(Directory.Exists(Path.Combine(dp, "employees", "sam-engineer")));
        gate.SetResult();
    }
}
