# Manager Loop — Sub-project 2 of Home Workplace (Design)

Date: 2026-09-03
Status: Approved in discussion; ready for implementation planning
Builds on: `2026-09-03-foreman-design.md` (the worker runtime this extends)

## 1. Premise

Foreman can run one employee on one task. The manager loop lets you hand a **goal** to a
**manager** — an employee whose job is to decompose the goal into worker tasks, assign them,
verify results, re-plan on failure, and declare the goal done — all inside a **dollar
budget**. The manager is an agent; Foreman is the mechanical executor of its decisions.

## 2. Decisions carried in

| Decision | Chosen | Rejected |
|---|---|---|
| Budget unit | **US dollars**, from the `$` cost the CLIs report (Claude `total_cost_usd`; Codex = tokens × price table) | Run count; wall-clock; raw tokens |
| Manager mechanism | **Manager is an agent role**: its structured result is a list of actions Foreman executes | Hard-coded C# orchestration; one-shot decomposition |
| When the manager runs | **On goal creation and whenever a child task settles** (done/failed) — never on every event | Re-run per event; fixed polling |
| Over-budget behaviour | **Block the goal and ping a human** before spawning the run that would exceed it | Overspend; silently stop |

On a flat subscription these dollars are *notional* — nothing is billed — but they are the
unit the CLIs surface and the one a person has intuition for. Docs say so plainly.

## 3. Goals / non-goals

**Goals:** hand a goal + budget to a manager employee; manager creates worker tasks, reacts
to their completion, re-plans on failure, completes or fails the goal; every run's $ cost
accrues to the goal; the goal blocks (needs-human) at the budget; top-up resumes it; all of
it over HTTP and the event stream; fully testable with the existing fakes.

**Non-goals (later):** multi-manager hierarchies; parallel managers on one goal; budget
*forecasting*; per-employee salaries; any UI.

## 4. Model

### 4.1 Goal

```
Goal {
  id, title, brief, manager,          // manager = employee id
  budgetUsd, spentUsd,
  status: planning | running | blocked | done | failed | cancelled,
  room,                               // "goal-<id>" in context-api
  taskIds[],                          // worker tasks the manager created
  lastDecision?: { at, summary },     // the manager's last stated reasoning
  createdAt, updatedAt
}
```

`Task` gains `GoalId?`. A task with a `GoalId` is a **worker task**; its run costs accrue
to the goal.

### 4.2 Manager actions (the manager's structured result)

```json
{
  "type": "object",
  "properties": {
    "summary": { "type": "string" },
    "actions": { "type": "array", "items": {
      "type": "object",
      "properties": {
        "kind":     { "enum": ["create_task", "message", "wait", "complete", "fail"] },
        "assignee": { "type": "string" },
        "title":    { "type": "string" },
        "brief":    { "type": "string" },
        "to":       { "type": "string" },
        "text":     { "type": "string" },
        "reason":   { "type": "string" }
      },
      "required": ["kind"] } }
  },
  "required": ["summary", "actions"]
}
```

| kind | Foreman does |
|---|---|
| `create_task` | `TaskBook.Create` with `GoalId`, assignee, title, brief; adds to `goal.taskIds`; pumps |
| `message` | posts `text` into employee `to`'s current task room (or the goal room if none) as the manager |
| `wait` | nothing; the manager runs again when a child settles |
| `complete` | goal → `done`, posts summary to the goal room |
| `fail` | goal → `failed` with reason |

Unknown `assignee` in `create_task` → the action is skipped and the manager is told so in
its next prompt. An empty action list is treated as `wait`.

### 4.3 Cost

`Cost.Of(Usage, model, PricingTable)`: use `Usage.CostUsd` when present; otherwise
`inputTokens/1e6 × inPrice + outputTokens/1e6 × outPrice` from the table; otherwise `0`.
`PricingTable` lives in `ForemanOptions.Pricing` as `{ model: { in, out } }` in $/Mtok, with
a `default` entry. Every finished run's cost is added to its task's goal (`spentUsd`), and
manager runs add directly to the goal.

## 5. The loop

```
POST /goals  ──► goal: planning ──► manager run #1
manager run ends ──► execute actions ──► goal: running (or done/failed)
child task settles (done|failed) ──► if goal running: manager run
before ANY run for the goal: if spentUsd >= budgetUsd ──► goal: blocked, human.needed
POST /goals/{id}/topup {addUsd} ──► budgetUsd += addUsd; if blocked ──► running, manager run
POST /goals/{id}/cancel ──► cancelled; open child tasks cancelled
```

The manager's prompt each run contains: goal title + brief; team roster (id, name, role,
vendor, status); every child task (id, title, assignee, status, last result summary);
`spentUsd`/`budgetUsd`; its own last `summary`; and the instruction to answer with the action
schema. The manager keeps one resumable session per goal per day like any employee; sleep
wraps it up like any employee (its wrap-up lands on the goal's `lastDecision`).

A manager run while a worker run is live is fine — they are different employees. A manager
is one employee: one live run at a time, via the existing supervisor latch.

## 6. Components

| Component | Job |
|---|---|
| `GoalBook` | Owns `Goal` records; persistence; status transitions; budget check; `OnTaskSettled` hook |
| `ManagerComposer` | Builds the manager system prompt + per-run prompt from goal, roster, children, budget |
| `ManagerActions` | Parses the action schema; `Execute(goal, actions)` |
| `Cost` | Static: `Of(usage, model, pricing)` |
| `GoalEndpoints` | routes |

`RunSupervisor` gains a `RunManagerAsync(goalId)` path (same providers, different composer
and schema). `TaskBook.ApplyResult` calls `GoalBook.OnTaskSettled` when a task with a
`GoalId` reaches done/failed, and adds the run's cost to the goal.

## 7. HTTP

| Method | Path | Body / notes |
|---|---|---|
| POST | `/goals` | `{ title, brief, manager, budgetUsd }` → 201; manager must be a known employee; budgetUsd > 0 |
| GET | `/goals` | list |
| GET | `/goals/{id}` | full record |
| POST | `/goals/{id}/topup` | `{ addUsd }` > 0 |
| POST | `/goals/{id}/cancel` | cancels open child tasks too |

Events: `goal.state`, `goal.decision` (with the action list), `goal.blocked`.

## 8. Persistence

`data/goals/<id>.json`, same atomic-write discipline; loaded by `StateRecovery`. A goal
whose manager run was in flight at a crash simply gets a manager run on the next settle or
top-up (no special recovery).

## 9. Testing (fakes only)

1. `POST /goals` triggers a manager run whose prompt carries goal, roster, and `$0 / $budget`.
2. A manager `create_task` ×2 creates two worker tasks with `GoalId`, pumped to their employees.
3. When a worker task completes, the manager is re-run with that task's status and summary in its prompt.
4. Cost from a worker run (fake `Usage.CostUsd`) accrues to `goal.spentUsd`; manager run cost accrues too.
5. Spending past `budgetUsd` blocks the goal (`blocked`, `human.needed` event) and no further run is spawned.
6. `topup` unblocks and re-runs the manager.
7. `complete` → `done`; `fail` → `failed`.
8. A worker `failed` re-runs the manager (it can re-plan).
9. Unknown assignee in `create_task` is skipped and reported in the next manager prompt.
10. `cancel` cancels open children.
11. Goals survive restart.

## 10. Risks

- **Runaway manager.** A manager that keeps creating tasks burns budget; the budget block is
  the backstop, and `MaxActionsPerRun` (default 5) caps how many actions one run may emit.
- **Cost accuracy.** Codex cost is computed, not reported — the price table must be kept
  current; wrong prices mis-budget but never crash.
- **Notional dollars.** Stated above; the number is real *effort* even if not a real bill.
