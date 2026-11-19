param(
    [string]$Path = (Join-Path $env:TEMP "track-demo-home")
)

$ErrorActionPreference = "Stop"
$now = [DateTimeOffset]::UtcNow
$timestamp = $now.ToString("O")

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

$codexEvents = @(
    [ordered]@{ timestamp = $timestamp; type = "session_meta"; payload = [ordered]@{ id = "demo-codex" } },
    [ordered]@{ timestamp = $timestamp; type = "turn_context"; payload = [ordered]@{ model = "gpt-5.1" } },
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

Write-JsonLines (Join-Path $Path ".claude\projects\demo\demo-claude.jsonl") @($claude)
Write-JsonLines (Join-Path $Path ".codex\sessions\2026\11\19\rollout-demo-codex.jsonl") $codexEvents
Write-JsonLines (Join-Path $Path ".copilot\session-state\demo-copilot\events.jsonl") @($copilot)

Write-Output $Path
