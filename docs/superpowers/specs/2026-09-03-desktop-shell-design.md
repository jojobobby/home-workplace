# Desktop Shell — Sub-project 3 of Home Workplace (Design)

Date: 2026-09-03
Status: Approved in discussion; ready for implementation planning
Builds on: `2026-09-03-foreman-design.md`, `2026-09-03-manager-loop-design.md`

## 1. Premise

One app you open. It boots your company — the room service and the worker runtime — and
shows it to you as a Terraria-styled, top-down pixel workplace: your employees, their
tasks, the goals your managers are running, and what needs you. It is the host the pixel
office renderer (sub-project 4) plugs into and the codebase the phone app (7) is built
from.

## 2. Decisions carried in

| Decision | Chosen | Rejected |
|---|---|---|
| Platform | **.NET MAUI Blazor Hybrid, 100% C#** — Windows now; Android/iOS TFMs kept for sub-project 7 | Web UI + Tauri/Electron; Godot |
| Login | **CLI setup only, fully local** — verify and launch `claude` / `codex` logins; no accounts | Better Accounts integration in v1 |
| Look | **Terraria-style UI everywhere, office top-down** — original assets and CSS, no Terraria art | Clean shell with a pixel office inside |
| Process model | **The app launches and supervises context-api + foreman** as child processes; connect-only mode for dev | In-process hosting; connect-only as the only mode |
| Live state | **Refetch-on-event**: the event stream tells the app *what* changed, the app refetches it | Client-side event replay |
| UI placement | **Razor Class Library** holds components, store, pump, CSS; the MAUI project is a thin host | Components inside the MAUI project |

## 3. Goals / non-goals

**Goals:** boot both services from one exe; verify and launch CLI logins; show employees,
tasks, goals, and live activity; perform every management action the Foreman API offers
from the UI; surface `human.needed`; look like one game; be testable without a running
service; run on this Windows machine today.

**Non-goals (deferred):** the animated office renderer (4) — v1 shows a top-down placeholder
room; email/push/voice (5); VM layer (6); Android/iOS builds (7); accounts and sync;
theming beyond the one skin; editing employee files in-app.

## 4. Projects

```
home-workplace/
├── libs/
│   ├── HomeWorkplace.Client/        net8.0 classlib: DTOs, ForemanClient, ContextApiClient,
│   │                                ServiceSupervisor, CliSetupChecker, IProcessRunner
│   └── HomeWorkplace.UI/            net8.0 Razor Class Library: components, AppStore,
│                                    EventPump, wwwroot/css/pixel.css, fonts
├── apps/desktop/HomeWorkplace.App/  MAUI Blazor Hybrid host: BlazorWebView, DI wiring,
│                                    real ProcessRunner, app.json, packaging
└── tests/HomeWorkplace.UI.Tests/    xunit + bUnit: components, store, pump, clients,
                                     supervisor, setup checker — no real service, no real CLI
```

`HomeWorkplace.Client` owns its own DTO records mirroring the API JSON (camelCase, enums
as numbers as the services emit them today). It does not reference the service projects.

## 5. Boot

`ServiceSupervisor.StartAsync()`:

1. Read `app.json` next to the executable: `{ connectOnly, foremanUrl, contextApiUrl,
   contextApiCommand, foremanCommand }`. Defaults: `false`, `http://localhost:5172`,
   `http://localhost:5171`, and in dev `dotnet run --project <repo>/services/...`; in release
   the bundled `HomeWorkplace.ContextApi.exe` / `HomeWorkplace.Foreman.exe` beside the app.
2. Unless `connectOnly`: launch both through `IProcessRunner` with the environment scrubbed
   of every `CLAUDE*` / `ANTHROPIC*` variable (same rule as Foreman's provider).
3. Poll both `/health` up to 60 s, reporting progress to the boot screen.
4. On app exit, stop what it started. Never stop a service it did not start.

The app must be launched normally — never from inside a Claude Code session — because
its child Foreman will spawn `claude`.

## 6. Setup screen

Per CLI, two checks through `IProcessRunner` (verified commands):

| | Installed | Signed in |
|---|---|---|
| Claude | `claude --version` exit 0 | `claude auth status` → JSON with `"loggedIn": true` |
| Codex | `codex --version` exit 0 | `codex login status` exit 0 and output contains "Logged in" |

Cards show installed / signed-in / not found. "Sign in" opens a terminal running the CLI's
own interactive login (`claude` for Claude — its first run prompts login; `codex login`).
Re-check on window focus and on a Refresh button. The app stores nothing.

## 7. Client

`ForemanClient` covers every Foreman endpoint: employees (list, get, reload, wake(until),
sleep, reset), tasks (create, list(status, assignee), get, approve, answer, reassign, retry,
cancel), goals (create, list, get, topup, cancel), events(since, wait, limit), health.
`ContextApiClient`: room brief (`context?format=text`), list files, get file. Errors: a
non-2xx becomes `ApiException(status, title, detail)` from the problem details.

## 8. State and live updates

`AppStore`: dictionaries of employees, tasks, goals by id; a bounded recent-events list;
`bool ServicesUp`; `int HumanNeeded` (count of open needs-human items); `event Action Changed`.

`EventPump` (started after boot): loop `GET /events?since=&wait=30`; for each event:
`employee.state` → refetch that employee; `task.state` / `run.*` / `handoff.*` /
`wrapup.written` → refetch that task; `goal.*` → refetch that goal; `human.needed` → bump the
badge and raise a toast; `catalog.reloaded` → refetch all employees. Cursor carried forward;
`truncated` → full refetch. On error: backoff 1 s → 30 s, `ServicesUp=false` until a
success. Initial load = full refetch of all three collections.

## 9. Screens

Left nav rail; six screens. Every action calls the client, then the store refetches on
the resulting event (no optimistic updates).

| Screen | Shows | Actions |
|---|---|---|
| Office | a top-down room: one desk tile per employee with name, status colour, current task title; a `<canvas id="office">` sized to the room for sub-project 4 | click a desk → Employees detail |
| Employees | slot grid; detail: role, vendor, model, status, energy, runs today, current task, schedule | wake (with until), sleep, reset, reload catalog |
| Tasks | list with filters; detail: brief, status, assignee, runs, progress ledger, room brief | create; approve, answer, reassign, retry, cancel by status |
| Goals | list; detail: budget bar (spent/budget), status, manager, last decision, child tasks | create; top up, cancel |
| Activity | live event feed, newest first, filter by type | — |
| Setup | CLI cards; services status; connect settings | sign in, refresh |

The `HumanNeeded` badge sits on the nav; clicking it filters Tasks to `needs-human` and
Goals to `blocked`.

## 10. Look and feel

Terraria-style, original. `pixel.css` defines the tokens and the primitives use only them:

- Palette: frame `#1b1f3a`, panel `#2b3055`, panel-light `#3a4170`, border-dark `#0d0f22`,
  border-light `#7b85c9`, highlight `#f0d78c` (gold), text `#f4f1e8`, text-dim `#b9b7c9`,
  status colours: awake `#7bd88f`, working `#f0d78c`, waiting `#8fb8f0`, asleep `#8c8c9a`,
  needs-human `#f08c7b`, blocked `#f0a07b`.
- Panels: 2-px pixel border (dark outside, light inside) with a 1-px gold top highlight;
  no anti-aliasing (`image-rendering: pixelated` on art, crisp borders).
- Font: **Pixelify Sans** (SIL OFL, bundled in `wwwroot/fonts`) — 18 px body, 24 px
  headers; monospace fallback. Text must stay readable: no pixel font below 16 px.
- Primitives: `PxPanel(title)`, `PxButton(variant: primary|danger|ghost)`, `PxSlot(state,
  tooltip)`, `PxBar(value, max, colour)`, `PxBadge`, `PxToast`, `PxTooltip`, `PxTabs`.
- Slot grids for employees, with border colour by status; hover tooltips everywhere a
  Terraria item would have one.

## 11. Errors

Boot failure → boot screen with the failing service, its last log lines, and Retry / Open
in connect-only mode. API error → toast with the problem-details title. Event pump down →
a red "reconnecting" strip under the nav. Nothing is silent.

## 12. Testing

bUnit + xunit against the RCL; nothing touches a real service or CLI.

1. `ForemanClient` / `ContextApiClient`: request paths/bodies via a stub `HttpMessageHandler`;
   DTO parsing from fixtures **recorded from the real running Foreman** in this repo.
2. `ServiceSupervisor` with a fake `IProcessRunner` + fake health: launches both, waits,
   reports progress, stops only what it started, honours `connectOnly`.
3. `CliSetupChecker` with a fake runner: the four states per CLI.
4. `EventPump` with a fake client: each event type refetches the right thing; cursor carried;
   truncated → full refetch; backoff on error.
5. Components: nav badge count; Office renders one desk per employee with the right colour;
   Employees detail actions by status; Tasks detail actions by status; Goals budget bar math
   and blocked state; Setup cards for each state; toasts on `human.needed`.
6. Smoke: build and launch the real app on this Windows machine, boot both services, walk
   the acceptance flow.

## 13. Acceptance

Launch one exe → boot screen completes with both services up → Setup shows both CLIs
green → create a goal from Goals → tasks appear in Tasks and events flow in Activity →
approve a task and top up the goal from the UI → goal reaches Done.

## 14. Risks

- MAUI Windows desktop rough edges (WebView2 required — present on Windows 11 here;
  unpackaged run via `WindowsPackageType=None` for development).
- Bundling service binaries in release is packaging work; v1 dev mode uses `dotnet run`.
- Enums arrive as numbers over HTTP today; the DTOs map them explicitly so a later switch
  to string enums on the services does not break the app.
