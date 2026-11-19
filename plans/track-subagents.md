# Track — sub-agents in the island and the tray

Show the sub-agents each main agent (Claude Code, Codex, GitHub Copilot) is running as rows under that agent in the
opened island, with their name, task, model, tokens and live run time, dimmed when done and gone when their agent
finishes; hint at them in the compact island; and count them in the tray popover's running rows. **Correct data is
the top requirement**: every sub-agent's start, end, run time and tokens are tested against hand-computed values and
checked against what the tools themselves report (Copilot's own `durationMs` / `totalTokens`, Claude's `finished`).

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Rules that carry over from the earlier plans (`plans/track-tray-app.md`, `plans/track-usage-charts.md`):
- All logic lives in **Track.Core** with a **100 % line/branch/method** coverage gate (`scripts/test.ps1`); Track.App only draws.
- No automated mouse clicks while the user is at the PC without their go-ahead; the `TRACK_POINTER_FILE` commands and `diag.txt` (`TRACK_DIAG=1`) cover most checks without the mouse.
- Stop the installed Track before running the bin build or a render (single-instance mutex); restart it after.
- Do not change system settings. Commit / push / release only when the user asks.
- Track is its own project: use its own design language (round 1–6 mockups in `design/`), nothing from other products.

## Agreed Scope (source of truth for decisions)

Round 6 choices (pasted by the user 2026-11-19, from `design/track-mockup-v6.html`,
artifact https://claude.ai/artifact/SUkfU7gt2aApijTxRDZXWQ):
`{"layout":"tree","info":["name","task","model","tokens"],"activity":"dot","done":"keep","max":"all","nested":"indent","compact":"dots","tray":"split"}`

| Decision | Choice |
|---|---|
| Layout in the opened island | Rows under each main agent, indented, with a thin vertical **guide line** on the left |
| Row content | **Name / type**, **task**, **model**, **tokens**, plus the **run time** (always). No tool-call count |
| Working sign | **Pulsing dot** in the tool's color (respects reduced motion); finished rows show **✓** instead |
| When a sub-agent finishes | **Stays, dimmed**, showing `done 4:05` (its final run time), until its main agent is finished; then it leaves with the agent |
| Rows per agent | **All** (no "+N more") |
| Sub-agents of sub-agents | Shown, **indented one more step** under their parent sub-agent |
| Compact island | **Dots** after the timer: one pulsing dot per running sub-agent of the shown agent, at most 4 |
| Tray popover running row | **`1 session · 2 sub-agents`** (only running sub-agents; `1 session` when there are none). Sub-agents count for the tool being "running" |
| Background sub-agents (user, 2026-11-19) | A main agent whose turn ended **stays in the island and counts as running in the tray while any of its sub-agents still runs**; its own task timer stops at its turn end |
| Codex sub-agents (user, 2026-11-19) | Moved **under their parent Codex agent** (today they show as extra top-level agents) |
| Session tokens (user, 2026-11-19) | The main agent's "this session" tokens **include its sub-agents**; each sub-agent row shows its own part |

What each tool logs (researched on the user's real logs, 2026-11-19):

| Tool | Where | Name | Task | Model | Start / end | Tokens |
|---|---|---|---|---|---|---|
| Claude Code | `~/.claude/projects/<p>/<session>/subagents/agent-<id>.jsonl` (+ `agent-<id>.meta.json`: `agentType`, `description`, `pickedEffortModel`, `spawnDepth`, `finished`, `toolUseId`); lines carry the parent `sessionId`, `agentId`, `isSidechain: true` | `agentType` (`Explore`, `general-purpose`) | `description` | from the log's `message.model` (meta as fallback) | first / last line timestamp; done when `finished: true` (or the parent's tool result for that `toolUseId`) | the sub-agent log's usage lines |
| Copilot | same `events.jsonl` as the parent; sub-agent events carry a top-level `agentId` | `subagent.started.data.agentDisplayName` (`QA`) | `agentDescription` (the role) | `data.model` | `subagent.started` / `subagent.completed` (has `durationMs`, `totalTokens`, `totalToolCalls`) | `session.usage_record` lines with that `agentId` |
| Codex | separate `rollout-*.jsonl`; `session_meta.payload.source.subagent.thread_spawn.parent_thread_id`, `thread_source: "subagent"`, `agent_nickname` | `agent_nickname` (`Zeno`) | none (the row shows name only) | `turn_context.model` | first line / `task_complete` (or silence, by the existing tracker rules) | its own `token_count` events |

Out of scope: tool-call counts, sub-agents in the Usage tab or charts, a separate sub-agent history, changes to the usage totals (sub-agent tokens are already counted under their tool).

## Phase 1: Sub-agent model and readers (Core)
Status: Complete

- [x] `SubAgentActivity` record in `Track.Core.Activity`: `Tool`, `ParentSessionId`, `Id`, `ParentId` (for nesting), `Depth`, `Name`, `Task?`, `Model?`, `StartedUtc`, `EndedUtc?`, `Tokens`
- [x] Claude Code: the activity reader recognises `subagents/agent-<id>.jsonl`, reads its `.meta.json` (type, description, model, depth, finished) once and on change; start = first timestamp, end = `finished` (end time = last line) or the parent's tool result for its `toolUseId`; tokens from the file's usage lines (same dedupe as `ClaudeLogParser`)
- [x] Copilot: `CopilotActivityParser` emits sub-agent start/end from `subagent.started` / `subagent.completed` (name, role, model, `durationMs`, `totalTokens`); tokens add up from `session.usage_record` with that `agentId`; nesting from the sub-agent's own `subagent.started` events (its `agentId` as parent)
- [x] Codex: `CodexActivityParser` reads `session_meta.source.subagent.thread_spawn.parent_thread_id` and `agent_nickname`; such a session becomes a sub-agent of its parent thread instead of a top-level agent
- [x] `AgentActivityTracker` keeps sub-agents per parent session and exposes them; a crashed sub-agent (no end, log silent) ends by the same stale/dead rules as sessions
- [x] Scrubbed fixtures from the real log shapes (no prompts, no paths): a Claude sub-agent log + meta, a Copilot session with a nested sub-agent, a Codex parent + sub-agent pair
- [x] Tests with hand-computed start, end, run time, tokens, nesting and names for each tool, incl. a sub-agent still running, one finished, one crashed, and old logs without the new fields

### Verification Plan
- `powershell -ExecutionPolicy Bypass -File scripts\test.ps1` → all pass, 100 % line/branch/method
- Replay script over the user's real logs (scratchpad, like `replay.cs`): for every Copilot sub-agent, Track's run time equals `subagent.completed.durationMs` (±1 s) and Track's tokens equal `totalTokens`; for every Claude sub-agent with `finished: true`, Track marks it done; Codex sub-agent sessions are attached to the right parent thread; result table recorded in the summary

### Phase Summary
**Complete 2026-11-19.**
- **Event model:** `ActivityEvent` gained `Sub` (`SubAgent`: id, name, task, model, tokens, parent id, own-session flag, launch id) and kinds `SubAgentStart` (start or resume), `SubAgentStepEnd`, `SubAgentEnd`, `SubAgentTokens`, `SubAgentInfo`, `SubAgentLaunch`. Sub-agent events are sent to the **parent's** session key.
- **Claude Code** (`ClaudeActivityParser`): any line with a top-level `agentId` (or `isSidechain: true`) is the sub-agent's — its prompt starts it, usage adds tokens once per `message.id`, `Agent`/`Task` tool calls record launches (nesting), `end_turn`/interrupt end it. These lines no longer start or end the parent's turn (**bug fixed**: a sub-agent's prompt used to restart the parent's timer). `ActivityReader` reads `agent-<id>.meta.json` (type, description, model, `toolUseId`, `finished`) whenever it changes; `finished: true` ends the sub-agent at the log's last write (its log has no end marker of its own — the last message ends with a tool call).
- **Copilot** (`CopilotActivityParser`): lines with a top-level `agentId` are sub-agent lines — `subagent.started` (display name, role, model, parent via the `task` call a sub-agent made), `subagent.completed`, `assistant.turn_start`/`turn_end` (steps), `session.usage_record` (input+output), and the main agent's messages to it (main task continues, sub-agent resumes). A sub-agent's `ask_user` still means "waiting for you".
- **Codex** (`CodexActivityParser`): a log whose `session_meta.source.subagent.thread_spawn.parent_thread_id` is set reports to that parent thread as a sub-agent (nickname, `agent_path` as task, `OwnSession`), with token deltas from `token_count`. A forked child replays a copy of its parent's history after a second `session_meta`, up to `thread_settings_applied`; that copy is skipped (found on the real logs: it made the child look like its parent).
- **Tracker** (`AgentActivityTracker`): per-session sub-agents (`Sub` state: start, end, pending step end, tokens, facts); `SubAgentActivity` list on `AgentActivity` (tree order, depth); silent sub-agents end at their last line by the same stale/dead rules; sub-agent events keep a session known (bug found by the tests: a session only seen through sub-agents was forgotten at once). A sub-agent's writes do not count as its parent's (else the parent would look "still written" for 2 minutes after every sub-agent).
- Tests: `Activity/SubAgentTests.cs` (parsers for all three tools incl. forked history and agentId-only lines, lifecycle, resume, grace, stale, nesting incl. Codex grandchildren, meta reading incl. locked/changed/finished files, tray count), updated Copilot parser test. 471 tests, 100 % line/branch/method.
- **Real-log check** (`replay/subcheck.cs` in the session scratchpad), **96 checks, 0 mismatches**: all 12 Copilot sub-agents' run time equals Copilot's `durationMs` and tokens equal `totalTokens` exactly (e.g. QA 7:45 / 1,575,467; Implementer 26:10 / 7,734,892); all 10 Claude sub-agents named from meta (e.g. "general-purpose · Port server to browser engine", 10:08, 6.2M) and ended at their log's last write; all 16 Codex child threads attached to an existing parent, sub-agent events only.

## Phase 2: Working rules, tokens and texts (Core)
Status: Complete

- [x] A main agent counts as working while any of its sub-agents runs (background sub-agents), its task timer frozen at its own turn end; it leaves (with its done sub-agents) when its last sub-agent ends
- [x] Codex sub-agent sessions no longer appear as top-level agents (island, tray, popover)
- [x] Main agent "this session" tokens = its own + its sub-agents' (Codex: roll up the child sessions); usage totals stay unchanged (no double counting — test)
- [x] `IslandAgentRow` gains `SubAgents`: `IslandSubAgentRow(Tool, Name, Task?, Meta, Timer, Running, Depth)`; `Meta` = `model · 1.2M tokens`; `Timer` = live run time, or `done 4:05`; order = start time, children right under their parent; done rows kept (dimmed) until the agent leaves
- [x] Compact content: `SubAgentDots` = running sub-agents of the shown agent, capped at 4
- [x] Tray / popover: `LiveStatus` gains the running sub-agent count; detail text `1 session · 2 sub-agents` / `2 sessions · 1 sub-agent` / `1 session`; a tool with only background sub-agents running shows as running
- [x] Hand-computed text tests for every row shape (each tool, missing task/model, nested, done, crashed), the compact dots, and the tray wording incl. singular/plural

### Verification Plan
- `scripts\test.ps1` → all pass, 100 %
- Snapshot-style tests that break when one token or one second changes (as in the usage-charts plan)

### Phase Summary
**Complete 2026-11-19.**
- `AgentActivity` gained `TurnEndedUtc` (its own turn ended while sub-agents still run — the island timer stops there) and `SubAgents`. Working rule: marked working → shown; else any running sub-agent → shown with the stopped timer (start = its turn start, or the first sub-agent's start when no turn was seen); else the tray's "still written" rule; else gone, and its finished sub-agents are dropped. A new task (`TurnStart`) drops the last task's finished sub-agents; running ones stay.
- Codex child threads are never top-level agents (also when they hold their own sub-agents: their thread is excluded and attached under the child).
- Island (`IslandViewModel`): `IslandAgentRow.SubAgents` → `IslandSubAgentRow(Tool, Name, Task, Meta, Timer, Running, Depth)`: name or "sub-agent", `model · 640k tokens`, `1:12` or `done 4:05`; task timer `(TurnEnded ?? now) − TurnStart` in the opened and compact island and in "done"; `IslandContent.SubAgentDots` = running sub-agents of the shown agent, max `IslandViewModel.MaxDots` (4); session tokens add Codex children's own sessions.
- Tray: `LiveStatus.SubAgents` (from `LiveStatusDetector.WithAgents`); popover detail `1 session · 2 sub-agents · model` / `2 sessions · 1 sub-agent` / `1 session`.
- Tests: `Activity/IslandSubAgentTests.cs` (rows with every field combination and exact texts, no sub-agents, stopped timer + dots capped at 4 + "done" duration, Codex session tokens), `PopoverViewModelTests.Running_sub_agents_count_in_the_tray_row`, `SubAgentTests` background / never-seen-turn / new-task cases. 471 tests, 100 %.

## Phase 3: Island and tray UI (WPF)
Status: Complete

- [x] `IslandWindow`: sub-agent rows under each agent with the guide line, pulsing dot (static when reduced motion is on), ✓ and dimming when done, one more indent per depth; text trims with an ellipsis, the timer column never moves
- [x] Compact pill: up to 4 pulsing dots after the timer; `ContentKey` includes the sub-agent rows so timers update in place without rebuilding every second
- [x] Popover running rows show the new detail text
- [x] `diag.txt`: `subagent:` lines (tool, parent, name, task, model, tokens, timer, running/done, depth); `--render-island` adds an opened island with sub-agents (demo agents, like the existing renders)
- [x] `docs/coverage-exclusions.md` updated if new App files are added

### Verification Plan
- `dotnet build Track.slnx -c Release` → 0 warnings; `scripts\test.ps1` 100 %
- `--render-island` images compared with the round 6 mockup (tree layout, dots, dimmed done rows)
- Real app with `TRACK_DIAG=1` while the user runs real sub-agents (or a recorded replay): `subagent:` lines appear within one live refresh of the sub-agent starting, timers tick, a finished one turns `done` and stays dimmed, all leave with the agent; the popover says `1 session · N sub-agents`. Real-mouse/visual checks only with the user's go-ahead
- Idle CPU/RAM unchanged within targets (≤ 0.5 % of one core, ≤ 60 MB, 5-minute measurement)

### Phase Summary
**Complete 2026-11-19.**
- `IslandWindow`: `SubAgentRows` under each agent — guide line (left border), pulsing dot in the tool color (still with reduced motion) or ✓ when done, **name** + task (dimmed) with ellipsis, `model · tokens` below, run time on the right (`done 4:05` in green, row at 50 % opacity), 14 px more indent per depth; sub-agent timers update in place (`_subTimers`), the content key includes the rows (not their timers) and the dot count. Compact pill: up to 4 pulsing dots after the timer.
- `TrayController`: `subagent:` lines in `diag.txt`; `--render-island` adds `island-3-subagents.png` (opened, nested and done rows) and `island-3-subagents-compact.png` (dots). No new App files, `docs/coverage-exclusions.md` updated.
- Renders match the round 6 mockup (tree layout, guide line, dots, dimmed done row).
- **End-to-end run of the real app** (fake home with live logs for all three tools, island kept hidden with the pointer file's `fullscreen` line, so nothing on screen; `e2e_subs.ps1` in the session scratchpad): all three sub-agents appeared within one refresh with name, task, model and tokens (Claude 10,500 = 1,000 + 9,000 + 500; Copilot 42,000; Codex 33,000), timers ticked (+5 s after 6 s), Claude's (meta `finished`) and Copilot's (`subagent.completed`) turned `done 1:00` / `done 1:05` while Codex's kept running; popover rows `1 session · 1 sub-agent` → `1 session`; no errors.log.
- Idle (5 min, popover closed, island hidden, real data): **0.24 % CPU, 42.9 MB working set, 45.7 MB private** ✔.
- Real-mouse / visual checks skipped at the user's request (goal 2026-11-19: "probably skip the computer tests for now").

## Phase 4: Docs, packaging, release
Status: Complete (release prepared, not published)

- [x] README: sub-agents in the island and the tray; refresh the hero / island images from demo data if they change (demo logs with sub-agents)
- [x] Reinstall locally with `scripts\install.ps1 -Rebuild`
- [x] Release prepared: `<Version>` 1.1.0, `docs/release-notes/v1.1.0.md`, local package built
- [ ] Commit, push, CI green, tag `v1.1.0`, verify the release assets (left to the user: "don't commit, but prepare everything")

### Verification Plan
- `scripts\test.ps1` 100 %; `scripts\check-series.ps1` "All equal." (usage totals unchanged)
- Installed Track runs from `%LOCALAPPDATA%\Programs\Track`, no `errors.log`
- If released: CI and Release workflows green; the zip's digest on GitHub equals the workflow's sha256

### Phase Summary
**Release prepared 2026-11-19, not committed or published** (user: "don't commit, but prepare everything for it").
- **README rewritten** to be short: title, tagline, badges, hero image, a download link, three one-line features, two images side by side (animated island, sub-agents), a 2-step install, "Private by design" with the log folders, and Tips / About the prices folded into `<details>`. Developer material moved to the new **`docs/DEVELOPMENT.md`** (build and test, data check, project layout, install from source, releases, verification switches).
- **New images** in `docs/images/`, rendered by Track itself from made-up demo logs (no real usage): `hero.png` (light popover Week · island with sub-agents · dark popover Month), `island.png` (animated PNG, 9 frames: working → two agents → dots → sub-agents → Usage today/month → done → idle), `subagents.png`. The demo generator gives today's demo sessions the island demo agents' ids so "this session" shows numbers.
- Version **1.1.0** in `src/Track.App/Track.App.csproj`; notes in `docs/release-notes/v1.1.0.md`.
- Checks: `dotnet build` 0 warnings; `scripts\test.ps1` 471 tests, 100 % line/branch/method; `scripts\check-series.ps1` "All equal." (usage totals unchanged); `scripts\package.ps1` → `artifacts\Track-1.1.0-win-x64.zip` (56.6 MB, sha256 `be6077eb…72de`); reinstalled: `%LOCALAPPDATA%\Programs\Track\Track.exe` runs, version 1.1.0, no errors.log.

## Final Recap
Track 1.1.0 shows the sub-agents of Claude Code, Codex and Copilot.
- **Data (Phases 1–2):** each tool's parser turns its sub-agent lines into sub-agent events on the parent session (Claude sidechain logs + meta files, Copilot `agentId` events incl. resumes, Codex spawned threads incl. their copied parent history); the tracker keeps them per agent with start, end, tokens and nesting, keeps an agent while its background sub-agents run (its timer stopped at its own turn end), drops finished ones when the agent leaves, and never shows a Codex child thread as an agent. Two older bugs fixed on the way: a Claude sub-agent restarting / ending its parent's task, and Codex sub-agents showing as separate agents.
- **UI (Phase 3):** sub-agent rows under each agent in the opened island (guide line, pulsing dot, name, task, model · tokens, run time, dimmed ✓ when done, nesting), dots in the compact pill, "1 session · 2 sub-agents" in the tray.
- **Correctness:** 471 tests at 100 % line/branch/method with hand-computed values; on the user's real logs all 12 Copilot sub-agents match Copilot's own `durationMs` and `totalTokens` exactly, all 10 Claude sub-agents are named and end at their log's last write, all 16 Codex child threads attach to their parent (96 checks, 0 mismatches); an end-to-end run of the real app on live logs; idle 0.24 % CPU / 42.9 MB.
- **Release prep (Phase 4):** a much shorter README with new demo images, `docs/DEVELOPMENT.md`, version 1.1.0, release notes, a locally built and installed package. Not committed (user's instruction).

## Deployment Plan
Everything is in the working tree; nothing is committed.
1. Review and commit (e.g. `git add -A` then a commit "1.1.0: sub-agents in the island and the tray"). `artifacts/`, `coverage/`, `publish/` are ignored.
2. `git push origin main` → CI (`.github/workflows/ci.yml`) must be green (build + 471 tests + 100 % gate).
3. `git tag -a v1.1.0 -m "Track 1.1.0"` and `git push origin v1.1.0` → the Release workflow builds, tests, packages and publishes `Track-1.1.0-win-x64.zip` + `.sha256` with `docs/release-notes/v1.1.0.md` as the notes.
4. Check: `gh release view v1.1.0`; the zip's digest on GitHub equals the workflow log's sha256; the README on GitHub shows the new images.
5. Users install by unzipping and running `scripts\install.ps1` (replaces the old version, keeps settings and data). Roll back: install the 1.0.1 zip the same way.
