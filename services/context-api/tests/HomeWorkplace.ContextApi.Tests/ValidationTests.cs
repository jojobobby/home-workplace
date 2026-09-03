using System.Net;
using System.Net.Http.Json;

namespace HomeWorkplace.ContextApi.Tests;

public class ValidationTests
{
    [Fact]
    public async Task A_blank_id_is_rejected()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "   ",
            name = "Claude",
            goal = "Ship it",
            content = "hello",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_content_is_rejected()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Ship it",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_content_is_rejected()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxContentLength", "16"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Ship it",
            content = new string('x', 17),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_goal_is_rejected()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxGoalLength", "8"));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = new string('g', 9),
            content = "hello",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    [InlineData("has.dot")]
    public async Task A_malformed_room_id_is_rejected(string roomId)
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/rooms/{Uri.EscapeDataString(roomId)}/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Ship it",
            content = "hello",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Room_ids_are_case_insensitive()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/rooms/Alpha/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Ship it",
            content = "hello",
        });

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Single(read!.Messages);
        Assert.Equal("alpha", read.Messages[0].Room);
    }

    [Fact]
    public async Task Exceeding_the_room_cap_is_rejected()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxRooms", "2"));
        using var client = factory.CreateClient();

        // "global" already occupies one slot, so "room1" fills the cap.
        var first = await client.PostAsJsonAsync("/rooms/room1/messages", new
        {
            id = "claude-1", name = "Claude", goal = "Ship it", content = "hello",
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/rooms/room2/messages", new
        {
            id = "claude-1", name = "Claude", goal = "Ship it", content = "hello",
        });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }
}
