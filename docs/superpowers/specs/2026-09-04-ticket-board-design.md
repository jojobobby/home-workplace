# Ticket board (sub-project 4e) — design

Hire as many engineers as you like, then stop assigning work by hand: pin **tickets** on a
board and idle employees whose role fits pick them up themselves.

## 1. Decisions

| Decision | Choice |
|----------|--------|
| What a ticket is | a task with no assignee (`Assignee == ""`) and an optional `Role` it is meant for |
| Who claims | Foreman's pump: the oldest ticket goes to an idle, awake employee whose role matches (or anyone when the ticket has no role); claiming sets the assignee, posts to the room, emits `task.claimed`, and starts the run |
| Where you pin | a ticket board on the top wall next to the whiteboard; walk up (E) or click; the Tab overlay's Tasks tab shows tickets as `(board)` |
| Posting | title, brief, and a role picked from the roles present in the company plus "Any role" |
| In the office | a claim makes the employee walk to the board, take the ticket (a `!` bubble, a page sound), and walk back to type; the board shows pinned notes while tickets are open |
| Hiring many | nothing new: the stand already hires repeatedly (unique ids); the room seats 18 |

## 2. Foreman

- `TaskModel.Role` (string?, null = any). `CreateTicketRequest(Title, Brief, Role?, RequiresApproval)`.
- `TaskBook.CreateTicketAsync` (assignee "", room note "Ticket posted: TITLE (ROLE)"), `Tickets()`
  (queued, unassigned, oldest first), `Claim(taskId, employeeId)` (assignee set, saved,
  `task.claimed` event with `EmployeeId`, room note).
- `RunSupervisor.Pump`: after the assigned queue, each ticket is offered to the first awake,
  not-busy employee whose `Role` equals the ticket's role (case-insensitive) or any employee
  when the ticket has none; the claim then runs like any task.
- Endpoints: `POST /tickets`, `GET /tickets`. Reassign of a ticket is a claim by hand.

## 3. Client

`TaskDto.Role`; `CreateTicketRequest`; `IForemanApi.CreateTicketAsync`, `GetTicketsAsync`.

## 4. Office

- World: `PropKind.TicketBoard` (4×1) at the top wall right of the whiteboard, `TicketSpot`
  under it; sprites `tickets` (notes pinned) and `tickets_empty`.
- Sim: commands `TicketClaimed(EmployeeId)` and `TicketsChanged(Count)`; a claimed employee
  runs a board errand (walk there, 1.2 s at the board with a `!` bubble and a page sound,
  walk back) before the status behaviour (typing) resumes. `Simulation.OpenTickets` drives
  which board sprite is drawn.
- Feed: `task.claimed` → `TicketClaimed`; the count of open tickets → `TicketsChanged` when
  it changes.
- UI: `OpenTickets` (fetch → dialogue listing tickets with role and age; options **Post a
  ticket**, **Take down: TITLE** per ticket (confirm), Leave), `PostTicket(Role?)` → text
  entry (Title, Brief) → `CreateTicketAsync`. Role pick is a small dialogue: "Any role" plus
  each distinct role among employees. A claim toasts "NAME took a ticket".

## 5. Tests

Foreman: ticket created unassigned; an idle engineer claims and runs it; a reviewer does not
claim an engineer ticket; two engineers take two tickets; a ticket waits with nobody idle;
`GET /tickets`. Client: calls and DTO. Office: board in the world and as a target; claim
errand visits the board then types; feed maps claims and counts; dialogues and actions;
goldens regenerated (the board is in every frame) plus a board-dialogue golden. Smoke:
post a ticket on an empty company and see it pinned.

## 6. Out of scope

Priorities, ticket deadlines, manager-posted tickets, dragging tickets between people.
