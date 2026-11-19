<h1 align="center">Track</h1>

<p align="center">
  <b>See what Claude Code, Codex and GitHub Copilot are doing, and what they cost, right from your Windows tray.</b>
</p>

<p align="center">
  <a href="https://github.com/katsSeges/track/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/katsSeges/track?label=download&color=E08560"></a>
  <a href="https://github.com/katsSeges/track/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/katsSeges/track/actions/workflows/ci.yml/badge.svg"></a>
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-4C9BFF">
  <img alt="Coverage 100%" src="https://img.shields.io/badge/core%20coverage-100%25-3CCF62">
</p>

<p align="center">
  <img src="docs/images/hero.png" alt="Track's tray popover in light and dark with usage charts, and the top island listing agents and their sub-agents" width="100%">
</p>

<p align="center">
  <a href="https://github.com/katsSeges/track/releases/latest"><b>Download for Windows</b></a> · no .NET install needed
</p>

## What it does

- **Who is working.** The Claude, Codex and Copilot marks in your tray light up while an agent works.
- **What it costs.** Tokens and an estimated price for today, this week and this month, with a chart per hour or day.
- **Long tasks at a glance.** A small black island at the top of the screen shows each running agent, its timer, and the sub-agents it started.

<table>
  <tr>
    <td align="center" width="50%"><img src="docs/images/island.png" alt="The island: an agent starts, a second joins, it opens to show sub-agents and usage, then work finishes" width="100%"><br><sub>The island while agents work</sub></td>
    <td align="center" width="50%"><img src="docs/images/subagents.png" alt="Opened island: Claude Code with three running sub-agents and one finished, Copilot with a QA sub-agent" width="100%"><br><sub>Sub-agents under their agent</sub></td>
  </tr>
</table>

## Install

1. Download `Track-<version>-win-x64.zip` from [Releases](https://github.com/katsSeges/track/releases/latest) and unzip it.
2. In that folder, run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install.ps1
   ```

Track starts now and with Windows. To remove it, run `scripts\uninstall.ps1` the same way.

## Private by design

Track only reads the agents' own log files on your computer. Nothing is sent anywhere; the one network request
is a daily download of the public LiteLLM price list.

| Agent | Logs |
|---|---|
| Claude Code | `%USERPROFILE%\.claude\projects` |
| Codex | `%USERPROFILE%\.codex\sessions` |
| GitHub Copilot | `%USERPROFILE%\.copilot\session-state` |

<details>
<summary><b>Tips</b></summary>

- Left-click the tray icon for the popover. Hover a tool to see its models.
- Click a bar in a chart to see just that hour or day; click it again to go back. The **−** next to the chart hides it.
- Click the island to open it: **Now** lists the agents and sub-agents, **Usage** has the charts.
- The **–** button on the island tucks it away while agents keep working. Rest the mouse on the top edge to peek.
- When an agent finishes, the island says "done"; when Copilot asks you something, it says "needs you".

</details>

<details>
<summary><b>About the prices</b></summary>

Prices are API-equivalent estimates from LiteLLM's public list prices, not your bill: Claude, ChatGPT and
Copilot subscriptions are not charged per token. Models without a public price count their tokens but show `$—`.

</details>

## Development

Build, test, release and verification switches are in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

<sub>Screenshots use made-up demo data.</sub>
