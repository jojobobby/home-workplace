using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;

namespace HomeWorkplace.ContextApi.Tests;

public class LongPollTests
{
    [Fact]
    public async Task A_long_poll_is_released_by_a_concurrent_post()
    {
        using var factory = new ChatApiFactory();
        using var reader = factory.CreateClient();
        using var writer = factory.CreateClient();

        var pending = reader.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=0&wait=20", TestJson.Options);

        // Give the reader time to park on the waiter before writing.
        await Task.Delay(500);

        await writer.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "codex-1", name = "Codex", goal = "Write the tests", content = "woke you up",
        });

        var read = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        var message = Assert.Single(read!.Messages);
        Assert.Equal("woke you up", message.Content);
    }

    [Fact]
    public async Task A_long_poll_with_no_writer_times_out_with_an_empty_ok()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/rooms/quiet/messages?since=0&wait=2");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await response.Content.ReadFromJsonAsync<RoomReadResponse>(TestJson.Options);
        Assert.Empty(read!.Messages);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(1.5),
            $"expected the request to block, but it returned in {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task A_long_poll_returns_immediately_when_messages_already_exist()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "codex-1", name = "Codex", goal = "Write the tests", content = "already here",
        });

        var stopwatch = Stopwatch.StartNew();
        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=0&wait=30", TestJson.Options);
        stopwatch.Stop();

        Assert.Single(read!.Messages);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"expected an immediate return, took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Wait_above_the_maximum_is_clamped_not_rejected()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxWaitSeconds", "2"));
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/rooms/quiet/messages?since=0&wait=600");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"expected the wait to be clamped to 2s, took {stopwatch.Elapsed}.");
    }
}
