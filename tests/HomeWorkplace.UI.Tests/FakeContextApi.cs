using HomeWorkplace.Client;

namespace HomeWorkplace.UI.Tests;

public sealed class FakeContextApi : IContextApi
{
    public Dictionary<string, string> Briefs { get; } = new();
    public List<string> Calls { get; } = new();

    public Task<string> GetBriefAsync(string room, CancellationToken ct = default)
    {
        Calls.Add("brief:" + room);
        return Task.FromResult(Briefs.TryGetValue(room, out var b) ? b : $"# Agency room: {room}\n_No messages yet._");
    }

    public Task<RoomFilesDto> ListFilesAsync(string room, CancellationToken ct = default)
        => Task.FromResult(new RoomFilesDto(room, Array.Empty<FileDto>()));

    public Task<string> GetFileAsync(string room, string path, CancellationToken ct = default)
        => Task.FromResult("");
}
