using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class RestartTests
{
    private const string AdaJson = """
    {"id":"ada","name":"ada","role":"r","vendor":"claude","model":"m","claudeAllowedTools":["Read"],"schedule":{"wake":"09:00","sleep":"20:00"}}
    """;
    private static void WriteAda(string dp)
    {
        var d = Path.Combine(dp, "employees", "ada"); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "employee.json"), AdaJson);
        File.WriteAllText(Path.Combine(d, "skills.md"), "s");
        File.WriteAllText(Path.Combine(d, "life.md"), "l");
    }

    [Fact]
    public async Task Tasks_state_and_event_cursor_survive_a_restart_and_running_becomes_queued()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "foreman-restart", Guid.NewGuid().ToString("N"));
        var employeesPath = Path.Combine(dataPath, "employees");
        Directory.CreateDirectory(employeesPath);
        WriteAda(dataPath);
        string taskId; long cursor;

        // The run blocks and is never released — it simulates a crash mid-run. MarkRunning
        // persists "Running" before the provider call, so the task is on disk as Running
        // without the run ever completing (which would overwrite it with Done).
        var neverReleased = new TaskCompletionSource();
        await using (var f1 = ForemanFactory.Existing(dataPath, employeesPath,
            provider: p => p.Enqueue(s => { neverReleased.Task.Wait(); return FakeAgentProvider.Done(s); })))
        {
            using var c1 = f1.CreateClient();
            await c1.PostAsync("/employees/ada/wake", null);
            var t = await (await c1.PostAsJsonAsync("/tasks", new { title = "T", brief = "b", assignee = "ada" }))
                .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
            taskId = t!.Id;
            var end = DateTime.UtcNow.AddSeconds(5);
            while ((await c1.GetFromJsonAsync<TaskModel>($"/tasks/{taskId}", TestJson.Options))!.Status != TaskState.Running && DateTime.UtcNow < end)
                await Task.Delay(50);
            cursor = (await c1.GetFromJsonAsync<EventPage>("/events?since=0", TestJson.Options))!.Cursor;
        }

        await using var f2 = ForemanFactory.Existing(dataPath, employeesPath);
        using var c2 = f2.CreateClient();
        var recovered = await c2.GetFromJsonAsync<TaskModel>($"/tasks/{taskId}", TestJson.Options);
        Assert.Equal(TaskState.Queued, recovered!.Status);
        var page = await c2.GetFromJsonAsync<EventPage>("/events?since=0", TestJson.Options);
        Assert.True(page!.Cursor >= cursor);
        try { Directory.Delete(dataPath, true); } catch { }
    }
}
