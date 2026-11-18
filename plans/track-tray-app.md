# Track — Windows tray app for AI agent usage

Build **Track**, a small .NET 10 WPF tray app for Windows. It shows whether Claude Code, Codex or
GitHub Copilot is running right now. It also shows token usage and an estimated API-equivalent $
cost for today, this week and this month, read directly from each tool's local logs (no ccusage
dependency). All logic has 100% line and branch coverage, enforced by the build.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Agreed Scope (source of truth for decisions)

UI choices made in mockup round 1 `design/track-mockup.html`
(https://claude.ai/artifact/QUpSHmW4EBxaYYQqhA9W3D). `metric` and `tray` were later replaced by round 2
`design/track-mockup-v2.html` (https://claude.ai/artifact/Y3hz91Qwc1LjQN7HQwY4HR): tokens first and logo dots.
Round 2 choices (`arrange`, `idle`, `none`, `count`, `split`, `rowicon`) are pasted below when made:

```json
{ "style": "modern", "layout": "table", "metric": "both", "copilot": "tokens",
  "week": "mon", "refresh": "30s", "tray": "bars", "startup": "yes" }
```

```json
// round 2 — chosen 2026-11-18, plus: cache hits on the in/out line
{ "metric": "tokens-first", "tray": "logo-dots", "arrange": "triangle", "idle": "grey",
  "none": "grey", "count": "all", "split": "price+split+cache", "rowicon": "yes" }
```

Round 3 `design/track-mockup-v3.html` (https://claude.ai/artifact/J8SzTcuD7jqjQVM8GVkrPr), chosen 2026-11-18:

```json
{ "trayLimitWeekM": 5, "trayLimitMonthM": 10, "trayRule": "both", "weekWindow": "lastfull",
  "monthWindow": "lastfull", "whenRunning": "show", "trayFit": "adapt",
  "removeAfterNoUsage": "30d", "modelHover": "full" }
```

| Topic | Decision |
|---|---|
| Stack | .NET 10, WPF (`net10.0-windows`), xUnit, coverlet |
| Coverage | 100% line + branch on `Track.Core` (all logic incl. view-models). Only `Track.App` (tray/XAML shell, P/Invoke) is excluded, and it stays thin. Build fails below 100%. |
| Look | Modern macOS-style popover: rounded 12px, translucent/acrylic, SF-like font fallback (`Segoe UI Variable`), follows Windows light/dark |
| Layout | One table, segmented control `Today / Week / Month`, one row per tool + Total row |
| Metric | **Tokens first**: big token number per row (e.g. `21.4M`), est. $ cost as the small secondary line. Bars and sorting use tokens. The big number counts **all tokens** (input + output + cache write + cache read). The small line is `$14.82 · in 2.4M · out 920k · cache 18.1M`, where `in` = input + cache writes, `out` = output (incl. reasoning), `cache` = cache hits (cache reads), so in + out + cache = the big number. Codex `cached_input_tokens` and Copilot `cacheReadTokens` count as cache hits |
| Copilot | Counted in tokens + $ like the others (no premium-request view) |
| Week | Calendar week, resets Monday 00:00 local time. Month resets on the 1st, 00:00 local |
| Refresh | Usage every 30 s (incremental file reads). Running status every 5 s |
| Tray icon | **Logo dots**: the Claude, Codex and Copilot marks arranged as three dots in the 16 px icon. A mark is drawn in its brand color while that tool runs; idle tools are drawn grey (layout, idle look and nothing-running look are chosen in mockup round 2; default = triangle, grey mark, all three grey). Colors light/dark: Claude `#d97757`/`#e08560`, Codex `#2b2b2e`/`#e4e4ea`, Copilot `#7a4fd6`/`#a98bff`. Tooltip: `Track · Claude Code, Copilot running`. Crisp at 16/20/24/32 px. The same marks are shown at 14 px in the popover's live list and table rows |
| Tool visibility | Round 3 (`design/track-mockup-v3.html`). A tool is left **out of the tray icon** when its usage is below **both** 5M tokens in the last full Mon–Sun week **and** 10M in the last full calendar month (all tokens). It stays in the popover, and it is still shown in the tray while it is running. A tool with **no usage in the last 30 days** and not running is removed from the popover too. With fewer tools the tray icon adapts: 1 → one big mark, 2 → side by side, 3 → triangle; none → grey Track mark |
| Top island | Round 4 (`design/track-mockup-v4.html`, https://claude.ai/artifact/JE5J9ZhazjoTPSSpYnbowW). Optional (on by default, alongside the tray): a black pill at the top center of the screen, like the phone's live activity bar. While an agent works it shows its logo, an activity waveform and a live timer. It hides after everything is idle for a while, comes back when the mouse rests on the very top edge, and opens on hover to show running agents (task timer, session timer, model, session tokens) and today/week/month totals. Chosen 2026-11-18: on with the tray; timer from your last prompt; small pill = logo + waveform + timer + session tokens (`12:34 1.8M`); 2+ agents = main pill + bubble; "done" for 5 s; hide after 5 min idle; 0.6 s top-edge reveal delay; **opens on click** (Escape or clicking again closes it); opened = running agents + today/week/month line; main screen only; hidden over full-screen apps. JSON: `{"island":"on","timerFrom":"turn","compact":"tokens","activity":"wave","multi":"split","done":"show","hideAfter":"5m","reveal":"600","expand":"click","expanded":"agents","monitor":"primary","fullscreen":"hide"}` |
| Review 2026-11-18 | While agents run, the island stays up; a – button in the opened island minimizes it until the top edge peeks it or it is opened again (resets when all work ends). Popover usage rows are sorted by tokens, highest first |
| Model breakdown | Hovering (or keyboard-focusing) a tool in the live list or table opens a small card beside the popover with that tool's models for the selected period: model id, tokens, est. $, and `in · out · cache`, plus a stacked share bar, largest model first |
| Logo assets | Official logo files from each vendor (Anthropic/Claude, OpenAI/Codex, GitHub Copilot), supplied by you into `src/Track.App/Assets/logos/` as SVG. Simplified to single-color silhouettes for 16 px. For your own use only, not redistributed. Until supplied, the mockup's simplified stand-in marks are used |
| Startup | Starts with Windows by default (HKCU `Run` key), toggle in the right-click menu |
| Copilot source | `~/.copilot/session-state` — written by both the Copilot desktop app and the Copilot CLI |
| Pricing | Bundled price table + refresh once per day from LiteLLM `model_prices_and_context_window.json`; offline falls back to cache, then the bundled table. Unknown model → tokens counted, cost shown as `—` |
| Island data | Timers come from work markers in the logs (see Phase 8), so "running" means the agent is working on a prompt, not just that the app is open |
| Out of scope | VS Code Copilot Chat, subscription limit/reset times (not in local logs), multi-machine sync, macOS/Linux |

### Data sources (verified on this machine 2026-11-18)

- **Claude Code**: `%USERPROFILE%\.claude\projects\**\*.jsonl`. Lines with `message.usage`:
  `input_tokens`, `output_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`;
  `message.model`, top-level `timestamp` (UTC ISO). The same message can appear several times
  (streamed chunks, resumed sessions): dedupe on `message.id` + `requestId`.
- **Codex**: `%USERPROFILE%\.codex\sessions\YYYY\MM\DD\rollout-*.jsonl`. Events
  `payload.type == "token_count"` with `info.total_token_usage` (cumulative) and
  `info.last_token_usage`: `input_tokens`, `cached_input_tokens`, `output_tokens`,
  `reasoning_output_tokens`. Use the delta of `total_token_usage` per file (robust to duplicate
  events). Model comes from the latest preceding `turn_context`/`session_meta` line (`"model":"..."`).
  `info` can be null → skip.
- **Copilot**: `%USERPROFILE%\.copilot\session-state\<id>\events.jsonl`. Events
  `type == "session.usage_record"` with `data.usage`: `model`, `inputTokens`, `outputTokens`,
  `cacheReadTokens`, `cacheWriteTokens`, `reasoningTokens`. Live sessions have
  `inuse.<pid>.hold` files (some locked → open with `FileShare.ReadWrite | Delete`, ignore IO errors).
- **Running detection**: processes `claude`, `codex`, `copilot-runtime` (Copilot app also runs
  `copilotapp_proxy`). Shown per tool as: running (process alive) + count of sessions whose log
  was written in the last 2 minutes ("active") + latest model seen.

### Architecture

```
Track.sln
├─ src/Track.Core        net10.0 class library — ALL logic, 100% covered
│   ├─ Abstractions/     IFileSystem (System.IO.Abstractions), TimeProvider, IProcessList,
│   │                    IStartupRegistry, IHttpPriceSource, ISettingsStore
│   ├─ Parsing/          ClaudeLogParser, CodexLogParser, CopilotLogParser → UsageRecord
│   ├─ Ingest/           IncrementalLogReader (per-file offset + length cache), LogScanner
│   ├─ Pricing/          PriceTable, ModelNameNormalizer, LiteLlmPriceLoader, PriceCache
│   ├─ Aggregation/      PeriodCalculator (today/Mon-week/month, local TZ), UsageAggregator
│   ├─ Live/             LiveStatusDetector
│   └─ ViewModels/       PopoverViewModel, ToolRowViewModel, formatters ($ / 1.2M)
├─ src/Track.App         net10.0-windows WPF shell — excluded from coverage
│   ├─ App.xaml(.cs)     single-instance mutex, DI wiring, timers
│   ├─ TrayIcon          H.NotifyIcon.Wpf: left-click toggles popover, right-click menu
│   ├─ PopoverWindow     XAML only, binds to PopoverViewModel; acrylic via DWM
│   └─ WindowsAdapters   real IProcessList / IStartupRegistry implementations (thin)
└─ tests/Track.Core.Tests xUnit + MockFileSystem + FakeTimeProvider + fixture .jsonl files
```

`UsageRecord(Tool, Model, TimestampUtc, Input, Output, CacheWrite, CacheRead, Reasoning, DedupeKey)`.

---

## Phase 1: Repo and build skeleton
Status: Complete

- [x] `git init` in `C:\development\track`; add `.gitignore` (`dotnet new gitignore`) and `.editorconfig`
- [x] Create `Track.slnx` with `src/Track.Core` (classlib), `src/Track.App` (wpf, `net10.0-windows`, `<UseWPF>true</UseWPF>`), `tests/Track.Core.Tests` (xunit)
- [x] Add `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`
- [x] Add packages: Core → `System.IO.Abstractions`; Tests → `System.IO.Abstractions.TestingHelpers`, `Microsoft.Extensions.TimeProvider.Testing`, `coverlet.msbuild`; App → `H.NotifyIcon.Wpf`
- [x] Configure coverage gate in `tests/Track.Core.Tests.csproj`: `CollectCoverage=true`, `Threshold=100`, `ThresholdType=line,branch,method`, `ThresholdStat=total`, `Include=[Track.Core]*`
- [x] Add `scripts/test.ps1` that runs `dotnet test` with the gate and writes `coverage/coverage.cobertura.xml`
- [x] Add one trivial class + test so the gate passes on an empty project

### Verification Plan
- `dotnet build Track.slnx -c Release` → exit 0, 0 warnings
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` → tests pass, coverage reports 100% line and 100% branch for Track.Core
- Temporarily delete the trivial test → `dotnet test` fails with a coverage threshold error (then restore)

### Phase Summary
Done 2026-11-18. Verification results: `dotnet build Track.slnx -c Release` → 0 warnings, 0 errors. `scripts/test.ps1` → 4 tests pass, Track.Core 100% line/branch/method, report at `coverage/coverage.cobertura.xml`. With the unknown-tool test removed the run fails with "total line coverage is below the specified 100" and "total branch coverage is below…" (exit ≠ 0); restored afterwards.

Key facts for later phases:
- .NET 10 SDK 10.0.400 creates `Track.slnx` (XML solution), not `.sln`. Build with `dotnet build Track.slnx`.
- `pwsh` is not installed; scripts target Windows PowerShell 5.1: `powershell -ExecutionPolicy Bypass -File scripts/test.ps1`.
- The coverage gate lives in `tests/Track.Core.Tests/Track.Core.Tests.csproj` (coverlet.msbuild 10.1.1), so every plain `dotnet test` enforces it, not only the script. It also gates method coverage.
- Package versions: System.IO.Abstractions(+TestingHelpers) 23.0.1, Microsoft.Extensions.TimeProvider.Testing 11.0.0, H.NotifyIcon.Wpf 2.4.1, xunit 2.9.3.
- `Tool` enum + `ToolNames.Display` already exist in `src/Track.Core/Tool.cs` (Phase 2's "define Tool" item is done; `UsageRecord` is not).
- `src/Track.App` is still the WPF template (MainWindow); it is replaced in Phase 7. `coverage/` is git-ignored.
- Git repo initialised on branch `main`; nothing committed yet.

## Phase 2: Log parsers
Status: Complete

- [x] Define `UsageRecord`, `Tool` enum (`Claude`, `Codex`, `Copilot`)
- [x] Copy small anonymized fixtures to `tests/Track.Core.Tests/Fixtures/{claude,codex,copilot}.jsonl` (strip message content, keep usage/model/timestamp/ids)
- [x] `ClaudeLogParser.Parse(line)`: returns a record for assistant lines with `message.usage`, null otherwise; dedupe key `message.id:requestId`
- [x] `CodexLogParser` (stateful per file): tracks current model from `turn_context`/`session_meta`; on `token_count` with non-null `info`, emits the delta of `total_token_usage` vs the previous value; treats `cached_input_tokens` as cache read and subtracts it from input
- [x] `CopilotLogParser.Parse(line)`: emits a record for `session.usage_record`; dedupe key = event id (always present in real logs; lines without one are skipped)
- [x] All parsers: ignore blank/malformed JSON lines, missing fields, and unknown event types without throwing
- [x] Tests for every branch: valid line, duplicates, malformed JSON, null `info`, missing model, counters that go backwards (Codex session reset → treat as new baseline)

### Verification Plan
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` → all parser tests pass, coverage gate 100%
- Parser test over the real fixture files asserts exact token totals (hand-computed numbers in the test)

### Phase Summary
Done 2026-11-18. Verification: `scripts/test.ps1` → 68 tests pass, Track.Core 100% line/branch/method; `dotnet build Track.slnx -c Release` → 0 warnings. Fixture totals were computed independently in Python and match the parsers exactly (Claude 17 records / 1,818,300 tokens; Codex 31 / 2,719,045; Copilot 13 / 854,634).

What exists (`src/Track.Core/Parsing/`):
- `UsageRecord(Tool, SessionId, Model, Timestamp, Input, CacheWrite, CacheRead, Output, Reasoning, DedupeKey)` with `Total = Input + CacheWrite + CacheRead + Output`. **Parts never overlap**: `Input` is uncached input only, `Output` includes reasoning, `Reasoning` is informational (already inside Output). Phase 6 display: `in` = Input + CacheWrite, `out` = Output, `cache` = CacheRead.
- `ILogParser.Parse(line)` + `LogParsers.Create(tool, fallbackSessionId)`: one parser instance per file, fed lines in order. Parsers return records but do **not** dedupe; the store (Phase 3) must keep the first record per `DedupeKey`.
- `JsonLine` (internal, `InternalsVisibleTo Track.Core.Tests`): null-safe JSON helpers; negative/non-integer numbers read as 0.

Facts learned from the real logs (2026-11-18):
- Claude: each API response is written once per content block (`apiBlockIndex`) with identical usage → 341 of 461 keys repeat; never across files. One `<synthetic>` model line with zero usage exists → skipped. Key `claude:{message.id}:{requestId}`.
- Codex: `input_tokens` includes `cached_input_tokens`; `output_tokens` includes `reasoning_output_tokens`; `total_tokens = input + output`. `cache_write_input_tokens` was 0 in all 43,511 events (assumed part of input, subtracted). Repeated cumulative events are common (132 in 40 files) → skipped. Model is only in `turn_context.payload.model` (never in session_meta); one event appeared before any turn_context → model `unknown`. Logs live in both `~/.codex/sessions` and `~/.codex/archived_sessions` (no overlapping file names) — Phase 3 must scan both. Key `codex:{session_meta.id}:{cumulative total}`.
- Copilot: `inputTokens` includes `cacheReadTokens` + `cacheWriteTokens`; `outputTokens` includes `reasoningTokens`. Every event has a unique `id`. Models seen: `gpt-6.1-sol`, `claude-opus-5.5` (dotted!), `gpt-6-luna` → Phase 4 normalizer must map `claude-opus-5.5` to `claude-opus-5-5`. Session id = folder name (passed as fallback).
- Fixtures were generated by a one-off script kept outside the repo (it reads private logs); they keep only ids, timestamps, models and usage numbers.

## Phase 3: Incremental ingest
Status: Complete

- [x] `LogScanner`: enumerates log files per tool under `%USERPROFILE%` (roots injectable), only files modified within the retention window (90 days, changed from "current month" because the tray rule needs the last full month)
- [x] `IncrementalLogReader`: remembers byte offset per file; reads only new complete lines; resets when the file shrinks; opens with `FileShare.ReadWrite | FileShare.Delete`; skips files that throw `IOException`/`UnauthorizedAccessException`
- [x] `UsageStore`: holds records for the retention window, dedupes by `DedupeKey`, drops records older than 90 days on every refresh
- [x] Tests with `MockFileSystem`: append lines between scans, partial trailing line, truncated file, locked file, new file appears, records aging out

### Verification Plan
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` → pass, 100% coverage
- Benchmark-style test: second scan of an unchanged 50 MB fixture tree reads 0 bytes (assert reader offset unchanged)

### Phase Summary
Done 2026-11-18. Verification: `scripts/test.ps1` → 97 tests pass, Track.Core 100% line/branch/method; solution build 0 warnings; the 50 MB test reads 0 bytes on the second refresh.

Real-data run (one-off script outside the repo, `TimeProvider.System`, real `%USERPROFILE%`): first refresh 2.8 s, 1,641 MB read from 121 files, 26,628 records; second refresh 36 ms, 0 bytes read. Logs on disk: Codex sessions 1.3 GB + archived 254 MB, Copilot 447 MB, Claude 26 MB.

What exists (`src/Track.Core/Ingest/`):
- `LogSources.Default(home)`: Claude `~/.claude/projects` and `~/.config/claude/projects` (`*.jsonl`, recursive, includes `subagents/`), Codex `~/.codex/sessions` + `~/.codex/archived_sessions` (`rollout-*.jsonl`), Copilot `~/.copilot/session-state/*/events.jsonl` (session = folder name).
- `LogScanner.Scan(modifiedSince)`; missing or unlistable folders are skipped. A file deleted after listing reports 1601-01-01 and is skipped by the time filter.
- `IncrementalLogReader(fs, chunkSize = 1 MB)`: per-file cursor = byte offset + parser instance (Codex parser state survives between reads); streams in chunks, never loads a whole file; handles BOM and CRLF; `BytesRead`, `TrackedPaths`, `Forget(paths)`.
- `UsageStore`: `Add` keeps the first record per key, `Prune(keepFrom)`.
- `RetentionWindow.Start(now)` = now − 90 days (UTC).
- `UsageIngestor(scanner, reader, store, TimeProvider).Refresh()`: scan → read new → add → forget files no longer found → prune.
- Tests use `MockFileSystem` + `FakeTimeProvider`; NSubstitute 6.2.0 added to fake IO errors.

Open point: every app start still reads all 1.6 GB once (2.8 s). Phase 3b adds a saved snapshot so startup only reads what changed since the last run.

## Phase 3b: Saved snapshot (fast start)
Status: Complete

Goal: after a restart (e.g. Windows boot), read only log bytes written since the last run instead of all 90 days of logs.

- [x] Change the store to aggregate buckets: key `(Tool, Model, HourUtc)` → `Input, CacheWrite, CacheRead, Output, Reasoning, Calls`. Hour buckets keep every period exact (for whole-hour time zones) and keep the per-model breakdown; raw records are not kept after they are added
- [x] Per-session totals (`Tool, SessionId` → tokens, first/last timestamp) for sessions active in the last 7 days, for the island's "session tokens"
- [x] Duplicates: done differently than first planned. Dedupe moved into `ClaudeLogParser` (per file, remembers its last 256 keys, saved with the parser state). Store-level dedupe was removed: Claude duplicates never cross files, Codex repeats are skipped by its delta logic, Copilot ids are unique
- [x] Reader cursors become saveable: `FileCursor { Path, Tool, SessionId, Offset, Head (first ≤64 bytes), Parser state }`. After a restart a file is read again from the start if it is shorter than the saved offset or its first bytes changed
- [x] `SnapshotStore`: write `%LOCALAPPDATA%\Track\snapshot.json` (versioned schema, written to a temp file then renamed so a crash never leaves half a file) after a refresh that added data, at most once per minute, and on exit (`SaveSnapshot()`); load on start; a missing, corrupt or older-version file means a full rescan
- [x] Pruning works on buckets (drop hours older than 90 days) and sessions (last used > 7 days ago)
- [x] Tests: save → load round trip gives identical totals; restart reads 0 bytes for unchanged files and only appended bytes for grown ones; corrupt/old-version snapshot → full rescan; truncated/replaced file after restart → reread; Codex delta continues correctly across a restart; duplicate straddling the snapshot is not double counted

### Verification Plan
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` → pass, 100% coverage
- Real-data run: first start (no snapshot) ≈ 2.8 s; second process start with the snapshot reads < 5 MB and finishes in < 300 ms; totals per tool for today/week/month equal between the two runs
- Snapshot file size stays under 2 MB

### Phase Summary
Done 2026-11-18. Verification: `scripts/test.ps1` → 131 tests pass, Track.Core 100% line/branch/method; solution build 0 warnings.

Real-data run (one-off script outside the repo, snapshot in a scratch folder, not `%LOCALAPPDATA%`):
- Fresh start: 3.05 s, 1,642 MB read, 26,649 records → 431 hour buckets, 19 recent sessions, snapshot **169 KB**.
- Second process from the snapshot: **190 ms**, 0.0 MB read (only the ≤64-byte head of each of 121 files), 0 new records. Today/week/month totals per tool were **identical** to the fresh run.

What changed (for later phases):
- `UsageStore` no longer holds records. Read `store.Buckets` (`UsageBucketKey(Tool, Model, HourUtc)` → `TokenCounts`) and `store.Sessions` (`SessionKey(Tool, SessionId)` → `SessionUsage(Tokens, FirstUtc, LastUtc)`). `TokenCounts(Input, CacheWrite, CacheRead, Output, Reasoning, Calls)` + `Total`. Phase 5 aggregates over buckets, Phase 4 prices per bucket model, Phase 8 reads session totals.
- `ILogParser.SaveState()` / `LogParsers.Create(tool, session, state)`; `ParserState(SessionId, Model, Previous[6], RecentKeys)`.
- `IncrementalLogReader.SaveCursors()` / `RestoreCursors()`; `HeadLength = 64`.
- `UsageIngestor(scanner, reader, store, time, snapshots?)`: first `Refresh()` restores, `RestoredFromSnapshot`, `SaveSnapshot()` for app exit, `SaveInterval` 1 min, `SessionRetention` 7 days. The app (Phase 7) must call `SaveSnapshot()` on exit and use `SnapshotStore.DefaultPath(%LOCALAPPDATA%)`.
- Snapshot JSON uses the System.Text.Json source generator (`SnapshotJson`), so it works in trimmed/AOT builds; .NET 10 file-based apps (`dotnet run x.cs`) disable reflection JSON, which surfaced this.
- Hour buckets: "today" is exact for whole-hour UTC offsets (Denmark: +1/+2). Half-hour zones (e.g. India) would be off by up to 30 min at day edges — acceptable, noted.
- Coverlet counts a hidden branch in `foreach` over some collections; `UsageStore` uses index loops / `List.ForEach` to keep branch coverage at 100% honestly reachable.

## Phase 4: Pricing
Status: Complete

- [x] Embed `prices.bundled.json` in Track.Core (LiteLLM subset: every bare `claude-*`/`gpt-*`/`o*`/`codex*` entry plus `github_copilot/*`, 185 models, 27 KB; covers all 8 priced models in the logs)
- [x] `ModelNames.Candidates`: lowercases, strips provider prefixes (`anthropic/`, `openai/`), date suffixes, and tries Claude dotted names with dashes (`claude-opus-5.5` → `claude-opus-5-5`); lookup also tries `github_copilot/<name>`
- [x] `PriceTable.Cost(model, TokenCounts)` (per bucket, since the store keeps buckets): input, output, cache-write, cache-read per-token prices; reasoning tokens billed as output; returns `null` for unknown models
- [x] `PriceLoader` + `LiteLlmPriceDownloader`: fetches `https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json` at most once per 24 h (via `IPriceDownloader`); writes the cache to `%LOCALAPPDATA%\Track\prices.json`; order: cache < 24 h → download → older cache → bundled
- [x] Tests: known model cost to the cent, unknown model → null, fetch failure → cache, no cache → bundled, cache younger than 24 h → no fetch

### Verification Plan
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` → pass, 100% coverage
- Test asserts that the 1M input + 1M output cost of a bundled Claude model equals the hand-computed value

### Phase Summary
Done 2026-11-18. Verification: `scripts/test.ps1` → 168 tests pass, Track.Core 100% line/branch/method; solution build 0 warnings. `claude-opus-5-5` 1M in + 1M out = $24.00 ($4 + $20) asserted, plus 6 other models and cache prices.

Real-data run (one-off script outside the repo, cache in a scratch folder): first load downloaded 4,844 prices in 1.0 s and saved the cache (~5 MB); second load came from the cache in 112 ms. Priced from the Phase 3b snapshot: Claude $39.36 today; Codex $0.01 today / $0.22 week / $13.28 month; Copilot $7.27 / $23.31 / $204.54. 90-day per-model: gpt-5.6-sol $477, gpt-5.6-terra $267, gpt-6.1-sol $236, gpt-6-sol $112, claude-opus-5.5 (Copilot) $61, claude-opus-5-5 $39, gpt-6-luna $0.95.

What exists (`src/Track.Core/Pricing/`):
- `ModelPrice(Input, Output, CacheWrite, CacheRead)` per token in USD; `.Cost(TokenCounts)`. Missing cache prices fall back to the input price.
- `PriceTable.Parse(json)` (null if unusable), `.Bundled`, `.Find(model)`, `.Cost(model, tokens)`, `.Count`.
- `PriceLoader(fs, cachePath, IPriceDownloader, TimeProvider).LoadAsync()` → `LoadedPrices(Table, Source: Bundled|Cache|Download, AsOfUtc)`; `PriceLoader.DefaultCachePath(%LOCALAPPDATA%)`. Never throws on network/disk trouble; the app (Phase 6/7) should call it at start and then every 24 h, and can show "prices from <date>".

Known limits (documented on `PriceTable`): no price for `codex-auto-review` (55M tokens over 90 days) and Codex's `unknown` model → shown as `$—`, so any period containing them needs the `≈` partial marker (Phase 6). Long-context surcharges (GPT above 272k prompt tokens) and Claude's 1-hour cache-write rate ($8 vs $5 per M; Claude Code requests 1-hour caching) are not applied because the store keeps hourly sums, not per-call sizes. Costs are therefore a lower-bound, API-equivalent estimate. Phase 9 compares against ccusage.

## Phase 5: Periods, aggregation and live status
Status: Complete

- [x] `PeriodCalculator(TimeProvider, TimeZoneInfo)`: start of today 00:00, Monday 00:00 of the current week, 1st of the month 00:00 — all local, returned as UTC instants; handles DST changes
- [x] `UsageAggregator`: per tool × period → `UsageTotals { Tokens, KnownCost, UnpricedTokens, Cost (null when nothing priced), IsPartial }`; `Totals(null, range)` gives the Total row
- [x] `LiveStatusDetector`: per tool `LiveStatus { Working (log written ≤ 2 min ago), ActiveSessions, OpenSessions (Copilot live `inuse.<pid>.hold`), ProcessRunning, LastModel, LastActivityUtc }` — see summary for why "running" is based on log writes, not processes
- [x] `UsageAggregator` also returns per tool × period × model totals (same `UsageTotals` shape), sorted by tokens descending
- [x] `PeriodCalculator` gains the visibility windows: last full week (previous Mon 00:00 → this Mon 00:00), last 7 days, last full month, last 30 days, last 90 days
- [x] `ToolVisibilityPolicy` (pure): from week/month usage, `HasUsageInWindow`, running state and settings → `{ InTray, InPopover, Reason }`; settings `TrayLimitWeek` (5_000_000), `TrayLimitMonth` (10_000_000), `TrayRule` (`BelowBoth`/`BelowEither`), `WeekWindow`, `MonthWindow`, `ShowWhenRunning` (true), `RemoveAfterNoUsage` (30 days)
- [x] Tests: Sunday 23:59 vs Monday 00:00 boundary, month boundary, DST transition day, process present/absent, stale hold file with dead pid
- [x] Visibility tests: exactly at the limit (5,000,000 → shown), below one/both limits under each rule, running + below with `ShowWhenRunning` on/off, no usage + running (shown), no usage + idle (removed); model totals sum to the tool total

### Verification Plan
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` → pass, 100% coverage
- Test with `FakeTimeProvider` set to Monday 2026-11-16 00:00 local asserts week total excludes Sunday records

### Phase Summary
Done 2026-11-18. Verification: `scripts/test.ps1` → 205 tests pass, Track.Core 100% line/branch/method; solution build 0 warnings; `At_monday_midnight_the_week_total_leaves_out_sunday` passes.

Real-data run (one-off script): periods for Wed 2026-11-18 in Copenhagen: today 18 Nov 00:00 → 19 Nov, week Mon 16 → Mon 23, month 1 Nov → 1 Dec, last full week 9 → 16 Nov, last full month 1 Oct → 1 Nov. `LiveStatusDetector.Detect()` took 91 ms. Claude: working (this session), model claude-opus-5-5, today 98.8M tokens ≈ $42. Codex idle since 10:11, Copilot idle since 18:48 with 6 open sessions (hold files).

What exists:
- `src/Track.Core/Usage/`: `TimeRange` (half-open UTC), `Period` (Today/Week/Month), `WeekWindow`, `MonthWindow`, `PeriodCalculator.Range(...)` / `LastDays(n)` / `ToUtc(local)` (skipped midnight → first valid moment; repeated midnight → first occurrence). `UsageAggregator(store) { Prices }` → `Totals(tool?, range)`, `ByModel(tool, range)`, `LatestModel(tool)`. `ToolVisibilityPolicy.Decide(...)` (pure + store-backed overload), `VisibilitySettings`, `VisibilityReason`.
- `src/Track.Core/Live/`: `IProcessList` + `SystemProcessList` (real, tested against the test process), `LiveStatusDetector(fs, scanner, copilotRoot, processes, usage, time).Detect()`, `ProcessNames` per tool.
- `LogFile` now carries `LastWriteUtc` (set by `LogScanner`).

Decisions and findings:
- "Running" in the UI = `Working` (log written in the last 2 min), not "process alive": `claude.exe` is also the Claude desktop app (22 processes open while idle), and Codex/Copilot apps stay open too. `ProcessRunning` is reported separately. Phase 8 refines "working" with turn start/end markers.
- Money must be formatted with the invariant culture/`$` in Phase 6: the machine culture is Danish and `C2` printed "kr.".
- Tray rule fix (applied 2026-11-18 under the "finish the whole plan" goal, recommended in chat, no answer given): with only *last full* week/month, a tool used heavily only this week (Claude: 98.8M today, logs start 18 Nov) counted as "below limits". `VisibilitySettings.IncludeCurrentPeriod` (default **true**) now takes the larger of last full week and this week, and of last full month and this month. Set it to false for the rule exactly as chosen in round 3. Tested both ways (206 tests, 100%).

## Phase 6: View-models and settings
Status: Complete

- [x] `PopoverViewModel` (INotifyPropertyChanged, no WPF types): `SelectedPeriod`, `Rows` (Claude, Codex, Copilot, Total), `Live` rows, `UpdatedText` ("Updated 12s ago"), `ResetText` ("Week resets Mon 00:00 · every 30 s"), commands `SelectPeriod`, `Refresh`
- [x] `ToolRowViewModel`: name, logo key, color key, `TokenText` (primary, all tokens: `21.4M`, `920k`), `SubText` = `$14.82 · in 2.4M · out 920k · cache 18.1M` (`$—` for unknown price, `≈$` when partial; a part that is 0 shows `0`), `BarFraction` from tokens relative to the largest row
- [x] `PopoverViewModel` only includes tools with `InPopover`; hides the Total row when one tool is listed; shows "No Claude Code, Codex or Copilot usage in the last 30 days." when none are listed
- [x] `ModelBreakdownViewModel` for the hover card: tool name, period label, rows of `{ Model, TokenText, CostText, SplitText, Share }`, built from the per-model totals of the selected period; empty rows when the tool has 0 tokens in that period
- [x] `UsageTotals` exposes `Input` (input + cache write), `Output` (output + reasoning), `CacheHits` (cache read), `Total` (sum of the three) and `Cost`; Total row sums each part
- [x] `RefreshCoordinator`: usage timer 30 s, live timer 5 s, no overlapping refreshes, exceptions reported to the VM as a status line instead of crashing
- [x] `Settings` (JSON at `%LOCALAPPDATA%\Track\settings.json`): `StartWithWindows` (default true), `LastPeriod`; `StartupManager` applies it through `IStartupRegistry`
- [x] `TrayIconModel` places only tools with `InTray`: 1 → centered mark size 14.5, 2 → (4.2,8) and (11.8,8) size 8, 3 → triangle; 0 → grey Track mark
- [x] `TrayIconModel` (pure, in Core): from live status + icon settings + taskbar theme → `TrayIconSpec` = list of `LogoPlacement(Tool, CenterX, CenterY, Size, State: Running|Idle)` and idle shapes (dot/ring), in 16-unit coordinates, plus `Tooltip` and `CacheKey` (settings × running set × theme × size). Geometry matches `trayIcon()` in `design/track-mockup-v2.html` (triangle centers (8,4.1), (3.9,11.9), (12.1,11.9), size 7.8; row; running-only with 1/2/3 layouts)
- [x] Settings gain `IconLayout` (`Triangle` default, `Row`, `RunningOnly`), `IdleLook` (`GreyMark`, `Dot`, `Ring`), `NothingRunningLook` (`AllGrey`, `TrackMark`), `RowLogos` (bool)
- [x] Tests for every formatter branch, timer overlap guard, settings missing/corrupt file → defaults
- [x] `TrayIconModel` tests: all 8 running combinations × 3 layouts × 3 idle looks × 2 nothing-running looks × 2 themes produce the expected placements; running-only with 0 running falls back to the nothing-running look; tooltip text for 0/1/3 running; equal states give equal `CacheKey`
- [x] Formatter tests: `SubText` for known/unknown/partial price, zero parts, k/M boundaries (999k → `999k`, 1,000k → `1.0M`); test that in + out + cache equals the total for every tool and period

### Verification Plan
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` → pass, 100% coverage
- VM snapshot test: fixture data → asserts exact `CostText`/`TokenText` strings for all 4 rows × 3 periods

### Phase Summary
Done 2026-11-18. Verification: `scripts/test.ps1` → 282 tests pass, Track.Core 100% line/branch/method; solution build 0 warnings. The VM snapshot test (`Snapshot_of_all_rows_for_today_week_and_month`) asserts the exact token and `$… · in … · out … · cache …` strings for Claude, Codex, Copilot and Total in Today, Week and Month, hand-priced from the bundled list (e.g. Claude today `21.4M` / `$25.60 · in 3.0M · out 400k · cache 18.0M`, Codex month `≈$2.45` because `codex-auto-review` has no price).

What exists:
- `src/Track.Core/Display/Format.cs`: `Tokens` (`0`, `999`, `1k`, `999k`, `1.0M`, `999.9M`, `1.0B`), `Cost` (`$14.82`, `≈$…`, `$—`; invariant culture, tested under da-DK), `Split`, `SubLine`, `Ago` (`just now`, `12s ago`, `41 min ago`, `3 h ago`, `2 d ago`), `Timer` (`0:07`, `59:59`, `1:00:00`).
- `UsageTotals` gained `InTokens` (input + cache write), `OutTokens`, `CacheHits` (in + out + cache = total).
- `src/Track.Core/Display/ViewModels.cs`: `ObservableObject`, `RelayCommand`, `PopoverViewModel` (`SelectedPeriod`, `PeriodLabel`, `Rows`, `TotalRow` (null for ≤ 1 tool), `LiveRows`, `Visibility`, `EmptyText`, `UpdatedText` + `Tick()`, `ResetText`, `StatusText`, `RowLogos`, `SelectPeriodCommand`, `RefreshCommand` → `RefreshRequested`, `UsageRefreshed()`, `LiveUpdated(live)`, `ApplySettings()`, `ModelBreakdown(tool)`), `ToolRowViewModel`, `LiveRowViewModel`, `ModelRowViewModel`, `ModelBreakdownViewModel`.
- `src/Track.Core/Display/RefreshCoordinator.cs`: usage every `RefreshSeconds` (30), live every 5 s, overlap guard, errors → `reportStatus("Usage refresh failed: …")`, null after success. Callbacks run on timer threads: the WPF shell must marshal to the UI thread.
- `src/Track.Core/Settings/TrackSettings.cs`: `TrackSettings` (StartWithWindows, LastPeriod, RefreshSeconds, IconLayout, IdleLook, NothingRunningLook, RowLogos, Visibility), `SettingsStore` (`%LOCALAPPDATA%\Track\settings.json`, defaults on missing/broken), `IStartupRegistry`, `StartupManager.Apply(bool)` (writes only when different; command is the quoted exe path).
- `src/Track.Core/Tray/TrayIconModel.cs`: `TrayIconModel.Build(inTray, running, layout, idle, nothingRunning, theme)` → `TrayIconSpec(Shapes, Tooltip, CacheKey)`; `IconShape(Kind: Logo|Dot|Ring|TrackBars, Tool, X, Y, Size, Color, Opacity)` in 16-unit coordinates; `BrandColor`, `Ink`. 1 tool → (8,8) size 14.5; 2 → (4.2,8)/(11.8,8) size 8; 3 → triangle or row; running-only with nothing running falls back to the nothing-running look; no tools → Track bars.

Findings:
- System.Text.Json's source generator sets *every* `init` property on deserialize, so fields missing from `settings.json` became 0/false/null (would have turned off "start with Windows" for older files). Settings records now use `{ get; set; }`; tests cover `{}` and partial files.
- Live row text: working → `2 sessions · claude-opus-5-5`; idle → `idle · 41 min ago`; no activity in a day → `idle`.



## Phase 7: WPF tray shell (modern mac-style)
Status: Complete — all checks verified except the tray icon live at 150 % and 200 % Windows scaling, which the user chose to **skip** (2026-11-18). Verified live at 100 %; 24/32 px renders exist; Track reads the current scaling live.

- [x] `App.xaml.cs`: single-instance named mutex, build services, start `RefreshCoordinator`, no main window
- [x] Logo assets: put the vendor SVGs in `src/Track.App/Assets/logos/{claude,codex,copilot}.svg` (ask the user for the files if missing; otherwise use the stand-in marks from `design/track-mockup-v2.html`); convert each to a single-color WPF `Geometry` resource in `Logos.xaml` so they can be tinted (brand color when running, grey when idle)
- [x] Tray icon (`H.NotifyIcon.Wpf`); left-click toggles the popover; right-click menu: Refresh, Icon layout (Triangle / Row / Running only), Start with Windows (checkable), Quit
- [x] `TrayIconRenderer` (shell, excluded): draws a `TrayIconSpec` by placing the logo geometries with WPF `DrawingVisual` → `RenderTargetBitmap` at the current DPI size (16/20/24/32) → `Icon`; snaps to whole pixels so marks stay sharp; caches by `CacheKey`; swaps the icon and tooltip only when the running set changes (checked every 5 s); re-renders on taskbar theme or DPI change; static `Assets/track.ico` for the exe/Startup entry
- [x] Model hover card: a second borderless acrylic window (268 px) shown to the left of the popover, aligned with the hovered row, after a 150 ms hover delay or on keyboard focus; hides on mouse leave, Escape or popover close; flips to above the popover if there is no room on the left
- [x] Popover: 14 px logos in the live list (tinted when running, grey + muted text when idle, green `running` pill) and in table rows when `RowLogos` is on
- [x] `PopoverWindow.xaml`: 320 px wide, borderless, rounded 12 px, acrylic backdrop (`DwmSetWindowAttribute` DWMWA_SYSTEMBACKDROP_TYPE = transient window, fallback solid), drop shadow; positioned above the tray icon using the work area; closes on deactivate/Escape
- [x] Layout per mockup round 2: header "Track" + updated text; live box (logo, name, `running` pill, model, sessions); segmented Today/Week/Month; table with logo, token bar, big token number and small price line; Total row; footer with reset text + Refresh
- [x] Theme: follow Windows `AppsUseLightTheme`, two `ResourceDictionary` palettes matching the mockup tokens
- [x] `WindowsAdapters`: `ProcessList` (Process.GetProcessesByName), `StartupRegistry` (HKCU `...\CurrentVersion\Run\Track`) — each method ≤ 5 lines, marked `[ExcludeFromCodeCoverage]` with a comment
- [x] List every excluded file in `docs/coverage-exclusions.md`

### Verification Plan
- `dotnet build -c Release` → 0 warnings; `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` still 100%
- Launch `src/Track.App/bin/Release/net10.0-windows/Track.exe`; `Get-Process Track` → 1 process; launch again → still 1 process (mutex)
- Screenshot the popover in light and dark theme; compare against the mockup artifact (layout, spacing, rows)
- Start a `claude` session → within 5 s the Claude mark in the tray icon turns orange and the tooltip says `Claude Code running`; close it → the mark turns grey. Screenshot the tray at 100%, 150% and 200% scaling and compare with the "Tray icon at real size" section of mockup round 2
- Hover the Claude row → card lists Claude models whose tokens add up to the row total; screenshot compared with mockup round 3
- With a tool that has no logs at all (e.g. rename `~/.codex/sessions` temporarily) → Codex is gone from the popover and the tray icon; restore it afterwards
- Popover screenshot: token number is the large figure on every row; the small line underneath reads `$… · in … · out … · cache …` and fits on one line at 330 px
- `Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run -Name Track` → path to exe after first start

### Phase Summary
Done 2026-11-18. Verification results:
- `dotnet build Track.slnx -c Release` → 0 warnings, 0 errors; `scripts/test.ps1` → 282 tests, Track.Core 100 % line/branch/method.
- Launched `src/Track.App/bin/Release/net10.0-windows/Track.exe` → 1 process; launched again → still 1 (named mutex `Local\Track.TrayApp.SingleInstance`).
- Run key after first start: `HKCU\...\Run\Track` = `"C:\development\track\src\Track.App\bin\Release\net10.0-windows\Track.exe"` (Phase 9 install repoints it).
- Real data (`TRACK_DIAG=1` → `%LOCALAPPDATA%\Track\diag.txt`): tooltip `Track · Claude Code, Copilot running`, Claude mark `#D97757` at opacity 1, Codex grey 0.38; prices downloaded; rows e.g. `Claude Code | 116.9M | $48.31 · in 2.0M · out 781k · cache 114.1M`.
- Fake home without Codex logs (`TRACK_HOME`/`TRACK_DATA` in the scratchpad, `--no-startup`): Codex gone from popover and tray, the two remaining marks side by side at (4.2,8)/(11.8,8) size 8; costs hand-checked ($8.00 and $1.50). Marks colored while logs were written → **grey after 2 min idle** (`Track · nothing running`, `idle · 2 min ago`) → Claude colored again within 5 s of a new write.
- Popover and model card in light and dark: rendered with `Track.exe --render-popover <dir> --theme light|dark` (the real windows, bindings and data) because the workstation was **locked** during verification (LogonUI running; screen captures came back black). The acrylic blur is not in those images (rendered over the fallback tint). Layout matches mockup rounds 2/3; the `$… · in … · out … · cache …` line fits on one line at 330 px.
- Tray icon at 100/150/200 % = 16/24/32 px: `Track.exe --render-icons <dir>` renders for 6 running states × 2 taskbar themes match mockup round 2's "Tray icon at real size". **Live in the taskbar: verified at 100 % only** (Follow-up). **150 % and 200 % live: skipped at the user's request** (2026-11-18) — changing Windows scaling is a system setting Claude may not change, and the user chose not to change it. Not verified; the renders are not a substitute for it.
- Hover card: Claude card 120.6M = Claude row 120.6M; Copilot card models sum to the Copilot row.

Bugs found and fixed during verification:
1. **Crash after ~2 min**: H.NotifyIcon disposes the previous `Icon` when a new one is set; the renderer handed back a cached instance, so returning to an earlier icon state threw `ObjectDisposedException` (event log, `TrayController.UpdateTrayIcon`). `TrayIconRenderer.Icon` now returns a clone; `App` also logs unhandled UI exceptions to `<data>\errors.log` and keeps running. Re-run: no crash, no errors.log.
2. **Layout**: the live list's detail text covered the `running` pill, and the long price line squeezed tool names ("Claud"). Rows are now: name … big tokens / price · in · out · cache (full width) / usage bar; live detail trims with an ellipsis; model card shows tokens + cost on line 1 and the split on line 2.

What exists (`src/Track.App`, AssemblyName `Track`):
- `App.xaml(.cs)`: no main window, `ShutdownMode=OnExplicitShutdown`, mutex, error log. `app.manifest`: PerMonitorV2 DPI. `Assets/track.ico` (three-bar mark, 16–256 px).
- `Themes/Light.xaml`, `Themes/Dark.xaml` (mockup tokens; brushes `Claude`/`Codex`/`Copilot` per theme), `Themes/Styles.xaml`.
- `Shell/TrayController.cs`: composition root. Background refreshes and UI reads share one lock around the store; usage refresh every `RefreshSeconds`, live every 5 s, prices at start + every 6 h; saves snapshot + settings on exit; follows Windows theme changes (`SystemEvents.UserPreferenceChanged`). Right-click menu: Refresh, Icon layout (Triangle/Row/Running only), Start with Windows, Quit.
- `Shell/PopoverWindow.xaml`, `Shell/ModelCardWindow.xaml`: acrylic (`DWMWA_SYSTEMBACKDROP_TYPE`=3) with solid fallback, rounded corners, tool windows (card never takes focus), 150 ms hover delay, keyboard focus opens the card.
- `Shell/TrayIconRenderer.cs`, `Shell/Logos.cs` (stand-in marks; `Assets/logos/README.md` explains swapping in official SVGs; the user was not asked for files under the "finish the plan" goal), `Shell/Native.cs` (DWM, cursor, DPI, theme registry, `StartupRegistry`).
- Verification switches: env `TRACK_HOME`, `TRACK_DATA`, `TRACK_DIAG=1`; flags `--open`, `--card <tool>`, `--theme light|dark`, `--render-icons <dir>`, `--render-popover <dir>`, `--no-startup`. `scripts/screenshot.ps1` captures the tray corner when the screen is unlocked.
- Deviation: the plan's App-side `ProcessList` adapter was not needed; Track.Core's tested `SystemProcessList` is used. `docs/coverage-exclusions.md` lists every excluded file and how it is checked instead.

## Phase 8: Top island
Status: Complete

Agent work markers (verified in local logs 2026-11-18):
- Claude: turn starts at a `"type":"user"` line whose content is not a `tool_result`; ends at an assistant line with `"stop_reason":"end_turn"` (or a `stop_hook_summary` system line). Session start = first line's `timestamp`.
- Codex: `event_msg` payload `task_started` → working; `task_complete` or `turn_aborted` → done. Session start = `session_meta` timestamp.
- Copilot: `user.message` → working; `session.task_complete` (or the last `assistant.turn_end` with no newer `user.message`) → done. Session start = `session.start`/`session.resume`; `session.shutdown` ends it.

- [x] Parsers also emit `ActivityEvent(Tool, SessionId, Kind: SessionStart|TurnStart|TurnEnd|SessionEnd, TimestampUtc)` from the markers above
- [x] `AgentActivityTracker` (Core): per session → `{ Working, TurnStartedUtc, SessionStartedUtc, Model, SessionTokens }`; a working session whose log has not changed for 10 min and whose process is gone counts as ended (crash/kill)
- [x] `IslandStateMachine` (Core, `TimeProvider`): states `Hidden`, `Idle`, `Active`, `Done`, `Revealed`, `Open`; inputs: activity changes, `HideAfter` elapsed, top-edge dwell ≥ reveal delay, pointer enter/leave island, click, Escape, full-screen app detected; `Done` lasts 5 s
- [x] `IslandViewModel` (Core): compact content for 1 agent and for 2+ (`Split` main + bubble / `Merged` / `Cycle` every 4 s), timer text (`12:34`, `1:02:10`) from turn or session start, opened content (agent rows + today/week/month), settings from round 4
- [x] Tests: every state transition, timer formatting at 59:59 → 1:00:00, cycle order, reveal delay not reached, full-screen hides it, island off → never visible, Claude tool_result lines don't start a turn, Codex `turn_aborted` ends a turn
- [x] Shell `IslandWindow` (excluded): borderless, transparent, topmost, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (never steals focus, not in Alt+Tab), centered at the top of the chosen screen, spring animation for show/hide/open, 1-second timer tick only while visible
- [x] Shell top-edge detection: poll `GetCursorPos` every 100 ms instead of an invisible hot-zone window, so clicks on browser tabs and title bars are never blocked; full-screen detection via `SHQueryUserNotificationState`
- [x] Right-click tray menu gains "Show top island" (checkable)

### Verification Plan
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` → pass, 100% coverage
- Start a Claude prompt → island appears within 5 s with the Claude logo and a timer counting from the prompt; when Claude finishes → "Claude Code done" for 5 s, then idle
- Nothing running for 5 min → island gone; rest the mouse on the top edge ≥ 0.6 s → it slides in; a shorter touch does nothing; move away → it hides
- Click the island → it opens with agent rows; click again or press Escape → it closes; hovering alone does not open it
- Click a browser tab at the very top of a maximized window → the click lands in the browser (island does not block it)
- Play a full-screen video → island hidden; Alt+Tab list does not contain Track
- Screenshots compared with mockup round 4 (compact, two agents, opened)

### Phase Summary
Done 2026-11-18. Verification results:
- `scripts/test.ps1` → **347 tests**, Track.Core 100 % line/branch/method; `dotnet build Track.slnx -c Release` → 0 warnings.
- End-to-end on a fake home (`TRACK_HOME`/`TRACK_DATA` in the scratchpad, `TRACK_DIAG=1`, pointer driven through `TRACK_POINTER_FILE` because the workstation was locked):
  - Claude prompt 754 s old → island **Active** `12:40 · 6.0M`, Claude logo, top-center pill 171×56 px at y=0 (x 875–1046 of 1920).
  - `end_turn` appended → **"Claude Code done" 12:45** within 5 s → **Idle** `today 6.0M` 5 s later.
  - Idle for the hide time → **Hidden**, window not visible (checked with the real 5 min in the first run, 1 min test setting in the second).
  - Top edge 0.3 s → stays hidden; 1.5 s → **Revealed** (slides in); hovering the island 1.5 s → still Revealed (hover does not open, click mode); click → **Open** ("Nothing running", 432×127); click again → closed; pointer away → Hidden.
  - New prompt → Active with the timer from that prompt (`0:04`); "full-screen" → Hidden; back → Active.
  - Only one visible Track window (the island) and it has `WS_EX_TOOLWINDOW` → not in Alt+Tab. No `errors.log`, process alive.
  - Click-through: there is no hot-zone window (the top edge is polled with `GetCursorPos` every 100 ms); the island window is only the pill's size and is hidden when the island is hidden, so clicks on browser tabs beside/under it reach the app below.
- Renders compared with mockup round 4 (`Track.exe --render-island <dir>`, real state machine + view model): one agent (logo, waveform, `12:34`, tokens), two agents (main pill + Copilot bubble), opened (agent rows with task + session timers, `today … · $…`, `week … · month …`), done (`Copilot done ✓ 1:37`), idle, opened with nothing running.
- Correction: when this summary was first written, **Escape**, a **real full-screen app** and the **browser-tab click** had only been simulated (locked screen) and the phase was marked Complete too early. All three were later run with real input and passed only after fixing bugs they exposed (Escape did nothing; click-elsewhere stopped closing; the test override masked real full-screen detection) — see "Follow-up" below. Phase 8 counts as verified from that point.

What exists:
- `src/Track.Core/Activity/`: `ActivityKind`, `ActivityEvent`, `ClaudeActivityParser` (prompt = non-meta user text that is not a `tool_result`; ends on `end_turn`, `stop_hook_summary`, or "[Request interrupted by user…]"), `CodexActivityParser` (`task_started` / `task_complete` / `turn_aborted`), `CopilotActivityParser` (`user.message` → TurnStart, `assistant.turn_start/end` → StepStart/StepEnd with a 15 s grace, `session.task_complete` → TurnEnd, `session.start/resume/shutdown`), `AgentActivityTracker` (stale: silent ≥ 10 min with process gone, or ≥ 30 min; forget after a day; longest-running first), `ActivityReader` (incremental over logs written in the last day; maps each file to the sessions inside it, so Codex `session_meta.id` and Claude subagent files work).
- `src/Track.Core/Ingest/LineReader.cs`: shared chunked line reader (usage reader uses it too).
- `src/Track.Core/Island/Island.cs`: `IslandSettings` (round 4 defaults), `IslandStateMachine` (Hidden/Idle/Active/Done/Revealed/Open; `Update`, `PointerAtTop`, `PointerOverIsland`, `Click`, `Escape`, `Tick`), `IslandViewModel.Build()` → `IslandContent`. `TrackSettings.Island` holds the settings.
- `src/Track.App/Shell/IslandWindow.cs` + wiring in `TrayController` (activity on the 5 s live refresh, 100 ms pointer tick, "Show top island" menu item, island line + bounds in `diag.txt`).

Bugs found and fixed during verification: `TextBlock.FontFamily = null` threw on every draw (caught by the error log, render never finished) → explicit Sans family; a PowerShell edit re-encoded `IslandWindow.cs` and broke "✓"/"·" → restored as UTF-8 (repo scanned, no other file affected); `diag.txt` only refreshed every 5 s → now also written when the island changes state.

Known behavior: when one agent finishes while another is still working, the island stays Active for the other one (no "done" note for the first). Island screen choice "All" uses the primary screen; "Mouse" follows the pointer's monitor.

## Phase 9: Real-data check, packaging, docs
Status: Complete

- [x] Cross-check: today's Claude tokens from Track vs `npx ccusage@latest daily --since <today>` (within 1%); same for Codex with `npx @ccusage/codex@latest daily`; document any differences and why
- [x] Measure: idle RAM < 60 MB, CPU ~0% between refreshes, one refresh over the current month's logs < 500 ms
- [x] `dotnet publish src/Track.App -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o publish/`
- [x] `scripts/install.ps1`: copies `publish/` to `%LOCALAPPDATA%\Programs\Track`, sets the Run key, starts the app; `scripts/uninstall.ps1` reverses it
- [x] `README.md`: what it reads, how costs are estimated (API-equivalent, not your bill), how to build/test/install
- [x] User review: island stays up while agents run, with a minimize (–) button and top-edge peek
- [x] User review: popover usage rows sorted highest to lowest
- [x] Tray icon checked live at 150 % and 200 % Windows scaling — **skipped by the user** (2026-11-18); not verified

### Verification Plan
- `powershell -ExecutionPolicy Bypass -File scripts/install.ps1` → `Track.exe` running from `%LOCALAPPDATA%\Programs\Track`, Run key set
- `powershell -ExecutionPolicy Bypass -File scripts/uninstall.ps1` → process gone, folder and Run key removed
- `powershell -ExecutionPolicy Bypass -File scripts/test.ps1` → final run 100% line + branch

### Phase Summary
Done 2026-11-18. Verification results:
- **ccusage cross-check** (`npx ccusage@latest daily --since 20261118 --json`, run right after Track's own count): Claude `claude-opus-5-5` today **143,598,000 tokens in both**, every part identical (input 1,308 · cache write 2,070,675 · cache read 140,640,864 · output 885,153) → 0 % difference. Codex `gpt-5.6-luna` 25,627 in both. Differences, explained: cost $56.19 (Track) vs $59.53 (ccusage) = Claude's 1-hour cache-write rate that Track does not apply (1.11M 1h-cache tokens × $3/M); ccusage (v20) shows Copilot only as a stored $5.90 with 0 tokens, Track reads the Copilot app's token records (53.6M today). **Correction (2026-11-18, usage-charts plan Phase 1):** ccusage does read Copilot tokens, but it puts a whole Copilot session on its last active day, so its Copilot numbers per day differ from Track's per-call dating; Track's per-day Copilot numbers match an independent count exactly. `@ccusage/codex` is deprecated (needs Node 22, "use npx ccusage") — the main ccusage report covers Codex.
- **Resources** (real data, published-equivalent Release build): idle (island hidden, nothing to show) **0.34 % of one core, 45.8 MB working set / 48.9 MB private** ✓. While the island shows and animates: ~0.4 % CPU, ~80–116 MB working set; after it hides its window closes and the working set drops to ~24 MB at once (private memory is returned later). Usage refresh: 37 ms incremental, 190–254 ms on start from the snapshot ✓ (< 500 ms); the one-off first read without a snapshot (90 days, 1.6 GB) takes ~3 s.
- **Publish**: `dotnet publish src/Track.App -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o publish/` → `Track.exe` 1.2 MB (+ pdbs).
- **Install / uninstall**: `scripts/install.ps1` → `Track.exe` running from `%LOCALAPPDATA%\Programs\Track`, Run key = that path (the app re-applies the same value on start). `scripts/uninstall.ps1` → process gone, folder gone, Run key gone, data in `%LOCALAPPDATA%\Track` kept (`-RemoveData` removes it). Track was **re-installed at the end** (`install.ps1 -Rebuild`) and is running for the user.
- Final `scripts/test.ps1` → **361 tests**, Track.Core 100 % line/branch/method; solution build 0 warnings; no `errors.log`.

Performance fixes made to reach the targets (all tested):
1. The island's first read of today's logs allocated **358 MB** (every line JSON-parsed): activity parsers now skip lines that cannot hold a marker (Claude: only prompts, turn ends and, until known, the model; tool-result lines skipped; Codex/Copilot: marker words).
2. `SystemProcessList` takes one process snapshot per 2 s instead of 5 `GetProcessesByName` calls (35 ms → ~8 ms).
3. `LogScanCache`: full folder scan once a minute, or at once when a `FileSystemWatcher` sees a new log; otherwise only the known files' write times. Live status and activity share it. Copilot hold files are looked for only in each session folder.
4. App: workstation non-concurrent GC, `System.GC.ConserveMemory=7`, a compacting collection + working-set trim after the startup reads and every 30 min.
5. Island: the blurred `DropShadowEffect` was redrawn on every waveform frame (+0.8 % CPU, +55 MB) → 1 px hairline; waveform/pulse capped at 20 fps; timers update in place instead of rebuilding the pill every second; the island window closes when hidden and is recreated on demand.

**Real screenshots** (the screen was unlocked late in this phase): popover with the acrylic backdrop above the tray (tray icon visible in the taskbar), the island at the top center (`1:09:15 · 90.8M` for this session), and the opened island with the new – button. A focus-ring bug showed up there (the "Today" tab looked selected while "Month" was): the segmented control now shows its ring only for keyboard focus.

**User review (2026-11-18), done in this phase:**
1. *"Island should not close when something is running, unless I minimize it with a minimize button; then I can get it back by going up there."* → `IslandStateMachine.Minimize()` + `IsMinimized`, `IslandContent.CanMinimize`, a – button in the opened island. While agents run the island stays up; minimized it hides until the top edge peeks it (0.6 s) or the user opens it again (click during a peek ends minimize); when all work ends minimize resets to the normal done/idle/hide flow. Verified end to end (fake home + pointer file `minimize` command): running → open → minimize → hidden (still hidden after the next refresh) → top edge peeks → away hides → peek + click opens (not minimized) → click closes and it stays up → task ends → "Claude Code done".
2. *"Sort the data usage by highest to lowest."* → popover rows ordered by tokens of the selected period, highest first (ties keep Claude, Codex, Copilot), Total row last. Snapshot test updated; new sorting test.

Finding for the user: clicks that land on the visible island pill (e.g. reaching for a browser tab right under it at the top center) open it — that is the chosen click-to-open behavior; the transparent margin around the pill passes clicks through, and the island takes no space when hidden or minimized.

## Follow-up: checks redone with real input (2026-11-18, screen unlocked)
The checks below had been replaced by simulations because the workstation was locked. They were run again
with real mouse clicks, a real Escape key press and a real full-screen window (`real-checks.ps1` in the
session scratchpad). A test "maximized browser" (borderless window filling the work area, tab strip from y = 0)
recorded every click it received; the island was kept up by a Claude prompt in a fake home.

| Check | Result |
|---|---|
| Click a tab at (120,12), far from the island | browser got it ✓ |
| Click inside the island window's transparent margin, 6 px left of the pill | browser got it ✓ |
| Click on the pill | island opened, Track became the foreground app ✓ |
| Real **Escape** key | island closed (back to Active) ✓ |
| Open again, then click a tab | island closed **and** the browser got the click ✓ |
| Minimize, then click where the pill was | no Track window there any more; browser got it ✓ |
| Real full-screen window in front | Windows reported state 2 (busy app), island **Hidden**; back to Active when it closed ✓ |
| Tray icon at the current Windows scaling (100 %, 16 px) | real taskbar screenshot: Claude mark orange (working), Codex and Copilot grey ✓ |
| Tray icon at 150 % and 200 % | **skipped by the user**: it needs Windows display scaling changed, a system setting Claude may not change; not verified (renders at 24/32 px exist) |

Bugs these real checks found, now fixed (shell only; Track.Core unchanged, 361 tests still 100 %):
1. **Escape did nothing**: the click lands on a no-activate window, so Windows refused `Activate()`; WPF even reported `IsActive = true`. The island now brings itself to the front (joining the foreground thread's input for one call) when it switches to opened, only on that switch.
2. **Click elsewhere stopped closing the opened island** after the first time (WPF raised no `Deactivated` for this tool window). The 100 ms pointer check now closes it when the real left button goes down outside it.
3. **The test full-screen override hid real detection**: with `TRACK_POINTER_FILE` present, its absent "fullscreen" line forced "not full screen". It now only adds (`override || real`).
4. **Tray icon size used the sign-in scaling** (`GetDpiForSystem`), so a scaling change would not resize the icon until the next sign-in. It now reads the main monitor's current scaling (`GetDpiForMonitor`); the 5 s icon check redraws when the size changes.

Track was rebuilt and reinstalled with these fixes and is running from `%LOCALAPPDATA%\Programs\Track`.

## Final Recap
All phases are complete. Every check was verified except one the user chose to skip: Phase 7's tray icon live at 150 % and 200 % Windows scaling (verified live at 100 % only).

Track is a .NET 10 WPF tray app for Windows that shows whether Claude Code, Codex and GitHub Copilot are
working and how many tokens (with an API-equivalent price) each used today, this week (Monday reset) and
this month, read straight from the agents' local logs. It has no ccusage dependency, and its counts match
ccusage to the token for Claude and Codex.

- **Track.Core** (all logic, 361 tests, 100 % line/branch/method coverage enforced by the build): log
  parsers with per-tool token normalization and duplicate handling; chunked incremental ingest with a
  169 KB snapshot (a restart reads ~0 bytes in ~0.2 s instead of 1.6 GB in 3 s); hourly per-model buckets
  and per-session totals; LiteLLM pricing (bundled + daily cache); DST-safe local periods; the tray
  visibility rule; live status; work-marker tracking for the island; the island state machine and view
  model; settings; formatting (always `$`, invariant culture); popover view-models; tray icon layout.
- **Track.App** (WPF shell, excluded from coverage and listed in `docs/coverage-exclusions.md`): tray icon
  from the three logo marks (color = working, grey = idle), acrylic popover (tokens first,
  `$ · in · out · cache`, sorted highest first, per-model hover card), right-click menu, the top island
  (live timer, waveform, done note, top-edge reveal, click to open, minimize), Windows theme following,
  start with Windows.
- Decisions came from four clickable mockup rounds (`design/track-mockup*.html`) and one review round.
- Bugs caught by verification and fixed: source-generated JSON wiping missing settings; a crash when the
  tray icon returned to an earlier state (H.NotifyIcon disposes old icons); overlapping popover layout; an
  island font crash; a re-encoding that broke "✓"; memory and CPU above target (358 MB parse burst,
  repeated process enumeration, folder scans every 5 s, a blur effect redrawn per frame); a misleading
  focus ring.
- Known limits: costs are lower bounds (no long-context surcharge, no 1-hour cache-write rate, no price for
  `codex-auto-review`); the logos are simplified stand-ins (`src/Track.App/Assets/logos/README.md`); the
  island screen option "All" uses the primary screen; Escape on the opened island and a real full-screen
  video were checked through tests and the override switch, not by hand; while the island shows, Track uses
  ~80–116 MB (idle 46 MB).

## Deployment Plan
1. Prerequisite: the .NET 10 Desktop Runtime for Windows x64 (part of the .NET 10 SDK already on this
   machine; install the runtime on any other machine).
2. From `C:\development\track`: `powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Rebuild`.
   It publishes the single-file `Track.exe`, copies it to `%LOCALAPPDATA%\Programs\Track`, sets the HKCU
   Run key "Track" and starts it. Done on 2026-11-18: Track is installed and running.
3. Check: `Get-Process Track` → 1 process from `%LOCALAPPDATA%\Programs\Track\Track.exe`; the tray shows the
   three marks; a left-click opens the popover.
4. Update after code changes: run the same install command with `-Rebuild`. It stops the running copy,
   replaces it and restarts it; settings, snapshot and price cache in `%LOCALAPPDATA%\Track` are kept.
5. Remove: `powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1` (add `-RemoveData` to delete
   `%LOCALAPPDATA%\Track` as well).
6. Optional: drop official logo SVGs into `src/Track.App/Assets/logos/` (see the README there) and reinstall.
