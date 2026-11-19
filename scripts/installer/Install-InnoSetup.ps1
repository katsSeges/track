# Installs a pinned Inno Setup 6 on a CI runner (used by ci.yml and release.yml).
# The download must match the SHA-256 GitHub publishes for that release asset and carry a valid signature.
$ErrorActionPreference = 'Stop'
$version = '6.7.4'
$sha256 = 'a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c'
$url = "https://github.com/jrsoftware/issrc/releases/download/is-$($version -replace '\.', '_')/innosetup-$version.exe"

$temp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$installer = Join-Path $temp "innosetup-$version.exe"
Invoke-WebRequest $url -OutFile $installer
$actual = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $sha256) { throw "Inno Setup $version download has sha256 $actual, expected $sha256" }
if ((Get-AuthenticodeSignature $installer).Status -ne 'Valid') { throw 'Inno Setup installer signature is not valid' }

$process = Start-Process $installer -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/ALLUSERS' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed ($($process.ExitCode))" }
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) { throw "ISCC.exe not found at $iscc" }
Write-Host "Inno Setup $version installed: $iscc"
