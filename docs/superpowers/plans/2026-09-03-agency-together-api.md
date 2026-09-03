# Agency Together Shared-Context API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a C# ASP.NET Core service that accepts `{id, name, goal, content}` over HTTPS and serves it back as the shared in-memory context that Claude, Codex, and other agents read to collaborate in one conversation.

**Architecture:** Minimal APIs over a singleton in-memory `ChatStore`. Every message lives in exactly one room; `global` is the reserved default room, and `/firehose` is a read-only merged view fed by a bounded ring buffer. Reads are cursor-based with optional long-polling implemented with `TaskCompletionSource` waiter sets released on write.

**Tech Stack:** .NET 8 (`net8.0`), ASP.NET Core Minimal APIs, Swashbuckle, xunit + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`), `System.Text.Json`.

**Spec:** `docs/superpowers/specs/2026-09-03-agency-together-design.md`

## Global Constraints

These apply to every task. Values are copied verbatim from the spec.

- Target framework `net8.0`. `global.json` pins SDK `8.0.417` with `rollForward: latestFeature`.
- Namespace for all API code: `AgencyTogether.Api`. Tests: `AgencyTogether.Api.Tests`.
- No authentication anywhere. The service is deliberately open; the limits below are the only protection.
- JSON is camelCase (ASP.NET default). The POST request field is `id`, but it maps to `AgentId` on stored messages.
- Reserved global room id: `global`. It is auto-created at startup and is never removed.
- Two independent monotonic counters: per-room `seq` and service-wide `globalSeq`. **Neither ever resets while the process lives** — not on eviction, not on `DELETE`.
- Reading a room that does not exist returns an empty `200`, never `404`. Reads never create rooms.
- Long-poll timeout returns `200` with an **empty list** — never `204`, never `404`.
- Errors are RFC 7807 `ProblemDetails` via `Results.Problem` / `Results.ValidationProblem`.
- Limits, all in `ChatOptions` bound from the `Chat` config section: `MaxMessagesPerRoom` 1000, `MaxRooms` 200, `MaxContentLength` 32768, `MaxAgentIdLength` 128, `MaxNameLength` 128, `MaxGoalLength` 512, `MaxWaitSeconds` 60, `DefaultLimit` 200, `MaxLimit` 500, `FirehoseCapacity` 2000.
- Room ids are lower-cased before validation and lookup, and must match `^[a-z0-9][a-z0-9_-]{0,63}$`.
- `wait` and `limit` are **clamped**, not rejected. Everything else out of range is a `400`.
- Kestrel binds `https://localhost:7171` and `http://localhost:5171`.

---

## File Structure

| File | Responsibility |
|---|---|
| `global.json` | Pin SDK 8.0.417 |
| `AgencyTogether.sln` | Solution |
| `src/AgencyTogether.Api/AgencyTogether.Api.csproj` | Web project, net8.0, Swashbuckle |
| `src/AgencyTogether.Api/Program.cs` | Host, DI, Kestrel URLs, Swagger, endpoint mapping, `public partial class Program` |
| `src/AgencyTogether.Api/ChatOptions.cs` | Every limit, bound from config section `Chat` |
| `src/AgencyTogether.Api/Models.cs` | Request/response records, `ChatMessage`, `AgentPresence` |
| `src/AgencyTogether.Api/Room.cs` | One room: message list, roster, per-room seq, eviction, waiters |
| `src/AgencyTogether.Api/ChatStore.cs` | Room registry, `globalSeq`, firehose ring, global waiters, room cap |
| `src/AgencyTogether.Api/ChatEndpoints.cs` | Route mapping, query/body validation, HTTP shape, long-poll loop |
| `src/AgencyTogether.Api/ContextFormatter.cs` | Renders the markdown brief |
| `src/AgencyTogether.Api/appsettings.json` | Kestrel endpoints, default `Chat` section |
| `tests/AgencyTogether.Api.Tests/ChatApiFactory.cs` | `WebApplicationFactory<Program>` + JSON helpers |
| `tests/AgencyTogether.Api.Tests/HealthTests.cs` | Liveness |
| `tests/AgencyTogether.Api.Tests/MessageFlowTests.cs` | Round-trip, global default, empty room |
| `tests/AgencyTogether.Api.Tests/CursorTests.cs` | `since`, `limit`, POST catch-up |
| `tests/AgencyTogether.Api.Tests/RosterTests.cs` | Presence upsert, goal rule, name fallback |
| `tests/AgencyTogether.Api.Tests/ValidationTests.cs` | Every 400 path, room-id normalization |
| `tests/AgencyTogether.Api.Tests/EvictionTests.cs` | Retention cap, `truncated` |
| `tests/AgencyTogether.Api.Tests/LongPollTests.cs` | Release on write, timeout empty |
| `tests/AgencyTogether.Api.Tests/FirehoseTests.cs` | Cross-room ordering, firehose long-poll |
| `tests/AgencyTogether.Api.Tests/RoomAdminTests.cs` | `/rooms`, `/context`, `DELETE` |
| `README.md` | Build/run, curl per endpoint, agent prompt block, worked example |

Each test class creates its **own** `ChatApiFactory` inside a `using`, because the `global` room is process-wide state and sharing a factory would leak messages between tests.

---

### Task 1: Solution scaffold and `/health`

**Files:**
- Create: `global.json`, `AgencyTogether.sln`
- Create: `src/AgencyTogether.Api/AgencyTogether.Api.csproj`, `src/AgencyTogether.Api/Program.cs`, `src/AgencyTogether.Api/appsettings.json`
- Create: `tests/AgencyTogether.Api.Tests/AgencyTogether.Api.Tests.csproj`, `tests/AgencyTogether.Api.Tests/ChatApiFactory.cs`
- Test: `tests/AgencyTogether.Api.Tests/HealthTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public partial class Program` (needed by `WebApplicationFactory<Program>`); `ChatApiFactory` with `HttpClient CreateClient()` and `ChatApiFactory WithOptions(params (string Key, string Value)[] settings)`; `TestJson.Options` (`JsonSerializerOptions` with `PropertyNameCaseInsensitive = true`).

- [ ] **Step 1: Create the solution, projects, and packages**

The working directory is `C:\Users\raphe\Desktop\Both\Agency Together` (note the space — quote it).

```bash
dotnet new sln -n AgencyTogether
dotnet new web -n AgencyTogether.Api -o src/AgencyTogether.Api
dotnet new xunit -n AgencyTogether.Api.Tests -o tests/AgencyTogether.Api.Tests
dotnet sln add src/AgencyTogether.Api/AgencyTogether.Api.csproj tests/AgencyTogether.Api.Tests/AgencyTogether.Api.Tests.csproj
dotnet add src/AgencyTogether.Api package Swashbuckle.AspNetCore --version 6.9.0
dotnet add tests/AgencyTogether.Api.Tests package Microsoft.AspNetCore.Mvc.Testing --version 8.0.28
dotnet add tests/AgencyTogether.Api.Tests reference src/AgencyTogether.Api/AgencyTogether.Api.csproj
```

Write `global.json`:

```json
{
  "sdk": {
    "version": "8.0.417",
    "rollForward": "latestFeature"
  }
}
```

Ensure `src/AgencyTogether.Api/AgencyTogether.Api.csproj` has these properties (the template sets most; add `RootNamespace`):

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <InvariantGlobalization>true</InvariantGlobalization>
  <RootNamespace>AgencyTogether.Api</RootNamespace>
</PropertyGroup>
```

- [ ] **Step 2: Write the test factory and JSON helper**

Create `tests/AgencyTogether.Api.Tests/ChatApiFactory.cs`:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgencyTogether.Api.Tests;

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

public sealed class ChatApiFactory : WebApplicationFactory<Program>
{
    private readonly (string Key, string Value)[] _settings;

    public ChatApiFactory() : this(Array.Empty<(string, string)>()) { }

    private ChatApiFactory((string Key, string Value)[] settings) => _settings = settings;

    public static ChatApiFactory WithOptions(params (string Key, string Value)[] settings)
        => new(settings);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
```

- [ ] **Step 3: Write the failing test**

Create `tests/AgencyTogether.Api.Tests/HealthTests.cs`:

```csharp
using System.Net;

namespace AgencyTogether.Api.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_returns_ok()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

```bash
dotnet test tests/AgencyTogether.Api.Tests --filter FullyQualifiedName~HealthTests
```

Expected: FAIL. Either a compile error (`Program` is not accessible / not found) or `Assert.Equal() Failure: Expected OK, Actual NotFound`. Both are correct RED — the endpoint does not exist. If it errors on `Program` visibility, that is fixed in Step 5, which is the point.

- [ ] **Step 5: Write the minimal implementation**

Replace `src/AgencyTogether.Api/Program.cs` entirely:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
```

Replace `src/AgencyTogether.Api/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://localhost:5171" },
      "Https": { "Url": "https://localhost:7171" }
    }
  }
}
```

- [ ] **Step 6: Run the test to verify it passes**

```bash
dotnet test tests/AgencyTogether.Api.Tests --filter FullyQualifiedName~HealthTests
```

Expected: PASS, 1 test. Build output must have no warnings.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: scaffold AgencyTogether.Api solution with health endpoint"
```

---

### Task 2: Post a message and read it back

**Files:**
- Create: `src/AgencyTogether.Api/ChatOptions.cs`, `src/AgencyTogether.Api/Models.cs`, `src/AgencyTogether.Api/Room.cs`, `src/AgencyTogether.Api/ChatStore.cs`, `src/AgencyTogether.Api/ChatEndpoints.cs`
- Modify: `src/AgencyTogether.Api/Program.cs`, `src/AgencyTogether.Api/appsettings.json`
- Test: `tests/AgencyTogether.Api.Tests/MessageFlowTests.cs`

**Interfaces:**
- Consumes: `ChatApiFactory`, `TestJson.Options` from Task 1.
- Produces:
  - `ChatOptions` with `const string SectionName = "Chat"` and the limit properties listed in Global Constraints.
  - `record ChatMessage { long Seq; long GlobalSeq; string Room; string AgentId; string Name; string? Goal; string Content; DateTimeOffset Timestamp; }`
  - `record AgentPresence { string AgentId; string Name; string? Goal; int MessageCount; DateTimeOffset FirstSeen; DateTimeOffset LastSeen; }`
  - `record PostMessageRequest { string? Id; string? Name; string? Goal; string? Content; }`
  - `record RoomReadResponse { string Room; long Cursor; IReadOnlyList<ChatMessage> Messages; IReadOnlyList<AgentPresence> Agents; bool Truncated; }`
  - `record PostMessageResponse { string Room; ChatMessage Posted; long Cursor; IReadOnlyList<ChatMessage> Messages; IReadOnlyList<AgentPresence> Agents; bool Truncated; }`
  - `record RoomSnapshot(long Cursor, IReadOnlyList<ChatMessage> Messages, IReadOnlyList<AgentPresence> Agents, bool Truncated)`
  - `Room` with `string Id`, `ChatMessage Append(string agentId, string? name, string? goal, string content, long globalSeq, DateTimeOffset now, int maxMessages)`, `RoomSnapshot Read(long since, int limit)`.
  - `ChatStore` with `const string GlobalRoomId = "global"`, `ChatMessage? Post(string roomId, string agentId, string? name, string? goal, string content)` returning `null` when the room cap is hit, and `RoomSnapshot Read(string roomId, long since, int limit)`.
  - `ChatEndpoints.MapChatEndpoints(this WebApplication app)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/AgencyTogether.Api.Tests/MessageFlowTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgencyTogether.Api.Tests;

public class MessageFlowTests
{
    [Fact]
    public async Task Posted_message_round_trips_with_all_four_fields()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var post = await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Design the endpoint layer",
            content = "Endpoints are mapped; starting on the store.",
        });

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.NotNull(read);
        var message = Assert.Single(read!.Messages);
        Assert.Equal("claude-1", message.AgentId);
        Assert.Equal("Claude", message.Name);
        Assert.Equal("Design the endpoint layer", message.Goal);
        Assert.Equal("Endpoints are mapped; starting on the store.", message.Content);
        Assert.Equal("alpha", message.Room);
        Assert.Equal(1, message.Seq);
    }

    [Fact]
    public async Task Post_without_a_room_lands_in_global()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/messages", new
        {
            id = "codex-1",
            name = "Codex",
            goal = "Write the tests",
            content = "Picking up the test project.",
        });

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/global/messages", TestJson.Options);

        var message = Assert.Single(read!.Messages);
        Assert.Equal("global", message.Room);
        Assert.Equal("codex-1", message.AgentId);
    }

    [Fact]
    public async Task Reading_a_room_nobody_has_posted_to_returns_empty_ok()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/rooms/nobody-here/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonSerializer.Deserialize<RoomReadResponse>(
            await response.Content.ReadAsStringAsync(), TestJson.Options);
        Assert.Empty(body!.Messages);
        Assert.Empty(body.Agents);
        Assert.Equal(0, body.Cursor);
        Assert.False(body.Truncated);
    }

    [Fact]
    public async Task Post_response_carries_the_posted_message_and_cursor()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Ship it",
            content = "First.",
        });

        var body = JsonSerializer.Deserialize<PostMessageResponse>(
            await response.Content.ReadAsStringAsync(), TestJson.Options);

        Assert.Equal("alpha", body!.Room);
        Assert.Equal(1, body.Posted.Seq);
        Assert.Equal(1, body.Cursor);
        Assert.Empty(body.Messages);
        Assert.False(body.Truncated);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/AgencyTogether.Api.Tests --filter FullyQualifiedName~MessageFlowTests
```

Expected: FAIL to compile — `RoomReadResponse` and `PostMessageResponse` do not exist. That is the correct RED for this task: the types are the contract under test.

- [ ] **Step 3: Write `ChatOptions.cs`**

```csharp
namespace AgencyTogether.Api;

public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    public int MaxMessagesPerRoom { get; set; } = 1000;
    public int MaxRooms { get; set; } = 200;
    public int MaxContentLength { get; set; } = 32768;
    public int MaxAgentIdLength { get; set; } = 128;
    public int MaxNameLength { get; set; } = 128;
    public int MaxGoalLength { get; set; } = 512;
    public int MaxWaitSeconds { get; set; } = 60;
    public int DefaultLimit { get; set; } = 200;
    public int MaxLimit { get; set; } = 500;
    public int FirehoseCapacity { get; set; } = 2000;
}
```

- [ ] **Step 4: Write `Models.cs`**

```csharp
namespace AgencyTogether.Api;

public sealed record PostMessageRequest
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Goal { get; init; }
    public string? Content { get; init; }
}

public sealed record ChatMessage
{
    public required long Seq { get; init; }
    public required long GlobalSeq { get; init; }
    public required string Room { get; init; }
    public required string AgentId { get; init; }
    public required string Name { get; init; }
    public string? Goal { get; init; }
    public required string Content { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed record AgentPresence
{
    public required string AgentId { get; init; }
    public required string Name { get; init; }
    public string? Goal { get; init; }
    public required int MessageCount { get; init; }
    public required DateTimeOffset FirstSeen { get; init; }
    public required DateTimeOffset LastSeen { get; init; }
}

public sealed record RoomSnapshot(
    long Cursor,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<AgentPresence> Agents,
    bool Truncated);

public sealed record RoomReadResponse
{
    public required string Room { get; init; }
    public required long Cursor { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required IReadOnlyList<AgentPresence> Agents { get; init; }
    public required bool Truncated { get; init; }
}

public sealed record PostMessageResponse
{
    public required string Room { get; init; }
    public required ChatMessage Posted { get; init; }
    public required long Cursor { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required IReadOnlyList<AgentPresence> Agents { get; init; }
    public required bool Truncated { get; init; }
}
```

- [ ] **Step 5: Write `Room.cs`**

`_firstAvailableSeq` is the lowest `seq` a caller can still be served. It starts at 1 and only moves forward — on eviction (Task 6) and on clear (Task 9). A caller asking for `since` is missing data when `since + 1 < _firstAvailableSeq`.

```csharp
namespace AgencyTogether.Api;

public sealed class Room
{
    private readonly object _gate = new();
    private readonly List<ChatMessage> _messages = new();
    private readonly Dictionary<string, AgentPresence> _agents = new(StringComparer.Ordinal);

    private long _seq;
    private long _firstAvailableSeq = 1;

    public Room(string id) => Id = id;

    public string Id { get; }

    public ChatMessage Append(
        string agentId,
        string? name,
        string? goal,
        string content,
        long globalSeq,
        DateTimeOffset now,
        int maxMessages)
    {
        lock (_gate)
        {
            _agents.TryGetValue(agentId, out var existing);

            var effectiveName = !string.IsNullOrWhiteSpace(name)
                ? name!.Trim()
                : existing?.Name ?? agentId;

            var effectiveGoal = !string.IsNullOrWhiteSpace(goal)
                ? goal!.Trim()
                : existing?.Goal;

            var message = new ChatMessage
            {
                Seq = ++_seq,
                GlobalSeq = globalSeq,
                Room = Id,
                AgentId = agentId,
                Name = effectiveName,
                Goal = effectiveGoal,
                Content = content,
                Timestamp = now,
            };

            _messages.Add(message);

            _agents[agentId] = new AgentPresence
            {
                AgentId = agentId,
                Name = effectiveName,
                Goal = effectiveGoal,
                MessageCount = (existing?.MessageCount ?? 0) + 1,
                FirstSeen = existing?.FirstSeen ?? now,
                LastSeen = now,
            };

            while (_messages.Count > maxMessages)
            {
                _firstAvailableSeq = _messages[0].Seq + 1;
                _messages.RemoveAt(0);
            }

            return message;
        }
    }

    public RoomSnapshot Read(long since, int limit)
    {
        lock (_gate)
        {
            var messages = _messages
                .Where(m => m.Seq > since)
                .Take(limit)
                .ToArray();

            var agents = _agents.Values
                .OrderBy(a => a.FirstSeen)
                .ThenBy(a => a.AgentId, StringComparer.Ordinal)
                .ToArray();

            return new RoomSnapshot(_seq, messages, agents, since + 1 < _firstAvailableSeq);
        }
    }
}
```

`_messages.RemoveAt(0)` is O(n), which is fine at a 1000-message cap for a coordination tool. Do not optimise it now.

- [ ] **Step 6: Write `ChatStore.cs`**

Every write is serialised on `_globalGate`. That guarantees `globalSeq` order matches firehose order and removes a whole class of interleaving bugs; write throughput is irrelevant for this workload. Reads take only the per-room lock.

```csharp
using System.Collections.Concurrent;

namespace AgencyTogether.Api;

public sealed class ChatStore
{
    public const string GlobalRoomId = "global";

    private readonly ChatOptions _options;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly object _globalGate = new();

    private long _globalSeq;

    public ChatStore(ChatOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
        _rooms[GlobalRoomId] = new Room(GlobalRoomId);
    }

    /// <summary>Appends a message. Returns null when the room cap would be exceeded.</summary>
    public ChatMessage? Post(string roomId, string agentId, string? name, string? goal, string content)
    {
        lock (_globalGate)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                if (_rooms.Count >= _options.MaxRooms)
                {
                    return null;
                }

                room = new Room(roomId);
                _rooms[roomId] = room;
            }

            var globalSeq = ++_globalSeq;
            return room.Append(
                agentId, name, goal, content, globalSeq,
                _clock.GetUtcNow(), _options.MaxMessagesPerRoom);
        }
    }

    /// <summary>Reads a room. A room that does not exist reads as empty; it is never created.</summary>
    public RoomSnapshot Read(string roomId, long since, int limit)
        => _rooms.TryGetValue(roomId, out var room)
            ? room.Read(since, limit)
            : new RoomSnapshot(0, Array.Empty<ChatMessage>(), Array.Empty<AgentPresence>(), false);
}
```

- [ ] **Step 7: Write `ChatEndpoints.cs`**

Validation arrives in Task 5; for now trust the input so the round-trip test can pass with minimal code.

```csharp
namespace AgencyTogether.Api;

public static class ChatEndpoints
{
    public static WebApplication MapChatEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapPost("/messages", (PostMessageRequest request, ChatStore store, ChatOptions options)
            => PostMessage(ChatStore.GlobalRoomId, request, store, options));

        app.MapPost("/rooms/{roomId}/messages", (string roomId, PostMessageRequest request, ChatStore store, ChatOptions options)
            => PostMessage(roomId, request, store, options));

        app.MapGet("/rooms/{roomId}/messages", (string roomId, ChatStore store, ChatOptions options)
            => ReadRoom(roomId, since: 0, limit: options.DefaultLimit, store));

        return app;
    }

    private static IResult PostMessage(
        string roomId, PostMessageRequest request, ChatStore store, ChatOptions options)
    {
        var posted = store.Post(
            roomId, request.Id!.Trim(), request.Name, request.Goal, request.Content!);

        if (posted is null)
        {
            return Results.Problem(
                detail: $"Room limit of {options.MaxRooms} reached.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var snapshot = store.Read(roomId, since: posted.Seq, limit: options.DefaultLimit);

        return Results.Created($"/rooms/{roomId}/messages/{posted.Seq}", new PostMessageResponse
        {
            Room = roomId,
            Posted = posted,
            Cursor = snapshot.Cursor,
            Messages = Array.Empty<ChatMessage>(),
            Agents = snapshot.Agents,
            Truncated = false,
        });
    }

    private static IResult ReadRoom(string roomId, long since, int limit, ChatStore store)
    {
        var snapshot = store.Read(roomId, since, limit);

        return Results.Ok(new RoomReadResponse
        {
            Room = roomId,
            Cursor = snapshot.Cursor,
            Messages = snapshot.Messages,
            Agents = snapshot.Agents,
            Truncated = snapshot.Truncated,
        });
    }
}
```

- [ ] **Step 8: Wire it up in `Program.cs`**

Replace the body of `Program.cs` (keep `public partial class Program;` at the bottom):

```csharp
using AgencyTogether.Api;

var builder = WebApplication.CreateBuilder(args);

var chatOptions = builder.Configuration.GetSection(ChatOptions.SectionName).Get<ChatOptions>()
                  ?? new ChatOptions();

builder.Services.AddSingleton(chatOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ChatStore>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapChatEndpoints();

app.Run();

public partial class Program;
```

Add the defaults to `appsettings.json` alongside the existing keys:

```json
  "Chat": {
    "MaxMessagesPerRoom": 1000,
    "MaxRooms": 200,
    "MaxContentLength": 32768,
    "MaxAgentIdLength": 128,
    "MaxNameLength": 128,
    "MaxGoalLength": 512,
    "MaxWaitSeconds": 60,
    "DefaultLimit": 200,
    "MaxLimit": 500,
    "FirehoseCapacity": 2000
  }
```

- [ ] **Step 9: Run the tests to verify they pass**

```bash
dotnet test tests/AgencyTogether.Api.Tests
```

Expected: PASS, 5 tests (4 message-flow + 1 health). No warnings.

- [ ] **Step 10: Commit**

```bash
git add -A && git commit -m "feat: post and read messages in rooms with a global default"
```

---

### Task 3: Cursors, limit, and post-with-catch-up

**Files:**
- Modify: `src/AgencyTogether.Api/ChatEndpoints.cs`
- Test: `tests/AgencyTogether.Api.Tests/CursorTests.cs`

**Interfaces:**
- Consumes: `ChatStore.Read`, `RoomReadResponse`, `PostMessageResponse` from Task 2.
- Produces: `ChatEndpoints.ClampLimit(int? limit, ChatOptions options) -> int` and `ChatEndpoints.ClampWait(int? wait, ChatOptions options) -> TimeSpan`, both used by Tasks 7 and 8.

- [ ] **Step 1: Write the failing tests**

Create `tests/AgencyTogether.Api.Tests/CursorTests.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace AgencyTogether.Api.Tests;

public class CursorTests
{
    private static async Task PostAsync(HttpClient client, string room, string content)
    {
        var response = await client.PostAsJsonAsync($"/rooms/{room}/messages", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Ship it",
            content,
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Since_returns_only_messages_after_the_cursor()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "one");
        await PostAsync(client, "alpha", "two");
        await PostAsync(client, "alpha", "three");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=1", TestJson.Options);

        Assert.Equal(new[] { "two", "three" }, read!.Messages.Select(m => m.Content));
        Assert.Equal(3, read.Cursor);
    }

    [Fact]
    public async Task Limit_caps_the_page_size()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "one");
        await PostAsync(client, "alpha", "two");
        await PostAsync(client, "alpha", "three");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?limit=2", TestJson.Options);

        Assert.Equal(new[] { "one", "two" }, read!.Messages.Select(m => m.Content));
        Assert.Equal(3, read.Cursor);
    }

    [Fact]
    public async Task Limit_above_the_maximum_is_clamped_not_rejected()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "one");

        var response = await client.GetAsync("/rooms/alpha/messages?limit=99999");

        response.EnsureSuccessStatusCode();
        var read = JsonSerializer.Deserialize<RoomReadResponse>(
            await response.Content.ReadAsStringAsync(), TestJson.Options);
        Assert.Single(read!.Messages);
    }

    [Fact]
    public async Task Post_with_since_returns_the_catch_up_including_its_own_message()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "from codex");

        var response = await client.PostAsJsonAsync("/rooms/alpha/messages?since=0", new
        {
            id = "claude-1",
            name = "Claude",
            goal = "Ship it",
            content = "from claude",
        });

        var body = JsonSerializer.Deserialize<PostMessageResponse>(
            await response.Content.ReadAsStringAsync(), TestJson.Options);

        Assert.Equal(new[] { "from codex", "from claude" }, body!.Messages.Select(m => m.Content));
        Assert.Equal(2, body.Cursor);
        Assert.Equal(2, body.Posted.Seq);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/AgencyTogether.Api.Tests --filter FullyQualifiedName~CursorTests
```

Expected: FAIL — `Since_returns_only_messages_after_the_cursor` gets all three messages (query string is ignored), `Limit_caps_the_page_size` gets three, and `Post_with_since...` gets an empty `Messages` array.

- [ ] **Step 3: Write the implementation**

In `ChatEndpoints.cs`, add the clamp helpers and thread the query parameters through:

```csharp
    internal static int ClampLimit(int? limit, ChatOptions options)
        => limit is null or <= 0
            ? options.DefaultLimit
            : Math.Min(limit.Value, options.MaxLimit);

    internal static TimeSpan ClampWait(int? wait, ChatOptions options)
        => wait is null or <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Min(wait.Value, options.MaxWaitSeconds));
```

Change the GET mapping to accept `since` and `limit`:

```csharp
        app.MapGet("/rooms/{roomId}/messages",
            (string roomId, long? since, int? limit, ChatStore store, ChatOptions options)
                => ReadRoom(roomId, since ?? 0, ClampLimit(limit, options), store));
```

Change both POST mappings to accept `since` and pass it down:

```csharp
        app.MapPost("/messages",
            (PostMessageRequest request, long? since, ChatStore store, ChatOptions options)
                => PostMessage(ChatStore.GlobalRoomId, request, since, store, options));

        app.MapPost("/rooms/{roomId}/messages",
            (string roomId, PostMessageRequest request, long? since, ChatStore store, ChatOptions options)
                => PostMessage(roomId, request, since, store, options));
```

Replace the body of `PostMessage` so the catch-up reads from the caller's cursor:

```csharp
    private static IResult PostMessage(
        string roomId, PostMessageRequest request, long? since, ChatStore store, ChatOptions options)
    {
        var posted = store.Post(
            roomId, request.Id!.Trim(), request.Name, request.Goal, request.Content!);

        if (posted is null)
        {
            return Results.Problem(
                detail: $"Room limit of {options.MaxRooms} reached.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var snapshot = store.Read(roomId, since ?? posted.Seq, options.DefaultLimit);

        return Results.Created($"/rooms/{roomId}/messages/{posted.Seq}", new PostMessageResponse
        {
            Room = roomId,
            Posted = posted,
            Cursor = snapshot.Cursor,
            Messages = since is null ? Array.Empty<ChatMessage>() : snapshot.Messages,
            Agents = snapshot.Agents,
            Truncated = since is not null && snapshot.Truncated,
        });
    }
```

Reading from `since ?? posted.Seq` means the no-`since` case reads nothing after its own message, and the explicit-`since` case includes the caller's own post — the single consistent rule from the spec.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/AgencyTogether.Api.Tests
```

Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: cursor paging, limit clamping, and post-with-catch-up"
```

---

### Task 4: Agent roster and the goal rule

**Files:**
- Test: `tests/AgencyTogether.Api.Tests/RosterTests.cs`

**Interfaces:**
- Consumes: `Room.Append` roster logic from Task 2, `AgentPresence`, `RoomReadResponse`.
- Produces: nothing new. This task proves the Task 2 roster behaviour and fixes it if the tests expose a gap.

The roster logic was written in Task 2 because `Append` cannot be written without it. These tests are still RED-first for the *behaviour*: run them before reading `Room.cs` again, and if any fail, fix `Room.cs`, not the test.

- [ ] **Step 1: Write the failing tests**

Create `tests/AgencyTogether.Api.Tests/RosterTests.cs`:

```csharp
using System.Net.Http.Json;

namespace AgencyTogether.Api.Tests;

public class RosterTests
{
    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string id, string? name, string? goal, string content)
        => client.PostAsJsonAsync("/rooms/alpha/messages", new { id, name, goal, content });

    [Fact]
    public async Task Roster_lists_each_agent_once_with_its_message_count()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", "Claude", "Build the API", "one");
        await PostAsync(client, "claude-1", "Claude", "Build the API", "two");
        await PostAsync(client, "codex-1", "Codex", "Write the tests", "three");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal(2, read!.Agents.Count);
        var claude = read.Agents.Single(a => a.AgentId == "claude-1");
        Assert.Equal("Claude", claude.Name);
        Assert.Equal("Build the API", claude.Goal);
        Assert.Equal(2, claude.MessageCount);
    }

    [Fact]
    public async Task A_new_non_blank_goal_updates_the_roster()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", "Claude", "Build the API", "one");
        await PostAsync(client, "claude-1", "Claude", "Now writing docs", "two");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal("Now writing docs", read!.Agents.Single().Goal);
    }

    [Fact]
    public async Task An_omitted_goal_preserves_the_stored_goal_on_roster_and_message()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", "Claude", "Build the API", "one");
        await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1",
            name = "Claude",
            content = "two",
        });

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal("Build the API", read!.Agents.Single().Goal);
        Assert.All(read.Messages, m => Assert.Equal("Build the API", m.Goal));
    }

    [Fact]
    public async Task A_whitespace_goal_preserves_the_stored_goal()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", "Claude", "Build the API", "one");
        await PostAsync(client, "claude-1", "Claude", "   ", "two");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal("Build the API", read!.Agents.Single().Goal);
    }

    [Fact]
    public async Task A_blank_name_on_the_first_message_falls_back_to_the_agent_id()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", null, "Build the API", "one");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal("claude-1", read!.Agents.Single().Name);
    }

    [Fact]
    public async Task Presence_is_scoped_to_the_room()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "claude-1", "Claude", "Build the API", "one");

        var beta = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/beta/messages", TestJson.Options);

        Assert.Empty(beta!.Agents);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail or pass honestly**

```bash
dotnet test tests/AgencyTogether.Api.Tests --filter FullyQualifiedName~RosterTests
```

Expected: PASS if Task 2's `Append` is correct. **If any test fails, fix `Room.cs` — never the test.** Record which ones failed in the commit message. A test that passes here is still valuable: it pins behaviour that later tasks (eviction, delete) could silently break.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "test: pin agent roster upsert and goal-preservation rules"
```

---

### Task 5: Validation, room-id normalization, and the room cap

**Files:**
- Modify: `src/AgencyTogether.Api/ChatEndpoints.cs`
- Test: `tests/AgencyTogether.Api.Tests/ValidationTests.cs`

**Interfaces:**
- Consumes: `ChatEndpoints.PostMessage`, `ChatEndpoints.ReadRoom`, `ChatOptions`.
- Produces: `ChatEndpoints.TryNormalizeRoomId(string raw, out string roomId) -> bool` and `ChatEndpoints.ValidateRequest(PostMessageRequest request, ChatOptions options) -> Dictionary<string, string[]>?`, used by Tasks 7, 8 and 9.

- [ ] **Step 1: Write the failing tests**

Create `tests/AgencyTogether.Api.Tests/ValidationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;

namespace AgencyTogether.Api.Tests;

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
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/AgencyTogether.Api.Tests --filter FullyQualifiedName~ValidationTests
```

Expected: FAIL. The blank-id and missing-content cases throw a `NullReferenceException` from `request.Id!.Trim()` (500, not 400); the malformed-room-id cases return 201; the case-insensitivity case creates a separate `Alpha` room.

- [ ] **Step 3: Write the implementation**

At the top of `ChatEndpoints.cs`:

```csharp
using System.Text.RegularExpressions;

namespace AgencyTogether.Api;

public static partial class ChatEndpoints
{
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex RoomIdPattern();

    internal static bool TryNormalizeRoomId(string raw, out string roomId)
    {
        roomId = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return RoomIdPattern().IsMatch(roomId);
    }

    internal static Dictionary<string, string[]>? ValidateRequest(
        PostMessageRequest request, ChatOptions options)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Id))
        {
            errors["id"] = new[] { "id is required." };
        }
        else if (request.Id.Trim().Length > options.MaxAgentIdLength)
        {
            errors["id"] = new[] { $"id must be at most {options.MaxAgentIdLength} characters." };
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            errors["content"] = new[] { "content is required." };
        }
        else if (request.Content.Length > options.MaxContentLength)
        {
            errors["content"] = new[] { $"content must be at most {options.MaxContentLength} characters." };
        }

        if (request.Name is { Length: > 0 } && request.Name.Trim().Length > options.MaxNameLength)
        {
            errors["name"] = new[] { $"name must be at most {options.MaxNameLength} characters." };
        }

        if (request.Goal is { Length: > 0 } && request.Goal.Trim().Length > options.MaxGoalLength)
        {
            errors["goal"] = new[] { $"goal must be at most {options.MaxGoalLength} characters." };
        }

        return errors.Count == 0 ? null : errors;
    }
```

Note the class is now `public static partial class ChatEndpoints` so `[GeneratedRegex]` works.

Gate both handlers. Replace the opening of `PostMessage`:

```csharp
    private static IResult PostMessage(
        string roomId, PostMessageRequest request, long? since, ChatStore store, ChatOptions options)
    {
        if (!TryNormalizeRoomId(roomId, out var normalizedRoom))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roomId"] = new[] { "roomId must match ^[a-z0-9][a-z0-9_-]{0,63}$." },
            });
        }

        var errors = ValidateRequest(request, options);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        var posted = store.Post(
            normalizedRoom, request.Id!.Trim(), request.Name, request.Goal, request.Content!);
```

...and use `normalizedRoom` for the rest of that method in place of `roomId`.

Replace `ReadRoom`:

```csharp
    private static IResult ReadRoom(string roomId, long since, int limit, ChatStore store)
    {
        if (!TryNormalizeRoomId(roomId, out var normalizedRoom))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roomId"] = new[] { "roomId must match ^[a-z0-9][a-z0-9_-]{0,63}$." },
            });
        }

        var snapshot = store.Read(normalizedRoom, since, limit);

        return Results.Ok(new RoomReadResponse
        {
            Room = normalizedRoom,
            Cursor = snapshot.Cursor,
            Messages = snapshot.Messages,
            Agents = snapshot.Agents,
            Truncated = snapshot.Truncated,
        });
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/AgencyTogether.Api.Tests
```

Expected: PASS, 24 tests.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: validate message fields, normalize room ids, enforce the room cap"
```

---

### Task 6: Retention cap and the `truncated` flag

**Files:**
- Test: `tests/AgencyTogether.Api.Tests/EvictionTests.cs`

**Interfaces:**
- Consumes: `Room.Append` eviction loop and `_firstAvailableSeq` from Task 2.
- Produces: nothing new. Proves the retention contract that Task 9's `DELETE` must also honour.

- [ ] **Step 1: Write the failing tests**

Create `tests/AgencyTogether.Api.Tests/EvictionTests.cs`:

```csharp
using System.Net.Http.Json;

namespace AgencyTogether.Api.Tests;

public class EvictionTests
{
    private static async Task PostAsync(HttpClient client, string content)
    {
        var response = await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "claude-1", name = "Claude", goal = "Ship it", content,
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Exceeding_the_retention_cap_drops_the_oldest_messages()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxMessagesPerRoom", "3"));
        using var client = factory.CreateClient();

        foreach (var content in new[] { "one", "two", "three", "four", "five" })
        {
            await PostAsync(client, content);
        }

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal(new[] { "three", "four", "five" }, read!.Messages.Select(m => m.Content));
    }

    [Fact]
    public async Task Sequence_numbers_stay_monotonic_across_eviction()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxMessagesPerRoom", "3"));
        using var client = factory.CreateClient();

        foreach (var content in new[] { "one", "two", "three", "four", "five" })
        {
            await PostAsync(client, content);
        }

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal(new long[] { 3, 4, 5 }, read!.Messages.Select(m => m.Seq));
        Assert.Equal(5, read.Cursor);
    }

    [Fact]
    public async Task A_cursor_older_than_retention_is_reported_as_truncated()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxMessagesPerRoom", "3"));
        using var client = factory.CreateClient();

        foreach (var content in new[] { "one", "two", "three", "four", "five" })
        {
            await PostAsync(client, content);
        }

        var stale = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=0", TestJson.Options);
        Assert.True(stale!.Truncated);

        var current = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=2", TestJson.Options);
        Assert.False(current!.Truncated);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail or pass honestly**

```bash
dotnet test tests/AgencyTogether.Api.Tests --filter FullyQualifiedName~EvictionTests
```

Expected: PASS if Task 2's eviction loop is correct. **If any fail, fix `Room.cs`, not the test.** The likely bug is `_firstAvailableSeq` being set to `_messages[0].Seq` rather than `_messages[0].Seq + 1`, which makes `since=2` report truncated when it should not.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "test: pin retention eviction, monotonic seq, and the truncated flag"
```

---

### Task 7: Long-polling on room reads

**Files:**
- Modify: `src/AgencyTogether.Api/Room.cs`, `src/AgencyTogether.Api/ChatStore.cs`, `src/AgencyTogether.Api/ChatEndpoints.cs`
- Test: `tests/AgencyTogether.Api.Tests/LongPollTests.cs`

**Interfaces:**
- Consumes: `ClampWait` (Task 3), `TryNormalizeRoomId` (Task 5), `Room.Read`.
- Produces:
  - `Room.TryRegisterWaiter(long since, out Task signal) -> bool` — returns `false` (with `signal` completed) when `_seq > since` already.
  - `Room.ReleaseWaiters()` — called from `Append` under the room lock.
  - `ChatStore.ReadWithWaitAsync(string roomId, long since, int limit, TimeSpan wait, CancellationToken ct) -> Task<RoomSnapshot>`, reused by Task 8's firehose in shape.

- [ ] **Step 1: Write the failing tests**

Create `tests/AgencyTogether.Api.Tests/LongPollTests.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;

namespace AgencyTogether.Api.Tests;

public class LongPollTests
{
    [Fact]
    public async Task A_long_poll_is_released_by_a_concurrent_post()
    {
        using var factory = new ChatApiFactory();
        using var reader = factory.CreateClient();
        using var writer = factory.CreateClient();

        var pending = reader.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=0&wait=20", TestJson.Options);

        // Give the reader time to park on the waiter before writing.
        await Task.Delay(500);

        await writer.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "codex-1", name = "Codex", goal = "Write the tests", content = "woke you up",
        });

        var read = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        var message = Assert.Single(read!.Messages);
        Assert.Equal("woke you up", message.Content);
    }

    [Fact]
    public async Task A_long_poll_with_no_writer_times_out_with_an_empty_ok()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/rooms/quiet/messages?since=0&wait=2");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await response.Content.ReadFromJsonAsync<RoomReadResponse>(TestJson.Options);
        Assert.Empty(read!.Messages);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(1.5),
            $"expected the request to block, but it returned in {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task A_long_poll_returns_immediately_when_messages_already_exist()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/rooms/alpha/messages", new
        {
            id = "codex-1", name = "Codex", goal = "Write the tests", content = "already here",
        });

        var stopwatch = Stopwatch.StartNew();
        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=0&wait=30", TestJson.Options);
        stopwatch.Stop();

        Assert.Single(read!.Messages);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"expected an immediate return, took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Wait_above_the_maximum_is_clamped_not_rejected()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:MaxWaitSeconds", "2"));
        using var client = factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/rooms/quiet/messages?since=0&wait=600");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"expected the wait to be clamped to 2s, took {stopwatch.Elapsed}.");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/AgencyTogether.Api.Tests --filter FullyQualifiedName~LongPollTests
```

Expected: FAIL. `A_long_poll_is_released_by_a_concurrent_post` returns immediately with zero messages (`wait` is ignored), and `A_long_poll_with_no_writer_times_out_with_an_empty_ok` fails its elapsed-time assertion because the request returns instantly.

- [ ] **Step 3: Add the waiter set to `Room.cs`**

Add the field:

```csharp
    private readonly List<TaskCompletionSource> _waiters = new();
```

Add these methods:

```csharp
    /// <summary>
    /// Registers a waiter for new messages. Returns false (with a completed signal) when
    /// something newer than <paramref name="since"/> already exists — the check and the
    /// registration happen under one lock, so a write cannot slip between them.
    /// </summary>
    public bool TryRegisterWaiter(long since, out Task signal)
    {
        lock (_gate)
        {
            if (_seq > since)
            {
                signal = Task.CompletedTask;
                return false;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add(waiter);
            signal = waiter.Task;
            return true;
        }
    }

    private void ReleaseWaiters()
    {
        foreach (var waiter in _waiters)
        {
            waiter.TrySetResult();
        }

        _waiters.Clear();
    }
```

Call `ReleaseWaiters()` as the last statement inside the `lock (_gate)` block of `Append`, just before `return message;`. A waiter whose caller already timed out is still completed here and then discarded — harmless, and it keeps the list bounded.

- [ ] **Step 4: Add the wait loop to `ChatStore.cs`**

```csharp
    public async Task<RoomSnapshot> ReadWithWaitAsync(
        string roomId, long since, int limit, TimeSpan wait, CancellationToken cancellationToken)
    {
        var deadline = _clock.GetUtcNow() + wait;

        while (true)
        {
            var snapshot = Read(roomId, since, limit);
            if (snapshot.Messages.Count > 0 || wait <= TimeSpan.Zero)
            {
                return snapshot;
            }

            var remaining = deadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return snapshot;
            }

            // The room may not exist yet. A read must never create one, and there is no
            // waiter set to park on, so poll at a short interval until it appears or the
            // deadline passes. Sleeping the full remaining time here would be a bug: an
            // agent polling an empty room would miss the message that creates it.
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                var slice = remaining < PollInterval ? remaining : PollInterval;
                await Task.Delay(slice, cancellationToken);
                continue;
            }

            if (!room.TryRegisterWaiter(since, out var signal))
            {
                continue;
            }

            var completed = await Task.WhenAny(signal, Task.Delay(remaining, cancellationToken));
            if (completed != signal)
            {
                return Read(roomId, since, limit);
            }
        }
    }
```

Add the interval as a field on `ChatStore`:

```csharp
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
```

Two deliberate simplifications, both fine at this scale — do not "fix" them without a measurement:

- When the signal wins the race, its `Task.Delay(remaining, ...)` timer stays armed until it fires. Bounded by the 60s `MaxWaitSeconds` cap.
- A waiter whose caller has already timed out stays in `_waiters` until the next write clears the list.

- [ ] **Step 5: Thread `wait` through `ChatEndpoints.cs`**

```csharp
        app.MapGet("/rooms/{roomId}/messages",
            (string roomId, long? since, int? limit, int? wait,
             ChatStore store, ChatOptions options, CancellationToken cancellationToken)
                => ReadRoomAsync(roomId, since ?? 0, ClampLimit(limit, options),
                                 ClampWait(wait, options), store, cancellationToken));
```

Replace `ReadRoom` with the async version:

```csharp
    private static async Task<IResult> ReadRoomAsync(
        string roomId, long since, int limit, TimeSpan wait,
        ChatStore store, CancellationToken cancellationToken)
    {
        if (!TryNormalizeRoomId(roomId, out var normalizedRoom))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roomId"] = new[] { "roomId must match ^[a-z0-9][a-z0-9_-]{0,63}$." },
            });
        }

        var snapshot = await store.ReadWithWaitAsync(
            normalizedRoom, since, limit, wait, cancellationToken);

        return Results.Ok(new RoomReadResponse
        {
            Room = normalizedRoom,
            Cursor = snapshot.Cursor,
            Messages = snapshot.Messages,
            Agents = snapshot.Agents,
            Truncated = snapshot.Truncated,
        });
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test tests/AgencyTogether.Api.Tests
```

Expected: PASS, 31 tests. These four are timing-sensitive; if `A_long_poll_is_released_by_a_concurrent_post` is flaky, raise the pre-write `Task.Delay` rather than shortening the assertions.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: long-poll room reads with waiter release on write"
```

---

### Task 8: The firehose

**Files:**
- Modify: `src/AgencyTogether.Api/ChatStore.cs`, `src/AgencyTogether.Api/Models.cs`, `src/AgencyTogether.Api/ChatEndpoints.cs`
- Test: `tests/AgencyTogether.Api.Tests/FirehoseTests.cs`

**Interfaces:**
- Consumes: `ChatStore.Post` (which already assigns `globalSeq`), `ClampLimit`, `ClampWait`.
- Produces:
  - `record FirehoseSnapshot(long Cursor, IReadOnlyList<ChatMessage> Messages, bool Truncated)`
  - `record FirehoseResponse { long Cursor; IReadOnlyList<ChatMessage> Messages; bool Truncated; }`
  - `ChatStore.ReadFirehose(long since, int limit) -> FirehoseSnapshot`
  - `ChatStore.ReadFirehoseWithWaitAsync(long since, int limit, TimeSpan wait, CancellationToken ct) -> Task<FirehoseSnapshot>`

- [ ] **Step 1: Write the failing tests**

Create `tests/AgencyTogether.Api.Tests/FirehoseTests.cs`:

```csharp
using System.Net.Http.Json;

namespace AgencyTogether.Api.Tests;

public class FirehoseTests
{
    private static async Task PostAsync(HttpClient client, string room, string id, string content)
    {
        var response = await client.PostAsJsonAsync($"/rooms/{room}/messages", new
        {
            id, name = id, goal = "Ship it", content,
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_firehose_merges_rooms_in_global_sequence_order()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "alpha one");
        await PostAsync(client, "beta", "codex-1", "beta one");
        await PostAsync(client, "alpha", "claude-1", "alpha two");

        var read = await client.GetFromJsonAsync<FirehoseResponse>("/firehose", TestJson.Options);

        Assert.Equal(
            new[] { "alpha one", "beta one", "alpha two" },
            read!.Messages.Select(m => m.Content));
        Assert.Equal(new[] { "alpha", "beta", "alpha" }, read.Messages.Select(m => m.Room));
        Assert.Equal(new long[] { 1, 2, 3 }, read.Messages.Select(m => m.GlobalSeq));
        Assert.Equal(3, read.Cursor);
    }

    [Fact]
    public async Task The_firehose_since_cursor_is_the_global_sequence()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "alpha one");
        await PostAsync(client, "beta", "codex-1", "beta one");

        var read = await client.GetFromJsonAsync<FirehoseResponse>(
            "/firehose?since=1", TestJson.Options);

        var message = Assert.Single(read!.Messages);
        Assert.Equal("beta one", message.Content);
    }

    [Fact]
    public async Task Per_room_seq_restarts_per_room_while_global_seq_does_not()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "alpha one");
        await PostAsync(client, "beta", "codex-1", "beta one");

        var read = await client.GetFromJsonAsync<FirehoseResponse>("/firehose", TestJson.Options);

        Assert.Equal(new long[] { 1, 1 }, read!.Messages.Select(m => m.Seq));
        Assert.Equal(new long[] { 1, 2 }, read.Messages.Select(m => m.GlobalSeq));
    }

    [Fact]
    public async Task A_firehose_long_poll_is_released_by_a_post_to_any_room()
    {
        using var factory = new ChatApiFactory();
        using var reader = factory.CreateClient();
        using var writer = factory.CreateClient();

        var pending = reader.GetFromJsonAsync<FirehoseResponse>(
            "/firehose?since=0&wait=20", TestJson.Options);

        await Task.Delay(500);
        await PostAsync(writer, "somewhere", "codex-1", "anywhere will do");

        var read = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        var message = Assert.Single(read!.Messages);
        Assert.Equal("anywhere will do", message.Content);
        Assert.Equal("somewhere", message.Room);
    }

    [Fact]
    public async Task A_cursor_older_than_the_ring_is_reported_as_truncated()
    {
        using var factory = ChatApiFactory.WithOptions(("Chat:FirehoseCapacity", "2"));
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "one");
        await PostAsync(client, "alpha", "claude-1", "two");
        await PostAsync(client, "alpha", "claude-1", "three");

        var read = await client.GetFromJsonAsync<FirehoseResponse>(
            "/firehose?since=0", TestJson.Options);

        Assert.True(read!.Truncated);
        Assert.Equal(new[] { "two", "three" }, read.Messages.Select(m => m.Content));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/AgencyTogether.Api.Tests --filter FullyQualifiedName~FirehoseTests
```

Expected: FAIL to compile — `FirehoseResponse` does not exist. After adding the type it would still fail with 404 on `/firehose`.

- [ ] **Step 3: Add the response types to `Models.cs`**

```csharp
public sealed record FirehoseSnapshot(
    long Cursor,
    IReadOnlyList<ChatMessage> Messages,
    bool Truncated);

public sealed record FirehoseResponse
{
    public required long Cursor { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required bool Truncated { get; init; }
}
```

- [ ] **Step 4: Add the ring buffer and global waiters to `ChatStore.cs`**

Add fields:

```csharp
    private readonly Queue<ChatMessage> _firehose = new();
    private readonly List<TaskCompletionSource> _globalWaiters = new();
    private long _firstAvailableGlobalSeq = 1;
```

Inside `Post`, after `room.Append(...)` and before returning, still holding `_globalGate`:

```csharp
            var message = room.Append(
                agentId, name, goal, content, globalSeq,
                _clock.GetUtcNow(), _options.MaxMessagesPerRoom);

            _firehose.Enqueue(message);
            while (_firehose.Count > _options.FirehoseCapacity)
            {
                _firstAvailableGlobalSeq = _firehose.Dequeue().GlobalSeq + 1;
            }

            foreach (var waiter in _globalWaiters)
            {
                waiter.TrySetResult();
            }

            _globalWaiters.Clear();

            return message;
```

Add the read paths:

```csharp
    public FirehoseSnapshot ReadFirehose(long since, int limit)
    {
        lock (_globalGate)
        {
            var messages = _firehose
                .Where(m => m.GlobalSeq > since)
                .Take(limit)
                .ToArray();

            return new FirehoseSnapshot(_globalSeq, messages, since + 1 < _firstAvailableGlobalSeq);
        }
    }

    public async Task<FirehoseSnapshot> ReadFirehoseWithWaitAsync(
        long since, int limit, TimeSpan wait, CancellationToken cancellationToken)
    {
        var deadline = _clock.GetUtcNow() + wait;

        while (true)
        {
            var snapshot = ReadFirehose(since, limit);
            if (snapshot.Messages.Count > 0 || wait <= TimeSpan.Zero)
            {
                return snapshot;
            }

            var remaining = deadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return snapshot;
            }

            Task signal;
            lock (_globalGate)
            {
                if (_globalSeq > since)
                {
                    continue;
                }

                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _globalWaiters.Add(waiter);
                signal = waiter.Task;
            }

            var completed = await Task.WhenAny(signal, Task.Delay(remaining, cancellationToken));
            if (completed != signal)
            {
                return ReadFirehose(since, limit);
            }
        }
    }
```

The `continue` inside the lock is safe: `lock` releases on any exit from the block, including `continue`.

- [ ] **Step 5: Map the endpoint in `ChatEndpoints.cs`**

```csharp
        app.MapGet("/firehose",
            async (long? since, int? limit, int? wait,
                   ChatStore store, ChatOptions options, CancellationToken cancellationToken) =>
            {
                var snapshot = await store.ReadFirehoseWithWaitAsync(
                    since ?? 0, ClampLimit(limit, options), ClampWait(wait, options), cancellationToken);

                return Results.Ok(new FirehoseResponse
                {
                    Cursor = snapshot.Cursor,
                    Messages = snapshot.Messages,
                    Truncated = snapshot.Truncated,
                });
            });
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test tests/AgencyTogether.Api.Tests
```

Expected: PASS, 36 tests.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: read-only firehose across all rooms with long-poll support"
```

---

### Task 9: Room listing, the context brief, and DELETE

**Files:**
- Create: `src/AgencyTogether.Api/ContextFormatter.cs`
- Modify: `src/AgencyTogether.Api/Room.cs`, `src/AgencyTogether.Api/ChatStore.cs`, `src/AgencyTogether.Api/Models.cs`, `src/AgencyTogether.Api/ChatEndpoints.cs`
- Test: `tests/AgencyTogether.Api.Tests/RoomAdminTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2–8.
- Produces:
  - `record RoomSummary { string Room; int MessageCount; long Cursor; IReadOnlyList<string> Agents; DateTimeOffset? LastActivity; }`
  - `record RoomListResponse { IReadOnlyList<RoomSummary> Rooms; }`
  - `record ContextResponse { string Room; long Cursor; IReadOnlyList<ChatMessage> Messages; IReadOnlyList<AgentPresence> Agents; bool Truncated; string Brief; }`
  - `Room.Summarize() -> RoomSummary`, `Room.Clear()`
  - `ChatStore.ListRooms() -> IReadOnlyList<RoomSummary>`, `ChatStore.ClearRoom(string roomId)`
  - `ContextFormatter.Render(string room, long cursor, IReadOnlyList<AgentPresence> agents, IReadOnlyList<ChatMessage> messages) -> string`

- [ ] **Step 1: Write the failing tests**

Create `tests/AgencyTogether.Api.Tests/RoomAdminTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;

namespace AgencyTogether.Api.Tests;

public class RoomAdminTests
{
    private static async Task PostAsync(HttpClient client, string room, string id, string goal, string content)
    {
        var response = await client.PostAsJsonAsync($"/rooms/{room}/messages", new
        {
            id, name = id, goal, content,
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Room_listing_reports_counts_and_agents()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Ship it", "one");
        await PostAsync(client, "alpha", "codex-1", "Test it", "two");

        var list = await client.GetFromJsonAsync<RoomListResponse>("/rooms", TestJson.Options);

        var alpha = list!.Rooms.Single(r => r.Room == "alpha");
        Assert.Equal(2, alpha.MessageCount);
        Assert.Equal(2, alpha.Cursor);
        Assert.Equal(new[] { "claude-1", "codex-1" }, alpha.Agents.OrderBy(a => a));
        Assert.NotNull(alpha.LastActivity);
    }

    [Fact]
    public async Task Room_listing_always_includes_the_global_room()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<RoomListResponse>("/rooms", TestJson.Options);

        var global = list!.Rooms.Single(r => r.Room == "global");
        Assert.Equal(0, global.MessageCount);
        Assert.Null(global.LastActivity);
    }

    [Fact]
    public async Task Context_returns_a_brief_naming_every_agent_and_goal()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Build the API", "starting");
        await PostAsync(client, "alpha", "codex-1", "Write the tests", "on it");

        var context = await client.GetFromJsonAsync<ContextResponse>(
            "/rooms/alpha/context", TestJson.Options);

        Assert.Contains("claude-1", context!.Brief);
        Assert.Contains("Build the API", context.Brief);
        Assert.Contains("codex-1", context.Brief);
        Assert.Contains("Write the tests", context.Brief);
        Assert.Contains("starting", context.Brief);
        Assert.Equal(2, context.Messages.Count);
        Assert.Equal(2, context.Cursor);
    }

    [Fact]
    public async Task Context_as_text_returns_plain_text_markdown()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Build the API", "starting");

        var response = await client.GetAsync("/rooms/alpha/context?format=text");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Build the API", body);
        Assert.Contains("starting", body);
    }

    [Fact]
    public async Task Delete_clears_messages_and_roster()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Ship it", "one");

        var deleted = await client.DeleteAsync("/rooms/alpha");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);
        Assert.Empty(read!.Messages);
        Assert.Empty(read.Agents);
    }

    [Fact]
    public async Task Delete_does_not_reset_the_sequence()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Ship it", "one");
        await PostAsync(client, "alpha", "claude-1", "Ship it", "two");
        await client.DeleteAsync("/rooms/alpha");
        await PostAsync(client, "alpha", "claude-1", "Ship it", "three");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages", TestJson.Options);

        Assert.Equal(3, read!.Messages.Single().Seq);
        Assert.Equal(3, read.Cursor);
    }

    [Fact]
    public async Task A_cursor_from_before_a_delete_is_reported_as_truncated()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "alpha", "claude-1", "Ship it", "one");
        await client.DeleteAsync("/rooms/alpha");

        var read = await client.GetFromJsonAsync<RoomReadResponse>(
            "/rooms/alpha/messages?since=0", TestJson.Options);

        Assert.True(read!.Truncated);
    }

    [Fact]
    public async Task Deleting_the_global_room_clears_it_but_keeps_it_listed()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        await PostAsync(client, "global", "claude-1", "Ship it", "one");
        await client.DeleteAsync("/rooms/global");

        var list = await client.GetFromJsonAsync<RoomListResponse>("/rooms", TestJson.Options);

        var global = list!.Rooms.Single(r => r.Room == "global");
        Assert.Equal(0, global.MessageCount);
    }

    [Fact]
    public async Task Deleting_a_room_that_does_not_exist_is_a_no_op()
    {
        using var factory = new ChatApiFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/rooms/never-existed");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/AgencyTogether.Api.Tests --filter FullyQualifiedName~RoomAdminTests
```

Expected: FAIL to compile — `RoomListResponse` and `ContextResponse` do not exist. After adding those types, the endpoints still 404/405.

- [ ] **Step 3: Add the response types to `Models.cs`**

```csharp
public sealed record RoomSummary
{
    public required string Room { get; init; }
    public required int MessageCount { get; init; }
    public required long Cursor { get; init; }
    public required IReadOnlyList<string> Agents { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
}

public sealed record RoomListResponse
{
    public required IReadOnlyList<RoomSummary> Rooms { get; init; }
}

public sealed record ContextResponse
{
    public required string Room { get; init; }
    public required long Cursor { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required IReadOnlyList<AgentPresence> Agents { get; init; }
    public required bool Truncated { get; init; }
    public required string Brief { get; init; }
}
```

- [ ] **Step 4: Add `Summarize` and `Clear` to `Room.cs`**

`Clear` moves `_firstAvailableSeq` past the current head, so a caller holding a pre-delete cursor is correctly told it is truncated — and `_seq` itself is untouched.

```csharp
    public RoomSummary Summarize()
    {
        lock (_gate)
        {
            return new RoomSummary
            {
                Room = Id,
                MessageCount = _messages.Count,
                Cursor = _seq,
                Agents = _agents.Values
                    .OrderBy(a => a.FirstSeen)
                    .ThenBy(a => a.AgentId, StringComparer.Ordinal)
                    .Select(a => a.AgentId)
                    .ToArray(),
                LastActivity = _messages.Count == 0 ? null : _messages[^1].Timestamp,
            };
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _messages.Clear();
            _agents.Clear();
            _firstAvailableSeq = _seq + 1;
            ReleaseWaiters();
        }
    }
```

- [ ] **Step 5: Add `ListRooms` and `ClearRoom` to `ChatStore.cs`**

```csharp
    public IReadOnlyList<RoomSummary> ListRooms()
    {
        lock (_globalGate)
        {
            return _rooms.Values
                .Select(r => r.Summarize())
                .OrderBy(r => r.Room, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// Clears a room's messages and roster. The room's seq counter is deliberately
    /// left alone so cursors held by polling agents stay valid. The global room is
    /// cleared but never removed.
    /// </summary>
    public void ClearRoom(string roomId)
    {
        lock (_globalGate)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                room.Clear();
            }
        }
    }
```

- [ ] **Step 6: Write `ContextFormatter.cs`**

```csharp
using System.Text;

namespace AgencyTogether.Api;

public static class ContextFormatter
{
    public static string Render(
        string room,
        long cursor,
        IReadOnlyList<AgentPresence> agents,
        IReadOnlyList<ChatMessage> messages)
    {
        var brief = new StringBuilder();

        brief.Append("# Agency room: ").AppendLine(room);
        brief.Append("Cursor: ").Append(cursor).AppendLine();
        brief.AppendLine();

        brief.AppendLine("## Agents");
        if (agents.Count == 0)
        {
            brief.AppendLine("_No agents have posted yet._");
        }
        else
        {
            foreach (var agent in agents)
            {
                brief.Append("- **").Append(agent.Name).Append("** (`").Append(agent.AgentId)
                     .Append("`) — goal: ")
                     .AppendLine(string.IsNullOrWhiteSpace(agent.Goal) ? "_none stated_" : agent.Goal);
            }
        }

        brief.AppendLine();
        brief.AppendLine("## Transcript");
        if (messages.Count == 0)
        {
            brief.AppendLine("_No messages yet._");
            return brief.ToString();
        }

        foreach (var message in messages)
        {
            brief.Append('[').Append(message.Seq).Append("] ")
                 .Append(message.Name).Append(" (`").Append(message.AgentId).Append("`) ")
                 .AppendLine(message.Timestamp.UtcDateTime.ToString("O"));
            brief.AppendLine(message.Content);
            brief.AppendLine();
        }

        return brief.ToString();
    }
}
```

- [ ] **Step 7: Map the endpoints in `ChatEndpoints.cs`**

```csharp
        app.MapGet("/rooms", (ChatStore store)
            => Results.Ok(new RoomListResponse { Rooms = store.ListRooms() }));

        app.MapGet("/rooms/{roomId}/context",
            async (string roomId, long? since, int? limit, int? wait, string? format,
                   ChatStore store, ChatOptions options, CancellationToken cancellationToken) =>
            {
                if (!TryNormalizeRoomId(roomId, out var normalizedRoom))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["roomId"] = new[] { "roomId must match ^[a-z0-9][a-z0-9_-]{0,63}$." },
                    });
                }

                var snapshot = await store.ReadWithWaitAsync(
                    normalizedRoom, since ?? 0, ClampLimit(limit, options),
                    ClampWait(wait, options), cancellationToken);

                var brief = ContextFormatter.Render(
                    normalizedRoom, snapshot.Cursor, snapshot.Agents, snapshot.Messages);

                if (string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Text(brief, "text/plain");
                }

                return Results.Ok(new ContextResponse
                {
                    Room = normalizedRoom,
                    Cursor = snapshot.Cursor,
                    Messages = snapshot.Messages,
                    Agents = snapshot.Agents,
                    Truncated = snapshot.Truncated,
                    Brief = brief,
                });
            });

        app.MapDelete("/rooms/{roomId}", (string roomId, ChatStore store) =>
        {
            if (!TryNormalizeRoomId(roomId, out var normalizedRoom))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["roomId"] = new[] { "roomId must match ^[a-z0-9][a-z0-9_-]{0,63}$." },
                });
            }

            store.ClearRoom(normalizedRoom);
            return Results.NoContent();
        });
```

- [ ] **Step 8: Run the tests to verify they pass**

```bash
dotnet test tests/AgencyTogether.Api.Tests
```

Expected: PASS, 45 tests.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat: room listing, markdown context brief, and room reset"
```

---

### Task 10: README and agent onboarding

**Files:**
- Create: `README.md`
- Test: manual — run the service and execute the documented curls.

**Interfaces:**
- Consumes: the full endpoint surface from Tasks 1–9.
- Produces: nothing consumed by code.

- [ ] **Step 1: Verify the whole suite is green before documenting**

```bash
dotnet test
```

Expected: PASS, 45 tests, zero warnings. Do not write the README against untested behaviour.

- [ ] **Step 2: Run the service and confirm the documented flow by hand**

```bash
dotnet dev-certs https --trust
dotnet run --project src/AgencyTogether.Api
```

In a second shell, verify each command below actually returns what the README will claim. Fix the README, not the output.

```bash
curl -k -X POST https://localhost:7171/messages -H "Content-Type: application/json" -d '{"id":"claude-1","name":"Claude","goal":"Build the API","content":"Store and endpoints are done."}'
curl -k "https://localhost:7171/rooms/global/context?format=text"
```

- [ ] **Step 3: Write `README.md`**

Include, in this order:

1. **What it is** — two sentences: a shared in-memory context API so several subscription agents collaborate in one conversation; a message is `{id, name, goal, content}`.
2. **Run it** — the two commands from Step 2, plus the note that `http://localhost:5171` exists for harnesses that reject the self-signed certificate, and that `-k` is needed on the HTTPS port.
3. **Endpoint table** — copy the table from spec section 6.1, with one working `curl` per row using the values verified in Step 2.
4. **Cursors** — state plainly that room reads use `seq` and `/firehose` uses `globalSeq`, that the two are not interchangeable, and that every response echoes the `cursor` to send back next.
5. **The agent prompt block** — a fenced block the user pastes into Claude or Codex, with `AGENT_ID`, `AGENT_NAME`, `ROOM`, and `BASE_URL` marked as the four things to fill in. It must instruct the agent to: POST its goal and progress after each meaningful step; GET with `?since=<cursor>&wait=30` to block until a teammate replies; carry the returned `cursor` forward; and read `/rooms/{room}/context?format=text` when it needs the full picture.
6. **Worked two-agent example** — an annotated transcript of Claude and Codex alternating through `alpha`, showing the cursor advancing.
7. **Limits** — the table from spec section 7, noting all of it is configurable under the `Chat` section.
8. **Not built yet** — the spec's section 12 list: shared folder, persistence, auth.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "docs: README with endpoint reference and agent onboarding prompt"
```

- [ ] **Step 5: Push**

```bash
git push
```

---

## Self-Review

**1. Spec coverage.** Every spec section maps to a task: §4.1 rooms/global/firehose → Tasks 2, 8; §4.2 dual cursors → Tasks 3, 8; §4.3 storage → Tasks 2, 8; §4.4 long-poll → Tasks 7, 8; §5.1–5.2 models → Task 2; §5.3 goal rule → Tasks 2, 4; §6.1–6.4 core endpoints → Tasks 2, 3, 7; §6.5 context brief → Task 9; §6.6 room listing → Task 9; §6.7 firehose → Task 8; §6.8 delete → Task 9; §7 limits/validation → Tasks 5, 6; §8 layout → Task 1; §9 HTTPS → Tasks 1, 10; §10 tests → distributed across every task; §11 README → Task 10. No gaps.

**2. Placeholder scan.** No "TBD"/"TODO"/"add appropriate error handling"/"similar to Task N". Every code step carries real code. The one prose-only step is Task 10 Step 3, where the README's content is enumerated point by point rather than transcribed — acceptable because it is documentation whose exact wording depends on the Step 2 verification output.

**3. Type consistency.** `RoomSnapshot` (Task 2) is consumed unchanged by Tasks 3, 7, 9. `FirehoseSnapshot` (Task 8) is separate and never confused with it. `ClampLimit`/`ClampWait` (Task 3) are used verbatim in Tasks 7, 8, 9. `TryNormalizeRoomId`/`ValidateRequest` (Task 5) are used verbatim in Tasks 7, 9. `Room.ReleaseWaiters()` is introduced in Task 7 and called by `Room.Clear()` in Task 9 — Task 9 depends on Task 7, and the plan is ordered accordingly. `_firstAvailableSeq` is written in Tasks 2, 7, 9 with one consistent meaning. `ChatEndpoints` becomes `partial` in Task 5 for `[GeneratedRegex]`; no earlier task declares it non-partial in a conflicting file.

**One ordering constraint for executors:** Task 9 Step 4 calls `ReleaseWaiters()`, which Task 7 Step 3 creates. Do not execute Task 9 before Task 7.

**4. Concurrency trace.** Lock order is always `_globalGate` → `Room._gate` (`Post` → `Append`, `ListRooms` → `Summarize`, `ClearRoom` → `Clear`). No path takes them in the reverse order, so there is no deadlock. Reads take only `Room._gate`; `ReadFirehose` takes only `_globalGate`. `TryRegisterWaiter` checks `_seq` and registers under one lock acquisition, so a write cannot land between the check and the registration and strand a reader.

**5. Test-count arithmetic.** 1 → 5 → 9 → 15 → 24 → 27 → 31 → 36 → 45. The totals asserted in each task's "run the tests" step match this progression; if an executor sees a different total, a test was dropped or double-counted and they should stop and reconcile before continuing.
