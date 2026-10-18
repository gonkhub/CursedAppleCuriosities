<#
  The friends' build: Release, then a zip WITH the fan-made skin models (TSelky's Spirit Guardian Celeste, funnymonkeyman's
  Bunny Seven) packed in, so a friend gets those skins without hunting down the .vpk files.

  Usage (from anywhere):
    powershell -ExecutionPolicy Bypass -File "packaging\build-friends-package.ps1"

  Output:
    dist\CursedAppleCuriosities-<version>-friends.zip   (share this one with friends only)
    dist\CursedAppleCuriosities-<version>.zip           (the clean public one, made by the Release build as usual)

  NEVER upload the -friends zip to Thunderstore, and never commit the .vpk files: the mod authors haven't been asked yet.
#>

$ErrorActionPreference = 'Stop'
$packagingDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot  = Split-Path -Parent $packagingDir
$csproj       = Join-Path $projectRoot 'CursedAppleCuriosities\CursedAppleCuriosities.csproj'

Write-Host "Building Release..." -ForegroundColor Cyan
dotnet build $csproj -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

& (Join-Path $packagingDir 'package-only.ps1') -WithFanSkins
Write-Host "`nShare dist\*-friends.zip with friends -> r2modman 'Import local mod'." -ForegroundColor Yellow
