# P5d Restriktionen und Jobs-Übersicht – Plan

Spec: `docs/superpowers/specs/2026-10-02-p5-automation-design.md` (P5d).

- Coworkee: `IPermissionChecker.GetRoleIdsAsync` (mandantenweite Rollen direkt und über Gruppen) und `IResourceRestriction`, das jeder Ressourcen-Prüfung (auch bei globalen Rechten) Zugriff wieder entziehen kann.
- `Restriction` je Mandant und Rolle: Property verbergen, Property nur lesen, Knoten mit Listenwert verbergen, Rendition sperren. Gilt für alle mit der Rolle zusätzlich zu ihren Rechten (Vereinigung über Rollen), auch in Jobs und Shares (Impersonation).
- Durchsetzung: Knoten mit verborgenem Listenwert über `NodeRestriction` in jeder Ressourcen-Prüfung (Detail, Metadaten, Dateien, Schreiben …) und als jsonb-Filter in Listen (Kinder, Einstiege, Suche, Workspaces, Pakete); verborgene Properties fehlen in Metadaten, Definitionen, Suchfeldern (Filter, Facetten, Sortierung) und Exporten und sind wie schreibgeschützte nicht änderbar (Replace behält sie); gesperrte Renditions fehlen im Asset und lassen sich weder einzeln noch als Paket holen.
- Verwaltung `/api/v1/restrictions` (Nodes.Admin), Seite `/admin/restrictions`.
- Jobs-Übersicht `/api/v1/jobs` und Seite `/jobs`: eigene Mass-Edits, Importe, KI-Läufe, Regel-Läufe, Archiv-Entpackungen (lesbare Archive), Reindex für Admins, Link aufs Hangfire-Dashboard mit `Jobs.View`.
