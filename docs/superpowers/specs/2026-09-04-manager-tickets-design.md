# Manager tickets (sub-project 4f) — design

Pin a big ticket for a manager. The manager takes it, cuts it into tasks for the team (by
name, or as sub-tickets on the board for a role), follows them as they settle, and the
ticket closes when the work is done.

## 1. Decisions

| Decision | Choice |
|----------|--------|
| What happens when a manager claims a ticket | the ticket becomes a **goal** run by that manager: same title and brief, the ticket's budget (default `Foreman:DefaultTicketBudgetUsd`, $5); the ticket is marked Running under the manager and linked (`GoalId` on the ticket, `TicketId` on the goal) |
| How the manager hands out work | two decision actions: `create_task` (a named employee, as today) and the new `post_ticket` (title, brief, role) which pins a sub-ticket on the board for that role; both are goal children, so costs, settles and the budget work as for any goal |
| When the ticket closes | the goal's terminal state closes it: Done → ticket Done, Failed → Failed, Cancelled → Cancelled, with a room note |
| Who is a manager | an employee whose role contains "manager" |
| Posting for a manager | the board's role pick includes manager roles present; a manager ticket takes an optional budget (Title, Brief, Budget USD) |
| Office | a manager claim runs the same board errand; a `goal.decision` event toasts "NAME planned N tasks" |

## 2. Foreman

- `TaskModel.BudgetUsd` (decimal?, tickets only), `GoalModel.TicketId` (string?).
- `CreateTicketRequest.BudgetUsd` (decimal?).
- `RunSupervisor.Pump`: a ticket whose taker is a manager is not run; `GoalBook.CreateFromTicketAsync(ticket, manager)` creates the goal, `TaskBook.HandToGoal(ticketId, managerId, goalId)` marks the ticket Running under the manager, then `RunManagerAsync(goalId)`.
- `ManagerActions`: schema and executor gain `post_ticket` (`role`); the posted ticket carries `GoalId` and joins `goal.TaskIds`. `ManagerComposer` explains both actions and lists the roles on the team.
- `GoalBook`: on Done/Failed (executor) and Cancel, `TaskBook.CloseTicketOfGoal(goal)` settles the linked ticket.

## 3. Client

`TaskDto.BudgetUsd`; `CreateTicketRequest(Title, Brief, Role, BudgetUsd)`.

## 4. Office

- `PostTicket(role)`: when the role is a manager role the text entry has a third field,
  Budget USD (blank = default). The board dialogue notes a manager ticket's budget.
- `goal.decision` events toast "NAME planned N tasks" (count of create_task + post_ticket).

## 5. Tests

Foreman: a manager claim creates a linked goal and runs the manager; a `post_ticket`
decision pins a sub-ticket with the goal id that an idle engineer then claims; `create_task`
still works; completing the goal closes the ticket; cancelling the goal cancels the ticket;
the prompt names `post_ticket` and the team's roles. Client: request round trip. Office:
the manager ticket entry has a budget field and posts it; the decision toast.

## 6. Out of scope

Managers re-planning on a schedule, several managers on one ticket, priorities.
