using System.Collections.Concurrent;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public sealed record FakePost(string Room, string AgentId, string Name, string? Goal, string Content);

public sealed class FakeContextApi : IContextApiClient
{
    public ConcurrentQueue<FakePost> Posts { get; } = new();
    public ConcurrentDictionary<string, string> Briefs { get; } = new();
    public ConcurrentQueue<(string Room, string Path, string Content)> Files { get; } = new();

    public Task PostAsync(string room, string agentId, string name, string? goal, string content, CancellationToken ct)
    { Posts.Enqueue(new FakePost(room, agentId, name, goal, content)); return Task.CompletedTask; }

    public Task<string> GetBriefAsync(string room, CancellationToken ct)
        => Task.FromResult(Briefs.TryGetValue(room, out var b) ? b : $"# room {room}\n(empty)");

    public Task PutFileAsync(string room, string path, string content, string agentId, string name, CancellationToken ct)
    { Files.Enqueue((room, path, content)); return Task.CompletedTask; }
}
