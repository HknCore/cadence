# build-installer.ps1 – baut Cadence und verpackt alles in Cadence-Setup-<Version>.exe
#
#   .\build-installer.ps1              -> Version aus Directory.Build.props
#   .\build-installer.ps1 -Version 0.3.0
#
# Voraussetzungen: Visual Studio 2022 (".NET-Desktopentwicklung" + "Desktopentwicklung mit C++")
#                  und Inno Setup 6.6+ oder 7  ->  winget install JRSoftware.InnoSetup
param(
    [string]$Version,
    [switch]$SkipHook
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if (-not $Version) {
    $props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
    $Version = [regex]::Match($props, "<Version>([^<]+)</Version>").Groups[1].Value
}
Write-Host "Cadence $Version" -ForegroundColor Magenta

# ---- 1. Hook-DLL --------------------------------------------------------------
$hookOut = Join-Path $root "build\hook"
if (-not $SkipHook -and (Get-Command cmake -ErrorAction SilentlyContinue)) {
    Write-Host "== Hook-DLL ==" -ForegroundColor Magenta
    $cmakeDir = Join-Path $root "build\hook-cmake"
    cmake -S (Join-Path $root "src\Cadence.Hook") -B $cmakeDir -A x64
    if ($LASTEXITCODE) { throw "CMake-Konfiguration fehlgeschlagen" }
    cmake --build $cmakeDir --config Release
    if ($LASTEXITCODE) { throw "Hook-DLL konnte nicht gebaut werden" }
    New-Item -ItemType Directory -Force $hookOut | Out-Null
    Copy-Item (Join-Path $cmakeDir "Release\CadenceHook.dll") $hookOut -Force
} elseif (-not (Test-Path (Join-Path $hookOut "CadenceHook.dll"))) {
    throw "Keine CadenceHook.dll vorhanden und CMake nicht gefunden."
} else {
    Write-Host "Verwende vorhandene build\hook\CadenceHook.dll"
}

# ---- 2. App veroeffentlichen (enthaelt .NET und Windows App SDK) --------------
Write-Host "== App ==" -ForegroundColor Magenta
$publish = Join-Path $root "build\publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root "src\Cadence.App\Cadence.App.csproj") `
    -c Release -r win-x64 --self-contained true -p:Platform=x64 `
    -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw "dotnet publish fehlgeschlagen" }
if (-not (Test-Path (Join-Path $publish "CadenceHook.dll"))) { throw "CadenceHook.dll fehlt im Publish-Ordner" }

# ---- 3. Installer -------------------------------------------------------------
Write-Host "== Installer ==" -ForegroundColor Magenta
$iscc = Get-ChildItem "${env:ProgramFiles}\Inno Setup*\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup*\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup*\ISCC.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup nicht gefunden. Installieren mit: winget install JRSoftware.InnoSetup" }

& $iscc.FullName "/DAppVersion=$Version" "/DSourceDir=$publish" (Join-Path $root "installer\Cadence.iss")
if ($LASTEXITCODE) { throw "Inno Setup ist fehlgeschlagen" }

$setup = Join-Path $root "build\installer\Cadence-Setup-$Version.exe"
$size = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host ""
Write-Host "Fertig: $setup ($size MB)" -ForegroundColor Green
