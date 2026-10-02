# P4a Workspaces – Plan

Spec: `docs/superpowers/specs/2026-10-02-p4-collaboration-design.md` Abschnitt 1 und 2 (P4a).

- Graph: Semantik `workspace`, Knotentyp `Workspace` (TPH, Diskriminator `workspace`) mit Art, Template-Flag, Template-Herkunft, Finalisierungsordner, Metadaten-Übernahme, Pflicht-Set, Finalisierungszeit. Workspaces sind eigene Wurzeln (`ParentId` null); der eindeutige Root-Index gilt nur noch für Ordner, Root-Abfragen lesen nur Ordner, Einstiegspunkte nur den Ordnerbaum. Workspaces lassen sich nicht verschieben, aber löschen und wiederherstellen (ohne Umhängen unter die Ordner-Wurzel).
- Ordner und Workspaces sind gleichwertige Container für Uploads, Zip-Downloads, Varianten und Archiv-Entpacken.
- Rollen „Workspace owner“ (lesen, anlegen, ändern, löschen, Rechte, Download, Finalisieren) und „Workspace collaborator“ (lesen, anlegen, ändern, Download) je Mandant beim ersten Bedarf angelegt (normale Rollen, im Admin anpassbar); Mitglieder sind Resource-Grants dieser Rollen auf dem Workspace (eine Rolle je Mitglied, letzter Owner bleibt).
- Globale Rechte `Workspaces.Create`, `Workspaces.Templates`; `Workspaces.Finalize` je Workspace.
- Templates: Workspaces mit `IsTemplate`; Anlegen aus Template übernimmt Art, Ordner, Optionen, Metadaten und die Mitglieder des Templates; der Ersteller wird Owner.
- Collections verlinken lesbare Assets per Edge `link`; Mitglieder sehen nur verlinkte Assets, die sie selbst lesen dürfen.
- Finalisieren: Pflicht-Set (mandatory) muss gefüllt sein, Create-Recht im Zielordner, direkte Kind-Assets wandern in den Ordner, Workspace-Metadaten werden auf Wunsch übernommen (eigene Werte des Assets gewinnen).
- UI: `/workspaces` (Karten, Templates-Tab, Anlegen mit Template), `/workspaces/{id}` (Items mit Upload, Mitglieder mit Suche, Metadaten, Einstellungen mit Ordnerwahl, Aktivität, Finalisieren).
