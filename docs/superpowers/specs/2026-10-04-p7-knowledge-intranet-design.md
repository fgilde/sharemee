# P7 Wissensbasis und Intranet – Design

Ziel (Architektur-Spec A6, §5.5, §7): versionierte Seiten im Knotenbaum, News, Startseite mit Widgets, Personenverzeichnis, Favoriten, globale Suche über alles. Nicht in Sharedien vorhanden; minimaler, erweiterbarer Umfang auf dem Graph-Modell.

## 1. Teilphasen

| Phase | Inhalt |
|---|---|
| **P7a Seiten** | Knotentyp `Page` (Semantik Page) in Ordnern und Workspaces; `PageVersion` (Nummer, Titel, HTML-Inhalt, Kommentar, Autor, Zeit); neue Version bei jeder Änderung mit Prüfung gegen die zuletzt gelesene Version (409 bei Konflikt); Inhalt serverseitig mit HtmlSanitizer bereinigt (nur Formatierung, Links, Bilder aus Shareme und https, keine Skripte/Styles/Events); Vergleich zweier Versionen als Zeilen-Diff des Textes; Wiederherstellen einer alten Version als neue Version; Verweise auf Assets und Knoten als Links (`/assets/{id}`, `/browse/{id}`) im Inhalt; Volltext der aktuellen Version im Suchindex; UI: Seite lesen, bearbeiten mit MudExRichTextEditor, Versionsliste mit Diff. |
| **P7b News, Favoriten, Startseite** | Knotentyp `News` (Inhalt wie Seiten, ohne Versionen) mit Veröffentlichung ab/bis und angeheftet; News-Feed der lesbaren, veröffentlichten News (angeheftete zuerst); Favoriten je User auf Knoten; Startseite mit Widgets je User (Reihenfolge, an/aus) und Mandanten-Standard: News, Zuletzt geändert, Favoriten, Meine Aufgaben, Fälligkeiten, Suche. |
| **P7c Personen und globale Suche** | Profil je User (Abteilung, Position, Telefon, Ort, Über mich, Avatar); Personenverzeichnis der aktiven Mitglieder des Mandanten mit Suche; globale Suche über Knoten (Assets, Ordner, Seiten, News, Workspaces) und Personen in einem Ergebnis. |

## 2. Grundsätze

- Seiten und News sind Knoten: Rechte, Restriktionen, Papierkorb, Verschieben, Metadaten, Suche und Regeln gelten wie für andere Knoten.
- HTML wird nur bereinigt gespeichert und im Client per MudExFileDisplay/MarkupString dargestellt.
- Kommentare zu Seiten folgen, wenn Kommentare über Workspaces hinaus gelten (eigener Schritt).

## 3. Nicht in P7

Blog, Umfragen, Kalender, Echtzeit-Coediting.
