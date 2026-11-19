# Run after package.ps1. Uses a unique AppId, install directory, Start menu group and TRACK_DATA.
# Refuses to launch Track if that could affect an existing app or startup entry. Never enables startup.
param(
    [Parameter(Mandatory)] [string] $IsccPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root 'src\Track.App\Track.App.csproj') -Raw)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
$publish = Join-Path $root "artifacts\Track-$version-win-x64\publish"
$id = 'TrackInstallerTest-' + [guid]::NewGuid().ToString('N')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) $id
$target = Join-Path $testRoot 'app'
$data = Join-Path $testRoot 'data'
$settings = Join-Path $data 'settings.json'
$group = Join-Path ([Environment]::GetFolderPath('Programs')) $id
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${id}_is1"
$originalData = $env:TRACK_DATA
$originalHome = $env:TRACK_HOME
$originalDiag = $env:TRACK_DIAG
$app = $null
$passed = $false

function Assert($Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Get-StartupCommand {
    [Microsoft.Win32.Registry]::GetValue('HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run', 'Track', $null)
}

function Invoke-Setup([string] $LogName) {
    $args = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=""',
        "/DIR=`"$target`"", "/GROUP=`"$id`"", "/LOG=`"$(Join-Path $testRoot $LogName)`"")
    $process = Start-Process $setup -ArgumentList $args -Wait -PassThru
    Assert ($process.ExitCode -eq 0) "Setup failed ($($process.ExitCode)); see $testRoot"
}

function Invoke-Uninstall {
    $process = Start-Process (Join-Path $target 'unins000.exe') -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        "/LOG=`"$(Join-Path $testRoot 'uninstall.log')`"") -PassThru -Wait
    Assert ($process.ExitCode -eq 0) "Uninstall failed ($($process.ExitCode)); see $testRoot"
}

try {
    Assert (Test-Path (Join-Path $publish 'Track.exe')) 'Run scripts\package.ps1 first.'
    Assert (-not (Get-Process Track -ErrorAction SilentlyContinue)) 'Close Track or use a clean Windows test account before testing.'
    $startup = Get-StartupCommand
    Assert ($null -eq $startup) 'A real Track startup entry exists. Use a clean Windows test account; it will not be modified.'
    New-Item -ItemType Directory -Force $testRoot, $data, (Join-Path $testRoot 'home') | Out-Null
    $env:TRACK_DATA = $data
    $env:TRACK_HOME = Join-Path $testRoot 'home'
    $env:TRACK_DIAG = '1'
    $helper = Join-Path $PSScriptRoot 'installer\Set-StartupPreference.ps1'
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $helper `
        -SettingsPath $settings -Enabled true -ErrorFile (Join-Path $testRoot 'helper-error.txt')
    Assert ($LASTEXITCODE -eq 0) 'Could not save an enabled startup preference.'
    Assert ((Get-Content $settings -Raw | ConvertFrom-Json).startWithWindows -eq $true) 'Enabled startup preference was not saved.'
    & $IsccPath /Q "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$testRoot" `
        "/DTrackAppId=$id" (Join-Path $PSScriptRoot 'installer\Track.iss')
    Assert ($LASTEXITCODE -eq 0) 'Test installer compilation failed.'
    $setup = Join-Path $testRoot "Track-$version-win-x64-setup.exe"

    # Invalid settings must fail before installation, without replacing the file.
    '[{"startWithWindows":true}]' | Set-Content $settings -Encoding ascii
    $invalid = Get-Content $settings -Raw
    $process = Start-Process $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        '/TASKS=""', "/DIR=`"$target`"", "/GROUP=`"$id`"") -Wait -PassThru
    Assert ($process.ExitCode -ne 0) 'Setup accepted a non-object settings file.'
    Assert ((Get-Content $settings -Raw) -eq $invalid) 'Setup changed invalid settings.'
    Assert (-not (Test-Path (Join-Path $target 'Track.exe'))) 'Setup installed files despite invalid settings.'

    Remove-Item -LiteralPath $settings
    Invoke-Setup 'install.log'
    Assert (Test-Path (Join-Path $target 'Track.exe')) 'Track.exe missing.'
    Assert (Test-Path $uninstallKey) 'Apps & features entry missing.'
    $entry = Get-ItemProperty $uninstallKey
    Assert ($entry.DisplayVersion -eq $version) 'Wrong installed version.'
    Assert ($entry.UninstallString -like "*$target*") 'Wrong uninstall command.'
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $group 'Track.lnk'))
    Assert ($shortcut.TargetPath -eq (Join-Path $target 'Track.exe')) 'Start menu shortcut target is wrong.'
    Assert ((Get-Content $settings -Raw | ConvertFrom-Json).startWithWindows -eq $false) 'Fresh unchecked install did not save false.'

    # Normal launch, deliberately without --no-startup: an unchecked install must stay disabled.
    $app = Start-Process (Join-Path $target 'Track.exe') -PassThru
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path (Join-Path $data 'diag.txt')) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        Assert (-not $app.HasExited) 'Track exited before it became ready.'
    }
    Assert (Test-Path (Join-Path $data 'diag.txt')) 'Track did not become ready.'
    Assert ($null -eq (Get-StartupCommand)) 'Normal launch unexpectedly enabled startup.'
    $process = Start-Process $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        '/TASKS=""', "/DIR=`"$target`"", "/GROUP=`"$id`"") -Wait -PassThru
    Assert ($process.ExitCode -ne 0) 'Setup did not block a running Track.'
    Assert (-not $app.HasExited) 'Setup terminated a running app without consent.'
    Stop-Process -Id $app.Id -ErrorAction Stop
    $app.WaitForExit()
    $app = $null

    # Upgrading changes only the startup preference, preserving unrelated/unknown settings and data.
    '{"startWithWindows":true,"refreshSeconds":45,"future":{"label":"keep","items":[1,2]}}' |
        Set-Content $settings -Encoding ascii
    'keep usage' | Set-Content (Join-Path $data 'usage-sentinel.txt') -Encoding ascii
    Invoke-Setup 'upgrade.log'
    $saved = Get-Content $settings -Raw | ConvertFrom-Json
    Assert ($saved.startWithWindows -eq $false) 'Upgrade did not persist unchecked startup.'
    Assert ($saved.refreshSeconds -eq 45 -and $saved.future.label -eq 'keep' -and $saved.future.items.Count -eq 2) 'Upgrade lost unrelated settings.'
    $beforeUninstall = Get-Content $settings -Raw
    Invoke-Uninstall
    Assert (-not (Test-Path $target)) 'Installed files remain after uninstall.'
    Assert (-not (Test-Path $uninstallKey)) 'Apps & features entry remains after uninstall.'
    Assert (-not (Test-Path (Join-Path $group 'Track.lnk'))) 'Start menu shortcut remains after uninstall.'
    Assert ((Get-Content $settings -Raw) -eq $beforeUninstall) 'Uninstall changed settings.'
    Assert ((Get-Content (Join-Path $data 'usage-sentinel.txt') -Raw).Trim() -eq 'keep usage') 'Uninstall removed usage data.'
    Assert ($null -eq (Get-StartupCommand)) 'Startup entry changed.'
    $passed = $true
    Write-Host 'Installer checks passed: fresh install, normal launch with startup off, running-app guard, upgrade, shortcuts, Apps & features and clean uninstall.'
} finally {
    try {
        if ($app -and -not $app.HasExited) {
            Stop-Process -Id $app.Id -ErrorAction Stop
            $app.WaitForExit()
        }
        if (Test-Path (Join-Path $target 'unins000.exe')) {
            Invoke-Uninstall
        }
    } finally {
        $env:TRACK_DATA = $originalData
        $env:TRACK_HOME = $originalHome
        $env:TRACK_DIAG = $originalDiag
    }
    if ($passed) {
        # The unique test directory contains only this run's generated files and logs.
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    } elseif (Test-Path $testRoot) {
        Write-Warning "Installer test failed; diagnostic files retained in $testRoot"
    }
}
