# build.ps1 – baut Cadence komplett (Hook-DLL + App)
#   .\build.ps1                 -> Release
#   .\build.ps1 -Configuration Debug
#   .\build.ps1 -SkipHook       -> vorhandene build\hook\CadenceHook.dll verwenden
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$SkipHook
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$hookOut = Join-Path $root "build\hook"

Write-Host "== 1/2 Hook-DLL ==" -ForegroundColor Magenta
if ($SkipHook) {
    Write-Host "uebersprungen (-SkipHook)"
} elseif (Get-Command cmake -ErrorAction SilentlyContinue) {
    $cmakeDir = Join-Path $root "build\hook-cmake"
    cmake -S (Join-Path $root "src\Cadence.Hook") -B $cmakeDir -A x64
    if ($LASTEXITCODE) { throw "CMake-Konfiguration fehlgeschlagen" }
    cmake --build $cmakeDir --config $Configuration
    if ($LASTEXITCODE) { throw "Hook-DLL konnte nicht gebaut werden" }
    New-Item -ItemType Directory -Force $hookOut | Out-Null
    Copy-Item (Join-Path $cmakeDir "$Configuration\CadenceHook.dll") $hookOut -Force
} elseif (Test-Path (Join-Path $hookOut "CadenceHook.dll")) {
    Write-Warning "CMake nicht gefunden – verwende die mitgelieferte build\hook\CadenceHook.dll"
} else {
    throw "CMake nicht gefunden und keine CadenceHook.dll vorhanden. Visual Studio mit 'Desktopentwicklung mit C++' installieren."
}

Write-Host "== 2/2 App ==" -ForegroundColor Magenta
dotnet build (Join-Path $root "src\Cadence.App\Cadence.App.csproj") -c $Configuration -p:Platform=x64
if ($LASTEXITCODE) { throw "App-Build fehlgeschlagen" }

$exe = Join-Path $root "src\Cadence.App\bin\x64\$Configuration\net8.0-windows10.0.19041.0\win-x64\Cadence.exe"
Write-Host ""
Write-Host "Fertig: $exe" -ForegroundColor Green
