using System.Net.Http.Json;
using System.Text.Json;

namespace HomeWorkplace.Client;

/// <summary>Typed client for every Foreman endpoint. Nothing here that curl couldn't do.</summary>
public sealed class ForemanClient
{
    private readonly HttpClient _http;

    public ForemanClient(HttpClient http) => _http = http;

    // ---- employees ----
    public Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<EmployeeDto>>("/employees", ct);
    public Task<EmployeeDto> GetEmployeeAsync(string id, CancellationToken ct = default) => GetAsync<EmployeeDto>($"/employees/{id}", ct);
    public Task ReloadEmployeesAsync(CancellationToken ct = default) => PostNoContentAsync("/employees/reload", ct);
    public Task WakeAsync(string id, string? until = null, CancellationToken ct = default)
        => PostNoContentAsync($"/employees/{id}/wake" + (string.IsNullOrWhiteSpace(until) ? "" : $"?until={until}"), ct);
    public Task SleepAsync(string id, CancellationToken ct = default) => PostNoContentAsync($"/employees/{id}/sleep", ct);
    public Task ResetAsync(string id, CancellationToken ct = default) => PostNoContentAsync($"/employees/{id}/reset", ct);

    // ---- tasks ----
    public Task<TaskDto> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default) => PostAsync<TaskDto>("/tasks", request, ct);
    public Task<IReadOnlyList<TaskDto>> GetTasksAsync(TaskState? status = null, string? assignee = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (status is { } s) query.Add($"status={s}");
        if (!string.IsNullOrEmpty(assignee)) query.Add($"assignee={Uri.EscapeDataString(assignee)}");
        return GetAsync<IReadOnlyList<TaskDto>>("/tasks" + (query.Count > 0 ? "?" + string.Join("&", query) : ""), ct);
    }
    public Task<TaskDto> GetTaskAsync(string id, CancellationToken ct = default) => GetAsync<TaskDto>($"/tasks/{id}", ct);
    public Task<TaskDto> ApproveAsync(string id, CancellationToken ct = default) => PostAsync<TaskDto>($"/tasks/{id}/approve", null, ct);
    public Task<TaskDto> AnswerAsync(string id, string text, CancellationToken ct = default) => PostAsync<TaskDto>($"/tasks/{id}/answer", new { text }, ct);
    public Task<TaskDto> ReassignAsync(string id, string assignee, CancellationToken ct = default) => PostAsync<TaskDto>($"/tasks/{id}/reassign", new { assignee }, ct);
    public Task<TaskDto> RetryAsync(string id, CancellationToken ct = default) => PostAsync<TaskDto>($"/tasks/{id}/retry", null, ct);
    public Task<TaskDto> CancelTaskAsync(string id, CancellationToken ct = default) => PostAsync<TaskDto>($"/tasks/{id}/cancel", null, ct);

    // ---- goals ----
    public Task<GoalDto> CreateGoalAsync(CreateGoalRequest request, CancellationToken ct = default) => PostAsync<GoalDto>("/goals", request, ct);
    public Task<IReadOnlyList<GoalDto>> GetGoalsAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<GoalDto>>("/goals", ct);
    public Task<GoalDto> GetGoalAsync(string id, CancellationToken ct = default) => GetAsync<GoalDto>($"/goals/{id}", ct);
    public Task<GoalDto> TopUpAsync(string id, decimal addUsd, CancellationToken ct = default) => PostAsync<GoalDto>($"/goals/{id}/topup", new { addUsd }, ct);
    public Task<GoalDto> CancelGoalAsync(string id, CancellationToken ct = default) => PostAsync<GoalDto>($"/goals/{id}/cancel", null, ct);

    // ---- events / health ----
    public Task<EventPageDto> GetEventsAsync(long since = 0, int wait = 0, int limit = 200, CancellationToken ct = default)
        => GetAsync<EventPageDto>($"/events?since={since}&wait={wait}&limit={limit}", ct);
    public Task<HealthDto> GetHealthAsync(CancellationToken ct = default) => GetAsync<HealthDto>("/health", ct);

    // ---- plumbing ----
    private Task<T> GetAsync<T>(string path, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, path, null, ct);
    private Task<T> PostAsync<T>(string path, object? body, CancellationToken ct) => SendAsync<T>(HttpMethod.Post, path, body, ct);

    private async Task PostNoContentAsync(string path, CancellationToken ct)
    {
        using var response = await SendRawAsync(HttpMethod.Post, path, null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, body, ct);
        await EnsureSuccessAsync(response, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, ApiJson.Options)
               ?? throw new ApiException((int)response.StatusCode, "Empty response", null);
    }

    private Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: ApiJson.Options);
        return _http.SendAsync(request, ct);
    }

    /// <summary>Throws <see cref="ApiException"/> for a non-2xx reply; reads problem details when the body carries them.</summary>
    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var status = (int)response.StatusCode;
        var title = response.ReasonPhrase is { Length: > 0 } reason ? reason : status.ToString();
        string? detail = null;
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var problem = JsonSerializer.Deserialize<ProblemDetailsDto>(body, ApiJson.Options);
                if (problem?.Title is { Length: > 0 } t) title = t;
                detail = problem?.Detail;
            }
            catch (JsonException) { detail = body.Length > 500 ? body[..500] : body; }
        }
        throw new ApiException(status, title, detail);
    }
}
