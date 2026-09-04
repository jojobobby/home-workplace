namespace HomeWorkplace.Client;

/// <summary>What the UI needs from context-api. <see cref="ContextApiClient"/> is the HTTP implementation.</summary>
public interface IContextApi
{
    Task<string> GetBriefAsync(string room, CancellationToken ct = default);
    Task<RoomFilesDto> ListFilesAsync(string room, CancellationToken ct = default);
    Task<string> GetFileAsync(string room, string path, CancellationToken ct = default);
}
