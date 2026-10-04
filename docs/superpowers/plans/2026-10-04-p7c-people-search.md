# P7c Personen und globale Suche – Plan

Spec: `docs/superpowers/specs/2026-10-04-p7-knowledge-intranet-design.md` (P7c).

- `UserProfile` je User (Abteilung, Position, Telefon, Ort, Über mich, Avatar als Blob mit erkanntem Bildtyp PNG/JPEG/WebP bis 2 MB); jeder pflegt nur das eigene Profil.
- Personenverzeichnis `/api/v1/people`: aktive Mitglieder des eigenen Mandanten (Coworkee-User mit TenantId), Suche über Name, E-Mail, Abteilung, Position, Ort; Avatar-Abruf nur für Mitglieder.
- Globale Suche `/api/v1/global-search?q=`: Knoten über die bestehende Suche (Rechte, Restriktionen, Embargo) plus Personen.
- UI: `/people` (Karten, eigenes Profil, Bild), `/find` (Inhalte und Personen), Startseiten-Suche führt nach `/find`.
