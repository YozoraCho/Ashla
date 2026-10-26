Ashla - overlay tools for Star Wars: The Old Republic
=====================================================

Ashla shows small tool windows (like a combat meter or boss timers) on top of SWTOR. It runs as a separate
program next to the game: it does not inject anything into SWTOR, read its memory, change game files, or send
input. Its data comes from the combat-log text files SWTOR writes when combat logging is on.

Ashla's features are plugins, and it comes with none: you pick the ones you want the first time you run it,
or any time later in Settings > Plugins.

Not affiliated with Broadsword, EA or Lucasfilm. Check SWTOR's rules yourself before using any third-party tool.


Getting started
---------------
1. Unzip this folder anywhere (for example Documents\Ashla). There is no installer.
2. In SWTOR, turn on combat logging: Preferences > Combat Logging > enable it.
   (Log out to character select and back in if you were already playing.)
3. Set SWTOR's display mode to "Fullscreen (Windowed)" or "Windowed". Exclusive fullscreen hides other
   windows, including Ashla.
4. Run Ashla.exe. The first time, a short setup asks where SWTOR is installed (it's usually found for you),
   how Play should start the game, checks combat logging, and sets the show/hide hotkey, the corner for
   notifications and the theme. Then it lists the plugins you can install, with nothing ticked: tick the ones
   you want (for example Combat Meter for damage and healing numbers). After that the Ashla launcher opens and
   shows whether it found SWTOR and whether combat logging looks on. Press Play: Ashla starts the game's own
   launcher (or Steam), where you sign in as usual.
   Ashla never sees your login. If SWTOR is already running, Play just attaches Ashla to it.
5. After Play, Ashla runs in the notification area (system tray, by the clock) and appears over the game.

The gear in the launcher's top-right corner opens Settings:
- Game: where SWTOR is installed (if it wasn't found) and how Play starts it: through the SWTOR launcher,
  through Steam, or not at all (you start the game yourself).
- Overlay: the show/hide hotkey, which corner notifications appear in, and a button that puts every panel back
  in its starting place.
- Appearance: the theme (Ashla, Republic, Empire, Mandalorian, or Auto: Republic or Empire to match the
  character you're playing) and the size of Ashla's windows and text.
- Fights: whether fights are saved, how long they're kept (7, 30 or 90 days, or forever), opening an older
  combat log, and deleting saved fights.
- Plugins: turn plugins on or off, update or remove them (Installed), find and install new ones (Browse),
  and manage where they come from (Repositories). See "Plugins" below.
- About: version, license and links, and Run setup again.
Apart from that reset button, nothing changes until you press Save; Cancel or Esc leaves everything as it
was.

When SWTOR has focus, Ashla's windows appear on top of it. Clicks go to the game everywhere except on
Ashla's windows. Clicking Ashla's buttons doesn't take keyboard focus away from the game.


Using it
--------
- The small "Ashla" window starts collapsed; open it with its arrow. It turns tool windows on and off, opens
  the Plugin Manager and the Log, changes the theme while you play, and has an Exit button.
- Move a window by dragging it; resize it from its bottom-right corner. Positions are remembered.
- Ashla hides when you Alt+Tab away from SWTOR or minimize it, and comes back when you return.
- Ctrl+Shift+H hides or shows Ashla at any time, also while you play. To use other keys, open Settings >
  Overlay, click Change and press the new combination; it needs Ctrl or Alt so it can't take over keys you
  type in the game. While Ashla holds a combination, the game doesn't receive it.
- Notifications ("toasts") pop up in a corner of the game for things like combat logging being off or a
  plugin that couldn't load, and fade after a few seconds. Hover one to keep it; click it to close it.
- Right-click the tray icon to show or hide Ashla, for the log file, the plugins and settings folders, and
  Quit.
- Combat Meter (a plugin): a parser for you and your companion (SWTOR's log only records your own fights, so other group
  members don't appear). Pages: Damage, Healing (effective, with overheal), Tank (damage taken, absorbed,
  avoided, deaths) and Threat. Click a row to see its abilities; hover anything for the details. Click the
  encounter name for earlier encounters. Right-click the meter (or its menu button) for Compact view, Hide
  names (shows your discipline instead, for screenshots), Split encounter and Clear this session. Compact view
  shrinks it to one line per combatant; turning it off gives back your size.
- Tactical Readout (a plugin): boss timelines and alerts, with optional text-to-speech. Open its settings from
  the Ashla window's Panels list. It needs a timeline for each boss; Make a Timeline builds a starter one from a
  saved fight.
- Saved fights: every fight is saved on this PC when it ends. With the Combat Meter, click the encounter name to
  see this session's fights and, below them, saved fights by day; pick one to look at it again. To add fights
  from an older combat log (for example from before you used Ashla), use "Open combat log..." in the Ashla
  window or in Settings > Fights.


Plugins
-------
First-run setup and Settings > Plugins > Browse list the plugins in Holocron, Ashla's official plugin list. Every plugin there
has public source code and was reviewed before it was listed. Press Install; Ashla downloads it, checks that
it's exactly the file Holocron reviewed, and it starts the next time you press Play.

- Updates: when Ashla starts, it checks Holocron for newer versions of your plugins. The launcher says
  "Update available" and Settings > Plugins > Installed has an Update button. If an update comes out while
  you play, a notification tells you to install it the next time you start Ashla.
- Remove deletes a plugin's files; its settings stay in case you install it again.
- Other repositories: someone can share their own plugin list with you (a web address ending in repo.json).
  Add it under Settings > Plugins > Repositories. Plugins from those lists are marked "Not reviewed by
  Holocron" and Ashla asks before installing one. A plugin runs with the same access to your PC as Ashla
  itself, so only add lists from people you trust.
- Plugins you copy into the plugins folder yourself still work; they show as "Added by hand".

Network use: Ashla only goes online to read plugin lists and download the plugins you install. It sends
nothing about you or your game; the requests are ordinary downloads from GitHub (or the repositories you
added). Turn off "Check for plugin updates when Ashla starts" in Settings > Plugins > Installed, and Ashla
only goes online when you open Browse or press Install or Check for updates.


Windows "protected your PC" warning
-----------------------------------
Ashla isn't code-signed, so Windows SmartScreen may warn the first time you run it. If you got it from a
source you trust, choose "More info" > "Run anyway". Some antivirus tools are wary of overlay programs in
general; Ashla's source code is public if you want to check what it does.


Troubleshooting
---------------
- "... wasn't loaded" with "files don't match manifest.json" or "No manifest.json": the plugin's folder is
  incomplete or its files were changed. Get the plugin again from where you found it and replace the whole
  folder.
- "Couldn't reach Holocron. Showing the list from ...": you're offline or GitHub didn't answer. Browse shows
  the last list Ashla saved; installing needs a connection.
- "... doesn't match Holocron's checksum" or "... files don't match the hashes Holocron published": the
  download was damaged or isn't the reviewed file, so it wasn't installed. Try again later.
- Nothing appears over the game: make sure SWTOR is in Fullscreen (Windowed) or Windowed mode, and that
  Ashla is running (tray icon). The log (tray icon > Open log file) says whether it found the game window.
- The hotkey does nothing: another program may already use it (Ashla says so with a notification when it
  starts). Pick a different one in Settings > Overlay.
- The meter says "Waiting for combat": combat logging is probably off (step 2). Ashla also warns about this
  in its window and with a notification.
- Requires Windows 10 or 11 (64-bit) and a graphics driver with OpenGL 3.3 (any current NVIDIA, AMD or
  Intel driver).


Files
-----
- Settings and window positions: %APPDATA%\Ashla (settings.json; plugin settings in pluginConfigs)
- Logs (kept 7 days):            %LOCALAPPDATA%\Ashla\logs
- Plugins:                       the "plugins" folder next to Ashla.exe
- Where installed plugins came from: %APPDATA%\Ashla\installed-plugins.json
- Last saved plugin lists:         %LOCALAPPDATA%\Ashla\holocron

To uninstall, quit Ashla, delete its folder, and optionally delete %APPDATA%\Ashla and %LOCALAPPDATA%\Ashla.


License and source code
-----------------------
Ashla is free software, licensed under the GNU Affero General Public License v3.0 (LICENSE.txt).
Source code: https://github.com/YozoraCho/Ashla
Licenses of the libraries Ashla includes are in THIRD-PARTY-NOTICES.txt.
