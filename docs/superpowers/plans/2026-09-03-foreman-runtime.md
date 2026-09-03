# Foreman Worker Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Foreman — the C# service that turns folder-defined "employees" into working agents by spawning `claude`/`codex` CLI runs on the user's subscriptions, moving tasks between them with returning hand-offs, and cycling them through sleep/wake with a written progress ledger.

**Architecture:** One ASP.NET Core minimal-API service holding all employees and tasks in memory and on disk. A `RunSupervisor` spawns at most one CLI run per employee through an `IAgentProvider` adapter; a `TaskBook` owns the task state machine; a `DayCycle` background service drives sleep/wake; an `EventLog` exposes a cursor + long-poll stream for the future office UI. Foreman talks to the existing context-api room service over HTTP, exactly as an employee would.

**Tech Stack:** .NET 8 (`net8.0`), ASP.NET Core Minimal APIs, `System.Text.Json`, `Microsoft.Extensions.Hosting.BackgroundService`, `TimeProvider`; tests use xunit, `Microsoft.AspNetCore.Mvc.Testing`, and `Microsoft.Extensions.TimeProvider.Testing` 8.10.0.

**Spec:** `docs/superpowers/specs/2026-09-03-foreman-design.md`

## Global Constraints

Apply to every task. Values copied verbatim from the spec.

- Target `net8.0`; `global.json` stays pinned to SDK `8.0.417`, `rollForward: latestFeature`.
- Agents run **only** through the `claude` and `codex` CLIs on the user's subscription. Foreman never calls a model API and holds no API key.
- **The provider must scrub the child environment**: strip every variable whose name starts with `CLAUDE`, `CLAUDECODE`, or `ANTHROPIC` before launching a CLI, or the run is refused with the nested-session guard. This is correctness, not tidiness.
- Two independent cursor spaces already exist in context-api; Foreman adds one more: `RuntimeEvent.seq`, monotonic, never reset while the process lives. Timeout on a long-poll returns `200` with an empty list, never `204`/`404` — same contract as context-api.
- Errors are RFC 7807 problem details via `Results.Problem` / `Results.ValidationProblem`.
- Room ids and employee ids share the rule `^[a-z0-9][a-z0-9_-]{0,63}$`.
- Foreman binds `http://localhost:5172` and `https://localhost:7172`. context-api keeps `5171`/`7171`.
- All time comes from an injected `TimeProvider`. No `DateTimeOffset.UtcNow` / `DateTime.Now` anywhere in production code — tests drive the clock.
- Config lives under the `Foreman` section (see spec §13); every limit is settable via `appsettings.json` or `Foreman__Key` env vars.
- The build runs while the service may be running from `bin/Debug`, which locks the exe on Windows. Every `dotnet test` in this plan passes `-p:ArtifactsPath=./artifacts` to build test binaries outside `bin/`.

---

## File Structure

After Task 1 the repo is the `home-workplace` monorepo:

```
home-workplace/
├── HomeWorkplace.sln
├── global.json
├── .claude/launch.json                         # context-api (5171) + foreman (5172)
├── employees/                                   # starter pack (Task 14)
├── services/
│   ├── context-api/                             # the existing room API, moved & renamed
│   │   ├── src/HomeWorkplace.ContextApi/
│   │   └── tests/HomeWorkplace.ContextApi.Tests/
│   └── foreman/
│       ├── src/HomeWorkplace.Foreman/
│       │   ├── HomeWorkplace.Foreman.csproj
│       │   ├── Program.cs                        # host, DI, Kestrel, endpoint wiring
│       │   ├── ForemanOptions.cs                 # every limit/path
│       │   ├── Models.cs                         # EmployeeDefinition, Task, RunSpec, RunResult, events…
│       │   ├── EventLog.cs                        # cursor + long-poll ring
│       │   ├── EmployeeCatalog.cs                 # load employees/*/, hold state, reload
│       │   ├── TaskBook.cs                        # Task records + state machine (only writer)
│       │   ├── FileStore.cs                       # atomic JSON persistence + replay
│       │   ├── PersonaComposer.cs                 # system + run prompts
│       │   ├── RunSpec building lives in TaskBook/Supervisor
│       │   ├── IAgentProvider.cs                  # + RunSpec/RunResult usage
│       │   ├── ClaudeCliProvider.cs
│       │   ├── CodexCliProvider.cs
│       │   ├── ProcessRunner.cs                   # env-scrubbed process launch + timeout
│       │   ├── RunSupervisor.cs                   # one run per employee; drives providers
│       │   ├── DayCycle.cs                        # BackgroundService: sleep/wake ticks
│       │   ├── ContextApiClient.cs               # HTTP client for the room API
│       │   ├── ForemanEndpoints.cs               # /employees, /tasks, /events routing
│       │   └── appsettings.json
│       └── tests/HomeWorkplace.Foreman.Tests/
│           ├── ForemanFactory.cs                 # WebApplicationFactory + fakes wired in
│           ├── FakeAgentProvider.cs              # scripted RunResults; records RunSpecs
│           ├── FakeContextApi.cs                 # in-process stub of the 4 room calls
│           ├── HealthTests.cs
│           ├── EventLogTests.cs
│           ├── EmployeeCatalogTests.cs
│           ├── TaskLifecycleTests.cs
│           ├── ApprovalTests.cs
│           ├── HandoffTests.cs
│           ├── ReassignTests.cs
│           ├── DayCycleTests.cs
│           ├── RestartTests.cs
│           └── ProviderTests.cs                  # argv + env scrub + fixture parsing
├── docs/{superpowers,agents,trials}/
└── scripts/acceptance.ps1                        # real-CLI acceptance (Task 14)
```

Each Foreman test class news up its **own** `ForemanFactory` in a `using` and points `Foreman:DataPath` at a fresh temp folder, because tasks and state persist to disk and would leak between tests otherwise.

---

### Task 1: Monorepo migration (context-api move + Foreman scaffold)

Renames the repo into the product monorepo and stands up an empty Foreman service. The 68 existing context-api tests are the guard: they must pass before and after, unchanged in behaviour.

**Files:**
- Rename: local folder `Agency Together` → `home-workplace`; `AgencyTogether.sln` → `HomeWorkplace.sln`
- Move: `src/AgencyTogether.Api` → `services/context-api/src/HomeWorkplace.ContextApi`; `tests/AgencyTogether.Api.Tests` → `services/context-api/tests/HomeWorkplace.ContextApi.Tests`
- Create: `services/foreman/src/HomeWorkplace.Foreman/*`, `services/foreman/tests/HomeWorkplace.Foreman.Tests/*`
- Modify: both context-api `.csproj` (paths/RootNamespace), all context-api `.cs` (namespace), `.claude/launch.json` (both repo-level and project-level), `README.md` paths

**Interfaces:**
- Consumes: nothing.
- Produces: solution `HomeWorkplace.sln` building context-api (namespace `HomeWorkplace.ContextApi`) and an empty Foreman web project; `.claude/launch.json` with entries `context-api` (5171) and `foreman` (5172).

- [ ] **Step 1: Confirm the service is stopped and tests are green before touching anything**

```bash
curl -s -m 3 http://localhost:5171/health || echo "down (good)"
cd "C:/Users/raphe/Desktop/Both/Agency Together" && dotnet test -p:ArtifactsPath=./artifacts 2>&1 | grep -E "Passed!|Failed!"
```

Expected: service down, `Passed! … 68`. If the service is up, stop it (in this harness, `preview_stop`; otherwise Ctrl+C its terminal) before continuing — the folder cannot be renamed while the exe is loaded.

- [ ] **Step 2: Rename the top folder and solution**

From the parent directory so nothing holds the target:

```bash
cd "C:/Users/raphe/Desktop/Both" && mv "Agency Together" home-workplace && cd home-workplace && git mv AgencyTogether.sln HomeWorkplace.sln
```

All later steps run from `C:/Users/raphe/Desktop/Both/home-workplace`.

- [ ] **Step 3: Move the project and test folders under `services/context-api`**

```bash
mkdir -p services/context-api services/foreman
git mv src services/context-api/src
git mv tests services/context-api/tests
git mv services/context-api/src/AgencyTogether.Api services/context-api/src/HomeWorkplace.ContextApi
git mv services/context-api/tests/AgencyTogether.Api.Tests services/context-api/tests/HomeWorkplace.ContextApi.Tests
git mv services/context-api/src/HomeWorkplace.ContextApi/AgencyTogether.Api.csproj services/context-api/src/HomeWorkplace.ContextApi/HomeWorkplace.ContextApi.csproj
git mv services/context-api/tests/HomeWorkplace.ContextApi.Tests/AgencyTogether.Api.Tests.csproj services/context-api/tests/HomeWorkplace.ContextApi.Tests/HomeWorkplace.ContextApi.Tests.csproj
```

- [ ] **Step 4: Rewrite namespaces and project references**

The namespace `AgencyTogether.Api` maps cleanly to `HomeWorkplace.ContextApi` (the test namespace `AgencyTogether.Api.Tests` becomes `HomeWorkplace.ContextApi.Tests`, and C# name-resolution still lets the test project see the API types unqualified).

```bash
grep -rl "AgencyTogether" services/context-api --include=*.cs --include=*.csproj \
  | xargs sed -i 's/AgencyTogether\.Api/HomeWorkplace.ContextApi/g'
# Fix the test project's <ProjectReference> path (folder name changed):
sed -i 's#AgencyTogether\.Api/AgencyTogether\.Api\.csproj#HomeWorkplace.ContextApi/HomeWorkplace.ContextApi.csproj#g; s#\.\./\.\./src/HomeWorkplace\.ContextApi#../../src/HomeWorkplace.ContextApi#g' \
  services/context-api/tests/HomeWorkplace.ContextApi.Tests/HomeWorkplace.ContextApi.Tests.csproj
grep -rc "AgencyTogether" services/context-api || echo "no AgencyTogether references remain (good)"
```

Verify the `<ProjectReference>` in the test csproj resolves to
`..\..\src\HomeWorkplace.ContextApi\HomeWorkplace.ContextApi.csproj`; fix by hand if the sed above left it wrong.

- [ ] **Step 5: Rebuild the solution file**

```bash
rm HomeWorkplace.sln && dotnet new sln -n HomeWorkplace
dotnet sln add services/context-api/src/HomeWorkplace.ContextApi/HomeWorkplace.ContextApi.csproj \
               services/context-api/tests/HomeWorkplace.ContextApi.Tests/HomeWorkplace.ContextApi.Tests.csproj
```

- [ ] **Step 6: Verify the move — 68 tests still green**

```bash
dotnet test HomeWorkplace.sln -p:ArtifactsPath=./artifacts 2>&1 | grep -E "Passed!|Failed!|error"
```

Expected: `Passed! … 68`, no build errors. If a type is "not found", a namespace slipped in Step 4 — fix and re-run. Do not proceed until green.

- [ ] **Step 7: Scaffold the empty Foreman project**

```bash
dotnet new web -n HomeWorkplace.Foreman -o services/foreman/src/HomeWorkplace.Foreman
dotnet new xunit -n HomeWorkplace.Foreman.Tests -o services/foreman/tests/HomeWorkplace.Foreman.Tests
rm services/foreman/tests/HomeWorkplace.Foreman.Tests/UnitTest1.cs
dotnet sln add services/foreman/src/HomeWorkplace.Foreman/HomeWorkplace.Foreman.csproj \
               services/foreman/tests/HomeWorkplace.Foreman.Tests/HomeWorkplace.Foreman.Tests.csproj
dotnet add services/foreman/tests/HomeWorkplace.Foreman.Tests package Microsoft.AspNetCore.Mvc.Testing --version 8.0.28
dotnet add services/foreman/tests/HomeWorkplace.Foreman.Tests package Microsoft.Extensions.TimeProvider.Testing --version 8.10.0
dotnet add services/foreman/tests/HomeWorkplace.Foreman.Tests reference services/foreman/src/HomeWorkplace.Foreman/HomeWorkplace.Foreman.csproj
dotnet add services/foreman/src/HomeWorkplace.Foreman package Swashbuckle.AspNetCore --version 6.9.0
```

Set the Foreman csproj `<PropertyGroup>` to match context-api: `net8.0`, `Nullable enable`, `ImplicitUsings enable`, `InvariantGlobalization true`, `RootNamespace HomeWorkplace.Foreman`.

- [ ] **Step 8: Update both launch.json files**

Repo-level `.claude/launch.json` (used by this harness — note it lives at `Both/.claude/launch.json`, one level up, and now needs the new folder name):

```json
{
  "version": "0.0.1",
  "configurations": [
    { "name": "context-api", "runtimeExecutable": "dotnet", "runtimeArgs": ["run", "--project", "home-workplace/services/context-api/src/HomeWorkplace.ContextApi"], "port": 5171 },
    { "name": "foreman", "runtimeExecutable": "dotnet", "runtimeArgs": ["run", "--project", "home-workplace/services/foreman/src/HomeWorkplace.Foreman"], "port": 5172 }
  ]
}
```

The in-repo `home-workplace/.claude/launch.json` mirrors it with project-relative paths (`services/context-api/...`, `services/foreman/...`).

- [ ] **Step 9: Commit the migration**

```bash
git add -A && git commit -m "refactor: migrate to home-workplace monorepo; scaffold foreman service"
```

---

### Task 2: Foreman scaffold — options, host, health, test factory

**Files:**
- Create: `services/foreman/src/HomeWorkplace.Foreman/ForemanOptions.cs`, `Program.cs` (replace template), `appsettings.json` (replace)
- Create: `services/foreman/tests/HomeWorkplace.Foreman.Tests/ForemanFactory.cs`, `HealthTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `ForemanOptions` with `const string SectionName = "Foreman"` and every key from spec §13 (`EmployeesPath`, `DataPath`, `ContextApiBaseUrl`, `ClaudeExecutable`, `CodexExecutable`, `MaxRunMinutes`, `SchedulerTickSeconds`, `EventsCapacity`, `MaxHandoffDepth`).
  - `public partial class Program`.
  - `ForemanFactory : WebApplicationFactory<Program>` with `static ForemanFactory Create(out string dataPath)` (fresh temp `DataPath`, `EmployeesPath`, faked provider/context-api slots added in later tasks) and `TestJson.Options`.

- [ ] **Step 1: Write the failing test**

`HealthTests.cs`:

```csharp
using System.Net;

namespace HomeWorkplace.Foreman.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_returns_ok()
    {
        using var factory = ForemanFactory.Create(out _);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 2: Write the test factory**

`ForemanFactory.cs`:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HomeWorkplace.Foreman.Tests;

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}

public sealed class ForemanFactory : WebApplicationFactory<Program>
{
    private readonly string _dataPath;
    private readonly string _employeesPath;

    private ForemanFactory(string dataPath, string employeesPath)
    {
        _dataPath = dataPath;
        _employeesPath = employeesPath;
    }

    public static ForemanFactory Create(out string dataPath)
    {
        dataPath = Path.Combine(Path.GetTempPath(), "foreman-tests", Guid.NewGuid().ToString("N"));
        var employeesPath = Path.Combine(dataPath, "employees");
        Directory.CreateDirectory(employeesPath);
        return new ForemanFactory(dataPath, employeesPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Foreman:DataPath", _dataPath);
        builder.UseSetting("Foreman:EmployeesPath", _employeesPath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true); } catch { }
    }
}
```

- [ ] **Step 3: Run the test — expect FAIL**

```bash
dotnet test HomeWorkplace.sln --filter FullyQualifiedName~HealthTests -p:ArtifactsPath=./artifacts
```

Expected: FAIL — `Program` inaccessible (no `public partial class Program`) or `/health` 404.

- [ ] **Step 4: Write `ForemanOptions.cs`**

```csharp
namespace HomeWorkplace.Foreman;

public sealed class ForemanOptions
{
    public const string SectionName = "Foreman";

    public string EmployeesPath { get; set; } = "../../employees";
    public string DataPath { get; set; } = "./data";
    public string ContextApiBaseUrl { get; set; } = "http://localhost:5171";
    public string ClaudeExecutable { get; set; } = "claude";
    public string CodexExecutable { get; set; } = "codex";
    public int MaxRunMinutes { get; set; } = 30;
    public int SchedulerTickSeconds { get; set; } = 30;
    public int EventsCapacity { get; set; } = 5000;
    public int MaxHandoffDepth { get; set; } = 5;
}
```

- [ ] **Step 5: Write `Program.cs`**

```csharp
using HomeWorkplace.Foreman;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(ForemanOptions.SectionName).Get<ForemanOptions>()
              ?? new ForemanOptions();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", (ForemanOptions o) => Results.Ok(new { status = "ok", contextApi = o.ContextApiBaseUrl }));

app.Run();

public partial class Program;
```

`appsettings.json`: copy context-api's Logging/AllowedHosts, add the Kestrel block binding `http://localhost:5172` + `https://localhost:7172`, and a `Foreman` section with the spec §13 defaults.

- [ ] **Step 6: Run the test — expect PASS**

```bash
dotnet test HomeWorkplace.sln --filter FullyQualifiedName~HealthTests -p:ArtifactsPath=./artifacts
```

Expected: PASS, 1 test.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(foreman): host, options, and health endpoint"
```

---

### Task 3: EventLog — cursor + long-poll runtime stream

The office UI animates from this. It is the same cursor/waiter primitive as context-api's firehose, standalone and testable before anything emits into it.

**Files:**
- Create: `services/foreman/src/HomeWorkplace.Foreman/EventLog.cs`; add `RuntimeEvent` + `EventPage` to `Models.cs` (create `Models.cs`)
- Modify: `Program.cs` (register `EventLog`, map `/events`)
- Test: `services/foreman/tests/HomeWorkplace.Foreman.Tests/EventLogTests.cs`

**Interfaces:**
- Consumes: `TimeProvider`, `ForemanOptions`.
- Produces:
  - `record RuntimeEvent { long Seq; DateTimeOffset Timestamp; string Type; string? EmployeeId; string? TaskId; string? RunId; JsonElement? Data; }`
  - `record EventPage { long Cursor; IReadOnlyList<RuntimeEvent> Events; bool Truncated; }`
  - `EventLog` with `void Emit(string type, string? employeeId = null, string? taskId = null, string? runId = null, object? data = null)`, `EventPage Read(long since, int limit)`, `Task<EventPage> ReadWithWaitAsync(long since, int limit, TimeSpan wait, CancellationToken ct)`, and `IReadOnlyList<RuntimeEvent> Snapshot()` (for FileStore persistence in Task 12).

- [ ] **Step 1: Write the failing tests**

`EventLogTests.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;
using Microsoft.Extensions.Time.Testing;

namespace HomeWorkplace.Foreman.Tests;

public class EventLogTests
{
    private static EventLog NewLog(int capacity = 100)
        => new(new ForemanOptions { EventsCapacity = capacity }, new FakeTimeProvider());

    [Fact]
    public void Emit_then_read_returns_events_after_the_cursor()
    {
        var log = NewLog();
        log.Emit("task.state", taskId: "t1");
        log.Emit("run.started", taskId: "t1", runId: "r1");

        var page = log.Read(since: 1, limit: 100);

        var only = Assert.Single(page.Events);
        Assert.Equal("run.started", only.Type);
        Assert.Equal(2, page.Cursor);
        Assert.False(page.Truncated);
    }

    [Fact]
    public void A_cursor_older_than_the_ring_is_truncated()
    {
        var log = NewLog(capacity: 2);
        log.Emit("a"); log.Emit("b"); log.Emit("c");

        var page = log.Read(since: 0, limit: 100);

        Assert.True(page.Truncated);
        Assert.Equal(new[] { "b", "c" }, page.Events.Select(e => e.Type));
    }

    [Fact]
    public async Task A_long_poll_is_released_by_a_concurrent_emit()
    {
        var log = NewLog();
        var pending = log.ReadWithWaitAsync(0, 100, TimeSpan.FromSeconds(20), CancellationToken.None);
        await Task.Delay(200);
        log.Emit("woke.up");

        var page = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("woke.up", Assert.Single(page.Events).Type);
    }

    [Fact]
    public async Task Events_endpoint_streams_over_http()
    {
        using var factory = ForemanFactory.Create(out _);
        using var client = factory.CreateClient();

        var page = await client.GetFromJsonAsync<EventPage>("/events?since=0", TestJson.Options);

        Assert.NotNull(page);
        Assert.False(page!.Truncated);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`EventLog` and `EventPage` undefined).

```bash
dotnet test HomeWorkplace.sln --filter FullyQualifiedName~EventLogTests -p:ArtifactsPath=./artifacts
```

- [ ] **Step 3: Create `Models.cs` with the event types**

```csharp
using System.Text.Json;

namespace HomeWorkplace.Foreman;

public sealed record RuntimeEvent
{
    public required long Seq { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Type { get; init; }
    public string? EmployeeId { get; init; }
    public string? TaskId { get; init; }
    public string? RunId { get; init; }
    public JsonElement? Data { get; init; }
}

public sealed record EventPage
{
    public required long Cursor { get; init; }
    public required IReadOnlyList<RuntimeEvent> Events { get; init; }
    public required bool Truncated { get; init; }
}
```

- [ ] **Step 4: Write `EventLog.cs`**

Model it on context-api's firehose: a `Queue<RuntimeEvent>` guarded by one lock, a `_firstAvailableSeq` retention floor, and a `List<TaskCompletionSource>` released on emit.

```csharp
using System.Text.Json;

namespace HomeWorkplace.Foreman;

public sealed class EventLog
{
    private readonly ForemanOptions _options;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Queue<RuntimeEvent> _events = new();
    private readonly List<TaskCompletionSource> _waiters = new();

    private long _seq;
    private long _firstAvailableSeq = 1;

    public EventLog(ForemanOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
    }

    public void Emit(string type, string? employeeId = null, string? taskId = null, string? runId = null, object? data = null)
    {
        lock (_gate)
        {
            var evt = new RuntimeEvent
            {
                Seq = ++_seq,
                Timestamp = _clock.GetUtcNow(),
                Type = type,
                EmployeeId = employeeId,
                TaskId = taskId,
                RunId = runId,
                Data = data is null ? null : JsonSerializer.SerializeToElement(data),
            };
            _events.Enqueue(evt);
            while (_events.Count > _options.EventsCapacity)
            {
                _firstAvailableSeq = _events.Dequeue().Seq + 1;
            }
            foreach (var w in _waiters) w.TrySetResult();
            _waiters.Clear();
        }
    }

    public EventPage Read(long since, int limit)
    {
        lock (_gate)
        {
            var events = _events.Where(e => e.Seq > since).Take(limit).ToArray();
            return new EventPage { Cursor = _seq, Events = events, Truncated = since + 1 < _firstAvailableSeq };
        }
    }

    public async Task<EventPage> ReadWithWaitAsync(long since, int limit, TimeSpan wait, CancellationToken cancellationToken)
    {
        var deadline = _clock.GetUtcNow() + wait;
        while (true)
        {
            var page = Read(since, limit);
            if (page.Events.Count > 0 || wait <= TimeSpan.Zero) return page;

            var remaining = deadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) return page;

            Task signal;
            lock (_gate)
            {
                if (_seq > since) continue;
                var w = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(w);
                signal = w.Task;
            }
            var completed = await Task.WhenAny(signal, Task.Delay(remaining, cancellationToken));
            if (completed != signal) return Read(since, limit);
        }
    }

    public IReadOnlyList<RuntimeEvent> Snapshot()
    {
        lock (_gate) { return _events.ToArray(); }
    }
}
```

- [ ] **Step 5: Register and map in `Program.cs`**

Add `builder.Services.AddSingleton<EventLog>();` and, with the clamp helpers copied from context-api (`ClampWait`/`ClampLimit` — inline them or add a small `Http` static):

```csharp
app.MapGet("/events", async (long? since, int? limit, int? wait, EventLog log, ForemanOptions o, CancellationToken ct) =>
{
    var w = wait is null or <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(wait.Value, 60));
    var l = limit is null or <= 0 ? 200 : Math.Min(limit.Value, 500);
    return Results.Ok(await log.ReadWithWaitAsync(since ?? 0, l, w, ct));
});
```

- [ ] **Step 6: Run — expect PASS** (5 tests total).

```bash
dotnet test HomeWorkplace.sln --filter FullyQualifiedName~EventLogTests -p:ArtifactsPath=./artifacts
```

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat(foreman): runtime EventLog with cursor and long-poll /events"
```

---

### Task 4: EmployeeCatalog — load definitions, hold state, GET /employees

**Files:**
- Create: `services/foreman/src/HomeWorkplace.Foreman/EmployeeCatalog.cs`; add `EmployeeDefinition`, `Schedule`, `EmployeeState`, `EmployeeStatus`, `Vendor`, `EmployeeView` to `Models.cs`
- Modify: `Program.cs` (register catalog, map `/employees`, `/employees/reload`)
- Test: `EmployeeCatalogTests.cs`

**Interfaces:**
- Consumes: `ForemanOptions`, `TimeProvider`, `EventLog`.
- Produces:
  - `enum Vendor { Claude, Codex }`, `enum EmployeeStatus { Awake, Asleep, Working, Waiting }`
  - `record Schedule(string Wake, string Sleep)` with `TimeOnly WakeTime`/`SleepTime` parse helpers.
  - `record EmployeeDefinition { string Id; string Name; string Role; Vendor Vendor; string Model; string? Effort; IReadOnlyList<string> ClaudeAllowedTools; string? CodexSandbox; Schedule Schedule; int? MaxRunMinutes; string SkillsMd; string LifeMd; }`
  - `record EmployeeState { string Id; EmployeeStatus Status; string? CurrentTaskId; int RunsToday; DateTimeOffset? LastRunAt; DateTimeOffset? AwakeOverrideUntil; int Energy; }`
  - `EmployeeCatalog` with `void Load()`, `IReadOnlyList<EmployeeDefinition> Definitions`, `EmployeeDefinition? Find(string id)`, `EmployeeState GetState(string id)`, `void SetState(EmployeeState)`, `IReadOnlyList<EmployeeView> List()` (definition + state joined).

- [ ] **Step 1: Write the failing tests**

`EmployeeCatalogTests.cs` (writes employee folders into the factory's `EmployeesPath`, then asserts over HTTP):

```csharp
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class EmployeeCatalogTests
{
    private static void WriteEmployee(string employeesPath, string id, string json, string skills = "skills", string life = "life")
    {
        var dir = Path.Combine(employeesPath, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "employee.json"), json);
        File.WriteAllText(Path.Combine(dir, "skills.md"), skills);
        File.WriteAllText(Path.Combine(dir, "life.md"), life);
    }

    private const string AdaJson = """
    { "id": "ada-coder", "name": "Ada", "role": "Engineer", "vendor": "claude",
      "model": "claude-haiku-4-5-20251001", "effort": "low",
      "claudeAllowedTools": ["Read","Edit"], "schedule": { "wake": "09:00", "sleep": "20:00" } }
    """;

    [Fact]
    public async Task Loads_an_employee_from_disk_with_its_md_files()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteEmployee(Path.Combine(dataPath, "employees"), "ada-coder", AdaJson, skills: "TDD always", life: "sleeps at 8");
        using var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<List<EmployeeView>>("/employees", TestJson.Options);

        var ada = Assert.Single(list!);
        Assert.Equal("Ada", ada.Name);
        Assert.Equal(Vendor.Claude, ada.Vendor);
        Assert.Equal(EmployeeStatus.Asleep, ada.Status); // starts asleep until DayCycle wakes it
        Assert.Equal(100, ada.Energy);
    }

    [Fact]
    public async Task A_malformed_employee_json_names_the_file_and_does_not_crash_startup()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteEmployee(Path.Combine(dataPath, "employees"), "broken", "{ not json ");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/employees");

        response.EnsureSuccessStatusCode(); // startup survived
        var list = await response.Content.ReadFromJsonAsync<List<EmployeeView>>(TestJson.Options);
        Assert.DoesNotContain(list!, e => e.Id == "broken");
    }

    [Fact]
    public async Task Reload_picks_up_a_newly_added_employee()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        using var client = factory.CreateClient();
        Assert.Empty((await client.GetFromJsonAsync<List<EmployeeView>>("/employees", TestJson.Options))!);

        WriteEmployee(Path.Combine(dataPath, "employees"), "ada-coder", AdaJson);
        await client.PostAsync("/employees/reload", content: null);

        var list = await client.GetFromJsonAsync<List<EmployeeView>>("/employees", TestJson.Options);
        Assert.Single(list!);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (types + endpoints undefined).

- [ ] **Step 3: Add the models to `Models.cs`**

Include `EmployeeView` (the API projection = definition fields + state fields). Write `Schedule` with:

```csharp
public sealed record Schedule(string Wake, string Sleep)
{
    public TimeOnly WakeTime => TimeOnly.Parse(Wake);
    public TimeOnly SleepTime => TimeOnly.Parse(Sleep);
}
```

`EmployeeState` starts `Asleep`, `RunsToday = 0`, `Energy = 100`. Add a static `EmployeeState.Initial(string id)`.

- [ ] **Step 4: Write `EmployeeCatalog.cs`**

`Load()` enumerates `EmployeesPath/*/employee.json`, deserializes each with a `JsonSerializerOptions` using `JsonStringEnumConverter` (so `"claude"` → `Vendor.Claude`), reads sibling `skills.md`/`life.md`, and on any parse error emits a `catalog.error` event carrying the folder path and skips that employee (never throws). State is kept in a `ConcurrentDictionary<string, EmployeeState>`, seeded `Initial` for a newly seen id and preserved across `Load()` for existing ids. `Energy` is computed `Math.Max(0, 100 - 10 * RunsToday)` in the `EmployeeView` projection.

Call `Load()` once at construction. Register as a singleton; map:

```csharp
app.MapGet("/employees", (EmployeeCatalog c) => Results.Ok(c.List()));
app.MapGet("/employees/{id}", (string id, EmployeeCatalog c) =>
    c.Find(id) is null ? Results.NotFound() : Results.Ok(c.List().First(e => e.Id == id)));
app.MapPost("/employees/reload", (EmployeeCatalog c) => { c.Load(); return Results.NoContent(); });
```

- [ ] **Step 5: Run — expect PASS** (3 tests).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(foreman): EmployeeCatalog loads folder-defined employees; /employees"
```

---

### Task 5: TaskBook, FileStore, and the task endpoints

Creates the `Task` record, its on-disk persistence, and create/list/get. Tasks are created `queued`; nothing runs yet (employees are asleep and the run engine arrives in Task 6). Introduces the `IContextApiClient` seam and its fake so a created task can announce itself in its room.

**Files:**
- Create: `TaskBook.cs`, `FileStore.cs`, `IContextApiClient.cs`; add `Task`(record), `TaskStatus`, `ProgressEntry`, `RunRecord`, `Usage`, `PendingAnswer`, `HandoffAsk`, `CreateTaskRequest` to `Models.cs`
- Modify: `Program.cs` (register `FileStore`, `TaskBook`, real `ContextApiClient`; map `/tasks`), `ForemanFactory.cs` (register `FakeContextApi`), create `FakeContextApi.cs`
- Test: `TaskLifecycleTests.cs` (create/list/get portion)

**Interfaces:**
- Consumes: `ForemanOptions`, `TimeProvider`, `EventLog`, `EmployeeCatalog`, `IContextApiClient`.
- Produces:
  - `enum TaskStatus { Queued, Running, Waiting, NeedsHuman, Done, Failed, Cancelled }`
  - `record ProgressEntry(string Author, DateOnly Date, IReadOnlyList<string> Done, IReadOnlyList<string> Next)`
  - `record Usage(long DurationMs, long? InputTokens, long? OutputTokens, decimal? CostUsd, int? Turns)`
  - `record RunRecord(string Id, string Employee, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string Status, Usage? Usage, string? ResultSummary)`
  - `record HandoffAsk(string To, string Question)`; `record PendingAnswer(string From, string Text)`
  - `record TaskModel { string Id; string Title; string Brief; string Assignee; TaskStatus Status; bool RequiresApproval; string? ParentId; List<string> ChildIds; string Room; string Workspace; SessionRef? Session; List<ProgressEntry> Progress; List<RunRecord> Runs; PendingAnswer? PendingAnswer; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt; }` (named `TaskModel` to avoid clashing with `System.Threading.Tasks.Task`)
  - `record SessionRef(string Vendor, string SessionId, DateOnly Day)`
  - `record CreateTaskRequest(string? Title, string? Brief, string? Assignee, bool RequiresApproval)`
  - `IContextApiClient` (methods in the code below).
  - `FileStore` with `void SaveTask(TaskModel)`, `void SaveState(EmployeeState)`, `IReadOnlyList<TaskModel> LoadTasks()`, `IReadOnlyList<EmployeeState> LoadStates()`, plus event persistence used in Task 12.
  - `TaskBook` with `TaskModel Create(CreateTaskRequest)`, `TaskModel? Get(string id)`, `IReadOnlyList<TaskModel> List(TaskStatus? status, string? assignee)`, and (used by Task 6+) `void Save(TaskModel)`, `IEnumerable<TaskModel> Queued()`.

- [ ] **Step 1: Write the failing tests**

`TaskLifecycleTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class TaskLifecycleTests
{
    private const string AdaJson = """
    { "id":"ada-coder","name":"Ada","role":"Engineer","vendor":"claude","model":"m",
      "claudeAllowedTools":["Read"],"schedule":{"wake":"09:00","sleep":"20:00"} }
    """;

    private static void WriteAda(string dataPath)
    {
        var dir = Path.Combine(dataPath, "employees", "ada-coder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "employee.json"), AdaJson);
        File.WriteAllText(Path.Combine(dir, "skills.md"), "s");
        File.WriteAllText(Path.Combine(dir, "life.md"), "l");
    }

    [Fact]
    public async Task Creating_a_task_returns_it_queued_and_announces_it_in_a_room()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteAda(dataPath);
        await using var _ = factory; // ensure Load ran
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/tasks",
            new { title = "Build parser", brief = "Write a JSON parser", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        Assert.Equal(TaskStatus.Queued, created!.Status);
        Assert.Equal("ada-coder", created.Assignee);
        Assert.Equal($"task-{created.Id}", created.Room);
        Assert.Contains(factory.ContextApi.Posts, p => p.Room == created.Room && p.Content.Contains("Build parser"));
    }

    [Fact]
    public async Task Creating_a_task_for_an_unknown_employee_is_400()
    {
        using var factory = ForemanFactory.Create(out _);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tasks",
            new { title = "x", brief = "y", assignee = "ghost" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_and_list_return_created_tasks()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteAda(dataPath);
        using var client = factory.CreateClient();
        var created = await (await client.PostAsJsonAsync("/tasks",
            new { title = "t", brief = "b", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var fetched = await client.GetFromJsonAsync<TaskModel>($"/tasks/{created!.Id}", TestJson.Options);
        var listed = await client.GetFromJsonAsync<List<TaskModel>>("/tasks?assignee=ada-coder", TestJson.Options);

        Assert.Equal(created.Id, fetched!.Id);
        Assert.Single(listed!);
    }
}
```

- [ ] **Step 2: Write `FakeContextApi.cs` and extend `ForemanFactory`**

```csharp
using System.Collections.Concurrent;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public sealed record FakePost(string Room, string AgentId, string Name, string? Goal, string Content);

public sealed class FakeContextApi : IContextApiClient
{
    public ConcurrentQueue<FakePost> Posts { get; } = new();
    public ConcurrentDictionary<string, string> Briefs { get; } = new();     // room -> brief text
    public ConcurrentQueue<(string Room, string Path, string Content)> Files { get; } = new();

    public Task PostAsync(string room, string agentId, string name, string? goal, string content, CancellationToken ct)
    { Posts.Enqueue(new FakePost(room, agentId, name, goal, content)); return Task.CompletedTask; }

    public Task<string> GetBriefAsync(string room, CancellationToken ct)
        => Task.FromResult(Briefs.TryGetValue(room, out var b) ? b : $"# room {room}\n(empty)");

    public Task PutFileAsync(string room, string path, string content, string agentId, string name, CancellationToken ct)
    { Files.Enqueue((room, path, content)); return Task.CompletedTask; }
}
```

In `ForemanFactory`, add `public FakeContextApi ContextApi { get; } = new();` and, in `ConfigureWebHost`, `builder.ConfigureTestServices(s => { s.RemoveAll<IContextApiClient>(); s.AddSingleton<IContextApiClient>(ContextApi); });` (`using Microsoft.Extensions.DependencyInjection.Extensions;`).

- [ ] **Step 3: Run — expect FAIL** (types + `/tasks` undefined).

- [ ] **Step 4: Add the models and `IContextApiClient.cs`**

Add the records above to `Models.cs`. `IContextApiClient`:

```csharp
namespace HomeWorkplace.Foreman;

public interface IContextApiClient
{
    Task PostAsync(string room, string agentId, string name, string? goal, string content, CancellationToken ct);
    Task<string> GetBriefAsync(string room, CancellationToken ct);
    Task PutFileAsync(string room, string path, string content, string agentId, string name, CancellationToken ct);
}
```

Add a real `ContextApiClient : IContextApiClient` using `HttpClient` against `ContextApiBaseUrl` (POST `/rooms/{room}/messages`, GET `/rooms/{room}/context?format=text`, PUT `/rooms/{room}/files/{path}?id=&name=`). Register it in `Program.cs`; tests replace it with the fake.

- [ ] **Step 5: Write `FileStore.cs`**

Atomic writes (`*.tmp` then `File.Move(overwrite:true)`), one file per task/state, replay on load. Directories: `{DataPath}/tasks`, `{DataPath}/employees`, created on construction. Use `JsonSerializerOptions` with `JsonStringEnumConverter` and `WriteIndented`.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeWorkplace.Foreman;

public sealed class FileStore
{
    private static readonly JsonSerializerOptions Json = new()
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private readonly string _tasks, _states;

    public FileStore(ForemanOptions options)
    {
        _tasks = Path.Combine(options.DataPath, "tasks");
        _states = Path.Combine(options.DataPath, "state");
        Directory.CreateDirectory(_tasks);
        Directory.CreateDirectory(_states);
    }

    public void SaveTask(TaskModel t) => WriteAtomic(Path.Combine(_tasks, $"{t.Id}.json"), t);
    public void SaveState(EmployeeState s) => WriteAtomic(Path.Combine(_states, $"{s.Id}.json"), s);

    public IReadOnlyList<TaskModel> LoadTasks() => LoadAll<TaskModel>(_tasks);
    public IReadOnlyList<EmployeeState> LoadStates() => LoadAll<EmployeeState>(_states);

    private static void WriteAtomic<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }

    private static IReadOnlyList<T> LoadAll<T>(string dir)
    {
        var list = new List<T>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            try { if (JsonSerializer.Deserialize<T>(File.ReadAllText(f), Json) is { } v) list.Add(v); }
            catch { /* skip a corrupt file rather than fail startup */ }
        }
        return list;
    }
}
```

- [ ] **Step 6: Write `TaskBook.cs`**

Holds `ConcurrentDictionary<string, TaskModel>`; `Create` validates the assignee against the catalog, mints an id (`Guid.NewGuid().ToString("N")[..8]`), sets `Room = $"task-{id}"`, `Workspace = Path.Combine(DataPath,"workspaces",id)` (created), status `Queued`, persists via `FileStore`, emits `task.state` on `EventLog`, and posts "Task created: {title}" to the room via `IContextApiClient`. `Save` persists + emits. `List` filters. Register singleton. Map:

```csharp
app.MapPost("/tasks", async (CreateTaskRequest req, TaskBook book, EmployeeCatalog cat, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Brief))
        return Results.ValidationProblem(new() { ["body"] = new[] { "title and brief are required." } });
    if (string.IsNullOrWhiteSpace(req.Assignee) || cat.Find(req.Assignee) is null)
        return Results.Problem(detail: $"Unknown employee '{req.Assignee}'.", statusCode: 400);
    var task = await book.CreateAsync(req, ct);
    return Results.Created($"/tasks/{task.Id}", task);
});
app.MapGet("/tasks", (TaskStatus? status, string? assignee, TaskBook book) => Results.Ok(book.List(status, assignee)));
app.MapGet("/tasks/{id}", (string id, TaskBook book) => book.Get(id) is { } t ? Results.Ok(t) : Results.NotFound());
```

(`CreateAsync` because it awaits the room post; keep the signature `Task<TaskModel> CreateAsync(CreateTaskRequest, CancellationToken)`.)

- [ ] **Step 7: Run — expect PASS** (3 tests).

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat(foreman): TaskBook, FileStore persistence, and task endpoints"
```

---

### Task 6: The run engine — provider adapter, persona, supervisor, dispatch

The heart. A queued task assigned to an awake, idle employee starts one CLI run; the result is applied to the task; the employee is freed and the next queued task pumps. All against a `FakeAgentProvider` — no real CLI in tests.

**Files:**
- Create: `IAgentProvider.cs` (+ `RunSpec`, `RunResult`, `RunOutcome`, `SessionMode` in `Models.cs`), `PersonaComposer.cs`, `RunSupervisor.cs`
- Modify: `Program.cs` (register provider, composer, supervisor; call `supervisor.Pump()` from task-create and a minimal `/employees/{id}/wake`), `TaskBook.cs` (`ApplyResult`, `SetSession`), `EmployeeCatalog` (state transition helpers), `ForemanFactory.cs` (register `FakeAgentProvider`)
- Create: `FakeAgentProvider.cs`
- Test: `TaskLifecycleTests.cs` (run portion)

**Interfaces:**
- Consumes: `TaskBook`, `EmployeeCatalog`, `PersonaComposer`, `IAgentProvider`, `IContextApiClient`, `EventLog`, `ForemanOptions`, `TimeProvider`.
- Produces:
  - `enum RunOutcome { Done, Handoff, NeedsHuman, Failed }`, `enum SessionMode { New, Resume }`
  - `record RunSpec { string RunId; EmployeeDefinition Employee; string TaskId; string Workspace; string SystemPrompt; string Prompt; SessionMode Mode; string? SessionId; TimeSpan Timeout; }`
  - `record RunResult { string RunId; RunOutcome Status; string Summary; HandoffAsk? Ask; IReadOnlyList<string> Artifacts; string SessionId; Usage Usage; string RawTail; }`
  - `IAgentProvider { bool Handles(Vendor vendor); Task<RunResult> RunAsync(RunSpec spec, CancellationToken ct); }`
  - `PersonaComposer` with `string BuildSystemPrompt(EmployeeDefinition e, TaskModel t)` and `Task<string> BuildRunPromptAsync(EmployeeDefinition e, TaskModel t, CancellationToken ct)`.
  - `RunSupervisor` with `void Pump()` (start eligible runs) and internal completion handling.

- [ ] **Step 1: Write the failing tests**

Append to `TaskLifecycleTests.cs`:

```csharp
    private static async Task<TaskModel> PollUntil(HttpClient client, string id, Func<TaskModel, bool> done, int seconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var t = await client.GetFromJsonAsync<TaskModel>($"/tasks/{id}", TestJson.Options);
            if (t is not null && done(t)) return t;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("task did not reach the expected state in time");
    }

    [Fact]
    public async Task An_awake_employee_runs_a_created_task_to_done()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteAda(dataPath);
        factory.Provider.EnqueueDone("parser shipped");
        using var client = factory.CreateClient();
        await client.PostAsync("/employees/ada-coder/wake", null);

        var created = await (await client.PostAsJsonAsync("/tasks",
            new { title = "Build parser", brief = "Write it", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var done = await PollUntil(client, created!.Id, t => t.Status == TaskStatus.Done);
        Assert.Single(done.Runs);
        Assert.Equal("parser shipped", done.Runs[0].ResultSummary);
    }

    [Fact]
    public async Task The_run_spec_carries_persona_brief_and_room_context()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteAda(dataPath);
        factory.Provider.EnqueueDone();
        using var client = factory.CreateClient();
        await client.PostAsync("/employees/ada-coder/wake", null);
        var created = await (await client.PostAsJsonAsync("/tasks",
            new { title = "Build parser", brief = "Write a JSON parser", assignee = "ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        await PollUntil(client, created!.Id, t => t.Status == TaskStatus.Done);
        var spec = Assert.Single(factory.Provider.Specs);
        Assert.Contains("Ada", spec.SystemPrompt);           // identity
        Assert.Contains("Write a JSON parser", spec.Prompt);  // brief
        Assert.Equal(SessionMode.New, spec.Mode);
    }

    [Fact]
    public async Task Only_one_run_per_employee_at_a_time_the_second_task_queues_then_runs()
    {
        using var factory = ForemanFactory.Create(out var dataPath);
        WriteAda(dataPath);
        var gate = new TaskCompletionSource();
        factory.Provider.Enqueue(s => { gate.Task.Wait(); return FakeAgentProvider.Done(s, "first"); });
        factory.Provider.EnqueueDone("second");
        using var client = factory.CreateClient();
        await client.PostAsync("/employees/ada-coder/wake", null);

        var a = await (await client.PostAsJsonAsync("/tasks", new { title="A", brief="a", assignee="ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var b = await (await client.PostAsJsonAsync("/tasks", new { title="B", brief="b", assignee="ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        await Task.Delay(200);
        var bMid = await client.GetFromJsonAsync<TaskModel>($"/tasks/{b!.Id}", TestJson.Options);
        Assert.Equal(TaskStatus.Queued, bMid!.Status);   // B waits while A holds the employee
        gate.SetResult();
        await PollUntil(client, b.Id, t => t.Status == TaskStatus.Done);
    }
```

- [ ] **Step 2: Write `FakeAgentProvider.cs`**

```csharp
using System.Collections.Concurrent;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public sealed class FakeAgentProvider : IAgentProvider
{
    private readonly ConcurrentQueue<Func<RunSpec, RunResult>> _scripted = new();
    public System.Collections.Generic.List<RunSpec> Specs { get; } = new();

    public bool Handles(Vendor vendor) => true;

    public void Enqueue(Func<RunSpec, RunResult> f) => _scripted.Enqueue(f);
    public void EnqueueDone(string summary = "done") => Enqueue(s => Done(s, summary));
    public void EnqueueHandoff(string to, string q) => Enqueue(s => new RunResult
    { RunId = s.RunId, Status = RunOutcome.Handoff, Summary = "asking", Ask = new HandoffAsk(to, q),
      Artifacts = Array.Empty<string>(), SessionId = s.SessionId ?? Guid.NewGuid().ToString(),
      Usage = new Usage(1, null, null, null, null), RawTail = "" });

    public static RunResult Done(RunSpec s, string summary = "done") => new()
    { RunId = s.RunId, Status = RunOutcome.Done, Summary = summary, Ask = null,
      Artifacts = Array.Empty<string>(), SessionId = s.SessionId ?? Guid.NewGuid().ToString(),
      Usage = new Usage(1, null, null, null, null), RawTail = "" };

    public Task<RunResult> RunAsync(RunSpec spec, CancellationToken ct)
    {
        lock (Specs) Specs.Add(spec);
        var f = _scripted.TryDequeue(out var x) ? x : (s => Done(s));
        return Task.Run(() => f(spec), ct);   // off-thread so a blocking script doesn't deadlock Pump
    }
}
```

In `ForemanFactory` add `public FakeAgentProvider Provider { get; } = new();` and in `ConfigureTestServices` also `s.RemoveAll<IAgentProvider>(); s.AddSingleton<IAgentProvider>(Provider);`.

- [ ] **Step 3: Run — expect FAIL** (provider/spec/supervisor undefined).

- [ ] **Step 4: Add run models and `IAgentProvider.cs`**

Add `RunOutcome`, `SessionMode`, `RunSpec`, `RunResult` to `Models.cs`; write `IAgentProvider` as above.

- [ ] **Step 5: Write `PersonaComposer.cs`**

```csharp
using System.Text;

namespace HomeWorkplace.Foreman;

public sealed class PersonaComposer
{
    private readonly IContextApiClient _rooms;
    private readonly ForemanOptions _options;

    public PersonaComposer(IContextApiClient rooms, ForemanOptions options)
    { _rooms = rooms; _options = options; }

    public string BuildSystemPrompt(EmployeeDefinition e, TaskModel t)
    {
        var b = new StringBuilder();
        b.Append("You are ").Append(e.Name).Append(", ").Append(e.Role).AppendLine(".");
        b.AppendLine().AppendLine("## Your skills").AppendLine(e.SkillsMd);
        b.AppendLine().AppendLine("## Your life").AppendLine(e.LifeMd);
        b.AppendLine().AppendLine("## House rules");
        b.Append("- Your team room is '").Append(t.Room).Append("' on ").Append(_options.ContextApiBaseUrl).AppendLine(".");
        b.AppendLine("- Read it before you act; post progress after each meaningful step, with your id and name.");
        b.AppendLine("- Share files through the room folder, not by pasting them into chat.");
        b.AppendLine("- Your FINAL message must be the JSON result object you were asked for — nothing after it.");
        return b.ToString();
    }

    public async Task<string> BuildRunPromptAsync(EmployeeDefinition e, TaskModel t, CancellationToken ct)
    {
        // Resume after a returned answer: the prompt is exactly the answer.
        if (t.PendingAnswer is { } ans)
            return $"Answer from {ans.From}: {ans.Text}\n\nContinue the task.";

        var b = new StringBuilder();
        b.Append("# Task: ").AppendLine(t.Title);
        b.AppendLine(t.Brief).AppendLine();
        foreach (var p in t.Progress)
        {
            b.Append("Done on ").Append(p.Date).Append(" by ").Append(p.Author).AppendLine(":");
            foreach (var d in p.Done) b.Append("  - ").AppendLine(d);
            if (p.Next.Count > 0) { b.AppendLine("Next:"); foreach (var n in p.Next) b.Append("  - ").AppendLine(n); }
        }
        b.AppendLine().AppendLine("## Current room context");
        b.AppendLine(await _rooms.GetBriefAsync(t.Room, ct));
        return b.ToString();
    }
}
```

- [ ] **Step 6: Add employee state transitions to `EmployeeCatalog`**

`void MarkWorking(string id, string taskId)`, `void MarkWaiting(string id)`, `void Free(string id)` (→ Awake, clear currentTask, `RunsToday++`, `LastRunAt`), `void Wake(string id, DateTimeOffset? until)`. Each persists via `FileStore.SaveState` and emits `employee.state`.

- [ ] **Step 7: Write `RunSupervisor.cs`**

One run per employee, enforced by a `HashSet<string> _busy` under a lock. `Pump()` scans awake, idle employees with a queued task and starts each. Completion applies the result and pumps again.

```csharp
namespace HomeWorkplace.Foreman;

public sealed class RunSupervisor
{
    private readonly TaskBook _tasks;
    private readonly EmployeeCatalog _employees;
    private readonly PersonaComposer _composer;
    private readonly IEnumerable<IAgentProvider> _providers;
    private readonly IContextApiClient _rooms;
    private readonly EventLog _events;
    private readonly ForemanOptions _options;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly HashSet<string> _busy = new();

    public RunSupervisor(TaskBook tasks, EmployeeCatalog employees, PersonaComposer composer,
        IEnumerable<IAgentProvider> providers, IContextApiClient rooms, EventLog events,
        ForemanOptions options, TimeProvider clock)
    { _tasks = tasks; _employees = employees; _composer = composer; _providers = providers;
      _rooms = rooms; _events = events; _options = options; _clock = clock; }

    public void Pump()
    {
        lock (_gate)
        {
            foreach (var task in _tasks.Queued())
            {
                var emp = _employees.GetState(task.Assignee);
                if (emp.Status != EmployeeStatus.Awake) continue;   // asleep, working, or waiting
                if (_busy.Contains(task.Assignee)) continue;
                _busy.Add(task.Assignee);
                _employees.MarkWorking(task.Assignee, task.Id);
                _ = RunAsync(task.Id, task.Assignee);
            }
        }
    }

    private async Task RunAsync(string taskId, string employeeId)
    {
        var runId = Guid.NewGuid().ToString("N")[..8];
        try
        {
            var def = _employees.Find(employeeId)!;
            var task = _tasks.Get(taskId)!;
            var provider = _providers.First(p => p.Handles(def.Vendor));
            var spec = new RunSpec
            {
                RunId = runId, Employee = def, TaskId = taskId, Workspace = task.Workspace,
                SystemPrompt = _composer.BuildSystemPrompt(def, task),
                Prompt = await _composer.BuildRunPromptAsync(def, task, CancellationToken.None),
                Mode = task.Session is null ? SessionMode.New : SessionMode.Resume,
                SessionId = task.Session?.SessionId,
                Timeout = TimeSpan.FromMinutes(def.MaxRunMinutes ?? _options.MaxRunMinutes),
            };
            _tasks.MarkRunning(taskId, runId, employeeId, _clock.GetUtcNow());
            _events.Emit("run.started", employeeId, taskId, runId);

            var result = await provider.RunAsync(spec, CancellationToken.None);

            _tasks.ApplyResult(taskId, employeeId, runId, result, _clock.GetUtcNow());
            _events.Emit("run.finished", employeeId, taskId, runId, new { result.Status, result.Summary });
        }
        catch (Exception ex)
        {
            _tasks.FailRun(taskId, runId, ex.Message, _clock.GetUtcNow());
            _events.Emit("run.finished", employeeId, taskId, runId, new { Status = "Failed", Summary = ex.Message });
        }
        finally
        {
            // ApplyResult decided the employee's next state (Free / Waiting); release the busy latch and pump.
            lock (_gate) _busy.Remove(employeeId);
            Pump();
        }
    }
}
```

- [ ] **Step 8: Implement `TaskBook.MarkRunning`, `ApplyResult`, `FailRun`, `SetSession`, `Queued`**

- `MarkRunning`: status → `Running`, append a `RunRecord` (open), persist, post "run started" to the room.
- `ApplyResult`: record the run's end + usage + `SessionId` on the task's `Session`; then branch on `result.Status`:
  - `Done` → status `NeedsHuman` if `RequiresApproval` else `Done`; free the employee (`_employees.Free`).
  - `NeedsHuman` → status `NeedsHuman`, `PendingAnswer = null`, free the employee, emit `human.needed`.
  - `Handoff` → in Task 9.
  - `Failed` → status `Failed`, free the employee.
  Post the run summary to the room in every branch.
- `FailRun`: close the open run as failed, status `Failed`, free the employee.
- `Queued()`: tasks with status `Queued`, oldest first.

For this task, implement `Done`/`NeedsHuman`(basic)/`Failed`; leave a `TODO`-free stub for `Handoff` that throws `NotSupportedException("handoff arrives in Task 8")` — replaced in Task 8 (the FakeAgentProvider in this task never returns handoff, so the branch is unreached).

- [ ] **Step 9: Wire `Program.cs`**

Register `PersonaComposer`, `RunSupervisor`, and the provider list. In tests the provider is the fake; in production register `ClaudeCliProvider` and `CodexCliProvider` (Task 13) — for now register a `NotConfiguredProvider` that throws a clear message, so production wiring compiles before Task 13. Call `supervisor.Pump()` at the end of task creation, and add the minimal wake endpoint:

```csharp
app.MapPost("/employees/{id}/wake", (string id, EmployeeCatalog cat, RunSupervisor sup) =>
{
    if (cat.Find(id) is null) return Results.NotFound();
    cat.Wake(id, until: null);
    sup.Pump();
    return Results.NoContent();
});
```

(Task 11 extends `wake` with `?until=` semantics and the schedule; the signature stays.)

- [ ] **Step 10: Run — expect PASS** (TaskLifecycle now 6 tests; full suite green).

```bash
dotnet test HomeWorkplace.sln -p:ArtifactsPath=./artifacts 2>&1 | grep -E "Passed!|Failed!"
```

- [ ] **Step 11: Commit**

```bash
git add -A && git commit -m "feat(foreman): run engine — provider adapter, persona, supervisor, dispatch"
```

---

### Task 7: Approval gate and human answers

Two ways a task parks on a human: it finished but needs sign-off (`RequiresApproval`), or the agent asked a human a question (`RunOutcome.NeedsHuman`). Both land in `NeedsHuman`; `approve` clears the first, `answer` resumes the second.

**Files:**
- Modify: `TaskBook.cs` (`ApplyResult` approval branch + `NeedsHuman`; `Approve`, `Answer`), `Program.cs` (`/tasks/{id}/approve`, `/tasks/{id}/answer`); add `AwaitingApproval` (bool) and `PendingQuestion` (string?) to `TaskModel`
- Test: `ApprovalTests.cs`

**Interfaces:**
- Consumes: Task 6 engine.
- Produces: `TaskBook.Approve(string id) -> bool` (false if not awaiting approval), `TaskBook.Answer(string id, string text, RunSupervisor sup) -> bool`.

- [ ] **Step 1: Write the failing tests**

`ApprovalTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class ApprovalTests
{
    private const string AdaJson = """
    {"id":"ada-coder","name":"Ada","role":"Engineer","vendor":"claude","model":"m",
     "claudeAllowedTools":["Read"],"schedule":{"wake":"09:00","sleep":"20:00"}}
    """;
    private static void WriteAda(string dataPath)
    {
        var dir = Path.Combine(dataPath, "employees", "ada-coder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "employee.json"), AdaJson);
        File.WriteAllText(Path.Combine(dir, "skills.md"), "s");
        File.WriteAllText(Path.Combine(dir, "life.md"), "l");
    }
    private static async Task<TaskModel> Poll(HttpClient c, string id, Func<TaskModel,bool> p, int s=5)
    { var end=DateTime.UtcNow.AddSeconds(s); while(DateTime.UtcNow<end){ var t=await c.GetFromJsonAsync<TaskModel>($"/tasks/{id}",TestJson.Options); if(t is not null&&p(t)) return t; await Task.Delay(50);} throw new Xunit.Sdk.XunitException("timeout"); }

    [Fact]
    public async Task A_task_that_requires_approval_parks_then_approves_to_done()
    {
        using var factory = ForemanFactory.Create(out var dp); WriteAda(dp);
        factory.Provider.EnqueueDone("built");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada-coder/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks",
            new { title="X", brief="y", assignee="ada-coder", requiresApproval=true }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var parked = await Poll(c, t!.Id, x => x.Status == TaskStatus.NeedsHuman);
        Assert.True(parked.AwaitingApproval);

        var ok = await c.PostAsync($"/tasks/{t.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(TaskStatus.Done, (await c.GetFromJsonAsync<TaskModel>($"/tasks/{t.Id}", TestJson.Options))!.Status);
    }

    [Fact]
    public async Task Approving_a_task_not_awaiting_approval_is_409()
    {
        using var factory = ForemanFactory.Create(out var dp); WriteAda(dp);
        var gate = new TaskCompletionSource();
        factory.Provider.Enqueue(s => { gate.Task.Wait(); return FakeAgentProvider.Done(s); });
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada-coder/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks", new { title="X", brief="y", assignee="ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        await Poll(c, t!.Id, x => x.Status == TaskStatus.Running);

        var resp = await c.PostAsync($"/tasks/{t.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        gate.SetResult();
    }

    [Fact]
    public async Task A_needs_human_result_parks_and_an_answer_resumes_the_run()
    {
        using var factory = ForemanFactory.Create(out var dp); WriteAda(dp);
        factory.Provider.Enqueue(s => new RunResult { RunId=s.RunId, Status=RunOutcome.NeedsHuman,
            Summary="which format?", Ask=null, Artifacts=Array.Empty<string>(),
            SessionId=s.SessionId ?? Guid.NewGuid().ToString(), Usage=new Usage(1,null,null,null,null), RawTail="" });
        factory.Provider.EnqueueDone("used JSON");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada-coder/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks", new { title="X", brief="y", assignee="ada-coder" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var parked = await Poll(c, t!.Id, x => x.Status == TaskStatus.NeedsHuman);
        Assert.False(parked.AwaitingApproval);

        await c.PostAsJsonAsync($"/tasks/{t.Id}/answer", new { text = "use JSON" });
        var done = await Poll(c, t.Id, x => x.Status == TaskStatus.Done);
        Assert.Equal(2, done.Runs.Count);
        Assert.Equal(SessionMode.Resume, factory.Provider.Specs[1].Mode);
        Assert.Contains("use JSON", factory.Provider.Specs[1].Prompt);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (approve/answer + fields undefined).

- [ ] **Step 3: Implement**

Add `AwaitingApproval` and `PendingQuestion` to `TaskModel`. In `ApplyResult`:
- `Done` + `RequiresApproval` → status `NeedsHuman`, `AwaitingApproval = true`.
- `NeedsHuman` → status `NeedsHuman`, `AwaitingApproval = false`, `PendingQuestion = result.Summary`, emit `human.needed`.

`Approve(id)`: if task status is `NeedsHuman` and `AwaitingApproval`, set `Done`, clear the flag, persist, post to room, return true; else false. `Answer(id, text, sup)`: if `NeedsHuman` and not `AwaitingApproval`, set `PendingAnswer = new("human", text)`, `PendingQuestion = null`, status `Queued`, persist, then `sup.Pump()`, return true; else false. Endpoints:

```csharp
app.MapPost("/tasks/{id}/approve", (string id, TaskBook b) =>
    b.Get(id) is null ? Results.NotFound() : b.Approve(id) ? Results.Ok(b.Get(id)) : Results.Conflict());
app.MapPost("/tasks/{id}/answer", (string id, AnswerRequest req, TaskBook b, RunSupervisor sup) =>
    b.Get(id) is null ? Results.NotFound()
    : string.IsNullOrWhiteSpace(req.Text) ? Results.ValidationProblem(new(){["text"]=new[]{"text is required."}})
    : b.Answer(id, req.Text!, sup) ? Results.Ok(b.Get(id)) : Results.Conflict());
```

Add `record AnswerRequest(string? Text)`.

- [ ] **Step 4: Run — expect PASS** (3 tests). **Step 5: Commit**

```bash
git add -A && git commit -m "feat(foreman): approval gate and human answers on parked tasks"
```

---

### Task 8: Sub-ask hand-off — ask a teammate, get the answer, continue

The user's headline requirement. A run ends `Handoff{to,question}`: the parent parks, a child task carries the question to the teammate, and when the child finishes the parent resumes its **same session** with the answer.

**Files:**
- Modify: `TaskBook.cs` (`ApplyResult` handoff branch — replace the Task 6 stub; child-completion hook), `Program.cs` (no new routes)
- Test: `HandoffTests.cs`

**Interfaces:**
- Consumes: Task 6/7 engine.
- Produces: `TaskBook.OnChildDone(TaskModel child, RunSupervisor sup)` called from `ApplyResult` when a task with a `ParentId` reaches `Done`.

- [ ] **Step 1: Write the failing tests**

`HandoffTests.cs` (two employees; parent asks, child answers, parent resumes):

```csharp
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class HandoffTests
{
    private static void Write(string dp, string id, string vendor="claude")
    {
        var dir = Path.Combine(dp, "employees", id); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"employee.json"),
          $$"""{"id":"{{id}}","name":"{{id}}","role":"r","vendor":"{{vendor}}","model":"m","claudeAllowedTools":["Read"],"schedule":{"wake":"09:00","sleep":"20:00"}}""");
        File.WriteAllText(Path.Combine(dir,"skills.md"),"s"); File.WriteAllText(Path.Combine(dir,"life.md"),"l");
    }
    private static async Task<TaskModel> Poll(HttpClient c, string id, Func<TaskModel,bool> p, int s=5)
    { var end=DateTime.UtcNow.AddSeconds(s); while(DateTime.UtcNow<end){ var t=await c.GetFromJsonAsync<TaskModel>($"/tasks/{id}",TestJson.Options); if(t is not null&&p(t)) return t; await Task.Delay(50);} throw new Xunit.Sdk.XunitException("timeout"); }

    [Fact]
    public async Task Parent_asks_child_answers_parent_resumes_same_session_with_the_answer()
    {
        using var factory = ForemanFactory.Create(out var dp);
        Write(dp, "ada"); Write(dp, "rex");
        // ada's first run asks rex; ada's resumed run finishes; rex answers.
        factory.Provider.EnqueueHandoff("rex", "What's the schema?");   // ada run 1
        factory.Provider.EnqueueDone("done with schema");               // rex run 1 (child)
        factory.Provider.EnqueueDone("parent finished");                // ada run 2 (resumed)
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null);
        await c.PostAsync("/employees/rex/wake", null);

        var parent = await (await c.PostAsJsonAsync("/tasks", new { title="P", brief="parent", assignee="ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var finished = await Poll(c, parent!.Id, t => t.Status == TaskStatus.Done, 8);
        Assert.Single(finished.ChildIds);
        // parent's 2nd run resumed and its prompt carried the child's answer
        var adaResume = factory.Provider.Specs.Last(s => s.TaskId == parent.Id);
        Assert.Equal(SessionMode.Resume, adaResume.Mode);
        Assert.Contains("done with schema", adaResume.Prompt);
    }

    [Fact]
    public async Task Parent_is_Waiting_while_the_child_runs()
    {
        using var factory = ForemanFactory.Create(out var dp);
        Write(dp, "ada"); Write(dp, "rex");
        var childGate = new TaskCompletionSource();
        factory.Provider.EnqueueHandoff("rex", "q");
        factory.Provider.Enqueue(s => { childGate.Task.Wait(); return FakeAgentProvider.Done(s, "child done"); });
        factory.Provider.EnqueueDone();
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null); await c.PostAsync("/employees/rex/wake", null);
        var parent = await (await c.PostAsJsonAsync("/tasks", new { title="P", brief="p", assignee="ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        await Poll(c, parent!.Id, t => t.Status == TaskStatus.Waiting, 8);
        childGate.SetResult();
        await Poll(c, parent.Id, t => t.Status == TaskStatus.Done, 8);
    }

    [Fact]
    public async Task An_unknown_handoff_target_degrades_to_needs_human()
    {
        using var factory = ForemanFactory.Create(out var dp);
        Write(dp, "ada");
        factory.Provider.EnqueueHandoff("nobody", "help?");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks", new { title="P", brief="p", assignee="ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);

        var parked = await Poll(c, t!.Id, x => x.Status == TaskStatus.NeedsHuman, 8);
        Assert.False(parked.AwaitingApproval);
        Assert.Contains("help?", parked.PendingQuestion);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (handoff stub throws / degrade missing).

- [ ] **Step 3: Implement the handoff branch in `ApplyResult`**

Replace the Task 6 `NotSupportedException` stub. On `Handoff{to, question}`:

1. If `to` is not a catalogued employee (`_employees.Find(to) is null`) or the parent chain already has `MaxHandoffDepth` ancestors, degrade: treat as `NeedsHuman` with `PendingQuestion = question` (append " (requested teammate '{to}' unavailable)" when unknown), free the employee. Done.
2. Else create a child `TaskModel` via `TaskBook.CreateAsync(new CreateTaskRequest(title: $"Q for {to}: {Truncate(question,60)}", brief: question + $"\n\nAnswer for the team in room {parent.Room}.", assignee: to, requiresApproval: false), ct)` but with `ParentId = parent.Id`; add its id to `parent.ChildIds`.
3. Parent status → `Waiting`; mark the parent's employee `Waiting` (`_employees.MarkWaiting`). Persist parent. Emit `handoff.requested`; post to both rooms.
4. `sup.Pump()` so the child starts (its assignee may be idle).

Compute depth by walking `ParentId` links via `Get`. Add `TaskBook` a private `CreateChildAsync(parent, to, question, ct)` to keep `CreateAsync` clean (child skips the "unknown employee" guard — already checked).

- [ ] **Step 4: Implement child completion**

In `ApplyResult`, when a task that has a `ParentId` reaches `Done`, after the normal done handling call `OnChildDone(childTask, sup)`:

```
parent = Get(child.ParentId);
parent.PendingAnswer = new PendingAnswer(child.Assignee, child.Runs[^1].ResultSummary ?? child.Title);
parent.Status = Queued;
Save(parent);
_employees.Free(parent.Assignee);      // parent's employee returns to Awake so Pump can pick it
_events.Emit("handoff.answered", parent.Assignee, parent.Id);
post to parent.Room: "Answer delivered from {child.Assignee}";
sup.Pump();
```

The parent's next run has `Session` set (so `Mode = Resume`) and `PendingAnswer` set (so the prompt is the answer, per `PersonaComposer`). After that run consumes it, `ApplyResult` must clear `PendingAnswer`.

- [ ] **Step 5: Run — expect PASS** (3 tests; full suite green). **Step 6: Commit**

```bash
git add -A && git commit -m "feat(foreman): returning sub-ask hand-offs between employees"
```

---

### Task 9: Reassignment, retry, and cancel

Move a whole task to another employee (including across vendors) seeded from the room brief + progress; re-queue a failed task; cancel a live one.

**Files:**
- Modify: `TaskBook.cs` (`Reassign`, `Retry`, `Cancel`), `RunSupervisor.cs` (honor a cancel flag), `Program.cs` (three routes)
- Test: `ReassignTests.cs`

**Interfaces:**
- Consumes: Task 6–8 engine.
- Produces: `TaskBook.Reassign(id, newAssignee, sup)`, `TaskBook.Retry(id, sup)`, `TaskBook.Cancel(id)`; `RunSupervisor` observes `_cancelled` run ids.

- [ ] **Step 1: Write the failing tests**

`ReassignTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class ReassignTests
{
    private static void Write(string dp, string id, string vendor)
    {
        var dir = Path.Combine(dp, "employees", id); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"employee.json"),
          $$"""{"id":"{{id}}","name":"{{id}}","role":"r","vendor":"{{vendor}}","model":"m","claudeAllowedTools":["Read"],"codexSandbox":"read-only","schedule":{"wake":"09:00","sleep":"20:00"}}""");
        File.WriteAllText(Path.Combine(dir,"skills.md"),"s"); File.WriteAllText(Path.Combine(dir,"life.md"),"l");
    }
    private static async Task<TaskModel> Poll(HttpClient c, string id, Func<TaskModel,bool> p, int s=5)
    { var end=DateTime.UtcNow.AddSeconds(s); while(DateTime.UtcNow<end){ var t=await c.GetFromJsonAsync<TaskModel>($"/tasks/{id}",TestJson.Options); if(t is not null&&p(t)) return t; await Task.Delay(50);} throw new Xunit.Sdk.XunitException("timeout"); }

    [Fact]
    public async Task Reassigning_a_failed_task_across_vendors_reruns_it_under_the_new_employee()
    {
        using var factory = ForemanFactory.Create(out var dp);
        Write(dp, "ada", "claude"); Write(dp, "rex", "codex");
        factory.ContextApi.Briefs["task-placeholder"] = "prior context"; // brief lookup returns something
        factory.Provider.Enqueue(s => new RunResult { RunId=s.RunId, Status=RunOutcome.Failed, Summary="boom",
            Ask=null, Artifacts=Array.Empty<string>(), SessionId=s.SessionId ?? Guid.NewGuid().ToString(),
            Usage=new Usage(1,null,null,null,null), RawTail="" });          // ada fails
        factory.Provider.EnqueueDone("rex fixed it");                       // rex succeeds
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null); await c.PostAsync("/employees/rex/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks", new { title="T", brief="b", assignee="ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        await Poll(c, t!.Id, x => x.Status == TaskStatus.Failed);

        await c.PostAsJsonAsync($"/tasks/{t.Id}/reassign", new { assignee = "rex" });
        var done = await Poll(c, t.Id, x => x.Status == TaskStatus.Done, 8);

        Assert.Equal("rex", done.Assignee);
        var rexSpec = factory.Provider.Specs.Last();
        Assert.Equal(Vendor.Codex, rexSpec.Employee.Vendor);
        Assert.Equal(SessionMode.New, rexSpec.Mode);        // session was cleared on reassign
    }

    [Fact]
    public async Task Reassigning_to_an_unknown_employee_is_400()
    {
        using var factory = ForemanFactory.Create(out var dp); Write(dp, "ada", "claude");
        using var c = factory.CreateClient();
        var t = await (await c.PostAsJsonAsync("/tasks", new { title="T", brief="b", assignee="ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var resp = await c.PostAsJsonAsync($"/tasks/{t!.Id}/reassign", new { assignee = "ghost" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Cancel_stops_a_queued_task()
    {
        using var factory = ForemanFactory.Create(out var dp); Write(dp, "ada", "claude");
        using var c = factory.CreateClient();   // ada left asleep, so the task stays queued
        var t = await (await c.PostAsJsonAsync("/tasks", new { title="T", brief="b", assignee="ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        var resp = await c.PostAsync($"/tasks/{t!.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(TaskStatus.Cancelled, (await c.GetFromJsonAsync<TaskModel>($"/tasks/{t.Id}", TestJson.Options))!.Status);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (routes/methods undefined).

- [ ] **Step 3: Implement**

- `Reassign(id, newAssignee, sup)`: guard `newAssignee` catalogued (else the endpoint returns 400); only from a non-terminal-or-`Failed` status; set `Assignee`, clear `Session`, `PendingAnswer`, status `Queued`; free the old employee if it was `Waiting`/`Working` on this task; persist; emit `task.reassigned`; post to room; `sup.Pump()`. The new run is `Mode = New` and its prompt already includes the room brief + progress (via `PersonaComposer`), which is the cross-vendor seed.
- `Retry(id, sup)`: only from `Failed`; status `Queued`; `sup.Pump()`.
- `Cancel(id)`: from any non-terminal status → `Cancelled`; if a run is live, add its run id to `RunSupervisor._cancelled` (a concurrent set) so the provider result is discarded when it returns; free the employee; persist; emit.

Routes:

```csharp
app.MapPost("/tasks/{id}/reassign", (string id, ReassignRequest req, TaskBook b, EmployeeCatalog cat, RunSupervisor sup) =>
    b.Get(id) is null ? Results.NotFound()
    : (string.IsNullOrWhiteSpace(req.Assignee) || cat.Find(req.Assignee) is null) ? Results.Problem(detail:$"Unknown employee '{req.Assignee}'.", statusCode:400)
    : b.Reassign(id, req.Assignee!, sup) ? Results.Ok(b.Get(id)) : Results.Conflict());
app.MapPost("/tasks/{id}/retry", (string id, TaskBook b, RunSupervisor sup) =>
    b.Get(id) is null ? Results.NotFound() : b.Retry(id, sup) ? Results.Ok(b.Get(id)) : Results.Conflict());
app.MapPost("/tasks/{id}/cancel", (string id, TaskBook b) =>
    b.Get(id) is null ? Results.NotFound() : b.Cancel(id) ? Results.Ok(b.Get(id)) : Results.Conflict());
```

Add `record ReassignRequest(string? Assignee)`. In `RunSupervisor.RunAsync`, after the provider returns, `if (_cancelled.Remove(runId)) return;` before applying the result.

- [ ] **Step 4: Run — expect PASS** (3 tests; full suite green). **Step 5: Commit**

```bash
git add -A && git commit -m "feat(foreman): task reassignment, retry, and cancel"
```

---

### Task 10: DayCycle — sleep with a wrap-up ledger, wake, reset, call-in

Sleep is a real run: before an employee sleeps, its in-progress task gets a wrap-up run that writes `{done, next}` bullets into the task's progress; then the session is dropped. Morning resumes from those bullets (already handled by `PersonaComposer`, which prints `Progress` into a new-session prompt).

**Files:**
- Modify: `IAgentProvider.cs` (+`WrapUpResult`, `WrapUpAsync`), `RunSupervisor.cs` (`WrapUpAsync`), `EmployeeCatalog.cs` (`Sleep`, `Reset`, `Wake+until`), `FakeAgentProvider.cs` (`EnqueueWrapUp`), `Program.cs` (extend `/wake`, add `/reset`, `/sleep`); create `DayCycle.cs`
- Test: `DayCycleTests.cs` (uses `FakeTimeProvider`)

**Interfaces:**
- Consumes: Task 6–9 engine, `TimeProvider`.
- Produces:
  - `record WrapUpResult(IReadOnlyList<string> Done, IReadOnlyList<string> Next, string SessionId)`
  - `IAgentProvider.WrapUpAsync(RunSpec spec, CancellationToken ct) -> Task<WrapUpResult>`
  - `RunSupervisor.WrapUpAsync(string employeeId, CancellationToken ct) -> Task` (wrap the employee's active-today task if any)
  - `DayCycle : BackgroundService`
  - `ForemanFactory.Create(out string dataPath, FakeTimeProvider? clock = null)` — when a clock is passed it replaces `TimeProvider.System`.

- [ ] **Step 1: Write the failing tests**

`DayCycleTests.cs`:

```csharp
using System.Net.Http.Json;
using HomeWorkplace.Foreman;
using Microsoft.Extensions.Time.Testing;

namespace HomeWorkplace.Foreman.Tests;

public class DayCycleTests
{
    private static void Write(string dp, string id, string wake, string sleep)
    {
        var dir = Path.Combine(dp, "employees", id); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"employee.json"),
          $$"""{"id":"{{id}}","name":"{{id}}","role":"r","vendor":"claude","model":"m","claudeAllowedTools":["Read"],"schedule":{"wake":"{{wake}}","sleep":"{{sleep}}"}}""");
        File.WriteAllText(Path.Combine(dir,"skills.md"),"s"); File.WriteAllText(Path.Combine(dir,"life.md"),"l");
    }
    private static async Task<TaskModel> Poll(HttpClient c, string id, Func<TaskModel,bool> p, int s=5)
    { var end=DateTime.UtcNow.AddSeconds(s); while(DateTime.UtcNow<end){ var t=await c.GetFromJsonAsync<TaskModel>($"/tasks/{id}",TestJson.Options); if(t is not null&&p(t)) return t; await Task.Delay(50);} throw new Xunit.Sdk.XunitException("timeout"); }
    private static async Task<EmployeeView> PollEmp(HttpClient c, string id, Func<EmployeeView,bool> p, int s=5)
    { var end=DateTime.UtcNow.AddSeconds(s); while(DateTime.UtcNow<end){ var e=await c.GetFromJsonAsync<EmployeeView>($"/employees/{id}",TestJson.Options); if(e is not null&&p(e)) return e; await Task.Delay(50);} throw new Xunit.Sdk.XunitException("timeout"); }

    [Fact]
    public async Task Reset_writes_progress_bullets_and_clears_the_session_but_stays_awake()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        using var factory = ForemanFactory.Create(out var dp, clock);
        Write(dp, "ada", "09:00", "20:00");
        // Run parks the task NeedsHuman so a session exists at reset time.
        factory.Provider.Enqueue(s => new RunResult { RunId=s.RunId, Status=RunOutcome.NeedsHuman, Summary="q?",
            Ask=null, Artifacts=Array.Empty<string>(), SessionId="sess-1", Usage=new Usage(1,null,null,null,null), RawTail="" });
        factory.Provider.EnqueueWrapUp(new[]{ "wrote the parser", "added tests" }, new[]{ "wire up CLI" });
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null);
        var t = await (await c.PostAsJsonAsync("/tasks", new { title="T", brief="b", assignee="ada" }))
            .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
        await Poll(c, t!.Id, x => x.Status == TaskStatus.NeedsHuman);

        await c.PostAsync("/employees/ada/reset", null);

        var after = await c.GetFromJsonAsync<TaskModel>($"/tasks/{t.Id}", TestJson.Options);
        var ledger = Assert.Single(after!.Progress);
        Assert.Equal(new[]{ "wrote the parser", "added tests" }, ledger.Done);
        Assert.Null(after.Session);                       // forgotten
        Assert.Equal(EmployeeStatus.NeedsHuman is var _ ? after.Status : after.Status, after.Status);
        Assert.Equal(EmployeeStatus.Awake, (await c.GetFromJsonAsync<EmployeeView>("/employees/ada", TestJson.Options))!.Status);
    }

    [Fact]
    public async Task At_sleep_time_the_scheduler_puts_an_idle_employee_to_sleep()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 19, 59, 0, TimeSpan.Zero));
        using var factory = ForemanFactory.Create(out var dp, clock);
        Write(dp, "ada", "09:00", "20:00");
        using var c = factory.CreateClient();
        await c.PostAsync("/employees/ada/wake", null);
        Assert.Equal(EmployeeStatus.Awake, (await c.GetFromJsonAsync<EmployeeView>("/employees/ada", TestJson.Options))!.Status);

        clock.Advance(TimeSpan.FromMinutes(2));   // now 20:01, past sleep; fires the DayCycle timer
        await PollEmp(c, "ada", e => e.Status == EmployeeStatus.Asleep);
    }

    [Fact]
    public async Task Wake_with_until_keeps_an_employee_awake_past_its_sleep_time()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 21, 0, 0, TimeSpan.Zero)); // already past 20:00
        using var factory = ForemanFactory.Create(out var dp, clock);
        Write(dp, "ada", "09:00", "20:00");
        using var c = factory.CreateClient();

        await c.PostAsJsonAsync("/employees/ada/wake?until=23:00", new { });
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(EmployeeStatus.Awake, (await c.GetFromJsonAsync<EmployeeView>("/employees/ada", TestJson.Options))!.Status);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (wrap-up + DayCycle + endpoints undefined).

- [ ] **Step 3: Extend the provider surface**

Add to `IAgentProvider`: `Task<WrapUpResult> WrapUpAsync(RunSpec spec, CancellationToken ct);` and `record WrapUpResult(IReadOnlyList<string> Done, IReadOnlyList<string> Next, string SessionId);`. In `FakeAgentProvider` add a wrap-up queue and `EnqueueWrapUp(string[] done, string[] next)`; `WrapUpAsync` returns the next scripted wrap-up (or an empty one).

- [ ] **Step 4: `RunSupervisor.WrapUpAsync(employeeId)`**

Find the employee's task with a session opened today and a non-terminal status (`_tasks.ActiveToday(employeeId, _clock.GetLocalNow().DayNumber)`). If none, return. Else build a `RunSpec` (`Mode = Resume`, the task's `SessionId`, a wrap-up prompt), call `provider.WrapUpAsync`, append `new ProgressEntry(employeeId, DateOnly.FromDateTime(_clock.GetLocalNow().Date), result.Done, result.Next)` to the task, clear `task.Session`, persist, emit `wrapup.written`, post the bullets to the room.

- [ ] **Step 5: `EmployeeCatalog` transitions and `DayCycle`**

`Sleep(id)` = (caller wraps up first) set `Asleep`. `Reset(id)` = stay `Awake`, `AwakeOverrideUntil` unchanged. `Wake(id, until)` = `Awake`, `AwakeOverrideUntil = until`. Add `bool ShouldBeAwake(EmployeeDefinition def, DateTimeOffset localNow)`:

```csharp
public static bool WithinShift(TimeOnly now, TimeOnly wake, TimeOnly sleep)
    => wake <= sleep ? now >= wake && now < sleep      // normal day
                     : now >= wake || now < sleep;      // wraps past midnight
```

`DayCycle` (a `BackgroundService`) loops: `await Task.Delay(TimeSpan.FromSeconds(SchedulerTickSeconds), _clock, ct)` then for each employee compares desired vs actual:
- desired awake (`WithinShift` OR an unexpired `AwakeOverrideUntil`) and actual `Asleep` → `Wake` + `Pump`.
- desired asleep (past shift and no unexpired override) and actual not `Asleep` and the employee is **not currently executing a run** (`!supervisor.IsBusy(id)`) → `await supervisor.WrapUpAsync(id)` then `Sleep`.

Expire `AwakeOverrideUntil` when `localNow >= it`. Using `Task.Delay(…, _clock, ct)` means `FakeTimeProvider.Advance` fires the tick in tests. Add `RunSupervisor.IsBusy(string id)`.

- [ ] **Step 6: Endpoints**

```csharp
app.MapPost("/employees/{id}/reset", async (string id, EmployeeCatalog cat, RunSupervisor sup, CancellationToken ct) =>
{ if (cat.Find(id) is null) return Results.NotFound(); await sup.WrapUpAsync(id, ct); cat.Reset(id); return Results.NoContent(); });

app.MapPost("/employees/{id}/sleep", async (string id, EmployeeCatalog cat, RunSupervisor sup, CancellationToken ct) =>
{ if (cat.Find(id) is null) return Results.NotFound(); await sup.WrapUpAsync(id, ct); cat.Sleep(id); return Results.NoContent(); });
```

Extend `/wake` to read `?until=HH:mm` and pass `TimeOnly.Parse(until)` combined with today's date into `cat.Wake(id, until)`.

- [ ] **Step 7: Register `DayCycle`** via `builder.Services.AddHostedService<DayCycle>();`. In `ForemanFactory`, when a `FakeTimeProvider` is passed, `s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock);`.

- [ ] **Step 8: Run — expect PASS** (3 tests; full suite green). **Step 9: Commit**

```bash
git add -A && git commit -m "feat(foreman): DayCycle sleep/wake with a wrap-up progress ledger"
```

---

### Task 11: Restart recovery

Foreman restarts must lose nothing: tasks, employee state, and the event cursor survive, and a task that was mid-run when the process died comes back `Queued`.

**Files:**
- Modify: `FileStore.cs` (`AppendEvent`, `LoadEvents`), `EventLog.cs` (`Seed`, append on emit), `Program.cs` (a `StateRecovery` hosted step that runs before the app serves)
- Create: `StateRecovery.cs`
- Test: `RestartTests.cs`

**Interfaces:**
- Consumes: `FileStore`, `TaskBook`, `EmployeeCatalog`, `EventLog`.
- Produces: `FileStore.AppendEvent(RuntimeEvent)`, `FileStore.LoadEvents(int max)`; `EventLog.Seed(IReadOnlyList<RuntimeEvent>)`; `StateRecovery.Recover()`.

- [ ] **Step 1: Write the failing test**

`RestartTests.cs` (two factories over the same `DataPath`):

```csharp
using System.Net.Http.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class RestartTests
{
    private const string AdaJson = """
    {"id":"ada","name":"ada","role":"r","vendor":"claude","model":"m","claudeAllowedTools":["Read"],"schedule":{"wake":"09:00","sleep":"20:00"}}
    """;
    private static void WriteAda(string dp)
    { var d=Path.Combine(dp,"employees","ada"); Directory.CreateDirectory(d);
      File.WriteAllText(Path.Combine(d,"employee.json"),AdaJson); File.WriteAllText(Path.Combine(d,"skills.md"),"s"); File.WriteAllText(Path.Combine(d,"life.md"),"l"); }

    [Fact]
    public async Task Tasks_state_and_event_cursor_survive_a_restart_and_running_becomes_queued()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "foreman-restart", Guid.NewGuid().ToString("N"));
        var employeesPath = Path.Combine(dataPath, "employees");
        Directory.CreateDirectory(employeesPath);
        WriteAda(dataPath);
        string taskId; long cursor;

        // First boot: create a task, let a long run leave it Running, capture the event cursor.
        var gate = new TaskCompletionSource();
        await using (var f1 = ForemanFactory.Existing(dataPath, employeesPath, provider: p => p.Enqueue(s => { gate.Task.Wait(); return FakeAgentProvider.Done(s); })))
        {
            using var c1 = f1.CreateClient();
            await c1.PostAsync("/employees/ada/wake", null);
            var t = await (await c1.PostAsJsonAsync("/tasks", new { title="T", brief="b", assignee="ada" }))
                .Content.ReadFromJsonAsync<TaskModel>(TestJson.Options);
            taskId = t!.Id;
            // wait until Running
            var end = DateTime.UtcNow.AddSeconds(5);
            while ((await c1.GetFromJsonAsync<TaskModel>($"/tasks/{taskId}", TestJson.Options))!.Status != TaskStatus.Running && DateTime.UtcNow < end)
                await Task.Delay(50);
            cursor = (await c1.GetFromJsonAsync<EventPage>("/events?since=0", TestJson.Options))!.Cursor;
        }
        gate.SetResult();

        // Second boot: same DataPath, fresh process.
        await using var f2 = ForemanFactory.Existing(dataPath, employeesPath);
        using var c2 = f2.CreateClient();
        var recovered = await c2.GetFromJsonAsync<TaskModel>($"/tasks/{taskId}", TestJson.Options);
        Assert.Equal(TaskStatus.Queued, recovered!.Status);          // running → queued
        var page = await c2.GetFromJsonAsync<EventPage>("/events?since=0", TestJson.Options);
        Assert.True(page!.Cursor >= cursor);                          // cursor did not reset
        try { Directory.Delete(dataPath, true); } catch { }
    }
}
```

Add `ForemanFactory.Existing(string dataPath, string employeesPath, Action<FakeAgentProvider>? provider = null)` that reuses a caller-owned `DataPath` (does not delete it on dispose) and optionally scripts the provider.

- [ ] **Step 2: Run — expect FAIL** (recovery absent; task not found / Running).

- [ ] **Step 3: Implement**

- `FileStore.AppendEvent` writes one JSON line to `events.jsonl`; `LoadEvents(max)` returns the last `max` parsed lines.
- `EventLog`: on `Emit`, also `_store.AppendEvent(evt)` (inject `FileStore`); `Seed(events)` loads them and sets `_seq` to the max seq so new events continue above it.
- `StateRecovery.Recover()`: `EmployeeCatalog` already loaded definitions; overlay `FileStore.LoadStates()` onto state; `TaskBook.SeedFrom(FileStore.LoadTasks())`, flipping any `Running` task to `Queued` and clearing `Session` only if its `Session.Day` is not today; `EventLog.Seed(FileStore.LoadEvents(EventsCapacity))`. Run it once at startup **before** `app.Run()` (call `app.Services.GetRequiredService<StateRecovery>().Recover();`). Then `Pump()` so recovered `Queued` tasks resume if their employees are awake.

- [ ] **Step 4: Run — expect PASS. Step 5: Commit**

```bash
git add -A && git commit -m "feat(foreman): crash-safe restart recovery from disk"
```

---

### Task 12: Real CLI providers — argv, environment scrub, output parsing

Replaces the placeholder production provider with real `claude`/`codex` launchers. Pure logic (argv, env scrub, parsing) is unit-tested; the CLIs themselves are never launched by the suite. The one real-behaviour dependency — the exact JSON each CLI prints — is captured as **fixtures recorded from a real run** in Step 1, so the parser is written against real bytes, not a guess.

**Files:**
- Create: `ProcessRunner.cs`, `ClaudeCliProvider.cs`, `CodexCliProvider.cs`, `tests/.../fixtures/claude-run.json`, `fixtures/codex-run.jsonl`
- Modify: `Program.cs` (register the two real providers instead of the placeholder)
- Test: `ProviderTests.cs`

**Interfaces:**
- Consumes: `ForemanOptions`, `TimeProvider`.
- Produces: `ProcessRunner.RunAsync(string exe, IReadOnlyList<string> args, string workingDir, string stdin, IDictionary<string,string?> extraEnv, TimeSpan timeout, CancellationToken ct) -> Task<(int ExitCode, string Stdout, string Stderr, bool TimedOut)>` with the environment scrub built in; `ClaudeCliProvider`/`CodexCliProvider : IAgentProvider`.

- [ ] **Step 1: Record fixtures from one real run of each CLI**

From a **clean terminal** (not inside a Claude Code session), against a throwaway prompt, capture the exact structured output each CLI emits and save the two files:

```bash
claude -p --model claude-haiku-4-5-20251001 --output-format json \
  --json-schema '{"type":"object","properties":{"status":{"type":"string"},"summary":{"type":"string"}},"required":["status","summary"]}' \
  "Reply with status=done and summary=hello, nothing else." > services/foreman/tests/HomeWorkplace.Foreman.Tests/fixtures/claude-run.json

codex exec --json --output-schema schema.json -o last.txt "Reply done." ; # save the JSONL stream to fixtures/codex-run.jsonl
```

Inspect both files and record, in a comment at the top of `ProviderTests.cs`, the exact field paths for: the result/last message, the session id, and usage (tokens, cost, turns) for each CLI. The parser in Step 4 targets those paths. **Do not proceed on guessed field names.**

- [ ] **Step 2: Write the failing tests**

`ProviderTests.cs` — argv, env scrub, and fixture parsing are all pure:

```csharp
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class ProviderTests
{
    private static EmployeeDefinition Ada(Vendor v) => new()
    { Id="ada", Name="Ada", Role="Eng", Vendor=v, Model="m", Effort="low",
      ClaudeAllowedTools=new[]{ "Read","Edit" }, CodexSandbox="workspace-write",
      Schedule=new Schedule("09:00","20:00"), MaxRunMinutes=10, SkillsMd="s", LifeMd="l" };
    private static RunSpec Spec(EmployeeDefinition e, SessionMode mode) => new()
    { RunId="r1", Employee=e, TaskId="t1", Workspace="/w", SystemPrompt="SYS", Prompt="DO IT",
      Mode=mode, SessionId= mode==SessionMode.Resume ? "sess-9" : null, Timeout=TimeSpan.FromMinutes(10) };

    [Fact]
    public void Claude_new_run_argv_has_model_effort_tools_and_json_schema()
    {
        var argv = ClaudeCliProvider.BuildArgs(Spec(Ada(Vendor.Claude), SessionMode.New), schemaFile: "/tmp/s.json", systemFile: "/tmp/sys.txt");
        Assert.Contains("-p", argv);
        Assert.Contains("--model", argv); Assert.Contains("m", argv);
        Assert.Contains("--effort", argv); Assert.Contains("low", argv);
        Assert.Contains("--allowedTools", argv);
        Assert.Contains("--append-system-prompt-file", argv);
        Assert.Contains("--session-id", argv);
        Assert.DoesNotContain("--resume", argv);
    }

    [Fact]
    public void Claude_resume_run_uses_resume_not_session_id()
    {
        var argv = ClaudeCliProvider.BuildArgs(Spec(Ada(Vendor.Claude), SessionMode.Resume), "/tmp/s.json", "/tmp/sys.txt");
        Assert.Contains("--resume", argv); Assert.Contains("sess-9", argv);
        Assert.DoesNotContain("--session-id", argv);
    }

    [Fact]
    public void Codex_argv_has_model_and_sandbox()
    {
        var argv = CodexCliProvider.BuildArgs(Spec(Ada(Vendor.Codex), SessionMode.New), "/tmp/schema.json", "/tmp/out.txt");
        Assert.Contains("exec", argv);
        Assert.Contains("-m", argv); Assert.Contains("-s", argv); Assert.Contains("workspace-write", argv);
        Assert.Contains("--json", argv);
    }

    [Fact]
    public void The_environment_scrub_removes_claude_and_anthropic_variables()
    {
        var src = new Dictionary<string,string?> { ["PATH"]="x", ["CLAUDECODE"]="1", ["CLAUDE_CODE_CHILD_SESSION"]="1", ["ANTHROPIC_BASE_URL"]="y", ["HOME"]="h" };
        var scrubbed = ProcessRunner.Scrub(src);
        Assert.True(scrubbed.ContainsKey("PATH")); Assert.True(scrubbed.ContainsKey("HOME"));
        Assert.False(scrubbed.Keys.Any(k => k.StartsWith("CLAUDE") || k.StartsWith("ANTHROPIC")));
    }

    [Fact]
    public void Claude_fixture_parses_into_a_run_result()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude-run.json"));
        var result = ClaudeCliProvider.Parse(json, runId: "r1", requestedSessionId: "sess-1");
        Assert.Equal(RunOutcome.Done, result.Status);
        Assert.False(string.IsNullOrEmpty(result.SessionId));
    }
}
```

(Copy the `fixtures/` folder to output: add to the test csproj
`<ItemGroup><None Include="fixtures/**" CopyToOutputDirectory="PreserveNewest" /></ItemGroup>`.)

- [ ] **Step 3: Write `ProcessRunner.cs`** — `Scrub` drops any key starting `CLAUDE`/`CLAUDECODE`/`ANTHROPIC` (case-insensitive); `RunAsync` starts the process with `ProcessStartInfo` (redirect stdio, `UseShellExecute=false`), clears then repopulates `Environment` from the scrubbed parent plus `extraEnv`, writes `stdin`, and enforces `timeout` by killing the process tree.

- [ ] **Step 4: Write the two providers** — `BuildArgs` (pure, tested above), `Parse` (pure, against the fixtures), and `RunAsync`/`WrapUpAsync` that compose a temp system-prompt file + schema file, call `ProcessRunner`, and map the parsed output into `RunResult`/`WrapUpResult`. `Handles(Vendor)` returns the matching vendor. A run whose final JSON does not parse becomes `RunOutcome.Failed` with the last 4 KB of stdout in `RawTail`. Register both in `Program.cs`, replacing the placeholder.

- [ ] **Step 5: Run — expect PASS** (5 tests; full suite green). **Step 6: Commit**

```bash
git add -A && git commit -m "feat(foreman): real claude/codex providers with env scrub and fixture-tested parsing"
```

---

### Task 13: Starter employees, acceptance script, and docs

Ships the folder-defined starter team, a real-CLI acceptance script, and the READMEs — the deliverables that make Foreman runnable by a person.

**Files:**
- Create: `employees/ada-coder/{employee.json,skills.md,life.md}`, `employees/rex-reviewer/{…}`, `employees/vfx-artist/{…}`; `scripts/acceptance.ps1`; `services/foreman/README.md`; product `README.md`; update `services/context-api/README.md` header to note the monorepo
- Test: manual — the acceptance script

**Interfaces:** none consumed by code.

- [ ] **Step 1: Verify the whole suite is green before documenting**

```bash
dotnet test HomeWorkplace.sln -p:ArtifactsPath=./artifacts 2>&1 | grep -E "Passed!|Failed!"
```

Expected: PASS, all Foreman + 68 context-api tests, zero warnings.

- [ ] **Step 2: Write the three starter employees**

- `ada-coder` — `vendor: claude`, `model: claude-haiku-4-5-20251001`, `effort: low`, tools `["Bash(curl *)","Bash(dotnet *)","Read","Edit","Write","Glob","Grep"]`, `schedule {09:00, 20:00}`. `skills.md`: read the repo, TDD, post progress to the room, end with the JSON result. `life.md`: name Ada, steady, wraps up at 8 PM with bullets.
- `rex-reviewer` — `vendor: codex`, `codexSandbox: read-only`, same schedule. `skills.md`: read diffs, write numbered change requests, approve or request changes. `life.md`: name Rex, blunt, brief.
- `vfx-artist` — `vendor: claude`, a real model-agnostic brief in `skills.md`: fixed palette, sprite sizes (8×8 / 16×16), naming, deliverables into the room folder, consistency rules; `life.md`: name Vex, particular about palette.

- [ ] **Step 3: Write `scripts/acceptance.ps1`**

A PowerShell script the user runs from a clean terminal: it starts context-api and foreman, waits for both `/health`, then creates a task for `ada-coder`, drives one hand-off to `rex-reviewer`, forces a wrap-up (`/employees/ada-coder/reset`), triggers a morning resume, and reassigns a task `ada-coder → rex-reviewer`, printing the room brief at each step. It asserts the task reaches `done` and prints the progress ledger. Its expected transcript is saved to `docs/trials/foreman-acceptance.md`.

- [ ] **Step 4: Write the READMEs**

Product `README.md`: what Home Workplace is, the monorepo map, how to run both services (`.claude/launch.json` names, or `dotnet run --project …`), and the sub-project roadmap (Foreman done; manager loop, desktop shell, office renderer, notifications, VM, phone app next). `services/foreman/README.md`: the endpoint table (spec §10), the employee-folder format, the day cycle, and the "launch from a clean terminal, never nested" rule with the reason. Update `services/context-api/README.md`'s top to note it now lives in the monorepo and its port is unchanged.

- [ ] **Step 5: Commit and push**

```bash
git add -A && git commit -m "feat(foreman): starter employees, acceptance script, and docs"
git push
```

- [ ] **Step 6: Run the acceptance script from a clean terminal** (manual, by the user) and save the transcript to `docs/trials/foreman-acceptance.md`; commit it.

---

## Self-Review

**1. Spec coverage.** Every spec section maps to a task: §5.1 processes/env-scrub → Tasks 1, 12; §5.2 components → Tasks 3–12 (one each); §6 employees → Task 4; §7.1–7.2 task record + state machine → Tasks 5–9; §7.3 sub-ask → Task 8; §7.4 reassignment → Task 9; §7.5 rooms → Tasks 5, 6, 8; §8 runs/provider/schema/wrap-up → Tasks 6, 10, 12; §9 memory/day cycle → Task 10; §10 HTTP surface → Tasks 4–10 (every route); §11 events → Task 3; §12 storage → Tasks 5, 11; §13 config → Task 2; §14 monorepo/starter → Tasks 1, 13; §15 testing → every task's tests + Task 13 acceptance; §16 risks → carried, not code. No gaps.

**2. Placeholder scan.** No "TBD"/"add error handling"/"similar to Task N"; every code step carries real code or a precise, itemized description (the README and starter-employee steps enumerate exact contents; their prose is deliberate because the exact wording depends on the recorded fixtures and the running system). The one intentional stub — `ApplyResult`'s handoff branch in Task 6 — throws with a message pointing at **Task 8**, is never reached by Task 6's tests, and is replaced there.

**3. Type consistency.** `TaskModel` (not `Task`) is used everywhere to avoid the TPL clash. `RunSpec`/`RunResult`/`RunOutcome`/`SessionMode` introduced in Task 6 are consumed unchanged in 7–12. `WrapUpResult`/`WrapUpAsync` introduced in Task 10 and implemented for real in Task 12. `IContextApiClient` (Task 5) is faked in tests and realized once. `EmployeeCatalog` state methods (`MarkWorking`, `MarkWaiting`, `Free`, `Wake`, `Sleep`, `Reset`) are named identically across Tasks 6, 8, 9, 10. `EventLog.Emit`/`Read`/`ReadWithWaitAsync`/`Seed` are stable from Task 3 through 11. `RunSupervisor.Pump`/`WrapUpAsync`/`IsBusy`/`_cancelled` are consistent across 6, 9, 10, 11.

**4. Ordering constraints for executors.** (a) Task 6 leaves a handoff stub that **Task 8** replaces — do not ship between them expecting handoff to work. (b) Task 6's minimal `/wake` is **extended** (not rewritten) by Task 10 with `?until=`; keep the signature. (c) `ForemanFactory` is grown in Tasks 2 → 5 (context-api fake) → 6 (provider fake) → 10 (clock swap, `Existing`) — each task adds, none rewrites. (d) Task 11 makes `EventLog` depend on `FileStore`; do it in that task, not earlier. (e) The whole plan is strictly ordered; a later task never precedes an earlier one.

**5. The one thing that can only be verified live.** Task 12 Step 1 records CLI output fixtures from a real clean-terminal run; every other test runs against fakes. If those fixtures reveal the CLIs cannot enforce the output schema or emit no session id, the affected parsing in Task 12 and the resume mechanic in Tasks 8/10 need revisiting — that is the plan's single external unknown, and it is isolated to one task and one script.
