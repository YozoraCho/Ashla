# Contributing to Ashla

Thanks for helping. Bug reports, ideas, documentation fixes and code are all welcome.

## The rules every change follows

Ashla stays outside the game. A change can't be accepted if it:

- loads anything into the SWTOR process,
- reads or writes the game's memory,
- hooks game functions or intercepts network traffic,
- sends keystrokes, clicks or other input to the game, or automates gameplay,
- shows data that isn't actually in the combat log (no guessing, estimating or filling gaps).

If a feature seems to need one of these, open an issue first so we can look for an external way to do it.

## Before you start

- For anything bigger than a small fix, open an issue describing what you want to change. It saves you from
  building something that can't be merged.
- Plugin submissions don't go here: they go to [Holocron](https://github.com/YozoraCho/Holocron).
- Setup, build commands and the host's options are in [building.md](building.md).

## Pull requests

- `dotnet build Ashla.slnx` must finish with **0 warnings** (warnings are errors) and `dotnet test Ashla.slnx`
  must pass. GitHub Actions runs both on every pull request.
- Add or update tests for behavior you change. Parser changes need a test with a real log line.
- Real combat-log samples must be anonymized: replace character names and player ids, as in `tests/combat-logs/`.
  Keep the file's original bytes (Windows-1252, CRLF); `.gitattributes` already protects `tests/combat-logs/combat_*.txt`.
- Package versions go in `Directory.Packages.props`, not in project files.
- Update the docs when behavior or options change.
- Keep pull requests focused: one feature or fix each.

## Commit messages

Use `type(scope): summary`, with the summary in lowercase and no trailing period:

```text
feat(combatlog): parse shield absorbs on heals
fix(host): keep overlay hidden while the game is minimized
docs(plugins): explain panel appearance options
```

Types: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`.

Scopes: `sdk`, `combatlog`, `rendering`, `ui`, `host`, `overlay`, `plugins`, `tests`, `docs`, `scripts`,
`solution`, `repo`, `ci`. Pick the area most of the change is in.

## Licensing of contributions

Ashla is licensed under the [AGPL-3.0](../LICENSE). By submitting a contribution, you agree that it is
licensed under the AGPL-3.0 as part of Ashla, and that you have the right to submit it.

The license doesn't cover the Ashla name or logo; see "Name and logo" in the [README](../README.md#name-and-logo).
