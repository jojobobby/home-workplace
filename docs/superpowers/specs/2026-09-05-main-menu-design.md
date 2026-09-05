# Main menu, workplaces and settings (sub-project 5a) — design

The game opens on a Terraria-style main menu instead of booting straight into one office. A
workplace is a world: you create, rename, duplicate, delete and pick one from a list, and the
services boot for the one you enter. Settings cover video, interface, audio, controls and the
player, apply live, and persist. Multiplayer (host and join by IP, port, password) gets its own
spec next; its menu entries exist here but are disabled.

## 1. Decisions

| Decision | Choice |
|----------|--------|
| First screen | the main menu: Single Player, Multiplayer, Settings, Exit — a centred column of text buttons, the selected one gold with the highlight bar, mouse hover selects, click activates |
| Behind the menu | a seeded showroom office (four fake employees at work, no services) rendered at 10:00, dimmed to 45 %, camera drifting slowly — Terraria's world behind its menu |
| Title | "HOME WORKPLACE" in the pixel font at 4× zoom with a drop shadow, above the column |
| Workplace | one folder under `Documents\Home Workplace\<name>\` (what `OfficePaths` already makes) plus `workplace.json` (created, lastOpened, favourite) |
| Workplace list | Terraria's world list: one row per workplace — name, then a small dim line "N employees · last opened …" — favourites first, then by last opened; the selected row shows a button strip: Play, Rename, Duplicate, Delete, Folder, ★ |
| New workplace | name only (a text entry); Terraria's size and difficulty have no office analogue yet; the new one is selected in the list |
| Delete | confirm, then the folder moves to `Documents\Home Workplace\.trash\<name>-<stamp>` — nothing is destroyed |
| Play | boot the services for that folder behind the boot screen (existing), then the office; the last played workplace is remembered in `app.json` |
| In the office | Esc with nothing open opens the pause menu: Resume, Settings, Leave the office, Quit; leaving stops the services, clears the store and returns to the main menu |
| Boot failure | the boot screen keeps R to retry and gains Esc back to the menu |
| Settings | five tabs — Video, Interface, Audio, Controls, General — rows of label + value; Left/Right or Enter change a value; every change applies at once and is saved to `app.json` |
| Where settings live | `AppConfig.Office` (already `Volume`, `Scale`, `ShowDebug`), extended; `AppConfig.Save` writes the file the app reads |
| Key bindings | walk up/down/left/right, talk, menu, mute, debug, screenshot — rebindable from Controls (select a row, Enter, press a key); stored as key names |
| Dev | `--workplace <name>` skips the menu (smoke scripts); `--ui-shot menu | workplaces | new-workplace | settings-video | settings-controls | pause` |

## 2. Settings, tab by tab

| Tab | Row | Values | Applies to |
|-----|-----|--------|-----------|
| Video | Window | Windowed · Borderless · Fullscreen | `GraphicsDeviceManager` (borderless = fullscreen without a mode switch) |
| Video | Scale | Fit · 1× · 2× · 3× · 4× | back buffer size (Fit = 0 = largest that fits, as today) |
| Video | VSync | On · Off | `SynchronizeWithVerticalRetrace` |
| Video | Lighting | On · Off | the light map pass (off = flat ambient) |
| Video | Particles | On · Off | dust and effect particles |
| Video | Screen shake | On · Off | `ScreenShake` |
| Interface | UI font | Cascadia Mono · Consolas · Segoe UI · Pixel | `Hud.FontFamilies` / `Hud.PixelText` (the atlas rebuilds) |
| Interface | Shortcut bar | On · Off | the key hints at the bottom of the office |
| Interface | Debug overlay | On · Off | F3's overlay |
| Audio | Volume | 0–100 % in steps of 10 | `Jukebox.Volume` |
| Audio | Mute | On · Off | `Jukebox.Muted` |
| Controls | one row per action | the bound key | `InputMap` reads `Bindings` |
| General | Player name | text (1–16 chars) | shown in multiplayer later; saved now |
| General | Player colour | 8 swatches | the player's shirt colour |

## 3. Client library (engine-free)

`Workplaces(documentsRoot)`: `List()` → `WorkplaceInfo(Name, Root, EmployeeCount, Created,
LastOpened, Favourite)`; `Create(name)` (prepares folders and seeds hiring, writes
`workplace.json`); `Rename`, `Duplicate` (copies the tree, new name), `Delete` (to `.trash`),
`Touch` (lastOpened = now), `SetFavourite`. Names go through `OfficePaths.SafeName`; a taken
name gets ` 2`, ` 3`. An office folder without `workplace.json` (today's Main Office) is
listed and gains one when touched.

`OfficeConfig` gains `WindowMode`, `VSync`, `Lighting`, `Particles`, `ScreenShake`, `UiFont`,
`ShortcutBar`, `Muted`, `PlayerName`, `PlayerColour`, `LastWorkplace`, `Bindings`
(action → key name). `AppConfig.Save(path)` round-trips with `Load`. `Bindings.Default`
lists the actions and their keys; unknown or missing entries fall back to the default.

## 4. Office

`Ui/Menu/`: `MenuScreen : ILayer` (items, selection, hover, submit an action record),
`WorkplaceSelect : ILayer` (rows + button strip + New / Back footer), `SettingsScreen :
ILayer` (tabs + rows over a `SettingsModel` that wraps `OfficeConfig` and raises `Changed`),
`MenuActions` (the action records), `MenuLayout` (rects for rows, buttons, tabs; hit tests),
`AppFlow` (states Menu · Booting · Running · Failed with the transitions the game executes).
`Sim/Showroom` builds the seeded office (moved out of `Dev/UiScenes`).

`Render/MenuRenderer` draws the three layers and the title; `UiRenderer` dispatches to it,
so menu layers, dialogues, confirms and text entries stack the same way everywhere.

`OfficeGame`: phase from `AppFlow`; Menu phase draws the showroom, the title and the menu
stack; Play prepares `OfficePaths` for the chosen workplace, sets the services' environment
and the window title, boots; pause menu on Esc; `ApplySettings` after every change; leaving
cancels the session token, stops the supervisor, clears the store (`AppStore.Clear`), drops
the simulation. `Program.cs` no longer prepares an office at start.

## 5. Tests

Client: `Workplaces` on a temp root — create/list/rename/duplicate/delete-to-trash/touch/
favourite ordering, safe and unique names, legacy folder without metadata; config save/load
round trip; bindings parse and fall back. Office: `MenuScreen` navigation, hover, disabled
items; `WorkplaceSelect` rows, button strip, footer, actions; `SettingsScreen` value cycling
and `Changed`; key capture; `AppFlow` transitions; goldens `ui-menu`, `ui-workplaces`,
`ui-settings-video`, `ui-pause`; the `--ui-shot` scenes at 3× for the user.

## 6. Out of scope

Multiplayer (next spec), starter-team templates on creation, office size, cloud saves,
gamepad, per-workplace settings.
