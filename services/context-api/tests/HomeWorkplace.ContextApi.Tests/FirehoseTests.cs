using System.Net.Http.Json;

namespace HomeWorkplace.ContextApi.Tests;

public class FirehoseTests
{
    private static async Task PostAsync(HttpClient client, string room, string id, string content)
    {
        var response = await client.PostAsJsonAsync($"/rooms/{room}/messages", new
        {
            id, name = id, goal = "Ship it", content,
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_firehose_merges_rooms_in_global_sequence_order()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "alpha one");
        await PostAsync(client, "beta", "codex-1", "beta one");
        await PostAsync(client, "alpha", "claude-1", "alpha two");

        var read = await client.GetFromJsonAsync<FirehoseResponse>("/firehose", TestJson.Options);

        Assert.Equal(
            new[] { "alpha one", "beta one", "alpha two" },
            read!.Messages.Select(m => m.Content));
        Assert.Equal(new[] { "alpha", "beta", "alpha" }, read.Messages.Select(m => m.Room));
        Assert.Equal(new long[] { 1, 2, 3 }, read.Messages.Select(m => m.GlobalSeq));
        Assert.Equal(3, read.Cursor);
    }

    [Fact]
    public async Task The_firehose_since_cursor_is_the_global_sequence()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "alpha one");
        await PostAsync(client, "beta", "codex-1", "beta one");

        var read = await client.GetFromJsonAsync<FirehoseResponse>(
            "/firehose?since=1", TestJson.Options);

        var message = Assert.Single(read!.Messages);
        Assert.Equal("beta one", message.Content);
    }

    [Fact]
    public async Task Per_room_seq_restarts_per_room_while_global_seq_does_not()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "alpha one");
        await PostAsync(client, "beta", "codex-1", "beta one");

        var read = await client.GetFromJsonAsync<FirehoseResponse>("/firehose", TestJson.Options);

        Assert.Equal(new long[] { 1, 1 }, read!.Messages.Select(m => m.Seq));
        Assert.Equal(new long[] { 1, 2 }, read.Messages.Select(m => m.GlobalSeq));
    }

    [Fact]
    public async Task A_firehose_long_poll_is_released_by_a_post_to_any_room()
    {
        using var factory = new ChatApiFactory();
        using var reader = factory.CreateClient();
        using var writer = factory.CreateClient();

        var pending = reader.GetFromJsonAsync<FirehoseResponse>(
            "/firehose?since=0&wait=20", TestJson.Options);

        await Task.Delay(500);
        await PostAsync(writer, "somewhere", "codex-1", "anywhere will do");

        var read = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        var message = Assert.Single(read!.Messages);
        Assert.Equal("anywhere will do", message.Content);
        Assert.Equal("somewhere", message.Room);
    }

    [Fact]
    public async Task A_cursor_older_than_the_ring_is_reported_as_truncated()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:FirehoseCapacity", "2"));
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "one");
        await PostAsync(client, "alpha", "claude-1", "two");
        await PostAsync(client, "alpha", "claude-1", "three");

        var read = await client.GetFromJsonAsync<FirehoseResponse>(
            "/firehose?since=0", TestJson.Options);

        Assert.True(read!.Truncated);
        Assert.Equal(new[] { "two", "three" }, read.Messages.Select(m => m.Content));
    }
}
