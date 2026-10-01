# Sharedien – Erkenntnisse aus der Produktivdatenbank (bacpac)

Quelle: `C:\dev\Sharedien3-2021-4-21-11-23.bacpac` (Azure SQL Export, 2021-04-21, App-Version ~3.1, 261 Migrationsskripte seit 2018-11).
Importiert nach LocalDB `(localdb)\MSSQLLocalDB` / DB `Sharedien3Analysis` (kann jederzeit gelöscht werden).
Vollständiges Schema (142 Tabellen, 17 Views, alle Spalten/FKs/Indizes): [sharedien-db-schema.md](sharedien-db-schema.md).

Die DB ist eine Entwicklungs-/Demo-Instanz (viele Testeinträge). Personenbezogene Daten wurden bewusst nicht übernommen. Hier stehen nur Struktur, Systemdaten und Verteilungen.

## 1. Schemata und Bounded Contexts

| Schema | Tabellen | Zweck |
|---|---|---|
| `MAM` | 85 | Media Asset Management Kern: Graph (Node/Edge), Assets, Versionen, Derivate, Properties, Workspaces, Tasks, Shares, Lizenzen, Jobs, ActivityLog |
| `Admin` | 32 | Tenants, User, Gruppen, Rollen, Permissions, Restriktionen, Rules (Regel-Engine), Settings, Registrierung |
| `STH` | 5 | StorageHandler: Sessions, Chunks, Data (Blob-Metadaten), Storage-Konfiguration |
| `HangFire` | 11 | Hangfire Background Jobs |
| `dbo` | 5 | DbUp-Schemaversionen (`SchemaVersionsSharedien`, `SchemaVersionsStorageHandler`) + UAA-Tabellen (`users`, `groups`, `group_membership`, aus CloudFoundry UAA) |
| `DataSync` | 4 | Azure SQL Data Sync Metadaten (Betrieb, nicht fachlich) |

Migrationen: DbUp-Stil, SQL-Skripte `YYYYMMDD_NNNN_Name.sql`. Kein EF Core im Legacy-System.

## 2. Das Kernmodell: ein typisierter Graph

Alles Fachliche ist ein **Node** mit einem **NodeType**; Beziehungen sind **Edges** mit **EdgeType**. Fachtabellen hängen 1:1 an einem Node (`AssetSet.AssetSetNodeId`, `Category.CategoryNodeId`, `Workspace.WorkspaceNodeId`, `Task.NodeId`, `Comment.NodeId`, `State.StateNodeId`, `License.NodeId`, `RightsOwner.NodeId`, `BillingContact.NodeId`, `CustomNode.NodeId`).

### 2.1 Node

| Feld | Bedeutung |
|---|---|
| `NodeId` (bigint, PK) | interne ID |
| `Identifier` (nvarchar 200, unique je NodeType) | öffentliche ID (Guid-String oder sprechend), in API/URLs verwendet |
| `NodeTypeId` | FK NodeType |
| `TenantId` | Mandant |
| `SequenceNr` | manuelle Sortierung |
| `IsDeleted` | Soft Delete |
| `InterruptInheritance` | unterbricht Vererbung von NodePermissions (und Properties) nach unten |
| `CreateUser/CreateDate/MutUser/MutDate/RowVersion` | Audit-Spalten, optimistic concurrency via rowversion |

### 2.2 NodeType (NodeSemantic = fest verdrahtete Semantik)

| NodeSemantic | Identifier | IsSystem | Fachtabelle | Nodes in Demo-DB |
|---|---|---|---|---|
| 1 | `Category` | ja | MAM.Category | 1 982 |
| 2 | `AssetSet` | ja | MAM.AssetSet | 7 920 |
| 3 | `Version` | ja | MAM.AssetVersion | 8 080 |
| 4 | `Workspace` | ja | MAM.Workspace | 350 |
| 5 | `Task` | ja | MAM.Task | 222 |
| 6 | `Comment` | ja | MAM.Comment | 236 |
| 7 | `State` | ja | MAM.State | 3 092 |
| 8 | `BillingContact` | ja | MAM.BillingContact | 4 |
| 9 | `RightsOwner` | ja | MAM.RightsOwner | 3 |
| 10 | `License` | ja | MAM.License | 4 |
| 1000 | *beliebig* (Custom Entities) | nein | MAM.CustomNode | ~60 Typen (Product, Campaign, Article, Variant, Flyer, ...) |

Erkenntnis: **AssetSet = das Asset** (Container), **Version = die konkrete Datei-Version**. Ein AssetSet hat n Versionen (Edge `Version`), jede Version hat Derivate. Custom Entity Types (NodeSemantic 1000) sind frei definierbare Business-Objekte mit Properties und Relationen — das ist der "PIM-light"-Teil von Sharedien.

### 2.3 EdgeType (EdgeSemantic)

| EdgeSemantic | Identifier | Directed | System | Bedeutung | Edges |
|---|---|---|---|---|---|
| 0 | `Undefined` / Custom | frei | nein | frei definierbare Relationen zwischen NodeTypes (`EdgeTypeAllowedNodeTypes` erlaubt FromType→ToType-Paare) | |
| 1 | `ParentChild` | ja | ja | Hierarchie: Category→Category, Category→AssetSet, Workspace→Category/State/Comment, State→Workspace/Task, Task→AssetSet | 16 474 |
| 2 | `Version` | nein | ja | AssetSet→Version | 8 079 |
| 3 | `Link` | nein | ja | Mehrfachverortung: AssetSet↔AssetSet, Category→AssetSet, Task→AssetSet, Custom↔AssetSet | 5 975 |
| 4 | `Crop` | nein | ja | Version→Version (Ausschnitt-Derivat als eigene Version) | 90 |
| 5 | `Translation` | nein | nein | AssetSet↔AssetSet (Sprachvariante) | |
| 6 | `Variant` | nein | nein | AssetSet↔AssetSet, Category→AssetSet | |
| 7 | `Licence` | nein | nein | AssetSet↔AssetSet | |
| 8 | `Duplicate` | ja | ja | AssetSet→AssetSet (Duplikat-Erkennung via Hash) | 36 |
| 9 | `Transition` | ja | ja | State→State (Workflow-Übergänge im Workspace) | 2 936 |
| 10 | `LicenseDrm` | ja | ja | License→AssetSet / License→Custom (DRM-Lizenz gilt für) | 69 |

`EdgeType.IsUsedForAssetSearch`: Relation wird in der Asset-Suche als Facette/Filter angeboten.
Properties können auch **an Edges** hängen (`Property.EdgeId`).

## 3. Assets, Versionen, Derivate, Dateien

- `AssetSet` (Status: 0 = 367, 1 = 7 534, 2 = 19; vermutlich Uploading/Ready/Deleted — Enum in Code prüfen).
- `AssetVersion`: `Version` (1..19 in Demo, ø 1.07), `AssetVersionStatus`, `ProcessingStatus`, `OriginalFilename/Name/FileEnding`, `AssetTypeId`.
- `AssetDerivation`: je Version × DerivationType: `DataId` (Verweis auf STH.Data), `FileTypeId`, `FileSize`, `FileHash`, `Status` (0 = 523, 2 = 38 633, 3 = 164), `DerivationFeedback` (Fehlertext).
- `FileReference`: generische Dateireferenz (`DataIdentifier` → STH, `FileUsageType`) für Avatare, Wasserzeichen-Dateien, Import-Dateien.
- `AssetSetDuplicate`: Protokoll erkannter Duplikate beim Import (Hash-basiert).
- Optimistic Concurrency überall via `RowVersion`.

### 3.1 AssetType (12 System-Typen) und FileType (105)

AssetTypes: `Unknown, Image, Movie, MS PowerPoint, MS Word, MS Excel, PDF, Adobe Illustrator, Audio, Adobe InDesign, CAD, Archive`.
FileType = Endung + MimeType → AssetType; Flags `QualityChecksActive`, `EnableBinaryDocumentExtraction` (Volltext aus Binärdokumenten für Suche).

### 3.2 DerivationType (System: `Original, Thumbnail, Screen, Print, Thumbnail_Animated`) + frei konfigurierbare

`AssetTypeDerivationType` steuert je AssetType × DerivationType:
`IsDefaultDerivation` (automatisch erzeugen), `AllowManualUpload` (Derivat manuell hochladen), `IsOnDemand` (erst bei Anforderung rendern), `ShowInDownloads`, `UseAsThumbnail`, `UseAsPreview`.

`DerivationTypeRestriction` (Role × AssetType × DerivationType × Action): rollenbasierte Einschränkung, welche Derivate eine Rolle sehen/downloaden darf.

### 3.3 DerivationJobDefinition = deklarative Rendering-Pipeline (JSON)

Die Processing-Engine ist eine **Step-Pipeline** mit Tool-Adaptern. `CustomStepType` in der Demo-DB: `ImageMagickConvert`, `FFMpeg`, `GhostScript`, `LibreOffice`, `Blender`. Platzhalter: `{JobInputFile}`, `{JobOutputFile}`, `{StepInputFile}`, `{StepOutputFile}`, `{SourceProfile}`, `{TargetProfile}`, `{WatermarkFile}`, `{StepVideoDurationSeconds}`, `{StepLibreOfficeEnvironmentArgument}`, `{JobInputFileNameWithoutExtension}`; Variablen mit Expression-Auswertung (`"Expression": "({StepVideoDurationSeconds} < 2) ? 1 : 2", "Evaluate": true`).

Beispiel Thumbnail (Bild):
```json
{"DerivationDataType":4,"Steps":[{"CustomStepType":"ImageMagickConvert","StepInputFile":"{JobInputFile}","StepOutputFile":"{JobOutputFile}",
 "TargetProfile":"sRGB_v4.icc","UseColorProfileOverrides":"true","IntentString":"-intent relative -black-point-compensation",
 "ArgumentString":"-quiet \"{StepInputFile}[0]\" -density 72x72 -resize \"240x240>\" -quality 92 -flatten -background white -alpha remove {SourceProfile} {TargetProfile} -colorspace sRGB -strip +repage \"{StepOutputFile}\""}]}
```
Beispiel Office-Thumbnail: LibreOffice → PDF, GhostScript → JPG (300 dpi), ImageMagick → 240px.
Beispiel Video-Thumbnail: FFmpeg Frame bei Sekunde 1/2 → ImageMagick resize. Animated Thumbnail: 16 Frames → GIF.
Beispiel Video-Stream: FFmpeg `libx264 -crf 19`, AAC 192k.
Beispiel 3D (`fbx`): Blender headless mit Python-Expression, Kamera/Licht automatisch, PNG mit Alpha.
`Crop` (DerivationDataType 0): kein Tool-Aufruf, sondern strukturierte `SourceImageDefinition / SourceImageTransformation / OverlayDefinitions / OutputImageDefinition` (Bildbearbeitung im Processing-Service, Basis für den Crop/Overlay-Editor im UI).

Eingabe-Dateitypen je Job: `DerivationJobDefinitionInputFileType`; Ausgabe-Format: `OutputFileTypeId`.

### 3.4 Wasserzeichen

`WatermarkDefinition` (JobDefinition + PropertyDefinition + `DisplayInUI`) und `WatermarkTrigger` (PropertyListValue oder Bool + `FileReferenceId` = Wasserzeichen-Bild). Bedeutung: *Wenn Property X den Listenwert Y hat (z. B. `published = false`, `country_of_origin = CH`), rendere Derivat mit Wasserzeichen-Datei Z.* Metadaten-getriebene DRM-Wasserzeichen.

### 3.5 Upload (chunked, resumable)

`UploadSession` (SessionIdentifier, `UploadType` 1–6 mit CHECK-Constraint auf Zielkombination: neues Asset in Entity, neue Version, Asset in Workspace, Asset in Task, Derivat-Upload für AssetSet, Derivat-Upload für Version; `ArchiveExtractionMode`, `DeleteAssetAfterExtraction`) + `UploadSessionChunk`. STH-Seite: `STH.Session`, `STH.Chunk` (ChunkDataId, SequenceNumber, StorageId), `STH.Data` (Name, Extension, FileHash, HashAlgorithm, FileSize, MimeType, StorageId), `STH.Storage` (ConfigurationType, Configuration JSON, Version) → mehrere Storage-Backends konfigurierbar.

`ArchiveExtractionJob` + `ArchiveExtractionJobEntry`: ZIP-Upload wird entpackt, Ordner → Kategorien, Dateien → Assets (Mode 0/1), optional Archiv danach löschen.

## 4. Property-System (dynamische Metadaten, EAV)

| Tabelle | Zweck |
|---|---|
| `PropertyGroup` | Gruppierung im UI (Tabs/Sections), `SequenceNr`, `Status` |
| `PropertyDefinition` | Metadatenfeld-Definition (535 in Demo, 206 System) |
| `PropertyDefinitionNodeType` | an welchen NodeTypes das Feld verfügbar ist |
| `PropertyDefinitionSystemPropertyStrategy` | wie System-Properties je NodeType befüllt werden (Strategy-Enum 1–10, z. B. `file_name`→1, `workspace_name`→2, `category_name`→3, `asset_status`→4, `customnode_name`→5, `workspace_type`→6, Task-Felder→7, BillingContact→8, RightsOwner→9, License→10) |
| `PropertyListValue` | Listenwerte (typisiert: Bool/Int/Decimal/Date/DateTime/Duration/Text, `TranslationIdentifier`, `SortKey`, `Status`) |
| `Property` | Instanz: PropertyDefinition an Node **oder** Edge |
| `PropertyValue` | Wert(e) je Property: typisierte Spalten + `PropertyListValueId` + `TranslationIdentifier` (sprachabhängig) + `UserContextId` (kontextabhängig, z. B. Land) |
| `PropertyNodeTypeInheritance` | Property wird an Kind-Nodes bestimmter NodeTypes vererbt (z. B. Kategorie-Property auf alle Assets darunter) |
| `PropertySet` | benannte Feld-Auswahl je NodeType (Ansicht/Formular), `IsGlobal`, `IsDefault`; `PropertySetPropertyDefinitions` mit `FillMode` (0/1/2); `PropertySetCondition` (Set gilt nur wenn PropertyDefinition = ListValue) → **bedingte Formulare** |
| `Admin.UserPropertySet` | User-spezifische Sichtbarkeit/Schreibrecht auf Property |
| `Admin.PropertyDefinitionRestriction` / `PropertyValueRestriction` | rollenbasierte Einschränkung je Feld / je Wert (Action 0–4) |
| `PropertyImport*` | CSV/Excel-Metadatenimport mit Mutationen, Messages (Row/Column, Severity), Statistik |
| `PropertyMassEdit*` | Massenbearbeitung: Mutationen mit `PropertyStrategy` / `PropertyValueStrategy` / `TranslationStrategy` (genau eine gesetzt), Ergebnis je Asset, Limit je Rolle (`Role.PropertyMassEditEntityLimit`, `PropertyExportEntityLimit`) |

### 4.1 PropertyDefinition-Flags

`ValueType` (0 Text 289×, 1 LongText 17×, 2 Date 12×, 3 DateTime 166×, 4 ? 3×, 5 Int 21×, 6 ? 1×, 7 Decimal 5×, 8 Bool 16×, 9 Duration 5× — genaue Enum-Namen im Code prüfen),
`HasListValues`, `AllowMultipleValues`, `AllowCustomListValue`, `Editable`, `SearchOption` (0 nicht / 1 / 2 / 3 = Volltext+Filter — Enum prüfen), `IsLanguageDependant`, `IsSystemProperty`, `IgnoreCopyingOnNewVersion`, `IsSearchableFacet`, `IsFileNamingUnlocked`, `HasUserContext`, `IsUrl`, `SortKey`, `Status`.

### 4.2 System-Properties (Auszug, alle IsSystemProperty = 1)

- Allgemein (Version): `system_import_date`, `imported_by`, `asset_type` (Liste, 12 Werte), `asset_status` (Liste, 3), `file_name`, `file_extension`, `file_size`, `file_hash`, `asset_version`, `asset_identifier`, `expiry_date`, `valid_from`.
- Namen je NodeType: `category_name`, `workspace_name`, `customnode_name`, `task_name`, `license_name`, `rightsowner_name`, `billingcontact_name`; `entity_identifier`, `workspace_identifier`, `workspace_type`, `workspace_is_template`.
- Task: `task_assignee`, `task_due_date`, `task_status`, `task_description`, `task_update_date`, `task_type` (4 Typen: standard, asset_post_production, asset_upload, asset_metadata_edit).
- Technisch (automatisch extrahiert): `image_width/height`, `image_horizontal/vertical_resolution`, `image_color_space`, `image_bit_depth`, `image_width/height_at_300dpi`, `image_compression_type/quality`, `image_file_format`, `image_icc_profile`; `video_duration/format_name/format_long_name/bitrate/frames_per_second/width/height/pixel_format/codec_name/codec_long_name`; `audio_duration/format_name/format_long_name/bitrate/channels/sample_rate/codec_name/codec_long_name/artist/publisher/title/album/genre/recording_date`.
- EXIF: `exif_model, exif_make, exif_flash, exif_exposure_time, exif_exposure_program, exif_datetime, exif_focal_length, exif_aperture_value, exif_metering_mode, exif_subject_distance, exif_iso_speed_ratings, exif_exposure_bias_value, exif_white_balance, exif_owner_name, exif_lens`.
- IPTC: `iptc_city, iptc_sublocation, iptc_province_state, iptc_country_name, iptc_country_code, iptc_geodata, iptc_source, iptc_author, iptc_author_title, iptc_creator*, iptc_writer_editor, iptc_copyright_status/notice/url, iptc_credit, iptc_contact, iptc_keywords, iptc_caption, iptc_headline, iptc_datecreated, iptc_objectname, iptc_category, iptc_supplemental_category`.
- DRM (License/RightsOwner/BillingContact): `entity_status`, `license_description`, `license_rights` (Multi-Liste), `license_period_start/end`, `rights_owner_type`, `contact_*` (first/last name, email, phone, street, zip, city, country), `banking_*` (bank name, swift, account name/number, IBAN), `billingcontact_email`.

## 5. Kategorien, Workspaces, Tasks, States, Kommentare, Shares

- `Category`: Baum via ParentChild-Edges, `TranslationIdentifier` (mehrsprachiger Name), `SequenceNr`. Assets können in mehreren Kategorien sein (`Link`). Favoriten je User in `UserSettings.UiSettings` (`favoriteCategoryIdentifiers`).
- `Workspace`: Kollaborationsraum. `WorkspaceType` (0/1/2), `Status`, `IsTemplate` + `SourceTemplateIdentifier` (Vorlagen), `PropertySetIdentifier` (Formular), `FinalizationCategoryId` + `CopyPropertiesToAssets` (beim Abschluss Assets in Zielkategorie übernehmen), `AvatarFileReferenceId`. Enthält Kategorien, States, Kommentare.
- `State`: Kanban-Spalten/Workflow-Status im Workspace (`SequenceNr`, `IsDefault`, mehrsprachig). `Transition`-Edges definieren erlaubte Übergänge. Tasks hängen an States (ParentChild State→Task).
- `Task`: Name, Description, DueDate, AssignedUserId, TaskTypeId, Status (0 offen/1 erledigt), SequenceNr; Assets werden per ParentChild/Link an Tasks gehängt (Upload-Ziel „Task").
- `Comment`: Text an beliebigem Node.
- `Share`: Freigabe eines Workspace-Nodes an externe E-Mail-Empfänger mit Token und Ablaufdatum (`ValidUntil`); `ShareAssetTypeDerivationType` steuert, welche Derivate je AssetType im Share downloadbar sind.
- `SavedSearch`: benannte Suche, `SearchQuery` als JSON (enthält `includePropertyDefinitionIdentifiers`, `advancedSearchFilters[]` mit Berechtigungs-Snapshot `entityPermissions{canView..., canRead..., canCreate..., canUpdate..., canDelete...}` — d. h. das Frontend erhält pro Entität ein Permission-Objekt mit ~150 `can*`-Flags).

## 6. Berechtigungsmodell (das Wichtigste für Shareme)

### 6.1 Ebenen

1. **Tenant** (`Admin.Tenant`): Mandant mit Status, `IsDefaultTenant`, `AcceptsRegistrations`; Nodes gehören einem Tenant. 13 Tenants in Demo.
2. **User** (`Admin.User`): lokale Credentials (Salt/Hash) oder externer Provider (`ProviderName`, `ProviderSubjectId`; z. B. `Sharedien`, `<name>-azure-ad`), `Status` (1 aktiv, 2, 10, 20, 21, 31 — Enum prüfen; Registrierungs-/Sperr-Workflow), `AccessType` (0/1), Kontaktfelder, `LastLoginDate`, `LastPasswordResetDate`, `StatusChangeDate`.
3. **UserGroup** + `UserGroupUser` (`IsProvisioned` = aus externem Verzeichnis) + `ExternalGroup`→`UserGroup` Mapping (Azure AD/SAML-Gruppen auf interne Gruppen).
4. **Role** (`Admin.Role`): Bündel von Permissions. `IsSystem` (workspaceOwner/Reader/Collaborator, entityOwner/Reader, collectionOwner/Reader, globaladmin, licenseAdmin), `PropertyMassEditEntityLimit`, `PropertyExportEntityLimit`.
5. **RoleObjectTypePermission** (Role × `Permission` int): flache Liste feingranularer Permissions (Enum mit ~290 Werten; in Demo 1..267 belegt). Beispiele aus dem Permission-Snapshot: `ViewAssetBrowser, AddAssetSet, UploadAssetSet, View/Read/Create/Edit/Update/Delete{AssetSet, Category, AssetProperty, WorkspaceProperty, TaskProperty, CategoryProperty, Relation, AssetDerivationJob, AssetVersion, AssetDerivation, CropDerivation, PropertyDefinition, PropertyGroup, User, UserGroup, Tenant, UserContext, Role, DataLanguage, Workspace, WorkspaceTemplate, RelationType, FileNamingDefinition, Task, Comment, NodePermission, Basket, {Asset|Version|Workspace|Task|Category|CustomEntity}PropertySet[Global], ...}, FinalizeWorkspace, EditCategoryPropertyInheritance, ...`. Muster: je Objekttyp `View` (Menü/UI), `Read`, `Create`, `Update`/`Edit`, `Delete`.
6. **RoleObjectType** (Role × ObjectType): auf welche Objekttypen die Rolle *zugewiesen* werden darf (3, 14, 15, 17, 19, 51, 52, 53, 55, 56, 59 — z. B. 15/55 = Workspace-Rollen, 56 = Collection-Rollen, 59 = License-Rollen, 19 = Custom Entity).
7. **NodePermission** (Node × Role × (User XOR UserGroup), `Status`): **Zuweisung einer Rolle an genau einem Knoten** an einen User oder eine Gruppe. CHECK-Constraint erzwingt genau einen Principal. Unique (NodeId, UserGroupId, UserId, RoleId).
8. **MaterializedNodePermission** (Node × NodePermission): **vorberechnete Vererbung** — für jeden Nachfahren-Node ein Eintrag je geerbter NodePermission (22 272 Einträge für 1 718 NodePermissions). `Node.InterruptInheritance` stoppt die Materialisierung. Damit ist „darf User X Node Y?" ein Index-Lookup.
9. **EntityPermission**: Rolle an einer **Nicht-Node-Entität** (genau eine von: SavedSearch, PropertySet, Workspace, AssetDerivationJob, PropertyMassEdit, PropertyImport, FileNamingDefinition, ArchiveExtractionJob) für User XOR Group. → Besitz/Freigabe von Suchen, Sets, Jobs.
10. **UserGroupToRoleMap** (Tenant × Role × UserGroup): globale Rollen je Mandant für eine Gruppe (nicht knotenbezogen).
11. **Restrictions**: `DerivationTypeRestriction`, `PropertyDefinitionRestriction`, `PropertyValueRestriction` (Role × Ziel × Action) — negative/positive Feinsteuerung unterhalb der Permission-Ebene.
12. **UserPropertySet**: User × Property → `CanWrite`.

### 6.2 Konsequenz für Shareme

Sharedien hat genau das, was gefordert ist: Rechte auf **spezifischen Knoten/Assets** zuweisen und entziehen, mit Vererbung nach unten, Unterbrechung der Vererbung, User- und Gruppenbezug, Rollen als Permission-Bündel, plus Rechte auf Nicht-Baum-Objekte. Die Materialisierung ist das Performance-Muster (Write-heavy, Read-cheap).

## 7. Audit / ActivityLog / Benachrichtigungen

- `ActivityLog` (20 434): `ObjectType` (Enum: 0, 3, 10, 12, 15, 17, 23, 34, 35, 36, 54, 55, 60, 1000, 1070, 1080 …), `Type`, `ChangeType`, `NodeId`, `ObjectIdentifier`, `UserIdentifier`, `CreatedAt`, `ParentActivityLogId` (Gruppierung: ein Upload → viele Kind-Einträge). `ActivityLogAdditionalData` (Name/Value): `OldValue`, `NewValue`, `PropertyDefinitionIdentifier`, `RelationTypeIdentifier`, `Directed`, `Status`, `WorkspaceType`, `ValidUntil`, `Name`, `LanguageIdentifier`, `FileName`, `SourceTemplateIdentifier`, `PropertySetIdentifier`, `FinalizationCategoryIdentifier`. → Feld-genaues Change-Log mit Alt/Neu-Wert.
- `ActivityLogUser` (ActivityLog × User, `ReadAt`): **Inbox/Notification-Feed** pro User mit Gelesen-Status.
- `NodeActivityLogSettings` (Node × User: `SendToInbox`, `SendEmail`): **Abonnement** („Watch") eines Knotens.
- `UserNotificationSettings` (User × Node: `DisableNotifications`): Stummschalten.
- `OutgoingEmail` + Recipients (Address/User) + `OutgoingEmailTemplateParameter`: Mail-Queue mit Template-Identifier und Parametern.
- `OutgoingMessageQueueEvent`: Outbox für externe Integrationen (Event-Stream): `TypeName` (z. B. `...MessageQueue.V1.EventStream.UserEvent`), `EventAction` (Read/Create/Update/Delete), `ObjectType`, `ObjectIdentifier`, `PropertyDefinitionIdentifier`, `UserIdentifier`, `Payload`, `TransactionIdentifier`, `Sent`, `PriorityForced/PrioritySelector`. → **Transactional Outbox Pattern**.
- `BackgroundJob` (Type 1–15, `HangfireJobId`) + `BackgroundJobTrigger` (`TransactionIdentifier`, `JobType` 1/3/4/5, `EntityIdentifier/EntityTypeIdentifier`, `PermissionIdentifier`, `Done`): Jobs werden transaktional getriggert (Outbox für Jobs, z. B. Permission-Materialisierung, Search-Reindex, Rule-Ausführung).

Versionierung: `AssetVersion.Version` fortlaufend, Properties je Version (`IgnoreCopyingOnNewVersion` steuert, welche Metadaten beim neuen Upload übernommen werden). Keine Property-History in eigener Tabelle — Historie nur über ActivityLog (OldValue/NewValue).

## 8. Regel-Engine (Rules)

- `Rule` (+ `RuleNodeType`: für welche NodeTypes).
- `RuleTrigger`: `IsFilter` (Vorbedingung) vs. Trigger, `TriggerOperator` (0 equals, 1, 2, 5, 6, 8, 9, 10 — z. B. „on version upload", „on category create"), `PropertyId`, `AdditionalData`.
- `RuleAction`: `OnTriggerMatched` (Aktion bei Match oder bei Nicht-Match), Ziel-`PropertyId`, `Template` mit Ausdruckssprache: `PI_{fnc.findInAscendants(counter_int).increment().write()}`, `06{fnc.find(baumer_sapmat)}` → Funktionen `find`, `findInAscendants`, `increment`, `write`.
- `RuleJob` (Statistik: Matched/NotMatched/Executed/Skipped/Error) + `RuleJobEntity` (je Node × Rule × UserContext) + `RuleJobEntityValueExecuted` + Identifier-Tabellen: Regeln können **massenhaft nachträglich** auf Entity-Mengen ausgeführt werden (78 037 Job-Entities).
- Typische Use-Cases: Status setzen wenn Land = CH, Counter-Nummern für Kategorien vergeben, Beschreibung setzen bei Upload, Materialnummer mit Präfix.

## 9. DRM / Lizenzen / Preise

- `License` (Node, `Status`, `PeriodStart/End`) mit Properties (`license_rights` Multi-Liste, Beschreibung). Edge `LicenseDrm` License→AssetSet/Custom.
- `RightsOwner` (Node, Name, BillingContact, Avatar, Status) und `BillingContact` (Node, Name, Email, Status) mit Kontakt-/Bank-Properties.
- `PriceList` (+ `PriceListUserContext`: Prozentsatz je UserContext bzw. UserContextGroup) und `Buyout` (RightsOwner × PriceList × License × Currency, Basis, Zuschlag %, Zeitraum): Nutzungs-/Buyout-Kalkulation nach Land/Kontext.
- `Currency` (CHF, EUR).
- `UserContext` (Länder DE/CH/AT/MX/CL/CN/AU/BE …, `IsDefault`) + `UserContextGroup`: **Kontext-Dimension** für Property-Werte (`PropertyValue.UserContextId`) und Preise. User hat Default-Kontext in `UserSettings.UserContextId`.

## 10. Mehrsprachigkeit

- `Language` (Data Languages: en (default), de, fr, it, es, ru, da, ga, hy, de_ch, en_us, de_DE, de_AT …; `IsDefaultLanguage`, `Status`).
- `Translation` (ObjectType × TranslationIdentifier × FieldName × LanguageId → TranslatedValue): 17 877 Einträge. FieldNames: `Name`, `Description`, `TextValue`. Alle Systemobjekte (NodeType, EdgeType, Role, PropertyGroup, PropertyDefinition, PropertyListValue, Category, State, Workspace, Language, UserContext, DerivationType …) haben `TranslationIdentifier`. Property-Werte sprachabhängig via `PropertyValue.TranslationIdentifier`.
- UI-Sprache getrennt: `UserSettings.DisplayLanguage`, `Locale`, `LanguageId` (Datensprache), `SearchTermOperator` (AND/OR Default), `UiSettings` JSON (`selectedPropertySetIdentifierToEntityTypeMapping`, `favoriteCategoryIdentifiers`), `AvatarFileReferenceId`.

## 11. Weitere Systemobjekte

- `FileNamingDefinition` (+ Properties, + Tenant-Default): Download-Dateinamen-Muster, z. B. `test_{var.year}.{var.extension}`; `CharacterCasing`, `ReplaceIllegalCharacters`, `AbsentPropertyStrategy`.
- `Admin.Configuration` (Path/Value): Key-Value-Konfiguration (in Demo leer).
- `Admin.ExpiringCode` (User, Code, CodeType 1/2/3, ExpiresAt): E-Mail-Bestätigung / Passwort-Reset / Einladung.
- `Admin.UserRegistration` (User, TenantIdentifier, ApprovedAt): Self-Registration mit Freigabe.
- `SearchReIndex` (+ FailedEntities): Reindex-Läufe mit Statistik, abbrechbar, je EntityType.
- `DbStatus`: Versionsinfo je Migration.

## 12. Was Shareme daraus übernehmen sollte (Kurzfassung)

1. **Graph-Modell** (Node/Edge/NodeType/EdgeType mit Semantik + frei definierbaren Custom-Typen und Relationen) — ist das Fundament für DAM + DMS + Knowledge Base + Intranet in einem.
2. **EAV-Property-System** mit Gruppen, Listen, Mehrsprachigkeit, Kontext, Vererbung, bedingten PropertySets, Restriktionen, Import/Massenbearbeitung.
3. **Permission-Modell**: Rollen = Permission-Bündel; Zuweisung von Rollen an User/Gruppe **pro Node** mit Materialisierung und Vererbungsunterbrechung; EntityPermissions für Nicht-Baum-Objekte; globale Tenant-Rollen.
4. **Versionen + Derivate + deklarative Rendering-Pipeline** (ImageMagick/FFmpeg/Ghostscript/LibreOffice/Blender) inkl. On-Demand, manuelle Derivate, Wasserzeichen per Metadaten-Regel, Crop.
5. **Audit**: ActivityLog mit Alt/Neu-Werten, Inbox mit Gelesen-Status, Node-Abos (Inbox/E-Mail), Outbox für Events + Jobs.
6. **Workspaces** (Kanban-States mit Transitions, Tasks, Kommentare, Templates, Finalisierung), **Shares** (externe Links mit Ablauf und Derivat-Auswahl), **SavedSearches**, **Rules** (Trigger/Filter/Action mit Template-Sprache), **DRM** (Lizenzen, Rechteinhaber, Preislisten, Buyouts), **UserContext** (Länder-Dimension).
7. **Chunked Upload** mit Session, Archiv-Extraktion, Duplikaterkennung per Hash, mehrere Storage-Backends.
