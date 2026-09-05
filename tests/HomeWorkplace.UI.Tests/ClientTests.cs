using System.Net;
using System.Text;
using System.Text.Json;
using HomeWorkplace.Client;

namespace HomeWorkplace.UI.Tests;

/// <summary>Records the last request and answers with a canned response.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string Body { get; set; } = "{}";
    public string ContentType { get; set; } = "application/json";
    public HttpMethod? LastMethod { get; private set; }
    public string? LastPathAndQuery { get; private set; }
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastMethod = request.Method;
        LastPathAndQuery = request.RequestUri!.PathAndQuery;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, ContentType) };
    }
}

public class ClientTests
{
    private static string Fx(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    private static (ForemanClient Client, StubHandler Stub) Foreman(string body = "{}", HttpStatusCode status = HttpStatusCode.OK)
    {
        var stub = new StubHandler { Body = body, Status = status };
        return (new ForemanClient(new HttpClient(stub) { BaseAddress = new Uri("http://foreman.test") }), stub);
    }

    // ---- DTOs parse the recorded shapes -------------------------------------------------

    [Fact]
    public void Task_fixture_parses()
    {
        var t = JsonSerializer.Deserialize<TaskDto>(Fx("task.json"), ApiJson.Options)!;
        Assert.Equal("6309ec5a", t.Id);
        Assert.Equal(TaskState.Queued, t.Status);
        Assert.True(t.RequiresApproval);
        Assert.Equal("task-6309ec5a", t.Room);
        Assert.Null(t.GoalId);
        Assert.Empty(t.Runs);
        Assert.Empty(t.Progress);
    }

    [Fact]
    public void Goal_fixture_parses()
    {
        var g = JsonSerializer.Deserialize<GoalDto>(Fx("goal.json"), ApiJson.Options)!;
        Assert.Equal("3cd80f66", g.Id);
        Assert.Equal(GoalState.Planning, g.Status);
        Assert.Equal(5.00m, g.BudgetUsd);
        Assert.Equal(0m, g.SpentUsd);
        Assert.False(g.NeedsManagerAttention);
        Assert.Null(g.LastDecision);
    }

    [Fact]
    public void Employee_fixture_parses()
    {
        var e = JsonSerializer.Deserialize<EmployeeDto>(Fx("employee.json"), ApiJson.Options)!;
        Assert.Equal("ada-coder", e.Id);
        Assert.Equal(Vendor.Claude, e.Vendor);
        Assert.Equal(EmployeeStatus.Asleep, e.Status);
        Assert.Equal(100, e.Energy);
    }

    [Fact]
    public void Events_and_lists_parse()
    {
        var page = JsonSerializer.Deserialize<EventPageDto>(Fx("events.json"), ApiJson.Options)!;
        Assert.True(page.Cursor > 0);
        Assert.Contains(page.Events, e => e.Type == "catalog.reloaded");
        Assert.Equal(4, JsonSerializer.Deserialize<List<EmployeeDto>>(Fx("employees.json"), ApiJson.Options)!.Count);
        Assert.Single(JsonSerializer.Deserialize<List<TaskDto>>(Fx("tasks.json"), ApiJson.Options)!);
        Assert.Single(JsonSerializer.Deserialize<List<GoalDto>>(Fx("goals.json"), ApiJson.Options)!);
        Assert.Equal("ok", JsonSerializer.Deserialize<HealthDto>(Fx("health.json"), ApiJson.Options)!.Status);
        Assert.Empty(JsonSerializer.Deserialize<RoomFilesDto>(Fx("room-files.json"), ApiJson.Options)!.Files);
    }

    // ---- errors ---------------------------------------------------------------------------

    [Fact]
    public async Task A_400_with_problem_details_becomes_an_ApiException_with_its_title()
    {
        var (client, _) = Foreman(Fx("problem-400.json"), HttpStatusCode.BadRequest);
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.CreateTaskAsync(new CreateTaskRequest("", "", "ada-coder")));
        Assert.Equal(400, ex.Status);
        Assert.Equal("One or more validation errors occurred.", ex.Title);
    }

    [Fact]
    public async Task A_404_with_an_empty_body_becomes_an_ApiException()
    {
        var (client, _) = Foreman("", HttpStatusCode.NotFound);
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetTaskAsync("nope"));
        Assert.Equal(404, ex.Status);
    }

    // ---- request shapes -------------------------------------------------------------------

    [Fact]
    public async Task Employee_calls_hit_the_right_paths()
    {
        var (client, stub) = Foreman(Fx("employees.json"));
        await client.GetEmployeesAsync();
        Assert.Equal("/employees", stub.LastPathAndQuery);

        stub.Body = Fx("employee.json");
        await client.GetEmployeeAsync("ada-coder");
        Assert.Equal("/employees/ada-coder", stub.LastPathAndQuery);

        stub.Body = ""; stub.Status = HttpStatusCode.NoContent;
        await client.WakeAsync("ada-coder", until: "23:00");
        Assert.Equal(HttpMethod.Post, stub.LastMethod);
        Assert.Equal("/employees/ada-coder/wake?until=23:00", stub.LastPathAndQuery);
        await client.SleepAsync("ada-coder");
        Assert.Equal("/employees/ada-coder/sleep", stub.LastPathAndQuery);
        await client.ResetAsync("ada-coder");
        Assert.Equal("/employees/ada-coder/reset", stub.LastPathAndQuery);
        await client.ReloadEmployeesAsync();
        Assert.Equal("/employees/reload", stub.LastPathAndQuery);
    }

    [Fact]
    public async Task Task_calls_hit_the_right_paths_and_bodies()
    {
        var (client, stub) = Foreman(Fx("task.json"), HttpStatusCode.Created);
        await client.CreateTaskAsync(new CreateTaskRequest("Build it", "Do the thing", "ada-coder", RequiresApproval: true));
        Assert.Equal(HttpMethod.Post, stub.LastMethod);
        Assert.Equal("/tasks", stub.LastPathAndQuery);
        Assert.Contains("\"title\":\"Build it\"", stub.LastBody);
        Assert.Contains("\"requiresApproval\":true", stub.LastBody);

        stub.Status = HttpStatusCode.OK; stub.Body = Fx("tasks.json");
        await client.GetTasksAsync(TaskState.Queued, "ada-coder");
        Assert.Equal("/tasks?status=Queued&assignee=ada-coder", stub.LastPathAndQuery);

        stub.Body = Fx("task.json");
        await client.AnswerAsync("6309ec5a", "use JSON");
        Assert.Equal("/tasks/6309ec5a/answer", stub.LastPathAndQuery);
        Assert.Contains("\"text\":\"use JSON\"", stub.LastBody);
        await client.ReassignAsync("6309ec5a", "rex-reviewer");
        Assert.Equal("/tasks/6309ec5a/reassign", stub.LastPathAndQuery);
        Assert.Contains("\"assignee\":\"rex-reviewer\"", stub.LastBody);
        await client.ApproveAsync("6309ec5a");
        Assert.Equal("/tasks/6309ec5a/approve", stub.LastPathAndQuery);
        await client.RetryAsync("6309ec5a");
        Assert.Equal("/tasks/6309ec5a/retry", stub.LastPathAndQuery);
        await client.CancelTaskAsync("6309ec5a");
        Assert.Equal("/tasks/6309ec5a/cancel", stub.LastPathAndQuery);
    }

    [Fact]
    public async Task Goal_and_event_calls_hit_the_right_paths()
    {
        var (client, stub) = Foreman(Fx("goal.json"), HttpStatusCode.Created);
        await client.CreateGoalAsync(new CreateGoalRequest("Ship it", "All of it", "mia-manager", 5.00m));
        Assert.Equal("/goals", stub.LastPathAndQuery);
        Assert.Contains("\"budgetUsd\":5.00", stub.LastBody);

        stub.Status = HttpStatusCode.OK;
        await client.TopUpAsync("3cd80f66", 2.50m);
        Assert.Equal("/goals/3cd80f66/topup", stub.LastPathAndQuery);
        Assert.Contains("\"addUsd\":2.50", stub.LastBody);
        await client.CancelGoalAsync("3cd80f66");
        Assert.Equal("/goals/3cd80f66/cancel", stub.LastPathAndQuery);

        stub.Body = Fx("events.json");
        await client.GetEventsAsync(since: 22, wait: 30, limit: 200);
        Assert.Equal("/events?since=22&wait=30&limit=200", stub.LastPathAndQuery);
    }

    [Fact]
    public async Task Context_api_client_reads_the_brief_and_files()
    {
        var stub = new StubHandler { Body = Fx("room-brief.txt"), ContentType = "text/plain" };
        var client = new ContextApiClient(new HttpClient(stub) { BaseAddress = new Uri("http://ctx.test") });

        var brief = await client.GetBriefAsync("task-6309ec5a");
        Assert.Equal("/rooms/task-6309ec5a/context?format=text", stub.LastPathAndQuery);
        Assert.Contains("Agency room", brief);

        stub.Body = Fx("room-files.json"); stub.ContentType = "application/json";
        var files = await client.ListFilesAsync("task-6309ec5a");
        Assert.Equal("/rooms/task-6309ec5a/files", stub.LastPathAndQuery);
        Assert.Empty(files.Files);
    }
}

public class HiringClientTests
{
    private static (ForemanClient Client, StubHandler Stub) Foreman(string body = "{}", HttpStatusCode status = HttpStatusCode.OK)
    {
        var stub = new StubHandler { Body = body, Status = status };
        return (new ForemanClient(new HttpClient(stub) { BaseAddress = new Uri("http://foreman.test") }), stub);
    }

    [Fact]
    public async Task Get_hiring_parses_templates_and_brain_costs()
    {
        const string body = """{"templates":[{"id":"engineer","role":"Software engineer","description":"Builds things","brains":[{"model":"claude-haiku-4-5-20251001","vendor":0,"label":"Claude Haiku 4.5","usdPerRun":0.10,"usdPerDay":0.60},{"model":"gpt-5-codex","vendor":1,"label":"GPT-5 Codex","usdPerRun":0.16,"usdPerDay":0.93}]}],"brains":[{"model":"claude-haiku-4-5-20251001","vendor":0,"label":"Claude Haiku 4.5"}]}""";
        var (client, stub) = Foreman(body);

        var hiring = await client.GetHiringAsync();

        Assert.Equal(HttpMethod.Get, stub.LastMethod);
        Assert.Equal("/hiring", stub.LastPathAndQuery);
        var engineer = Assert.Single(hiring.Templates);
        Assert.Equal("Software engineer", engineer.Role);
        Assert.Equal(2, engineer.Brains.Count);
        Assert.Equal(Vendor.Codex, engineer.Brains[1].Vendor);
        Assert.Equal(0.60m, engineer.Brains[0].UsdPerDay);
    }

    [Fact]
    public async Task Hire_posts_the_request_and_fire_posts_to_the_employee()
    {
        var (client, stub) = Foreman("""{"id":"sam-engineer","name":"Sam","role":"Software engineer","vendor":0,"model":"claude-haiku-4-5-20251001","status":0}""", HttpStatusCode.Created);
        var hired = await client.HireAsync(new HireRequest("engineer", "claude-haiku-4-5-20251001", "Sam"));
        Assert.Equal(HttpMethod.Post, stub.LastMethod);
        Assert.Equal("/hiring", stub.LastPathAndQuery);
        Assert.Contains("\"templateId\":\"engineer\"", stub.LastBody);
        Assert.Contains("\"name\":\"Sam\"", stub.LastBody);
        Assert.Equal("sam-engineer", hired.Id);

        var (client2, stub2) = Foreman("", HttpStatusCode.NoContent);
        await client2.FireAsync("sam-engineer");
        Assert.Equal(HttpMethod.Post, stub2.LastMethod);
        Assert.Equal("/employees/sam-engineer/fire", stub2.LastPathAndQuery);
    }
}

public class TicketClientTests
{
    private static (ForemanClient Client, StubHandler Stub) Foreman(string body = "{}", HttpStatusCode status = HttpStatusCode.OK)
    {
        var stub = new StubHandler { Body = body, Status = status };
        return (new ForemanClient(new HttpClient(stub) { BaseAddress = new Uri("http://foreman.test") }), stub);
    }

    [Fact]
    public async Task Create_ticket_posts_title_brief_and_role()
    {
        var (client, stub) = Foreman("""{"id":"t9","title":"Fix the parser","brief":"b","assignee":"","role":"Software engineer","status":0,"room":"task-t9"}""", HttpStatusCode.Created);
        var ticket = await client.CreateTicketAsync(new CreateTicketRequest("Fix the parser", "b", "Software engineer"));
        Assert.Equal(HttpMethod.Post, stub.LastMethod);
        Assert.Equal("/tickets", stub.LastPathAndQuery);
        Assert.Contains("\"role\":\"Software engineer\"", stub.LastBody);
        Assert.Equal("", ticket.Assignee);
        Assert.Equal("Software engineer", ticket.Role);
    }

    [Fact]
    public async Task Get_tickets_lists_the_board()
    {
        var (client, stub) = Foreman("""[{"id":"t9","title":"Fix the parser","brief":"b","assignee":"","role":null,"status":0,"room":"task-t9"}]""");
        var tickets = await client.GetTicketsAsync();
        Assert.Equal("/tickets", stub.LastPathAndQuery);
        Assert.Null(Assert.Single(tickets).Role);
    }
}

public class ManagerTicketClientTests
{
    [Fact]
    public async Task A_ticket_can_carry_a_budget()
    {
        var stub = new StubHandler { Body = """{"id":"t9","title":"Big","brief":"b","assignee":"","role":"Engineering manager","budgetUsd":8,"status":0,"room":"task-t9"}""", Status = HttpStatusCode.Created };
        var client = new ForemanClient(new HttpClient(stub) { BaseAddress = new Uri("http://foreman.test") });
        var ticket = await client.CreateTicketAsync(new CreateTicketRequest("Big", "b", "Engineering manager", BudgetUsd: 8m));
        Assert.Contains("\"budgetUsd\":8", stub.LastBody);
        Assert.Equal(8m, ticket.BudgetUsd);
    }
}
