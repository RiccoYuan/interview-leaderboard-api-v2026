# restore → build → test → benchmark (Dry)
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBenchmark
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

Write-Host '==> dotnet restore' -ForegroundColor Cyan
dotnet restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> dotnet build -c $Configuration" -ForegroundColor Cyan
dotnet build -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> dotnet test -c $Configuration --no-build" -ForegroundColor Cyan
dotnet test -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipBenchmark) {
    Write-Host '==> benchmark (Dry smoke)' -ForegroundColor Cyan
    dotnet run -c $Configuration --no-build --project tests/Leaderboard.Benchmarks -- --job Dry --filter *IndexComparison*
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host 'Done.' -ForegroundColor Green
