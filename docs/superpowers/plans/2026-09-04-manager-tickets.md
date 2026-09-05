# Manager Tickets (4f) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A manager claims a ticket, turns it into a goal, cuts it into tasks and sub-tickets for the team, and the ticket closes with the goal.

**Architecture:** Reuse the manager loop: a manager claim converts the ticket into a linked goal; a new `post_ticket` decision action pins sub-tickets; goal terminal states close the ticket.

**Tech Stack:** as the repo.

**Spec:** `docs/superpowers/specs/2026-09-04-manager-tickets-design.md`

## Global Constraints

Fakes only in tests; commit per task; full gate before pushing.

---

### Task 1: Foreman — manager claim → goal, `post_ticket`, ticket closes with the goal
**Files:** `Models.cs`, `GoalModels.cs`, `TaskBook.cs`, `GoalBook.cs`, `RunSupervisor.cs`, `ManagerActions.cs`, `ManagerComposer.cs`, `ForemanOptions.cs`, `Program.cs` (ticket budget passthrough); tests `ManagerTicketTests.cs`.
- [ ] Tests: manager claim → goal linked both ways, ticket Running under the manager, manager run requested; decision `[create_task ada, post_ticket role=Software engineer]` → one direct child, one sub-ticket with GoalId that an idle engineer claims; `complete` → goal Done and ticket Done; goal cancel → ticket Cancelled; the run prompt mentions `post_ticket` and lists roles. RED → implement → GREEN → commit.

### Task 2: Client and office
**Files:** `Dtos.cs`, both fakes; `Ui/Actions.cs` (budget field for manager roles), `Ui/DialogueScript.cs` (budget in the listing), `OfficeUi.cs` (decision toast); tests `ClientTests.cs`, `ActionsTests.cs`, `OfficeUiTests.cs`.
- [ ] Tests: `PostTicket("Engineering manager")` opens Title/Brief/Budget and posts the budget; a non-manager role has no budget field; a `goal.decision` event toasts "Mia planned 2 tasks". RED → implement → GREEN → docs → full gate → commit → push.

## Self-Review
Spec §2 → Task 1; §3–4 → Task 2. Names: `BudgetUsd`, `TicketId`, `CreateFromTicketAsync`, `HandToGoal`, `CloseTicketOfGoal`, `post_ticket`, `DefaultTicketBudgetUsd`.
