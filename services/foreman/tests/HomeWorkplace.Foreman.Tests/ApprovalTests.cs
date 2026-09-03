using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class ApprovalTests
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
    private static async Task<TaskModel> Poll(HttpClient c, string id, Func<TaskModel, bool> p, int s = 5)
    { var end = DateTime.UtcNow.AddSeconds(s); while (DateTime.UtcNow < end) { var t = await c.GetFromJsonAsync<TaskModel>($"/tasks/{id}", TestJson.Options); if (t is not null && p(t)) return t; await Task.Delay(50); } throw new Xunit.Sdk.XunitException("timeout"); }

    [Fact]
    public async Task A_task_that_requires_approval_parks_then_approves_to_done()
    {
        using var factory = ForemanFactory.Create(out var dp); WriteAda(dp);
        factory.Provider.EnqueueDone("built");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada-coder/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks",
            new { title = "X", brief = "y", assignee = "ada-coder", requiresApproval = true }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var parked = await Poll(c, t!.Id, x => x.Status == TaskState.NeedsHuman);
        Assert.True(parked.AwaitingApproval);

        var ok = await c.PostAsync($"/tasks/{t.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(TaskState.Done, (await c.GetFromJsonAsync<TaskModel>($"/tasks/{t.Id}", TestJson.Options))!.Status);
    }

    [Fact]
    public async Task Approving_a_task_not_awaiting_approval_is_409()
    {
        using var factory = ForemanFactory.Create(out var dp); WriteAda(dp);
        var gate = new TaskCompletionSource();
        factory.Provider.Enqueue(s => { gate.Task.Wait(); return FakeAgentProvider.Done(s); });
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada-coder/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks", new { title = "X", brief = "y", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        await Poll(c, t!.Id, x => x.Status == TaskState.Running);

        var resp = await c.PostAsync($"/tasks/{t.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        gate.SetResult();
    }

    [Fact]
    public async Task A_needs_human_result_parks_and_an_answer_resumes_the_run()
    {
        using var factory = ForemanFactory.Create(out var dp); WriteAda(dp);
        factory.Provider.Enqueue(s => new RunResult
        {
            RunId = s.RunId, Status = RunOutcome.NeedsHuman, Summary = "which format?", Ask = null,
            Artifacts = Array.Empty<string>(), SessionId = s.SessionId ?? Guid.NewGuid().ToString(),
            Usage = new Usage(1, null, null, null, null), RawTail = "",
        });
        factory.Provider.EnqueueDone("used JSON");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada-coder/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks", new { title = "X", brief = "y", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var parked = await Poll(c, t!.Id, x => x.Status == TaskState.NeedsHuman);
        Assert.False(parked.AwaitingApproval);

        await c.PostAsJsonAsync($"/tasks/{t.Id}/answer", new { text = "use JSON" });
        var done = await Poll(c, t.Id, x => x.Status == TaskState.Done);
        Assert.Equal(2, done.Runs.Count);
        Assert.Equal(SessionMode.Resume, factory.Provider.Specs[1].Mode);
        Assert.Contains("use JSON", factory.Provider.Specs[1].Prompt);
    }
}
