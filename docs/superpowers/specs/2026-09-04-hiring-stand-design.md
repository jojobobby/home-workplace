# Hiring stand (sub-project 4d) — design

The company starts with **no employees**. A hiring stand by the office door is where you
hire: pick a role, pick a brain from the models your subscriptions unlock, name them, and
they walk in. Employees can be let go from their dialogue. Costs shown are approximate.

## 1. Decisions

| Decision | Choice |
|----------|--------|
| Starting state | `employees/` is empty; the four current employees become **role templates** under `hiring/` |
| What a hire is | a role template × a brain (vendor + model) × a name → a new `employees/<id>/` folder |
| Which brains show | all brains Foreman knows, grouped by vendor; a vendor whose CLI is not signed in is listed as "sign in first" and cannot be picked |
| Costs | approximate USD per run and per day from the template's typical tokens × the model's list price (`Foreman:Pricing`); notional on a subscription, and said so |
| Where hires live | the folder is the truth (same as today); Foreman reloads its catalog and wakes the new employee so they walk in now |
| Letting go | `POST /employees/{id}/fire` archives the folder under `employees/.former/`; refused while the employee is working |
| Persistence of old data | untouched: tasks/goals of former employees stay on disk, the office ignores unknown ids |

## 2. Foreman

- `ForemanOptions.HiringPath` (default `../../../../hiring`) and `Brains` (model, vendor,
  label; defaults: Claude Haiku 4.5 `claude-haiku-4-5-20251001`, Sonnet 5 `claude-sonnet-5`,
  Opus 4.8 `claude-opus-4-8`, Opus 5 `claude-opus-5`, Fable 5.1 `claude-fable-5-1`; Codex
  `gpt-5-codex`). `Pricing` gains list prices for each (approximate, editable).
- `hiring/<template>/template.json` = `{ id, role, description, effort, claudeAllowedTools,
  codexSandbox, schedule, maxRunMinutes, typicalTokensPerRun: { in, out }, runsPerDay }` plus
  `skills.md` and `life.md`. Templates: `engineer`, `reviewer`, `vfx-artist`, `manager`.
- `HiringDesk` service: `List()` → templates with, per brain, `usdPerRun` and `usdPerDay`;
  `Hire(templateId, model, name)` → validates (template known, brain known, name 1–24
  chars), id = `slug(name)-slug(templateId)` (suffix `-2`, `-3` if taken), writes the folder,
  `catalog.Load()`, wakes the employee (override until shift end), emits `employee.hired`;
  `Fire(id)` → 409 if Working/Waiting, archives, reloads, emits `employee.fired`.
- Endpoints: `GET /hiring`, `POST /hiring`, `POST /employees/{id}/fire`.

## 3. Client

`HiringDto { Templates: [ HiringTemplateDto { Id, Role, Description, Brains: [ BrainCostDto
{ Model, Vendor, Label, UsdPerRun, UsdPerDay } ] } ] }`, `HireRequest(TemplateId, Model,
Name)`; `IForemanApi.GetHiringAsync / HireAsync / FireAsync`.

## 4. Office

- World: `PropKind.HiringStand` (2×1) at the bottom-left near the door with `HiringSpot` in
  front; the world exists even with zero employees. Sprite `hiring` (a booth with a sign).
- `InteractKind.HiringStand`; E or a click on the stand opens it; `Player.Target` prefers an
  employee, then the stand, then the whiteboard.
- Dialogue flow: **stand** ("Who do you need?" — one option per role with the cheapest
  brain's per-day cost) → **brain** (one option per brain: label, ≈ $/run, ≈ $/day; a vendor
  not signed in shows "sign in first" and does nothing) → **name** (TextEntry) → hire → toast
  "Hired NAME as ROLE" and the employee walks in.
- Employee dialogue gains **Let go** (confirm) after Reset.
- Actions: `OpenHiring` (fetch → dialogue), `HireRole(TemplateId)`, `HireBrain(TemplateId,
  Model, Label)` → text → `HireAsync`, `Fire(EmployeeId)` → confirm → `FireAsync`.
- Signed-in vendors come from the Setup statuses the game already fetches.

## 5. Tests

Foreman: templates list with costs; hire writes the folder, reloads, wakes, unique ids;
validation 400s; fire archives and 409s while working. Client: DTO round trip through the
fake. Office: stand in the world and as a target; zero-employee world; hiring/brain
dialogues (costs, sign-in gating); action flows via fakes; a golden of the brain dialogue.
Smoke: boot empty, hire an engineer on Haiku through the script, watch them walk in.

## 6. Out of scope

Editing templates in-game, salaries/budgets per employee, changing a hired employee's brain
(let go and re-hire), art.
