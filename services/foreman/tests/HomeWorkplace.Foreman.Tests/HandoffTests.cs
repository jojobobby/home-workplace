using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class HandoffTests
{
    private static void Write(string dp, string id, string vendor = "claude")
    {
        var dir = Path.Combine(dp, "employees", id); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "employee.json"),
          $$$"""{"id":"{{{id}}}","name":"{{{id}}}","role":"r","vendor":"{{{vendor}}}","model":"m","claudeAllowedTools":["Read"],"schedule":{"wake":"09:00","sleep":"20:00"}}""");
        File.WriteAllText(Path.Combine(dir, "skills.md"), "s"); File.WriteAllText(Path.Combine(dir, "life.md"), "l");
    }
    private static async Task<TaskModel> Poll(HttpClient c, string id, Func<TaskModel, bool> p, int s = 8)
    { var end = DateTime.UtcNow.AddSeconds(s); while (DateTime.UtcNow < end) { var t = await c.GetFromJsonAsync<TaskModel>($"/tasks/{id}", TestJson.Options); if (t is not null && p(t)) return t; await Task.Delay(50); } throw new Xunit.Sdk.XunitException("timeout"); }

    [Fact]
    public async Task Parent_asks_child_answers_parent_resumes_same_session_with_the_answer()
    {
        using var factory = ForemanFactory.Create(out var dp);
        Write(dp, "ada"); Write(dp, "rex");
        factory.Provider.EnqueueHandoff("rex", "What's the schema?");
        factory.Provider.EnqueueDone("done with schema");
        factory.Provider.EnqueueDone("parent finished");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null);
        await c.PostAsync("/employees/rex/wake", null);

        var parent = await (await c.PostAsJsonAsync("/tasks", new { title = "P", brief = "parent", assignee = "ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var finished = await Poll(c, parent!.Id, t => t.Status == TaskState.Done);
        Assert.Single(finished.ChildIds);
        var adaResume = factory.Provider.Specs.Last(s => s.TaskId == parent.Id);
        Assert.Equal(SessionMode.Resume, adaResume.Mode);
        Assert.Contains("done with schema", adaResume.Prompt);
    }

    [Fact]
    public async Task Parent_is_Waiting_while_the_child_runs()
    {
        using var factory = ForemanFactory.Create(out var dp);
        Write(dp, "ada"); Write(dp, "rex");
        var childGate = new TaskCompletionSource();
        factory.Provider.EnqueueHandoff("rex", "q");
        factory.Provider.Enqueue(s => { childGate.Task.Wait(); return FakeAgentProvider.Done(s, "child done"); });
        factory.Provider.EnqueueDone();
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null); await c.PostAsync("/employees/rex/wake", null);
        var parent = await (await c.PostAsJsonAsync("/tasks", new { title = "P", brief = "p", assignee = "ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        await Poll(c, parent!.Id, t => t.Status == TaskState.Waiting);
        childGate.SetResult();
        await Poll(c, parent.Id, t => t.Status == TaskState.Done);
    }

    [Fact]
    public async Task An_unknown_handoff_target_degrades_to_needs_human()
    {
        using var factory = ForemanFactory.Create(out var dp);
        Write(dp, "ada");
        factory.Provider.EnqueueHandoff("nobody", "help?");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks", new { title = "P", brief = "p", assignee = "ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var parked = await Poll(c, t!.Id, x => x.Status == TaskState.NeedsHuman);
        Assert.False(parked.AwaitingApproval);
        Assert.Contains("help?", parked.PendingQuestion);
    }
}
