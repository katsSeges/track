# Setup must update the saved preference too: Track reapplies it at every normal launch.
param(
    [Parameter(Mandatory)] [string] $SettingsPath,
    [Parameter(Mandatory)] [ValidateSet('true', 'false')] [string] $Enabled,
    [Parameter(Mandatory)] [string] $ErrorFile,
    [switch] $ValidateOnly
)
$ErrorActionPreference = 'Stop'
$temp = $null
try {
    $settings = if (Test-Path -LiteralPath $SettingsPath) {
        $raw = Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8
        if (-not $raw -or -not $raw.TrimStart().StartsWith('{')) {
            throw "Expected a JSON object in '$SettingsPath'. Restore or rename that file and retry Setup."
        }
        $raw | ConvertFrom-Json
    } else {
        [pscustomobject]@{}
    }
    if ($null -eq $settings -or $settings -isnot [pscustomobject]) {
        throw "Expected a JSON object in '$SettingsPath'. Restore or rename that file and retry Setup."
    }
    $settings | Add-Member -NotePropertyName startWithWindows -NotePropertyValue ($Enabled -eq 'true') -Force
    $json = $settings | ConvertTo-Json -Depth 100
    if (-not $ValidateOnly) {
        $directory = Split-Path -Parent $SettingsPath
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $temp = Join-Path $directory ([IO.Path]::GetRandomFileName())
        [IO.File]::WriteAllText($temp, $json, [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temp -Destination $SettingsPath -Force
    }
} catch {
    [IO.File]::WriteAllText($ErrorFile, $_.Exception.Message)
    Write-Error $_
    exit 1
} finally {
    if ($temp -and (Test-Path -LiteralPath $temp)) {
        Remove-Item -LiteralPath $temp -Force
    }
}
