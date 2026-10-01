# P1 – Graph, Metadaten, Assets (Detail-Spec)

Status: Arbeitsstand, abgeleitet aus Master-Spec Abschnitt 5.1–5.4 und `docs/analysis/sharedien-backend.md` Kapitel 4.

## 1. Ziel und Definition of Done

Ordnerbaum, Assets hochladen, anzeigen, versionieren, herunterladen, Metadaten pflegen, Rechte je Ordner/Asset vergeben und entziehen, Papierkorb, Duplikaterkennung, alles realtime und auditiert.

## 2. Schnitt

| Teil | Inhalt | Ort |
|---|---|---|
| P1a Graph und Knotenrechte | NodeType, Node (Folder, Asset, Custom), Edge/EdgeType, Baum-API, Verschieben, Verlinken, Papierkorb, Rechte-Vererbung über Pfad, Realtime-Topics, Abos | `Coworkee.Identity` (Hierarchie-Hook), `Shareme.Graph` |
| P1b Metadaten | PropertyDefinition, -Group, -ListValue, -Set, DataLanguage, UserContext, Werte als jsonb, Vererbung Ordner→Asset, Editor- und Admin-UI | `Shareme.Metadata` |
| P1c Storage und Assets | `Coworkee.Storage` (FileSystem, S3/MinIO, Azure Blob, HMAC-signierte URLs), Upload-Protokoll mit Chunks, AssetType/FileType, AssetVersion, Duplikate, Download | `Coworkee.Storage`, `Shareme.Assets` |
| P1d Verarbeitung und Anzeige | `Shareme.Processing` (Hangfire-Queue `processing`, Magick.NET: Thumbnail, Preview), Derivat-Status realtime, Asset-Detail mit MudExFileDisplay, Upload-UI | `Shareme.Processing`, Client |

Jeder Teil hat einen eigenen Plan, eigenes Review, Merge und Push.

## 3. Graph (P1a)

### 3.1 Modell

- `NodeType { Id, Key, Semantic (Folder, Asset, Custom), Name (LocalizedText), Icon, IsSystem, TenantId? }`. System-Typen `folder`, `asset` je Installation (TenantId null), Custom-Typen je Tenant.
- `Node` (eine Tabelle `sm.Nodes`, TPH über `Semantic`): `Id, TenantId, NodeTypeId, ParentId?, Path, Depth, Name (LocalizedText), Slug, SortIndex, InterruptInheritance, OwnerId, Metadata (jsonb, P1b), IsDeleted/DeletedAt/DeletedBy, Revision (IVersioned), xmin`.
  - `Path` = `/{rootId}/{…}/{id}/` (Guid-Strings), Index mit `text_pattern_ops` für Teilbaum-Präfix-Abfragen. `Depth` = Anzahl Segmente minus 1.
  - `Folder : Node { Color?, Icon? }`, `Asset : Node { AssetTypeId?, CurrentVersionId?, Status (Importing, Ready, Duplicate) }` (Felder kommen in P1c), `CustomNode : Node`.
- Wurzel je Tenant: ein `Folder` mit `ParentId = null`, `Slug = "root"`, angelegt beim ersten Zugriff (Seed). Papierkorb ist eine Sicht, kein Ordner.
- `EdgeType { Id, Key, Semantic (Link, Variant, Translation, Duplicate, Custom), IsDirected, IsSystem, Name, AllowedPairs[(FromTypeKey, ToTypeKey)] }`, `Edge { Id, TenantId, EdgeTypeId, FromNodeId, ToNodeId, CreatedAt/By }` mit Unique `(EdgeTypeId, FromNodeId, ToNodeId)`. Hierarchie ist keine Edge.
- `LocalizedText` = Wertobjekt `{ "de": "...", "en": "..." }` als jsonb; Anzeige: User-Sprache → Default-Sprache → erster Wert.

### 3.2 Operationen

- Anlegen (Ordner, Custom-Knoten) unter einem Elternknoten; Assets entstehen in P1c über Upload.
- Umbenennen, Sortieren (`SortIndex`), Verschieben (Teilbaum-Pfade in einem `UPDATE … SET Path = @new || substring(Path, …)` für alle Nachkommen; Zyklen verhindern), Verlinken (Edge `link` Folder→Asset; ein Asset hat genau einen Elternordner, weitere Ordner über Link).
- Löschen = Soft-Delete des Teilbaums (alle Nachkommen `IsDeleted`), Papierkorb listet gelöschte Teilbaum-Wurzeln, Wiederherstellen (Elternknoten muss existieren, sonst in die Wurzel), Endgültig löschen (Hard-Delete Teilbaum; Blobs in P1c per Job).
- Kinderliste mit Paging, Sortierung (Name, Datum, SortIndex), Filter nach Typ; Breadcrumb aus `Path`.

### 3.3 Rechte

- Ressourcentyp `Node` in Coworkee `ResourcePermission`. Neuer Hook in Coworkee.Identity: `IResourceHierarchy { string ResourceType; Task<IReadOnlyList<Guid>> GetInheritanceChainAsync(Guid id) }` liefert die Knoten-ID und ihre Vorfahren bis einschließlich des nächsten Knotens mit `InterruptInheritance`. `PermissionChecker.IsGrantedAsync(permission, type, id)` prüft globale Rechte, dann Rollen an jeder ID der Kette.
- Permissions (Gruppe `Nodes`): `Nodes.Read`, `Nodes.Create` (Kinder anlegen), `Nodes.Update`, `Nodes.Delete`, `Nodes.Move`, `Nodes.ManagePermissions`, `Assets.Download` (P1c), `Nodes.Admin` (Typen verwalten, Papierkorb endgültig leeren). Implikationen: Update ⇒ Read, Create ⇒ Read, Delete ⇒ Read, ManagePermissions ⇒ Read.
- Global vergebene Rechte gelten für alle Knoten des Tenants. Ressourcenrechte über Rollen am Knoten (ResourcePermissionsPanel, `ResourceType = Node`).
- Lesen von Kindern: Kinder erben vom Elternknoten, außer `InterruptInheritance`; solche Kinder erscheinen nur mit eigenem Recht.
- Einstiegspunkte: Wer die Wurzel nicht lesen darf, bekommt die obersten Knoten, an denen er ein eigenes Recht hat (`GET /api/v1/nodes/entry-points`).

### 3.4 Realtime und Abos

- `[Realtime(Nodes.Read)]` auf `Node`; zusätzliches Topic `children:{parentId}`. Topic-Autorisierung für `entity:Node:{id}` und `children:{id}` über das Ressourcenrecht `Nodes.Read` am Knoten (neuer Authorizer, statt nur globaler Permission).
- `NodeSubscription { NodeId, UserId, Inbox, Email }`; Änderungen im Teilbaum erzeugen über einen Domain-Event-Handler eine Notification für Abonnenten (Inbox in P1, Mail-Digest in P4).
- Audit über den vorhandenen AuditTrail; Activity-Tab = `AuditTimeline(Node, id)`.

## 4. Metadaten (P1b)

- `PropertyGroup { Key, Name, SortIndex }`, `PropertyDefinition { Key, GroupId, Name, Description, ValueType (String, Text, RichText, Date, DateTime, Time, Integer, Decimal, Boolean, Duration, Url, Reference), ListMode (None, Single, Multi), AllowCustomValues, MultiValue, Localizable, HasContext, Editable, IsSystem, Searchable (None, Filter, Fulltext, Both), Facet, IgnoreOnNewVersion, InheritToChildren, NodeTypeKeys[], Validation { Required, Regex, Min, Max } }`, `PropertyListValue { DefinitionId, Key, Name, SortIndex, IsActive }`, `PropertySet { NodeTypeKey, Name, IsDefault, Items[{DefinitionKey, FillMode}], Conditions[{DefinitionKey, ListValueKey}] }`, `DataLanguage { Code, Name, IsDefault }`, `UserContext { Key, Name, IsDefault }`.
- Werte: `Node.Metadata` jsonb `{ "<key>": [ { "lang": "de", "ctx": "eu", "v": <json>, "list": "<listKey>" } ] }`. Validierung gegen Definition (Typ, Liste, Mehrfach, Sprache, Kontext, Regex, Min/Max). Änderung über `PUT /api/v1/nodes/{id}/metadata` (ganzer Satz) und `PATCH` (einzelne Keys).
- Effektive Metadaten = eigene Werte + vererbte Werte der Vorfahren für Definitionen mit `InheritToChildren`, wenn der Knoten selbst keinen Wert hat. Antwort markiert geerbte Werte mit Herkunft.
- System-Properties werden gespiegelt (Dateiname, Größe, Hash, Version, Import-Datum, Importiert von) und sind nicht editierbar.
- Feld-Restriktionen je Rolle: P5.

## 5. Storage und Assets (P1c)

- `Coworkee.Storage`: `IBlobStorage { PutAsync, OpenReadAsync, DeleteAsync, ExistsAsync, GetSignedReadUrl? }`, Provider `FileSystem` (Default, Pfad aus Option), `S3` (MinIO kompatibel), `AzureBlob`; Auswahl per Option `Coworkee:Storage:Provider`. Blob-Keys `{tenant}/{yyyy}/{mm}/{guid}`. Signierte URLs per HMAC-SHA256 mit Ablauf.
- `AssetType { Key, Name, DownloadOriginalAlways }` Seeds (image, video, audio, document, archive, other), `FileType { Extension, MimeType, AssetTypeKey, ForbiddenForUpload, MagicNumbers? }` Seeds für gängige Endungen; verbotene Endungen (exe, bat, …) und Magic-Number-Prüfung.
- `AssetVersion { AssetId, Number, FileName, Size, Sha256, MimeType, FileTypeId, OriginalBlobKey, Status, ProcessingStatus, MetadataSnapshot, CreatedAt/By }`.
- Upload: `POST /api/v1/uploads { targetFolderId | assetId, fileName, size, sha256? }` → Session; `PUT /api/v1/uploads/{id}/chunks/{n}` (≤ 8 MB); `POST /api/v1/uploads/{id}/complete` → Datei zusammensetzen, Hash prüfen, Typ bestimmen, Asset bzw. neue Version anlegen, Metadaten der Vorversion kopieren (außer `IgnoreOnNewVersion`), Duplikat (gleicher Hash im Tenant) melden und Edge `duplicate` setzen, Default-Derivate einplanen. Abgebrochene Sessions räumt ein Job auf.
- Download: `GET /api/v1/assets/{id}/download?version=&derivation=original` (Content-Disposition), Recht `Assets.Download`.

## 6. Verarbeitung und Anzeige (P1d)

- `DerivationType { Key (original, thumbnail, preview), Name }`, `Derivation { VersionId, DerivationTypeKey, BlobKey, Size, MimeType, Status (Pending, Processing, Ready, Failed, NotSupported), Error }`.
- `Shareme.Processing`: eigener Host, Hangfire-Server nur für Queue `processing`, Job `RenderDerivations(versionId)`; Bilder mit Magick.NET: Thumbnail 320 px WebP, Preview 1600 px WebP; andere Typen `NotSupported` (P3). Status per Realtime `entity:Node:{assetId}`.
- Client: Ordnerbaum (lazy), Browser mit Galerie und Liste, Mehrfachauswahl, Drag&Drop-Upload mit Queue und Chunk-Retry, Asset-Detail mit Vorschau (MudExFileDisplay), Metadaten, Versionen, Relationen, Rechte, Aktivität; Papierkorb-Seite; Admin-Seiten für Typen und Metadaten-Definitionen.

## 7. Nicht in P1

Suche (P2), Video/Office/PDF/3D-Renditions, Wasserzeichen, Crop, Dateinamensregeln, Zip-Download (P3), Workspaces/Kommentare/Shares (P4), Rules, Mass-Edit, Import/Export, Feld-Restriktionen (P5), DRM (P6).
