# Development Roadmap

## v0.1 — SDK, plugin loader, external log events
- [x] .NET 10 SDK, two sample plugins, lifecycle and configuration-independent contracts
- [x] AssemblyLoadContext + dependency resolver per plugin
- [x] External combat log line replay/live tail and demo mode
- [x] Capabilities contract to distinguish missing game state
- [x] Run actual build and smoke test on Windows
- [x] Add automated tests (`tests/Ashla.Tests`, `dotnet test Ashla.slnx`)
- [x] CI (GitHub Actions: build and test on Windows; tagged releases attach the player zip)

## v0.2 — graphical host (standalone window, no injection)
- [x] Independent Silk.NET (GLFW + OpenGL 3.3) / Dear ImGui renderer in `Ashla.Rendering`
- [x] Mouse, keyboard, text input, clipboard, cursors, resize, DPI scaling
- [x] `IOverlayRegistry` panels rendered on the render thread; thread-safe registration, visibility,
      fault isolation, no draw after unregistration
- [x] Per-plugin scopes: plugin-named logger, automatic removal of leftover subscriptions/panels
- [x] `HelloWorldOverlay` sample plugin
- [x] Plugin manager: version, author, status, errors, panel visibility, restart-based enable/disable
- [x] Window layout persistence (imgui.ini)
- [x] Manual verification on Windows 10/11 hardware

## v0.3 — overlay mode, data and distribution
- [x] Track the SWTOR window (position/size/focus) from outside the process (windowed/borderless only)
- [x] Transparent, click-through overlay window that is interactive only over plugin panels
- [x] Overlay shown only while the game (or overlay) has focus; doesn't steal focus on click
- [x] Manual verification of overlay mode on Windows 10/11 with SWTOR
- [x] Global hotkey to show/hide the overlay (default Ctrl+Shift+H, changeable or off in Settings)
- [x] In-app log window; WinExe build without a console window (player build)
- [x] Player release: `scripts/publish.ps1` zip, tray icon, overlay + watch by default, single instance
- [ ] Start with Windows option; "Launch SWTOR" button
- [ ] Installer with auto-update (e.g. Velopack); code signing
- [ ] Render-thread dispatcher for plugins (marshal work from event handlers onto the UI thread)
- [ ] Per-plugin configuration storage and a settings UI hook
- [x] Structured combat-log parser with real fixture data and correctness tests
- [x] Decode logs as Windows-1252 (v0.2 assumed UTF-8)
- [x] `CombatMeter` plugin (damage/DPS, effective healing, damage taken per fight)
- [ ] More fixture logs: group content, PvP, healer and tank disciplines
- [ ] Versioned event contracts; remove event-thread coupling
- [x] Plugin manifests and compatibility checks (API level)
- [ ] Opt-in update feed, reputation checks
- [x] Move from ImGui.NET (Dear ImGui 1.91.6) to Hexa.NET.ImGui (Dear ImGui 1.92): dynamic font atlas
      (`RendererHasTextures`), sharp text at any DPI without rebuilding fonts
- [ ] Dear ImGui 1.93 once a .NET binding ships it

## What makes Ashla different

Ashla isn't a copy of an in-game plugin framework. Five ideas shape everything below:

1. **Verified safe.** Plugins are checked, not just trusted, to prove they don't touch the game.
2. **Every fight is kept and replayable**, and every plugin works on old fights without extra code.
3. **It adapts to how you play**: layouts follow your discipline and where you are.
4. **It looks like SWTOR**: Republic and Empire themes, picked from the faction your log shows.
5. **Panels without code**: players can build simple meters and timers in the app.

### 1. Verified plugins
- [ ] Plugin manifests declare what a plugin needs (overlay panels, its own config folder, network access,
      opening files); the installer shows the list before installing, like app permissions on a phone
- [ ] Rule check that scans a plugin's compiled DLL and rejects game-touching calls: reading or writing another
      process's memory, opening the game process, remote threads, keyboard/mouse hooks, sending input, and
      ways to hide such calls (loading native code or emitting code at runtime)
- [ ] Holocron runs the rule check on every pull request; plugins that pass get a "Holocron verified" mark
- [ ] Ashla runs the same check when loading any plugin, including ones from custom repositories, and warns
      when an unverified plugin uses something it didn't declare
- [ ] Document honestly what the check can and can't catch (it filters and supports human review; in-process
      .NET code can't be fully sandboxed)

### 2. Encounter archive and replay
- [x] Save every fight (start, end, zone, opponent, the events in it) to a local archive, kept for 7, 30 or 90
      days or forever; plugins read it through `IPluginContext.Encounters`
- [x] Open an older SWTOR combat log from the app; its fights join the archive
- [x] Fight list: browse past fights by day in the Combat Meter and open any of them
- [ ] Personal bests per boss and training dummy, compare two attempts
- [ ] Replay any fight through the event bus so all plugins (meter, parser window, timers) show it as it happened
- [ ] Scrub through a replayed fight; panels show the state at that moment
- [ ] Export a fight summary as an image to share, made on your computer

### 3. Adaptive layouts
- [ ] Layout profiles: which panels are shown, where, and at what size
- [ ] Switch profiles automatically from the log: discipline changes (tank, healer, damage) and area changes
      (operations, flashpoints, training dummy)
- [ ] Manual override from the launcher or a hotkey

### 4. Faction themes
- [x] Republic (navy and gold), Empire (black, crimson and steel) and Mandalorian (gunmetal and beskar) themes,
      plus Ashla's own, for the host and the `Ashla.UI` kit; plugins using `Ashla.UI` follow automatically
- [x] Auto theme: faction from the class in the log (mirror classes have different names), remembered between
      sessions; choose in Settings > Appearance or first-run setup
- [ ] Class ids for non-English clients (only English class names and the Sentinel id are known so far)

### 5. Panels without code
- [ ] In-app editor for simple panels: pick log events, filters and a display (number, bar, list, countdown)
- [ ] Save panels as small shareable files; import a friend's panel with one click, shown with what it reads
- [ ] Built on the same plugin API, so these panels also get replay, layouts and themes

## Next — plugins, setup and the parser window
- [x] Per-plugin settings saved to `%APPDATA%\Ashla\pluginConfigs` (`IPluginContext.Config`); Ashla's own
      settings in `%APPDATA%\Ashla\settings.json`
- [x] Settings window (gear in the launcher): Game, Overlay (show/hide hotkey, toasts, reset panel positions),
      Appearance (theme, UI scale), Plugins (installed, browse, repositories), About; changes apply on Save
- [ ] Settings hook so plugins can add their own page to the Settings window
- [x] Toasts at a chosen corner: plugin failed to load, panel error, combat logging off/found, hotkey taken;
      plugins show their own with `context.Toasts`
- [x] "Update available" toast and launcher notice, checked at startup (can be turned off)
- [x] Global show/hide hotkey: press the keys in Settings > Overlay; Save refuses one another program holds
- [x] First-run setup: SWTOR location and start method, combat-log folder check, hotkey, toast corner, theme;
      can be run again from Settings > About
- [x] Launcher shown on every start: finds SWTOR (standalone or Steam), Play starts the game's own launcher or
      Steam (or nothing), never handling logins
- [x] Plugin manifests with an API version check and SHA-256 hashes, written by the build; plugins without a
      matching manifest aren't loaded
- [x] Holocron: official plugin repository (reviewed by pull request) and an in-app plugin installer that checks
      the zip's checksum, the manifest and Holocron's published file hashes
- [x] Custom plugin repositories in settings, with a clear warning
- [x] Parser window in the style of ACT overlays: Damage / Healing / Tank / Threat pages with tables and
      ability breakdowns, compact view, encounter history (this session and saved fights), split encounter,
      hide names
- [x] Tactical Readout: timers and triggers plugin driven by combat-log events, in the spirit of Cactbot (boss
      timelines with syncs and jumps, info/alert/alarm text, text-to-speech, starter timelines from saved fights)
- [x] Plugins are optional: the player build ships none, and first-run setup lists Holocron's plugins with nothing
      ticked
- [x] `Ashla.SDK`, `Ashla.UI` and `Ashla.CombatLog` as NuGet packages; the official plugins moved to Holocron
- [x] Tactical Readout reads BARAS's boss encounter files directly: timers, phases, counters and alerts for 17
      operations (BARAS data under the MIT license, shipped with the plugin); flashpoint bosses are recognized
- [ ] Tactical Readout timers for flashpoints, which BARAS doesn't have yet, made from recorded pulls
- [ ] DoT/HoT tracker from the combat log (BARAS's effect definitions: DoTs, HoTs, defensive cooldowns)
- [ ] Session page: every fight this session with totals, deaths, kills and wipes
- [ ] Charts (damage, healing and damage taken over time) and a rotation view (abilities in order, with gaps)
      in the Combat Meter
- [ ] Healer view built from the combat log only (HoTs and effects on group members); no screen reading
- [ ] Later: timer and effect editors in Tactical Readout, writing BARAS-format files to the user's fights folder
- [x] Update the website's "never connects to the internet" wording once plugin downloads or update checks exist
- [x] Tell players when a new Ashla is out (launcher sidebar, Settings > About, a toast after Play)
- [x] Update Ashla in place: download the zip, check its hash, swap the exe and restart
- [ ] Plugins state the Ashla version they need; Ashla explains instead of failing to load them
- [ ] Update button in the plugin-update toast; the update is applied the next time Ashla starts

## Speculative, conditional — additional integration providers
- [ ] Obtain authorization and review SWTOR user agreement and applicable rules
- [ ] Research authorized APIs or supported integration points only
- [ ] Add game-state and native UI capabilities behind a separate provider
- [ ] Never make unsupported player/target data appear real

The last stage is not a commitment that SWTOR permits or supports native integration.
