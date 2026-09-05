# Home Workplace — the office

A MonoGame (DesktopGL) top-down pixel-art office drawn live from Foreman's event stream.
Employees walk in, sit at their desks, type, fetch coffee, talk to teammates during
hand-offs, and go home when their shift ends. Lights go down at night, monitors glow,
particles drift, and every moment has a synthesized sound.

## Run

```bash
dotnet run --project apps/office/HomeWorkplace.Office
```

The game boots the context API and Foreman itself (same `app.json` as the desktop shell,
`connectOnly` honoured) behind a boot screen, then the office fades in.

| Key | Action |
|-----|--------|
| WASD | walk your character |
| E | talk to the employee (or whiteboard) you are next to |
| Tab | the office menu: Employees, Tasks, Goals, Activity, Setup |
| left click | talk to an employee, open the whiteboard, or open a toast |
| arrows | pan the camera (it follows you again when you walk) |
| mouse wheel | zoom (1× to 4×, pixel-perfect) |
| left drag | pan |
| F3 | debug overlay (fps, clock, phase, agents, particles, services) |
| M | mute |
| F12 | save the current frame to `frames/` beside the exe |
| R | retry a failed boot |
| Esc | back out of a dialogue or menu (close the window to quit) |

Dev flags for unattended runs: `--clock HH:mm` fixes the office clock (see night lighting
at noon), `--frames-every N` saves a frame every N seconds, `--exit-after N` quits, and
`--smoke-script "walk ada-coder;talk;pick 0;type Hello;enter;esc;tab;wait 2"` drives the UI.

## Hiring

The company starts with nobody. The stand by the door (walk up and press E, or click it)
lists the roles under `hiring/`; pick one, then a brain, then type a name. Brains are the
models Foreman knows (Claude Haiku 4.5, Sonnet 5, Opus 4.8, Opus 5, Fable 5.1; GPT-5
Codex), each with an approximate price per day at API list prices, which is notional on a
subscription. A brain whose CLI is not signed in (see the Setup tab) shows "(sign in)" and
cannot be picked. The hire walks in at once. "Let go" in an employee's dialogue archives
their folder under `employees/.former/`.

## Tickets

Hire as many engineers as you like, then stop assigning by hand: the ticket board on the
top wall (right of the whiteboard; walk up and press E, or click it) lists what is pinned.
"Post a ticket" asks who should take it (any role, or one of the roles in the company), then
a title and brief. The oldest ticket goes to the first idle, awake employee of that role: they
walk to the board, take it, and go type. "Take down" cancels a ticket. The Tasks tab shows
tickets as `(board)` until someone claims them.

Pin a ticket for a **manager** role (it takes an optional budget) and the manager takes it as a
goal: their plan gives tasks to named teammates or pins sub-tickets on the board for a role,
the goal tracks them as they settle, and the original ticket closes when the goal does. The
whiteboard shows the goal; a toast says how many tasks the manager planned.

## Managing the company

Walk up to an employee (or click them) and a dialogue box opens at the bottom: they say what
they are on, then you pick from the options — give a task, approve or answer a task that is
waiting on you, wake or sleep them, open their task room's brief, reset their day. Managers
add set a goal (with a dollar budget) and top up. The whiteboard on the top wall lists goals
and offers top up / cancel. Bubbles over an employee mean they need you; toasts top-right
say who, and clicking one opens their dialogue. Tab opens the office menu with lists of
employees, tasks (approve / answer / reassign / retry / cancel), goals, activity (Foreman
events), and setup (the CLI checks and a reload). Destructive actions ask first.

## Where the company lives

Everything about your company is a folder: `DocumentsHome Workplace<office name>`
with `employees` (one folder per hire), `hiring` (the role templates, seeded from the repo
the first time and yours to edit), and `data` (Foreman's tasks, goals, events, employee
state, and `workspaces`, one folder per task, which is where the agents do their work). The
first launch after this change copies an existing `employees` and Foreman `data` from the
repo into it, once; the repo copies are left alone.

Your own desk sits bottom-right. Walk up (E) or click it and the computer opens the office
folder, the workspaces, or the employees in Explorer.

## Config

`app.json` gains an `office` section:

```json
{ "office": { "name": "Main Office", "volume": 0.6, "scale": 0, "showDebug": false } }
```

`name` picks the folder under `DocumentsHome Workplace` and the window title.

`scale` 0 means the largest integer scale that fits the window.

## How it fits together

```
Foreman /events ─► EventPump ─► AppStore ─► ForemanFeed (diff → commands) ─► Simulation (60 Hz)
                                                                                │
                                              Jukebox ◄── moments ──────────────┤
                                                                                ▼
                                                                    SceneRenderer (per frame)
```

- `Simulation/` — pure C#, no engine types, deterministic for a seed: `WorldLayout`,
  `TileMap`, `AStar`, `Agent`, `Simulation`, `Moment`, `ForemanFeed`, `HitTest`.
- `Render/` — `SpriteGenerator` (procedural placeholder art), `Atlas`, `PixelFont`, `Camera`,
  `SceneRenderer`, `LightMap` (stencil shadows + additive lights, multiplied over the scene),
  `Lighting` (ambient schedule from shifts), `Particles`, `ScreenShake`, `Hud`, `Shifts`.
- `Audio/` — `SfxSynth` (deterministic 16-bit WAV synth), `Jukebox` (moments → sounds with
  cooldowns, volume, mute, pan), `MonoGameSoundPlayer`.
- `OfficeGame` composes everything; `InputMap` holds the pure input arithmetic.

## Tests

```bash
dotnet test tests/HomeWorkplace.Office.Tests
```

Simulation, feed, lighting math, particles, synth, jukebox and input are plain unit tests.
Golden-image tests render real frames on the GPU and compare them to `goldens/*.png`; a new
scene writes its PNG and fails once so the image is looked at before it becomes the standard.

## Sprite-sheet contract (for sub-project 4c)

Art replaces `SpriteGenerator` output without touching the renderer as long as it follows
the manifest the generator produces (`Render/Atlas.cs`):

- One RGBA atlas plus a `Manifest` of named `SpriteRect`s and `Animation`s (frames + fps).
- Tiles are 16×16: `floor`, `floor2`, `wall`; props `desk`, `desk_lamp`, `desk_monitor`,
  `desk_lamp_monitor`, `coffee`, `whiteboard`, `plant`.
- Characters: per employee id, animations `idle`, `walk`, `type`, `talk`, drawn facing right
  (the renderer flips for left).
- Bubbles `bubble_question`, `bubble_exclaim`, `bubble_dots`; a radial `light` sprite; a
  1×1 white `pixel`.

## When a run fails

Open the employee (or the whiteboard) and the dialogue says why; the task room and the
Activity tab carry the same line. Two causes seen on a real machine:

- **"Your organization has disabled Claude subscription access for Claude Code · Use an
  Anthropic API key instead, or ask your admin to enable access" (HTTP 403).** Anthropic
  refuses headless `claude -p` on this account's subscription; the interactive Claude Code
  app still works. Nothing in Home Workplace can change that policy. Your options: set
  `ANTHROPIC_API_KEY` (pay-per-use; the scrub passes `ANTHROPIC_API_KEY`,
  `ANTHROPIC_AUTH_TOKEN` and `ANTHROPIC_BASE_URL` through to Foreman and the CLI), ask your
  Anthropic admin to enable subscription access for Claude Code, or give the work to Codex
  employees (sign in with `codex login`; the Setup tab shows whether that worked).
- **A goal that stays in Planning.** Its manager is asleep (shift over, or a fresh boot
  before the first scheduler tick). The goal room says so, and the whiteboard offers to
  wake them. A manager run that fails at the API is written on the goal, toasted, and not
  retried until something changes (a top-up, an approval, a wake) or ten minutes pass.
