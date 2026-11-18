# Cross-checks Track's chart numbers against the raw logs and ccusage.
#   - Track      : UsageSeries per local day (this month) and per local hour (today), via scripts/series-dump.cs
#   - Independent: scripts/independent_usage.py, its own parsing of the raw logs (not Track's code)
#   - ccusage    : `npx ccusage@latest daily --by-agent`: Claude and Codex must match exactly. ccusage dates a Copilot
#                  session's whole usage on the session's last active day (seen 2026-11-18: session 4f8f... with calls on
#                  5-13 Nov, 372.5M tokens, all shown on 17 Nov), while Track counts each call on its own day. So its
#                  Copilot number is shown for information and not counted as a difference.
# Only complete hours and days before the current hour are compared, so a log written while this runs
# cannot cause a false difference. Exit code 1 when anything differs.
param([int] $Days = 14)
$ErrorActionPreference = 'Continue' # dotnet/npx write warnings to stderr; exit codes are checked instead
$root = Split-Path -Parent $PSScriptRoot
$home2 = $env:USERPROFILE

$nowUtc = (Get-Date).ToUniversalTime()
$cutoff = [DateTime]::SpecifyKind((Get-Date -Year $nowUtc.Year -Month $nowUtc.Month -Day $nowUtc.Day -Hour $nowUtc.Hour -Minute 0 -Second 0 -Millisecond 0), 'Utc')
$cutoffText = $cutoff.ToString('yyyy-MM-ddTHH:00:00Z')
$today = (Get-Date).Date
$firstDay = @($today.AddDays(-$Days), (Get-Date -Day 1).Date) | Sort-Object | Select-Object -Last 1

Write-Host "Cutoff $cutoffText (complete hours only); days $($firstDay.ToString('yyyy-MM-dd')) .. $($today.AddDays(-1).ToString('yyyy-MM-dd')) plus today's hours"
$independent = python -I (Join-Path $root 'scripts\independent_usage.py') $home2 $cutoffText $firstDay.ToString('yyyy-MM-dd') | ConvertFrom-Json
Push-Location $root
try { $trackJson = cmd /c "dotnet run scripts/series-dump.cs -c Release -- ""$home2"" $cutoffText 2>nul" | Select-Object -Last 1 } finally { Pop-Location }
if ($LASTEXITCODE -ne 0 -or -not $trackJson) { throw 'series-dump failed' }
$track = $trackJson | ConvertFrom-Json
$cc = (cmd /c "npx -y ccusage@latest daily --since $($firstDay.ToString('yyyyMMdd')) --json --by-agent 2>nul" | Out-String) | ConvertFrom-Json

function Value($obj, $key, $tool) { $d = $obj.$key; if ($null -eq $d) { return [int64]0 }; $v = $d.$tool; if ($null -eq $v) { [int64]0 } else { [int64]$v } }
$differences = 0
$tools = 'Claude', 'Codex', 'Copilot'

"{0,-11} {1,-8} {2,15} {3,15} {4,15}  {5}" -f 'day', 'tool', 'Track', 'independent', 'ccusage', 'result'
for ($d = $firstDay; $d -lt $today; $d = $d.AddDays(1)) {
    $key = $d.ToString('yyyy-MM-dd')
    $ccDay = $cc.daily | Where-Object { $_.period -eq $key -or $_.date -eq $key }
    foreach ($t in $tools) {
        $tr = Value $track.days $key $t; $ind = Value $independent.days $key $t
        $agent = $ccDay.agents | Where-Object { $_.agent -eq $t.ToLowerInvariant() }
        $ccv = if ($agent) { [int64]($agent.inputTokens + $agent.outputTokens + $agent.cacheCreationTokens + $agent.cacheReadTokens) } else { [int64]0 }
        $ok = ($tr -eq $ind) -and ($t -eq 'Copilot' -or $tr -eq $ccv)
        if (-not $ok) { $differences++ }
        if ($tr -or $ind -or $ccv) { "{0,-11} {1,-8} {2,15:N0} {3,15:N0} {4,15}  {5}" -f $key, $t, $tr, $ind, ('{0:N0}' -f $ccv), $(if (-not $ok) { 'DIFFERENT' } elseif ($t -eq 'Copilot' -and $tr -ne $ccv) { 'ok (ccusage dates Copilot by session)' } else { 'ok' }) }
    }
}

""
"{0,-11} {1,-8} {2,15} {3,15}  {4}" -f 'hour', 'tool', 'Track', 'independent', 'result'
for ($h = 0; $h -lt 24; $h++) {
    foreach ($t in $tools) {
        $tr = Value $track.hours "$h" $t; $ind = Value $independent.hours "$h" $t
        if ($tr -ne $ind) { $differences++ }
        if ($tr -or $ind) { "{0,-11} {1,-8} {2,15:N0} {3,15:N0}  {4}" -f ('{0:00}:00' -f $h), $t, $tr, $ind, $(if ($tr -eq $ind) { 'ok' } else { 'DIFFERENT' }) }
    }
}

""
$tc = $track.comparison; $ic = $independent.comparison
$same = ($tc.current -eq $ic.current) -and ($tc.previous -eq $ic.previous) -and ("$($tc.percent)" -eq "$($ic.percent)")
if (-not $same) { $differences++ }
"Today vs yesterday until {0}: Track {1:N0} vs {2:N0} ({3} %) '{4}' | independent {5:N0} vs {6:N0} ({7} %)  {8}" -f $cutoff.ToLocalTime().ToString('HH:mm'), [int64]$tc.current, [int64]$tc.previous, $tc.percent, $tc.text, [int64]$ic.current, [int64]$ic.previous, $ic.percent, $(if ($same) { 'ok' } else { 'DIFFERENT' })

""
if ($differences -eq 0) { "All equal." } else { "$differences difference(s)."; exit 1 }
