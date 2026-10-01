<p align="center">
  <img src="docs/images/hero.png" alt="Cadence – Jeder Frame. Exakt im Takt. Präziser FPS-Limiter für Windows." width="100%">
</p>

<p align="center">
  <img alt="Windows 10 und 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-2B2B2B?style=flat-square&labelColor=1C1C1C">
  <img alt="DirectX 10 bis 12 und OpenGL" src="https://img.shields.io/badge/DirectX%2010–12-OpenGL-B9A3FF?style=flat-square&labelColor=1C1C1C">
  <img alt="Version 0.2" src="https://img.shields.io/badge/Version-0.2-B9A3FF?style=flat-square&labelColor=1C1C1C">
  <img alt="Status: Early Access" src="https://img.shields.io/badge/Status-Early%20Access-E0A458?style=flat-square&labelColor=1C1C1C">
</p>

<p align="center">
  <b><a href="#so-legst-du-los">Loslegen</a></b> ·
  <a href="#funktionen">Funktionen</a> ·
  <a href="#die-drei-modi">Modi</a> ·
  <a href="#die-perfekte-einrichtung">Einrichtung</a> ·
  <a href="#häufige-fragen">FAQ</a> ·
  <a href="docs/BUILD.md">Selbst bauen</a>
</p>

<br>

## Warum fühlen sich 30 FPS auf der Konsole flüssig an – und am PC nicht?

Die Antwort ist nicht die Bildrate, sondern **der Abstand zwischen den Bildern**.

Ein FPS-Zähler zeigt einen Durchschnitt. Er sagt nichts darüber, ob jedes Bild nach genau 16,67 ms kommt oder abwechselnd nach 12 und nach 21 ms. Genau dieses Schwanken spürst du als Mikroruckler, auch wenn oben rechts konstant „60“ steht. Konsolen takten ihre Bilder deshalb streng. Am PC übernehmen das meist ungenaue Limiter im Spiel oder im Treiber.

**Cadence bringt diesen Takt auf den PC.** Es hält jedes Bild auf den Bruchteil einer Millisekunde genau ein. Das Ergebnis: dieselbe Bildrate, aber ein spürbar ruhigeres Spielgefühl.

<p align="center">
  <img src="docs/images/pacing.png" alt="Vergleich der Frametimes: ein typischer Limiter schwankt stark, Cadence liegt flach auf der Ziel-Linie" width="100%">
</p>

<br>

## Funktionen

<table>
<tr>
<td width="50%" valign="top">

### Präzises Frame Pacing
Cadence wartet auf einem hochauflösenden Windows-Timer und gleicht die letzten Mikrosekunden aktiv aus. Statt einer zappelnden Kurve bekommst du eine flache Linie.

</td>
<td width="50%" valign="top">

### Live ändern, ohne Neustart
Ziel-FPS und Modus wirken sofort, auch mitten im Spiel. Mit <kbd>Strg</kbd> + <kbd>Alt</kbd> + <kbd>↑</kbd> / <kbd>↓</kbd> wechselst du die Stufe, ohne das Spiel zu verlassen.

</td>
</tr>
<tr>
<td valign="top">

### Profile pro Spiel
Jedes Spiel bekommt seine eigenen Werte, etwa 40 FPS für Cyberpunk und 90 für Baldur's Gate. Cadence erkennt das Spiel beim Start und lädt das Profil automatisch.

</td>
<td valign="top">

### Passt sich deinem Monitor an
Für alle Spiele ohne eigenes Profil gilt automatisch die Bildwiederholrate deines Hauptbildschirms. Neuer Monitor? Cadence merkt es von selbst.

</td>
</tr>
<tr>
<td valign="top">

### Overlay im Spiel
FPS, Frametime und ein Mini-Graph direkt über dem Spiel. Drei Varianten, frei wählbare Ecke, durchklickbar. Ein- und ausblenden mit <kbd>Strg</kbd> + <kbd>Alt</kbd> + <kbd>O</kbd>.

</td>
<td valign="top">

### Messen statt raten
Aufnahme per <kbd>Strg</kbd> + <kbd>Alt</kbd> + <kbd>R</kbd>, danach Ø FPS, 1 % und 0,1 % Lows, Streuung und Stotterer auf einen Blick. Mehrere Aufnahmen direkt vergleichen, Export als CSV oder Bild.

</td>
</tr>
<tr>
<td valign="top">

### Kühler, leiser, sparsamer
Wer nicht hunderte überflüssige Bilder berechnet, spart Strom und Wärme. Die frei gewordene Leistung steckst du in höhere Grafikdetails.

</td>
<td valign="top">

### Schutz vor Anti-Cheat-Sperren
Cadence prüft jedes Spiel vor dem Start auf Anti-Cheat-Systeme und hält sich bei einem Treffer konsequent heraus.

</td>
</tr>
</table>

<br>

## Eine App, die sich wie Windows anfühlt

Cadence ist mit WinUI 3 gebaut, der Oberfläche von Windows 11 selbst: Mica-Hintergrund, flüssige Übergänge, dunkles Design mit lila Akzent. Keine überladene Gaming-Software, sondern ein Werkzeug, das sich ins System einfügt.

<p align="center">
  <img src="docs/images/app-overview.png" alt="Cadence Übersicht: aktives Spiel, Ziel-FPS, Modus und Live-Frametime-Graph" width="100%">
</p>

<table>
<tr>
<td width="50%" valign="top">
  <img src="docs/images/app-profiles.png" alt="Profilverwaltung mit eigenen Werten für jedes Spiel" width="100%">
  <p align="center"><sub><b>Profile</b> – jedes Spiel mit eigenem Ziel und Modus</sub></p>
</td>
<td width="50%" valign="top">
  <img src="docs/images/app-stats.png" alt="Statistikseite mit Verteilung der Frametimes und Vergleich mehrerer Aufnahmen" width="100%">
  <p align="center"><sub><b>Statistiken</b> – Aufnahmen auswerten und vergleichen</sub></p>
</td>
</tr>
</table>

<br>

## Die drei Modi

<p align="center">
  <img src="docs/images/modes.png" alt="Zeitleiste der drei Modi: wann das Spiel arbeitet, wann Cadence wartet und wann das Bild erscheint" width="100%">
</p>

| Modus | Ideal für | Kurz erklärt |
|---|---|---|
| **Ausgewogen** | fast alles | Cadence wartet direkt vor der Bildausgabe. Jedes Bild erscheint im exakten Takt. |
| **Niedrige Latenz** | schnelle Spiele mit Maus | Cadence wartet nach der Ausgabe. Das Spiel liest deine Eingaben dadurch so spät wie möglich, das senkt den Input-Lag. |
| **Max. Glätte** | Controller-Spiele mit 30–60 FPS | Wie Ausgewogen, aber mit längerem Präzisionsfenster. Die flachste Kurve, für etwas mehr CPU-Last. |

<br>

## Overlay im Spiel

<p align="center">
  <img src="docs/images/overlay.png" alt="Die drei Overlay-Varianten Minimal, Kompakt und Detail über einer Spielszene, unten ein Hinweis zum geladenen Profil" width="100%">
</p>

Kurze Hinweise blenden sich ein, wenn ein Profil geladen wird, wenn du Ziel oder Modus änderst und während einer Aufnahme. Das Overlay funktioniert im Fenster- und im randlosen Vollbildmodus.

<br>

## Immer griffbereit im Tray

<table>
<tr>
<td width="45%" valign="middle">
  <img src="docs/images/tray.png" alt="Tray-Menü mit Limiter, Ziel-FPS-Untermenü, Modus, Overlay, Aufnahme und Beenden" width="100%">
</td>
<td width="55%" valign="middle">

Beim Minimieren verschwindet Cadence in den Infobereich der Taskleiste. Ein Rechtsklick genügt, um:

- den Limiter ein- oder auszuschalten
- Ziel-FPS und Modus zu wechseln
- das Overlay ein- oder auszublenden
- eine Aufnahme zu starten
- Cadence zu beenden

Beim Beenden laufen alle Spiele sofort wieder unbegrenzt. Selbst wenn Cadence abstürzt, gibt das Spiel das Limit nach spätestens zwei Sekunden frei.

</td>
</tr>
</table>

<br>

## Die perfekte Einrichtung

1. **Ein erreichbares Ziel wählen.** Lieber stabile 60 als schwankende 70 bis 90. Schalte andere FPS-Limits im Spiel und im Treiber ab, damit sich nichts in die Quere kommt.
2. **Zum Monitor passend synchronisieren.** Mit G-Sync oder FreeSync (VRR) genügt das Limit. Bei einem Monitor mit fester Bildwiederholrate V-Sync im Spiel einschalten, damit es nicht zu Tearing kommt.
3. **Bewegungsunschärfe einschalten.** Sie füllt die Lücken zwischen den Bildern wie die Belichtung einer Kamera und glättet schnelle Kameraschwenks.
4. **Zum Controller greifen.** Analogsticks bewegen die Kamera gleichmässig. Mit der Maus fallen niedrige Bildraten viel stärker auf.

> [!TIP]
> Auf 120-Hz-Bildschirmen sind **40 FPS** ein Geheimtipp: Jedes Bild bleibt exakt drei Bildwechsel lang stehen. Das fühlt sich deutlich runder an als 45 oder 50 FPS.

<br>

## So legst du los

> [!NOTE]
> Cadence befindet sich im **Early Access**. Fertige Downloads folgen unter [Releases](../../releases). Bis dahin baust du die App in wenigen Schritten selbst, siehe **[Bauen und testen](docs/BUILD.md)**.

1. Cadence starten.
2. Ein Spiel starten. Läuft es im Vollbild, schlägt Cadence vor, es zu begrenzen. Alternativ unter **Profile → Profil hinzufügen**.
3. Ziel-FPS wählen. Fertig.

**Systemanforderungen:** Windows 10 22H2 oder Windows 11 (64-Bit) · 64-Bit-Spiele mit DirectX 10, 11, 12 oder OpenGL · empfohlen: Monitor mit G-Sync oder FreeSync

<br>

## Tastenkürzel

| Kürzel | Wirkung |
|---|---|
| <kbd>Strg</kbd> + <kbd>Alt</kbd> + <kbd>↑</kbd> / <kbd>↓</kbd> | Ziel-FPS eine Stufe höher / tiefer |
| <kbd>Strg</kbd> + <kbd>Alt</kbd> + <kbd>R</kbd> | Aufnahme starten / stoppen |
| <kbd>Strg</kbd> + <kbd>Alt</kbd> + <kbd>O</kbd> | Overlay ein- / ausblenden |

<br>

> [!WARNING]
> **Nicht in Online-Spielen mit Anti-Cheat verwenden** (z. B. Easy Anti-Cheat, BattlEye, Vanguard, VAC). Cadence klinkt sich in das Spiel ein, und solche Systeme können das als Manipulation werten. Cadence erkennt viele dieser Spiele und hält sich dann heraus. Die Erkennung ist aber keine Garantie. Die Nutzung erfolgt auf eigenes Risiko.

<br>

## Häufige Fragen

<details>
<summary><b>Warum nicht einfach das Limit im Spiel nutzen?</b></summary>
<br>

Viele Limiter in Spielen schlafen mit einer Genauigkeit von etwa einer Millisekunde. Das reicht für den Durchschnitt, aber nicht für einen gleichmässigen Takt. Manche Spiele bieten auch nur 30, 60 oder „unbegrenzt“ an. Cadence erlaubt jeden Wert und hält ihn genauer ein.

Ehrlich gesagt: Ein sehr gut gebauter Limiter im Spiel, vor allem zusammen mit NVIDIA Reflex oder AMD Anti-Lag, kann beim Input-Lag besser sein. Für schnelle Shooter mit Maus lohnt sich ein Vergleich. Mit der Statistik-Seite von Cadence siehst du den Unterschied direkt.
</details>

<details>
<summary><b>Und der Limiter im Grafiktreiber?</b></summary>
<br>

Der funktioniert überall, ist aber oft nicht so präzise und verlangt bei manchen Treibern einen Neustart des Spiels, damit Änderungen greifen. Cadence ändert das Ziel live, speichert Werte pro Spiel und zeigt dir, wie gleichmässig es tatsächlich läuft.
</details>

<details>
<summary><b>Kostet Cadence Leistung?</b></summary>
<br>

Kaum. Die Hook-DLL ist wenige hundert Kilobyte gross und arbeitet nur beim Wechsel zum nächsten Bild. Im Modus „Max. Glätte“ ist die CPU-Last etwas höher, weil Cadence länger aktiv auf den exakten Zeitpunkt wartet.
</details>

<details>
<summary><b>Warum sehe ich das Overlay nicht?</b></summary>
<br>

Im exklusiven Vollbild lässt Windows keine anderen Fenster über dem Spiel zu. Stelle das Spiel auf „Randlos“ bzw. „Borderless“. Mit VRR ist das ohnehin meist die bessere Wahl.
</details>

<details>
<summary><b>Welche Spiele werden unterstützt?</b></summary>
<br>

64-Bit-Spiele mit DirectX 10, 11, 12 oder OpenGL. Vulkan und 32-Bit-Spiele sind in Arbeit.
</details>

<br>

## Was als Nächstes kommt

- [x] Präziser Limiter mit drei Modi
- [x] Profile pro Spiel und automatische Erkennung
- [x] Overlay, Tray, Statistiken mit Export
- [x] Ziel-FPS passend zum Monitor
- [ ] Vulkan-Unterstützung
- [ ] 32-Bit-Spiele
- [ ] Automatisch knapp unter der Bildwiederholrate bleiben, wenn VRR aktiv ist
- [ ] Fertige Installationspakete unter Releases

Ideen oder Fehler gefunden? Eröffne ein [Issue](../../issues). Bitte gib Spiel, Grafik-API und den Status aus der Übersicht an.

<br>

<p align="center">
  <img src="docs/images/logo.png" width="40" alt=""><br>
  <sub>Cadence · gebaut für alle, die ein ruhiges Bild mehr schätzen als eine hohe Zahl.</sub>
</p>
