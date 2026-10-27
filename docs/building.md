# Building, running and releasing Ashla

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned by `global.json`; any 10.0 feature
  band from 10.0.100 up works).
- Windows 10/11 for the graphical host, with an OpenGL 3.3 capable graphics driver (any current
  NVIDIA/AMD/Intel driver). The console host is cross-platform.

SWTOR does not need to be installed or running.

## Build, test and run

```powershell
dotnet build Ashla.slnx
dotnet test Ashla.slnx

# Graphical host with the Hello, SWTOR! window (plus synthetic events in the background)
dotnet run --no-build --project src/Ashla.Host -- --gui --demo

# Replay the included real (anonymized) combat log through the plugins in the host's plugins folder
dotnet run --no-build --project src/Ashla.Host -- --gui --replay tests\combat-logs\combat_2026-10-22_04_06_32_959698.txt

# Console demo (no arguments = --demo)
dotnet run --no-build --project src/Ashla.Host
```

Building a sample copies its output into the host's plugin folder
(`src/Ashla.Host/bin/<Configuration>/net10.0/plugins/<Name>/`): `samples/Directory.Build.targets` sets
`AshlaPluginsDir` and imports the SDK's build targets (`src/Ashla.SDK/build/Ashla.SDK.targets`). To try the
official plugins in a developer build, build Holocron with
`dotnet build Holocron.slnx -p:AshlaPluginsDir=<Ashla>/src/Ashla.Host/bin/Debug/net10.0/plugins`.
The scripts `scripts/run-gui.ps1` and `scripts/run-demo.ps1` (and `.sh` versions) do the same in one step.

In the window: **Ashla ▸ Plugin Manager** lists plugins; **Panels** re-opens closed plugin windows;
**Ashla ▸ Dear ImGui Demo** shows the toolkit's demo for reference.

### Host options

```text
--launcher         The launcher (Play / Settings), then the overlay; what the player build always starts with
--gui              Graphical host window (otherwise console only)
--overlay          Transparent click-through overlay over the SWTOR window (Windows)
--target-process n Overlay: process whose main window to follow (default: swtor)
--target-title t   Overlay: only follow a window whose title contains t
--overlay-rect x,y,w,h  Overlay: pin to a fixed screen rectangle (testing; any OS)
--demo             Publish synthetic events
--replay <file>    Publish each line of a combat-log file
--watch [dir]      Tail new lines from the newest .txt/.log in a directory
--plugins <dir>    Plugin directory (default: <host>/plugins)
--config <dir>     Settings and logs directory (default: settings in %APPDATA%\Ashla,
                   logs in %LOCALAPPDATA%\Ashla\logs)
--ui-scale <n>     Override the monitor DPI scale, e.g. 1.5 (takes priority over the scale chosen in Settings)
--holocron-url <url>  Use another plugin list instead of Holocron's (testing; plain http only on localhost)
--update-url <url>    Check this address for new Ashla releases, in GitHub's releases-list format (testing;
                      plain http only on localhost). Developer builds only check with this
--after-update     Passed by Ashla to itself after it updates: waits for the old process to exit and says
                   which version it updated to
```

With `--gui`/`--overlay`, `--demo/--replay/--watch` run in the background until the window closes, and
Ctrl+C in the console closes the window cleanly. Files in the settings directory: `settings.json` (the
launcher's SWTOR folder and start method, UI scale, show/hide hotkey, toast corner, theme and last faction seen,
whether setup is done, plugin repositories and the update check), `plugins.json` (disabled plugins),
`installed-plugins.json` (which repository each installed plugin came from), `imgui.ini` (desktop window layout),
`imgui-overlay.ini` (overlay layout) and `pluginConfigs\<Plugin>.json` (each plugin's settings). Older versions kept
the first three files in `%LOCALAPPDATA%\Ashla`; they're copied over the first time. Saved fights are in
`%LOCALAPPDATA%\Ashla\encounters` (or `<config>\encounters` with `--config`): one combat-log file per fight and
`index.json`. Fights are recorded only with `--watch` (the player build's default). The last copy of each plugin
list is in `%LOCALAPPDATA%\Ashla\holocron` (or `<config>\holocron`), used when Ashla can't go online.

To try the plugin installer without Holocron, serve a folder with a `repo.json` and plugin zips
(`python -m http.server 8765 --bind 127.0.0.1`) and start with
`--holocron-url http://127.0.0.1:8765/repo.json`. Holocron's `tools/holocron.py add` and `build` write the
entries and the index; plain http is accepted only for localhost.

To try the launcher during development: `dotnet run --no-build --project src/Ashla.Host -- --launcher`. Add
`--config <empty folder>` to see first-run setup again. On Linux add
`--overlay-rect 0,0,1280,720` so the overlay has something to follow after Play.

### Real combat logs

```powershell
# Replay an existing log (offline):
dotnet run --no-build --project .\src\Ashla.Host -- --gui --replay "C:\path\to\combat_log.txt"

# Tail new lines from SWTOR's combat-log folder:
dotnet run --no-build --project .\src\Ashla.Host -- --gui --watch "$env:USERPROFILE\Documents\Star Wars - The Old Republic\CombatLogs"
```

Combat Logging must be on in SWTOR (Preferences → Combat Logging). `--watch` starts at the end of the newest
log and only shows **new** lines; a log SWTOR starts after that (a new login) is read from its first line, so its
area line isn't missed. `--replay` is for past fights.

## Overlay mode

`dotnet run --no-build --project src/Ashla.Host -- --overlay` shows the plugin panels directly over SWTOR.
Use the game in **windowed** or **fullscreen (windowed)** mode; exclusive fullscreen doesn't let other windows
draw on top. To try it without the game, follow any window (`--overlay --target-process notepad`) or pin it to
a rectangle (`--overlay-rect 0,0,1280,720`). How the overlay behaves is described in
[architecture.md](architecture.md#overlay-mode).

## Player release

Players don't need the .NET SDK or command lines. Build the release on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

This produces `artifacts\Ashla-<version>-win-x64.zip` (self-contained) containing `Ashla.exe`,
`glfw3.dll`, `cimgui.dll`, `ashla.ico`, `README.txt` (player instructions, from `docs/PLAYER-README.txt`),
`LICENSE.txt` and `THIRD-PARTY-NOTICES.txt`. It ships no plugins: players pick them from Holocron in first-run
setup or Settings > Plugins. The samples are never shipped.

### SDK packages

`Ashla.SDK`, `Ashla.UI` and `Ashla.CombatLog` are published to NuGet for plugins to build against. Their version is
`AshlaSdkVersion` in `Directory.Build.props`; its major number is the plugin API level (`AshlaApi.Level`), and a
test checks they agree. To publish, raise `AshlaSdkVersion`, then push a tag such as `sdk-v1.0.0`:
`.github/workflows/sdk-release.yml` checks the tag, runs the tests, packs the three packages, pushes them to
nuget.org through NuGet's Trusted Publishing (no stored API key; the `NUGET_USER` secret holds the nuget.org
username) and attaches them to a GitHub release. To pack locally:
`dotnet pack src/Ashla.SDK -c Release -o artifacts/nuget` (and the same for `Ashla.UI` and `Ashla.CombatLog`).

Pushing a tag such as `v0.3.0` runs the same script on GitHub Actions and attaches the zip to a GitHub
release (`.github/workflows/release.yml`). The tag must match `<Version>` in `src/Ashla.Host/Ashla.Host.csproj`.
Installed copies offer "Update now" for a release only if its asset is named `Ashla-<version>-win-x64.zip` and
GitHub lists a `sha256:` digest for it (GitHub adds one to every asset uploaded since June 2025); otherwise
players get the Download button. Keep the zip's layout flat, with `Ashla.exe` at the top, and never put a
`plugins` folder in it: the updater skips it either way.

The player build (`-p:AshlaPlayerBuild=true`) differs from the developer build only in startup:

- Windowed app named `Ashla.exe`: no console window. Logs go to `%LOCALAPPDATA%\Ashla\logs` (kept 7 days)
  and the in-app **Log** window; unexpected errors show a message box pointing at the log file.
- Double-clicking it (no arguments) opens the launcher every time. Play starts SWTOR the chosen way (its
  launcher, Steam, or not at all), then overlay mode starts and watches
  `Documents\Star Wars - The Old Republic\CombatLogs`. Starting it with command-line options skips the launcher.
- A tray icon (overlay mode, Windows) shows the status (waiting for SWTOR / attached / combat logging off /
  hidden) and has Show/Hide Ashla (also the global hotkey, default Ctrl+Shift+H), Open log file, Open log folder, Open plugins folder, Open settings folder and Quit. It notifies
  once on start and when the CombatLogs folder is missing.
- Only one copy runs at a time.

The developer build keeps its console (`dotnet run` with no arguments = console demo).

## Package versions

All NuGet versions are in `Directory.Packages.props`. Hexa.NET.ImGui 2.2.9 (Dear ImGui 1.92.2b) is the
default; 3.1.0-experimental (Dear ImGui 1.92.9b) also builds, passes the tests and renders the same. To try
it, set `AshlaHexaImGuiVersion` to `3.1.0-experimental`. Plugins built against one version should be rebuilt
for the other.

## Manual Windows checks

### Desktop window

1. `dotnet build Ashla.slnx` then `dotnet run --no-build --project src/Ashla.Host -- --gui --demo`.
2. Expect a dark "Ashla" window with **Hello, SWTOR!** and **Plugin Manager** windows.
3. Click **Click me** (counter increases), drag the title bar, resize from the corner, close it with ✕,
   re-open it via **Panels** or the plugin manager checkbox. Click **Show a toast**: a toast appears in the
   bottom-right corner (or the corner chosen in Settings) and fades after about 6 seconds; hovering keeps it,
   clicking closes it.
4. Type into a text field in **Ashla ▸ Dear ImGui Demo** (Widgets ▸ Basic); try Ctrl+C / Ctrl+V.
5. At 125–200% Windows display scaling, text and windows should be scaled and crisp; drag the window to a
   monitor with a different scale and the UI should rescale.
6. Close the window: the console shows the plugins unloading and "Graphical host closed."; reopening
   restores window positions.

### Overlay

1. Open Notepad, then `dotnet run --no-build --project src/Ashla.Host -- --overlay --target-process notepad`.
   The console should say "Game window found"; the **Ashla** launcher and **Hello, SWTOR!** appear over
   Notepad's text area with Notepad visible behind them.
2. Click into Notepad's text area away from Ashla windows and type: the text goes to Notepad.
3. Click **Click me** in the Hello window: the counter increases and Notepad keeps keyboard focus
   (keep typing to check). Drag/resize the Hello window.
4. Move and resize Notepad: the overlay follows. Minimize Notepad: the overlay hides; restore: it returns.
5. Alt+Tab to another app: the overlay hides after a moment; Alt+Tab back: it reappears. It should have
   no taskbar button and not appear in Alt+Tab.
6. With Notepad focused, press Ctrl+Shift+H: the overlay hides and Notepad doesn't type an "H". Press it
   again: it returns. The tray icon's Show/Hide Ashla does the same. Change the hotkey in the launcher's
   Settings > Overlay, then try one another program uses (for example one set in a game or Discord): Save
   should refuse it.
7. Close Notepad: the console says it is waiting; reopen Notepad: it attaches again.
8. Repeat with SWTOR in fullscreen (windowed) mode: `--overlay` (no target needed). If it doesn't attach,
   check the client's process name in Task Manager ▸ Details and pass it with `--target-process`.
9. Exit with the launcher's **Exit Ashla** button or Ctrl+C in the console.
