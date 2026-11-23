# MarkDown Viewer – Spezifikation

Status: v1.0 implementiert (Release wird nach Freigabe per Tag `v*` erstellt).

## Ziel
Ein schneller, zuverlässiger Desktop-Viewer für Markdown-Dokumentationen, die aus mehreren verlinkten `.md`-Dateien bestehen (z. B. `index.md` → Kapitel → Unterkapitel). Nur lesen, nicht bearbeiten. Rein private Nutzung.

## Technik
- .NET 10 SDK (Avalonia 12 benötigt einen neueren Compiler), C#, Avalonia UI 12
- Rendering: Markdig → HTML → Avalonia `NativeWebView` (WebKitGTK unter Linux, WebView2 unter Windows)
- Mermaid, KaTeX und highlight.js werden als lokale Assets in die Anwendung eingebettet (offline, kein CDN)
- Lokaler HTTP-Server (Kestrel, nur `127.0.0.1`, zufälliger Port) liefert Seiten, Assets und Dateien aus. Dadurch lösen sich relative Bilder/Links wie im Browser auf.
- Aufteilung: `MarkDownViewer.Core` (Engine, Server, Baum, Verlauf, Suche – ohne UI, getestet) und `MarkDownViewer` (Avalonia-Oberfläche)
- Plattformen: Windows x64, Linux x64 (Linux benötigt WebKitGTK 4.1 + GTK 3)
- Repo: `mikefogel/MarkDown` (public, ohne Lizenzdatei – bewusst), CI/CD mit GitHub Actions
- **Gebaut wird ausschließlich in der Pipeline**, nie lokal.

## Funktionen

### 1. Dateien öffnen
- **Primär per Drag & Drop** auf das Fenster, kein „Datei öffnen“-Menü
- Wird eine Datei auf die Dokumentansicht gezogen, fängt der Viewer die `file://`-Navigation ab und öffnet sie im Viewer
- Zusätzlich Kommandozeilenargument und „Öffnen mit“ des Systems
- Startseite mit Liste der zuletzt geöffneten Dateien

### 2. Darstellung
- CommonMark + GFM: Tabellen, Task-Listen, Fußnoten, Autolinks, Durchgestrichen, Emoji, Definitionslisten u. a.
- Bilder (lokal relativ/absolut, `http(s)`), SVG, eingebettetes HTML
- Mermaid-Diagramme aus ` ```mermaid `-Blöcken (Hell/Dunkel)
- Mathe (KaTeX), Code mit Syntax-Highlighting (Hell/Dunkel)
- Relative Pfade beziehen sich auf die Datei, in der sie stehen
- Überschriften-Anker im GitHub-Stil (`#mein-abschnitt`)

### 3. Navigation (Kernfeature)
- Klick auf Link zu einer `.md`-Datei öffnet sie im Viewer, auch mit `#anker`
- **Zurück/Vor** wie im Browser: Verlaufs-Stack, Scroll-Position je Eintrag wird wiederhergestellt
- Anker-Links innerhalb einer Datei scrollen
- Links auf Nicht-Markdown-Ziele (PDF, HTML, Bilder, sonstige Dateien) und externe URLs öffnen mit dem Standardprogramm des Systems
- Fehlende Zieldateien werden als „defekter Link“ (rot, durchgestrichen) markiert
- Tastatur: `Alt+←` / `Alt+→`, Maus-Zurück/Vor-Tasten

### 4. Seitenleiste
- Tab „Gliederung“: Überschriften der aktuellen Datei, Klick springt dorthin
- Tab „Baum“: Dokumentenbaum **immer ab der aktuell angezeigten Datei** (Links nach unten); die Wurzel ist nicht fest und kann mitten im Dokumentenbaum liegen. Breitensuche: jedes Dokument erscheint einmal unter dem flachsten Dokument, das es verlinkt; Zyklen und Rück-Links erzeugen keine Dubletten
- Tab „Suche“: Ergebnisliste der Baumsuche

### 5. Suche
- In der aktuellen Datei (`Strg+F`, `Enter`/`F3` weiter, `Shift` rückwärts)
- Optional über den gesamten Baum (Checkbox „Baum“); Klick auf Treffer öffnet die Datei und springt zur Stelle

### 6. Drucken / PDF
- Button „Drucken“ (`Strg+P`) öffnet Dialog mit Auswahl:
  - **Nur aktuelles Dokument**: Links gelb hervorgehoben; auf Papier steht das Linkziel in Klammern dahinter
  - **Gesamter Baum**: Deckblatt mit Inhaltsverzeichnis, dann alle per Link nach unten erreichbaren Dokumente in Lesereihenfolge, jedes einmal, je Dokument neue Seite; interne Links zeigen auf den jeweiligen Abschnitt
- Danach öffnet sich der Druckdialog des Systems (dort auch „In Datei drucken“/PDF)
- Die Druckansicht ist ein Verlaufseintrag; „Zurück“ führt zum Dokument
- Print-CSS: Seitenumbrüche vor Kapiteln, Diagramme/Bilder/Tabellen werden nicht zerschnitten

### 7. Komfort
- Hell/Dunkel-Theme (merkt sich die Wahl)
- Live-Reload bei externer Änderung der angezeigten Datei (auch atomares Speichern), Scroll-Position bleibt erhalten
- Zoom (`Strg +/−/0`, Buttons)

## Sicherheit
- Server bindet nur an `127.0.0.1`; Zugriff nur mit zufälligem Token im Pfad; Host-Header wird geprüft
- Seiten haben eine strikte Content-Security-Policy: Skripte nur per Nonce (nur eigene), `connect-src 'none'`, keine Frames/Objekte/Formulare. Skripte, Event-Handler oder `fetch` aus Markdown-HTML werden dadurch blockiert – eine fremde `.md` kann keine lokalen Dateien lesen oder versenden
- SVG/HTML-Dateien werden mit `sandbox`-CSP ausgeliefert; ausschließlich lesender Zugriff
- Fremde Server-URLs werden nie an den Browser weitergereicht

## Nicht-Ziele
- Lizenz, Editieren, macOS, Cloud-Sync, Plugins, Code-Signierung der Programme (Windows SmartScreen warnt beim ersten Start)

## Qualität
- 20 automatische Tests (Link-Auflösung, URL-Abbildung, Verlauf, Baum/Zyklen, Suche, Rendering, Baum-Druck, Server-Sicherheit)
- Die Debug-Variante der App besitzt einen Testkanal (`MDV_CMD_FILE`) für automatisierte UI-Tests; er ist im Release-Build nicht enthalten

## Pipeline und Release
- **CI** bei Push und PR: Build + Tests auf Ubuntu und Windows
- **Release** bei Tag `v*`: Tests, `dotnet publish` self-contained je Plattform, Pakete
  - `markdown-viewer-win-x64.zip`
  - `markdown-viewer-linux-x64.tar.gz`
  - `SHA256SUMS.txt`, automatisch generierte Release-Notes
- Manueller Lauf (`workflow_dispatch`) baut nur die Artefakte, ohne Release
- Das erste Release (`v1.0.0`) wird erst auf Freigabe erstellt
