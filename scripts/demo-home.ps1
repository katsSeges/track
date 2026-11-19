param(
    [string]$Path = (Join-Path $env:TEMP "track-demo-home")
)

$ErrorActionPreference = "Stop"
$now = [DateTimeOffset]::UtcNow
$timestamp = $now.ToString("O")
$earlier = $now.AddMinutes(-42)
$earlierTimestamp = $earlier.ToString("O")

function Write-JsonLines([string]$File, [object[]]$Events) {
    $parent = Split-Path -Parent $File
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $Events | ForEach-Object { $_ | ConvertTo-Json -Depth 12 -Compress } |
        Set-Content -Path $File -Encoding utf8
}

$claude = [ordered]@{
    type = "assistant"
    timestamp = $timestamp
    sessionId = "demo-claude"
    requestId = "demo-claude-request"
    message = [ordered]@{
        id = "demo-claude-message"
        model = "claude-sonnet-4-5"
        usage = [ordered]@{
            input_tokens = 2180
            cache_creation_input_tokens = 430
            cache_read_input_tokens = 1240
            output_tokens = 760
            output_tokens_details = [ordered]@{ thinking_tokens = 120 }
        }
    }
}
$claudeEarlier = [ordered]@{
    type = "assistant"
    timestamp = $earlierTimestamp
    sessionId = "demo-claude"
    requestId = "demo-claude-earlier-request"
    message = [ordered]@{
        id = "demo-claude-earlier-message"
        model = "claude-sonnet-4-5"
        usage = [ordered]@{
            input_tokens = 1540
            cache_creation_input_tokens = 220
            cache_read_input_tokens = 610
            output_tokens = 430
            output_tokens_details = [ordered]@{ thinking_tokens = 70 }
        }
    }
}

$codexEvents = @(
    [ordered]@{ timestamp = $timestamp; type = "session_meta"; payload = [ordered]@{ id = "demo-codex" } },
    [ordered]@{ timestamp = $timestamp; type = "turn_context"; payload = [ordered]@{ model = "gpt-5.1" } },
    [ordered]@{
        timestamp = $earlierTimestamp
        type = "event_msg"
        payload = [ordered]@{
            type = "token_count"
            info = [ordered]@{
                total_token_usage = [ordered]@{
                    input_tokens = 2440
                    cached_input_tokens = 800
                    cache_write_input_tokens = 180
                    output_tokens = 610
                    reasoning_output_tokens = 60
                    total_tokens = 3050
                }
            }
        }
    },
    [ordered]@{
        timestamp = $timestamp
        type = "event_msg"
        payload = [ordered]@{
            type = "token_count"
            info = [ordered]@{
                total_token_usage = [ordered]@{
                    input_tokens = 3440
                    cached_input_tokens = 1180
                    cache_write_input_tokens = 260
                    output_tokens = 920
                    reasoning_output_tokens = 80
                    total_tokens = 4360
                }
            }
        }
    }
)

$copilot = [ordered]@{
    id = "demo-copilot-event"
    timestamp = $timestamp
    type = "session.usage_record"
    data = [ordered]@{
        usage = [ordered]@{
            model = "gpt-4.1"
            inputTokens = 2980
            cacheReadTokens = 520
            cacheWriteTokens = 160
            outputTokens = 680
            reasoningTokens = 40
        }
    }
}
$copilotEarlier = [ordered]@{
    id = "demo-copilot-earlier-event"
    timestamp = $earlierTimestamp
    type = "session.usage_record"
    data = [ordered]@{
        usage = [ordered]@{
            model = "gpt-4.1"
            inputTokens = 1320
            cacheReadTokens = 210
            cacheWriteTokens = 80
            outputTokens = 330
            reasoningTokens = 25
        }
    }
}

$gemini = [ordered]@{
    '$set' = [ordered]@{
        sessionId = "demo-gemini"
        messages = @(
            [ordered]@{
                id = "demo-gemini-message"
                type = "gemini"
                timestamp = $earlierTimestamp
                model = "gemini-2.5-flash"
                tokens = [ordered]@{ input = 960; cached = 180; output = 245; thoughts = 25 }
            },
            [ordered]@{
                id = "demo-gemini-message"
                type = "gemini"
                timestamp = $timestamp
                model = "gemini-2.5-flash"
                tokens = [ordered]@{ input = 1840; cached = 360; output = 490; thoughts = 55 }
            }
        )
    }
}

$taskTimestamp = $now.ToUnixTimeMilliseconds()
function New-TaskMessage([string]$Model, [long]$InputTokens, [long]$OutputTokens, [long]$CacheWrite, [long]$CacheRead, [long]$At) {
    [ordered]@{
        type = "say"
        say = "api_req_started"
        ts = $At
        text = (@{ tokensIn = $InputTokens; tokensOut = $OutputTokens; cacheWrites = $CacheWrite; cacheReads = $CacheRead } | ConvertTo-Json -Compress)
        modelInfo = [ordered]@{ providerId = "anthropic"; modelId = $Model }
    }
}

Write-JsonLines (Join-Path $Path ".claude\projects\demo\demo-claude.jsonl") @($claudeEarlier, $claude)
Write-JsonLines (Join-Path $Path ".codex\sessions\2026\11\19\rollout-demo-codex.jsonl") $codexEvents
Write-JsonLines (Join-Path $Path ".copilot\session-state\demo-copilot\events.jsonl") @($copilotEarlier, $copilot)
$geminiPath = Join-Path $Path ".gemini\tmp\session-demo.jsonl"
Write-JsonLines $geminiPath @($gemini)

$clinePath = Join-Path $Path "AppData\Roaming\Code\User\globalStorage\saoudrizwan.claude-dev\tasks\demo-cline\ui_messages.json"
$clineMessages = @(
    (New-TaskMessage "claude-sonnet-4-5" 980 255 110 210 $earlier.ToUnixTimeMilliseconds()),
    (New-TaskMessage "claude-sonnet-4-5" 1640 510 210 430 $taskTimestamp)
)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $clinePath) | Out-Null
ConvertTo-Json -InputObject $clineMessages -Depth 12 -Compress | Set-Content -Path $clinePath -Encoding utf8

$rooPath = Join-Path $Path "AppData\Roaming\Code\User\globalStorage\RooVeterinaryInc.roo-cline\tasks\demo-roo\ui_messages.json"
$rooMessages = @(
    (New-TaskMessage "gpt-4.1" 1410 370 140 290 $earlier.ToUnixTimeMilliseconds()),
    (New-TaskMessage "gpt-4.1" 2580 740 280 610 $taskTimestamp)
)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $rooPath) | Out-Null
ConvertTo-Json -InputObject $rooMessages -Depth 12 -Compress | Set-Content -Path $rooPath -Encoding utf8

$openCodeDb = Join-Path $Path ".local\share\opencode\opencode.db"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $openCodeDb) | Out-Null
$openCodeScript = @'
import sqlite3, sys
db = sqlite3.connect(sys.argv[1])
db.execute("DROP TABLE IF EXISTS session_message")
db.execute("CREATE TABLE session_message (id TEXT, session_id TEXT, time_created INTEGER, type TEXT, data TEXT)")
db.execute(
    "INSERT INTO session_message VALUES (?, ?, ?, ?, ?)",
    ("demo-opencode-earlier-message", "demo-opencode", int(__import__("time").time() * 1000) - 42 * 60 * 1000, "assistant",
     '{"model":{"providerID":"openai","id":"gpt-4.1"},"tokens":{"input":1420,"output":340,"reasoning":20,"cache":{"read":280,"write":100}},"cost":0.007}'),
)
db.execute(
    "INSERT INTO session_message VALUES (?, ?, ?, ?, ?)",
    ("demo-opencode-message", "demo-opencode", int(__import__("time").time() * 1000), "assistant",
     '{"model":{"providerID":"openai","id":"gpt-4.1"},"tokens":{"input":2320,"output":680,"reasoning":45,"cache":{"read":510,"write":190}},"cost":0.012}'),
)
db.commit()
db.close()
'@
$openCodeScript | python - $openCodeDb
if ($LASTEXITCODE -ne 0) { throw "Python failed to create the OpenCode demo database." }

Write-Output $Path
