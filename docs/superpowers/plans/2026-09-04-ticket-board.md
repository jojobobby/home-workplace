# Ticket Board (4e) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pin tickets on a board; idle employees whose role fits claim and run them; the office shows the claim.

**Architecture:** Tickets are unassigned tasks in Foreman, claimed by the existing pump; the client exposes them; the office adds a board prop, a claim errand in the simulation, and a dialogue to post and take down tickets.

**Tech Stack:** as the repo.

**Spec:** `docs/superpowers/specs/2026-09-04-ticket-board-design.md`

## Global Constraints

- A ticket is `Assignee == ""` with an optional `Role`; nothing else changes about tasks.
- Fakes only in tests; a new golden fails once and is viewed. Commit per task; full gate before each push.

---

### Task 1: Foreman tickets and auto-claim
**Files:** modify `Models.cs` (Role, CreateTicketRequest), `TaskBook.cs` (CreateTicketAsync, Tickets, Claim), `RunSupervisor.cs` (Pump claims), `Program.cs` (routes); tests `TicketTests.cs`.
- [ ] Tests: `POST /tickets` → Queued, assignee "", role kept, room note; with an awake engineer `Pump` claims it (assignee set, `task.claimed` event, run started, room note); a reviewer does not claim an engineer ticket but any role claims a role-less ticket; two engineers × two tickets → both claimed, oldest first; `GET /tickets` lists only unclaimed. RED → implement → GREEN → commit.

### Task 2: Client
**Files:** `Dtos.cs`, `IForemanApi.cs`, `ForemanClient.cs`, both fakes; `ClientTests.cs`.
- [ ] Tests: `CreateTicketAsync` posts `{title, brief, role}` to `/tickets`; `GetTicketsAsync` gets `/tickets`; `TaskDto.Role` parses. RED → implement → GREEN → commit.

### Task 3: Office simulation and world
**Files:** `World.cs` (TicketBoard, TicketSpot), `Player.cs` (InteractKind.TicketBoard), `Commands.cs`, `Simulation.cs` (errand, OpenTickets), `ForemanFeed.cs`, `SpriteGenerator.cs` (`tickets`, `tickets_empty`), `SceneRenderer.cs`; tests `SimulationTests.cs`, `FeedTests.cs`, `PlayerTests.cs`.
- [ ] Tests: the board and spot exist and the spot is a target; `TicketClaimed` sends a seated Working agent to the board (bubble, sound moment) and back to typing; `TicketsChanged` sets `OpenTickets`; the feed maps `task.claimed` and emits `TicketsChanged` only when the count changes. Regenerate goldens, view, accept. RED → implement → GREEN → commit.

### Task 4: Office UI
**Files:** `UiAction.cs` (OpenTickets, PostTicket, PickTicketRole), `DialogueScript.cs` (Tickets, TicketRoles), `Actions.cs`, `OfficeUi.cs` (interact, claim toast), `Overlay.cs` (`(board)`); tests `DialogueTests.cs`, `ActionsTests.cs`, `OfficeUiTests.cs`, `UiGoldenTests.cs` (`ui-tickets`).
- [ ] Tests: the board dialogue lists tickets with role and age and offers post/take down; the role pick lists "Any role" plus distinct employee roles; posting opens Title/Brief and calls `CreateTicketAsync(title, brief, role)`; take down confirms then cancels; a `task.claimed` event toasts; golden viewed. RED → implement → GREEN → commit.

### Task 5: Smoke, docs, memory
- [ ] Smoke: empty company, script `wait 8;board;wait 3;pick 0;wait 2;pick 0;wait 2;type Fix the parser;enter;type It crashes on empty input;enter;wait 6` → frames show the dialogue, the entry, and a pinned note; view. Docs: office README (tickets), root README (4e). Full gate → commit → push.

## Self-Review
Spec §2 → Task 1; §3 → 2; §4 → 3–4; §5 → distributed; smoke → 5. Names: `Role`, `CreateTicketRequest`, `CreateTicketAsync`, `Tickets`, `Claim`, `task.claimed`, `PropKind.TicketBoard`, `TicketSpot`, `InteractKind.TicketBoard`, `TicketClaimed`, `TicketsChanged`, `OpenTickets`, `OpenTickets` (action) → renamed `OpenTicketBoard` to avoid the clash with the sim property, `PostTicket`, `PickTicketRole`.
