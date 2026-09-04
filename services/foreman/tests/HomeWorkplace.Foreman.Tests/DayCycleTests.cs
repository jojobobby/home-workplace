using System.Net.Http.Json;
using HomeWorkplace.Foreman;
using Microsoft.Extensions.Time.Testing;

namespace HomeWorkplace.Foreman.Tests;

public class DayCycleTests
{
    private static void Write(string dp, string id, string wake, string sleep)
    {
        var dir = Path.Combine(dp, "employees", id); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "employee.json"),
          $$$"""{"id":"{{{id}}}","name":"{{{id}}}","role":"r","vendor":"claude","model":"m","claudeAllowedTools":["Read"],"schedule":{"wake":"{{{wake}}}","sleep":"{{{sleep}}}"}}""");
        File.WriteAllText(Path.Combine(dir, "skills.md"), "s"); File.WriteAllText(Path.Combine(dir, "life.md"), "l");
    }
    private static async Task<TaskModel> PollTask(HttpClient c, string id, Func<TaskModel, bool> p, int s = 10)
    { var end = DateTime.UtcNow.AddSeconds(s); while (DateTime.UtcNow < end) { var t = await c.GetFromJsonAsync<TaskModel>($"/tasks/{id}", TestJson.Options); if (t is not null && p(t)) return t; await Task.Delay(50); } throw new Xunit.Sdk.XunitException("timeout"); }
    private static async Task<EmployeeView> PollEmp(HttpClient c, string id, Func<EmployeeView, bool> p, int s = 10)
    { var end = DateTime.UtcNow.AddSeconds(s); while (DateTime.UtcNow < end) { var e = await c.GetFromJsonAsync<EmployeeView>($"/employees/{id}", TestJson.Options); if (e is not null && p(e)) return e; await Task.Delay(50); } throw new Xunit.Sdk.XunitException("timeout"); }

    [Fact]
    public async Task Reset_writes_progress_bullets_and_clears_the_session_but_stays_awake()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        using var factory = ForemanFactory.Create(out var dp, clock);
        Write(dp, "ada", "09:00", "20:00");
        factory.Provider.Enqueue(s => new RunResult
        {
            RunId = s.RunId, Status = RunOutcome.NeedsHuman, Summary = "q?", Ask = null,
            Artifacts = Array.Empty<string>(), SessionId = "sess-1",
            Usage = new Usage(1, null, null, null, null), RawTail = "",
        });
        factory.Provider.EnqueueWrapUp(new[] { "wrote the parser", "added tests" }, new[] { "wire up CLI" });
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks", new { title = "T", brief = "b", assignee = "ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        await PollTask(c, t!.Id, x => x.Status == TaskState.NeedsHuman);

        await c.PostAsync("/employees/ada/reset", null);

        var after = await c.GetFromJsonAsync<TaskModel>($"/tasks/{t.Id}", TestJson.Options);
        var ledger = Assert.Single(after!.Progress);
        Assert.Equal(new[] { "wrote the parser", "added tests" }, ledger.Done);
        Assert.Null(after.Session);
        Assert.Equal(EmployeeStatus.Awake, (await c.GetFromJsonAsync<EmployeeView>("/employees/ada", TestJson.Options))!.Status);
    }

    [Fact]
    public async Task At_sleep_time_the_scheduler_puts_an_idle_employee_to_sleep()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 19, 59, 0, TimeSpan.Zero));
        using var factory = ForemanFactory.Create(out var dp, clock);
        Write(dp, "ada", "09:00", "20:00");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null);
        Assert.Equal(EmployeeStatus.Awake, (await c.GetFromJsonAsync<EmployeeView>("/employees/ada", TestJson.Options))!.Status);

        clock.Advance(TimeSpan.FromMinutes(2));
        await PollEmp(c, "ada", e => e.Status == EmployeeStatus.Asleep);
    }

    [Fact]
    public async Task Wake_with_until_keeps_an_employee_awake_past_its_sleep_time()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 21, 0, 0, TimeSpan.Zero));
        using var factory = ForemanFactory.Create(out var dp, clock);
        Write(dp, "ada", "09:00", "20:00");
        using var c = factory.CreateClient();

        await c.PostAsync("/employees/ada/wake?until=23:00", null);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(EmployeeStatus.Awake, (await c.GetFromJsonAsync<EmployeeView>("/employees/ada", TestJson.Options))!.Status);
    }
}
