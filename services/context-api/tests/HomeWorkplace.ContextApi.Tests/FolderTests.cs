using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace HomeWorkplace.ContextApi.Tests;

public class FolderTests
{
    private static Task<HttpResponseMessage> PutFileAsync(
        HttpClient client, string room, string path, string content, string id = "claude-1", string name = "Claude")
        => client.PutAsync(
            $"/rooms/{room}/files/{path}?id={Uri.EscapeDataString(id)}&name={Uri.EscapeDataString(name)}",
            new StringContent(content, Encoding.UTF8, "text/plain"));

    [Fact]
    public async Task A_written_file_reads_back_as_plain_text()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var put = await PutFileAsync(client, "alpha", "notes.md", "# Plan\n- ship it\n");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var get = await client.GetAsync("/rooms/alpha/files/notes.md");

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("text/plain", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal("# Plan\n- ship it\n", await get.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Writing_the_same_path_again_bumps_the_version()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var first = await (await PutFileAsync(client, "alpha", "notes.md", "v1"))
            .Content.ReadFromJsonAsync<FileWriteResponse>(TestJson.Options);
        var second = await (await PutFileAsync(client, "alpha", "notes.md", "v2", id: "codex-1", name: "Codex"))
            .Content.ReadFromJsonAsync<FileWriteResponse>(TestJson.Options);

        Assert.Equal(1, first!.Version);
        Assert.Equal(2, second!.Version);
        Assert.Equal("notes.md", second.Path);
        Assert.Equal(2, second.Bytes);

        var list = await client.GetFromJsonAsync<FileListResponse>("/rooms/alpha/files", TestJson.Options);
        var entry = Assert.Single(list!.Files);
        Assert.Equal("codex-1", entry.UpdatedBy);
        Assert.Equal(2, entry.Version);
    }

    [Fact]
    public async Task Listing_reports_every_file_sorted_by_path()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PutFileAsync(client, "alpha", "src/b.cs", "bb");
        await PutFileAsync(client, "alpha", "README.md", "r");
        await PutFileAsync(client, "alpha", "src/a.cs", "a");

        var list = await client.GetFromJsonAsync<FileListResponse>("/rooms/alpha/files", TestJson.Options);

        Assert.Equal("alpha", list!.Room);
        Assert.Equal(new[] { "README.md", "src/a.cs", "src/b.cs" }, list.Files.Select(f => f.Path));
        Assert.Equal(new long[] { 1, 1, 2 }, list.Files.Select(f => f.Bytes));
        Assert.All(list.Files, f => Assert.Equal(1, f.Version));
    }

    [Fact]
    public async Task Deleting_a_file_removes_it_and_is_idempotent()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PutFileAsync(client, "alpha", "notes.md", "x");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/rooms/alpha/files/notes.md?id=claude-1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/rooms/alpha/files/notes.md")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/rooms/alpha/files/notes.md?id=claude-1")).StatusCode);
    }

    [Fact]
    public async Task Reading_a_missing_file_is_404()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/rooms/alpha/files/nope.txt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_path_is_rejected_at_the_endpoint()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await PutFileAsync(client, "alpha", "has space.txt", "x");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // HttpClient resolves "." and ".." segments before sending, so traversal can't be
    // exercised over HTTP. The validator is tested directly for those rules.
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("src/../../etc/passwd")]
    [InlineData("/absolute.txt")]
    [InlineData("a//b.txt")]
    [InlineData("./x.txt")]
    [InlineData("has space.txt")]
    [InlineData("")]
    public void The_path_validator_rejects_traversal_and_malformed_paths(string path)
        => Assert.False(FolderEndpoints.TryValidatePath(path, out _));

    [Theory]
    [InlineData("notes.md")]
    [InlineData("src/a.cs")]
    [InlineData("deep/er/path-with_chars.v2.txt")]
    public void The_path_validator_accepts_normal_paths(string path)
        => Assert.True(FolderEndpoints.TryValidatePath(path, out _));

    [Fact]
    public async Task A_write_without_an_agent_id_is_rejected()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PutAsync("/rooms/alpha/files/notes.md", new StringContent("x", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_file_over_the_byte_cap_is_rejected()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxFileBytes", "16"));
        using var client = factory.CreateClient();

        var response = await PutFileAsync(client, "alpha", "big.txt", new string('x', 17));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Exceeding_the_file_count_cap_is_rejected_but_overwrites_still_work()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxFilesPerRoom", "2"));
        using var client = factory.CreateClient();

        await PutFileAsync(client, "alpha", "a.txt", "a");
        await PutFileAsync(client, "alpha", "b.txt", "b");

        Assert.Equal(HttpStatusCode.BadRequest, (await PutFileAsync(client, "alpha", "c.txt", "c")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutFileAsync(client, "alpha", "a.txt", "a2")).StatusCode);
    }

    [Fact]
    public async Task A_write_posts_a_file_notification_into_the_room_chat_and_firehose()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PutFileAsync(client, "alpha", "notes.md", "hello", id: "codex-1", name: "Codex");

        var room = await client.GetFromJsonAsync<RoomReadResponse>("/rooms/alpha/messages", TestJson.Options);
        var note = Assert.Single(room!.Messages);
        Assert.Equal("codex-1", note.AgentId);
        Assert.StartsWith("[file]", note.Content);
        Assert.Contains("notes.md", note.Content);
        Assert.Contains("v1", note.Content);

        var firehose = await client.GetFromJsonAsync<FirehoseResponse>("/firehose", TestJson.Options);
        Assert.Contains(firehose!.Messages, m => m.Room == "alpha" && m.Content.StartsWith("[file]"));
    }

    [Fact]
    public async Task A_delete_posts_a_file_notification_too()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PutFileAsync(client, "alpha", "notes.md", "hello");
        await client.DeleteAsync("/rooms/alpha/files/notes.md?id=claude-1");

        var room = await client.GetFromJsonAsync<RoomReadResponse>("/rooms/alpha/messages", TestJson.Options);

        Assert.Equal(2, room!.Messages.Count);
        Assert.Contains("deleted", room.Messages[1].Content);
    }

    [Fact]
    public async Task Files_are_scoped_to_their_room()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PutFileAsync(client, "alpha", "notes.md", "alpha only");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/rooms/beta/files/notes.md")).StatusCode);
        var beta = await client.GetFromJsonAsync<FileListResponse>("/rooms/beta/files", TestJson.Options);
        Assert.Empty(beta!.Files);
    }

    [Fact]
    public async Task Clearing_a_room_clears_its_files_and_the_listing_shows_file_count()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PutFileAsync(client, "alpha", "notes.md", "x");

        var before = await client.GetFromJsonAsync<RoomListResponse>("/rooms", TestJson.Options);
        Assert.Equal(1, before!.Rooms.Single(r => r.Room == "alpha").FileCount);

        await client.DeleteAsync("/rooms/alpha");

        var files = await client.GetFromJsonAsync<FileListResponse>("/rooms/alpha/files", TestJson.Options);
        Assert.Empty(files!.Files);
    }
}
