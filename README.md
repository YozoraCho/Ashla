<p align="center">
  <img src="docs/images/ashla-mark.png" alt="Ashla logo" width="128" height="128">
</p>

<h1 align="center">Ashla</h1>

<p align="center">
  An external plugin framework for <i>Star Wars: The Old Republic</i>.<br>
  Overlay panels and combat-log tools, written in C#, that never touch the game itself.
</p>

<p align="center">
  <a href="https://github.com/YozoraCho/Ashla/actions/workflows/ci.yml"><img src="https://github.com/YozoraCho/Ashla/actions/workflows/ci.yml/badge.svg" alt="Build and test"></a>
</p>

---

> **Here to play?** You don't need anything on this page. Get Ashla from
> [ashla.yozoracho.dev](https://ashla.yozoracho.dev) or the [latest release](https://github.com/YozoraCho/Ashla/releases/latest),
> unzip it, run `Ashla.exe` and press **Play**.

## What Ashla never touches

Ashla runs beside SWTOR as a separate program. Every feature, including every plugin API, stays inside these rules:

- **No injection.** Nothing is loaded into the game process.
- **No memory access.** Ashla never reads or writes the game's memory.
- **No hooks or traffic interception.** Game functions and network traffic are left alone.
- **No automation.** Ashla never sends keystrokes, clicks or any other input to the game.
- **No invented data.** If the combat log doesn't record something, Ashla treats it as unavailable.

What Ashla does use: the combat-log text files SWTOR writes when Combat Logging is on, the game window's
position, size and focus (ordinary Windows window queries), and its own transparent window drawn on top. It goes
online only to read plugin lists and download the plugins you install, and the startup update check can be
turned off.

## Where Ashla is headed

Planned, in [docs/ROADMAP.md](docs/ROADMAP.md#what-makes-ashla-different): plugins that are checked to prove
they don't touch the game, every fight saved and replayable through any plugin, layouts that switch with your
discipline and location, and simple panels you can build without code.

## Projects in this repository

| Project | What it is |
|---|---|
| `Ashla.SDK` | The contracts plugins build against: plugin lifecycle, event bus, overlay panels, combat events. No UI dependency. |
| `Ashla.CombatLog` | Turns SWTOR combat-log lines into typed `CombatEvent` records. |
| `Ashla.Rendering` | The window and renderer: Silk.NET (GLFW + OpenGL 3.3) with Dear ImGui, as a desktop window or a click-through overlay. |
| `Ashla.UI` | Optional kit for plugin panels: the themes (Ashla, Republic, Empire, Mandalorian), headline font, meter bars and tabs. |
| `Ashla.Host` | The app: loads plugins, follows the game window, watches the combat log, tray icon and logging. |
| `samples/` | Small example plugins for plugin authors (HelloWorld, CombatCounter, HelloWorldOverlay). Not shipped to players. |
| `tests/` | xUnit tests (`Ashla.Tests`), and a real anonymized combat log in `combat-logs/` that the parser is checked against. |

## Build and test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and, for the graphical host,
Windows 10 or 11 with an OpenGL 3.3 driver.

```powershell
dotnet build Ashla.slnx
dotnet test Ashla.slnx
dotnet run --no-build --project src/Ashla.Host -- --gui --replay tests\combat-logs\combat_2026-10-22_04_06_32_959698.txt
```

Host options, overlay testing and the player release are in [docs/building.md](docs/building.md).
How the pieces fit together is in [docs/architecture.md](docs/architecture.md).

## Writing plugins

A plugin is a .NET 10 class library that implements `IAshlaPlugin`. It can subscribe to combat events and
register panels that Ashla draws over the game. Start with [docs/plugins.md](docs/plugins.md); the plugins in
`samples/` are working examples.

Plugins build against the `Ashla.SDK` NuGet package (and the optional `Ashla.UI`), so they can live in any
repository. The official plugins, the Combat Meter and Tactical Readout (boss timelines and alerts, in the spirit of
Cactbot), live in Holocron.

Official plugins are collected in [Holocron](https://github.com/YozoraCho/Holocron), Ashla's plugin
repository, and are added there by pull request. Players browse, install and update them in Settings >
Plugins; Ashla checks every download against the hashes Holocron published.

## Contributing

Bug reports, ideas and pull requests are welcome. Read [CONTRIBUTING.md](docs/CONTRIBUTING.md) first; commit
messages follow `type(scope): lowercase summary`. To report a security problem or a malicious plugin, see
[SECURITY.md](docs/SECURITY.md).

## License

Ashla is licensed under the [GNU Affero General Public License v3.0](LICENSE). If you distribute a modified
version of Ashla, you must publish its source code under the same license.

### Name and logo

The license covers the source code. It does not grant any right to use the **Ashla** name or the crescent
logo. A fork that you distribute must use a different name and logo, and must not present itself as Ashla or
as endorsed by this project.

## Disclaimer

Ashla is an independent fan project. It is not affiliated with or endorsed by Broadsword, Electronic Arts or
Lucasfilm. *Star Wars: The Old Republic* and related names are trademarks of their respective owners. It is
your responsibility to follow the game's terms of service when using third-party tools.
