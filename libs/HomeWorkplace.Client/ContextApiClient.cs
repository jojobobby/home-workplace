using System.Text.Json;

namespace HomeWorkplace.Client;

/// <summary>The few context-api reads the shell needs: a room's brief and its folder.</summary>
public sealed class ContextApiClient : IContextApi
{
    private readonly HttpClient _http;

    public ContextApiClient(HttpClient http) => _http = http;

    public async Task<string> GetBriefAsync(string room, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"/rooms/{room}/context?format=text", ct);
        await ForemanClient.EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<RoomFilesDto> ListFilesAsync(string room, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"/rooms/{room}/files", ct);
        await ForemanClient.EnsureSuccessAsync(response, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<RoomFilesDto>(json, ApiJson.Options)
               ?? new RoomFilesDto(room, Array.Empty<FileDto>());
    }

    public async Task<string> GetFileAsync(string room, string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"/rooms/{room}/files/{path}", ct);
        await ForemanClient.EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
