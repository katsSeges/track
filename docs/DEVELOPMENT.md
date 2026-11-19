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
                    activity and sub-agents (island), settings, view-models, tray icon layout
src/Track.App       WPF tray shell: tray icon, popover, model card, island, Windows glue
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

`.github/workflows/release.yml` then builds, tests, installs Inno Setup 6, runs `scripts\package.ps1` and
publishes the ZIP, Setup EXE and a `.sha256` for each. No .NET runtime installation is needed for either download.

To package locally, install [Inno Setup 6](https://jrsoftware.org/isdl.php), then run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package.ps1
# If ISCC.exe is not on PATH or in the standard Inno Setup 6 folder:
powershell -ExecutionPolicy Bypass -File scripts\package.ps1 -IsccPath 'C:\Tools\Inno Setup 6\ISCC.exe'
```

Outputs are `artifacts\Track-<version>-win-x64.zip`, `artifacts\Track-<version>-win-x64-setup.exe` and their
checksums. `scripts\installer\Track.iss` defines the per-user installer: Start menu shortcut, optional desktop
shortcut and startup, finish-page launch, and an Apps & features uninstall entry. A running Track must be
closed before setup or uninstall continues (the app's existing single-instance mutex is checked).
The startup checkbox reflects the live Run entry on upgrades, rather than a stale previous installer selection.
Setup writes the same quoted `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value named `Track` as the tray
toggle and updates only `startWithWindows` in the saved settings, preserving other fields. Invalid settings JSON
stops installation with an error instead of discarding it. Startup entries pointing at another installation
are not removed when startup is unchecked or during uninstall. Uninstall always keeps settings and usage data.
`TRACK_DATA` is honored by setup, just as by the app. The Setup EXE is currently unsigned.

After packaging, run `scripts\test-installer.ps1 -IsccPath '<path-to-ISCC.exe>'` from a clean Windows test
account (no running Track or existing Track startup entry). It compiles the same installer with a unique
test AppId, uses temporary install/data folders and a separate Start menu group, and leaves startup disabled.
It checks invalid-settings refusal, fresh install, normal app launch (without `--no-startup`), running-app
protection, upgrade/settings preservation, shortcut/uninstall registration, and clean uninstall with data
retained. Test files and registration are cleaned up afterward. The release workflow runs this before publishing.

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
| `--render-icons <dir>`, `--render-popover <dir>`, `--render-island <dir>` | save PNGs and exit |
| `--no-startup` | leave the "start with Windows" entry alone |

The README images are rendered by Track itself (`--render-popover`, `--render-island`) from made-up demo logs
(`TRACK_HOME` pointing at a generated demo home), so no real usage is shown.

Logos are simplified stand-in marks; see `src/Track.App/Assets/logos/README.md` to use official ones.
