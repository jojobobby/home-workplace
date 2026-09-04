# Office Game Implementation Plan (sub-project 4a)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A MonoGame office that boots the services and animates employees from Foreman's live state, with dynamic lights and shadows, day/night, particles, and synthesized SFX.

**Architecture:** `HomeWorkplace.Live` (store + pump, extracted from the RCL) → `ForemanFeed` diff → deterministic `Simulation` → `Renderer` (480×272 render target, layered, light map) + `Jukebox`. Placeholder sprites generated in code; a manifest contract for real art later.

**Tech Stack:** MonoGame.Framework.DesktopGL 3.8.5.1, MonoGame.Content.Builder.Task 3.8.5.1, xunit; MonoGame.Extended only if a concrete need appears.

**Spec:** `docs/superpowers/specs/2026-09-04-office-game-design.md`

## Global Constraints

- New: `libs/HomeWorkplace.Live`, `apps/office/HomeWorkplace.Office`, `tests/HomeWorkplace.Office.Tests`; all in `HomeWorkplace.sln`. Game and tests target `net8.0`.
- The game references Client + Live only. `AppStore`, `EventPump`, `Toast` move to Live; the RCL and UI tests keep passing unchanged except for the project reference.
- Simulation and feed code has **no** MonoGame types in it (pure C#, testable without a GPU). Rendering reads simulation state; it never writes it.
- Native resolution 480×272, tile 16 px, point sampling, integer upscale. Palette = the design tokens in `pixel.css`.
- Sprite generation and world layout are deterministic; golden images are committed only after being viewed.
- No audio or image files are downloaded. All v1 art and sound is generated in code.
- `dotnet test HomeWorkplace.sln` green after every task (182 + new); commit per task.

---

### Task 1: Toolchain proof, `Live` extraction, scaffold
- [ ] Install templates: `dotnet new install MonoGame.Templates.CSharp::3.8.5.1`. Scaffold `dotnet new mgdesktopgl -n HomeWorkplace.Office -o apps/office/HomeWorkplace.Office`; `dotnet new xunit -n HomeWorkplace.Office.Tests -o tests/HomeWorkplace.Office.Tests`; `dotnet new classlib -n HomeWorkplace.Live -o libs/HomeWorkplace.Live`. Add all to the sln.
- [ ] Move `AppStore.cs`, `EventPump.cs`, `Toast.cs` from `libs/HomeWorkplace.UI` to `libs/HomeWorkplace.Live` (namespace `HomeWorkplace.Live`; Live references Client). RCL references Live; add `@using HomeWorkplace.Live` to its `_Imports.razor`; UI tests add `using HomeWorkplace.Live` where needed. `dotnet test HomeWorkplace.sln` → 182 green.
- [ ] Shader proof: add `Content/Effects/Light.fx` (a sprite pixel shader) to `Content.mgcb`; `dotnet build apps/office/HomeWorkplace.Office` must produce `Light.xnb`. If the content builder cannot compile it here, record that in the spec's risk and switch Task 5 to the blend-state fallback.
- [ ] Game references Client + Live; tests reference the game. Commit `chore(office): scaffold MonoGame office, extract Live library, prove shader toolchain`.

### Task 2: World and pathfinding (pure C#)
- [ ] Tests: `WorldLayout.Generate(ids)` places one desk per employee, a coffee corner, a whiteboard, plants, and walls with no overlaps and is identical for the same sorted ids; `TileMap.IsWalkable`; `AStar.FindPath` finds a path around a desk and returns null when blocked.
- [ ] RED → implement `Tile`, `TileMap`, `Prop`, `WorldLayout`, `AStar` → GREEN → commit.

### Task 3: Agents, state machine, moments, feed
- [ ] Tests: agent behaviour per spec §7 for each status (positions/targets/animation names after N ticks with a seeded RNG); events queue the right moments (bubble/particle/shake/sound kinds) with expiry; `ForemanFeed.Diff(previous, current, events)` yields exactly the expected commands; unknown ids ignored.
- [ ] RED → implement `Agent`, `Behaviour`, `Moment`, `Simulation` (60 Hz `Update(dt)`), `ForemanFeed` → GREEN → commit.

### Task 4: Renderer, procedural atlas, camera, golden harness
- [ ] Tests: `SpriteGenerator` emits a deterministic atlas + manifest (same bytes for same ids); manifest schema matches spec §8; golden harness renders a fixed scene (seeded team, 10:00) to PNG — **view the PNG**, then commit it as the golden; a second run matches within tolerance.
- [ ] RED → implement `SpriteGenerator`, `Atlas`/`Manifest`, `Camera`, `SceneRenderer` (render target, layers, y-sort, name tags, integer upscale), `GoldenHarness` (hidden-window game) → GREEN → commit.

### Task 5: Lighting, shadows, day/night
- [ ] Tests: `Shadows.QuadFor(edge, light)` math; `Ambient.For(time, schedule)` day/dusk/night; goldens: office at 10:00 lit, at 20:30 night with monitors, one lamp with a desk shadow — viewed before committing.
- [ ] RED → implement `LightMap` (render target, additive lights masked by shadow stencil, multiply blend; `Light.fx` or the blend-state fallback per Task 1), `Ambient` schedule from the team's shifts → GREEN → commit.

### Task 6: Particles and screen shake
- [ ] Tests: emitter pooling/lifetimes; shake decays to zero; moments spawn the right emitters; golden of a coffee-steam + done-sparkle frame.
- [ ] RED → implement `ParticleSystem`, emitters, `ScreenShake`, wiring from moments → GREEN → commit.

### Task 7: Audio synth and jukebox
- [ ] Tests: `SfxSynth` WAV header/length/determinism per sound; `Jukebox` maps moments to sounds and honours cooldowns and mute.
- [ ] RED → implement `SfxSynth`, `Jukebox`, `SoundEffect` loading, volume from `app.json` → GREEN → commit.

### Task 8: Boot, input, config, smoke, docs
- [ ] `OfficeGame` composition: `Boot` (ServiceSupervisor + boot screen), pump, feed, sim, renderer, jukebox, input (pan/zoom/drag/click/F3/M/Esc), `office` config section, frame-to-PNG hotkey (F12) for the smoke.
- [ ] Smoke on this machine: launch, save frames during boot / day / night / a wake → walk-to-coffee; view them. Docs: `apps/office/README.md`, root README roadmap (4a built), sprite-sheet contract for 4c. Full solution test green → commit → push.

## Self-Review
Spec §4 → Task 1; §6 → 2; §7 → 3; §8 → 4 (atlas, camera, layers) and 5 (lighting) and 6 (particles/shake); §9 → 7; §10 → 8; §11 → distributed; §12 → 8. Names used consistently: `Simulation`, `ForemanFeed`, `WorldLayout`, `TileMap`, `AStar`, `Agent`, `Moment`, `SpriteGenerator`, `SceneRenderer`, `LightMap`, `Ambient`, `ParticleSystem`, `ScreenShake`, `SfxSynth`, `Jukebox`, `GoldenHarness`, `OfficeGame`.
