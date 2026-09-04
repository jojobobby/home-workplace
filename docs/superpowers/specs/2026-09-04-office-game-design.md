# Office Game — Sub-project 4a of Home Workplace (Design)

Date: 2026-09-04
Status: Approved in discussion; ready for implementation planning
Builds on: the room API, Foreman, the manager loop, and the desktop shell specs in this folder.

## 1. Premise

The product goes all-in on a game engine: Home Workplace becomes a MonoGame game. This first
piece is the **office itself** — a Terraria-styled, top-down pixel office that boots the
services, draws your employees living their day from Foreman's live state, and does it with
the effects that make a game feel alive: dynamic lights with cast shadows, a day/night
cycle, particles, and synthesized sound. Management panels come next (4b); real art from the
vfx-artist employee after that (4c); mobile later, on the same simulation and renderer.

The Blazor/MAUI shell built in sub-project 3 keeps working and becomes the admin tool; it is
retired only once in-game panels exist.

## 2. Decisions carried in

| Decision | Chosen | Rejected |
|---|---|---|
| Engine | **MonoGame 3.8.5.1 (DesktopGL), 100% C#, pure code, no editor** — buildable and testable headlessly with `dotnet` | Unreal (C++/Blueprints, tens of GB, weak 2D, per-seat non-game licensing); Godot 4 C# (editor-centric workflow that cannot be driven from this harness); CSS sprites in the shell (rendering ceiling too low for the effects wanted) |
| Shape | **All-in: the game is the app.** This piece is the office; panels follow in 4b | Companion window beside the shell |
| Lighting | **In-house** render-target light maps with shadow polygons from occluders | Penumbra (not resolvable on NuGet under its id; and owning the pixel look matters) |
| Art for v1 | **Procedurally generated placeholder sprites**, deterministic, palette from the design tokens | Waiting on vfx-artist output (4c, spends the subscription — the user's click) |
| Sound | **Synthesized 8-bit SFX** generated at startup; no audio files | Downloaded sample packs |
| Live state | `AppStore` + `EventPump` **move to `libs/HomeWorkplace.Live`** (they have no Razor in them) and are shared by the game and the shell | Duplicating them |
| Resolution | Native **480×272** render target (30×17 tiles of 16 px), point-sampled upscale | Drawing at window resolution |

## 3. Goals / non-goals

**Goals:** one exe that boots the services and shows the office; employees as agents whose
behaviour follows their Foreman state and reacts to events; dynamic lights, shadows,
day/night, particles, screen shake, SFX; camera pan/zoom; a simulation that is fully
unit-tested without a GPU; golden-image tests of the rendering that a person (or I) can
look at; a documented sprite-sheet contract so real art drops in later.

**Non-goals (deferred):** in-game management panels and any editing (4b); real art and
sound assets (4c); music; multi-floor or multi-room offices; save files (the office is a
view of Foreman, which is the state); mobile builds; the Blazor shell's retirement.

## 4. Projects

```
home-workplace/
├── libs/HomeWorkplace.Live/          net8.0: AppStore, EventPump, Toast — moved from the RCL
├── libs/HomeWorkplace.UI/            now references Live (screens unchanged)
├── apps/office/HomeWorkplace.Office/ MonoGame DesktopGL game: Boot, Feed, Simulation, Renderer, Audio
│   └── Content/                      Content.mgcb + Effects/*.fx (shaders); art is procedural in v1
└── tests/HomeWorkplace.Office.Tests/ xunit: simulation, feed, lighting math, synth, golden images
```

The game references `HomeWorkplace.Client` (typed API, `ServiceSupervisor`, `AppConfig`) and
`HomeWorkplace.Live`. It never references the UI library.

## 5. Architecture and tick

```
Foreman /events ─► EventPump ─► AppStore ─► ForemanFeed (diff → commands) ─► Simulation (60 Hz)
                                                                                │
                                              Jukebox ◄── moments ──────────────┤
                                                                                ▼
                                                                    Renderer (per frame)
```

- `Boot` runs `ServiceSupervisor.StartAsync` (same `app.json` as the shell, `connectOnly`
  honoured) behind an in-game boot screen, then starts the pump.
- `ForemanFeed` watches the store and turns *differences* into simulation commands:
  employee appeared / status changed / current task changed, task settled, hand-off
  requested, human needed, run started/finished. It reads the same event types the shell's
  pump refetches on; Foreman stays the truth.
- `Simulation` is deterministic given a seed and elapsed time; it owns the world, agents,
  and a queue of **moments** (short-lived effects: bubble, particle burst, shake, sound).
- `Renderer` reads the simulation; it never mutates it. `Jukebox` consumes moments.

## 6. World

- Tile size 16 px. World 30×17 tiles: walls on the border, a floor, a **desk row** with one
  desk (32×16) per employee laid out left-to-right and wrapping to a second row past six, a
  **coffee corner** (machine + counter) top-right, a **whiteboard** on the top wall, plants
  in free corners. Layout is generated from the employee list and is stable for a given set
  of ids (sorted), so goldens are reproducible.
- Occluders (for shadows): walls and desks. Walkable: floor tiles not covered by props.
- Pathfinding: grid A* with 4-neighbour moves; agents move at 40 px/s, walking animation.

## 7. Agents and behaviour

One agent per employee, positioned at its desk. Behaviour from Foreman state:

| Foreman state / event | Behaviour | Visual |
|---|---|---|
| Awake, idle | at desk; every 20–60 s (seeded) wander to coffee and back | idle anim; walk anim; coffee steam |
| Working | at desk, typing | type anim; monitor glow on; typing sparks |
| Waiting | walk to the teammate's desk (the child task's assignee), talk | talk anim; "…" bubble |
| Asleep | absent; desk lamp off | desk only |
| `handoff.requested` | walk to the target's desk, "?" bubble, return | bubble + footsteps |
| `handoff.answered` | "!" bubble at the parent's agent | bubble + chime |
| `human.needed` | "!" bubble pulses until resolved | bubble + chime |
| `run.finished` done | particle burst at the desk | sparkle + ding |
| `run.finished` failed | small screen shake, smoke puff | shake + buzz |
| `wrapup.written` | agent stretches, then leaves (if asleep follows) | anim + door sound |

Managers are agents too; a manager run walks it between its team's desks. Name tags render
above agents; clicking an agent shows name, role, status, and current task in a small tag
(the only UI in 4a).

## 8. Renderer

- **Pipeline:** draw the scene into a 480×272 `RenderTarget2D`; blit to the back buffer with
  point sampling at the largest integer scale that fits the window, letterboxed. Camera:
  pan (drag / WASD / arrows), zoom (wheel, integer steps).
- **Layers, in order:** floor → walls → props (y-sorted with agents) → agents (y-sorted) →
  bubbles and name tags → lighting → particles → debug overlay (F3: fps, agent states).
- **Lighting:** per frame, render a light map to its own target: start from the ambient
  colour for the time of day, then for each light draw a radial-falloff sprite additively,
  **masked by that light's shadow** — for every occluder edge facing away from the light,
  project the edge's endpoints away from the light to the map edge and fill the resulting
  quad into a stencil so the light does not reach behind it. Multiply the light map over
  the scene. Lights: desk lamp (warm, on when the employee is awake), monitor (cool, on when
  working, flickers), ceiling (neutral, on during office hours), coffee machine (small
  warm). Ambient follows the clock: full during the earliest wake → latest sleep of the
  team, dusk 30 min either side, night otherwise (blue-dark, monitors only).
- **Particles:** a simple pooled emitter (position, velocity, gravity, life, colour, size);
  emitters: coffee steam, typing sparks, done sparkle, fail smoke, footstep puffs, dust
  motes drifting in light.
- **Screen shake:** a decaying random offset applied to the camera on `run.finished failed`.
- **Placeholder sprites** are generated at startup into one texture atlas from code, using
  the design palette: 16×16 characters (skin/hair/shirt colours hashed from the employee
  id; 4 idle, 4 walk, 2 type, 2 talk frames; facing flipped horizontally), desk 32×16 with
  lamp and monitor states, coffee machine, whiteboard, plant, wall and floor tiles, bubbles
  ("?", "!", "…"), radial light falloff. Generation is deterministic — goldens depend on it.
- **Sprite-sheet contract for 4c:** PNG atlas + a JSON manifest `{ name, x, y, w, h,
  frames, fps }` per animation; the placeholder generator emits the same manifest, so real
  art replaces it without renderer changes.

## 9. Audio

`SfxSynth` writes 16-bit mono 22.05 kHz WAV buffers from square, triangle, sawtooth, and
noise oscillators with attack/decay envelopes and optional pitch sweeps, loaded via
`SoundEffect.FromStream`. Sounds: footstep, keyboard click, coffee pour, chime, ding,
buzz, door, page (bubble). `Jukebox` maps moments to sounds with per-sound cooldowns and a
master volume (`app.json` → `office.volume`, default 0.6; `M` mutes). Music is deferred.

## 10. Boot, input, config

- Boot screen: the two services' progress lines, then the office fades in. Failure shows
  the error and last output, `R` retries, `Esc` quits.
- Input: WASD/arrows pan, wheel zoom, drag pan, click agent, `F3` debug, `M` mute, `Esc`.
- `app.json` gains an `office` section: `{ "volume": 0.6, "scale": 0, "showDebug": false }`
  (`scale` 0 = largest integer fit).

## 11. Testing

- **Simulation** (no GPU): layout generation is stable and collision-free; A* finds paths
  and respects occluders; the state machine maps each Foreman state/event to the behaviour
  in §7; wander timing is seeded; moments are queued and expire.
- **Feed:** store diffs produce exactly the expected commands; unknown ids are ignored.
- **Lighting math:** shadow quads from an edge and a light are the expected polygons; the
  ambient schedule gives day/dusk/night at the expected times.
- **Synth:** WAV headers valid; lengths match durations; deterministic bytes.
- **Golden images:** a hidden-window `Game` renders fixed scenes (a seeded team at 10:00
  working, at 20:30 night, a hand-off moment) to PNG and compares to committed goldens with
  a small per-pixel tolerance. New goldens are reviewed by eye — the PNGs are readable
  here — before they are committed.
- **Smoke:** launch the exe on this machine, save frames to disk, confirm boot, agents,
  lighting, and that sounds are produced.

## 12. Acceptance

Launch the exe → services boot → the office renders the four starter employees at their
desks → wake one → it walks to the coffee machine with footsteps and steam → set the clock
past the latest shift end → lights go down, monitors glow → frames saved to disk look right.
Creating a task and watching an employee type is the user's click.

## 13. Risks

- **Shader toolchain:** custom HLSL needs MonoGame's content builder to compile on this
  machine; proven by the plan's first task before anything depends on it. Fallback: the
  lighting pass done with blend states and pre-rendered falloff textures, no custom shader.
- Golden tests need a GL context; fine here, not on a headless CI.
- Placeholder art is deliberately plain; the look is judged on lighting and motion until 4c.
- The Blazor shell and the game both boot services; only one should run at a time — the
  supervisor's health check makes a second boot connect instead of relaunching.
