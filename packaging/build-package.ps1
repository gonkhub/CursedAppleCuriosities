<#
  Builds the mod in Release, then packages it into a Thunderstore-format .zip.

  Usage (from anywhere):
    powershell -ExecutionPolicy Bypass -File "packaging\build-package.ps1"

  Output:
    dist\CursedAppleCuriosities-<version>.zip

  Share the zip with friends -> r2modman: Settings -> Import local mod.
  Or upload the same zip to Thunderstore.

  NOTE: This runs a full build. The csproj post-build target instead calls
  package-only.ps1 directly (no rebuild) to avoid recursion.
#>

$ErrorActionPreference = 'Stop'

$packagingDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot  = Split-Path -Parent $packagingDir
$csproj       = Join-Path $projectRoot 'CursedAppleCuriosities\CursedAppleCuriosities.csproj'

Write-Host "Building Release..." -ForegroundColor Cyan
dotnet build $csproj -c Release | Out-Null

# The post-build target already produces the zip, but call package-only explicitly
# so this script also works if the post-build packaging target is ever disabled.
& (Join-Path $packagingDir 'package-only.ps1')

Write-Host "`nShare dist\*.zip -> friends use r2modman 'Import local mod'." -ForegroundColor Yellow
