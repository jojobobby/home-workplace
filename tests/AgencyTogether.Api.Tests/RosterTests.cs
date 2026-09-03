using System.Net.Http.Json;

namespace AgencyTogether.Api.Tests;

public class RosterTests
{
    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string id, string? name, string? goal, string content)
        => client.PostAsJsonAsync("/rooms/alpha/messages", new { id, name, goal, content });

    [Fact]
    public async Task Roster_lists_each_agent_once_with_its_message_count()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", "Claude", "Build the API", "one");
        await PostAsync(client, "claude-1", "Claude", "Build the API", "two");
        await PostAsync(client, "codex-1", "Codex", "Write the tests", "three");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal(2, read!.Agents.Count);
        var claude = read.Agents.Single(a => a.AgentId == "claude-1");
        Assert.Equal("Claude", claude.Name);
        Assert.Equal("Build the API", claude.Goal);
        Assert.Equal(2, claude.MessageCount);
    }

    [Fact]
    public async Task A_new_non_blank_goal_updates_the_roster()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", "Claude", "Build the API", "one");
        await PostAsync(client, "claude-1", "Claude", "Now writing docs", "two");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal("Now writing docs", read!.Agents.Single().Goal);
    }

    [Fact]
    public async Task An_omitted_goal_preserves_the_stored_goal_on_roster_and_message()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", "Claude", "Build the API", "one");
        await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1",
            name = "Claude",
            content = "two",
        });

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal("Build the API", read!.Agents.Single().Goal);
        Assert.All(read.Messages, m => Assert.Equal("Build the API", m.Goal));
    }

    [Fact]
    public async Task A_whitespace_goal_preserves_the_stored_goal()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", "Claude", "Build the API", "one");
        await PostAsync(client, "claude-1", "Claude", "   ", "two");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal("Build the API", read!.Agents.Single().Goal);
    }

    [Fact]
    public async Task A_blank_name_on_the_first_message_falls_back_to_the_agent_id()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", null, "Build the API", "one");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal("claude-1", read!.Agents.Single().Name);
    }

    [Fact]
    public async Task Presence_is_scoped_to_the_room()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", "Claude", "Build the API", "one");

        var beta = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/beta/messages", TestJson.Options);

        Assert.Empty(beta!.Agents);
    }
}
