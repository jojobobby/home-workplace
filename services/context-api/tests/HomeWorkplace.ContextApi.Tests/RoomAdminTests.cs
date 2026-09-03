using System.Net;
using System.Net.Http.Json;

namespace HomeWorkplace.ContextApi.Tests;

public class RoomAdminTests
{
    private static async Task PostAsync(HttpClient client, string room, string id, string goal, string content)
    {
        var response = await client.PostAsJsonAsync($"/rooms/{room}/messages", new
        {
            id, name = id, goal, content,
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Room_listing_reports_counts_and_agents()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Ship it", "one");
        await PostAsync(client, "alpha", "codex-1", "Test it", "two");

        var list = await client.GetFromJsonAsync<RoomListResponse>("/rooms", TestJson.Options);

        var alpha = list!.Rooms.Single(r => r.Room == "alpha");
        Assert.Equal(2, alpha.MessageCount);
        Assert.Equal(2, alpha.Cursor);
        Assert.Equal(new[] { "claude-1", "codex-1" }, alpha.Agents.OrderBy(a => a));
        Assert.NotNull(alpha.LastActivity);
    }

    [Fact]
    public async Task Room_listing_always_includes_the_global_room()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<RoomListResponse>("/rooms", TestJson.Options);

        var global = list!.Rooms.Single(r => r.Room == "global");
        Assert.Equal(0, global.MessageCount);
        Assert.Null(global.LastActivity);
    }

    [Fact]
    public async Task Context_returns_a_brief_naming_every_agent_and_goal()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Build the API", "starting");
        await PostAsync(client, "alpha", "codex-1", "Write the tests", "on it");

        var context = await client.GetFromJsonAsync<ContextResponse>(
            "/rooms/alpha/context", TestJson.Options);

        Assert.Contains("claude-1", context!.Brief);
        Assert.Contains("Build the API", context.Brief);
        Assert.Contains("codex-1", context.Brief);
        Assert.Contains("Write the tests", context.Brief);
        Assert.Contains("starting", context.Brief);
        Assert.Equal(2, context.Messages.Count);
        Assert.Equal(2, context.Cursor);
    }

    [Fact]
    public async Task Context_as_text_returns_plain_text_markdown()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Build the API", "starting");

        var response = await client.GetAsync("/rooms/alpha/context?format=text");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Build the API", body);
        Assert.Contains("starting", body);
    }

    [Fact]
    public async Task Delete_clears_messages_and_roster()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Ship it", "one");

        var deleted = await client.DeleteAsync("/rooms/alpha");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);
        Assert.Empty(read!.Messages);
        Assert.Empty(read.Agents);
    }

    [Fact]
    public async Task Delete_does_not_reset_the_sequence()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Ship it", "one");
        await PostAsync(client, "alpha", "claude-1", "Ship it", "two");
        await client.DeleteAsync("/rooms/alpha");
        await PostAsync(client, "alpha", "claude-1", "Ship it", "three");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal(3, read!.Messages.Single().Seq);
        Assert.Equal(3, read.Cursor);
    }

    [Fact]
    public async Task A_cursor_from_before_a_delete_is_reported_as_truncated()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Ship it", "one");
        await client.DeleteAsync("/rooms/alpha");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=0", TestJson.Options);

        Assert.True(read!.Truncated);
    }

    [Fact]
    public async Task Deleting_the_global_room_clears_it_but_keeps_it_listed()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "global", "claude-1", "Ship it", "one");
        await client.DeleteAsync("/rooms/global");

        var list = await client.GetFromJsonAsync<RoomListResponse>("/rooms", TestJson.Options);

        var global = list!.Rooms.Single(r => r.Room == "global");
        Assert.Equal(0, global.MessageCount);
    }

    [Fact]
    public async Task Deleting_a_room_that_does_not_exist_is_a_no_op()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/rooms/never-existed");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
