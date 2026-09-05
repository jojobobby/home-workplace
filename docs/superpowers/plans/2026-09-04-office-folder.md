# Office Folder and Boss Desk (4g) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The company lives in `Documents\Home Workplace\<office>\`; a boss desk in the office opens it.

**Architecture:** A pure `OfficePaths` helper prepares the folder and yields Foreman's environment; the supervisor passes it through; the office gets a boss desk prop whose dialogue calls an open-folder delegate the game binds to Explorer.

**Spec:** `docs/superpowers/specs/2026-09-04-office-folder-design.md`

---

### Task 1: OfficePaths, supervisor environment, game wiring
**Files:** create `libs/HomeWorkplace.Client/OfficePaths.cs`; modify `AppConfig.cs` (Office.Name, ServiceEnvironment), `ServiceSupervisor.cs` (merge env), `apps/office/HomeWorkplace.Office/Program.cs`, `OfficeGame.cs` (title); tests `tests/HomeWorkplace.UI.Tests/OfficePathsTests.cs`, `SupervisorTests.cs`.
- [ ] Tests: `For` builds the three paths under `<docs>\Home Workplace\<name>`; `Prepare` creates them, seeds `hiring` from a source once and not again, copies legacy employees and data once and never overwrites; `ForemanEnvironment` has the three keys; the supervisor's starts carry `ServiceEnvironment` entries. RED → implement → GREEN → commit.

### Task 2: Boss desk in the office
**Files:** `World.cs`, `Player.cs`, `SpriteGenerator.cs`, `SceneRenderer.cs`, `UiAction.cs`, `DialogueScript.cs`, `OfficeUi.cs`, `OfficeGame.cs`, `Program.cs`; tests `PlayerTests.cs`, `DialogueTests.cs`, `OfficeUiTests.cs`.
- [ ] Tests: desk and spot exist, spot targets the desk; the boss dialogue lists three options whose actions carry the paths; picking one calls the delegate and toasts. Regenerate goldens, view, accept. RED → implement → GREEN → commit.

### Task 3: Smoke, docs
- [ ] Launch; confirm `Documents\Home Workplace\Main Office\{employees,hiring,data}` with Mia and Tidan and the existing tasks migrated; script `desk` step opens the dialogue; view a frame. Docs: office README (folder, boss desk), root README. Full gate → commit → push.

## Self-Review
Spec §2 → Task 1; §3 → Task 2; §4 → distributed; smoke → Task 3. Names: `OfficePaths`, `ServiceEnvironment`, `Office.Name`, `PropKind.BossDesk`, `BossSpot`, `InteractKind.BossDesk`, `OpenFolder`.
