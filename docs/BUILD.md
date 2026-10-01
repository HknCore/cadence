# Cadence bauen und testen

## Aufbau

```
Cadence.sln
build.ps1                    baut alles in einem Schritt
build/hook/CadenceHook.dll   vorgebaute Hook-DLL (wird von build.ps1 neu gebaut, wenn CMake da ist)
src/
  Cadence.Hook/   C++  – DLL, die im Spiel läuft: Present-Hooks + Limiter
  Cadence.Core/   C#   – Spielerkennung, Injektion, Profile, Anti-Cheat-Prüfung, Statistik, Hotkeys
  Cadence.App/    C#   – WinUI-3-Oberfläche (Grau/Lila, Mica, Animationen)
```

So greift alles ineinander:

1. Die App erkennt ein Spiel (Profil vorhanden oder Vollbild-Programm mit Grafik-API).
2. Sie prüft es auf Anti-Cheat. Wird etwas gefunden, passiert nichts weiter.
3. Sie legt einen gemeinsamen Speicherbereich `Local\Cadence_<PID>` an und lädt `CadenceHook.dll` ins Spiel.
4. Die DLL hängt sich an `Present` / `Present1` (DirectX 10–12) bzw. `wglSwapBuffers` (OpenGL) und wartet dort bis zum exakten Zeitpunkt des nächsten Bildes. Grob schläft sie über einen hochauflösenden Windows-Timer, die letzte Millisekunde spinnt sie aktiv.
5. Ziel-FPS und Modus liest die DLL bei jedem Bild aus dem gemeinsamen Speicher. Änderungen wirken deshalb sofort, ohne Neustart des Spiels. Die gemessenen Frametimes schreibt sie zurück; daraus zeichnet die App Graph und Statistik.

### Die drei Modi

| Modus | Wo gewartet wird | Wirkung |
|---|---|---|
| Ausgewogen | vor `Present` | Bilder erscheinen im exakten Takt |
| Niedrige Latenz | nach `Present` | der nächste Frame startet im Takt; Eingaben werden später gelesen, also weniger Input-Lag |
| Max. Glätte | vor `Present`, doppelt so langes Spin-Fenster | noch flachere Frametimes, etwas mehr CPU-Last |

## Bauen

Voraussetzungen:

- Windows 10 22H2 oder Windows 11 (x64)
- Visual Studio 2022 mit den Workloads **„.NET-Desktopentwicklung“** und **„Desktopentwicklung mit C++“** (bringt CMake mit)
- .NET 8 SDK

Danach in einer **Developer PowerShell für VS 2022** im Projektordner:

```powershell
.\build.ps1
```

Das Skript baut zuerst die Hook-DLL und dann die App. Am Ende gibt es den Pfad zur `Cadence.exe` aus. Alternativ `Cadence.sln` in Visual Studio öffnen, Plattform **x64** wählen und mit F5 starten. Vorher muss die DLL einmal mit `.\build.ps1` gebaut worden sein, oder du nutzt die mitgelieferte.

Falls der Build an einem NuGet-Paket scheitert (z. B. Windows App SDK), zuerst Visual Studio über den Visual Studio Installer aktualisieren (bringt ein neueres .NET SDK mit). Hilft das nicht, melde die Fehlermeldung als Issue.

## Installer bauen

Zusätzlich zu Visual Studio wird [Inno Setup](https://jrsoftware.org/isdl.php) 6.6 oder neuer benötigt (`winget install JRSoftware.InnoSetup`). Dann:

```powershell
.\build-installer.ps1
```

Das Ergebnis liegt unter `build\installer\Cadence-Setup-<Version>.exe`. Die Versionsnummer steht an einer einzigen Stelle: `Directory.Build.props`.

### Automatische Releases

Sobald ein Versions-Tag auf GitHub landet, baut GitHub Actions den Installer und hängt ihn an ein neues Release:

```powershell
git tag v0.2.2
git push origin v0.2.2
```

## Testen

Bitte nur mit **Offline- bzw. Einzelspieler-Spielen ohne Anti-Cheat** testen.

1. Cadence starten, dann ein 64-Bit-Spiel mit DirectX 11 oder 12.
2. In Cadence unter **Profile → Profil hinzufügen** das Spiel auswählen.
   - Erwartet: In der Übersicht steht „Läuft · Limit aktiv“, und der Graph liegt flach auf der gestrichelten Ziel-Linie.
3. Presets antippen (30 / 60 / 90 …) und im Spiel **Strg + Alt + ↑ / ↓** drücken.
   - Erwartet: Das Limit ändert sich sofort.
4. Alle drei Modi durchprobieren. Auf Ruckler und auf die Maus-Reaktion achten.
5. Den Limiter ausschalten.
   - Erwartet: Die Kurve wird orange und unruhig. Cadence misst dann nur noch.
6. Unter **Statistiken** eine Minute aufnehmen, stoppen und als CSV exportieren.
7. Optional, für einen unabhängigen Vergleich: dieselbe Szene mit [CapFrameX](https://www.capframex.com/) oder PresentMon messen.

Für Fehlerberichte bitte angeben:

- Name und Grafik-API
- was als Status in der Übersicht steht
- einen Screenshot vom Graphen
- jede Fehlermeldung im Wortlaut

## Profile

Profile liegen als JSON unter `%LOCALAPPDATA%\Cadence\profiles.json`.
