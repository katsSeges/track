# Runs all tests with the 100% coverage gate on Track.Core.
# Fails (exit code != 0) when a test fails or line/branch/method coverage drops below 100%.
# Report: coverage/coverage.cobertura.xml
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $root 'tests/Track.Core.Tests/Track.Core.Tests.csproj') -c Release --nologo
exit $LASTEXITCODE
