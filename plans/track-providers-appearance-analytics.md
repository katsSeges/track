# Track — more providers, appearance settings, analytics, click-to-open and alerts

## Readiness and handoff

- Readiness: **Approved** (user, 2026-11-19, in the coordinating Project: "Yes, write the plan and start implementation"; also asked "When you are done, make a PR to the original repo with information, before and after images").
- Execution progress: In progress
- Planning depth: **Standard**. Many components, plus public file-format compatibility for `settings.json` and `snapshot.json`. No server or data-loss migration.
- Scope of approval: implement everything in this plan in `oliverseges/trackAI`, then open **one** PR (separate commits per phase) from the fork to `katsSeges/track` `main`. No tag or release.
- Current context: fork `oliverseges/trackAI` at `6a79970` (1.1.0). The upstream is `katsSeges/track` (user has READ access, so the PR is cross-fork).
- Next action: T-1 provider catalog and visibility policy.
- Blocking decisions: none. Bounded assumptions are listed in U-*.

## Outcome and boundaries

Track supports only Claude Code, Codex and Copilot. Its look follows Windows light or dark mode with a fixed acrylic
popover, and its settings are a few tray-menu items. The user wants:

1. More UI options: follow the Windows theme or choose one, choose the material (glass or acrylic, Mica, Mica Alt or
   solid), set custom colors, and change behavior options.
2. More providers. Each one is shown only when enabled. By default, only providers with log data appear, and the layout
   adapts to the number of enabled providers.
3. A more detailed analytics screen.
4. Clicking a provider row opens that provider's chat interface.
5. Additional improvements chosen by the user: toasts when an agent finishes or needs input, a global hotkey, and
   spend or token alerts.

**In scope:** everything above. Also update the README, DEVELOPMENT.md and coverage-exclusions docs, and add
`docs/release-notes/v1.2.0.md` plus the matching `<Version>` bump. The PR must include before and after images.

**Non-goals (with reasons):**
- Ollama token counting, because Ollama keeps no per-request usage on disk and a proxy is invasive. Ollama shows
  running status and the loaded model only.
- Cursor support, because its SQLite state is fragile and the user did not select it.
- Per-project breakdown and subscription cost view (proposed follow-ups in the PR body).
- An update checker (follow-up).
- Retention beyond 90 days (`RetentionWindow.Days`). Analytics ranges are capped at 90 days, and the cap is shown in the UI.
- Tagging or publishing a release.

## User decisions

- Providers: Gemini CLI + OpenCode + Cline/Roo with real token parsing, plus Ollama as running/loaded-model status only (no tokens).
- Packaging: ONE PR with all features, organized in clear commits (one per plan phase).
- Settings UI: new Settings window (tabs Appearance, Providers, Behavior) opened from the tray menu and a gear in the popover.
- Click-to-open: auto-detect the installed chat app per provider with a per-provider override in Settings.
- Analytics: standalone resizable window with 7d/30d/90d/custom range, stacked tokens+cost per provider, model breakdown, cache hit rate, sessions count/length, busiest-hours heatmap, top sessions, CSV export.
- Extras: toast notifications (done / needs you), configurable global hotkey, spend/token budget alerts.

## Constraints and preserved invariants (from repo rules, `plans/track-subagents.md` "For Future Agents")

- All logic lives in **Track.Core**, behind the **100 % line, branch and method** gate (`scripts\test.ps1`). Track.App
  only draws; every new App class needs `[ExcludeFromCodeCoverage(Justification=…)]` and a row in
  `docs/coverage-exclusions.md`.
- Privacy: Track reads only local files and processes. The one existing network call is the daily price list. New
  network use is allowed only for Ollama on `localhost` (`/api/ps`), and only when Ollama is enabled.
- Existing `settings.json` and `snapshot.json` files must still load unchanged, with the same three tools and numbers.
  `Snapshot.CurrentVersion` stays 1 unless a format change is unavoidable.
- Default behavior for a user with only Claude, Codex and Copilot must look the same as 1.1.0, except for the new gear
  and analytics entry points.
- No automated mouse clicks on the user's desktop without asking. Use `TRACK_POINTER_FILE` and `TRACK_DIAG=1`. Stop
  the installed Track before running a bin build or render because of the single-instance mutex.
- Use Track's own design language (`design/` mockups); copy nothing from other products. Logos are simplified stand-in
  marks, as the existing ones are (`src/Track.App/Assets/logos/README.md`).

## Acceptance criteria

| ID | Observable outcome | Task IDs | Verification |
| --- | --- | --- | --- |
| AC-1 | Gemini CLI, OpenCode, Cline and Roo Code usage is parsed into `UsageRecord`s with the correct tokens, model, session and time. Duplicates are not counted when a whole-file JSON log is rewritten. | T-2, T-3 | V-1, V-2 |
| AC-2 | Ollama shows as running with the loaded model when enabled and the server is up. It shows no tokens, and a down server is not an error. | T-4 | V-1, V-5 |
| AC-3 | Provider visibility: **Auto** (default) shows a provider only when it has usage data or is running. **On** always shows it, and **Off** never shows it. Ollama defaults to Off. The tray icon layout, popover rows, chart series, island and analytics follow the same set. | T-1, T-5, T-6 | V-1, V-3 |
| AC-4 | Layout adapts to the number of visible providers. The tray icon uses defined layouts for 1 to 3 providers and a defined overflow rule for 4 or more (for example, running providers first, at most N marks). No text is clipped in the popover with 8 providers. | T-6 | V-1, V-3 |
| AC-5 | The Settings window has Appearance, Providers and Behavior tabs. Theme can be System, Light or Dark (System follows Windows live). Material can be Acrylic, Mica, Mica Alt or Solid, with a solid fallback when unsupported. Users can set an accent and per-provider colors and reset them to defaults. Changes apply live and persist. | T-7, T-8 | V-1, V-3, V-4 |
| AC-6 | An actual 1.1.0 `settings.json` and `snapshot.json` fixture load with identical totals. Missing fields use declared defaults; unknown properties are ignored by the serializer; unknown enum strings are tested for the existing documented fallback behavior, not assumed to be equivalent to missing fields. | T-1, T-7 | V-1 (fixture test), V-4 |
| AC-7 | The analytics window offers 7d, 30d, 90d and custom ranges up to 90 days. It shows stacked tokens and cost over time per provider, a model breakdown, cache hit rate, session count and median length, a busiest-hours heatmap, and top sessions. Session totals and first-to-last observed span are labeled as full-session, not range-local; a session is included when its observed interval overlaps the selected range. CSV export writes exactly the shown aggregate, and its numbers match the popover for the same range. | T-9, T-10 | V-1, V-3 |
| AC-8 | Clicking a provider row in the popover, including running rows, or an agent row in the opened island opens that provider's chat app. It uses an auto-detected target order with a per-provider override in Settings. If nothing is found, it shows an inline message and never crashes. Hover still opens the model card. | T-11, T-12 | V-1, V-4 |
| AC-9 | Opt-in toasts (default off) fire once per transition: done, or needs you. A configurable global hotkey (default `Ctrl+Alt+Shift+T`, can be turned off) toggles the popover, and a registration conflict is reported in Settings. Spend and token alerts (default off) can use day or month thresholds per provider or for the total, and each fires at most once per threshold per period. | T-13, T-14 | V-1, V-4 |
| AC-10 | Coverage gate stays at 100 %, CI passes, and docs are updated. The PR to `katsSeges/track` contains a description, per-feature sections, before and after images (popover light and dark, island, new Settings, analytics), a test summary, known limitations and follow-ups. | T-15, T-16 | V-1, V-6 |

## Current state and evidence

| ID | Claim | Source | Class. | Consequence |
| --- | --- | --- | --- | --- |
| E-1 | `Tool` is a closed enum (Claude, Codex, Copilot), and `ToolNames.Display` uses an exhaustive switch. | `src/Track.Core/Tool.cs` | Observed | Every switch and `Order` array must change. |
| E-2 | The order and colors are hard-coded in 4 places: `Chart.cs:27`, `TrayIconModel.cs:46,55-59`, `ViewModels.cs:67`, and `UsageChart.cs:32` BrushKeys. Logos live in `Logos.cs:18-40`, and `TrayController.cs:265-269` has a render list. | grep `Tool\.(Claude…)` | Observed | Centralize them in one catalog (D-1). |
| E-3 | Parsers are line-based. `ILogParser.Parse(line)` uses `ParserState`, and `IncrementalLogReader` reads appended lines. `LogSource(Tool, Root, Pattern, SessionIdFrom)` supports file-name or parent-folder session IDs. | `Parsing/ILogParser.cs`, `Ingest/LogSource.cs` | Observed | Whole-file JSON logs (Gemini, Cline, Roo, and per-message OpenCode files) need a second read mode (D-2). |
| E-4 | Live status comes from log write times plus `ProcessNames` per tool. Copilot uses hold files. | `Live/LiveStatusDetector.cs:38-63` | Observed | New providers add process names. Ollama adds an HTTP probe behind an interface. |
| E-5 | The snapshot and settings use source-generated JSON with `UseStringEnumConverter`, so tools are stored as `"Claude"` and similar strings. Missing settings properties retain initializer defaults and unknown properties are ignored by default; unknown enum values can fail deserialization. Snapshot load then returns null, causing log reread. | `Ingest/SnapshotStore.cs`, `Settings/TrackSettings.cs` | Observed | Test each compatibility case separately against actual 1.1.0 fixtures. Do not equate unknown enum values with missing fields or promise fallback beyond observed behavior. |
| E-6 | The theme is either Light.xaml or Dark.xaml in `MergedDictionaries[0]`, chosen from the `AppsUseLightTheme` registry key and updated on `UserPreferenceChanged`. The backdrop is always DWM type 3 (transient or acrylic), with `PopSolid` as the fallback. | `TrayController.cs:166,1016-1040`, `Native.cs:26-38`, `Themes/*.xaml` | Observed | Add a theme mode and material setting, set DWMSBT 2 (Mica), 3 (Acrylic) or 4 (Tabbed, Mica Alt), and override brushes from settings. |
| E-7 | Settings UI consists only of tray context-menu items: Refresh, Icon layout, Start with Windows, Show top island and Quit. | `TrayController.cs:952-995` | Observed | Add a Settings window (user decision). |
| E-8 | Popover rows already use the `Tag=Tool` hover pattern for the model card. Clicking a row currently does nothing. | `PopoverWindow.xaml`, `.xaml.cs` | Observed | A click can open the provider target without conflicting with hover. |
| E-9 | Usage is stored as hourly buckets per tool and model, plus per-session lifetime totals with first and last observed time; per-session totals are pruned after seven days. Raw records are not stored. | `Ingest/UsageStore.cs`, `Ingest/UsageIngestor.cs`, `Ingest/TokenCounts.cs` | Observed | Hourly provider/model data is range-local; session totals are not. Analytics must label full-session totals and first-to-last observed span, include sessions only when that span overlaps the selected range, and never describe lifetime totals as range-local active usage. Project folders are unavailable (follow-up). |
| E-10 | Cost comes from the LiteLLM price table, and models without a price show `$—`. | README "About the prices", `Pricing/*` | Observed | Ollama and local models count as unpriced or free and are labeled that way. |
| E-11 | This PC has no Gemini CLI, OpenCode, Cline or Roo logs. Ollama, Claude, Copilot, Codex and VS Code are installed. | Local probe, 2026-11-19 | Observed | The new parsers cannot be checked against real logs here (R-1). |
| E-12 | Coverage gate and verification switches: `--render-popover`, `--render-island`, `--render-icons`, `TRACK_HOME`, `TRACK_DATA`, `TRACK_DIAG`, `TRACK_POINTER_FILE`, `--theme`. | `docs/DEVELOPMENT.md` | Observed | Before and after images come from `--render-*` against a generated demo home. |

## Uncertainty and decision register

| ID | Question / assumption | Impact if wrong | Disposition | Resolution / checkpoint | Blocks |
| --- | --- | --- | --- | --- | --- |
| U-1 | Each new provider's on-disk format and location are unknown. | Parser counts zero or wrong numbers. | **Investigate** with spike T-2a. For each provider, read its current open-source writer code, then record the path, schema, token fields and dedupe key in the plan with source links. | If a format has no token fields, ship that provider as status-only and note it in the PR. | T-3 |
| U-2 | Users can opt into Ollama `localhost:11434/api/ps` polling as acceptable network use. | Privacy promise breaks. | **Bound.** Only when Ollama is On, only loopback, with a 1 s timeout and polling at the live interval. Document it in the README "Private by design" section. | Review in the PR. | T-4 |
| U-3 | Upstream maintainers may reject a large single PR. | Rework is needed. | **Bound** by the user's decision: one PR with clean commits per phase, so it can be split later by cherry-picking. | — | None |
| U-4 | The global hotkey default may conflict. | Registration fails. | **Bound.** Use `Ctrl+Alt+Shift+T`, which can be changed or turned off. If `RegisterHotKey` fails, show the failure in Settings. | V-4 | T-14 |
| U-5 | The chat app targets and the way to detect them. | Clicking opens nothing or unsafe command execution. | **Bound.** Use a Core-tested ordered target list per provider, where each target is detected by an App-side probe: an AppX package, an exe on PATH, a URI scheme, or a VS Code command. Launch only validated executables/registered URIs with executable and arguments passed separately; never interpolate log-derived model IDs into a shell command. If no target is found, show an inline message pointing to Settings. Defaults: Copilot = Copilot app, then VS Code (`vscode://` chat or `code` + `workbench.action.chat.open`), then terminal `copilot`. Claude = Claude desktop, then terminal `claude`. Codex = Codex app, then terminal `codex`. Gemini, then terminal `gemini`. OpenCode, then terminal `opencode`. Cline and Roo = VS Code. Ollama = terminal `ollama run <last model>`. | V-4 on this PC for Copilot, Claude and Codex | T-11 |
| U-6 | Mica and Acrylic support (DWMSA_SYSTEMBACKDROP_TYPE) varies by Windows build. | Window looks blank on Windows 10. | **Bound.** The existing `ApplyBackdrop` returns false, so fall back to Solid and label "(not supported on this Windows)" in Settings. | V-4 | T-8 |

## Alternatives and recommendation

| Decision | Options | Choice and reason |
| --- | --- | --- |
| D-1 Provider model | (a) Replace the `Tool` enum with string IDs and a registry. (b) **Extend the enum** with `Gemini, OpenCode, Cline, Roo, Ollama` and add one `ProviderCatalog` in Core that holds the display name, default colors (light and dark), logo key, process names, log sources, capabilities (`HasTokens`, `HasActivity`) and the chat-target list. | **(b).** Snapshot and settings JSON stay compatible because enum strings are unchanged. Exhaustive switches let the compiler find every consumer, and the coverage gate stays simple. Reconsider (a) if third-party provider plugins become a goal. |
| D-2 Whole-file JSON logs | (a) Synthesize lines. (b) Add an `IDocumentParser` read mode: reparse a file when its size or mtime changes, replace that file's prior contribution, and use dedupe keys that are stable per message. | **(b)** with file-level replace semantics in the ingestor, so rewrites never double count. Persist per-file cursor and contribution data additively in the v1 snapshot when possible. A version bump must migrate existing aggregates/cursors rather than discard them or rely on rereading possibly deleted logs. |
| D-3 Visibility | Reuse `ToolVisibilityPolicy`, or add a new policy. | **Wrap** it. Use `ProviderMode {Auto, On, Off}` per provider. Auto applies the existing rule plus "has any data or is running". On forces popover rows, and Off removes the provider everywhere and skips scanning its sources. |
| D-4 Theme overrides | Generate brush dictionaries, or patch resources at runtime. | **Patch at runtime.** Keep Light and Dark as base dictionaries, then overlay a `ResourceDictionary` built from `AppearanceSettings`: accent, per-provider color, and material, which picks `PopBg` or `PopSolid` alpha. Core owns the color math (parse and validate hex, contrast-safe text color) and is tested. |
| D-5 Analytics | Extend the island Usage tab, or build a separate window. | **Separate resizable window** (user decision). All series and tables come from a Core `AnalyticsBuilder` (tested), and the WPF view only draws them. |
| D-6 Toasts | WinRT `ToastNotificationManager`, or the tray balloon. | Use the **tray balloon** through `TaskbarIcon.ShowNotification`, which the existing tray library supports. It needs no AppUserModelID or packaging. Reconsider if the balloon looks wrong on Windows 11. |

## Proposed design and contracts (proposed paths)

- `Track.Core/Providers/ProviderCatalog.cs`: `ProviderInfo(Tool Id, string Name, string LogoKey, ColorPair DefaultColor, string[] ProcessNames, Func<string home, string appData, IReadOnlyList<LogSource>> Sources, ProviderCaps Caps, IReadOnlyList<ChatTarget> ChatTargets)`. This is the single source for the order and metadata. `ToolNames.Display`, `Order` arrays and the color switch read from it.
- `Track.Core/Parsing/{GeminiLogParser, OpenCodeLogParser, ClineLogParser}.cs`. Cline and Roo share one parser with different roots. They also need activity parsers where the logs allow it.
- `Track.Core/Ingest`: add a document-mode reader with file-level replace semantics (D-2).
- `Track.Core/Live/OllamaProbe.cs`: `IOllamaClient` interface (HTTP implementation in App, or Core with an injectable `HttpMessageHandler`) → `LiveStatus` with `LastModel`.
- `Track.Core/Settings/TrackSettings.cs`: add `Appearance { ThemeMode, Material, Accent?, ProviderColors{} }`, `Providers { Tool → ProviderMode, ChatTargetOverride? }`, `Behavior { Notifications{Done,NeedsYou}, Hotkey{Enabled, Gesture}, Alerts[] }`. Every new field has a default, so old files load.
- `Track.Core/Display/{SettingsViewModel, AnalyticsViewModel/AnalyticsBuilder, ChatTargetResolver, AlertEvaluator, NotificationPolicy}.cs`: plain .NET classes that are fully tested.
- `Track.App/Shell/{SettingsWindow, AnalyticsWindow, AnalyticsCharts, ChatLauncher, HotkeyHost}.cs`, plus `Native` additions (backdrop type, `RegisterHotKey`) and new logos in `Logos.cs`. All of these are coverage-excluded with a justification.
- New `--render-settings <dir>` and `--render-analytics <dir>` switches for images and checks.

## Risk register and pre-mortem

| ID | Failure chain | L / I | Prevention | Detection | Recovery |
| --- | --- | --- | --- | --- | --- |
| R-1 | New parsers are written against assumed formats, and real users see 0 or inflated tokens. | M / H | Spike T-2a cites writer source. Fixtures copy real examples from upstream repos or issues where they exist, and every fixture states its source. | PR "Known limitations" says the parsers were not checked against real logs on the author's PC. Diagnostics list parsed counts per provider. | Users can set the provider to Off, and the parser can be fixed later. |
| R-2 | Whole-file reparse double counts after a rewrite or restart. | M / H | Use replace semantics plus a test for rewrite, append, truncate and restart from snapshot. | V-2 | Snapshot reread. |
| R-3 | Enum extension changes the default look for existing users, for example by adding empty rows. | M / M | Default Auto means a provider with no data is hidden. A test confirms the 3-tool demo popover is identical to the before render. | V-3 image diff | — |
| R-4 | Wide scans of VS Code globalStorage and Gemini tmp slow down the 5 s live tick. | L / M | Scan only enabled or Auto providers, reuse `LogScanCache`, and extend the existing `PerformanceGuardsTests`. | V-1 performance guards | Off switch. |
| R-5 | A custom color produces unreadable text. | M / L | Core contrast check picks the foreground. Reset to default is available. | Unit tests | Reset. |
| R-6 | Hotkey or toast spam during refresh loops. | L / M | Transition-based policy, tested once per transition and once per threshold per period. | V-1 | Turn the setting off. |

## Delivery phases

Each phase ends with `scripts\test.ps1` green (100 % gate) and one commit. Commits use the Copilot co-author trailer.

### Phase 0: Branch and baseline images. Status: Complete
- [x] T-0: Use the existing app-managed branch, and add this plan as `plans/track-providers-appearance-analytics.md`. Build a demo home with the generator used for the README if one exists. Otherwise create a deterministic fake-log generator script (`scripts\demo-home.ps1`). Render the **before** images with the 1.1.0 build (`--render-popover`, `--render-island`, `--theme light|dark`) into `docs/images/pr/before/`.

### Phase 1: Provider catalog (no behavior change). Status: Not started
- [ ] T-1: Add `ProviderCatalog`, move the order, colors, process names and sources there, and extend the enum. Add `Providers` settings with `ProviderMode` and the D-3 visibility wrapper. Add a test that 1.1.0 settings and snapshot files still load (AC-6). Covers AC-3 and AC-6.

### Phase 2: New providers. Status: Not started
- [ ] T-2a (spike, time-boxed): Record U-1 findings in the plan for Gemini CLI, OpenCode, Cline and Roo. Decision rule: if a provider has token fields, implement a full parser. Otherwise, make it status-only.
- [ ] T-2: Add the document-mode ingest (D-2) with replace semantics. Depends on T-1.
- [ ] T-3: Add the parsers, plus activity parsers where possible, sourced fixtures and tests. Depends on T-2a and T-2. Covers AC-1.
- [ ] T-4: Add the Ollama probe (U-2) and process names. Covers AC-2.
- [ ] T-5: Make scanning and live detection honor `ProviderMode` (Off means no scan). Covers AC-3.
- [ ] T-6: Add adaptive layout. In `TrayIconModel`, use the existing layouts for 1 to 3 providers. For 4 or more, show running providers first and cap the marks, then add tests. Add logos and chart series for N providers. Make the popover rows compact, with row height and font steps, when there are more than 4 visible rows. The island should show every visible provider. Covers AC-4.

### Phase 3: Appearance and Settings window. Status: Not started
- [ ] T-7: Add Core `AppearanceSettings` and `Behavior` settings plus a `SettingsViewModel` with Appearance, Providers and Behavior tabs, with tests. Covers AC-5 and AC-6.
- [ ] T-8: Build the App `SettingsWindow` with the same backdrop and material. In `Native.ApplyBackdrop`, accept a material. Add runtime brush overlay and a live theme switch. Open it from the tray menu "Settings…" and a popover header gear. Covers AC-5.

### Phase 4: Analytics. Status: Not started
- [ ] T-9: Add a Core `AnalyticsBuilder` that produces range series, the model table, cache hit rate (`CacheRead / (Input + CacheWrite + CacheRead)`), sessions count and median observed span, the 7×24 heatmap in local time, top sessions and CSV rows. Session totals/span are full-session; include sessions whose first-to-last observed interval overlaps the range and label these values clearly. Test it against hand-computed fixtures and check that range series match popover totals. Covers AC-7.
- [ ] T-10: Build the App `AnalyticsWindow`, resizable with backdrop and material. Open it from the tray menu "Analytics…" and a popover footer link. Use `SaveFileDialog` for CSV export, and add `--render-analytics`. Covers AC-7.

### Phase 5: Click to open chat. Status: Not started
- [ ] T-11: Add a Core `ChatTargetResolver` (ordered targets, override and probe results → a launch spec or a "not found" message), with tests. Covers AC-8.
- [ ] T-12: Add the App `ChatLauncher` (probes and `Process.Start` with `UseShellExecute`; terminal through `wt.exe` if present, else `cmd /k`). Wire popover row `MouseLeftButtonUp` and keyboard Enter, plus island agent rows. Show the target in the row tooltip. Covers AC-8.

### Phase 6: Notifications, hotkey and alerts. Status: Not started
- [ ] T-13: Add the Core `NotificationPolicy` (island transitions → toast events) and `AlertEvaluator` (thresholds, once per period, persisted fired state in settings or the data folder), with tests.
- [ ] T-14: Build the App balloon toasts (D-6) and `HotkeyHost` (`RegisterHotKey` on a message-only `HwndSource`). Add the Behavior tab controls and a conflict message. Covers AC-9.

### Phase 7: Docs, after images and PR. Status: Not started
- [ ] T-15: Update the README (providers table, privacy note for Ollama loopback, tips), DEVELOPMENT.md (new switches), coverage-exclusions.md (new App files) and `docs/release-notes/v1.2.0.md`, then bump `<Version>` to 1.2.0. Render the **after** images to `docs/images/pr/after/` with the same demo home, adding the Settings tabs, analytics, the 6-provider popover and the tray icon sheet. Covers AC-10.
- [ ] T-16: Push the branch to `origin` (`oliverseges/trackAI`). Open the PR to `katsSeges/track:main` with `gh pr create --repo katsSeges/track --head oliverseges:<branch>`. The PR body includes a summary, per-feature sections, a before and after table using images from the fork raw URLs, the test and coverage result, known limitations (R-1, Ollama without tokens, 90-day cap) and follow-ups (per-project breakdown, subscription cost view, update check, Cursor). Covers AC-10.

## Verification plan

| Check | Where | Command / procedure | Pass |
| --- | --- | --- | --- |
| V-1 | repo root | `dotnet build Track.slnx -c Release` then `powershell -ExecutionPolicy Bypass -File scripts\test.ps1` | Build is clean, all tests pass, and coverage is 100 % line, branch and method. |
| V-2 | tests | Document-mode tests for rewrite, append, truncate and restart from snapshot for each new provider | No double count, and totals equal the hand-computed fixtures. |
| V-3 | demo home | `--render-popover`, `--render-island`, `--render-icons`, `--render-settings` and `--render-analytics` with `TRACK_HOME` and `TRACK_DATA` set to the demo, in light and dark | These are illustrative renders. Existing render paths that use a solid backdrop do not verify native Acrylic or Mica; those require a separate observation on the supported Windows host (V-4). The 3-provider default matches the before images apart from the new gear and link. The 6-provider renders do not clip text. |
| V-4 | this PC (stop the installed Track first) | Run the bin build with `TRACK_DATA` in a temp folder, and use `TRACK_DIAG=1` and `TRACK_POINTER_FILE` where possible. Check theme modes, materials, colors, Copilot, Claude and Codex click targets, hotkey registration, and a balloon test. Ask the user before any real mouse automation. | Each behavior is observed. If a check cannot be run, record it as not run. |
| V-5 | this PC | Enable Ollama and run `ollama run <small model>` if a model is already pulled; otherwise only check server-down behavior | Shows running status with the model, or a quiet idle state. |
| V-6 | GitHub | PR CI on `katsSeges/track` (if workflows run for forks) and the fork CI | Green, or report whether CI needs maintainer approval. |

## Critical review (red-team pass, 2026-11-19)

| Finding | Severity | Resolution | State |
| --- | --- | --- | --- |
| New parsers cannot be checked against real logs on this PC. | Material | R-1: sourced fixtures, PR disclosure and per-provider Off. | Resolved (accepted residual, disclosed) |
| Whole-file logs do not fit the line-based ingest, which risks double counting. | Material | D-2 replace semantics plus V-2. A snapshot version bump is allowed, with the reread consequence. | Resolved |
| "Space optimized" was vague. | Material | AC-4 makes it concrete with tray caps and popover compact rows. | Resolved |
| Analytics "custom range" exceeds retention. | Minor | Cap at 90 days, shown in the UI. | Resolved |
| PR images in an upstream PR need hosting. | Minor | Commit them under `docs/images/pr/` and reference them by fork raw URLs. Maintainers can drop the folder. | Resolved |
| A large single PR may be hard for upstream to review. | Minor | User decision. Use per-phase commits. | Accepted |

## Execution log and deviations

| Date | Task | Result / evidence | Deviation |
| --- | --- | --- | --- |
| 2026-11-19 | Plan audit | Corrected top-session range semantics, distinguished missing/unknown JSON fields and enum values, required additive or migrating snapshot persistence, clarified render-vs-native-material verification, and prohibited shell interpolation of log-derived model IDs. | Evidence-driven coordinator review; implementation paths remain proposals. |
| 2026-11-19 | T-0 | Copied the plan, added `scripts/demo-home.ps1` to write made-up Claude/Codex/Copilot logs, built commit `6a79970` successfully, and rendered 27 baseline PNGs in `docs/images/pr/before/` using that demo home. Viewed the light/dark popover and opened island renders to confirm them. | Reused the app-managed branch rather than creating a second branch. Render PNGs use Track's solid fallback and are illustrative, not proof of native acrylic. |

## Resume instructions

Read `AGENTS`-style rules in `plans/track-subagents.md` ("For Future Agents") and `docs/DEVELOPMENT.md`. Reconcile this
plan with `git log`. Continue with the first unchecked task whose dependencies are done, and record every check
result above.

## Final outcome

Pending.
