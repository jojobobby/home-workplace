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
| Esc | back out of a dialogue or menu; quit when nothing is open |

Dev flags for unattended runs: `--clock HH:mm` fixes the office clock (see night lighting
at noon), `--frames-every N` saves a frame every N seconds, `--exit-after N` quits, and
`--smoke-script "walk ada-coder;talk;pick 0;type Hello;enter;esc;tab;wait 2"` drives the UI.

## Managing the company

Walk up to an employee (or click them) and a dialogue box opens at the bottom: they say what
they are on, then you pick from the options — give a task, approve or answer a task that is
waiting on you, wake or sleep them, open their task room's brief, reset their day. Managers
add set a goal (with a dollar budget) and top up. The whiteboard on the top wall lists goals
and offers top up / cancel. Bubbles over an employee mean they need you; toasts top-right
say who, and clicking one opens their dialogue. Tab opens the office menu with lists of
employees, tasks (approve / answer / reassign / retry / cancel), goals, activity (Foreman
events), and setup (the CLI checks and a reload). Destructive actions ask first.

## Config

`app.json` gains an `office` section:

```json
{ "office": { "volume": 0.6, "scale": 0, "showDebug": false } }
```

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
