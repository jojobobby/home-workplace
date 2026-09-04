# Home Workplace — desktop app

One exe that boots your company. It launches the two services, checks that your Claude and
Codex CLIs are signed in, and shows your employees, tasks, goals, and live activity as a
Terraria-styled, top-down pixel workplace. .NET MAUI Blazor Hybrid, 100% C#, Windows today
(the Android/iOS targets are kept in the project for the phone app later).

## Run it

Build and launch:

```bash
dotnet build apps/desktop/HomeWorkplace.App -f net8.0-windows10.0.19041.0
```

```bash
start "" apps\desktop\HomeWorkplace.App\bin\Debug\net8.0-windows10.0.19041.0\win10-x64\HomeWorkplace.App.exe
```

**Launch it from a normal shell — never from inside a Claude Code session.** The app
starts Foreman, and Foreman spawns `claude`; a `claude` that inherits a Claude Code
session's environment is treated as nested and refused subscription access. The app scrubs
that environment for everything it launches, but the app itself must start clean.

What happens on boot (measured here: both services healthy in 7 s):

1. `app.json` beside the exe is read (missing = the dev defaults below).
2. context-api and foreman are launched with `dotnet run` from the repo root, found by
   walking up from the exe until `HomeWorkplace.sln` appears.
3. The boot screen waits for both `/health` checks, then the app appears and the event
   pump starts. If a service never comes up, you see its last output and a Retry button.
4. Closing the window stops the services the app started — and only those.

## Setup — the "login"

Agents run through the `claude` and `codex` CLIs' own logins on your PC, so the app never
holds a credential. The Setup screen runs `claude --version` / `claude auth status` and
`codex --version` / `codex login status`, shows each CLI as *Signed in*, *Not signed in*,
or *Not installed*, and its **Sign in** button opens the CLI's own interactive login in a
terminal window. Refresh re-checks.

## Screens

| Screen | What it shows | What you can do |
|---|---|---|
| Office | a top-down room, one desk per employee with status and current task; the canvas sub-project 4 will draw into | click a desk → Employees |
| Employees | your team as item slots, border colour by status; detail with energy and schedule | wake (optionally until a time), send home, reset memory, reload the catalog |
| Tasks | list with a status filter; detail with brief, runs, progress ledger, and the room transcript | create; approve, answer, reassign, retry, cancel — only the actions that apply |
| Goals | list; detail with the budget bar, last decision, and child tasks | create; top up, cancel |
| Activity | the live event feed, newest first | filter by type |
| Setup | CLI status | sign in, refresh |

The badge on **Tasks** counts what needs you — tasks parked on a question or approval and
goals blocked on budget — and clicking it jumps there, filtered.

Live state is refetch-on-event: the app long-polls Foreman's `/events`, and each event
tells it *what* to refetch. Foreman stays the single truth; nothing is replayed client-side.

## `app.json`

```json
{
  "connectOnly": false,
  "foremanUrl": "http://localhost:5172",
  "contextApiUrl": "http://localhost:5171",
  "contextApi": { "command": "dotnet", "args": ["run", "--project", "services/context-api/src/HomeWorkplace.ContextApi"], "workingDirectory": null },
  "foreman":    { "command": "dotnet", "args": ["run", "--project", "services/foreman/src/HomeWorkplace.Foreman"], "workingDirectory": null },
  "healthPollMs": 500,
  "healthTimeoutSeconds": 120
}
```

`connectOnly: true` skips launching and connects to services you started yourself — handy
while developing them. A release build ships the service executables beside the app and
points `command` at them.

## Layout and tests

```
libs/HomeWorkplace.Client/   DTOs, ForemanClient, ContextApiClient, ServiceSupervisor,
                             CliSetupChecker — every process-shaped thing behind IProcessRunner
libs/HomeWorkplace.UI/       Razor Class Library: AppStore, EventPump, pixel.css, Px* primitives,
                             the screens, the Boot gate. Reused by the phone app later.
apps/desktop/HomeWorkplace.App/   the thin MAUI host: DI, BlazorWebView, app.json
tests/HomeWorkplace.UI.Tests/     bUnit + xunit against the two libraries — no real service, no real CLI
```

```bash
dotnet test HomeWorkplace.sln
```

Verified live on this machine: boot, both services from the app, Setup, live employee data,
stop-on-close. Not yet verified by a person: the look (the tests assert structure, not
pixels), and the full acceptance flow — creating a goal from Goals and watching it run to
Done — because that starts real `claude`/`codex` runs on your subscription. That click is
yours.

## Font

The design bundles **Pixelify Sans** (SIL Open Font License) at
`libs/HomeWorkplace.UI/wwwroot/fonts/PixelifySans.woff2`. The file is not in the repo yet;
until it is, `pixel.css` falls back to Silkscreen / Courier New / monospace. Drop the woff2
in that path and it is picked up with no other change.
