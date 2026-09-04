# Office management UI (sub-project 4b) — design

Sub-project 4b of Home Workplace. The office (4a) shows the company; 4b lets you run it from
inside the game, so the Blazor shell is no longer needed for daily use.

## 1. Goal

Everything the shell does, done in the office: give tasks, answer questions, approve work,
wake and sleep employees, set goals with budgets and top them up, see activity, check setup.
Two ways to reach an employee: **walk your own character up to them**, or **click them**.

## 2. Decisions

| Decision | Choice |
|----------|--------|
| Reaching an employee | Walk (WASD, `E` to talk when adjacent) **or** click from anywhere |
| Talking | RPG dialogue box at the bottom: the employee speaks, you pick options |
| Lists and history | `Tab` overlay with tabs Employees · Tasks · Goals · Activity · Setup |
| Goals in the world | The whiteboard: walk to it or click it |
| Approvals and questions | The agent's `?`/`!` bubble is clickable; toasts top-right link to the dialogue |
| UI toolkit | Hand-rolled pixel widgets on the existing `Hud` and `PixelFont`; no Myra |
| Camera | Follows the player; wheel zoom and drag still work, WASD recentres |
| Blazor shell | Kept in the repo, no longer the primary app; nothing removed |

Hand-rolled over Myra: one font, one palette, one input path, and a pure model that tests
drive without a GPU. Myra would bring its own skinning and font pipeline for a dozen widgets.

## 3. Player

`Sim/Player.cs`. A sprite (generated like the employees, fixed id `you`), spawned at the
door. Moves at 60 px/s with tile collision against `TileMap.IsWalkable`, sliding along
walls (X then Y). Facing follows movement. Walk animation and footstep moments reuse the
agent path. `Player.Near(agent)` is true within 1.5 tiles; the nearest such agent is the
**talk target**, shown with a small `E` prompt above it. The whiteboard and coffee machine
are targets too (`Interactable` = agent | whiteboard).

Camera: `Camera.Follow(player.Position)` each frame unless the user dragged or zoomed in
the last 2 s (then WASD movement resumes following).

## 4. UI model (pure, testable)

`Ui/` namespace, no engine types:

- `UiState` — a stack of **layers**: `None` → `Dialogue` | `Overlay` | `TextEntry` |
  `Confirm`. Input goes to the top layer only; `Esc` pops.
- `Dialogue` — `Speaker`, `Lines` (typewriter: `Revealed` chars advance at 40/s, any key
  completes), `Options`, `Selected`. `DialogueScript.For(employee, tasks, goals)` builds
  lines and options from state:
  - greeting line: status, current task title, energy, runs today;
  - a task waiting on a human adds the question text and **Approve** / **Answer** / **Cancel**;
  - always: **Give a task**, **Wake** or **Sleep**, **Open room brief**, **Reset**, **Leave**;
  - managers add **Set a goal** and, per active goal, **Top up**;
  - a failed last run adds **Retry**.
- `Overlay` — `Tab` (Employees, Tasks, Goals, Activity, Setup), `Rows`, `Selected`,
  per-row actions (Tasks: approve/answer/reassign/retry/cancel; Goals: top up/cancel;
  Employees: wake/sleep/reset/talk; Setup: reload employees, CLI check results). Tab cycles
  tabs, arrows move, Enter opens the row's actions as a small `Dialogue` (reusing it).
- `TextEntry` — fields (`Title`, `Brief`, or `Answer`, or `Budget`), cursor, insert/delete,
  word wrap for display, `Enter` on the last field submits, `Esc` cancels. Text comes from
  MonoGame's `Window.TextInput`; no clipboard in v1.
- `Confirm` — a yes/no for destructive actions (cancel, reset).
- `Toasts` — from `AppStore.Toasts` plus `HumanNeeded` events; a toast names the employee
  and, clicked, opens their dialogue.

## 5. Actions

`Ui/Actions.cs`: every option maps to one `IForemanApi`/`IContextApi` call, run on a
background task. The layer shows a "…" pending state and refuses double submit. Success
pops the layer; the store's pump refetches the truth. `ApiException` becomes a toast with
the message; nothing is retried automatically. Actions are recorded in a `Journal` (last
50, shown in Activity next to Foreman events).

## 6. Rendering

`Render/UiRenderer.cs` draws the model with `Hud`: a 9-slice pixel panel (`SpriteGenerator`
adds `panel` and `panel_dark`), the speaker's sprite at 2× on the left of the dialogue,
options with a `>` cursor, text fields with a blinking caret, overlay as a full-width
panel with a tab strip, toasts as small panels sliding in. Palette: the design tokens
already in `SpriteGenerator`. Sounds: `page` on dialogue open, `keys` per typed character,
`ding` on submit, `buzz` on error (existing synth sounds).

## 7. Input

`InputMap` grows: WASD → player move (arrows still pan); `E` interact; `Tab` overlay;
`Enter`/`Esc`; arrows in menus; mouse click on agent/whiteboard/bubble/toast/option. A
click on the world while a layer is open is ignored except on that layer's widgets.

## 8. Testing

- Player: moves, collides, slides, finds the nearest talk target, prompt appears in range.
- DialogueScript: options per employee state (asleep, working, waiting on human, manager,
  failed run) are exactly the expected set and order.
- TextEntry: insert, delete, wrap, submit, cancel.
- Overlay: tab cycling, row selection, per-tab actions.
- Actions: each option calls the right fake API method with the right arguments; an
  `ApiException` becomes a toast and leaves the layer open; double submit is refused.
- Goldens: dialogue open on Rex (waiting on human), overlay Tasks tab, whiteboard goals,
  a toast.
- Smoke: launch, walk to Ada, give a task, watch her type; Tab through the overlay;
  frames saved.

## 9. Out of scope

Clipboard paste, mouse-wheel scrolling in lists (arrows/PageUp/Down only), gamepad,
removing the Blazor shell, art (4c), notifications beyond toasts (sub-project 5).
