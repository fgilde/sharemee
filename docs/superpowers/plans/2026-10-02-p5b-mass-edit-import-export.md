# P5b Mass-Edit, Import, Export – Plan

Spec: `docs/superpowers/specs/2026-10-02-p5-automation-design.md` (P5b).

- `DataJob` (Mandant, Art massEdit/import, Probelauf, Startender, Zähler, bis 1000 Report-Einträge mit Knoten, Zeile, Spalte). Läufe sieht nur, wer sie gestartet hat. Ausführung als Hangfire-Job im Namen des Startenden, atomarer Claim, Fehler je Knoten stoppen nichts, Schreiben über `SetNodeMetadata` (neu: `ValidateOnly` für Probeläufe) in eigenem Scope.
- Mass-Edit `POST /api/v1/mass-edits`: Knoten oder alle Treffer einer Suche, Mutationen je Property (keep, overwrite, merge, delete) mit Werten oder Texten (wie Tabellenzellen, mit Sprache/Kontext). Recht `Automation.MassEdit` bis `Shareme:MassEdit:MaxNodes` (Standard 1000), `Automation.MassEditLarge` bis 10 000.
- Export `POST /api/v1/exports` (Knoten oder Ordner, bis 10 000, nur lesbare): XLSX (ClosedXML, nur Text-Zellen) oder CSV (Formel-Zeichen mit Apostroph geschützt); Spalten id, name, type, path und je Property/Sprache/Kontext `key[lang]@ctx`, Mehrfachwerte mit „; “.
- Import `PUT /api/v1/imports?folderId=&dryRun=` (Rohdaten bis 20 MB, XLSX mit Prüfung der entpackten Größe, CSV mit Komma oder Semikolon): Zuordnung über `id` oder `name` im Ordner, leere Zellen ändern nichts, Meldungen je Zeile und Spalte, unbekannte Spalten einmal gemeldet.
- UI: Browse (Auswahl: Metadaten bearbeiten, Export; Ordner: Export, Import/Probelauf), Suche (alle Treffer bearbeiten, geladene Treffer exportieren), Seite `/data-jobs` mit Report.
