<#
  Packages the ALREADY-BUILT Release DLL into a Thunderstore-format .zip.
  Does NOT run dotnet build (so it is safe to call from the csproj post-build target
  without causing infinite recursion).

  Usage:
    powershell -ExecutionPolicy Bypass -File "packaging\package-only.ps1"

  Output:
    dist\CursedAppleCuriosities-<version>.zip   (version read from manifest.json)
#>

$ErrorActionPreference = 'Stop'

$packagingDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot  = Split-Path -Parent $packagingDir
$builtDll     = Join-Path $projectRoot 'CursedAppleCuriosities\bin\Release\netstandard2.1\CursedAppleCuriosities.dll'
$distDir      = Join-Path $projectRoot 'dist'

if (-not (Test-Path $builtDll)) {
    throw "Built DLL not found at $builtDll. Build the project in Release first."
}

$manifestPath = Join-Path $packagingDir 'manifest.json'
$manifest     = Get-Content $manifestPath -Raw | ConvertFrom-Json
$version      = $manifest.version_number
$pkgName      = $manifest.name

# Stage the Thunderstore package contents in a clean temp folder.
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("cac_pkg_" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item $manifestPath                            (Join-Path $stage 'manifest.json')
    Copy-Item (Join-Path $packagingDir 'README.md')    (Join-Path $stage 'README.md')
    Copy-Item (Join-Path $packagingDir 'CHANGELOG.md') (Join-Path $stage 'CHANGELOG.md')
    Copy-Item (Join-Path $packagingDir 'icon.png')     (Join-Path $stage 'icon.png')

    $pluginsDir = Join-Path $stage 'plugins'
    New-Item -ItemType Directory -Path $pluginsDir | Out-Null
    Copy-Item $builtDll (Join-Path $pluginsDir 'CursedAppleCuriosities.dll')
    # NLayer (MIT) decodes the player's own cached Deadlock MP3s (RoR2 has Unity audio disabled).
    $nlayer = Join-Path $env:USERPROFILE '.nuget\packages\nlayer\1.16.0\lib\netstandard2.0\NLayer.dll'
    if (Test-Path $nlayer) { Copy-Item $nlayer (Join-Path $pluginsDir 'NLayer.dll') }

    if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
    $zipPath = Join-Path $distDir "$pkgName-$version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -Force
    Write-Host "Packaged $pkgName v$version -> dist\$pkgName-$version.zip" -ForegroundColor Green
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
