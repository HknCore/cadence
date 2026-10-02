# build-store.ps1 – baut das Paket fuer den Microsoft Store (unsigniertes .msix, signiert wird vom Store)
#
#   .\build-store.ps1 -IdentityName "12345Hakan.Cadence" -Publisher "CN=XXXXXXXX-..." -PublisherName "Hakan"
#
# Die drei Werte stehen im Partner Center unter: App > Produktverwaltung > Produktidentitaet.
# Ohne Werte entsteht ein Testpaket mit Platzhaltern (zum Pruefen, ob der Build klappt).
# Voraussetzung: build\hook\CadenceHook.dll existiert (build-installer.ps1 baut sie).
param(
    [string]$Version,
    [string]$IdentityName = "Cadence.Dev",
    [string]$Publisher = "CN=Cadence Dev",
    [string]$PublisherName = "Cadence"
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if (-not $Version) {
    $props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
    $Version = [regex]::Match($props, "<Version>([^<]+)</Version>").Groups[1].Value
}
# Der Store verlangt vier Stellen, die letzte muss 0 sein.
$storeVersion = (($Version -split '[.-]')[0..2] -join '.') + ".0"
Write-Host "Cadence $Version fuer den Microsoft Store ($storeVersion)" -ForegroundColor Magenta

if (-not (Test-Path (Join-Path $root "build\hook\CadenceHook.dll"))) {
    throw "build\hook\CadenceHook.dll fehlt. Zuerst build-installer.ps1 ausfuehren."
}

# ---- Manifest aus der Vorlage -------------------------------------------------
$app = Join-Path $root "src\Cadence.App"
$template = Get-Content (Join-Path $app "Package.Store.appxmanifest.template") -Raw
$manifest = $template.Replace("__IDENTITY_NAME__", $IdentityName).
    Replace("__PUBLISHER__", [Security.SecurityElement]::Escape($Publisher)).
    Replace("__PUBLISHER_NAME__", [Security.SecurityElement]::Escape($PublisherName)).
    Replace("__VERSION__", $storeVersion)
Set-Content (Join-Path $app "Package.Store.appxmanifest") $manifest -Encoding UTF8

# ---- Paket bauen --------------------------------------------------------------
$out = Join-Path $root "build\store"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

dotnet publish (Join-Path $app "Cadence.App.csproj") `
    -c Release -r win-x64 -p:Platform=x64 -p:Version=$Version `
    -p:WindowsPackageType=MSIX `
    -p:GenerateAppxPackageOnBuild=true `
    -p:UapAppxPackageBuildMode=SideloadOnly `
    -p:AppxSymbolPackageEnabled=false `
    -p:AppxBundle=Never `
    -p:AppxPackageSigningEnabled=false `
    -p:AppxPackageDir="$out\"
if ($LASTEXITCODE) { throw "Store-Paket konnte nicht gebaut werden" }

$upload = Get-ChildItem $out -Recurse -Include *.msix | Sort-Object Length -Descending | Select-Object -First 1
if (-not $upload) { throw "Kein .msix gefunden in $out" }
Write-Host ""
Write-Host "Fertig: $($upload.FullName)" -ForegroundColor Green
Write-Host "Im Partner Center unter 'Pakete' hochladen."
