param(
    [string]$Configuration = "Release",
    [string]$Output = "",
    [switch]$Restore,
    [bool]$CleanOutput = $true
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "L2Companion\L2Companion.csproj"
$dotnetHome = Join-Path $repoRoot ".dotnet_home"
$nugetPackages = Join-Path $repoRoot ".nuget\packages"
$nugetConfig = Join-Path $repoRoot "NuGet.Config"

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $repoRoot "out-latest"
}

New-Item -ItemType Directory -Path $dotnetHome -Force | Out-Null
New-Item -ItemType Directory -Path $nugetPackages -Force | Out-Null

if ($CleanOutput -and (Test-Path $Output)) {
    # Preserve the Config directory (character profiles, saved settings) across rebuilds
    Get-ChildItem -Path $Output -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'Config' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Path $Output -Force | Out-Null

$env:DOTNET_CLI_HOME = $dotnetHome
$env:NUGET_PACKAGES = $nugetPackages

if ($Restore) {
    Write-Host "[publish] Restoring packages..." -ForegroundColor Cyan
    dotnet restore $projectPath --configfile $nugetConfig
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "[publish] Publishing to: $Output" -ForegroundColor Cyan
$publishArgs = @($projectPath, "-c", $Configuration, "-o", $Output)
if (-not $Restore) {
    $publishArgs += "--no-restore"
}

dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "[publish] Done: $Output\\L2Companion.exe" -ForegroundColor Green
