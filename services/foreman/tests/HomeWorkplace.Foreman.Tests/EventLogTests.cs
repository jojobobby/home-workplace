using System.Net.Http.Json;
using HomeWorkplace.Foreman;
using Microsoft.Extensions.Time.Testing;

namespace HomeWorkplace.Foreman.Tests;

public class EventLogTests
{
    private static EventLog NewLog(int capacity = 100)
        => new(new ForemanOptions { EventsCapacity = capacity }, new FakeTimeProvider());

    [Fact]
    public void Emit_then_read_returns_events_after_the_cursor()
    {
        var log = NewLog();
        log.Emit("task.state", taskId: "t1");
        log.Emit("run.started", taskId: "t1", runId: "r1");

        var page = log.Read(since: 1, limit: 100);

        var only = Assert.Single(page.Events);
        Assert.Equal("run.started", only.Type);
        Assert.Equal(2, page.Cursor);
        Assert.False(page.Truncated);
    }

    [Fact]
    public void A_cursor_older_than_the_ring_is_truncated()
    {
        var log = NewLog(capacity: 2);
        log.Emit("a"); log.Emit("b"); log.Emit("c");

        var page = log.Read(since: 0, limit: 100);

        Assert.True(page.Truncated);
        Assert.Equal(new[] { "b", "c" }, page.Events.Select(e => e.Type));
    }

    [Fact]
    public async Task A_long_poll_is_released_by_a_concurrent_emit()
    {
        var log = NewLog();
        var pending = log.ReadWithWaitAsync(0, 100, TimeSpan.FromSeconds(20), CancellationToken.None);
        await Task.Delay(200);
        log.Emit("woke.up");

        var page = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("woke.up", Assert.Single(page.Events).Type);
    }

    [Fact]
    public async Task Events_endpoint_streams_over_http()
    {
        using var factory = ForemanFactory.Create(out _);
        using var client = factory.CreateClient();

        var page = await client.GetFromJsonAsync<EventPage>("/events?since=0", TestJson.Options);

        Assert.NotNull(page);
        Assert.False(page!.Truncated);
    }
}
