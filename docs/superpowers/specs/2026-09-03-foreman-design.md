# Foreman — Worker Runtime for Home Workplace (Design)

Date: 2026-09-03
Status: Approved in discussion; ready for implementation planning
Parent product: Home Workplace (see `docs/superpowers/specs/2026-09-03-agency-together-design.md`
for the coordination substrate this builds on)

## 1. Premise

Home Workplace is a desktop and phone app that shows your company as a top-down
pixel-art office. Employees are AI agents — Claude, Codex, others — each a "virtual
person" with a `skills.md` and a `life.md`. You click one and give it a task. They
hand work to each other, go to sleep at night, forget the day's details but keep a
ledger of what they got done, and pick the task back up in the morning. Managers
run teams on a budget. The app notifies you when a human is needed.

**Foreman is the worker runtime**: the process that makes employees exist, gives
them tasks, runs them on your Claude and Codex *subscriptions* through the
official CLIs, moves work between them, and puts them to sleep and wakes them up.
It is the first sub-project of Home Workplace and everything else — manager loop,
desktop shell, office renderer, notifications, VM layer, phone app — sits on top
of it.

Agents run **only** through the `claude` and `codex` command-line apps on the
user's subscriptions. Foreman never calls a model API and holds no API key.

## 2. Decisions carried into this design

Made with the user on 2026-09-03; alternatives listed so nobody re-litigates them
by accident.

| Decision | Chosen | Rejected |
|---|---|---|
| Worker model | **One Foreman daemon; employees are logical state records; one CLI run per unit of work** | A daemon per employee; stateless per-task spawn |
| Task model | **First-class `Task` record in Foreman; each task has a room** | Tasks as room messages; task record with the transcript as source of truth |
| Hand-offs | **Sub-ask: employee stops, asks another, gets the answer, continues in the same session. Reassignment: whole task moves to another employee** | — |
| Memory | **Working memory = one resumable CLI session per task per day. Before sleep the employee writes bullets of what it got done; then forgets. Morning = fresh session + identity + task + bullets** | Foreman-written memory.md; long-term diary |
| Shape | **Separate ASP.NET service `services/foreman` that uses the room API over HTTP** | Folded into the room API process; CLI-only runtime |
| Language | **C# / .NET 8**, matching the room API and the user's stack | — |
| Repo | **`home-workplace` monorepo** (already renamed on GitHub); room API moves to `services/context-api` | — |

## 3. Goals

- Define employees as folders of plain files a person can read and edit.
- Give an employee a task and have it work the task through the official CLI on
  the user's subscription, in its own workspace folder, with its persona applied.
- Let an employee ask another employee for something mid-task and *continue*
  afterwards with its context intact; let a task be reassigned to another employee,
  including across vendors.
- Put employees to sleep on their own schedule with a written ledger of progress;
  wake them into a fresh session that resumes from the ledger. Allow both on demand.
- Persist tasks, progress, and employee state across Foreman restarts.
- Expose all of it over HTTP, plus a cursor + long-poll event stream, so the desktop
  shell and office renderer can be built against it without change.
- Be fully testable without a real CLI, and demonstrably correct with one.

## 4. Non-goals (deferred to later sub-projects)

| Deferred | Where it goes |
|---|---|
| Manager loop: decompose, assign, verify, iterate under a budget | Sub-project 2 |
| Budget semantics on a subscription (quota / turns / time, not dollars) — Foreman only *records* cost signals | Sub-project 2 |
| Email, push, AI-voice calls, approval UX | Sub-project 5 |
| Per-employee VM partitions | Sub-project 6 |
| Login UI for Claude/Codex; any UI at all | Sub-projects 3, 4, 7 |
| Energy as a limiter (v1 computes it for display only) | Later |
| Persistence of *rooms* (the room API's own phase 2) | context-api |
| Authentication on Foreman's HTTP surface | Later; localhost only for now |

## 5. Architecture

### 5.1 Processes

Two services, two processes, both localhost:

- **context-api** (existing room API, moved): rooms, messages, cursors, long-poll,
  firehose, per-room folder. Unchanged in behaviour.
- **foreman** (new): employees, tasks, runs, scheduler, events. Talks to context-api
  over HTTP as an ordinary client — the same way employees do.

**Foreman must be launched from a normal shell as a standalone process.** A `claude`
spawned from inside a Claude Code session inherits `CLAUDE_CODE_CHILD_SESSION=1`
and is refused subscription access. Foreman therefore must not run as a child of
Claude Code, and its provider must launch CLIs with a *clean* environment: it
strips every `CLAUDE*`, `CLAUDECODE*`, and `ANTHROPIC*` variable from the child
process environment and sets only what the provider needs. This is a correctness
requirement, not an optimisation.

### 5.2 Internal components

| Component | One job |
|---|---|
| `EmployeeCatalog` | Loads `employees/*/` folders into `EmployeeDefinition`s; holds `EmployeeState`; reload on demand |
| `TaskBook` | Owns `Task` records and the state machine; the only writer of task state |
| `RunSupervisor` | At most one live run per employee; launches via a provider; enforces timeout; delivers `RunResult` to `TaskBook` |
| `IAgentProvider` + `ClaudeCliProvider`, `CodexCliProvider` | Turn a `RunSpec` into a CLI invocation and its output into a `RunResult`; hide vendor differences |
| `PersonaComposer` | Builds the system prompt and the run prompt from definition files, task, progress, and the room brief |
| `DayCycle` (a `BackgroundService`) | Ticks; compares each employee's schedule to the clock; triggers sleep (wrap-up) and wake (resume) |
| `EventLog` | Ring buffer of `RuntimeEvent`s with cursor + long-poll |
| `FileStore` | Atomic JSON persistence for tasks and employee state; replay at startup |
| `ContextApiClient` | Thin HTTP client for the room API: create/post/read/brief/put file |
| `ForemanEndpoints` | Minimal-API routes; validation; HTTP shapes |

All time comes from an injected `TimeProvider` so tests can drive the clock.

## 6. Employees

### 6.1 Definition (files, user-editable)

```
employees/
└── ada-coder/
    ├── employee.json
    ├── skills.md
    └── life.md
```

`employee.json`:

```json
{
  "id": "ada-coder",
  "name": "Ada",
  "role": "Software engineer",
  "vendor": "claude",
  "model": "claude-haiku-4-5-20251001",
  "effort": "low",
  "claudeAllowedTools": ["Bash(curl *)", "Bash(dotnet *)", "Read", "Edit", "Write", "Glob", "Grep"],
  "codexSandbox": "workspace-write",
  "schedule": { "wake": "09:00", "sleep": "20:00" },
  "maxRunMinutes": 30
}
```

- `id` — folder name; `^[a-z0-9][a-z0-9_-]{0,63}$`, same rule as room ids.
- `vendor` — `claude` or `codex`. `effort` applies to Claude only. `claudeAllowedTools`
  applies to Claude only (passed to `--allowedTools`); `codexSandbox` applies to Codex
  only (`read-only | workspace-write | danger-full-access`, passed to `-s`). A file
  may carry both so an employee can be re-vendored by editing one field.
- `schedule` — local wall-clock times, 24h. `sleep` earlier than `wake` means the
  employee works through midnight.
- `maxRunMinutes` — per-run wall-clock cap; optional, default from config.

`skills.md` — what the employee is good at and how it works (tools, conventions,
checklists). `life.md` — persona, temperament, energy, how it writes, what it does
at 8 PM. Both are free text and go into the system prompt verbatim.

### 6.2 Runtime state

```
EmployeeState { id, status: awake | asleep | working | waiting, currentTaskId?,
                runsToday, lastRunAt?, awakeOverrideUntil?, energy }
```

- `working` — a run is live. `waiting` — its current task is parked on a sub-ask or
  on a human. `awake`/`asleep` per the schedule and overrides.
- `energy` — display only in v1: `100 - 10 * runsToday`, floored at 0. Never gates
  anything.
- One employee works at most one task at a time; a second task assigned to it
  queues.

The catalog is loaded at startup and by `POST /employees/reload`. An employee
removed from disk while it has tasks keeps its state until those tasks finish or
are reassigned; it accepts no new tasks.

## 7. Tasks

### 7.1 Record

```
Task {
  id, title, brief, assignee, status, requiresApproval,
  parentId?, childIds[],
  room,                       // "task-<id>" in context-api
  workspace,                  // data/workspaces/<id>/
  session?: { vendor, sessionId, day },
  progress[]: { author, date, done[], next[] },
  runs[]:     { id, employee, startedAt, endedAt, status, usage, resultSummary },
  pendingAnswer?: { from: employeeId | "human", text },
  createdAt, updatedAt
}
```

### 7.2 State machine

```
queued ──(assignee status is awake, i.e. not working or waiting)──► running
running ──result: handoff────────► waiting ──(child done)──► running
running ──result: needs_human────► needs-human ──(POST answer)──► running
running ──result: done───────────► done            (requiresApproval = false)
running ──result: done───────────► needs-human     (requiresApproval = true) ──(POST approve)──► done
running ──result: failed / timeout / CLI error──► failed
any non-terminal ──(POST cancel)─► cancelled
any non-terminal ──(POST reassign)► queued          (assignee changed; session dropped)
```

Terminal: `done`, `failed`, `cancelled`. `failed` tasks can be reassigned or
re-queued (`POST /tasks/{id}/retry`), which returns them to `queued`.

### 7.3 Sub-ask (hand-off that returns)

When a run ends with `status: handoff` and `ask: { to, question }`:

1. If `to` is not a known employee, treat it as `needs_human` with the question.
2. Create a child task: `title = "Q from <parent assignee>: <first 60 chars>"`,
   `brief = question + a pointer to the parent's room`, `assignee = to`,
   `parentId = parent.id`. Child gets its own room and workspace.
3. Parent → `waiting`; parent's employee → `waiting` (it does no other work in v1 —
   it is one person, and it is waiting).
4. When the child reaches `done`, its `resultSummary` becomes
   `parent.pendingAnswer = { from: child.assignee, text }`, parent → `running`, and
   the next run **resumes the parent's session** with the answer as the prompt.
5. Both events are posted into both rooms as ordinary messages from Foreman so the
   office view and any human reading the chat see the hand-off.

Depth is unbounded in principle; v1 caps parent chains at 5 and fails the run with
a clear message beyond that.

### 7.4 Reassignment

`POST /tasks/{id}/reassign { assignee }`: status → `queued`, `assignee` replaced,
`session` cleared. The new employee's first run is seeded from the room's
`context?format=text` brief plus the full `progress[]`, so a Codex employee can pick
up where a Claude employee left off. The old employee's state returns to `awake`.

### 7.5 Rooms

Every task gets a room `task-<id>` in context-api, created on first post. Foreman
posts to it as agent id `foreman`, name `Foreman`, for: task created, run started,
run finished (summary), hand-off requested, answer delivered, wrap-up written,
reassigned. Employees post to it themselves during runs (their persona tells them
how). The room is the human-readable story of the task; the `Task` record is the
truth.

## 8. Runs and the provider adapter

### 8.1 `RunSpec`

```
RunSpec { runId, employee: EmployeeDefinition, taskId, workspace,
          systemPrompt, prompt,
          session: { mode: new | resume, sessionId },
          outputSchema, timeout }
```

### 8.2 Prompts (`PersonaComposer`)

System prompt, in order: a one-line identity (`You are <name>, <role>.`); `skills.md`;
`life.md`; the **house rules** — how to read and post to the task's room with curl
(the same block as the context-api README, filled in with `ContextApiBaseUrl`, the task's room, and the
employee's id/name), the rule that documents go in the room folder, and the
instruction that the final message must be the JSON result described below.

Run prompt for a **new** session: the task title and brief; every `progress[]` entry
as `Done on <date> by <author>:` bullets followed by `Next:` bullets; then the room
brief (`context?format=text`) as of now. For a **resume** with `pendingAnswer`: just
`Answer from <from>: <text>`. For a **wrap-up**: the wrap-up instruction (8.5).

### 8.3 Result schema

Every ordinary run must end with JSON matching:

```json
{
  "type": "object",
  "properties": {
    "status":    { "enum": ["done", "handoff", "needs_human", "failed"] },
    "summary":   { "type": "string" },
    "ask":       { "type": "object",
                   "properties": { "to": { "type": "string" }, "question": { "type": "string" } },
                   "required": ["to", "question"] },
    "artifacts": { "type": "array", "items": { "type": "string" } }
  },
  "required": ["status", "summary"]
}
```

`artifacts` are paths in the room folder the employee wrote. The provider passes the
schema to the CLI (`--json-schema` for Claude, `--output-schema <file>` for Codex)
**and** validates the parsed result itself; a run whose final output does not parse
is `failed` with the raw tail in `resultSummary`.

### 8.4 `RunResult`

```
RunResult { runId, status, summary, ask?, artifacts[],
            sessionId,            // the id to resume next time
            usage: { durationMs, inputTokens?, outputTokens?, costUsd?, turns? },
            rawTail }             // last 4 KB of stdout, for diagnostics
```

### 8.5 Wrap-up run

A special run on the task's live session whose prompt is: "Your day is ending. List
what you completed on this task as short bullets, then what should happen next."
Its schema is `{ done: string[], next: string[] }`, required both. The result is
appended to `Task.progress` with `author = employee id` and `date = today`. Then the
session is dropped. If the task has no live session (nothing ran today), no wrap-up
runs.

### 8.6 Provider mapping (flags verified from `--help` on 2026-09-03)

| Concern | `ClaudeCliProvider` | `CodexCliProvider` |
|---|---|---|
| Executable | `claude` | `codex` |
| Non-interactive | `-p`, prompt on stdin | `exec`, instructions on stdin |
| Model / effort | `--model <model>`, `--effort <level>` | `-m <model>`; effort not supported, ignored |
| Persona | `--append-system-prompt-file <tmp>` | Prepended to the instructions (no system-prompt flag) |
| Tools | `--allowedTools <list>` | `-s <codexSandbox>` |
| New session | `--session-id <uuid>` (Foreman generates the uuid) | Session id captured from `--json` events |
| Resume | `--resume <uuid>` | `codex exec resume <id>` |
| Structured result | `--output-format json` + `--json-schema <schema>` | `--json` + `--output-schema <file>` + `-o <file>` |
| Working dir | process cwd = workspace | process cwd = workspace |
| Environment | scrubbed (5.1) | scrubbed (5.1) |

The **exact JSON field names** for Claude's result envelope (usage, cost, session)
and for Codex's JSONL (session id, last message) are pinned in the plan by recorded
fixtures from one real run of each CLI in a clean terminal. Until then, the
providers are written against those fixtures; the spec does not guess the names.

### 8.7 Supervision

`RunSupervisor` allows one live run per employee. It starts the process with stdout
and stderr captured, applies `timeout` (the employee's `maxRunMinutes`, else
config), kills the process tree on timeout or cancel, and always produces a
`RunResult` (`failed` on timeout with `summary = "timed out after N minutes"`).
Temp files (system prompt, schema) live under `data/tmp/<runId>/` and are deleted
after the run.

## 9. Memory and the day cycle

- **Session per task per day.** `Task.session` holds the vendor, id, and the day it
  was opened. A run on a task with a session for *today* resumes it; otherwise a new
  session is opened and the prompt is the full new-session prompt (8.2).
- **Sleep.** At the employee's `sleep` time, `DayCycle` marks it `asleep` and, for
  its task with a live session, enqueues a wrap-up run (8.5), then clears the
  session. A run already in progress finishes first; wrap-up follows it.
- **Wake.** At `wake` time, `asleep → awake`; its `queued`/`running` task gets a run
  with a fresh session seeded from progress.
- **Force reset** (`POST /employees/{id}/reset`): wrap-up now, drop session, stay
  awake; the next run starts fresh from the bullets.
- **Call in** (`POST /employees/{id}/wake`, optional `until=HH:mm`): an asleep
  employee becomes awake until `until` or its next `sleep` time.
- **Send home** (`POST /employees/{id}/sleep`): wrap-up now, then asleep until wake.
- The scheduler ticks every `SchedulerTickSeconds` (default 30) and is idempotent:
  a missed tick (Foreman was down) is caught up on the next one.

## 10. HTTP API

`application/json`; errors are RFC 7807 problem details, as in context-api. No auth.

| Method | Path | Purpose |
|---|---|---|
| GET | `/employees` | Definitions + state |
| GET | `/employees/{id}` | One employee |
| POST | `/employees/reload` | Re-read the `employees/` folder |
| POST | `/employees/{id}/reset` | Wrap-up now, forget, stay awake |
| POST | `/employees/{id}/wake?until=` | Call in |
| POST | `/employees/{id}/sleep` | Send home now |
| POST | `/tasks` | `{ title, brief, assignee, requiresApproval? }` → 201 Task |
| GET | `/tasks?status=&assignee=` | List |
| GET | `/tasks/{id}` | One task, full record |
| POST | `/tasks/{id}/reassign` | `{ assignee }` |
| POST | `/tasks/{id}/answer` | `{ text }` — human answer for `needs-human`; resumes |
| POST | `/tasks/{id}/approve` | `needs-human` (approval) → `done` |
| POST | `/tasks/{id}/retry` | `failed` → `queued` |
| POST | `/tasks/{id}/cancel` | → `cancelled`; kills a live run |
| GET | `/events?since=&wait=&limit=` | Runtime event stream (11) |
| GET | `/health` | Liveness, plus whether context-api is reachable |

Validation: `assignee` must be a catalogued employee; `title` 1–200 chars; `brief`
1–32768 chars; unknown ids are 404; illegal transitions (e.g. `approve` on a
`running` task) are 409 with the current status in the detail.

## 11. Events

```
RuntimeEvent { seq, timestamp, type, employeeId?, taskId?, runId?, data }
```

Types: `employee.state`, `task.state`, `run.started`, `run.finished`,
`handoff.requested`, `handoff.answered`, `human.needed`, `wrapup.written`,
`task.reassigned`, `catalog.reloaded`. Ring buffer of `EventsCapacity` (default 5000)
with the same cursor + `wait` + `truncated` contract as the room API (`since` is
`seq`; timeout returns `200` with an empty list). This is the feed the office
renderer animates from.

## 12. Storage

- `data/tasks/<id>.json` — the full `Task`, rewritten atomically (write `<id>.json.tmp`,
  then rename) on every state change.
- `data/employees/<id>.state.json` — `EmployeeState`, same discipline.
- `data/events.jsonl` — append-only; the last `EventsCapacity` lines are replayed
  into the ring at startup so cursors survive a restart.
- `data/workspaces/<taskId>/` — the run's working directory; never deleted by Foreman.
- `data/tmp/<runId>/` — per-run temp files; deleted after the run.

Startup: load employees, replay tasks and states, replay events, then start the
scheduler. A task that was `running` when Foreman died becomes `queued` (its run is
gone); its session is kept if it is still today's.

## 13. Configuration (`appsettings.json`, section `Foreman`)

| Key | Default | Meaning |
|---|---|---|
| `EmployeesPath` | `../../employees` | Folder of employee definitions |
| `DataPath` | `./data` | Persistence root |
| `ContextApiBaseUrl` | `http://localhost:5171` | The room API |
| `ClaudeExecutable` | `claude` | Resolved via PATH |
| `CodexExecutable` | `codex` | Resolved via PATH |
| `MaxRunMinutes` | `30` | Default per-run timeout |
| `SchedulerTickSeconds` | `30` | Day-cycle tick |
| `EventsCapacity` | `5000` | Event ring size |
| `MaxHandoffDepth` | `5` | Parent-chain cap |

Kestrel binds `http://localhost:5172` and `https://localhost:7172`.

## 14. Monorepo layout and the migration (the plan's first task)

```
home-workplace/                       ← local folder renamed from "Agency Together"
├── HomeWorkplace.sln                 ← renamed from AgencyTogether.sln
├── global.json
├── README.md                         ← product-level; links to each service's README
├── employees/                        ← starter pack, see 14.1
├── services/
│   ├── context-api/
│   │   ├── README.md                 ← the current README, paths updated
│   │   ├── src/HomeWorkplace.ContextApi/
│   │   └── tests/HomeWorkplace.ContextApi.Tests/
│   └── foreman/
│       ├── README.md
│       ├── src/HomeWorkplace.Foreman/
│       └── tests/HomeWorkplace.Foreman.Tests/
├── docs/
│   ├── superpowers/                  ← specs and plans, unchanged
│   ├── agents/                       ← the two role prompts, unchanged
│   └── trials/                       ← saved room transcripts
└── .claude/launch.json               ← two entries: context-api (5171), foreman (5172)
```

The move renames projects and namespaces `AgencyTogether.Api` → `HomeWorkplace.ContextApi`
(and the test project likewise); behaviour and the 68 tests are unchanged and are the
guard for the move. The `Both/.claude/launch.json` used by this harness is updated to
the new paths as well. The service must be stopped for the move (it is).

### 14.1 Starter employees

- `ada-coder` — Claude (Haiku 4.5 by default), software engineer; `skills.md` covers
  reading a repo, TDD, posting progress; tools: Bash, Read, Edit, Write, Glob, Grep.
- `rex-reviewer` — Codex, code reviewer; `skills.md` covers reading diffs, writing
  numbered change requests; sandbox `read-only`.
- `vfx-artist` — Claude, a *stub* whose `skills.md` is a real, model-agnostic brief
  for consistent pixel-art/VFX work (palette, sizes, naming, deliverables in the
  room folder) so the "specialist worker" idea is exercised even before any art
  tooling exists.

Each has a `life.md` with a name, temperament, working hours, and how it wraps up its
day.

## 15. Testing

xunit, `WebApplicationFactory<Program>` for HTTP, `FakeTimeProvider`
(`Microsoft.Extensions.TimeProvider.Testing`) to drive the clock, a `FakeAgentProvider`
that returns scripted `RunResult`s and records every `RunSpec` it was given, and a
`FakeContextApi` (an in-process stub of the four room calls Foreman makes) so tests
never need context-api running. No real CLI is ever launched by the test suite.

Behaviours pinned:

1. Loading the starter employees; rejecting a malformed `employee.json` with the
   file named in the error.
2. `POST /tasks` on an awake, idle employee starts a run whose `RunSpec` carries the
   persona, the brief, an empty progress section, and the room brief.
3. One run per employee: a second task queues and starts when the first finishes.
4. Result `done` → `done` (no approval) / `needs-human` (approval); `approve` → `done`.
5. Result `handoff` creates the child task for the right employee, parks the parent
   in `waiting`, and — when the child finishes — resumes the parent's *same session id*
   with a prompt that is exactly the answer.
6. Result `needs_human` → `needs-human`; `POST answer` resumes with the text.
7. Unknown hand-off target degrades to `needs-human`.
8. Hand-off depth beyond `MaxHandoffDepth` fails the run with a clear summary.
9. Reassignment clears the session, re-queues, and seeds the new run with the room
   brief and full progress; works Claude → Codex in the `RunSpec`.
10. At `sleep` time a wrap-up run is issued on the live session, its `{done,next}`
    lands in `progress`, the session is cleared, the employee is `asleep`.
11. At `wake` time the task runs again with a *new* session whose prompt contains
    yesterday's bullets.
12. `reset` and `sleep`/`wake` endpoints do the same on demand; `wake?until=` expires.
13. A task with no live session gets no wrap-up.
14. Timeout kills the run and yields `failed` with the timeout summary.
15. Malformed final output → `failed` with the raw tail.
16. Every state change appears on `/events` in order; cursor, `wait`, and `truncated`
    behave as in context-api.
17. Restart: tasks, progress, employee state, and event cursors survive; a task that
    was `running` comes back `queued`.
18. Provider unit tests: each provider builds the exact argv for new/resume/wrap-up
    runs, scrubs the environment, and parses the recorded fixture output into a
    `RunResult`.

Plus `scripts/acceptance.ps1`, run by a person from a clean terminal against the real
CLIs: two employees, one task, one hand-off round trip, a forced wrap-up, a resumed
morning, one cross-vendor reassignment. Its expected transcript is checked into
`docs/trials/`.

## 16. Risks and assumptions

- **Subscription terms.** Programmatic, multi-worker use of consumer Claude/Codex
  subscriptions may sit outside their terms; the nested-session refusal shows the
  vendors have policy levers. The provider is an adapter so a sanctioned path (API
  key, team plan, or a future official agent runtime) is a drop-in. This is the
  product's largest external risk and is not solvable in code.
- **Untested CLI behaviours.** `--json-schema` / `--output-schema` enforcement, the
  result envelope fields, and Codex session-id capture are verified only as flags,
  not as behaviour. The plan's first provider task records fixtures from real runs.
- **Rate limits.** Several employees running back-to-back on one subscription will
  hit rate limits; v1 surfaces the CLI's error as a `failed` run and does not retry.
  Backoff belongs with the manager loop.
- **Clock.** Schedules are local wall-clock; DST transitions can make a day 23 or 25
  hours. v1 accepts this.
- **Workspaces are plain folders.** Until the VM layer exists, an employee with
  `Write` can write anywhere its tools allow; `claudeAllowedTools` and `codexSandbox`
  are the only fence. The starter employees are scoped conservatively.
