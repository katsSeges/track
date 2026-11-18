# Installs Track for the current user and starts it.
#   - publishes the app (framework-dependent single file) if publish\ is missing or -Rebuild is given
#   - copies it to %LOCALAPPDATA%\Programs\Track
#   - starts Track with Windows (HKCU Run key "Track"); turn this off later from the tray menu
#   - starts Track now
# From the release zip: publish\ is already there (self-contained, no .NET needed).
# From source: publishing needs the .NET 10 SDK, and the framework-dependent build needs the .NET 10 Desktop Runtime.
param(
    [switch] $Rebuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'publish'
$target = Join-Path $env:LOCALAPPDATA 'Programs\Track'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

if ($Rebuild -or -not (Test-Path (Join-Path $publish 'Track.exe'))) {
    Write-Host 'Publishing Track ...'
    dotnet publish (Join-Path $root 'src\Track.App\Track.App.csproj') -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

# Replace a running copy.
Get-Process Track -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $publish '*') $target -Recurse -Force

$exe = Join-Path $target 'Track.exe'
Set-ItemProperty -Path $runKey -Name 'Track' -Value "`"$exe`""
Start-Process $exe

Write-Host "Track installed in $target and started. It starts with Windows; your data stays in $env:LOCALAPPDATA\Track."
