# Developing Track

## Build and test

```powershell
dotnet build Track.slnx -c Release
powershell -ExecutionPolicy Bypass -File scripts\test.ps1
```

`scripts\test.ps1` fails unless **Track.Core** has 100 % line, branch and method coverage. All logic lives in
Track.Core; the WPF shell (Track.App) only draws it. See [coverage-exclusions.md](coverage-exclusions.md).

To check the numbers against your own logs, `scripts\check-series.ps1` compares Track's per-day and per-hour
totals with an independent count (`scripts\independent_usage.py`) and with `ccusage`, and prints "All equal."
when they match. It needs Python and Node (for `npx ccusage`).

## Project layout

```
src/Track.Core      parsing, ingest + snapshot, pricing, periods, visibility, live status,
                    activity and sub-agents (island), settings, analytics, alerts, chat targets,
                    notifications and hotkey rules
src/Track.App       WPF tray shell: tray icon, popover, model card, island, Settings and Analytics windows, Windows glue
tests/              xUnit tests (coverage gate) and anonymized log fixtures
design/             the clickable mockups the UI was decided with (rounds 1–6)
plans/              the build plans with every phase's checks and results
docs/               coverage exclusions, release notes, README images
```

## Install from source

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Rebuild
```

Publishes a framework-dependent build (needs the .NET 10 Desktop Runtime), copies it to
`%LOCALAPPDATA%\Programs\Track`, sets it to start with Windows and starts it. `scripts\uninstall.ps1`
removes it (add `-RemoveData` to delete settings and the snapshot too).

## Releases

CI (`.github/workflows/ci.yml`) builds and runs the tests with the coverage gate on every push and pull request.

To release:

1. Set `<Version>` in `src/Track.App/Track.App.csproj`.
2. Add `docs/release-notes/v<version>.md`.
3. Push, wait for CI, then push a matching tag: `git tag v1.2.3; git push origin v1.2.3`.

`.github/workflows/release.yml` then builds, tests, installs Inno Setup, runs `scripts\package.ps1 -RequireInstaller`,
runs the installer test and publishes the ZIP, the Setup EXE and a `.sha256` for each. Neither needs .NET installed.
CI (`ci.yml`) runs the same packaging and installer test on every push and pull request, so a tag never meets them first.
Both install a pinned Inno Setup with `scripts\installer\Install-InnoSetup.ps1` (SHA-256 and signature checked).

Package locally:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package.ps1
```

This writes `artifacts\Track-<version>-win-x64.zip` and, when [Inno Setup 6](https://jrsoftware.org/isdl.php) is
installed, `artifacts\Track-<version>-win-x64-setup.exe`, each with a `.sha256`. Without Inno Setup it builds only the
ZIP and warns; `-RequireInstaller` makes that an error, and `-IsccPath <path>` points at a non-standard `ISCC.exe`.

**The installer** (`scripts\installer\Track.iss`) installs per user in `%LOCALAPPDATA%\Programs\Track` without admin
rights, with a Start menu shortcut, an optional desktop shortcut, a finish-page launch and an Apps & features entry.
- **Startup** is on for a new install, like the app's default. On an upgrade the live Run entry decides: Track used
  before without an entry means the user turned it off. `/TASKS` overrides. Setup writes the same quoted
  `HKCU\...\Run` value `Track` as the tray toggle and sets only `startWithWindows` in `settings.json` (other fields
  kept; invalid JSON stops Setup before installing). `TRACK_DATA` is honored.
- **A running Track** must be closed first (Setup checks the app's single-instance mutex; it never kills it).
- **Uninstall** keeps settings and usage data and removes only a startup entry pointing at its own install.
- The Setup EXE is not code-signed yet.

**Installer test:** after packaging, `scripts\test-installer.ps1 -IsccPath '<path-to-ISCC.exe>'` compiles the
installer with a unique test AppId, temporary install and data folders and its own Start menu group, and never
enables startup. It checks: invalid settings refused, fresh install, normal launch keeps startup off, a running Track
blocks Setup, upgrades keep settings (with and without `/TASKS`), shortcut and Apps & features entries, and a clean
uninstall that keeps data. Run it locally only in a clean Windows account (no running Track, no startup entry).

## Verification switches

For checking the app without clicking around:

| Switch | Effect |
|---|---|
| `TRACK_HOME=<dir>` | read logs from another home folder |
| `TRACK_DATA=<dir>` | keep settings, snapshot and prices elsewhere |
| `TRACK_DIAG=1` | write the current state to `<data>\diag.txt` (incl. `subagent:` lines) |
| `TRACK_POINTER_FILE=<file>` | drive the island with `x,y`, `click`, `minimize`, `fullscreen` lines, and the tabs and charts with `island:tab usage`, `island:period week`, `island:bar 17`, `popover:period month`, `popover:bar 29` |
| `TRACK_CLOCK_OFFSET=<seconds>` | with `TRACK_DIAG=1` only: run the app's clock this far ahead (or behind) to watch midnight, week and month rollover |
| `--open`, `--card <tool>`, `--theme light\|dark` | open the popover (and card) at start |
| `--render-icons <dir>`, `--render-popover <dir>`, `--render-island <dir>`, `--render-settings <dir>`, `--render-analytics <dir>` | save PNGs and exit without changing user settings, snapshots or alert state |
| `--no-startup` | leave the "start with Windows" entry alone |

Generate source-shaped fake provider logs and point both paths at temporary folders:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\demo-home.ps1 -Path "$env:TEMP\track-demo-home"
$env:TRACK_HOME = "$env:TEMP\track-demo-home"
$env:TRACK_DATA = "$env:TEMP\track-demo-data"
$env:APPDATA = "$env:TEMP\track-demo-home\AppData\Roaming"
$env:OPENCODE_DB = "$env:TEMP\track-demo-home\.local\share\opencode\opencode.db"
.\src\Track.App\bin\Release\net10.0-windows\Track.exe --render-popover "$env:TEMP\track-after" --theme light
```

The demo generator creates fabricated logs for Claude Code, Codex, Copilot, Gemini CLI, OpenCode, Cline and Roo
Code; OpenCode's SQLite fixture is created with Python's standard `sqlite3` module. It does not create Ollama usage
because Ollama has no token data. Track's own render switches produce illustrative PNGs using a solid fallback;
they do **not** establish native Acrylic or Mica behavior, which must be observed separately on a supported Windows
host. Render mode does not write settings, snapshots or budget-alert state. Always use temporary `TRACK_HOME` and
`TRACK_DATA` paths and stop any running Track instance before launching a render because the app is single-instance.
Do not automate real desktop clicks; use `TRACK_POINTER_FILE` and `TRACK_DIAG` for interaction checks.

Logos are simplified stand-in marks; see `src/Track.App/Assets/logos/README.md` to use official ones.
