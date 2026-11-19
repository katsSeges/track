# Builds a self-contained Track (no .NET install needed), a ZIP and an Inno Setup installer.
#   artifacts\Track-<version>-win-x64.zip, Track-<version>-win-x64-setup.exe and a .sha256 for each.
# Without Inno Setup 6 only the ZIP is built, unless -RequireInstaller (CI and releases) makes that an error.
# Zip layout (what scripts\install.ps1 expects):
#   Track-<version>-win-x64\publish\Track.exe
#   Track-<version>-win-x64\scripts\install.ps1, uninstall.ps1
#   Track-<version>-win-x64\README.md, INSTALL.txt
# Used by .github/workflows/release.yml; runs the same locally.
param(
    [string] $Version,
    [string] $IsccPath,
    [switch] $RequireInstaller
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Track.App\Track.App.csproj'

# The version comes from the app project; a tag (v1.2.3) must match it.
$projectVersion = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $Version) { $Version = $projectVersion }
if ($Version -ne $projectVersion) { throw "Version '$Version' does not match <Version>$projectVersion</Version> in Track.App.csproj" }

if (-not $IsccPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) {
        $IsccPath = $compiler.Source
    } else {
        $IsccPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    }
}
$buildInstaller = Test-Path -LiteralPath $IsccPath -PathType Leaf
if (-not $buildInstaller) {
    $missing = 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php or pass -IsccPath <path-to-ISCC.exe>.'
    if ($RequireInstaller) { throw $missing }
    Write-Warning "$missing Building only the ZIP."
}

$name = "Track-$Version-win-x64"
$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts $name
$zip = Join-Path $artifacts "$name.zip"
$setup = Join-Path $artifacts "$name-setup.exe"
Remove-Item $stage, $zip, "$zip.sha256", $setup, "$setup.sha256" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $stage 'scripts') | Out-Null

Write-Host "Publishing Track $Version (self-contained, win-x64) ..."
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
    -o (Join-Path $stage 'publish') --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Copy-Item (Join-Path $root 'scripts\install.ps1'), (Join-Path $root 'scripts\uninstall.ps1') (Join-Path $stage 'scripts')
Copy-Item (Join-Path $root 'README.md') $stage
@"
Track $Version for Windows 10/11 (x64). No .NET installation needed.

Install (copies Track to %LOCALAPPDATA%\Programs\Track, starts it with Windows and starts it now):
    powershell -ExecutionPolicy Bypass -File scripts\install.ps1

Uninstall (keeps your data in %LOCALAPPDATA%\Track unless you add -RemoveData):
    powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1

Or just run publish\Track.exe without installing.
"@ | Set-Content (Join-Path $stage 'INSTALL.txt') -Encoding utf8

Compress-Archive -Path $stage -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name.zip" | Set-Content "$zip.sha256" -Encoding ascii -NoNewline
Write-Host "Package: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB), sha256 $hash"

if (-not $buildInstaller) { return }
& $IsccPath /Qp "/DAppVersion=$Version" "/DPublishDir=$(Join-Path $stage 'publish')" "/DOutputDir=$artifacts" `
    (Join-Path $PSScriptRoot 'installer\Track.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed ($LASTEXITCODE)" }
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name-setup.exe" | Set-Content "$setup.sha256" -Encoding ascii -NoNewline
Write-Host "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB), sha256 $hash"
