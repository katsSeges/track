# Track — usage in the island and usage charts

Add the tray popover's usage (Today / Week / Month, tokens first, `$ · in · out · cache`, sorted) to the opened
top island as a **Usage** tab, and add small stacked bar charts (today by hour, week and month by day) to both
the island and the popover, with hover details, click-to-drill and a "vs last period" comparison. **Correct data
and text are the top requirement**: every number, label and tooltip is tested against hand-computed values,
cross-checked against the real logs and ccusage, and proven to roll over correctly at midnight, on Monday, on
the 1st and on daylight-saving days.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Context: this builds on `plans/track-tray-app.md` (all phases complete). Read its Agreed Scope and the Phase 3b,
5, 6, 8 summaries first: the store keeps **hourly UTC buckets per tool and model for 90 days**
(`UsageStore.Buckets`), `PeriodCalculator` gives local, DST-safe ranges, `UsageAggregator` sums and prices,
`PopoverViewModel` / `IslandViewModel` build the texts. Track.Core must stay at **100 % line, branch and method
coverage** (`scripts/test.ps1` fails otherwise). Verification that needs the screen uses real input when the
workstation is unlocked; simulations do not count as verified (lesson from the previous plan).

## Agreed Scope (source of truth for decisions)

Mockup round 5: `design/track-mockup-v5.html` (https://claude.ai/artifact/TW1B54NvjVNcHcDQSKQxpf), chosen 2026-11-18:

```json
{ "islandLayout": "tabs", "chartWhere": "both", "chartType": "stacked", "weekView": "days",
  "monthView": "days", "chartHeight": "medium", "hoverDetail": "full", "compare": "on", "sparkline": "bar" }
```

| Topic | Decision |
|---|---|
| Opened island | Two tabs: **Now** (running agents, as today) and **Usage** (Today/Week/Month tabs, chart, sorted rows, Total). The minimize button and all island behavior stay as they are. The island remembers the last tab |
| Chart where | In the island's Usage tab **and** in the tray popover, between the period tabs and the table |
| Chart style | Bars stacked by tool in the tool colors (largest part at the bottom of each bar); medium height (64 px); faint dashed top line and a base line; the top value shown as "top 200M" |
| Today | One bar per **local** hour 00–24 (23 or 25 bars on clock-change days); the current hour emphasized; future hours drawn as a faint dash |
| Week / Month | One bar per **local** day (Mon–Sun; 1st–last of the month); today emphasized; future days faint |
| Hover | Tooltip with time, total tokens, price and the per-tool split, e.g. `Tue 17 Nov · 84.1M tokens · $20.32` / `● Copilot 83.8M` / `● Codex 300k` |
| Click a bar | Drill down: the table and price lines show just that hour or day, the label above the table becomes that bucket (e.g. "Tue 17 Nov"); click the same bar again or change tab to go back |
| Compare | Under the chart: `+18 % vs last week at this time` (Today: vs yesterday; Week: vs last week; Month: vs last month), counted up to the same point in the previous period |
| Rows | Keep the share bar under each row (no sparkline) |
| Hide chart (tray) | User request 2026-11-18, **changed in review 2026-11-18**: a small round minimize button (− / +) on the chart's own header row, right of the top value (`top 100.0M`), not next to the tabs (it must never push the period tabs or the usage rows around). Minimized, only the header row (`Today by hour` + the + button) stays; the bars and the comparison line disappear and the popover is shorter. Hidden, the chart and the comparison line disappear and the popover is shorter; the choice is remembered across opens and restarts (`TrackSettings.PopoverChartHidden`). The island's Usage tab is not affected |
| Chart unit | All tokens, the same total as the big number (input + cache write + cache read + output) |
| Not in scope | A 90-day view; charts in the model hover card; a cost-unit chart; changing any earlier decision |
| Correctness | Bucket sums must equal the tab totals exactly; every visible text is asserted in tests; day/week/month rollover, DST days and midnight are tested; real per-day and per-hour totals are cross-checked against an independent computation from the raw logs and against ccusage |

Data facts to rely on (verified 2026-11-18): history from 2026-08-21, 58 days with usage, 437 hourly buckets;
today in Copenhagen (UTC+1, whole-hour offset) hourly buckets align with local hours. For half-hour time zones a
local hour can straddle two UTC buckets — the bucketing below handles that by assigning each UTC hour to the
local hour its **start** falls in, and the plan tests that rule.

## Phase 1: Usage series (correct buckets)
Status: Complete

- [x] `UsageSeries` in `src/Track.Core/Usage/`: `Series(Period period)` → `SeriesBucket { Index, StartUtc, EndUtc, Label, ShortLabel, IsFuture, IsCurrent, PerTool: IReadOnlyDictionary<Tool, UsageTotals>, Total: UsageTotals }`
- [x] Today: buckets are the local hours of today, built by walking local wall-clock hours from `PeriodCalculator` (23 on spring-forward day, 25 on fall-back day; the repeated hour labelled `02:00 (2nd)`)
- [x] Week: 7 local days Monday–Sunday; Month: every local day of the month (28–31)
- [x] Each UTC hour bucket from the store goes to exactly one series bucket: the one containing its `HourUtc` start; buckets outside the period are ignored
- [x] `IsCurrent` = the bucket containing now; `IsFuture` = starts after now
- [x] Labels: hours `14:00–15:00`, short `14`; days `Tue 17 Nov`, short `T` (week) / `17` (month); invariant culture, English day/month names
- [x] Tests: bucket counts for normal / spring / autumn days and for 28/29/30/31-day months; every store bucket lands in exactly one series bucket; **sum of buckets == `UsageAggregator.Totals` for the period, per tool and per token part**; current/future flags at 23:59:59 and 00:00:00; a half-hour time zone (UTC+05:30) assigns each UTC hour by its start; labels for every kind
- [x] Real-data cross-check script `scripts/check-series.ps1` (+ a small file-based C# helper under `scripts/`): prints Track's per-day totals for the last 14 days and per-hour totals for today, next to (a) an independent Python computation straight from the raw logs (own parsing and dedupe, not Track's code) and (b) `npx ccusage@latest daily --since <14 days ago> --json` for Claude and Codex; reports any difference

### Verification Plan
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` → all pass, Track.Core 100 % line/branch/method
- `powershell -ExecutionPolicy Bypass -File scripts/check-series.ps1` → every day of the last 14 and every hour of today: Track = independent computation exactly; Claude and Codex per day = ccusage exactly (Copilot is not read by ccusage; compared only with the independent computation)

### Phase Summary
Done 2026-11-18. Verification results:
- `scripts/test.ps1` → **373 tests**, Track.Core 100 % line/branch/method.
- `scripts/check-series.ps1` (cutoff 20:00 UTC, days 4–17 Nov + today's hours): **Track = independent count for every day and every hour** (e.g. today 20:00 Claude 75,923,105; 12 Nov Copilot 161,331,907). Codex = ccusage exactly (17 Nov 322,199). Claude has no complete day before today in the logs (they start 18 Nov), so the Claude-vs-ccusage day check had nothing to compare this time; today's Claude hours equal the independent count, and the earlier full-day Claude check against ccusage (previous plan, Phase 9) was exact.
- **ccusage and Copilot**: ccusage (v20, `--by-agent`) puts a Copilot session's whole usage on the session's **last active day** — session `4f8f582f…` has 2,483 calls from 5 to 13 Nov (109.6M on 5 Nov, 104.9M on 6 Nov, …) and its last event on 17 Nov; ccusage shows all 372.5M on 17 Nov. Track counts each call on its own day (needed for charts), the independent count agrees with Track, so the script lists ccusage's Copilot column for information only. This corrects the previous plan's claim that ccusage "shows Copilot with 0 tokens".

What exists:
- `src/Track.Core/Usage/UsageSeries.cs`: `UsageSeries(store, usage, periods, time).For(Period)` → `SeriesBucket(Index, StartUtc, EndUtc, Label, ShortLabel, IsFuture, IsCurrent, PerTool, Total)`. Today = consecutive UTC hours from local midnight (23/25 on clock-change days, labels `00:00–01:00` … `23:00–24:00`, repeated hour `02:00–03:00 (2nd)`, end label = start + length); Week/Month = local days (`Mon 16 Nov`, short `M` / `16`). Each stored hour goes to the bucket containing its start (half-hour zones tested with Asia/Kolkata).
- `PeriodCalculator.ToLocal(utc)`; `UsageAggregator.Price(model, tokens)` is now public (used by the series so prices match the totals exactly).
- `scripts/check-series.ps1` + `scripts/independent_usage.py` (own parser, own dedupe) + `scripts/series-dump.cs` (file-based app; `#:property PublishAot=false` because the repo's warnings-as-errors turns a NuGet advisory on the AOT package into an error).


## Phase 2: Comparison and its text
Status: Complete

- [x] `UsageComparison.For(Period, now)` → `{ Current, Previous, Percent, Text }`: Today vs yesterday, Week vs last week, Month vs last month, both counted from the period start up to the same elapsed point (hour granularity: buckets that start before `periodStart + elapsed`); Month clamps to the previous month's length (31 Mar vs 28/29 Feb)
- [x] Text: `+18 % vs last week at this time`, `−7 % vs yesterday at this time`, `same as last month at this time` (0 %), `new this week` when the previous value is 0 and the current is not, hidden when both are 0; percentages rounded half away from zero, the minus sign is U+2212
- [x] Tests: each period on normal days, DST days, Monday 00:00 (elapsed 0 → both sides empty → hidden), month start, 31 March vs February (leap and non-leap), 0 → value, value → 0 (−100 %), rounding at .5

### Verification Plan
- `scripts/test.ps1` → pass, 100 %
- `scripts/check-series.ps1` extended: prints today's comparison and recomputes it independently from the raw logs → same percent

### Phase Summary
Done 2026-11-18. Verification results:
- `scripts/test.ps1` → **386 tests**, Track.Core 100 % line/branch/method.
- `scripts/check-series.ps1` (cutoff 21:00 local): Track and the independent count both give today **276,977,383** vs yesterday **84,159,876** up to 21:00 → **+229 %**, text `+229 % vs yesterday at this time`; every day and hour still equal.

What exists: `src/Track.Core/Usage/UsageComparison.cs` — `UsageComparison(usage, periods, time).For(Period)` → `Comparison(Current, Previous, Percent?, Text?)`. Elapsed time is measured in local wall-clock time from the period start and applied to the previous period's start (yesterday / last Monday / the 1st of last month), clamped at the current period's start for shorter months; both sides use `UsageAggregator.Totals` over `[start, cut)` on stored hour starts. Texts: `+18 % vs last week at this time`, `−7 % …` (U+2212), `same as … at this time`, `new today|this week|this month`, null when both are 0. Rounding half away from zero (tested at ±0.5 %).
Script fix: the cutoff was built as a local-kind DateTime, so the printed time was an hour off (numbers were right, the dump got the UTC text); now `SpecifyKind(Utc)`.


## Phase 3: Chart view model, drill-down and texts
Status: Complete

- [x] `ChartViewModel` (Core, no WPF types): from a `UsageSeries` → bars with stacked `Segment { Tool, From, To }` in 0..1 of the chart height (largest tool at the bottom), `Emphasized` (current), `Future`, the "nice" top value (1/2/2.5/5 × 10ⁿ) and its text `top 200M`, axis labels (Today `00 06 12 18 24`; Week `M T W T F S S`; Month `1 8 15 22 29`), and the tooltip lines (`Tue 17 Nov · 84.1M tokens · $20.32`, then `● <Tool> <tokens>` per tool with usage, largest first; `not yet` for future buckets; `no usage` for empty past buckets)
- [x] Minimum visible bar: a bucket with usage gets at least 1.5 px so tiny usage is not invisible; a bucket without usage shows only the base line
- [x] `PopoverViewModel` gains `Chart`, `ComparisonText`, `SelectedBucket` + `SelectBucketCommand`: selecting a bar makes `Rows`/`TotalRow` show that bucket and `PeriodLabel` show its label; selecting it again, or changing the tab, clears it; a selected bucket that leaves the period (rollover) is cleared automatically
- [x] **Rollover behavior** in the view model: every rebuild (each 5 s live refresh and 30 s usage refresh) recomputes the ranges from the clock, so within 5 s after local midnight Today shows the new day (all future hours faint, rows at 0), the comparison switches to the new yesterday, Monday 00:00 starts a new week and the 1st starts a new month
- [x] Tests (FakeTimeProvider, Copenhagen zone, fixture store): exact tooltip, axis, top-value and comparison strings for all 3 tabs; segment math (sums to the bar height, order); drill-down texts and totals for an hour and a day; clearing on second click and tab change; time advanced across 23:59:59 → 00:00:00, Sunday → Monday, 30 Nov → 1 Dec and the DST nights, asserting the chart, comparison, rows and labels before and after
- [x] `PopoverViewModel.ChartVisible` + `ToggleChartCommand` (texts `Hide` / `Show`), saved to `TrackSettings.PopoverChartHidden`; hidden → `Chart` and `ComparisonText` are null, drill-down cleared; tests for toggle, persistence through `SettingsStore` round trip and old settings files without the field (default: shown)
- [x] Snapshot test: one fixture → the full text content of the popover for each tab (rows, total, chart tooltips, axis, top, comparison) asserted as one block, hand-computed in the test

### Verification Plan
- `scripts/test.ps1` → pass, 100 %
- The snapshot test fails if any digit or word changes (proven by changing one fixture number and seeing the failure, then reverting)

### Phase Summary
Done 2026-11-18. Verification results:
- `scripts/test.ps1` → **417 tests**, Track.Core 100 % line/branch/method.
- Snapshot proof: changing one fixture value by one token (Copilot 999,499 → 999,500) failed all three snapshot tests (`999k` → `1.0M` in rows and tooltips); reverted → pass.

What exists:
- `src/Track.Core/Display/Chart.cs`: `ChartViewModel.Build(period, buckets, selectedStartUtc)` → `ChartViewModel(Title, Bars, TopText, Axis)`; `ChartBar(Index, StartUtc, Segments, Current, Future, Selected, Tooltip)`; `ChartSegment(Tool, From, To)` as 0..1 fractions of the nice top (largest tool at the bottom); `AxisLabel(Position, Text)` in bar units (Today `00 06 12 18` at bar centers + `24` after the last bar; Week every day; Month `1 8 15 22 29`); `NiceMax` (1/2/2.5/5 × 10ⁿ; "top 25.0M"; `no usage yet` when empty); `SegmentPixels(bar, px)` with `MinimumBarPixels = 1.5`. Tooltips: `Wed 18 Nov · 22.4M tokens · $29.60` + `● Claude Code 21.4M` lines, `not yet`, `no usage`; partial prices `≈$`, unknown `$—`.
- `src/Track.Core/Display/UsagePanel.cs`: `UsagePanelBuilder(store, usage, periods, time).Build(period, listed, selectedStartUtc, showChart)` → `UsagePanelContent(Period, Label, Rows, TotalRow, Chart, ComparisonText, SelectedStartUtc)`; `Toggle(chart, index, current)`; `PeriodLabel(period)`. Shared by the popover now and the island in Phase 4 (same numbers by construction). A selected bar is kept by its start time, so after a rollover it simply no longer matches and is dropped.
- `PopoverViewModel` now takes the `UsageStore` first; adds `Chart`, `ComparisonText`, `ChartVisible`, `ChartToggleText` (`Hide`/`Show`), `ToggleChartCommand`, `SelectBucketCommand(int)`, `SettingsChanged` event (the app saves it); `PeriodLabel` is the drilled bar's label while drilled; the model card keeps the whole tab's label. `TrackSettings.PopoverChartHidden` (default false).
- Rollover proven in tests: 23:59:59 → 00:00:00 (Today becomes the new day, drilled hour dropped, rows 0, comparison hidden), Sunday → Monday (new week, 6 future days, comparison hidden at 0 minutes), 30 Nov → 1 Dec (31 bars).
- Test helper note: number formatting in tests must be invariant (the machine culture is Danish).


## Phase 4: Island Usage tab (Core)
Status: Complete

- [x] `IslandSettings.OpenTab` (`Now` default, `Usage`), remembered across opens and restarts
- [x] `IslandContent` gains `Tab`, and for the Usage tab: period, `Chart`, comparison text, sorted rows and total (the same builders as the popover, so both surfaces always show the same numbers)
- [x] `IslandStateMachine` / `IslandViewModel`: switching tabs does not close or minimize the island; the island's own period and drill-down selection are independent from the popover's
- [x] Tests: Now ↔ Usage switching; Usage content equals the popover's for the same period (rows, total, chart); minimize from the Usage tab; rollover while the island is open on the Usage tab

### Verification Plan
- `scripts/test.ps1` → pass, 100 %

### Phase Summary
Done 2026-11-18. Verification: `scripts/test.ps1` → **426 tests**, Track.Core 100 % line/branch/method.

What exists (`src/Track.Core/Island/Island.cs`):
- `IslandTab { Now, Usage }`; `IslandSettings.OpenTab` (default `Now`).
- `IslandViewModel(machine, store, usage, periods, time, settings, listed?)`: `Tab` (starts from `settings().OpenTab`), `SelectTab` (+ `TabChanged` event for the app to save), `Period` + `SelectPeriod`, `SelectBucket(index)`. `listed` is the popover's visibility list (all tools when null) so the island lists the same tools.
- `IslandContent.Tab` and `IslandContent.Usage` (`UsagePanelContent`, built by the same `UsagePanelBuilder` as the popover; chart always on here — Hide is tray only). Tested equal to the popover row for row, total, label, every tooltip, top text and comparison for all three periods.
- Tested: tab switching keeps the island open and raises one change; saved tab used at start; drill-down cleared by period/tab change; minimize from Usage then reopen → still Usage; midnight while open on Usage → new day, drilled hour dropped.


## Phase 5: Chart in the WPF popover
Status: Complete

- [x] `UsageChart` control (`src/Track.App/Shell/`): draws a `ChartViewModel` (bars, dashed top line, base line, axis labels, top value) in theme colors; hover shows a tooltip with the view model's lines; click invokes `SelectBucketCommand`; keyboard: Left/Right moves a focus ring over the bars, Enter selects
- [x] Hide/Show toggle button right of the period tabs (link style, same row); hidden removes the chart block and comparison line
- [x] Review change 2026-11-18: toggle moved to a round − / + button right of the chart's top value; period tabs full width again; header row (`ChartTitle`) stays while minimized; tooltip `Minimize chart` / `Show chart`
- [x] Popover layout: tabs → chart (64 px + axis) → comparison line → period label (or drilled bucket label) → rows → total; popover stays 330 px wide
- [x] Popover keeps the same refresh paths; the chart rebuilds with the rows
- [x] Add the control to `docs/coverage-exclusions.md`

### Verification Plan
- `dotnet build Track.slnx -c Release` → 0 warnings; `scripts/test.ps1` still 100 %
- `Track.exe --render-popover <dir>` in light and dark for all three tabs → compare with mockup round 5 (layout, stacking order, faint future bars, axis labels, top value)
- **Real screen (unlocked), real mouse**: open the popover, hover three bars → tooltip text equals `diag.txt`'s chart lines for those buckets; click a day bar → table header shows that day and the rows equal that day's numbers from `scripts/check-series.ps1`; click again → back to the whole tab; click **Hide** → chart gone, popover shorter; close and reopen (and restart Track) → still hidden; **Show** → back
- Idle CPU/RAM re-measured as in the previous plan (popover closed): still ≤ 0.5 % of one core and ≤ 60 MB working set

### Phase Summary
**Complete 2026-11-18** (real-mouse results at the end of this summary).
- Done: `src/Track.App/Shell/UsageChart.cs` (bars, dashed top line, base line, axis, tooltip with colored tool dots, click and Left/Right/Enter), popover layout (Hide/Show right of the tabs, chart title + top, chart, comparison), chart lines in `diag.txt`, `docs/coverage-exclusions.md` entry. `dotnet build` 0 warnings; `scripts/test.ps1` 426 tests, 100 %.
- `--render-popover` light and dark, all three tabs, real data: matches mockup round 5 (stacked tool colors, faint future slots, axis, `top 100.0M`, `+320 % vs yesterday at this time`, Hide next to the tabs).
- Bug fixed: render-only runs saved the last tab ("Month") to the user's settings on exit; render runs no longer save settings (checked: settings.json unchanged by a render run).
- **Real-mouse checks NOT done.** The first automated attempt failed: UI Automation did not find the popover window, so the script clicked at wrong coordinates near the left edge of the screen (about (0–26, 132–226), 21:57) while the user was working, and the user was also clicking the test popover/island, which mixed the results. Nothing from that run counts. Next: run the hover / click / Hide checks only with the user's agreement (either the user performs them while `diag.txt` is read, or the automated script runs while the user is away), and fix the window lookup first (find the popover by its window handle from the process instead of UI Automation's name).
- Idle CPU/RAM (popover closed, island hidden, real data, 120 s after a 40 s settle, `idle5.ps1` in the session scratchpad): first run **0.57 %** of one core, above the 0.5 % target, because the 5-second refresh rebuilt the chart and comparison even with the popover closed. Fix: `PopoverViewModel.IsShown` (set by `TrayController` on open/close); while closed, `UsagePanelBuilder.Build` skips the hourly/daily split unless a bar is drilled, and the table stays correct. A drilled bar survives close/open. Tests: `While_closed_the_table_stays_correct_and_the_chart_is_built_again_on_open`, `A_drilled_bar_survives_closing_and_opening_the_popover` (428 tests, 100 %). Re-measured: **CPU 0.29 % of one core, working set 52.8 MB, private 49.6 MB** → within ≤ 0.5 % / ≤ 60 MB. ✔
- **Review change (user, 2026-11-18): chart minimize button.** The Hide link next to Today/Week/Month is gone; the period tabs are full width again. A round − / + button (`ChartToggle`) sits right of the chart's top value; minimized, the header row (`ChartTitle`, e.g. `This month by day`) stays and only the bars and comparison collapse; the usage rows never move sideways. `ChartToggleText` is now the tooltip `Minimize chart` / `Show chart`. `ChartViewModel.TitleFor(Period)`. Render-only runs no longer save a toggle to settings. Renders `popover-*-light|dark.png` and `popover-month-chart-minimized-light|dark.png` checked; settings.json unchanged by the render run.
- **Review fix (user, 2026-11-18): island missed working agents the tray saw.** Cause, found in this session's own Claude log: a Stop hook sends Claude back to work with a meta prompt (`isMeta: true`) followed by `stop_hook_summary`, so the island's tracker saw "turn ended" while the log kept being written; the tray used a different rule (any log write in the last 2 minutes). Fix: one shared answer. `AgentActivityTracker` also counts a session as working when its log was written within `LiveStatusDetector.ActiveWindow` and more than `EndSettle` (15 s) after its last end marker (task timer restarts at that end); `LiveStatusDetector.WithAgents` makes the tray icon and popover take Working and the session count from the same agents. Tests: the real Stop-hook lines through `ActivityReader`, end-tail settle, a session whose prompt was never seen, Copilot writing after a step end, `WithAgents` (434 tests, 100 %). Real data, 11 samples 5 s apart: tray running == island agents every time (Claude, Copilot).
- Window lookup fixed (no clicks used): `diag.txt` now has `popover-ui: chart=x,y,wxh | toggle=x,y,wxh | bars=N` (screen pixels from the app itself, via `PointToScreen` on the named `ChartView` / `ChartToggle`). The check script (`real5-chart.ps1` in the session scratchpad) reads those rectangles and picks the bars to hover/drill from the `bar N:` lines, so it works on whichever tab is open. A lookup-only run (`TRACK_CHECK_NO_CLICK=1`) at 22:07 read `chart=1592,788,302x77 | toggle=1868,745,28x15 | bars=24` correctly; its screenshot was black because the screen was locked (LogonUI running). The real-mouse checks need an unlocked screen and the user's go-ahead.
- **Real-mouse checks (user stepped away, 22:32–22:42; separate `TRACK_DATA`, `real-run.ps1` + `real5-chart.ps1` in the session scratchpad), all ✔:**
  - Hover bar 22 → tooltip `22:00–23:00 · 41.9M tokens · $11.65 · ● Claude Code 23.7M · ● Copilot 18.2M`, identical to diag's `bar 22` line (screenshot `real5-hover.png`).
  - Click bar 10 → label `10:00–11:00`, rows Copilot 6.1M / Codex 26k / Claude Code 0 / Total 6.2M $1.76; click again → `Today`.
  - − button → `chart: hidden`, popover shorter, header row `Today by hour` + `+` stays, rows unchanged (`real5-hidden.png`); restart → still hidden; + → chart back; settings.json `popoverChartHidden: false`; no errors.log.
- **Bugs found by the real mouse and fixed:** (1) a click above a short bar did nothing — `UsageChart` had no background, and WPF only hit-tests painted pixels; it now paints a transparent background over the whole area (reproduced before, passing after: hover bar 22 then click 14 → `14:00–15:00`, then 16 → `16:00–17:00`). (2) The tooltip was placed at the mouse; at the screen's right edge Windows pushed it over other bars. It now opens above the chart at the bar and is not hit-testable.

## Phase 6: Island Usage tab (WPF)
Status: Complete

- [x] Island opened view: **Now** | **Usage** tab buttons under the header; Usage shows the period tabs, the chart (dark palette), comparison, rows, total; same 400 px width; minimize button unchanged
- [x] Tab clicks inside the island do not close it (they are clicks on the island)
- [x] `--render-island` adds the Usage tab for each period

### Verification Plan
- `dotnet build -c Release` → 0 warnings; `scripts/test.ps1` 100 %
- Renders compared with mockup round 5 (island column)
- **Real screen, real mouse**: open the island, click Usage, hover and click bars (same checks as Phase 5), switch Today/Week/Month, minimize from the Usage tab, reopen → still on Usage; island Usage numbers equal the popover's for the same tab (`diag.txt`)

### Phase Summary
**Complete 2026-11-18** (real-mouse results at the end of this summary).
- Done: `IslandWindow` opened view has **Now | Usage** tab buttons under the header (buttons, so a click on them is not a click on the island); Usage shows Today/Week/Month, chart title + top, `UsageChart` (dark theme dictionary merged into the island's resources), comparison, label, rows (logo, tokens, price · in · out · cache, usage bar) and Total. Events `TabSelected` / `PeriodSelected` / `BarSelected` → `IslandViewModel` under the gate → redraw. `TabChanged` saves `IslandSettings.OpenTab`. The island lists `PopoverViewModel.ListedTools` (same tools as the popover; new test). `ContentKey` includes tab and usage content so it redraws on change.
- `--render-island` (real data): new `island-3-usage-today|week|month.png` and `island-3-usage-month-drilled.png`; match mockup round 5's island column. In the renders Today's total (382.0M, $116.73) equals the drilled "Wed 18 Nov" day, and the week total equals the Now tab's footer.
- Verification-only `TRACK_POINTER_FILE` lines `island:tab usage|now`, `island:period today|week|month`, `island:bar N` (same calls as the buttons/chart). `diag.txt` gained `island-tab:` and `island-row:` lines; it is rewritten when the island's tab, period or drilled bar changes.
- Bug fixed: the live update's full-screen check read the pointer file and sometimes swallowed the one-shot `click` line; it now only peeks (`PointerOverride(consume: false)`).
- Pointer-file run with real data, separate `TRACK_DATA` (`island6.ps1` in the session scratchpad), all ✔: open → Usage → island rows == popover rows (Today, line by line, 4 rows); Week `+101 % vs last week at this time`; Month `+29 % vs last month at this time`, total 1.3B; bar 17 → label `Wed 18 Nov`, total equals Today's total; bar 17 again → `This month`; settings `openTab: Usage`; restart + open → opens on Usage; minimize from Usage → Hidden/minimized; reopen → still Usage; back to Now → `openTab: Now`; no errors.log.
- `dotnet build Track.slnx -c Release` 0 warnings; tests 429, 100 % line/branch/method.
- **Real-mouse checks NOT done** (screen locked since about 22:07; also need the user's go-ahead): click Usage, hover and click bars, switch periods, minimize from Usage with the real mouse.
- **Real-mouse checks (same run; `real6-island.ps1`), all ✔:** cursor to the top edge + click → Open; click **Usage** → island rows == popover rows (Today, 4 lines identical: Claude Code 316.0M, Copilot 93.8M, Codex 26k, Total 409.8M $124.30); hover bar → tooltip above the chart (`real6-hover.png`); **Week** → `This week`, `+109 % vs last week at this time`; **Month** → `This month`, `+31 %`; click today's bar → `Wed 18 Nov`, total equals Today's total exactly; click again → `This month`; minimize (−) from Usage → Hidden, minimized; top edge + click → Open, still on Usage; **Now** → `openTab: Now` saved; Escape → closes; no errors.log. Diag gained `island-ui:` (screen rectangles of the island's buttons and chart) for these checks.

## Phase 7: Correctness over real time, packaging, docs
Status: Complete

- [x] Verification-only clock offset for the app (`TRACK_CLOCK_OFFSET=<seconds>`, used only when `TRACK_DIAG=1`) so a running app can be put a minute before midnight, Sunday 23:59 and the last day of the month
- [x] Rollover run on the real app: start 60 s before local midnight with real data → `diag.txt` before and after midnight shows Today switching to the new day, the comparison switching to "vs yesterday" for the new day, the drilled bucket cleared; same for Sunday → Monday and month end
- [x] `scripts/check-series.ps1` run again on the final build: all equal
- [x] README: charts, drill-down, comparison, island Usage tab
- [x] Reinstall with `scripts/install.ps1 -Rebuild`

### Verification Plan
- `scripts/test.ps1` → pass, 100 % line/branch/method
- The three rollover runs above pass on the real app (diag lines recorded in the summary)
- `scripts/check-series.ps1` → no differences
- `Get-Process Track` → running from `%LOCALAPPDATA%\Programs\Track`; idle CPU/RAM within the previous targets

### Phase Summary
**Complete 2026-11-18.**
- **Clock offset:** `Track.Core.Time.ShiftedTimeProvider` (+ `For(inner, offset, diagnostics)`: only with `TRACK_DIAG=1`; empty, invalid or 0 → real clock); tests in `Time/ShiftedTimeProviderTests.cs`. `TrayController` uses it for ingest, periods and view models; `diag.txt` has `clock: <local time> (offset …)` and the chart line `bars N, now I`. Pointer-file commands `popover:period X` / `popover:bar N` added (same calls as the tabs and chart).
- **Rollover on the real app** (`rollover.ps1` in the session scratchpad; real logs, separate data, popover open, drilled into the last bar before the boundary), all ✔, no errors.log:
  - Midnight: before `2026-11-18 ons 23:59:15`, `Today by hour | bars 24, now 23 | +391 % vs yesterday at this time | label 23:00–24:00` → after `2026-11-19 tor 00:00:20`, `no usage yet | bars 24, now 0 | compare - | label Today`.
  - Sunday → Monday: before `2026-11-22 søn 23:59:14`, `This week by day | bars 7, now 6 | +2 % vs last week at this time | label Sun 22 Nov` → after `2026-11-23 man 00:00:19`, `bars 7, now 0 | label This week`.
  - Month end: before `2026-11-30 man 23:59:13`, `This month by day | bars 30, now 29 | −11 % vs last month at this time | label Mon 30 Nov` → after `2026-12-01 tir 00:00:18`, `bars 31, now 0 | label This month`.
  - Right after a boundary the comparison is `-` because neither side has usage yet (rule tested in Phase 2: no text when both are 0); the drilled bar is dropped every time.
- **`scripts/check-series.ps1`** on the final build: every day (4–17 Nov) and every complete hour of today equal to the independent count; Codex equal to ccusage; Copilot differs from ccusage only where ccusage dates a whole session on its last day (known, documented); `+337 % vs yesterday at this time` equals the independent figure. "All equal."
- **README:** usage chart (hover, click/keyboard drill, comparison, − / + minimize), island Now/Usage tabs, the shared "who is working" rule, `check-series.ps1`, `TRACK_CLOCK_OFFSET` and the new pointer commands.
- **Memory bug found while measuring:** the one-time trim after start ran after the first live read only; the parallel first usage read (Copilot log lines up to 2.4 M characters) could finish later and its garbage stayed for 30 minutes (128.7 MB working set in one run, 52.8 MB in another). Now trimmed after both first reads (`TrimAfterStart`). Re-measured: 44.7 / 46.2 MB (2 min), and over 5 min **0.31 % CPU, 52.5 MB working set, 47.7 MB private** ✔ (CPU varies 0.3–0.6 % between 2-minute runs with the pointer-file method, which itself reads a file 10×/s).
- `scripts/test.ps1`: 441 tests, 100 % line/branch/method. `dotnet build` 0 warnings.
- Reinstalled with `scripts/install.ps1 -Rebuild`: `Get-Process Track` → `%LOCALAPPDATA%\Programs\Track\Track.exe`, Run key set, no errors.log.

## Final Recap
Track now shows usage as small stacked bar charts and in the top island, with every number checked.
- **Core (Phases 1–4):** `UsageSeries` (local-time hour/day buckets from the hourly store, DST-safe labels), `UsageComparison` (`+18 % vs last week at this time`, same elapsed local time), `ChartViewModel` (nice top value, stacked segments largest at the bottom, axis, tooltips), `UsagePanelBuilder` (one builder for popover and island: rows sorted by tokens, Total, chart, comparison, drill-down), island `IslandTab` Now/Usage. Every text is hand-computed in tests; snapshot tests break on a one-token change; rollover tests for midnight, Monday, month end and DST.
- **WPF (Phases 5–6):** `UsageChart` control (theme colors, tooltip above the bar, click / Left-Right-Enter drill, whole column clickable), popover chart with a − / + minimize next to the top value (remembered; rows never move), island Usage tab in the dark look; tab, period and drill wired through the tested view models; the island remembers its tab.
- **Review fixes:** island and tray share one "who is working" answer (Stop-hook continuations and missed markers no longer hide a working agent from the island); Hide moved off the tab row; chart clicks above short bars; tooltip covering bars at the screen edge; render runs no longer save settings; the full-screen check no longer swallows test clicks; memory trim after both first reads.
- **Verified:** 441 tests at 100 % line/branch/method; real data equal to an independent count and ccusage; real-mouse runs of popover and island; rollover at midnight, Sunday→Monday and month end on the real app via a shifted clock; idle 0.31 % CPU / 52.5 MB.
- Unchanged: the 90-day store, pricing and the visibility rule.

## Deployment Plan
1. `dotnet build Track.slnx -c Release` → 0 warnings.
2. `powershell -ExecutionPolicy Bypass -File scripts\test.ps1` → all tests pass, 100 % line/branch/method on Track.Core.
3. Optional data check: `powershell -ExecutionPolicy Bypass -File scripts\check-series.ps1` → "All equal." (needs Python and `npx ccusage`).
4. `powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Rebuild` → publishes, stops the running Track, copies to `%LOCALAPPDATA%\Programs\Track`, sets the HKCU Run key and starts it. Settings and the snapshot in `%LOCALAPPDATA%\Track` are kept; the new `popoverChartHidden` and `island.openTab` settings default to chart shown / Now tab.
5. Check: `Get-Process Track` runs from `%LOCALAPPDATA%\Programs\Track`; no `%LOCALAPPDATA%\Track\errors.log`; left-click the tray icon → chart under the tabs; open the island → Usage tab.
6. Roll back: `scripts\uninstall.ps1` (keeps data), then install a previous build the same way.
