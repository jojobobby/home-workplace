using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class TaskLifecycleTests
{
    private const string AdaJson = """
    {"id":"ada-coder","name":"Ada","role":"Engineer","vendor":"claude","model":"m",
     "claudeAllowedTools":["Read"],"schedule":{"wake":"09:00","sleep":"20:00"}}
    """;

    private static void WriteAda(string dataPath)
    {
        var dir = Path.Combine(dataPath, "employees", "ada-coder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "employee.json"), AdaJson);
        File.WriteAllText(Path.Combine(dir, "skills.md"), "s");
        File.WriteAllText(Path.Combine(dir, "life.md"), "l");
    }

    [Fact]
    public async Task Creating_a_task_returns_it_queued_and_announces_it_in_a_room()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteAda(dataPath);
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/tasks",
            new { title = "Build parser", brief = "Write a JSON parser", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        Assert.Equal(TaskState.Queued, created!.Status);
        Assert.Equal("ada-coder", created.Assignee);
        Assert.Equal($"task-{created.Id}", created.Room);
        Assert.Contains(factory.ContextApi.Posts, p => p.Room == created.Room && p.Content.Contains("Build parser"));
    }

    [Fact]
    public async Task Creating_a_task_for_an_unknown_employee_is_400()
    {
        using var factory = ForemanFactory.Create(out _);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tasks",
            new { title = "x", brief = "y", assignee = "ghost" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_and_list_return_created_tasks()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteAda(dataPath);
        using var client = factory.CreateClient();
        var created = await (await client.PostAsJsonAsync("/tasks",
            new { title = "t", brief = "b", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var fetched = await client.GetFromJsonAsync<TaskModel>($"/tasks/{created!.Id}", TestJson.Options);
        var listed = await client.GetFromJsonAsync<List<TaskModel>>("/tasks?assignee=ada-coder", TestJson.Options);

        Assert.Equal(created.Id, fetched!.Id);
        Assert.Single(listed!);
    }
}
