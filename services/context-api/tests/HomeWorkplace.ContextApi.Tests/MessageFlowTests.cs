using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HomeWorkplace.ContextApi.Tests;

public class MessageFlowTests
{
    [Fact]
    public async Task Posted_message_round_trips_with_all_four_fields()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var post = await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Design the endpoint layer",
            content = "Endpoints are mapped; starting on the store.",
        });

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.NotNull(read);
        var message = Assert.Single(read!.Messages);
        Assert.Equal("claude-1", message.AgentId);
        Assert.Equal("Claude", message.Name);
        Assert.Equal("Design the endpoint layer", message.Goal);
        Assert.Equal("Endpoints are mapped; starting on the store.", message.Content);
        Assert.Equal("alpha", message.Room);
        Assert.Equal(1, message.Seq);
    }

    [Fact]
    public async Task Post_without_a_room_lands_in_global()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/messages", new
        {
            id = "codex-1",
            name = "Codex",
            goal = "Write the tests",
            content = "Picking up the test project.",
        });

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/global/messages", TestJson.Options);

        var message = Assert.Single(read!.Messages);
        Assert.Equal("global", message.Room);
        Assert.Equal("codex-1", message.AgentId);
    }

    [Fact]
    public async Task Reading_a_room_nobody_has_posted_to_returns_empty_ok()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/rooms/nobody-here/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<RoomReadResponse>(
            await response.Content.ReadAsStringAsync(), TestJson.Options);
        Assert.Empty(body!.Messages);
        Assert.Empty(body.Agents);
        Assert.Equal(0, body.Cursor);
        Assert.False(body.Truncated);
    }

    [Fact]
    public async Task Post_response_carries_the_posted_message_and_cursor()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Ship it",
            content = "First.",
        });

        var body = JsonSerializer.Deserialize<PostMessageResponse>(
            await response.Content.ReadAsStringAsync(), TestJson.Options);

        Assert.Equal("alpha", body!.Room);
        Assert.Equal(1, body.Posted.Seq);
        Assert.Equal(1, body.Cursor);
        Assert.Empty(body.Messages);
        Assert.False(body.Truncated);
    }
}
