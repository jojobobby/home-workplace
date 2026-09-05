# Home Workplace

Instead of prompting one chat window, you run a company. Home Workplace is a desktop (and
later phone) app that shows your AI workers as a top-down pixel-art office — Claude, Codex,
and others as "virtual people," each with a `skills.md` and a `life.md`. You click a worker
and give it a task. Workers hand work to each other, go home to sleep, forget the day's
details but keep a ledger of what they got done, and pick the task back up in the morning.
Managers run teams on a budget. The app pings you when a human is needed.

Everything runs on your Claude and Codex **subscriptions** through the official CLIs. No
model API keys.

## Monorepo

```
home-workplace/
├── apps/desktop/        # the MAUI Blazor Hybrid desktop app (one exe that boots the company)
├── libs/
│   ├── HomeWorkplace.Client/   # typed clients, service supervisor, CLI setup checks
│   └── HomeWorkplace.UI/       # Razor Class Library: store, event pump, pixel UI, screens
├── services/
│   ├── context-api/     # the shared room service: chat, cursors, long-poll, per-room folder
│   └── foreman/         # the worker runtime: employees, tasks, goals, runs, sleep/wake, hand-offs
├── employees/           # folder-defined starter team (ada-coder, rex-reviewer, vfx-artist, mia-manager)
├── tests/               # UI + client tests (each service keeps its own tests beside it)
├── scripts/             # acceptance.ps1
└── docs/                # specs, plans, agent prompts, trial transcripts
```

## Run it

The whole thing, as a person would: build the desktop app and launch it — it boots both
services itself. See [apps/desktop/README.md](apps/desktop/README.md).

```bash
dotnet build apps/desktop/HomeWorkplace.App -f net8.0-windows10.0.19041.0
```

The services on their own, from a normal terminal:

```bash
dotnet run --project services/context-api/src/HomeWorkplace.ContextApi
```

```bash
dotnet run --project services/foreman/src/HomeWorkplace.Foreman
```

context-api serves `http://localhost:5171`, Foreman `http://localhost:5172`. See
[services/foreman/README.md](services/foreman/README.md) for the runtime, goals, and the
employee-folder format, and [services/context-api/README.md](services/context-api/README.md)
for the room API.

Run every test suite:

```bash
dotnet test HomeWorkplace.sln
```

## Roadmap

Built:

1. **context-api** — the shared conversation and file substrate agents coordinate through.
2. **Foreman** — the worker runtime: employees from folders, first-class tasks, returning
   hand-offs, cross-vendor reassignment, sleep/wake with a progress ledger, crash-safe
   restart, real `claude`/`codex` providers, a cursor + long-poll event stream.
3. **Manager loop** — a manager employee decomposes a goal into worker tasks, reacts as
   they settle, re-plans on failure, and completes it inside a dollar budget; a goal that
   would overspend blocks and asks you for a top-up.
4. **Desktop shell** — one exe that boots the company, verifies the CLIs, and manages
   employees, tasks, and goals live in a Terraria-styled pixel UI.
5. **Office game (4a)** — a MonoGame top-down pixel office animated from the event
   stream: A* walking, typing, coffee runs, hand-off chats, dynamic lights and stencil
   shadows on a day/night schedule, particles, screen shake, synthesized sound effects.
   See `apps/office/README.md`.
6. **In-game management UI (4b)** — walk or click to an employee, RPG dialogue box
   (give tasks, approve, answer, wake/sleep, goals and budgets), Tab overlay with lists,
   whiteboard goals, toasts for approvals. The Blazor shell is no longer the primary app.
7. **Hiring stand (4d)** — the company starts empty; hire a role on a brain your
   subscriptions unlock (Haiku, Sonnet, Opus, Fable, GPT-5 Codex) with an approximate
   daily cost; let people go. Role templates live in `hiring/`.
8. **Ticket board (4e)** — pin tickets for a role; idle employees of that role claim and
   run them, walking to the board to take one.
9. **Manager tickets (4f)** — a ticket for a manager becomes a goal: the manager cuts it
   into tasks for named people or sub-tickets for a role, and the ticket closes with the goal.
10. **Office folder and boss desk (4g)** — the company lives in `Documents\Home Workplace\<office>\`;
    your desk's computer opens it in Explorer.

Next:

11. **Art and sound pipeline (4c)** — real sprite sheets (via the vfx-artist employee) and music
   replacing the procedural placeholders.
12. **Notifications** — email, push, AI-voice calls, approvals.
13. **VM/sandbox layer** — per-employee scoped control of a machine.
14. **Phone app** — the same UI library, built for Android/iOS.

## Note on subscription use

Driving several workers programmatically over consumer subscriptions may sit outside the
Claude Max / Codex terms. On 2026-09-04 this became concrete: headless `claude -p` runs on
the author's account were refused with HTTP 403 `oauth_org_not_allowed` ("Your organization
has disabled Claude subscription access for Claude Code · Use an Anthropic API key instead"),
while the interactive Claude Code app kept working.
Foreman's provider is a swappable adapter so a sanctioned path (API key, team plan, or a
future official agent runtime) is a drop-in. This is the project's largest external risk
and is not solvable in code.
