# Writing Ashla plugins

A plugin is a .NET 10 class library that Ashla loads from `plugins/<Name>/<Name>.dll`. Each plugin gets its own
load context, its own logger, and a context object for subscribing to events and registering panels.

Plugins follow the same rules as Ashla itself: no injection, no reading or writing game memory, no hooks or
traffic interception, no input sent to the game, and no made-up data. Plugins that break these rules won't be
listed in [Holocron](https://github.com/YozoraCho/Holocron).

## Project setup

Create a .NET 10 class library named `MyPlugin`, then:

- reference the `Ashla.SDK` NuGet package (`<PackageReference Include="Ashla.SDK" Version="1.0.0" />`),
- set `<EnableDynamicLoading>true</EnableDynamicLoading>`,
- set `<Version>`, `<Authors>` and `<Description>` (shown in the plugin manager), and optionally
  `<AshlaPluginName>` (display name) and `<AshlaPluginTags>` (e.g. `combat;timers`),
- implement `IAshlaPlugin`.

The main DLL name must match the plugin folder name. Ashla provides `Ashla.SDK`, `Ashla.UI` and Dear ImGui at run
time, so the SDK's build step leaves them out of your output. Set `<AshlaPluginsDir>` to an Ashla `plugins` folder
(for example a developer build's `src/Ashla.Host/bin/Debug/net10.0/plugins`) and every build is copied there to try.

The SDK's major version is the plugin API level (`AshlaApi.Level`). `samples/HelloWorld` in this repository is the
smallest working example (the samples reference the SDK projects directly instead of the package); the Combat
Meter and Tactical Readout in [Holocron's src folder](https://github.com/YozoraCho/Holocron/tree/main/src) are full ones.

### The manifest

Every plugin folder has a `manifest.json`, and Ashla won't load a plugin without one. The `Ashla.SDK` package
writes it on every build (`src/Ashla.SDK/build/Ashla.SDK.targets`; the samples here import the same file).

```json
{
  "Id": "CombatMeter",
  "Name": "Combat Meter",
  "Version": "0.1.0",
  "Author": "Ashla contributors",
  "Description": "Damage, healing, damage taken and threat per encounter ...",
  "ApiLevel": 1,
  "Repository": "https://github.com/YozoraCho/Ashla",
  "Tags": ["combat", "parser", "meter"],
  "Files": {
    "CombatMeter.deps.json": "2423a8ba...",
    "CombatMeter.dll": "df065a74...",
    "CombatMeter.runtimeconfig.json": "6ab5c474..."
  }
}
```

Before loading a plugin, Ashla checks that:

- `Id` matches the folder name,
- `ApiLevel` is one this Ashla supports (`AshlaApi.Level` in the SDK is the current level; it goes up only when a
  change would break existing plugins),
- every file in `Files` is there with the same SHA-256 hash, and every `.dll` in the folder is listed
  (`.pdb` files are ignored).

If anything is off, the plugin isn't loaded; the Plugin Manager and a toast say why. This catches broken
downloads, half-finished updates and DLLs that were changed or dropped in afterwards. It doesn't stop someone who
rewrites the manifest as well: for plugins installed from Holocron, Ashla also checks the hashes Holocron
published. Ship the whole output folder
(the build's `manifest.json` included) and don't edit files after building.

## Combat-log events

Each combat-log line is published twice on the event bus: as the raw `CombatLogLine` (unchanged) and, when it
parses, as a `CombatEvent` (`Ashla.SDK/CombatEvents.cs`):

```csharp
context.Events.Subscribe<CombatEvent>(e =>
{
    if (e.IsDamage && e.Source?.IsOrBelongsTo(myPlayerId) == true)
        total += e.Value!.Amount ?? 0;            // e.Value.IsCritical, .Effective, .Kind (damage type), .Mitigation, .Absorbed
});
```

- Classify events by game id, not name: names are in the client's language. `CombatIds` lists the ids
  confirmed in real logs (`IsDamage`, `IsHeal` and `IsEvent(CombatIds.EnterCombat)` use them).
- `Effective` is the amount after `~` in the log: healing without overheal, damage without overkill.
- Health on an entity is the value after the event. For deaths, the target is who died.
- Timestamps are local game-client time; the date comes from the log file name and rolls over at midnight.
- Log files are Windows-1252, not UTF-8 (`CombatLogFile.Encoding`).
- Anything not understood is kept, not guessed: `CombatValue.Raw`/`IsFullyParsed`, `CombatEntityKind.Unknown`,
  and the raw line in `CombatEvent.RawText`.
- A player's log only contains events that involve that player or their companion. Other group members'
  numbers are not available from it.
- Handlers can run on a background thread; lock state that `Draw()` also reads (see the Combat Meter in Holocron).

The format is documented from Game Update 7.x logs (`<v7.0.0b>` on area-change lines). The sample in
`tests/combat-logs/` is a real log with the character's name and id replaced; tests check that every line parses
and that totals match independently computed values.

## Saving settings

`context.Config` saves your plugin's settings to `%APPDATA%\Ashla\pluginConfigs\<PluginId>.json`. Use a plain
class with public get/set properties and a parameterless constructor:

```csharp
public sealed class MySettings
{
    public int Tab { get; set; }
    public bool ShowCompanion { get; set; } = true;
}

var settings = context.Config.Load<MySettings>();   // a new MySettings if nothing is saved yet
settings.Tab = 2;
context.Config.Save(settings);                      // false (and logged) if it couldn't be written
```

- Saving is safe against crashes: the file is written to a temporary file first, then swapped in.
- An unreadable file gives you defaults; Ashla logs why and keeps the bad file as `.json.bad`.
- Nested classes, lists, dictionaries and enums work (enums are saved by name); `[JsonIgnore]` and
  `[JsonPropertyName]` from System.Text.Json are respected.
- Load and Save are safe to call from any thread. The Combat Meter uses this to remember the selected tab, and
  `samples/HelloWorldOverlay` to remember its click count.

## Saved fights

`context.Encounters` gives read access to the fights Ashla has saved (every fight, as it ends, plus any the player
added with "Open combat log..."):

```csharp
foreach (var fight in context.Encounters.List())        // newest first
    Console.WriteLine($"{fight.Title} {fight.Duration} {fight.Character}");

var events = context.Encounters.Load(fight.Id);          // the fight's CombatEvents, oldest first
context.Encounters.Changed += () => { /* a fight was saved, added or deleted */ };
```

- `Load` returns the same `CombatEvent`s the event bus carried, including the area and discipline lines from before
  the fight. Feed them to the same code that handles live events to get the fight's numbers; the Combat Meter does
  this when a saved fight is picked.
- `Changed` is raised on a background thread. Ashla removes your handler when your plugin unloads.
- `IsAvailable` is false where nothing is saved (console mode without a settings folder).

## Toasts

`context.Toasts` shows a short notification in the overlay corner the player picked (Settings > Overlay):

```csharp
context.Toasts.Show("Fight saved");
context.Toasts.Show("Enrage in 10 seconds", "Kephess", ToastKind.Warning);
```

- Kinds: `Info` and `Success` stay about 6 seconds, `Warning` 9, `Error` 12. Hovering keeps a toast; clicking
  closes it. Time only counts while the overlay is on screen, so toasts aren't missed while the game is in the
  background.
- Each toast shows your plugin's name and is written to the log too. In console mode toasts are only logged.
- Showing the same title and text again while it's on screen restarts it (with a count) instead of stacking
  copies. Four show at once; more wait their turn.
- Keep them for things worth interrupting the player for. A title is cut at 80 characters, the text at 300.
- Safe to call from any thread. `samples/HelloWorldOverlay` has buttons that show one.

Ashla uses the same toasts for its own notices: a plugin that couldn't load, a panel that hit an error, combat
logging off (and found later), and a hotkey another program already uses.

## Panels

The SDK itself has no UI dependency. Panels draw with **Hexa.NET.ImGui** (namespace `Hexa.NET.ImGui`), which
the host provides and shares with every plugin. Reference it without shipping a copy
(see `samples/HelloWorldOverlay/HelloWorldOverlay.csproj`):

```xml
<!-- Inside this repository the version comes from Directory.Packages.props. Outside it, add the same
     Version as the Ashla release you target (currently 2.2.9). -->
<PackageReference Include="Hexa.NET.ImGui" ExcludeAssets="runtime;native" />
```

```csharp
public void Initialize(IPluginContext context)
{
    if (!context.Overlays.IsAvailable) return;            // console mode has no renderer
    _registration = context.Overlays.Register(new MyPanel()); // IPluginPanel: Id, Title, Draw()
}

public void Dispose() => _registration?.Dispose();         // Draw is never called after this returns
```

Ashla uses Dear ImGui 1.92's dynamic fonts: text is rasterized at whatever size it is drawn, so it stays sharp at
any DPI. Change size with `ImGui.PushFont(font, size)` (`ImFontPtr.Null` keeps the current font); sizes are
unscaled, and the host applies the display scale.

For a compact, overlay-style frame (no title bar, translucent background), also implement
`IPluginPanelAppearance` and return e.g. `new PanelAppearance { ShowTitleBar = false, BackgroundOpacity = 0.8f }`.
Such panels are moved by dragging empty space and shown/hidden from the launcher or **Panels** list.
`IPluginPanelAppearance.Appearance` is read every frame, so it can change: set `FitToContent = true` (for example
in a compact mode) and the window takes the size of what you draw; draw at a fixed width while it's on. When it
goes back to false, the window returns to the size it had before (the Combat Meter does this).

To match Ashla's look, reference the optional `Ashla.UI` package (provided by Ashla at run time, so it isn't
copied into your output): `AshlaColors` (the active theme's colors), `AshlaFonts.LargeText`
(headline numbers; `AshlaFonts.Large` / `LargeSize` to push the font yourself), `AshlaDraw.MeterBar`,
`AshlaDraw.Tabs`, `AshlaDraw.Compact` (12.3k / 1.23M).

### Themes

Players pick one of four themes (Ashla, Republic, Empire, Mandalorian), or Auto, which switches between Republic
and Empire with the character in the combat log. `AshlaColors` always returns the active theme's colors, so:

- Read colors while drawing (`AshlaColors.Accent`), don't copy them into fields; the theme can change while your
  panel is open.
- `AshlaColors.Accent` is "you" and the main highlight, `Companion` the second accent, `Warning` and `Error` for
  things that need attention, and `Healing`, `Damage`, `DamageTaken`, `Threat`, `Neutral` for meters.
- Round custom shapes with `AshlaTheme.Round(radius)`: the Empire and Mandalorian themes use sharper corners.
- `AshlaTheme.Current` is the active `AshlaPalette` (`Id`, `Name` and every color) if you need to know which one.
- Windows the host frames (title bars, buttons, inputs) follow the theme on their own.

### Rules for panels

- The host draws the window frame (title bar, move, resize, close); `Draw()` fills in the contents and
  runs only on the render thread. Don't call ImGui from other threads.
- `IPanelRegistration.IsVisible` shows/hides the window; the user's close button sets it to false.
- A panel that throws is suspended (the host and other panels keep running) and shown with its error and
  a **Retry** button in the plugin manager. Dear ImGui's error recovery repairs unbalanced Begin/Push calls.
- Event-bus handlers may run on a background thread (`--watch`), so share state with `Draw()` safely.
- Prefer `ImGui.TextUnformatted` for arbitrary strings; the formatted `Text` variants treat `%` specially.

## Publishing a plugin

Official plugins are listed in [Holocron](https://github.com/YozoraCho/Holocron), which players browse and
install from in Settings > Plugins. To be listed, a plugin needs:

- public source code, under a license compatible with the AGPL-3.0 (Ashla's license),
- a release zip built from that source, with the build's `manifest.json`,
- to follow the rules at the top of this page.

To submit one:

1. Zip your plugin's output folder (files at the root of the zip or inside one folder) and attach it to a
   release on your repository.
2. Fork Holocron and run `python tools/holocron.py add <zip address> --license <SPDX id> --changes "..."`. It
   checks the zip and writes `plugins/<Id>.json` from your manifest.
3. Open a pull request with that file and the rebuilt `repo.json`. A check downloads the zip again; a maintainer
   reviews your source.

For an update, publish a zip with a higher `<Version>` and run `add` again with its address. Players see the
update the next time Ashla starts. Holocron's README has the full rules and the entry format.

The official plugins live in Holocron's `src/` folder and are released the same way, one at a time, from tags like
`CombatMeter-v0.1.0` there. Ashla's own player build ships no plugins.

When a player installs your plugin, Ashla checks the zip's SHA-256 against Holocron, unpacks it safely, checks
your `manifest.json` (id, API level, file hashes) and that its version and hashes are the ones Holocron
published. Anything that doesn't match isn't installed, so publish exactly the files you built.

### Your own repository

You can also host a list yourself: a `repo.json` in Holocron's format, served over https (a raw GitHub address
works). Holocron's `tools/holocron.py build` writes one from a `plugins/` folder. Players add it under
Settings > Plugins > Repositories, and Ashla shows its plugins as "Not reviewed by Holocron" and asks before
installing them. If a plugin id is in both lists, a player gets updates from the list they installed it from.

### Licensing

Ashla and its SDK are licensed under the AGPL-3.0. Plugins run inside Ashla's process and are built against
its SDK, so a plugin you distribute should use an AGPL-3.0-compatible license and publish its source. If you're
unsure, license your plugin under the AGPL-3.0 as well.
