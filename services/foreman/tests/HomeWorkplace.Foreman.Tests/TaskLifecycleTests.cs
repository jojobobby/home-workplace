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

    private static async Task<TaskModel> PollUntil(HttpClient client, string id, Func<TaskModel, bool> done, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var t = await client.GetFromJsonAsync<TaskModel>($"/tasks/{id}", TestJson.Options);
            if (t is not null && done(t)) return t;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("task did not reach the expected state in time");
    }

    [Fact]
    public async Task An_awake_employee_runs_a_created_task_to_done()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteAda(dataPath);
        factory.Provider.EnqueueDone("parser shipped");
        using var client = factory.CreateClient();
        await client.PostAsync("/employees/ada-coder/wake", null);

        var created = await (await client.PostAsJsonAsync("/tasks",
            new { title = "Build parser", brief = "Write it", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var done = await PollUntil(client, created!.Id, t => t.Status == TaskState.Done);
        Assert.Single(done.Runs);
        Assert.Equal("parser shipped", done.Runs[0].ResultSummary);
    }

    [Fact]
    public async Task The_run_spec_carries_persona_brief_and_room_context()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteAda(dataPath);
        factory.Provider.EnqueueDone();
        using var client = factory.CreateClient();
        await client.PostAsync("/employees/ada-coder/wake", null);
        var created = await (await client.PostAsJsonAsync("/tasks",
            new { title = "Build parser", brief = "Write a JSON parser", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        await PollUntil(client, created!.Id, t => t.Status == TaskState.Done);
        var spec = Assert.Single(factory.Provider.Specs);
        Assert.Contains("Ada", spec.SystemPrompt);
        Assert.Contains("Write a JSON parser", spec.Prompt);
        Assert.Equal(SessionMode.New, spec.Mode);
    }

    [Fact]
    public async Task Only_one_run_per_employee_at_a_time_the_second_task_queues_then_runs()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteAda(dataPath);
        var gate = new TaskCompletionSource();
        factory.Provider.Enqueue(s => { gate.Task.Wait(); return FakeAgentProvider.Done(s, "first"); });
        factory.Provider.EnqueueDone("second");
        using var client = factory.CreateClient();
        await client.PostAsync("/employees/ada-coder/wake", null);

        var a = await (await client.PostAsJsonAsync("/tasks", new { title = "A", brief = "a", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var b = await (await client.PostAsJsonAsync("/tasks", new { title = "B", brief = "b", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        await Task.Delay(300);
        var bMid = await client.GetFromJsonAsync<TaskModel>($"/tasks/{b!.Id}", TestJson.Options);
        Assert.Equal(TaskState.Queued, bMid!.Status);
        gate.SetResult();
        await PollUntil(client, b.Id, t => t.Status == TaskState.Done);
    }
}
