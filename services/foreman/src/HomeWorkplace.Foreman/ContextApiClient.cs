using System.Net.Http.Json;

namespace HomeWorkplace.Foreman;

/// <summary>HTTP client for the context-api room service. Replaced by a fake in tests.</summary>
public sealed class ContextApiClient : IContextApiClient
{
    private readonly HttpClient _http;

    public ContextApiClient(HttpClient http, ForemanOptions options)
    {
        _http = http;
        _http.BaseAddress = new Uri(options.ContextApiBaseUrl);
    }

    public async Task PostAsync(string room, string agentId, string name, string? goal, string content, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"/rooms/{room}/messages", new { id = agentId, name, goal, content }, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<string> GetBriefAsync(string room, CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"/rooms/{room}/context?format=text", ct);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(ct) : $"# room {room}\n(unavailable)";
    }

    public async Task PutFileAsync(string room, string path, string content, string agentId, string name, CancellationToken ct)
    {
        using var body = new StringContent(content, System.Text.Encoding.UTF8, "text/plain");
        using var resp = await _http.PutAsync(
            $"/rooms/{room}/files/{path}?id={Uri.EscapeDataString(agentId)}&name={Uri.EscapeDataString(name)}", body, ct);
        resp.EnsureSuccessStatusCode();
    }
}
