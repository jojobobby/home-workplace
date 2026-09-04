# Manager Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add goals, a manager agent role, and dollar budgets to Foreman so a manager employee decomposes a goal into worker tasks, reacts as they settle, and completes it within budget.

**Architecture:** `GoalBook` owns goals and the budget; `ManagerComposer` builds the manager prompt; `ManagerActions` parses the action schema and executes it against `TaskBook`; `RunSupervisor` gains a manager-run path on the existing providers; `TaskBook.ApplyResult` reports settled worker tasks and run costs back to `GoalBook`.

**Tech Stack:** .NET 8, existing Foreman project and test fakes (`FakeAgentProvider`, `FakeContextApi`, `FakeTimeProvider`).

**Spec:** `docs/superpowers/specs/2026-09-03-manager-loop-design.md`

## Global Constraints

- All code in `services/foreman/src/HomeWorkplace.Foreman`, tests in `services/foreman/tests/HomeWorkplace.Foreman.Tests`. Namespace `HomeWorkplace.Foreman`.
- Enum names must not collide with `System.Threading.Tasks` (`GoalState`, not `GoalStatus`).
- No real CLI in tests. Manager runs go through the same `IAgentProvider`.
- Budget is USD. `Cost.Of(usage, model, pricing)`: `CostUsd` if present, else tokens × `Pricing[model]` (fallback `Pricing["default"]`), else 0.
- Manager runs only on: goal create, child settle (done/failed), top-up from blocked. Never per-event.
- Before spawning any run for a goal: `spentUsd >= budgetUsd` → goal `Blocked` + `human.needed`, no spawn.
- `MaxActionsPerRun` default 5; extra actions ignored.
- Run the full Foreman suite after every task; commit per task.

---

### Task 1: Goal models, pricing options, Cost

**Files:** Modify `Models.cs`, `ForemanOptions.cs`; Create `Cost.cs`; Test `CostTests.cs`.

**Produces:**
- `enum GoalState { Planning, Running, Blocked, Done, Failed, Cancelled }`
- `class GoalModel { Id, Title, Brief, Manager, BudgetUsd, SpentUsd, Status, Room, List<string> TaskIds, LastDecision?, CreatedAt, UpdatedAt }`
- `record Decision(DateTimeOffset At, string Summary)`
- `record ModelPrice(decimal In, decimal Out)`; `ForemanOptions.Pricing : Dictionary<string, ModelPrice>` (with `"default"`), `MaxActionsPerRun = 5`
- `TaskModel.GoalId : string?`
- `record CreateGoalRequest(string? Title, string? Brief, string? Manager, decimal BudgetUsd)`, `record TopUpRequest(decimal AddUsd)`
- `record ManagerAction(string Kind, string? Assignee, string? Title, string? Brief, string? To, string? Text, string? Reason)`
- `record ManagerDecision(string Summary, IReadOnlyList<ManagerAction> Actions)`
- `static class Cost { static decimal Of(Usage u, string model, IReadOnlyDictionary<string, ModelPrice> pricing) }`

- [ ] Test: `CostTests` — reported CostUsd wins; tokens × model price; falls back to default price; no data → 0.
- [ ] RED → implement → GREEN → commit `feat(foreman): goal models, pricing table, Cost`.

### Task 2: GoalBook + persistence + create/get/list endpoints

**Files:** Create `GoalBook.cs`; Modify `FileStore.cs` (SaveGoal/LoadGoals), `StateRecovery.cs`, `Program.cs`; Test `GoalTests.cs`.

**Produces:** `GoalBook { CreateAsync(req) → GoalModel; Get(id); List(); Save(g); SeedFrom(goals); AddCost(goalId, usd); IsOverBudget(goalId); Block(goalId); TopUp(goalId, usd) → bool; Cancel(goalId) }`. Create posts "Goal created" into `goal-<id>`. Endpoints `POST/GET /goals`, `GET /goals/{id}`.

- [ ] Tests: create returns Planning with room + announcement; unknown manager 400; budget ≤ 0 400; list/get; goals survive restart (Existing factory).
- [ ] RED → implement → GREEN → commit.

### Task 3: Manager run — composer, actions, supervisor path

**Files:** Create `ManagerComposer.cs`, `ManagerActions.cs`; Modify `RunSupervisor.cs` (`RunManagerAsync(goalId)`), `GoalBook.cs` (trigger on create), `FakeAgentProvider` (already returns any RunResult; manager result is parsed from `RunResult.Summary`? — NO: add `IAgentProvider.RunManagerAsync(spec) → ManagerDecision`), `IAgentProvider`, both CLI providers (schema + parse), test fake.

**Produces:**
- `IAgentProvider.RunManagerAsync(RunSpec spec, CancellationToken) → Task<ManagerDecision>` with a manager schema.
- `ManagerComposer.BuildSystemPrompt(manager, goal)`, `BuildRunPrompt(goal, roster, children, skippedNotes)`.
- `ManagerActions.Execute(goal, decision, …)` → applies create_task/message/wait/complete/fail, honoring `MaxActionsPerRun`, recording skipped unknown assignees on the goal (`PendingNotes`).
- `RunSupervisor.RunManagerAsync(goalId)`: busy-latch on the manager employee; budget check first; build spec (session per goal per day stored on `GoalModel.Session`); provider → decision; `AddCost` (manager run); `Execute`; `goal.decision` event; Planning→Running.
- `FakeAgentProvider.EnqueueDecision(ManagerDecision)`.

- [ ] Tests: create goal → manager run whose prompt contains title, roster ids, `$0.00 / $budget`; two `create_task` actions → two tasks with GoalId pumped to employees; `complete` → Done; `fail` → Failed; unknown assignee skipped and named in next prompt.
- [ ] RED → implement → GREEN → commit.

### Task 4: Settle hook, cost accrual, budget block, top-up, cancel

**Files:** Modify `TaskBook.cs` (ApplyResult → `GoalBook.OnRunFinished(task, usage, model)` + `OnTaskSettled(task)`), `GoalBook.cs`, `RunSupervisor.cs` (budget check before worker runs for goal tasks too), `Program.cs` (topup/cancel endpoints).

**Produces:** worker run cost → `spentUsd`; settled worker task → `RunManagerAsync(goal)` if Running; over-budget → `Blocked` + `human.needed`, no spawn; `POST /goals/{id}/topup` → `budgetUsd += add`, Blocked→Running + manager run; `POST /goals/{id}/cancel` → Cancelled + cancel open children.

- [ ] Tests: worker completion re-runs manager with that task's status/summary in prompt; worker `Usage.CostUsd` accrues; exceeding budget blocks (state + event) and suppresses the next spawn; topup unblocks and re-runs manager; worker `failed` re-runs manager; cancel cancels children.
- [ ] RED → implement → GREEN → commit.

### Task 5: Docs

**Files:** Modify `services/foreman/README.md` (Goals section, endpoints, budget caveat), `README.md` roadmap (2 → built, 3 → next), `employees/` add `mia-manager` (Claude, role Manager, skills.md describing decompose/verify/economize).

- [ ] Full solution test green → commit `docs+employees: manager loop` → push.

## Self-Review

Spec §4–§9 map to Tasks 1–4; §7 endpoints split across 2 and 4; §8 persistence in 2; §9 tests distributed. Type names consistent (`GoalModel`, `GoalState`, `ManagerDecision`, `ManagerAction`). Manager result parsing is a distinct provider method so the worker result schema is untouched.
