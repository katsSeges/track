# Coverage exclusions

The coverage gate (`tests/Track.Core.Tests/Track.Core.Tests.csproj`, coverlet `Include=[Track.Core]*`,
threshold 100 % line, branch and method) measures **Track.Core**, which holds all logic: log parsing,
ingest and snapshots, pricing, periods, aggregation, visibility, live status, settings, formatting,
view-models and the tray icon layout.

**Track.App** (the WPF shell) is outside the gate. It only wires Track.Core to Windows and draws what
Track.Core decides. Every class in it carries `[ExcludeFromCodeCoverage(Justification = …)]` so the
reason is visible in the code too.

| File | What it does | Why excluded | How it is checked instead |
|---|---|---|---|
| `src/Track.App/App.xaml.cs` | Single-instance mutex, start/stop, error log | Application entry point | Launch twice → one process (Phase 7 check) |
| `src/Track.App/Shell/TrayController.cs` | Builds services, timers, tray icon, menu, popover, card, theme, startup entry; `--render-*` verification switches | WPF composition root; all decisions it relies on are tested in Track.Core | Real-data and fake-home runs with `TRACK_DIAG=1` (`diag.txt`) |
| `src/Track.App/Shell/PopoverWindow.xaml(.cs)` | Popover view, hover delay, converters, usage bar | WPF view bound to the tested `PopoverViewModel` | `--render-popover` images in light and dark |
| `src/Track.App/Shell/ModelCardWindow.xaml(.cs)` | Model card view and share bar | WPF view bound to the tested `ModelBreakdownViewModel` | `--render-popover` card images |
| `src/Track.App/Shell/TrayIconRenderer.cs` | Draws a `TrayIconSpec` to an icon | WPF/GDI drawing; the layout is tested in `TrayIconModel` | `--render-icons` sheet at 16/24/32 px |
| `src/Track.App/Shell/Logos.cs` | Logo geometries and the `LogoIcon` control | WPF drawing | Icon and popover renders |
| `src/Track.App/Shell/UsageChart.cs` | Draws the usage chart (bars, top line, axis), hover tooltip, click / keyboard selection | WPF drawing; the chart model, its geometry and every text are tested in `ChartViewModel` / `UsagePanelBuilder` | `--render-popover` images; real-mouse hover and click checks |
| `src/Track.App/Shell/AnalyticsWindow.cs`, `AnalyticsChart.cs` | Resizable analytics view, stacked provider charts, model/session tables, heatmap and CSV dialog | WPF view and drawing; range semantics, aggregates, overlap rules and CSV content are tested in `AnalyticsBuilder` / `AnalyticsCsv` | `--render-analytics` images in light and dark |
| `src/Track.App/Shell/IslandWindow.cs` | Draws the top island (Now and Usage tabs, status-only providers, sub-agent rows and dots), slide animation, focus only while opened | WPF view; state and content come from the tested `IslandStateMachine` / `IslandViewModel` | `--render-island` images (incl. Usage tab per period and sub-agents); `diag.txt` `subagent:` lines from a live fake home; `TRACK_POINTER_FILE` runs driving reveal, click, full-screen and the `island:` tab/period/bar commands |
| `src/Track.App/Shell/SettingsWindow.cs`, `ColorPickerEditor.cs` | Themed appearance, provider mode/color and behavior controls; live theme/material feedback; HSV color wheel and swatch popup | WPF view/drawing; HSV conversion, wheel mapping, validation, color contrast and persisted settings are tested in Track.Core | `--render-settings` images in light and dark, including open dropdown and picker |
| `src/Track.App/Shell/ChatLauncher.cs` | Probes fixed provider targets and opens the validated executable; passes each model/command argument separately | Windows process launching; target selection and argument boundaries are tested in `ChatTargetResolver` | `--render-popover`/`--render-island` and a host click check |
| `src/Track.App/Shell/HotkeyHost.cs` | Registers the validated global shortcut on a message-only HWND and toggles the popover | Windows `RegisterHotKey`/`HwndSource`; gesture grammar is tested in `HotkeyGestureParser` | Settings status on successful or conflicting registration |
| `src/Track.App/Shell/Native.cs` | DWM Acrylic/Mica/Solid backdrop, tool-window style, no-activate toggle, cursor, DPI, monitor work area, full-screen query, theme registry, HKCU Run key | Windows API wrappers, a few lines each | Running the app; Run key read back with `Get-ItemProperty`; native material support must be observed on the supported Windows host |
| `src/Track.App/Live/HttpOllamaStatusProbe.cs` | Queries the opt-in Ollama loopback status endpoint | HTTP adapter only; loopback gating and status decisions are covered in Track.Core | Core probe-gating and status tests |

Process detection is not in this list: the real `SystemProcessList` lives in Track.Core and is tested
against the test process itself.
