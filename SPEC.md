# MarkDown Viewer – Spezifikation (v0.1, Entwurf)

## Ziel
Ein schneller, zuverlässiger Desktop-Viewer für Markdown-Dokumentationen, die aus mehreren verlinkten `.md`-Dateien bestehen (z. B. `index.md` → Kapitel → Unterkapitel). Nur lesen, nicht bearbeiten.

## Technik
- .NET 10 SDK (Avalonia 12 benötigt neuen Compiler), Ziel net10.0, C#, Avalonia UI (MVVM)
- Rendering: Markdig → HTML → Avalonia `NativeWebView`
- Mermaid, KaTeX und Syntax-Highlighting werden als lokale Assets mitgeliefert (offline-fähig, kein CDN)
- Plattformen: Windows x64, Linux x64 (Linux benötigt WebKitGTK)
- Repo: `mikefogel/MarkDown` (public), CI/CD mit GitHub Actions

## Funktionen

### 1. Dateien öffnen
- **Primär per Drag & Drop**: Datei einfach auf das Fenster ziehen (auch auf ein bereits geöffnetes Dokument), kein „Datei öffnen“-Menü nötig
- Zusätzlich Kommandozeilenargument und „Öffnen mit“ des Systems
- Zuletzt geöffnete Dateien

### 2. Darstellung
- CommonMark + GFM: Tabellen, Task-Listen, Fußnoten, Autolinks, Durchgestrichen
- Bilder (lokal relativ, absolut, `http(s)`), SVG (als Bild und inline)
- Mermaid-Diagramme aus ` ```mermaid `-Blöcken
- Code-Blöcke mit Syntax-Highlighting, Mathe (KaTeX), eingebettetes HTML
- Relative Pfade (Bilder und Links) beziehen sich auf die Position der jeweiligen Datei

### 3. Navigation (Kernfeature)
- Klick auf Link zu einer `.md`-Datei öffnet sie im Viewer, auch mit `#anker`
- **Zurück/Vor** wie im Browser: Verlauf als Stack, Scroll-Position wird je Eintrag wiederhergestellt
- Anker-Links innerhalb einer Datei scrollen, Externe Links öffnen im Standardbrowser
- Links auf Nicht-Markdown-Ziele (PDF, HTML, Bilder, andere Dateien) und externe URLs werden mit dem Standardprogramm des Systems geöffnet
- Fehlende Zieldateien werden sichtbar als „defekter Link“ markiert
- Tastatur: `Alt+←` / `Alt+→`, Maus-Zurück/Vor-Tasten

### 4. Sidebar
- Tab „Gliederung“: Überschriften der aktuellen Datei
- Tab „Baum“: Dokumentenbaum **immer ab der aktuell angezeigten Datei** (Links nach unten); die Wurzel ist also nicht fest und kann mitten im Dokumentenbaum liegen. Zyklen werden erkannt und nur einmal aufgelöst

### 5. Suche
- In der aktuellen Datei (`Ctrl+F`) mit Treffer-Markierung
- Optional über den gesamten Baum, Ergebnisliste springt zur Datei und Stelle

### 6. Drucken / PDF
- Button „Drucken“ öffnet Dialog mit Auswahl:
  - **Nur aktuelle Datei**: Links werden hervorgehoben, bei Papier zusätzlich mit sichtbarem Ziel
  - **Gesamter Baum**: Alle per Link erreichbaren Dateien nach unten, in Reihenfolge des ersten Auftretens, jede Datei nur einmal, mit Kapiteltrennung
- Ausgabe: System-Druckdialog oder PDF-Export
- Print-CSS: Seitenumbrüche vor Kapiteln, Diagramme und Bilder nicht zerschneiden

### 7. Komfort
- Hell/Dunkel-Theme (folgt System, umschaltbar)
- Live-Reload bei externer Änderung der angezeigten Datei, Scroll-Position bleibt erhalten
- Zoom (`Ctrl +/-/0`)

## Nicht-Ziele (v1)
- Lizenz (rein private Nutzung, bewusst keine LICENSE-Datei), Editieren, macOS, Cloud-Sync, Plugins

## Qualitätsanforderungen
- Start unter 1 s, große Dateien (> 1 MB) flüssig scrollbar
- Kein Netzwerkzugriff nötig, außer für externe Bilder
- Sicherheit: Skripte aus Markdown-HTML werden deaktiviert, lokale Dateizugriffe nur lesend

## Pipeline und Release
- CI bei Push und PR: Restore, Build, Tests (Linux + Windows)
- Release bei Tag `v*`: Publish self-contained, single-file
  - `markdown-viewer-win-x64.zip`
  - `markdown-viewer-linux-x64.tar.gz`
  - Veröffentlichung als GitHub Release mit Changelog und Checksummen
- Release wird erst erstellt, wenn wir gemeinsam sagen „fertig“

## Meilensteine
1. Projektgerüst, CI, leeres Avalonia-Fenster
2. Markdig + WebView, Basis-Rendering, Bilder, SVG
3. Mermaid, KaTeX, Highlighting
4. Linknavigation mit Zurück/Vor
5. Sidebar (Gliederung, Baum)
6. Suche, Theme, Live-Reload
7. Druck/PDF mit Baumoption
8. Release-Pipeline, v1.0.0
