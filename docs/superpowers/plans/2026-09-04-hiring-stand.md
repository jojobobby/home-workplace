# Hiring Stand (4d) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Start the company empty and hire employees at an in-office stand: a role template, a brain from the models your subscriptions unlock, a name, an approximate cost.

**Architecture:** Foreman owns templates, brains, costs, hiring and firing (folder = truth, catalog reload, wake). The client exposes them as DTOs. The office adds a stand prop, a target, and a three-step dialogue flow through the existing Actions dispatcher.

**Tech Stack:** as the rest of the repo (.NET 8, MonoGame, xunit, WebApplicationFactory).

**Spec:** `docs/superpowers/specs/2026-09-04-hiring-stand-design.md`

## Global Constraints

- The folder under `employees/` stays the only truth for who is employed; no new store.
- Costs are computed from `Foreman:Pricing` and shown with a `≈`; never claimed exact.
- Nothing in the suite spawns a CLI; fakes only. A new golden fails once and is viewed.
- Commit per task; full `dotnet test HomeWorkplace.sln` before each push.

---

### Task 1: Templates, brains, costs, `GET /hiring`
**Files:** create `hiring/{engineer,reviewer,vfx-artist,manager}/{template.json,skills.md,life.md}` (moved from `employees/`, names removed), `services/foreman/src/HomeWorkplace.Foreman/HiringDesk.cs`; modify `ForemanOptions.cs` (HiringPath, Brains, Pricing entries), `Program.cs` (DI + route), `appsettings.json`; delete `employees/*` (keep `employees/.gitkeep`); tests `HiringTests.cs`.
**Interfaces:** `record Brain(string Model, Vendor Vendor, string Label)`; `record HiringTemplate(string Id, string Role, string Description, string? Effort, IReadOnlyList<string> ClaudeAllowedTools, string? CodexSandbox, Schedule Schedule, int? MaxRunMinutes, TokenEstimate TypicalTokensPerRun, int RunsPerDay, string SkillsMd, string LifeMd)`; `record TokenEstimate(long In, long Out)`; `record BrainCost(string Model, Vendor Vendor, string Label, decimal UsdPerRun, decimal UsdPerDay)`; `record HiringTemplateView(string Id, string Role, string Description, IReadOnlyList<BrainCost> Brains)`; `record HiringView(IReadOnlyList<HiringTemplateView> Templates)`; `HiringDesk.List()`.
- [ ] Tests: `GET /hiring` lists the four templates with a cost per brain (engineer on Haiku ≈ $0.10/run, $0.60/day with the default pricing); unknown pricing falls back to `default`. RED → implement → GREEN → commit.

### Task 2: `POST /hiring` and `POST /employees/{id}/fire`
**Files:** modify `HiringDesk.cs`, `Program.cs`; tests `HiringTests.cs`.
**Interfaces:** `record HireRequest(string TemplateId, string Model, string Name)`; `HiringDesk.Hire(HireRequest)` → `EmployeeView` (throws `HiringException(400 message)`); `HiringDesk.Fire(string id)` → `FireResult { Ok, Busy, NotFound }`.
- [ ] Tests: hiring writes `employees/<slug>/employee.json` with the brain's vendor+model and the template's fields, `skills.md`/`life.md` copied, the employee appears in `/employees` awake, `employee.hired` event; two hires with the same name get distinct ids; bad template/brain/name → 400; firing an idle employee archives the folder under `employees/.former/` and they vanish from `/employees`; firing a working employee → 409. RED → implement → GREEN → commit.

### Task 3: Client DTOs and API
**Files:** modify `libs/HomeWorkplace.Client/Dtos.cs`, `IForemanApi.cs`, `ForemanClient.cs`; both fakes (`tests/HomeWorkplace.UI.Tests/FakeForemanApi.cs`, `tests/HomeWorkplace.Office.Tests/Fakes.cs`); test in `tests/HomeWorkplace.UI.Tests/ClientTests` (or the nearest existing client test file).
- [ ] Tests: the client calls `GET /hiring`, `POST /hiring`, `POST /employees/{id}/fire` with the right bodies (existing HTTP-handler fake pattern). RED → implement → GREEN → commit.

### Task 4: Stand in the world, zero-employee world
**Files:** modify `Simulation/World.cs` (PropKind.HiringStand, HiringSpot), `Simulation/Player.cs` (InteractKind.HiringStand), `Render/SpriteGenerator.cs` (`hiring` sprite), `Render/SceneRenderer.cs` (draw + prompt), `OfficeGame.cs` (build the world with zero employees; click on the stand); tests `WorldTests.cs`, `PlayerTests.cs`.
- [ ] Tests: the stand is a prop by the door and its tiles are blocked, `HiringSpot` is walkable; the player standing on the spot targets the stand; an employee in reach wins over the stand; `WorldLayout.Generate([])` has no desks and still has the stand, coffee and whiteboard. Goldens unchanged (the stand is off-screen? no: it is in every frame — regenerate `office-10am`, `office-2030`, `office-moments`, and the four UI goldens, view, accept). RED → implement → GREEN → commit.

### Task 5: Hiring dialogues and actions
**Files:** modify `Ui/UiAction.cs` (OpenHiring, HireRole, HireBrain, Fire), `Ui/DialogueScript.cs` (`Hiring(HiringDto, signedIn)`, `Brains(template, signedIn)`, "Let go" in `For`), `Ui/Actions.cs` (flows), `OfficeUi.cs` (Interact on the stand → OpenHiring), `Ui/Overlay.cs` (Employees rows gain Let go); tests `DialogueTests.cs`, `ActionsTests.cs`, `OfficeUiTests.cs`, `UiGoldenTests.cs` (`ui-hiring-brains`).
- [ ] Tests: role dialogue lists templates with the cheapest available brain's per-day cost; brain dialogue lists brains, marks a not-signed-in vendor "sign in first" with a no-op action; picking a brain opens a name entry; submitting calls `HireAsync(template, model, name)` and toasts; Fire confirms then calls `FireAsync`; the stand target opens the hiring dialogue. Golden viewed. RED → implement → GREEN → commit.

### Task 6: Smoke, docs, memory
- [ ] Smoke: boot with an empty `employees/`, `--smoke-script "wait 8;stand;wait 2;pick 0;wait 2;pick 0;wait 2;type Ada;enter;wait 15"` (add a `stand` script step), frames show the stand dialogue, the brain list with costs, the name entry, and the new hire walking in; view them. Docs: `apps/office/README.md` (hiring), root README (empty start, 4d built), `hiring/README.md` (template format). Full gate → commit → push.

## Self-Review
Spec §2 → Tasks 1–2; §3 → 3; §4 → 4–5; §5 → distributed; smoke → 6. Names: `HiringDesk`, `Brain`, `HiringTemplate`, `BrainCost`, `HiringTemplateView`, `HiringView`, `HireRequest`, `FireResult`, `HiringDto`, `HiringTemplateDto`, `BrainCostDto`, `OpenHiring`, `HireRole`, `HireBrain`, `Fire`, `PropKind.HiringStand`, `InteractKind.HiringStand`, `HiringSpot`.
