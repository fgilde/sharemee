# P4c Kommentare, Watcher, Benachrichtigungen – Plan

Spec: `docs/superpowers/specs/2026-10-02-p4-collaboration-design.md` Abschnitt 2 (P4c).

- Kommentare an jedem Knoten (Workspace, Task, Asset): Lesen mit Leserecht, Schreiben mit Update-Recht; Autoren bearbeiten ihre Kommentare, Owner (ManagePermissions) löschen jeden; Text bis 8000 Zeichen, als reiner Text angezeigt (kein Markup); Erwähnungen nur von Mitgliedern des Workspaces, neu Erwähnte werden auch beim Bearbeiten benachrichtigt; Realtime über `children:{node}`.
- Benachrichtigungen (Coworkee.Notifications): Erwähnung, neuer Kommentar für Watcher (außer Autor und Erwähnten), Zuweisung, Verschiebung für Zuständige (mit Spaltenname), Aufnahme in einen Workspace; Watcher-Meldungen der Graph-Änderungen verlinken auf Workspace, Board oder Asset. Ersteller beobachten ihren Workspace automatisch; Beobachten-Schalter im Workspace-Kopf.
- Coworkee: täglicher Digest (`NotificationDigestJob`, Cron `Coworkee:Notifications:DigestCron`, Standard 6 Uhr UTC) mit den ungelesenen Benachrichtigungen seit dem letzten Digest (höchstens ein Tag, bis 50 je Mail), Template `Notifications.Digest`, User-Einstellung `Notifications.Digest` (Standard an, global abschaltbar), absolute Links über `Coworkee:Notifications:PublicAppUrl`; Coworkee.Notifications hängt dafür an Coworkee.Mailing.
- UI: Kommentar-Panel im Workspace (Tab), im Task-Editor und im Asset-Detail; das Benachrichtigungs-Center ist die Glocke aus Coworkee.Client.Blazor.
