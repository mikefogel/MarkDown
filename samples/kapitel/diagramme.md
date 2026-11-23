# Diagramme

Zurück zum [Index](../index.md) oder zu den [Grundlagen](grundlagen.md).

## Mermaid

```mermaid
flowchart LR
    A[Markdown-Datei] --> B{Link?}
    B -- Markdown --> C[Im Viewer öffnen]
    B -- Sonstiges --> D[Mit System-Programm öffnen]
    C --> E[Zurück-Button]
    E --> A
```

```mermaid
sequenceDiagram
    Nutzer->>Viewer: Datei ablegen
    Viewer->>Server: Seite rendern
    Server-->>Viewer: HTML
    Viewer-->>Nutzer: Anzeige
```

## SVG

![Logo als SVG](../images/logo.svg)

## Formeln

Inline: $E = mc^2$ und als Block:

$$
\int_0^\infty e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2}
$$
