<#
  Packages the ALREADY-BUILT Release DLL into a Thunderstore-format .zip.
  Does NOT run dotnet build (so it is safe to call from the csproj post-build target
  without causing infinite recursion).

  Usage:
    powershell -ExecutionPolicy Bypass -File "packaging\package-only.ps1"

  Output:
    dist\CursedAppleCuriosities-<version>.zip   (version read from manifest.json)

  -WithFanSkins: the friends' build. Also packs the fan-made skin models (Deadlock mod .vpk files: TSelky's Spirit Guardian
  Celeste, funnymonkeyman's Bunny Seven) from FanSkinsDir into plugins/skins, where the mod looks for them, and names the zip
  ...-<version>-friends.zip. NEVER upload that one to Thunderstore: the default zip (without them) is the public release; the
  mod authors haven't been asked yet (user, 2026-10-16).
#>

param(
    # Source2Viewer-CLI folder (MIT) bundled as plugins/vrf. The csproj passes its $(VrfSource).
    [string]$VrfSource = (Join-Path $PSScriptRoot '..\tools\vrf'),
    # Friends' build only: pack the fan-made skin .vpk files too (see above).
    [switch]$WithFanSkins,
    [string]$FanSkinsDir = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Hopoo Games, LLC\Risk of Rain 2\CursedApple\skins')
)

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
    Copy-Item (Join-Path $packagingDir 'THIRD_PARTY_NOTICES.md') (Join-Path $stage 'THIRD_PARTY_NOTICES.md')
    Copy-Item (Join-Path $packagingDir 'LICENSE-SPIRV-Cross.txt') (Join-Path $stage 'LICENSE-SPIRV-Cross.txt')

    $pluginsDir = Join-Path $stage 'plugins'
    New-Item -ItemType Directory -Path $pluginsDir | Out-Null
    Copy-Item $builtDll (Join-Path $pluginsDir 'CursedAppleCuriosities.dll')
    # NLayer (MIT) decodes the player's own cached Deadlock MP3s (RoR2 has Unity audio disabled).
    $nlayer = Join-Path $env:USERPROFILE '.nuget\packages\nlayer\1.16.0\lib\netstandard2.0\NLayer.dll'
    if (-not (Test-Path $nlayer)) { throw "NLayer.dll not found at $nlayer (run a build to restore it)." }
    Copy-Item $nlayer (Join-Path $pluginsDir 'NLayer.dll')
    # Our own Deadlock map converter (runs the stage conversion in its own process).
    $mapConvert = Join-Path $projectRoot 'CursedAppleMapConvert\bin\Release\net472'
    if (-not (Test-Path (Join-Path $mapConvert 'CursedAppleMapConvert.exe'))) { throw "CursedAppleMapConvert.exe not built at $mapConvert." }
    Copy-Item (Join-Path $mapConvert 'CursedAppleMapConvert.exe') $pluginsDir
    Copy-Item (Join-Path $mapConvert 'CursedAppleMapConvert.exe.config') $pluginsDir
    # Model converter: turns the player's OWN Deadlock hero models into glTF at runtime. No Valve file is shipped.
    if (-not (Test-Path (Join-Path $VrfSource 'Source2Viewer-CLI.exe'))) { throw "Source2Viewer-CLI.exe not found in $VrfSource." }
    Copy-Item $VrfSource (Join-Path $pluginsDir 'vrf') -Recurse
    # Friends' build: the fan-made skin models, beside the DLL (DeadlockInstall.FindVpkWith looks in plugins\skins).
    if ($WithFanSkins) {
        $vpks = @(Get-ChildItem -Path $FanSkinsDir -Filter '*.vpk' -File -ErrorAction SilentlyContinue)
        if ($vpks.Count -eq 0) { throw "No fan skin .vpk files in $FanSkinsDir." }
        $skinsDir = Join-Path $pluginsDir 'skins'
        New-Item -ItemType Directory -Path $skinsDir | Out-Null
        foreach ($v in $vpks) { Copy-Item $v.FullName (Join-Path $skinsDir $v.Name); Write-Host "  + fan skin $($v.Name)" -ForegroundColor DarkCyan }
    }

    if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
    $zipName = if ($WithFanSkins) { "$pkgName-$version-friends.zip" } else { "$pkgName-$version.zip" }
    $zipPath = Join-Path $distDir $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    # Not Compress-Archive: Windows PowerShell 5.1 writes '\' entry separators, which
    # Thunderstore and Linux/Proton mod managers don't treat as folders. Write '/' explicitly.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    $stageFull = (Get-Item $stage).FullName
    try {
        Get-ChildItem $stageFull -Recurse -File | ForEach-Object {
            $entry = $_.FullName.Substring($stageFull.Length).TrimStart('\', '/').Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry)
        }
    }
    finally { $zip.Dispose() }
    Write-Host "Packaged $pkgName v$version -> dist\$zipName" -ForegroundColor Green
    if ($WithFanSkins) { Write-Host "Friends' build (fan skins inside): do NOT upload this one to Thunderstore." -ForegroundColor Yellow }
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
