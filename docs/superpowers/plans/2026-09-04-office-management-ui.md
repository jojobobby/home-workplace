# Office Management UI (4b) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the company from inside the office: walk or click to an employee, talk through an RPG dialogue box, manage lists in a Tab overlay, set goals at the whiteboard.

**Architecture:** A player character in the simulation (pure C#), a pure `Ui/` model (layer stack: dialogue, overlay, text entry, confirm; toasts) that tests drive without a GPU, an `Actions` dispatcher over `IForemanApi`/`IContextApi`, and a `UiRenderer` that draws the model with `Hud`. `OfficeGame` routes input to the top layer.

**Tech Stack:** .NET 8, MonoGame DesktopGL 3.8.5.1, xunit, existing `PixelFont`/`Hud`/`SpriteGenerator`, golden-image tests via `GoldenHost`.

**Spec:** `docs/superpowers/specs/2026-09-04-office-management-ui-design.md`

## Global Constraints

- No engine types in `Sim/` or `Ui/` (System.Numerics only); every Ui behaviour has a GPU-free test.
- Hand-rolled widgets on `Hud` and `PixelFont`; no Myra, no new packages.
- All service calls go through `IForemanApi` / `IContextApi` fakes in tests; nothing spends the subscription in the suite.
- Goldens: a new PNG fails once and must be viewed before it is committed.
- Commit after every task with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`; full `dotnet test HomeWorkplace.sln` green before each push.

---

### Task 1: Player character, camera follow, talk target
**Files:** create `Simulation/Player.cs`; modify `Render/Camera.cs` (Follow), `Render/SpriteGenerator.cs` (a `you` character, an `e_prompt` sprite), `Render/SceneRenderer.cs` (draw player y-sorted with agents, draw prompt over the talk target), `OfficeGame.cs` (WASD → player, E, camera follow with a 2 s drag/zoom hold); tests `PlayerTests.cs`.
**Interfaces:** `Player(World world)` with `Position`, `FacingLeft`, `Anim`, `AnimTime`, `Tile`; `Move(Vector2 dir, float dt)` slides along walls; `Interactable? Target(Simulation sim)` → `Interactable(InteractKind Kind, string? EmployeeId)`, `InteractKind { Employee, Whiteboard }`; `Player.Speed = 60`, `Player.Reach = 1.5 * TileSize`. `Camera.Follow(Vector2 world)`.
- [ ] Tests: spawns at the door; moves at Speed·dt; a wall stops X but the Y component still slides; the nearest visible agent within Reach is the target, none beyond; the whiteboard is a target when adjacent; facing follows movement. RED → implement → GREEN → commit.

### Task 2: UI core — layer stack, text entry, confirm, toasts
**Files:** create `Ui/UiState.cs`, `Ui/TextEntry.cs`, `Ui/Confirm.cs`, `Ui/Toasts.cs`, `Ui/UiInput.cs`; tests `UiCoreTests.cs`.
**Interfaces:** `UiInput` = `Up|Down|Left|Right|Accept|Back|Tab|Char(c)|Backspace|Delete` (record `UiKey(UiKeyKind Kind, char Char)`); `interface ILayer { LayerResult Handle(UiKey key); }`, `LayerResult { None, Pop, Push(ILayer), Submit(object payload) }`; `UiState.Push/Pop/Top/IsOpen/Handle(UiKey)`; `TextEntry(fields: IReadOnlyList<Field>)` with `Field(Name, Multiline, MaxLength)`, `Values`, `Cursor`, `Current` index, `Insert(char)`, `Backspace`, `NextField`, `Enter on last field → Submit(values)`, `Esc → Pop`; `TextEntry.Wrap(text, columns)` → lines; `Confirm(question, onYes payload)`; `Toasts.Add(text, kind, employeeId?)`, `Update(dt)` expires after 6 s, `Live`.
- [ ] Tests: push/pop/top; keys go to top only; text insert/backspace/delete/cursor/wrap at word boundaries/max length; Enter on the last field submits all values; Esc pops; Confirm yes submits payload, no pops; toasts expire and cap at 5. RED → implement → GREEN → commit.

### Task 3: Dialogue and DialogueScript
**Files:** create `Ui/Dialogue.cs`, `Ui/DialogueScript.cs`; tests `DialogueTests.cs`.
**Interfaces:** `Dialogue(speakerId, speakerName, IReadOnlyList<string> lines, IReadOnlyList<DialogueOption> options)` implementing `ILayer`; `DialogueOption(string Label, UiAction Action)`; `Revealed` chars advance in `Update(dt)` at 40 chars/s, any key completes the reveal first; Up/Down move `Selected`, Accept → `Submit(option.Action)`, Back → Pop. `UiAction` records: `GiveTask(EmployeeId)`, `Wake(EmployeeId)`, `Sleep(EmployeeId)`, `Reset(EmployeeId)`, `OpenBrief(EmployeeId)`, `Approve(TaskId)`, `Answer(TaskId)`, `CancelTask(TaskId)`, `Retry(TaskId)`, `Reassign(TaskId, Assignee)`, `SetGoal(ManagerId)`, `TopUp(GoalId)`, `CancelGoal(GoalId)`, `Leave`, `TalkTo(EmployeeId)`, `ReloadEmployees`. `DialogueScript.For(EmployeeDto e, IReadOnlyDictionary<string,TaskDto> tasks, IReadOnlyDictionary<string,GoalDto> goals)` → `Dialogue`; `DialogueScript.Whiteboard(goals)` → `Dialogue` listing goals with TopUp/CancelGoal per active goal and SetGoal for the first manager.
- [ ] Tests: asleep employee → lines mention asleep, options exactly [Give a task, Wake, Open room brief, Reset, Leave]; working → [Give a task, Sleep, Open room brief, Reset, Leave] and the task title in a line; waiting-on-human task → question line and options start [Approve, Answer, Cancel task]; failed last run adds Retry; manager (role contains "manager") adds Set a goal and Top up per active goal; typewriter reveal and key-completes; whiteboard dialogue. RED → implement → GREEN → commit.

### Task 4: Overlay
**Files:** create `Ui/Overlay.cs`; tests `OverlayTests.cs`.
**Interfaces:** `Overlay(OverlayTab tab, AppStore-like snapshot: employees, tasks, goals, events, setup lines)` implementing `ILayer`; `OverlayTab { Employees, Tasks, Goals, Activity, Setup }`; `Rows` (`OverlayRow(string Id, string Text, IReadOnlyList<DialogueOption> Actions)`), `Selected`, Tab → next tab, Up/Down, Accept → `Push(new Dialogue(...row.Actions))`, Back → Pop; `Refresh(snapshot)` keeps the selection by id. Row text formats: employee `NAME  status  task-title`; task `title  state  assignee`; goal `title  state  spent/budget`; activity `HH:mm  type  text`; setup one line per CLI check plus a `Reload employees` row.
- [ ] Tests: tab cycling wraps; rows per tab from a snapshot; Enter opens a dialogue with that row's actions; refresh keeps the selected id; Setup tab has the reload row. RED → implement → GREEN → commit.

### Task 5: Actions and Journal
**Files:** create `Ui/Actions.cs`, `Ui/Journal.cs`; tests `ActionsTests.cs` with `FakeForemanApi` (copy the shape from `tests/HomeWorkplace.UI.Tests`) and `FakeContextApi`.
**Interfaces:** `Actions(IForemanApi foreman, IContextApi context, Journal journal, Toasts toasts)`; `Task<ActionOutcome> RunAsync(UiAction action, IReadOnlyDictionary<string,string>? fields)`; `ActionOutcome { Done, OpenText(TextEntry), OpenDialogue(Dialogue), Failed(message) }`; `GiveTask` → `OpenText(title, brief)` then on submit `CreateTaskAsync(new CreateTaskRequest(title, brief, employeeId))`; `Answer` → `OpenText(answer)` then `AnswerAsync`; `SetGoal` → `OpenText(title, brief, budget)` then `CreateGoalAsync`; `TopUp` → `OpenText(amount)` then `TopUpAsync`; `OpenBrief` → `GetBriefAsync(roomOf(employee))` → `OpenDialogue` showing the brief lines; the rest call their method directly. `ApiException` → `Failed(message)` + toast; a second call while one is in flight → `Failed("busy")`. `Journal.Entries` (last 50, `JournalEntry(DateTimeOffset At, string Text)`).
- [ ] Tests: each action calls the right fake method with the right arguments; text-backed actions return OpenText with the right fields and then submit; an ApiException becomes Failed and a toast, and the journal records both success and failure; busy guard. RED → implement → GREEN → commit.

### Task 6: UiRenderer and goldens
**Files:** create `Render/UiRenderer.cs`; modify `Render/SpriteGenerator.cs` (`panel` 9-slice 12×12 and `panel_dark`); tests `UiGoldenTests.cs`, `GoldenHost.cs` (an overload that renders a `UiState` over the scene).
**Interfaces:** `UiRenderer(Hud hud, SceneRenderer scene)`; `Draw(UiState ui, Toasts toasts, Simulation sim, int scale, float time)`: dialogue = bottom panel 8 px inset, speaker sprite at 2× left, lines wrapped at the panel width, options with `>`; text entry = centred panel with field labels, wrapped values, blinking caret (time-based, 0.5 s); confirm = small centred panel; overlay = full panel with a tab strip (selected tab bright) and rows (selected row inverted); toasts stack top-right. `Hud.Panel(x,y,w,h, dark)` draws the 9-slice from the atlas (Hud gains an atlas texture parameter).
- [ ] Goldens: `ui-dialogue` (Rex waiting on a human, reveal complete), `ui-overlay-tasks`, `ui-textentry` (caret visible at time 0), `ui-toast`. Create → view → accept → commit.

### Task 7: Wire into OfficeGame, README, smoke
**Files:** modify `OfficeGame.cs` (player, UiState, Actions, Toasts, Journal, input routing, `Window.TextInput`, click targets: agents/whiteboard/bubbles/toasts/options, camera follow, sounds page/keys/ding/buzz), `InputMap.cs` (`UiKeyFor(Keys)`), `apps/office/README.md` (controls, management), root README (4b built).
- [ ] Input routing: with a layer open, keys become `UiKey`s for `UiState.Handle`; `Submit` payloads go to `Actions.RunAsync`; outcomes push text/dialogue layers or pop; no layer → movement, E on `player.Target`, Tab opens the overlay, click on an agent/whiteboard opens its dialogue, click on a toast opens that employee's dialogue.
- [ ] Smoke on this machine with `--frames-every 5 --exit-after 60`: boot, walk to Ada (scripted via a `--smoke-script` dev flag: `walk ada-coder; talk; pick 0; type "Say hi"; enter; type "Reply with the word hi"; enter`), frames show the dialogue and the text entry; view frames. Full solution green → commit → push.

## Self-Review
Spec §3 → Task 1; §4 → Tasks 2–4; §5 → Task 5; §6 → Task 6; §7 → Task 7; §8 tests distributed; §9 out of scope untouched. Names used consistently: `Player`, `Interactable`, `InteractKind`, `UiState`, `ILayer`, `LayerResult`, `UiKey`, `TextEntry`, `Field`, `Confirm`, `Toasts`, `Dialogue`, `DialogueOption`, `DialogueScript`, `UiAction`, `Overlay`, `OverlayTab`, `OverlayRow`, `Actions`, `ActionOutcome`, `Journal`, `UiRenderer`.
