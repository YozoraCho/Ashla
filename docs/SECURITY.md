# Security

## Reporting a problem in Ashla

Please don't open a public issue for security problems. Report them privately through GitHub:
**Security** tab of this repository → **Report a vulnerability**.

Include what you found, how to reproduce it, and which Ashla version you used. You'll get a reply as soon as
possible, and a fix will be released before the details are made public.

Examples of what to report:

- a way for a crafted combat-log file to crash Ashla or run code,
- a way for one plugin to interfere with another plugin or with Ashla's settings beyond what's documented,
- a problem in how Ashla downloads or verifies plugins or updates.

## Reporting a malicious plugin

Plugins are ordinary programs with the same access to your computer as any app you run. Ashla can't sandbox
them. If a plugin listed in [Holocron](https://github.com/YozoraCho/Holocron) behaves maliciously (steals
data, damages files, breaks Ashla's rules such as touching the game process), report it the same way, or open
an issue in Holocron if it isn't sensitive. Listed plugins found to be malicious are removed.

Only install plugins from sources you trust.

## Supported versions

Only the latest release receives fixes.
