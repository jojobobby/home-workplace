namespace HomeWorkplace.Foreman;

/// <summary>Foreman's view of the context-api room service. Faked in tests.</summary>
public interface IContextApiClient
{
    Task PostAsync(string room, string agentId, string name, string? goal, string content, CancellationToken ct);
    Task<string> GetBriefAsync(string room, CancellationToken ct);
    Task PutFileAsync(string room, string path, string content, string agentId, string name, CancellationToken ct);
}
