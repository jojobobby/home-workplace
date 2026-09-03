using System.Net.Http.Json;

namespace AgencyTogether.Api.Tests;

public class EvictionTests
{
    private static async Task PostAsync(HttpClient client, string content)
    {
        var response = await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1", name = "Claude", goal = "Ship it", content,
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Exceeding_the_retention_cap_drops_the_oldest_messages()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxMessagesPerRoom", "3"));
        using var client = factory.CreateClient();

        foreach (var content in new[] { "one", "two", "three", "four", "five" })
        {
            await PostAsync(client, content);
        }

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal(new[] { "three", "four", "five" }, read!.Messages.Select(m => m.Content));
    }

    [Fact]
    public async Task Sequence_numbers_stay_monotonic_across_eviction()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxMessagesPerRoom", "3"));
        using var client = factory.CreateClient();

        foreach (var content in new[] { "one", "two", "three", "four", "five" })
        {
            await PostAsync(client, content);
        }

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal(new long[] { 3, 4, 5 }, read!.Messages.Select(m => m.Seq));
        Assert.Equal(5, read.Cursor);
    }

    [Fact]
    public async Task A_cursor_older_than_retention_is_reported_as_truncated()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxMessagesPerRoom", "3"));
        using var client = factory.CreateClient();

        foreach (var content in new[] { "one", "two", "three", "four", "five" })
        {
            await PostAsync(client, content);
        }

        var stale = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=0", TestJson.Options);
        Assert.True(stale!.Truncated);

        var current = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=2", TestJson.Options);
        Assert.False(current!.Truncated);
    }
}
