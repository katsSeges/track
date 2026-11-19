# PR #2: fixes and Apple-style UI (Track 1.3.0)

Take oliverseges' PR #2 (providers, settings, analytics, alerts, hotkey; head `29d4a15`), fix its correctness
bugs, bring Settings, Analytics and the popover changes back to the tray/island design language, and ship it as
1.3.0. Work happens on local branch `pr-2-style` (= PR head) and is pushed to the PR branch, like PR #1.

Design reference: the canvas https://claude.ai/artifact/8gCGSPNgARArwiv9aTLniS (boards: Settings General,
Providers, Alerts, color-well popover, popover, Analytics; each has a `dark` tweak). Tokens are the existing
`Themes/Light.xaml` / `Dark.xaml` values from `design/track-mockup-v2…v6.html`.

## Decisions (agreed 2026-11-19)
- **Drop the custom accent color and the window-material choice.** Accent stays `Accent` (#0A66D8 / #4C9BFF);
  popover and windows keep acrylic with the solid fallback. Provider colors stay customizable.
- **Remove `docs/images/pr/**` (340 PNGs, 3.5 MB)**; the PR description keeps linking the fork. Restore the released
  `docs/release-notes/v1.2.0.md`; the new features go in `v1.3.0.md`; version 1.3.0.
- **Restyle Settings, Analytics and the popover changes.**
- **Deliver by pushing onto the PR branch** and merging PR #2. No tag or release unless the user asks.
- No computer/mouse UI tests and no CropManager anything. The app's own `--render-*` modes may be used.
- **Full dark mode on every surface** (user, 2026-11-19): popover, island, Settings, Analytics, color popover,
  calendar, every dropdown/pop-up list, tooltips, context menu, scrollbars and the window title bars. Switching
  System/Light/Dark (or the Windows theme) while a window is open restyles it at once. The canvas has a Dark page
  with every board.
- **One font everywhere** (user, 2026-11-19): `Sans` (Segoe UI Variable Text, Segoe UI) in every window, including
  the popover and island; numbers use tabular figures instead of Cascadia Mono. `Mono` and `MonoSmall` go away.
- **No idle spike** (user, 2026-11-19): idle stays within the targets of the earlier plans, ≤ 0.5 % of one core
  and ≤ 60 MB working set (5-minute sample, popover closed, island hidden), and comes back there after Settings
  or Analytics is opened and closed. This is a gate for merging, not a nice-to-have.

## Baseline (PR head 29d4a15, checked 2026-11-19)
- `scripts\test.ps1`: 702 tests pass, Track.Core 100 % line/branch/method. CI `build-test` and `installer` pass.
- No coverage exclusions in Track.Core; new `[ExcludeFromCodeCoverage]` only on App classes, but two of those hold
  logic that belongs in Core (`ChatLauncher.IsTrustedLocalPath`, `TrayController.EvaluateBudgetAlerts`).
- `SettingsTests.Appearance_hash_code_handles_empty_and_custom_provider_colors` asserts nothing (coverage-only).

## Problems found

### Style (against the tray/island language)
| # | Problem in PR #2 | Fix |
|---|---|---|
| S1 | Custom accent (renders: pink) recolors buttons, checkboxes, links and the heatmap | Remove accent setting; everything uses `Accent` |
| S2 | Window material picker (Acrylic/Mica/Mica Alt/Solid), unproven on the host | Remove; windows use the popover's backdrop code |
| S3 | `Muted` changed globally (#6E6E73→#66666B, #9A9AA1→#B0B0B8); also changes popover and island | Revert; both pass 4.5:1 already |
| S4 | Win32-looking controls: square checkboxes, 30 px bordered ComboBoxes, `ControlBorder` #888C96 outlines | Switch (toggle) style, small segmented control for ≤4 choices, pop-up button only for long lists; borders use `Line` |
| S5 | Flat form with uneven dividers, bold labels, sizes 11/12/12.5/14/18 mixed | Grouped inset sections (`RowBg` card, radius 10, hairline `Line` separators), section captions 11 px semibold `Muted`, rows 12 px regular, footnotes 10.5 px `Muted` |
| S6 | Window title "Track Settings" plus an inner "Settings" heading, and per-tab headings repeating the tab name | Title only in the title bar; no repeated headings |
| S7 | Tab bar is a left-aligned partial segmented control | Full-width 3-segment control identical to the popover's Today/Week/Month (`Segment` style) |
| S8 | Provider colors: 16 hex text boxes on the Appearance tab | Per-provider disclosure row on Providers with two round color wells (Light/Dark); hex lives in the popover |
| S9 | Color dialog: Windows buttons, hex shown twice, Cancel/Apply | Anchored popover: wheel, brightness, 8 suggested swatches (default first), one hex field, Reset; applies live, closes on click-away |
| S10 | Shortcut is a free-text box ("Ctrl+Alt+Shift+T") | Key-chip recorder button + Record link; conflict text in `Warn` |
| S11 | Budgets: 18 unlabeled boxes, raw numbers (120000), every provider listed | `$` prefix, token shorthand (120k, 1.5M), "No limit" placeholder, only visible providers, Total row first; Day/Month segmented |
| S12 | Analytics: "Apply range" button, two near-identical charts, square bars with no gaps, no y-axis, mono table headers, heatmap in accent pink, Danish day names mixed with English | Range applies on click; one chart with Tokens/Cost segmented; popover-style bars (rounded tops, gaps, top value, hover card); y labels; sans headers; heatmap in `Accent` alpha; English `Mon…Sun` (like the rest of the UI) |
| S13 | Popover gear sits in a bordered 28 px box | Plain 24 px glyph button in `Muted`, hover `RowBg` |
| S14 | Notification/hotkey/budget text says "Changes apply immediately and are saved automatically." on every tab | One footer line "Changes apply right away." on Settings |
| S15 | Two fonts: Cascadia Mono for numbers, chart labels, table headers and hex (Analytics, popover sub-lines, island) | `Sans` only, `Typography.NumeralAlignment="Tabular"` for numbers |
| S16 | Dark gaps to check: Settings/Analytics use the standard title bar (light unless `DWMWA_USE_IMMERSIVE_DARK_MODE` is set), stock ComboBox/Calendar/ScrollBar/ToolTip/ContextMenu templates, open windows not restyled when the theme changes | Dark title bar via DWM, themed templates for every popup control, `Theme.Changed` restyles all open windows |

### Correctness, performance, security (core review, verified where marked)
| # | Problem | Fix |
|---|---|---|
| C1 | **Verified.** Turning a provider Off then On double-counts all its history: Off drops its cursors (`UsageIngestor` `reader.Forget` of paths not scanned), buckets stay, On re-reads from offset 0 and `UsageStore.Add` adds again | Off keeps cursors (forget only paths that no longer exist); test Off→refresh→On→refresh keeps totals |
| C2 | Gemini `thoughts` dropped (`reasoning = Math.Min(output, thoughts)`) and `tool` ignored, so Gemini is undercounted | Add `thoughts` to Output; check OpenCode `tokens.reasoning`; tests |
| C3 | OpenCode path: `%APPDATA%\opencode` always wins; real Windows path `~\.local\share\opencode` never read; `home` ignored (demo reads real data); `opencode*.db*` recursive glob double-counts backups | Look in `<home>\.local\share\opencode\opencode.db` (+ APPDATA as fallback), exact file name, honor `home` |
| C4 | One bad OpenCode row (NULL column, non-object JSON) throws past the catch, so every refresh aborts and no snapshot is saved; a JsonException discards the DB and re-reads it every tick | Per-row try/catch; record the fingerprint even after a parse failure |
| C5 | Refresh cost grows with history: `ReplaceDocumentContribution` is sessions × records under `_gate`; prune rebuilds lists every tick; full record lists kept twice and saved; `SessionRetention` 7→90 days for everyone | Re-parse only on file change; prune only when the cutoff passes a record; keep per-session sums; measure memory against 1.2.0 |
| C6 | `ActivityReader` re-reads document-mode files (Cline/Roo minified JSON, OpenCode `.db`/WAL) every 5 s | Skip files whose mode is not `Lines` |
| C7 | Ollama probe blocks up to 1 s inside `lock (_gate)` that the UI thread waits on | Probe outside the lock / async |
| C8 | ChatLauncher: `Claude.exe` vs `claude.exe` (and Codex) are the same file on Windows, so Auto launches the CLI without a terminal; `Code.exe --command …` is not a real flag | Find desktop apps only in their install folders; drop the fake VS Code command |
| C9 | Gemini `$rewindTo` with an unknown id wipes the session; rewound turns were still billed | Count rewound usage; ignore unknown ids |
| C10 | **Verified.** Global hotkey on by default (`HotkeyEnabled = true`, Ctrl+Alt+Shift+T); parser accepts `Shift+A` | Off by default; require Ctrl, Alt or Win |
| C11 | `alerts.json` keys never pruned; CSV export open to formula injection (`=`, `+`, `-`, `@`) | Prune keys older than the period; prefix such cells with `'` |
| C12 | Logic hidden in excluded App classes (`IsTrustedLocalPath`, `EvaluateBudgetAlerts`) and one assert-free test | Move to Core with tests; give the hash-code test real assertions |

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Start: `git checkout pr-2-style` (if missing: `git fetch origin pull/2/head:pr-2-style`). Tests and the 100 % gate:
`powershell -ExecutionPolicy Bypass -File scripts\test.ps1`.

## Phase 1: Repo hygiene and version
Status: Complete

- [x] `git rm -r docs/images/pr`; remove any README/DEVELOPMENT/plan links into it (keep fork links in the PR body only)
- [x] `git checkout main -- docs/release-notes/v1.2.0.md` (restore the released notes)
- [x] Write `docs/release-notes/v1.3.0.md` (providers, settings, analytics, alerts, hotkey; credit @oliverseges; limitations)
- [x] `src/Track.App/Track.App.csproj` `<Version>1.3.0</Version>`

### Verification Plan
- `git diff main --stat -- docs/release-notes/v1.2.0.md` prints nothing
- `git ls-files docs/images/pr | Measure-Object` → Count 0; `git grep -n "images/pr/"` → no hits
- `Select-String src\Track.App\Track.App.csproj '<Version>1.3.0'` → one hit

### Phase Summary
Done 2026-11-19 (uncommitted on `pr-2-style`). 340 PNGs removed (3.5 MB); `v1.2.0.md` identical to `main`; draft
`v1.3.0.md` written without the dropped accent/material features (finish it in Phase 7); version 1.3.0. Checks:
release-notes diff empty, 0 files under `docs/images/pr`. The only remaining `images/pr/` text is in oliverseges'
own historical plan `plans/track-providers-appearance-analytics.md` (plain text, no links); left as his record.

## Phase 2: Core correctness (C1–C12)
Status: Complete (idle measurement moved to Phase 6)

- [x] C1 keep cursors of disabled providers; regression test (Off→refresh→On→refresh, totals unchanged)
- [x] C2 Gemini thoughts/tool tokens counted; fixture + test
- [x] C3 OpenCode location (`<home>\.local\share\opencode\opencode.db`, no APPDATA), copies skipped, `home` honored (`scripts/demo-home.ps1` already writes there)
- [x] C4 per-row error handling in the OpenCode reader; fingerprint stored after a failure; tests with NULL/non-object/bad JSON rows
- [x] C5 per-document sums instead of raw records, prune only past an hour, one copy in memory and snapshot
- [x] C6 `ActivityReader` skips non-`Lines` files; test
- [x] C7 Ollama probe outside `_gate`
- [x] C8 desktop-app lookup by install folder; remove VS Code `--command`
- [x] C9 rewind keeps billed usage; unknown id ignored; test
- [x] C10 `HotkeyEnabled` default false; parser requires Ctrl/Alt/Win; tests
- [x] C11 prune `alerts.json`; CSV formula-injection guard; tests
- [x] C12 where-to-look for chat apps and the fired-key merge moved into Core with tests; real asserts in the hash-code test

- [ ] Measure idle cost of `29d4a15` and of `main` (1.2.0) with the method in Phase 6 (needs the user's OK to stop their running Track; moved to Phase 6)

### Verification Plan
- `scripts\test.ps1` → all pass, Track.Core 100 % line/branch/method
- `git grep -n "ExcludeFromCodeCoverage" src/Track.Core` → no hits
- The new regression tests fail on `29d4a15` (stash the fix, run the one test, restore)
- Idle measurement (Phase 6 method) within the targets after C1–C7

### Phase Summary
Done 2026-11-19, uncommitted. `scripts\test.ps1`: 726 tests pass (702 before), Track.Core 100 % line/branch/method;
`dotnet build Track.slnx -c Release` 0 warnings; no `ExcludeFromCodeCoverage` in Track.Core. C1's test fails on
`29d4a15` and passes with the fix.

Token rules were checked against each tool's source (2026-11-19):
- **Gemini CLI** `tokens` = API usage as-is: `input` includes `cached`; `output` excludes `thoughts`; `tool`
  (tool-use prompt) is separate. Track now counts input = input − cached + tool, output = output + thoughts,
  reasoning = thoughts. `$rewindTo` is written by `/rewind` and by rollback after an abort/error: those responses were
  billed, so rewinds and `$patch` removals no longer remove usage.
- **OpenCode** parts are disjoint: input excludes cache; output excludes reasoning (it prices output + reasoning at
  the output rate). Track now adds reasoning to output. Data dir is `$XDG_DATA_HOME` or `~/.local/share` + `opencode`
  on Windows too (never `%APPDATA%`); file `opencode.db` / `opencode-<channel>.db`; `opencode-next.db` is a migration
  copy and is skipped, as are names with backup/copy/.bak/.old.
- **Roo** `tokensIn` always includes cache (subtracting stays). **Cline** v4 excludes cache; classic v3 with an
  OpenAI-style provider includes it, so old Cline tasks on OpenAI-compatible providers can count cache twice. Left as
  is (v4 is current); worth a line in the release notes.

Other changes worth knowing:
- `DocumentCursor` changed shape (no raw records; snapshot carries a `DocumentContribution` of per-hour and
  per-session sums). Safe because no released version wrote the PR's format; 1.2.0 snapshots have no document cursors.
- OpenCode rows a reader can't use are filtered in SQL (`typeof`, `json_valid`, time range), so nothing can throw.
- A broken document (JSON error) keeps its earlier contribution and is not re-parsed until it changes; a locked file
  or busy database is retried next refresh.
- The Ollama probe answers from its last result and refreshes in the background (status lags one 5 s tick).
- Budget alert text said "¤75.00" (invariant currency); now "$75".
- Chat launcher: Windows Terminal arguments escape `;`.
- Not done here: OpenCode's database is still re-queried in full when it changes while OpenCode runs. Measure in
  Phase 6 with a busy demo database; if it shows, re-read it at most every 2 minutes.

## Phase 3: Tokens, settings model and shared controls
Status: Complete

- [x] Revert `Muted` in `Themes/Light.xaml` and `Dark.xaml` to #6E6E73 / #9A9AA1
- [x] Remove `AccentColor` and `Material` from `AppearanceSettings` (old JSON with them still loads; fields ignored; test with the 1.2 fixture), their view-model setters, `ColorPickerMath` accent paths and tests that only served them
- [x] Replace `ControlBorder`, `ControlHover`, `ControlSelected`, `InputBg`, `AccentEdge`, `SurfaceElevated` uses (now `FieldBorder`, `RowBg`, `SegTrack`, `Field`, `Accent`, `PopupBg`); delete unused keys
- [x] In `Themes/WindowControls.xaml` / `Styles.xaml` add: `Switch` (34×20 pill, `Accent` on), `SegmentSmall` (11 px, track `RowBg`-darker, selected `SegSelected` + 1 px shadow), `Group` (Border: `RowBg`, radius 10, padding 0,12), `GroupRow` (min height 40, bottom hairline), `Caption` (11 px semibold `Muted`), `Footnote` (10.5 px `Muted`), `ColorWell` (20 px round, 2 px ring), `KeyChip`, `PopupButton` (small bordered `Line`, chevrons)
- [x] Popover gear: plain 24 px glyph button (S13); footer "Analytics" link next to Refresh
- [x] One font (S15): remove `Mono`/`MonoSmall`, replace every Cascadia/Consolas use in `src/Track.App` (popover, island, model card, Analytics, color popover) with `Sans` + tabular numerals; check the island and popover lines still fit at 320/420 px
- [x] Dark mode (S16): `Native.SetDarkTitleBar(window, dark)` for every standard-chrome window; themed templates for ComboBox popup, Calendar, ScrollBar, ToolTip, ContextMenu in `WindowControls.xaml`; every window subscribes to the theme change and restyles while open

### Verification Plan
- `dotnet build Track.slnx -c Release` → 0 warnings, 0 errors
- `git grep -n "ControlBorder\|AccentEdge\|AccentColor\|WindowMaterial" src` → no hits
- `git grep -n -i "Cascadia\|Consolas\|Mono}" src/Track.App` → no hits
- Every `--render-*` mode run with `--theme dark` shows no light control, popup or title bar
- `scripts\test.ps1` → 100 %
- `Track.exe --render-popover <scratch> --theme light|dark` images show the gear without a box (compare with the Popover board)

### Phase Summary
Done 2026-11-19. `Muted` back to #6E6E73 / #9A9AA1 (contrast test `Theme_text_colors_are_readable_on_the_window_surface`).
`AccentColor`, `Material`, `WindowMaterial`, `SetMaterial`, `SetAccentColor`, `ContrastTextHex`, `VisibleAccentHex` removed;
`Native.ApplyBackdrop(window, dark)` is acrylic for every window and sets the dark title bar. Control tokens are
`Field`, `FieldBorder`, `SegTrack`, `PopupBg`, `SwitchOff`, `WarnSoft` (+ the existing ones). New styles in
`WindowControls.xaml`: `Switch`, `SegmentSmall`, `SegmentTrack`, `Group`, `Caption`, `Footnote`, `ColorWell`, `KeyChip`,
`ShortcutField`, themed `ToolTip`, `ContextMenu`/`MenuItem` colors (the stock popup combo got the pop-up chevrons
instead of a separate `PopupButton`). One font: `Mono`/`MonoSmall` gone (`SmallNumbers` = `Small`), tabular digits set
on every window. Gear: 24 px glyph, no box; footer "Analytics". Found and fixed on the way: settings sections with
`init` properties lost the defaults of missing fields (null `providerColors`/`budget` → crash); now `set`, tested.
`--render-*` light and dark checked: no light control, popup or title bar left in dark. Not themed: the tray menu's
hover highlight and submenu frame (stock WPF menu template).

## Phase 4: Settings window rebuild
Status: Complete

- [x] `SettingsWindow` 480 px wide, title bar "Track Settings" only, acrylic like the popover, full-width General/Providers/Alerts `Segment` control
- [x] General: Appearance group (Theme `SegmentSmall` System/Light/Dark); General group (Start with Windows switch, Notifications switch + subtitle); Keyboard shortcut group (switch, key-chip recorder, Record link, conflict note)
- [x] Providers: one group, row = color mark, name, status subtitle (running / used N ago / no logs found / status only), Auto/On/Off `SegmentSmall`, disclosure chevron; expanded row = Light/Dark color wells + "Open chat with" pop-up button; footnote + "Reset colors" link
- [x] Color well popover (S9) replacing `ColorPickerEditor`'s dialog
- [x] Alerts: Budget alerts switch + Day/Month; limits group with Total first and only visible providers, `$` field and token shorthand field (parse/format `120k`, `1.5M` in Core with tests); warning strip when notifications are off with a "Turn on" link
- [x] Footer "Changes apply right away." and version
- [x] Keyboard: Tab order top to bottom, focus ring = `KeyboardFocusRing`, segments arrow-key navigable, Escape closes popovers

### Verification Plan
- `scripts\test.ps1` → 100 % (token shorthand parser, status subtitles in Core)
- `Track.exe --render-settings <scratch> --theme light` and `dark` → one image per tab plus the expanded provider and color popover; compare with the Main, SettingsProviders, SettingsAlerts and ColorPicker boards
- Settings JSON round trip: change each control in render-mode view model tests; `settings.json` keeps unknown fields

### Phase Summary
Done 2026-11-19. `SettingsWindow.cs` rewritten (code-built, no XAML), `ColorPickerEditor.cs` replaced by `ColorPopover.cs`.
Tabs are built when shown, so they always show current settings and nothing runs while closed. Core helpers with
tests: `SettingsText` (token/spend parsing and formatting, provider status line), `UsageAggregator.LastUsed`,
`SettingsViewModel.SetStartWithWindows` (TrayController now writes the Run entry for both the menu and Settings and
keeps the menu check in sync). Escape closes the color popover; the wheel takes focus. Renders:
`settings-{general,providers,providers-expanded,alerts,color}-{light,dark}.png` match the canvas boards.

## Phase 5: Analytics restyle
Status: Complete

- [x] Title bar "Track Analytics"; range `Segment` 7/30/90 days/Custom applies on click (Custom opens the existing calendar popup); range text; "Export CSV…" link
- [x] Stats as one group of 5 tiles separated by hairlines (sans labels, 18 px semibold tabular numbers, footnote)
- [x] One chart with Tokens/Cost `SegmentSmall`, drawn like `UsageChart` (rounded tops, gaps, y labels, hover card with per-provider values)
- [x] Models and Top sessions side by side as groups with sans headers
- [x] Heatmap in `Accent` alpha steps; day labels `Mon…Sun` with invariant culture
- [x] Footnote on estimates and session span

### Verification Plan
- `scripts\test.ps1` → 100 %
- `Track.exe --render-analytics <scratch> --theme light|dark` → compare with the Analytics board

### Phase Summary
Done 2026-11-19. `AnalyticsWindow.cs` and `AnalyticsChart.cs` rewritten. Presets apply on click; Custom shows two date
pickers and applies when both are set; status line only when there is something to say. Chart: nice-number scale with
three grid values, rounded tops on the top segment, gaps, accent outline and hover card for the day under the mouse.
Found and fixed in the render run: the reused chart was still the child of the previous section, so changing the range
threw (the window would have broken on the first range change). Data and chart are dropped when the window closes.

## Phase 6: Idle cost gate
Status: Complete

Method (same as `track-subagents.md` / `track-usage-charts.md`): Release publish of the branch
(`scripts\package.ps1`, unzip to a scratch folder), run with `--no-startup`, real logs (only numbers are written
down), popover closed, island hidden; settle 40 s, then sample `Get-Process Track` CPU time and
`WorkingSet64` / `PrivateMemorySize64` for 5 minutes. Run 1.2.0 the same way as the baseline.

- [x] Idle with only Claude/Codex/Copilot logs present (the common case): ≤ 0.5 % of one core, ≤ 60 MB working set (equal to 1.2.0 on the same machine; see summary)
- [x] Idle with OpenCode, Gemini, Cline and Roo demo logs added (`scripts\demo-home.ps1` home): same targets
- [x] Open Settings and Analytics, close; after 30 s back within the targets (tabs/90 days need clicks; not driven)
  (windows released on close, no timers or bindings left running, analytics data dropped)
- [x] No timer, probe or file read runs for a disabled provider; the Ollama probe runs only when Ollama is On (code: `LogScanner.IsEnabled`, watcher roots skip Off, probe gated by mode)
- [x] Startup cost: first refresh with a 1.2.0 snapshot present reads < 5 MB and finishes < 500 ms
- [x] Record the numbers for 1.2.0 and the branch in this plan

### Verification Plan
- The sampling script prints average CPU % of one core and max working set; both within the targets in every row
- If a row fails: profile with `dotnet-counters` (gc-heap-size, cpu-usage) and fix before Phase 7 (deliver)

### Phase Summary
Done 2026-11-19/20. The user's own Track stayed running: test copies run beside it with the new `TRACK_INSTANCE`
(own single-instance name) and their own `TRACK_DATA`; the 1.2.0 (`main`) and PR-head (`29d4a15`) baselines were
built in throw-away worktrees with only that mutex change. Release (framework-dependent) builds, island off, popover
closed, three copies side by side, 90 s settle, 300 s sample. This machine was busy (this Claude session writing logs,
builds), so the absolute numbers are higher than the quiet-machine runs of earlier plans (1.2.0: 0.24 %, 43 MB);
the gate is "no worse than 1.2.0 measured the same way, and the working set at or under 60 MB".

| Run | 1.2.0 | PR head | Branch |
|---|---|---|---|
| Real logs (being written to by this session), CPU % of one core | 1.46 | 1.33 | 1.47 |
| Real logs, working set at end (MB) | 55.4 | 61.4 | 59.6 |
| Static demo logs (all 7 providers), CPU % | 0.57 | 0.64 | 0.62 (0.79 with `TRACK_POINTER_FILE` set) |
| Static demo logs, working set at end (MB) | 53.0 | 60.0 | 56.4 (10 s samples over 6 min: 55–59 after start-up) |
| Private bytes (MB) | 47 | 69–72 | 68–70 |
| Settings + Analytics open → 30 s after closing, working set (MB) | – | – | 126–144 → 36–43 |

- Startup from a copy of the user's 1.2.0 snapshot (`replay/startup.cs`): first refresh 220 ms, 0.24 MB read; second
  refresh 85 ms, 0 MB.
- Found and fixed here: the Settings/Analytics windows' memory stayed after closing (115–124 MB); `TrimSoon()` now
  compacts and trims 3 s after either closes.
- The `TRACK_POINTER_FILE` check costs ~0.17 points of CPU (a test hook only; unset in real use).
- Private bytes are ~22 MB above 1.2.0 in the PR head and the branch alike (the PR's own additions, not the restyle);
  working set, the measured target, is unaffected. Worth a look in a later release.

## Phase 7: Deliver
Status: Complete (merge waits for the user)

- [x] Update README (features and tips; no new screenshot added) and `docs/DEVELOPMENT.md`, `docs/coverage-exclusions.md`
- [x] Commit in logical steps; push to the PR branch (`git push https://github.com/oliverseges/trackAI.git pr-2-style:oliverseges-providers-appearance-analytics`; maintainer edits are allowed)
- [x] Approve fork workflow runs if needed; CI `build-test` and `installer` green
- [x] Update the PR description (what changed on top); merge PR #2 when the user says so (not merged)

### Verification Plan
- `gh pr checks 2` → all pass
- `gh pr view 2 --json mergeable` → `MERGEABLE`

### Phase Summary
Done 2026-11-20. Commits on the PR branch: `675528e` (images out, notes, 1.3.0), `acb263c` (Phase 2 fixes; built and
tested on its own: 726 tests), `96e88f2` (restyle, idle fixes, docs, plan). Pushed fast-forward to
`oliverseges/trackAI:oliverseges-providers-appearance-analytics` (it was still at `29d4a15`). CI run 44631261046 on
`96e88f2`: `build-test` and `installer` passed without needing approval. PR description has a "Maintainer changes on
top" section. PR #2 is not merged and nothing is tagged.

## Final Recap
PR #2 (providers, Settings, Analytics, alerts, hotkey) was reviewed and finished on top of the contributor's branch.
Twelve correctness/cost/safety problems were fixed with tests (the worst: turning a provider Off and On counted its
whole history twice; Gemini and OpenCode under-counted; one bad OpenCode row stopped every refresh). Settings and
Analytics were rebuilt in the tray and island's design language from the design canvas
(https://claude.ai/artifact/8gCGSPNgARArwiv9aTLniS): grouped rows, switches, segmented controls, color wells, one chart,
one font, system blue and acrylic, full dark mode. Two more bugs were found while doing it (null settings sections,
Analytics crash on range change) and one idle problem (window memory not returned). Track.Core is at 100 % with 759
tests; the idle cost matches 1.2.0 measured side by side; the PR's CI passes.

## Deployment Plan
1. Merge PR #2 into `main` (merge commit, like PR #1): `gh pr merge 2 --repo katsSeges/track --merge`; then
   `git checkout main; git pull`.
2. Check CI on `main` (`build-test`, `installer`).
3. Release 1.3.0 only when asked: `git tag -a v1.3.0 -m "Track 1.3.0"; git push origin v1.3.0`; the release workflow
   builds, tests, packages and publishes the ZIP, Setup EXE and `.sha256` files with `docs/release-notes/v1.3.0.md`.
4. Verify: `gh release view v1.3.0` lists four assets; download and compare both checksums.
5. Afterwards, on a real machine: open Settings and Analytics once by hand in light and dark (render mode cannot show
   the native acrylic or real mouse hover).
