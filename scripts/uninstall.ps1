# Removes Track for the current user: stops it, removes the "start with Windows" entry and the program folder.
# Settings, the usage snapshot and the price cache in %LOCALAPPDATA%\Track are kept unless -RemoveData is given.
param(
    [switch] $RemoveData
)
$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Programs\Track'
$data = Join-Path $env:LOCALAPPDATA 'Track'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process Track -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Remove-ItemProperty -Path $runKey -Name 'Track' -ErrorAction SilentlyContinue
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
if ($RemoveData -and (Test-Path $data)) { Remove-Item $data -Recurse -Force }

Write-Host ("Track removed." + $(if ($RemoveData) { ' Data removed too.' } else { " Data kept in $data." }))
