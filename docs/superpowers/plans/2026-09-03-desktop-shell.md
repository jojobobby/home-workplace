# Desktop Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A MAUI Blazor Hybrid desktop app that boots context-api + foreman, verifies the CLIs, and manages employees/tasks/goals live, styled Terraria-like.

**Architecture:** `HomeWorkplace.Client` (DTOs, HTTP clients, supervisor, setup checker over `IProcessRunner`) → `HomeWorkplace.UI` (Razor Class Library: store, event pump, pixel design system, screens) → `HomeWorkplace.App` (thin MAUI host). Tests target the two libraries with bUnit/xunit and fakes.

**Tech Stack:** .NET 8, MAUI 8.0.100 (`maui-blazor` template), Razor Class Library, bUnit, xunit.

**Spec:** `docs/superpowers/specs/2026-09-03-desktop-shell-design.md`

## Global Constraints

- New projects: `libs/HomeWorkplace.Client`, `libs/HomeWorkplace.UI`, `apps/desktop/HomeWorkplace.App`, `tests/HomeWorkplace.UI.Tests`; all added to `HomeWorkplace.sln`.
- `HomeWorkplace.Client` and `HomeWorkplace.UI` target `net8.0` only; the App targets `net8.0-windows10.0.19041.0` (keep android/ios TFMs commented in for sub-project 7). `WindowsPackageType=None`.
- No test touches a real service or CLI. Everything process-shaped goes through `IProcessRunner`.
- Enums in DTOs are explicit C# enums deserialized from numbers (services emit numbers); use a shared `JsonSerializerOptions` with `PropertyNameCaseInsensitive`.
- Palette, fonts, primitives exactly as spec §10; no component library.
- Run `dotnet test HomeWorkplace.sln` after every task (services' 123 tests must stay green); commit per task.

---

### Task 1: Scaffold the three projects and the test project

- [ ] `dotnet new classlib -n HomeWorkplace.Client -o libs/HomeWorkplace.Client`; `dotnet new razorclasslib -n HomeWorkplace.UI -o libs/HomeWorkplace.UI`; `dotnet new maui-blazor -n HomeWorkplace.App -o apps/desktop/HomeWorkplace.App`; `dotnet new xunit -n HomeWorkplace.UI.Tests -o tests/HomeWorkplace.UI.Tests`.
- [ ] App csproj: TFM `net8.0-windows10.0.19041.0` only, `WindowsPackageType=None`; reference UI + Client. UI references Client. Tests reference UI + Client, add `bunit` (pinned) and `Microsoft.AspNetCore.Components` as needed.
- [ ] Replace the template's sample pages with a single `Routes`/`Main` that renders `HomeWorkplace.UI.App` (a placeholder "Home Workplace" heading).
- [ ] `dotnet build HomeWorkplace.sln` green (Windows TFM builds). First bUnit test: `App` renders the heading. Commit `chore(app): scaffold client, UI library, MAUI host, tests`.

### Task 2: Client DTOs and HTTP clients with recorded fixtures

- [ ] Start foreman (harness `preview_start foreman`), record JSON from `/employees`, `/tasks` (after creating one), `/goals` (after creating one), `/events` into `tests/.../fixtures/`. Stop it.
- [ ] Tests: each DTO parses its fixture; `ForemanClient` builds the right method/path/body for every endpoint (stub handler); non-2xx → `ApiException` with title/detail.
- [ ] RED → implement `Dtos.cs`, `ForemanClient.cs`, `ContextApiClient.cs`, `ApiException` → GREEN → commit.

### Task 3: ServiceSupervisor and CliSetupChecker

- [ ] `IProcessRunner { Task<ProcessResult> RunAsync(cmd, args, env, timeout); IProcessHandle Start(cmd, args, env); }` in Client; `ProcessRunner` real impl in App later.
- [ ] Tests (fake runner + fake health): supervisor launches both with scrubbed env, waits for health, reports progress, honours `connectOnly`, stops only what it started; checker yields not-installed / installed-not-signed-in / signed-in per CLI using the verified commands.
- [ ] RED → implement → GREEN → commit.

### Task 4: AppStore, EventPump, Setup screen

- [ ] Tests: store `Changed` fires on set; pump maps each event type to the right refetch (fake client records calls), carries the cursor, full-refetches on truncated, backs off on error, bumps `HumanNeeded`; Setup component renders four card states and calls the checker on Refresh.
- [ ] RED → implement `AppStore`, `EventPump` (BackgroundService-like loop with cancellation), `Setup.razor` → GREEN → commit.

### Task 5: Pixel design system, nav, Office and Employees screens

- [ ] `wwwroot/css/pixel.css` with the spec tokens; bundle Pixelify Sans (OFL) in `wwwroot/fonts`; primitives `PxPanel`, `PxButton`, `PxSlot`, `PxBar`, `PxBadge`, `PxToast`, `PxTooltip`, `PxTabs`.
- [ ] Tests: nav renders six links and the badge count; Office renders one desk per employee with status class and a `#office` canvas; Employees grid + detail actions by status (wake shows until, sleep/reset present when awake).
- [ ] RED → implement → GREEN → commit.

### Task 6: Tasks, Goals, Activity screens

- [ ] Tests: Tasks list filters; detail shows actions by status (approve only when awaiting approval, answer only when needs-human without approval, retry only when failed, reassign/cancel when non-terminal); create form posts; Goals detail budget bar width = spent/budget, top-up visible when blocked or running, cancel when non-terminal; Activity lists newest first and filters by type; toast appears on `human.needed`.
- [ ] RED → implement → GREEN → commit.

### Task 7: Host wiring, smoke, docs

- [ ] App: `MauiProgram` registers `ProcessRunner`, clients (base URLs from `app.json`), store, pump, supervisor; `MainPage` hosts `BlazorWebView` → `HomeWorkplace.UI.App`; boot screen gate before routes; stop children on window close. `app.json` with dev `dotnet run` commands pointing at the repo paths.
- [ ] Smoke on this machine: `dotnet build -f net8.0-windows10.0.19041.0` then launch the exe; confirm boot, Setup green, create goal, watch activity, approve/top-up. Record findings.
- [ ] Docs: `apps/desktop/README.md`; root README roadmap (3 → built, 4 next). Full solution test green → commit → push.

## Self-Review

Spec §4 → Task 1; §7 → 2; §5–6 → 3 and 7; §8 → 4; §9–10 → 5–6; §11 → 4/7 (boot screen, toasts, reconnect strip); §12 → distributed; §13 → Task 7 smoke. Type names: `ForemanClient`, `ContextApiClient`, `ApiException`, `IProcessRunner`, `ServiceSupervisor`, `CliSetupChecker`, `AppStore`, `EventPump`, `Px*` primitives — used consistently.
