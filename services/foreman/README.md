# Foreman — worker runtime

Foreman turns folder-defined "employees" into working agents. It spawns `claude` /
`codex` CLI runs on your subscriptions, moves tasks between employees with returning
hand-offs, and cycles them through sleep and wake with a written progress ledger. It is
the runtime the rest of Home Workplace (desktop shell, office view, notifications) sits on.

Foreman never calls a model API and holds no API key. Agents run only through the
official CLIs, on your subscription.

## Run it

```bash
dotnet run --project services/foreman/src/HomeWorkplace.Foreman
```

Foreman listens on `http://localhost:5172` (and `https://localhost:7172`). It talks to the
context-api room service on `http://localhost:5171`, so start that too.

**Launch from a clean terminal — never from inside a Claude Code session.** A `claude`
spawned by a nested session inherits `CLAUDE_CODE_CHILD_SESSION` and is refused
subscription access. Foreman's provider scrubs every `CLAUDE*` / `ANTHROPIC*` variable
from the child environment for the same reason, but the Foreman process itself must not be
a child of Claude Code.

## Employees

An employee is a folder under `employees/`:

```
employees/ada-coder/
├── employee.json    # id, name, role, vendor, model, effort, tools, schedule
├── skills.md        # what it does and how it works — goes into the system prompt
└── life.md          # persona, temperament, working hours
```

`vendor` is `claude` or `codex`. `effort` and `claudeAllowedTools` apply to Claude;
`codexSandbox` (`read-only` / `workspace-write` / `danger-full-access`) applies to Codex.
`schedule.wake`/`sleep` are local 24-hour times. Edit a folder and `POST /employees/reload`.

## The day, memory, and hand-offs

- **Working memory** is one resumable CLI session per task per day.
- **Sleep** at the employee's `sleep` time runs a wrap-up that writes `{done, next}`
  bullets into the task's progress, then drops the session — the employee "forgets" but
  the ledger stays. **Wake** resumes from those bullets.
- **Sub-ask hand-off:** an employee can stop, ask a teammate a question, and continue its
  own session with the answer. A whole task can be reassigned to another employee, across
  vendors — it is re-seeded from the room transcript and the progress ledger.

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/employees` | Definitions + live state |
| GET | `/employees/{id}` | One employee |
| POST | `/employees/reload` | Re-read the `employees/` folder |
| POST | `/employees/{id}/wake?until=HH:mm` | Wake now (optionally until a time) |
| POST | `/employees/{id}/sleep` | Wrap up and sleep now |
| POST | `/employees/{id}/reset` | Wrap up and forget, stay awake |
| POST | `/tasks` | `{title, brief, assignee, requiresApproval?}` |
| GET | `/tasks?status=&assignee=` | List |
| GET | `/tasks/{id}` | One task |
| POST | `/tasks/{id}/approve` | Sign off a task awaiting approval |
| POST | `/tasks/{id}/answer` | `{text}` — answer a task's question, resume it |
| POST | `/tasks/{id}/reassign` | `{assignee}` |
| POST | `/tasks/{id}/retry` | Re-queue a failed task |
| POST | `/tasks/{id}/cancel` | Cancel a task, discarding any live run |
| GET | `/events?since=&wait=&limit=` | Runtime event stream (cursor + long-poll) |
| GET | `/health` | Liveness |

## State and config

Tasks, employee state, and events persist under `data/` (configurable), so a restart
loses nothing: a task caught mid-run comes back queued, employees come back asleep for the
DayCycle to wake on schedule, and the event cursor continues. Every limit lives under the
`Foreman` config section (`Foreman__MaxRunMinutes`, `Foreman__EmployeesPath`, …).

## Acceptance

`scripts/acceptance.ps1`, run from a clean terminal against the real CLIs, drives a
two-employee task with a hand-off and a wrap-up end to end. It is also where the CLI output
fixtures used by the provider tests should be confirmed against your CLI version.
