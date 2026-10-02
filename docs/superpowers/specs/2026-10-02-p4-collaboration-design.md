# P4 Kollaboration – Design

Ziel (Architektur-Spec §7): Kanban-Workspace mit Freigabe an Externe; Sharedien-Parität für Workspaces (Upload, Collection, Collaboration, Templates), States/Transitions, Tasks, Kommentare, Watcher, Shares mit Public-Seite, Finalisierung, Benachrichtigungs-Center, Digest-Mails.

## 1. Modell

- **Workspace** ist ein Graph-Knoten mit eigener Semantik `workspace` (Tabelle der Nodes, TPH) unterhalb eines Mandanten-Knotens „Workspaces“ (neben der Ordner-Wurzel), damit Rechte, Realtime, Audit, Papierkorb, Metadaten und Suche des Graphen gelten. Felder: `Kind` (`upload`, `collection`, `collaboration`), `IsTemplate`, `TemplateId`, `StateId` (Spalte im globalen Workspace-Board), `FinalizationFolderId`, `CopyMetadataToAssets`, `RequiredSetId` (Pflicht-Property-Set vor Finalisierung), `FinalizedAt`.
- **Rollen** je Workspace über Resource-Permissions: `Workspace owner` (alles inkl. Rechte, Shares, Finalisieren) und `Workspace collaborator` (lesen, hochladen, Tasks, Kommentare) als System-Rollen; der Ersteller wird Owner. Templates tragen „Template-Grants“, die beim Anlegen aus dem Template auf den neuen Workspace kopiert werden.
- **Inhalt**: Upload-Workspaces besitzen ihre Assets als Kinder (Upload wie in P1c mit Ziel Workspace); Collections verweisen per Edge `link` auf Assets anderswo; Collaboration kann beides.
- **Finalisieren** (Recht `Workspaces.Finalize` oder Owner): prüft Pflicht-Set, verschiebt die Kind-Assets in `FinalizationFolderId` (Create-Recht dort), kopiert auf Wunsch Workspace-Metadaten auf die Assets, setzt `FinalizedAt`, schreibt Audit.

## 2. Teilphasen

| Phase | Inhalt |
|---|---|
| **P4a Workspaces** | Semantik `workspace`, Wurzel „Workspaces“, CRUD, Arten, Templates (Anlegen aus Template kopiert Metadaten, States, Template-Grants), Owner/Collaborator-Rollen, Upload in Workspace, Collection-Links, Finalisieren; UI: Workspace-Liste (Karten), Detail mit Tabs (Assets, Mitglieder, Metadaten, Aktivität), Template-Admin |
| **P4b States & Tasks** | States je Workspace (Reihenfolge, Default, erlaubte Übergänge, wer darf hineinschieben = Resource-Permission je State), Tasks (Titel, Beschreibung, Fälligkeit, Zuständige, State, Reihenfolge, verknüpfte Assets) als Knoten unter dem Workspace; Move-Endpunkt mit Transition-Prüfung und optimistischer Concurrency; Kanban-Board (MudDropContainer) mit Realtime; globales Workspace-Board über Workspace-States |
| **P4c Kommentare, Watcher, Benachrichtigungen** | Kommentare an Workspace/Task (Markdown, @Erwähnungen), Watcher (bestehende Node-Abos) mit Inbox/Mail-Schaltern, Trigger (Upload in Workspace, Mitglied hinzugefügt, Task-Transition, Zuweisung, Kommentar, Erwähnung, Share erstellt), Benachrichtigungs-Center (Coworkee.Notifications) und tägliche Digest-Mail je User (Job, Mail-Template) |
| **P4d Shares** | Share eines Workspace (Name, Ablauf, erlaubte Renditions je Asset-Typ, Original ja/nein, Dateinamensregel), Empfänger mit eigenem Token per Mail, Widerruf; anonyme Public-API (Token im Pfad, Rate-Limit, nur freigegebene Dateien, Ablauf geprüft) und Public-Seite im Web-Host (Galerie mit MudExFileDisplay, Einzel- und Zip-Download über P3d-Pakete) |

## 3. Grundsätze

- Alles läuft über Graph-Rechte: ein Workspace ist für Mitglieder sichtbar, nicht über die Ordnerrechte; die Wurzel „Workspaces“ vergibt kein Leserecht an alle.
- Externe sehen ausschließlich, was ein Share freigibt; der Token ist zufällig (122 Bit), wird gehasht gespeichert und läuft ab.
- Benachrichtigungen werden über das Outbox-Event-System ausgelöst, Mails nur über Coworkee.Mailing-Templates.

## 4. Nicht in P4

Rules-Engine und Mass-Edit (P5), Lizenzen (P6), Wiki/Intranet (P7).
