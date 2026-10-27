# Ashla plugin packages

[Ashla](https://github.com/YozoraCho/Ashla) is an open-source, external overlay and combat-log framework for
Star Wars: The Old Republic. It never injects into the game, reads its memory, hooks functions or sends input;
plugins get their data from the combat log SWTOR writes.

| Package | What it's for |
|---|---|
| `Ashla.SDK` | Everything a plugin builds against: `IAshlaPlugin`, the plugin context, combat events, overlay panels, settings, toasts and saved fights. Its build step writes the plugin's `manifest.json`. |
| `Ashla.UI` | Optional, for plugins that draw with Dear ImGui: Ashla's themes, colors, headline font, meter bars and tabs. |
| `Ashla.CombatLog` | The combat-log parser, for tests and tools. |

The SDK's major version is Ashla's plugin API level.

## A plugin project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Version>1.0.0</Version>
    <Authors>You</Authors>
    <Description>What it does.</Description>
    <AshlaPluginName>My Plugin</AshlaPluginName>
    <AshlaPluginTags>combat;timers</AshlaPluginTags>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Ashla.SDK" Version="1.1.0" />
  </ItemGroup>
</Project>
```

Building it writes `manifest.json` next to the DLL, with each file's SHA-256 hash; Ashla won't load a plugin
without it. Ashla provides `Ashla.SDK`, `Ashla.UI` and Dear ImGui (`Hexa.NET.ImGui`) at run time, so they
aren't copied into the plugin's folder. Set `AshlaPluginsDir` to an Ashla `plugins` folder to have each build
copied there for testing.

Guides: [writing plugins](https://github.com/YozoraCho/Ashla/blob/main/docs/plugins.md) and
[Holocron](https://github.com/YozoraCho/Holocron), the official plugin list. Licensed under the AGPL-3.0.
