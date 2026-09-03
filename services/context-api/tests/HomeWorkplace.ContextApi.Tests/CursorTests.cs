using System.Net.Http.Json;
using System.Text.Json;

namespace HomeWorkplace.ContextApi.Tests;

public class CursorTests
{
    private static async Task PostAsync(HttpClient client, string room, string content)
    {
        var response = await client.PostAsJsonAsync($"/rooms/{room}/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Ship it",
            content,
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Since_returns_only_messages_after_the_cursor()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "one");
        await PostAsync(client, "alpha", "two");
        await PostAsync(client, "alpha", "three");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=1", TestJson.Options);

        Assert.Equal(new[] { "two", "three" }, read!.Messages.Select(m => m.Content));
        Assert.Equal(3, read.Cursor);
    }

    [Fact]
    public async Task Limit_caps_the_page_size()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "one");
        await PostAsync(client, "alpha", "two");
        await PostAsync(client, "alpha", "three");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?limit=2", TestJson.Options);

        Assert.Equal(new[] { "one", "two" }, read!.Messages.Select(m => m.Content));
        Assert.Equal(3, read.Cursor);
    }

    [Fact]
    public async Task Limit_above_the_maximum_is_clamped_not_rejected()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "one");

        var response = await client.GetAsync("/rooms/alpha/messages?limit=99999");

        response.EnsureSuccessStatusCode();
        var read = JsonSerializer.Deserialize<RoomReadResponse>(
            await response.Content.ReadAsStringAsync(), TestJson.Options);
        Assert.Single(read!.Messages);
    }

    [Fact]
    public async Task Post_with_since_returns_the_catch_up_including_its_own_message()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "from codex");

        var response = await client.PostAsJsonAsync("/rooms/alpha/messages?since=0", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Ship it",
            content = "from claude",
        });

        var body = JsonSerializer.Deserialize<PostMessageResponse>(
            await response.Content.ReadAsStringAsync(), TestJson.Options);

        Assert.Equal(new[] { "from codex", "from claude" }, body!.Messages.Select(m => m.Content));
        Assert.Equal(2, body.Cursor);
        Assert.Equal(2, body.Posted.Seq);
    }
}
