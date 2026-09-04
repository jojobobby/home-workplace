namespace HomeWorkplace.Client;

/// <summary>Is a service answering on its base URL? Faked in tests.</summary>
public interface IHealthProbe
{
    Task<bool> IsHealthyAsync(string baseUrl, CancellationToken ct);
}

public sealed class HttpHealthProbe : IHealthProbe
{
    private readonly HttpClient _http;

    public HttpHealthProbe(HttpClient http) => _http = http;

    public async Task<bool> IsHealthyAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"{baseUrl.TrimEnd('/')}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;   // not up yet
        }
    }
}
