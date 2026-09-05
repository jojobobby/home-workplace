# Office folder and the boss desk (sub-project 4g) — design

The company lives in its own folder under Documents, not in the repo, and the boss's desk in
the office has a computer that opens it.

## 1. Decisions

| Decision | Choice |
|----------|--------|
| Where the company lives | `Documents\Home Workplace\<office name>\` with `employees\`, `hiring\`, `data\` (Foreman's tasks, goals, events, state, `workspaces\`) |
| Office name | `app.json` → `office.name`, default `Main Office`; shown in the window title |
| First launch | the folders are created; `hiring\` is seeded from the repo's templates when empty; existing `employees\` and Foreman `data\` in the repo are copied in once when the new folders are empty (nothing is deleted from the repo) |
| How Foreman learns the paths | the game passes `Foreman__EmployeesPath`, `Foreman__HiringPath`, `Foreman__DataPath` in the services' environment (ASP.NET configuration reads them) |
| The boss desk | a desk with a computer near the bottom-right; E or click opens a dialogue: open the office folder, the workspaces (where agents work), or the employees folder, in Explorer |
| Rooms | context-api is in memory; nothing to move |

## 2. Client

`OfficePaths(Root, Employees, Hiring, Data)`: `For(officeName, documentsRoot)`,
`Prepare(officeName, templatesSource, legacyEmployees, legacyData, documentsRoot)` (creates,
seeds, migrates), `Workspaces` (`Data\workspaces`), `ForemanEnvironment()`.
`AppConfig.Office.Name`; `AppConfig.ServiceEnvironment` merged into the environment the
supervisor gives both services.

## 3. Office

`PropKind.BossDesk` (2×1) with `BossSpot`; `InteractKind.BossDesk`; sprite `bossdesk`
(desk, monitor, nameplate); `UiAction OpenFolder(Path)`; `OfficeUi` takes an `openFolder`
delegate and the paths, and the boss dialogue lists the three folders. The game launches
Explorer for the path and toasts it.

## 4. Tests

Client: paths under Documents; Prepare creates, seeds templates once, migrates once, never
overwrites; the supervisor passes the extra environment to both services. Office: the desk
and spot exist and target; the boss dialogue's options call the delegate with the right
paths; goldens regenerated (the desk is in every frame). Smoke: launch, confirm the folders
appear under Documents with Mia and Tidan migrated, open the desk dialogue.

## 5. Out of scope

Multiple offices at once, moving an office, syncing the folder anywhere.
