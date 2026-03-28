param(
    [string]$Configuration = "Release",
    [switch]$Restore
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "L2Companion\L2Companion.csproj"
$dotnetHome = Join-Path $repoRoot ".dotnet_home"
$nugetPackages = Join-Path $repoRoot ".nuget\packages"
$nugetConfig = Join-Path $repoRoot "NuGet.Config"

New-Item -ItemType Directory -Path $dotnetHome -Force | Out-Null
New-Item -ItemType Directory -Path $nugetPackages -Force | Out-Null

$env:DOTNET_CLI_HOME = $dotnetHome
$env:NUGET_PACKAGES = $nugetPackages

if ($Restore) {
    Write-Host "[build] Restoring packages..." -ForegroundColor Cyan
    dotnet restore $projectPath --configfile $nugetConfig
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "[build] Building ($Configuration)..." -ForegroundColor Cyan
$buildArgs = @($projectPath, "-c", $Configuration)
if (-not $Restore) {
    $buildArgs += "--no-restore"
}

dotnet build @buildArgs
exit $LASTEXITCODE
