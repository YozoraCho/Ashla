# Architecture

```text
Plugins (C# DLLs, one collectible AssemblyLoadContext each)
  -> Ashla.SDK          contracts: IAshlaPlugin, IPluginContext, IEventBus, IGameState,
                        IOverlayRegistry / IPluginPanel / IPanelRegistration, PluginInfo
  -> Ashla.Host         loader + per-plugin scopes, event bus, panel registry, plugin state,
                        console + GUI composition (plugin manager, ImGui panel frames)
       -> Overlay/          game-window tracking (Win32 window queries only) + show/hide policy
       -> ExternalProvider  (combat-log capability only; no game state)
       -> CombatLogSource   (synthetic, replay or file tail; Windows-1252)
       -> Ashla.CombatLog   line parser -> CombatEvent (SDK contract), date/midnight handling
       -> Ashla.Rendering   standalone renderer: Silk.NET GLFW window + OpenGL 3.3
                            + Dear ImGui backend (input, DPI, clipboard, fonts), desktop or
                            transparent click-through overlay window. No SDK dependency.
  -> Ashla.UI            optional plugin UI kit: theme, palette, headline font, meter widgets
Shared with plugins by the host: Ashla.SDK, Hexa.NET.ImGui (+ HexaGen.Runtime), Ashla.UI
```

Dependencies (versions in `Directory.Packages.props`): Hexa.NET.ImGui 2.2.9 (Dear ImGui 1.92.2b, docking
build), Silk.NET 2.23.0 (`Silk.NET.Windowing`, `Silk.NET.Input`, `Silk.NET.OpenGL`), StbImageSharp 2.30.16
(reads the PNG images in `ashla.ico`).

The logo lives in one file, `src/Ashla.Host/Assets/ashla.ico` (every size from 16 to 256 px, stored as PNG). It is the
exe's icon, the tray icon, the window and taskbar icon (`RenderWindowOptions.Icons`), and the launcher's logo.
`docs/images/ashla-mark.png` is the same logo for the README.

## Plugin loading

- Plugins are discovered from `plugins/<name>/<name>.dll` and loaded into a collectible
  `AssemblyLoadContext` each, with an `AssemblyDependencyResolver` for their own dependencies.
- Before any plugin code is loaded, `PluginManifest.cs` checks the folder's `manifest.json`: the id matches the
  folder, the API level is between `PluginManifests.OldestSupportedApiLevel` and `AshlaApi.Level`, every listed
  file has its SHA-256 hash, and no unlisted DLL is present. A plugin that fails is listed as Failed with the
  reason and a toast. The manifest is written at build time by an inline task in the SDK package's build targets
  (`src/Ashla.SDK/build/Ashla.SDK.targets`), which also keeps host-shared assemblies out of a plugin's output.
- `Ashla.SDK`, `Hexa.NET.ImGui`, `HexaGen.Runtime` and `Ashla.UI` always resolve to the host's copy: one SDK
  type identity, one native Dear ImGui context, one set of fonts.
- Each plugin gets a scope: a plugin-named logger, and the host removes any event subscriptions or panels it
  leaves behind when it unloads.
- The plugin manager shows version, author, status and errors, and enables/disables plugins (applied on the
  next start).
- Plugin settings (`IPluginContext.Config`) are JSON files in `pluginConfigs\`. Plugin types are serialized with a
  reflection-only contract (`Settings/UnloadSafeTypeResolver.cs`): System.Text.Json's default accessors are cached
  globally and would keep an unloaded plugin's load context alive. A test checks the Combat Meter still unloads.

## Holocron and the installer

All of it is in `Holocron.cs`, driven from `LauncherModel` (Settings > Plugins) and `Program.cs`.

- `HolocronClient` fetches `repo.json` from Holocron
  (`https://raw.githubusercontent.com/YozoraCho/Holocron/main/repo.json`) and from any repositories in
  `settings.json`, in parallel, over https only (plain http only for loopback, for `--holocron-url`), up to 4 MB
  each. A `Format` newer than `HolocronClient.SupportedFormat` is refused. Each list is saved in the `holocron`
  folder and used when a repository can't be reached.
- `PluginInstaller` runs in the launcher, before any plugin is loaded, so nothing is locked. It downloads the zip
  (up to 50 MB), checks its SHA-256 against the listing, unpacks it into `plugins\.installing-<id>-<guid>`
  (no paths outside it, at most 500 files and 200 MB), runs the same manifest check as loading, and checks the
  manifest's version and `Files` against the listing. The old folder is moved aside, the new one moved in (same
  volume, so both are renames) and the old one deleted; on failure the old one is moved back. Leftover
  `.installing-*` and `.replaced-*` folders are removed at startup. `installed-plugins.json` records which
  repository each plugin came from, so updates come from the same list; plugins without a record get updates from
  Holocron only.
- `PluginUpdates.Find` compares installed versions with the catalog (`PluginVersions` compares numerically and
  ignores `-beta`-style suffixes). With the update check on, the launcher fetches at startup and shows the
  result; after Play, the overlay reuses that catalog (or fetches it) and shows a toast for updates. Updates are
  installed from the launcher only.
- Entries outside Holocron are shown as not reviewed and need a confirm to install; adding a repository needs a
  confirm too. Repositories are saved right away, not on Save, because the catalog depends on them.

## Startup and the launcher

- The player build always opens the launcher first (`Gui/LauncherWindow.cs`, logic in `Launch/LauncherModel.cs`).
  It closes when the player presses Play, and the overlay then opens in its own window in the same process.
- The first time (no `"SetupDone": true` in `settings.json`), the launcher shows setup instead of the Play view:
  Welcome, SWTOR (folder and start method), Combat Logging (checks the CombatLogs folder), Hotkey, Toasts,
  Theme, Plugins, Finish. The Plugins page reads Holocron's list when the player reaches it, lists only Holocron's
  own plugins with nothing ticked, and installs the picked ones when the player presses Next; Finish waits until
  they're done. It
  edits the same `SettingsDraft` as Settings and saves through the same checks on Finish; each page checks its
  own choice before Next. Skip setup marks it done without saving anything else. Settings > About > Run setup
  again opens it with the current settings filled in; its Cancel changes nothing.
- The gear opens the Settings window, a modal over the launcher. It edits a `SettingsDraft`; Save checks it and
  writes `settings.json` (SWTOR folder, start method, UI scale, hotkey) and `plugins.json`, Cancel discards it. Plugins
  are listed from their DLLs' version info without loading them, so turning one off applies to the same session.
- SWTOR is found by checking for `launcher.exe` in Steam libraries (Steam's folder from the registry, other
  libraries from `steamapps\libraryfolders.vdf`) and in the standalone installer's folders under Program Files.
  The player can pick the folder instead; it's saved in `settings.json`.
- Play starts the game's own `launcher.exe`, or `steam://rungameid/1286830`, or nothing. No arguments are passed
  and the game process is never opened; if SWTOR is already running, nothing is started.
- Settings live in `%APPDATA%\Ashla` and logs in `%LOCALAPPDATA%\Ashla\logs` (`Settings/SettingsLocations.cs`).

## Overlay mode

- Ashla finds the game's main window (process `swtor`, override with `--target-process`) using ordinary
  Windows window queries, and keeps a borderless, always-on-top, transparent window exactly over the game's
  client area, following moves and resizes. It never opens the game process, reads its memory, or sends it
  input; the game is unaware of the overlay.
- Mouse clicks pass through to the game everywhere except over Ashla windows.
- Clicking overlay buttons doesn't take keyboard focus from the game; the overlay takes focus only while
  you type into a text field, and gives it back when you click into the game.
- The overlay is shown only while the game window exists, isn't minimized, and the game (or the overlay)
  has focus. It hides when you Alt+Tab to another application, and waits (hidden) if the game isn't running.
- The show/hide hotkey (`Overlay/Hotkey.cs`, default Ctrl+Shift+H) is a Win32 `RegisterHotKey` on the tray
  icon's message-only window, so it works while the game has focus without reading the game's input: Windows
  sends `WM_HOTKEY` to Ashla and doesn't pass the combination on. That's why it must include Ctrl or Alt.
  Settings captures the combination with ImGui's key state and, on Save, registers it briefly to check that no
  other program holds it. In `settings.json` it's text (`"ToggleHotkey": "Ctrl+Shift+H"`); empty means none.
  The tray menu's Show/Hide Ashla does the same; hiding this way wins over the focus rules below until shown again.
- Saved fights (`Encounters.cs`): with `--watch`, an `EncounterRecorder` cuts the event stream at the log owner's
  EnterCombat/ExitCombat and the `EncounterArchive` writes each fight's raw lines (plus the area and discipline
  lines before it) as a combat-log file named after its first line, so the existing parser reads it back with the
  right date. `index.json` holds the title, character, duration and totals for listing without parsing. Fights
  already saved (same start and character) are skipped, so opening a log twice adds nothing. Plugins get
  `IEncounterArchive` through their context; the Combat Meter rebuilds a picked fight in its own tracker, so live
  tracking is never touched.
- Toasts (`Toasts.cs`, drawn by `Gui/ToastRenderer.cs`): one `ToastCenter` per process takes toasts from any
  thread (plugins through `IPluginContext.Toasts`, the host for load failures, panel errors, combat logging and
  the hotkey). The render thread draws up to four as small borderless ImGui windows in the chosen corner
  (`settings.json` `Overlay.ToastCorner`), always in front, and ages them by frame time, so a hidden overlay
  pauses them. Repeats of a toast on screen restart it instead of stacking.
- Themes (`Ashla.UI`): four `AshlaPalette`s; `AshlaColors` reads the active one, so plugins follow without
  changes. A theme switch re-runs the style setup through the renderer (`ImGuiWindowHost.RefreshStyle`) so DPI
  scaling is kept. `Themes.cs` maps the class on DisciplineChanged lines to a faction (`FactionWatcher`, log owner
  only); with the Auto theme the overlay switches live and `settings.json` remembers `Appearance.LastFaction`, so
  the launcher opens in the same colors next time.
- A small **Ashla** launcher window replaces the menu bar (Plugin Manager, panel visibility, Exit).
  Window positions are relative to the game window and saved separately from desktop mode.

## Known limitations

- The exe isn't code-signed, so SmartScreen warns on first run.
- Any window drawn over a game can cost some performance (Windows may stop using the fastest fullscreen
  presentation path for the game while another window overlaps it).
- The overlay sits on top of everything while the game is focused, including game-launched dialogs.
- `dotnet run` opens a console window alongside the GUI (the developer host is a console app); logs go there.
- Plugin load contexts are not security boundaries. Load **trusted plugins only**. Holocron's review and the
  installer's hash checks make sure a player gets the reviewed files; they don't sandbox what those files do.
- Unloading is cooperative. The host removes everything it tracks and a test confirms the sample overlay
  plugin's load context is collected, but plugin threads, timers or static caches can still pin a plugin.
  Enable/disable needs a restart; there is no hot reload.
- A plugin can still crash the process with native faults (bad ImGui pointer use, stack overflow);
  exception isolation only covers managed exceptions.
- Panel removal from a non-render thread waits for that panel's in-progress `Draw` to finish; don't hold a
  lock in `Dispose` that your `Draw` also needs.
- Event handlers run synchronously on the publishing thread (a background thread in `--watch`).
- The parser was built from one solo session (Sentinel with a companion). Group content, PvP and other
  classes may log value shapes not seen yet; those lines still publish, with the unrecognized part in `Raw`.
- It is not established that all external tools are allowed under SWTOR's terms. Review the rules
  before distributing or using software with the game.
