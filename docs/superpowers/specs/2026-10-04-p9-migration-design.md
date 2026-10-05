# P9a Sharedien-Importer – Design

Ziel (Architektur-Spec §P9): Bestandsdaten aus Sharedien 3 (SQL Server + Binär-Storage) nach Shareme übernehmen. Abnahme: ein Lauf gegen eine Kopie der Produktiv-DB übernimmt Struktur, Rechte, Metadaten und Originaldateien; Zählvergleich Quelle/Ziel ohne ungeklärte Differenzen; Suche und Vorschaubilder sind nach den Nachlauf-Jobs vollständig; ein zweiter Lauf ändert nichts.

Grundlagen: [sharedien-db-schema.md](../../analysis/sharedien-db-schema.md), [sharedien-db-insights.md](../../analysis/sharedien-db-insights.md), [sharedien-services.md](../../analysis/sharedien-services.md) §4 (Storage). Alle Zahlen unten stammen aus `Sharedien3Analysis` (LocalDB, Stand bacpac 2021-04) und sind **nur Zeilenzahlen** – keine Inhalte.

## 1. Umfang

### 1.1 Wird übernommen

| Bereich | Quelltabellen (Zeilen) | Ziel |
|---|---|---|
| Mandanten | `Admin.Tenant` (35, davon 22 gelöscht; **nur 1 Mandant besitzt Nodes**) | `Coworkee.Identity.Tenant` |
| Benutzer | `Admin.User` (192: 179 lokal, 13 extern, 35 gelöscht), `Admin.UserSettings` (159) | `User` (+ `UserLogins` für externe), `UserProfile` (Department, Phone) |
| Gruppen | `Admin.UserGroup` (105), `UserGroupUser` (358), `ExternalGroup`/`ExternalGroupUserGroups` (21/31, nur als Bericht) | `UserGroup`, `UserGroupMember` |
| Rollen + globale Rechte | `Admin.Role` (116, 27 gelöscht, 9 System), `RoleObjectTypePermission` (6 534, 295 verschiedene Permission-Ints), `UserGroupToRoleMap` (63) | `Role`, `PermissionGrant` (ProviderType Role), `UserGroupRole` |
| Knoten-Rechte | `Admin.NodePermission` (1 718: 1 506 User, 212 Gruppe; 770 auf gelöschten Nodes), `MAM.Node.InterruptInheritance` (26 Nodes) | `ResourcePermission` (ResourceType `Node`), `Node.InterruptInheritance` |
| Restriktionen | `DerivationTypeRestriction` (19), `PropertyDefinitionRestriction` (169), `PropertyValueRestriction` (7) | `Restriction` (Kinds hiddenProperty/readOnlyProperty/hiddenListValue/blockedRendition) |
| Typen | `MAM.NodeType` (47, 37 Custom), `EdgeType` (49, 39 Custom), `EdgeTypeAllowedNodeTypes` (57) | `NodeType`, `EdgeType` (+ `AllowedPairs`) |
| Knoten | `MAM.Node` (24 592, 2 606 gelöscht), `Category` (3 709), `AssetSet` (7 920), `CustomNode` (146), `Workspace` (809), `Task` (406) | `Folder`, `Asset`, `CustomNode`, `Workspace`, `WorkTask` |
| Kanten | `MAM.Edge` (34 155: ParentChild 16 474, Version 8 079, Link 5 975, Transition 2 936, Custom 166, Duplicate/Variant/Translation/Licence 366, Crop 90, LicenseDrm 69) | `Node.ParentId`/`Path`, `Edge`, `WorkflowState.AllowedTargets`, `LicenseAsset` |
| Metadaten-Katalog | `PropertyGroup` (53), `PropertyDefinition` (535, 206 System), `PropertyDefinitionNodeType` (1 645), `PropertyListValue` (3 774), `PropertyNodeTypeInheritance` (64), `PropertySet*` (80/483/15), `Language` (26), `UserContext` (92), `UserContextGroup*` | `PropertyGroup`, `PropertyDefinition`, `PropertyListValue`, `PropertySet`, `DataLanguage`, `UserContext`, `ContextGroup` |
| Metadaten-Werte | `MAM.Property` (188 109), `PropertyValue` (189 622; 20 460 Listenwerte, 6 859 übersetzt, 1 037 mit UserContext), `MAM.Translation` (17 877, davon 7 951 für Werte) | `Node.Metadata` (jsonb `{lang, ctx, v, list}`), `AssetVersion.MetadataSnapshot` |
| Assets/Dateien | `AssetVersion` (8 079, max. 19 Versionen je Asset), `AssetDerivation` (39 320; 8 079 `Original` ≈ 34,6 GB, alle ≈ 42,6 GB), `FileType` (105), `AssetType` (12), `DerivationType` (56) | `AssetVersion` + Blob, `Derivation` nur für manuelle Derivate, `DerivationType` |
| Workspaces | `State` (3 117), `Comment` (289), Workspace-Rollen über `NodePermission` | `WorkflowState`, `Comment`, `ResourcePermission` |
| DRM | `License` (17), `RightsOwner` (19), `BillingContact` (44), `PriceList`/`PriceListUserContext` (3/5), `Buyout` (13), `Currency` (2) | `License`, `RightsOwner`, `BillingContact`, `PriceList`, `Buyout` |
| Sonstiges | `Rule*` (17 Regeln, 30 Trigger, 19 Aktionen), `WatermarkDefinition`/`Trigger` (18/22), `FileNamingDefinition` (1), `FileReference` (84, Avatare/Wasserzeichen), `NodeActivityLogSettings` (99) | `Rule` (**inaktiv**), `WatermarkDefinition`, `FileNamingDefinition`, Avatar-/Wasserzeichen-Blobs, `NodeSubscription` |

### 1.2 Bewusst nicht

| Was | Warum |
|---|---|
| `HangFire.*`, `DataSync.*`, `dbo.SchemaVersions*`, `dbo.users/groups/group_membership` (UAA-Altlast), `MAM.DbStatus` | Betrieb/Infrastruktur, kein Fachinhalt |
| `MaterializedNodePermission` (22 272) | Shareme materialisiert nicht; Vererbung wird zur Laufzeit über `Node.Path` + `InterruptInheritance` ausgewertet (`NodeHierarchy`) |
| Generierte Derivate (Thumbnail, Screen, Print, Animated …; ≈ 31 000) | werden von `RenderDerivationsJob` neu erzeugt; ältere Renderer-Ergebnisse wären ohnehin inkonsistent |
| System-Properties mit Strategy oder technischer Herkunft (173 329 Werte, z. B. `file_*`, `image_*`, `video_*`, `exif_*`, `iptc_*`, `*_name`) | Namen kommen aus den Fachtabellen, Technik liest `TechnicalMetadataReader` beim Rendern neu |
| `ActivityLog*` (20 434 / 10 318 / 130), `OutgoingEmail*`, `OutgoingMessageQueueEvent`, `BackgroundJob*`, `SearchReIndex*` | andere Struktur als `AuditEntry`; Historie bleibt in der archivierten Quell-DB |
| Job-Protokolle: `RuleJob*` (78 037 Job-Entities), `PropertyImport*`, `PropertyMassEdit*` (≈ 75 000), `AssetDerivationJob*`, `ArchiveExtractionJob*`, `AssetSetDuplicate` (10 375) | abgeschlossene Läufe, kein Zustand |
| `UploadSession*`, `STH.Session`/`Chunk`, `ExpiringCode`, `UserRegistration` | flüchtig bzw. sicherheitsrelevant (Codes/Passwörter nie migrieren) |
| `Share`/`ShareRecipient` (106/105), `SavedSearch` (88), `EntityPermission` (1 324) | Tokens liegen in Shareme nur gehasht, Suchformat inkompatibel; siehe offene Fragen §6 |
| `DerivationJobDefinition` (35) | Shareme hat eigene Renderer (Rendition-Typen mit Größe/Format); manuell nachkonfigurieren |

## 2. Mapping

### 2.1 Ids

- Alle Sharedien-PKs sind `bigint IDENTITY`; daneben trägt fast jede Tabelle `Identifier nvarchar(200)` (Default `lower(newid())`), der in API und URLs verwendet wird.
- Regel: **`Identifier` ist eine GUID → diese GUID wird die Ziel-Id** (bleibt in Links/Integrationen stabil). Sonst **deterministische UUIDv5** aus festem Namespace und `"{schema}.{table}:{pk}"`.
- Anteil GUID-Identifier: Node 21 544/24 592, Edge 100 %, User 191/192, UserGroup 104/105, PropertyDefinition 131/535, PropertyListValue 67/3 774, NodeType 9/47, Tenant 0/35, Role 0/116.
- `MAM.Node.Identifier` ist nur je NodeType eindeutig: 42 Identifier kommen mehrfach vor → bei Kollision zweiter und folgende auf UUIDv5 ausweichen, im Bericht ausweisen.
- Sprechende Identifier (`file_name`, `country`, Custom-Typen) werden zum `Key` (Kleinbuchstaben, Ziffern, `-`, `_`, ≤ 100; sonst slugifizieren + Kollisionssuffix).
- Mapping-Tabelle `import.IdMap (SourceTable, SourceId bigint, TargetId uuid, Hash, ImportedAt)` im Ziel-Postgres (eigenes Schema `import`, nicht im EF-Modell); Basis für Idempotenz und spätere Redirects alter URLs.

### 2.2 Tabellen

| Quelle | Ziel | Transformation |
|---|---|---|
| `Admin.Tenant` | `Tenant` | Name aus `Translation` (Default-Sprache), `Identifier` neu (Slug), `IsActive = Status==1 && !IsDeleted`, `IsDefault`, `AcceptsRegistrations`. Standard: nur Mandanten mit Nodes oder per `--tenant` gewählte |
| `Admin.User` | `User` | Sharedien-User sind **mandantenlos**, Coworkee-User haben genau eine `TenantId` → Zielmandant des Laufs. `UserName`, `Email` (+ normalisiert), `EmailConfirmed = true`, `FirstName/LastName`, `IsActive = Status==1 (Active) && !IsDeleted`, gelöschte nicht übernommen (Bericht), `LastLoginAt = LastLoginDate`, `CreatedAt`. Passwort-Hash/Salt **nie**; Passwort bleibt leer |
| `Admin.User` (extern) | `UserLogins` | `ProviderName` → konfigurierte `LoginProvider`-Zuordnung, `ProviderSubjectId` → `ProviderKey` |
| `UserSettings` / `User.Department,PhoneNumber` | `UserProfile` | Avatar über `AvatarFileReferenceId` → Blob-Kopie, `AvatarBlobKey` |
| `Admin.UserGroup` / `UserGroupUser` | `UserGroup` / `UserGroupMember` | `IsProvisioned` verworfen (Provisioning neu durch externes Login) |
| `Admin.Role` | `Role` | System-Rollen → vorhandene Shareme-Rollen (`globaladmin`→`admin`, `workspaceOwner`→Binding `owner`, `workspaceCollaborator`→`collaborator`, Reader-Rollen→Rolle mit `Nodes.Read`); übrige Rollen als Mandantenrollen, Name aus `Translation`, gelöschte nicht |
| `RoleObjectTypePermission` | `PermissionGrant` | Code-Tabelle `PermissionMap` (Sharedien-Enum-Int → Shareme-Recht, z. B. Read/Create/Update/Delete AssetSet → `Nodes.*`, Edit PropertyDefinition → `Metadata.Manage`, Rule → `Automation.Rules`, User → `Identity.Users.Manage`, License → `Drm.Manage`); unbekannte Ints: Bericht, kein Grant |
| `UserGroupToRoleMap` | `UserGroupRole` | nur Zeilen des Zielmandanten |
| `Admin.NodePermission` | `ResourcePermission` | `ResourceType = "Node"`, `PrincipalType` User/Group (CHECK garantiert genau einen), `RoleId` gemappt; Nodes gelöscht/nicht importiert → verworfen; Status inaktiv → verworfen |
| `*Restriction` | `Restriction` | DerivationType → `blockedRendition` (Key des Rendition-Typs), PropertyDefinition → `hiddenProperty`/`readOnlyProperty` je Action, PropertyValue → Listenwert-Restriktion |
| `MAM.NodeType` | `NodeType` | Semantik 1→`folder`, 2→`asset`, 4→`workspace`, 5→`task`, 1000→neuer `NodeType` (Semantic Custom, Key aus Identifier). 3/6/7/8/9/10 werden keine Nodes |
| `MAM.EdgeType` | `EdgeType` | Link/Duplicate/Variant/Translation → Seed-Typen (feste Ids), Custom (Semantik 0) → neuer Typ, `IsDirected`, `AllowedPairs` aus `EdgeTypeAllowedNodeTypes` als `"from:to"` |
| `MAM.Node` + `Category` | `Folder` | Name/Names aus `Translation` (`Category.TranslationIdentifier`, `FieldName = Name`, je Sprache), `SortIndex = SequenceNr`, `InterruptInheritance`, `IsDeleted` → nur mit `--include-deleted` (dann `DeletedAt = MutDate`) |
| `Node` + `AssetSet` | `Asset` | `Status`: 0 Importing→`importing`, 1 Imported→`ready`, 2 Duplicate→`duplicate`; `AssetTypeKey` aus `AssetVersion.AssetTypeId` der neuesten Version; `CurrentVersionId` = höchste `Version`; Name = `OriginalFilename` der neuesten Version |
| `Node` + `CustomNode` | `CustomNode` | Name aus `Translation`/`customnode_name` |
| `Node` + `Workspace` | `Workspace` | `WorkspaceKind`: 0 Upload→`upload`, 1 Collection→`collection`, 2 Collaboration→`collaboration`; `IsTemplate`, `TemplateId` aus `SourceTemplateIdentifier`, `FinalizationFolderId` aus `FinalizationCategoryId`, `CopyMetadataToAssets`, `RequiredSetId` aus `PropertySetIdentifier`; Workspace ist Wurzel (`ParentId = null`) |
| `Node` + `Task` | `WorkTask` | Parent = Workspace des States, `StateId` = State-Parent, `Description`, `DueDate`, `AssigneeIds = [AssignedUserId]`, `Status 1` → `CompletedAt = MutDate`, `SortIndex = SequenceNr` |
| `Node` + `State` | `WorkflowState` | `WorkspaceId` aus ParentChild, `Name` aus `Translation`, `IsDefault`, `SortIndex`; `Transition`-Kanten → `AllowedTargets` |
| `Node` + `Comment` | `Comment` | `NodeId` = Parent, `AuthorId` = User zu `CreatedBy`, `Text`, `CreatedAt` |
| `Edge` ParentChild | `Node.ParentId`/`Path`/`Depth` | Shareme ist ein Baum: erste ParentChild-Kante (nach `EdgeId`) wird Parent, weitere (2 Fälle) → `Link`-Kante. Wurzelkategorien → unter die Mandanten-Wurzel (genau ein Root-Folder je Mandant, `IX_Nodes_Root`). Task→Asset und State→Task siehe oben; Assets eines Tasks: Parent = Workspace, plus `Link` Task→Asset |
| `Edge` Link/Duplicate/Variant/Translation/Licence/Custom | `Edge` | Typ gemappt; Licence (Semantik 7) → Custom-Typ `licence`; Duplikate bei gleichem (Typ, From, To) zusammenfassen |
| `Edge` Version | – | ergibt `AssetVersion.AssetId` |
| `Edge` Crop (90) | `Asset` + `Edge` Variant | Crop-Version wird eigenes Asset (wie `CreateCropVariant`), Variant-Kante vom Quell-Asset |
| `Edge` LicenseDrm | `LicenseAsset` | nur Ziele vom Typ Asset; Custom-Ziele → Bericht |
| `PropertyDefinition` | `PropertyDefinition` | `ValueType`: 0 String→`string`, 1 Text→`text`, 2 Date→`date`, 3 DateTime→`datetime`, 4 Time→`time`, 5 Integer→`integer`, 6 Float→`decimal`, 7 Decimal→`decimal`, 8 Boolean→`boolean`, 9 TimeSpan→`duration`; `IsUrl`→`url`. `ListMode`: `HasListValues` + `AllowMultipleValues` → `single`/`multi`, sonst `none`; `MultiValue = AllowMultipleValues && !HasListValues`; `AllowCustomValues`, `Localizable = IsLanguageDependant`, `HasContext`, `Editable`, `IsSystem`, `Searchable`: SearchOption 0/1/2/3 → `none`/`filter`/`fulltext`/`both`, `Facet = IsSearchableFacet`, `IgnoreOnNewVersion`, `NodeTypeKeys` aus `PropertyDefinitionNodeType`, `InheritToChildren` aus `PropertyNodeTypeInheritance`, Name/Description aus `Translation` |
| `PropertyListValue` | `PropertyListValue` | `Key` = Identifier (slug), `Name` aus `Translation` oder typisierter Wert, `SortIndex = SortKey`, `IsActive = Status==1 && !IsDeleted` |
| `Property` + `PropertyValue` | `Node.Metadata[key]` | je Wert ein `MetadataValue`: `list` = Key des Listenwerts, sonst `v` aus der gefüllten Typspalte (Date `yyyy-MM-dd`, DateTime ISO 8601 UTC, Decimal als Zahl, Duration-Ticks → ISO 8601 Dauer); `TranslationIdentifier` gesetzt → ein Wert je Sprache aus `Translation` mit `lang` = Sprachcode; `UserContextId` → `ctx` = UserContext-Key. Gelöschte Properties/Werte (915/1 108) und Edge-Properties (0) entfallen |
| Werte auf **Version**-Nodes | `Asset.Metadata` / `AssetVersion.MetadataSnapshot` | In Sharedien hängen Asset-Metadaten an der Version (13 212 Nicht-System-Werte auf Versionen, **0 auf AssetSets**): Werte der aktuellen Version → `Asset.Metadata`, ältere Versionen → `MetadataSnapshot` (JSON wie `Node.Metadata`) |
| `AssetVersion` + `AssetDerivation(Original)` + Blob | `AssetVersion` | `Number = Version`, `FileName = OriginalFilename`, `Extension`, `MimeType` aus `FileType`, `Size` = Bytes der Kopie, `Sha256` beim Kopieren berechnet (Sharedien hat nur MD5-Base64), `BlobKey = BlobKeys.New(tenant)`, `CreatedAt = Created` |
| manuelle Derivate | `Derivation` (`IsManual = true`) | Derivate von Typen mit `AllowManualUpload` ohne `IsDefaultDerivation` (Heuristik, siehe §6) werden mitkopiert |
| `DerivationType` | `DerivationType` | System-Typen Thumbnail/Screen/Print → Shareme-Kinds; Custom-Typen als Mandanten-Rendition-Typ mit Standardgröße, `IsDefault`/`IsOnDemand`/`AllowManualUpload` aus `AssetTypeDerivationType` |
| `License` / `RightsOwner` / `BillingContact` | DRM-Entitäten | Felder aus den Fachtabellen + System-Properties (`license_rights` → `Rights`, `license_period_*` → `ValidFrom/Until`, `contact_*` → `Address`, `banking_iban/swift` → `Iban/Bic`, `rights_owner_type` → `Kind`); `Status` → `IsActive`/`LicenseStatuses` |
| `PriceList*` / `Buyout` / `UserContextGroup` | `PriceList` / `Buyout` / `ContextGroup` | Währung als Code aus `Currency.ShortName` |
| `Rule*` | `Rule` | `IsActive = false` immer; Trigger→Conditions, Actions mit Template-Sprache (`fnc.find`, `findInAscendants`, `increment`) nur soweit abbildbar, sonst Regel leer + Hinweis im Namen und Bericht |
| `WatermarkDefinition`/`Trigger` + `FileReference` | `WatermarkDefinition` | Bild kopiert, `TriggerProperty/Value` aus Trigger-Listenwert |
| `NodeActivityLogSettings` | `NodeSubscription` | nur `SendToInbox || SendEmail` |

### 2.3 Querschnitt

- **Zeit**: Quelle hat ausschließlich `datetime2` ohne Offset (0 `datetimeoffset`-Spalten); Defaults sind `getutcdate()` → als UTC lesen (`DateTimeKind.Utc` → `DateTimeOffset` +00:00). Fachliche DateTime-Werte in `PropertyValue` ebenfalls als UTC, übersteuerbar mit `--value-timezone`.
- **Audit-Felder**: `CreateUser/MutUser` sind Strings (User-Identifier oder UserName) → per Lookup auf Ziel-User, sonst `null`; `CreatedAt/ModifiedAt` aus `CreateDate/MutDate`.
- **Mehrsprachigkeit**: `Translation` wird einmal komplett gelesen (17 877) und als Dictionary `(ObjectType, TranslationIdentifier, FieldName) → {lang: text}` gehalten; Default-Sprache (`Language.IsDefaultLanguage`) liefert `Name`, alle Sprachen `Names`.
- **Enums** werden in einer statischen Klasse `SharedienEnums` aus der Enum-Liste in `sharedien-backend.md` codiert, unbekannte Werte → Problem im Bericht, nie stillschweigend.

## 3. Architektur

### 3.1 Projekt

`src/Shareme.Importer` – Konsolen-App (.NET 10, `System.CommandLine`), referenziert `Shareme.Infrastructure` (DbContext, Module) und `Coworkee.Storage`; liest SQL Server mit `Microsoft.Data.SqlClient` (+ Dapper, nur lesend, `ApplicationIntent=ReadOnly`). Läuft per Aspire-AppHost-Ressource (explizit gestartet) oder standalone mit Connection-Strings.

```
shareme-import plan    --source <sql> --target <pg> [--tenant <id>]        # Dry-run: liest, mappt, validiert, schreibt nichts
shareme-import run     --source <sql> --target <pg> --storage <cfg> [--phase <name>] [--include-deleted]
shareme-import verify  --source <sql> --target <pg>                         # Zählvergleich je Tabelle
shareme-import post    --target <pg>                                        # Reindex + Renditions anstoßen
```

### 3.2 Schreibweg: Bulk statt Dispatcher

| Weg | Pro | Contra |
|---|---|---|
| Dispatcher-Commands | volle Validierung, Rechte, Events | ~250 000 Einzelaufrufe; `AuditablePropertiesInterceptor` überschreibt `CreatedAt` mit „jetzt“; AuditTrail-Flut (ein `AuditEntry` je Zeile); Outbox-Events lösen je Node Indexierung und je Version Rendering aus, unkontrolliert parallel; braucht einen angemeldeten User mit allen Rechten |
| DbContext **ohne Interceptors** | einfach, Zeitstempel bleiben, keine Seiteneffekte, Spalten aus dem Modell | Validierung muss im Importer passieren; langsamer als COPY |
| Npgsql Binary COPY | am schnellsten | eigener Writer für Konverter, jsonb, TPH – erst bei Bedarf |

Entscheidung: **eigener `ShareMeDbContext` ohne Interceptors** (Optionen ohne `AddInterceptors`, Modell aus denselben `IModelContributor`s): `AddRange` + `SaveChanges` in Batches, historische Zeitstempel bleiben, kein AuditTrail, keine Outbox-Events, Spalten und Konverter kommen automatisch aus dem EF-Modell. Bei ≈ 250 000 Zeilen reicht das (Minuten); ein Npgsql-COPY-Writer folgt nur, wenn ein Produktivlauf zu langsam ist. Ausnahmen über Services: Mandant anlegen (Coworkee-Setup-Logik), User über `UserManager<User>` (Normalisierung, SecurityStamp; danach `CreatedAt` per Update korrigiert). Validierung durch dieselben Regeln wie die API: der Wertecheck aus `NodeMetadataHandlers.Check/CheckValue` und `CatalogErrors.Key` werden als `public static` Klasse `MetadataRules` in `Shareme.Metadata` herausgezogen (kleiner Refactor, keine Verhaltensänderung) und vom Importer aufgerufen. Ungültige Werte werden nicht geschrieben, sondern berichtet.

Die Zieldatenbank muss migriert und leer für die betroffenen Mandanten sein (Ausnahme: Seed-Typen, Root-Folder); der Importer prüft das vor Phase 1.

### 3.3 Phasen und Checkpoints

Jede Phase liest vollständig, mappt in Speicher (Größenordnung < 1 Mio Zeilen, passt), validiert, schreibt in **einer Transaktion je Phase** und trägt sich in `import.Checkpoint (Phase, StartedAt, FinishedAt, SourceCount, WrittenCount, Problems)` ein. Ein Abbruch verliert nur die laufende Phase.

1. `catalog` – Mandant, Sprachen, UserContexts, ContextGroups, NodeTypes, EdgeTypes, PropertyGroups/-Definitions/-ListValues, PropertySets, DerivationTypes, FileNaming
2. `identity` – Users, UserLogins, Profile, Groups, Members, Roles, PermissionGrants, GroupRoles
3. `tree` – Nodes (Folder, Asset, Custom, Workspace, Task) in Pfadreihenfolge (Path/Depth aus der ParentChild-Topologie berechnet, Zyklen → Bericht), WorkflowStates, Edges, Comments, Subscriptions
4. `metadata` – `Node.Metadata`, `MetadataSnapshot`
5. `permissions` – ResourcePermissions, Restrictions, WorkspaceRoleBindings
6. `drm` – BillingContacts, RightsOwners, PriceLists, Licenses, LicenseAssets, Buyouts
7. `automation` – Rules (inaktiv), Watermarks
8. `blobs` – Dateikopie (eigene Schleife, siehe 3.5), schreibt AssetVersion-Zeilen erst nach erfolgreicher Kopie
9. `post` – Reindex und Renditions (siehe 3.6)

**Idempotenz**: jede Zeile wird über `import.IdMap` zugeordnet; Ziel-Ids sind deterministisch (2.1), deshalb ist ein erneuter Lauf ein Upsert (vorhandene Ids laden, neue einfügen, geänderte aktualisieren). `IdMap.Hash` (SHA-256 der gemappten Zeile) erlaubt, unveränderte Zeilen zu überspringen – so taugt der Importer auch für einen **Delta-Lauf** kurz vor der Umschaltung. Blobs werden nur kopiert, wenn `IdMap` für die Version noch keinen BlobKey hat.

### 3.4 Dry-run und Bericht

`plan` führt Phasen 1–7 ohne Schreiben aus und erzeugt `import-report.json` + Konsolen-Zusammenfassung: Zähler je Quelle/Ziel/verworfen, Probleme gruppiert nach Code (`unknown_enum`, `invalid_value`, `orphan_edge`, `multi_parent`, `duplicate_identifier`, `unmapped_permission`, `missing_blob`, `email_collision` …) mit **Quell-PKs, nie Inhalten** (Namen, E-Mails, Werte stehen nicht im Bericht). Exit-Code ≠ 0 bei Problemen über einem Schwellwert (`--max-problems`).

### 3.5 Blob-Kopie

Altes Storage laut [sharedien-services.md](../../analysis/sharedien-services.md) §4.6: genau ein aktiver Provider je Installation – **Azure Blob Storage** (Azure-Deployments; Blob-Name = `DataId`/`DataIdentifier`, Container aus `StorageProviders.AzureBlobStorageProviders[].ContainerName`) oder **StorageHandler mit FileSystem** (On-Premise; Pfad `StorageRootPath/<id[0..2]>/<id[0..4]>/<id>`). Die Analyse-DB ist ein Azure-SQL-Export → vermutlich Azure Blob. Die Zuordnung Datei ↔ Asset steht in `MAM.AssetDerivation.DataId` (38 628 GUIDs, 643 leer), nicht in `STH.Data`: dort liegen in dieser DB nur 62 488 Altzeilen, davon passen nur 3 666 zu Derivaten (STH lebt produktiv in eigener DB `Sharedien3StorageHandler`).

Importer-Quelle `ISourceBlobs` mit zwei Implementierungen (`AzureBlobSource` über `Azure.Storage.Blobs`, `FileSystemSource` über den Pfad-Algorithmus) – ein Interface ist hier gerechtfertigt, weil beide Quellen real vorkommen. Ziel ist `IBlobStorage` aus Coworkee (FileSystem/S3/AzureBlob je Shareme-Konfiguration), Key `BlobKeys.New(tenantId, clock)`.

Ablauf je Version: Stream lesen → durch `SHA256` + Zähler → `PutAsync` → MD5 aus dem Stream gegen `AssetDerivation.FileHash` prüfen (Abweichung = Problem, Datei trotzdem übernommen) → AssetVersion-Zeile schreiben. Parallelität konfigurierbar (`--blob-parallelism`, Standard 8), Fortschritt je 100 Dateien in `import.Checkpoint`. Fehlende Blobs: Version wird ohne Datei nicht angelegt, Asset bekommt `Status = failed`, Bericht `missing_blob`. Größe in der Demo-DB ≈ 34,6 GB Originale.

### 3.6 Nachlauf

- **Suche**: je Mandant `StartReindex` (vorhandener `ReindexJob`, schreibt alle Nodes und entfernt Altdokumente).
- **Renditions**: für jede importierte aktuelle Version `DerivationsRequested`/`PlanDerivations`-Logik anstoßen – gedrosselt (Batches über die vorhandene Processing-Queue, `--render-batch`), Thumbnails zuerst. Ältere Versionen nur on demand. Der Render-Job füllt dabei `TechnicalMetadata` neu.
- **Personen-Directory/Avatare**: nichts nötig, Profile sind geschrieben.

### 3.7 Benutzer und Anmeldung

Keine Passwort-Migration (Sharedien: PBKDF2 mit eigenem Salt-Format; Übernahme würde einen zweiten Hasher erfordern und alte, evtl. schwache Passwörter konservieren).

- **Lokale User** (179): importiert ohne Passwort. Nach Freigabe der Umschaltung sendet `shareme-import notify` je aktivem User `SendPasswordReset` (Coworkee.Account, Mail mit Reset-Link „Konto einrichten“), gedrosselt; ohne `notify` bleibt der Weg „Passwort vergessen“ im AuthServer.
- **Externe User** (13): `UserLogins`-Eintrag aus `ProviderName`/`ProviderSubjectId` über `--login-provider-map sharedien-name=shareme-scheme`. Externe Login-Provider sind in Coworkee noch nicht konfiguriert (eigener Plan) – bis dahin gelten sie wie lokale User. Achtung: bei Azure AD ist `sub` je App-Registrierung verschieden; stabil ist `oid` → Mapping ggf. per E-Mail beim ersten Login statt per Key (offene Frage).
- E-Mail-Kollisionen (gleiche Person lokal + extern, eindeutiger Schlüssel in Sharedien ist `UserName+ProviderName`): zusammenführen auf einen User, Zweitkonto nur in `IdMap` auf dieselbe Ziel-Id, Bericht.

## 4. Tests

- **Mapper-Unit-Tests** (`tests/Shareme.Importer.Tests`): reine Funktionen Quellzeile → Zielobjekt mit synthetischen Zeilen: Id-Regel (GUID behalten, UUIDv5 stabil, Kollision), alle Enum-Tabellen inkl. unbekannter Werte, PropertyValue → `MetadataValue` je Typ (Duration-Ticks, Decimal, Datum UTC, Listenwert, Übersetzungen, Kontext), Versions-Metadaten → Asset/Snapshot, ParentChild → Pfad (Multi-Parent, Zyklus, Wurzelkategorie), PermissionMap, Workspace/State/Task-Zuordnung, FileSystem-Pfad-Algorithmus.
- **Integrationstest** mit `Testcontainers.MsSql` + dem bestehenden Postgres-Testcontainer: ein Mini-Schema (DDL-Teilmenge aus `sharedien-db-schema.md`: Tenant, User, UserGroup(+User), Role(+Permission), Node, NodeType, Edge, EdgeType, Category, AssetSet, AssetVersion, AssetDerivation, DerivationType, FileType, PropertyDefinition, PropertyListValue, Property, PropertyValue, Translation, Language, NodePermission, Workspace, State, Task) mit ~50 synthetischen Zeilen und Blobs im FileSystem-Layout. Prüft: Zählungen, Baum/Pfade, Rechte inkl. InterruptInheritance (über `IPermissionChecker` als importierter User), Metadaten über die normale API lesbar, Blob-Hash, zweiter Lauf schreibt 0 Zeilen, `plan` schreibt nichts.
- **Lokaler Verifikationslauf** gegen `Sharedien3Analysis` (kein CI, manuell, Dokumentation im Plan): `plan` + `run` (ohne Blobs, `--skip-blobs`, da die Dateien lokal fehlen) + `verify`. Ausgaben (`import-report.json`, Logs) liegen unter einem in `.gitignore` eingetragenen Pfad außerhalb des Repos und werden **nie committet**; im Repo landen höchstens aggregierte Zähler.

## 5. Umsetzungsschnitt (Plan-Vorschlag)

**Stand (umgesetzt):** `shareme-import plan|run --source-tenant <id> --tenant <guid>`, Verbindungen nur über `SHAREME_IMPORT_SOURCE`/`SHAREME_IMPORT_TARGET`. Übernommen: Benutzer (ohne Passwort, gleiche E-Mail → ein User), Profile, Gruppen mit Mitgliedern und Mandantenrollen, Rollen mit Rechten (`PermissionMap`, Sharedien-Enum → Shareme-Recht, Lücken im Bericht), Knotenrechte, Sprachen (vorhandene Codes bleiben, höchstens ein Default), Knoten- und Kantentypen, Property-Gruppen/-Definitionen/-Listenwerte (Version-Typen zählen als Asset), Baum (Ordner, Assets ohne Dateien, Custom Nodes), Kanten und Metadaten (volle API-Prüfung über `MetadataRules`). Regeln:
- Ein Lauf ist **eine Transaktion**; abgeleitete Ids enthalten den Zielmandanten, eine übernommene Quell-GUID darf nur in einem Mandanten leben (sonst Abbruch); vorhandene Keys werden wiederverwendet, ein zweiter Lauf schreibt nichts.
- Knoten, deren erster Elternknoten in eine spätere Phase gehört (Workspace, Task), bleiben samt Teilbaum draußen (`deferred_parent`), statt unter der Wurzel für alle sichtbar zu werden. Variant-Kanten warten auf die Dateiphase (sonst markiert der Processing-Sweep Assets als fehlgeschlagen).
- Lokaler Lauf gegen die Analyse-DB: ≈ 9 200 Knoten, 118 User, 88 Rollen, 602 Grants, 141 Knotenrechte in ≈ 13 s; übrig nur echte Datenfehler (12 ungültige URLs, 13 Einzelwerte).
- **Identität:** nur Gruppen mit Rolle im Quellmandanten oder Knotenrecht darin und deren Mitglieder; nie bestätigte Registrierungen bleiben draußen. Gleicher Benutzername in einem anderen Mandanten → nicht übernommen (`user_in_other_tenant`). Externe Konten kommen inaktiv (Shareme hat noch keine externe Anmeldung; ein Passwort würde den IdP umgehen). Sperre nach Fehlversuchen aktiv wie bei normal angelegten Usern.
- **Rollen:** Sharedien gibt Knotenzugriff nur über Knotenzuweisungen. Jede Rolle wird deshalb geteilt: die Mandantenrolle (für `UserGroupToRoleMap`) ohne Knotenrechte, die Rolle „(items)" mit Knotenrechten (`Nodes.*`, `Assets.Download`) nur für Knotenzuweisungen. GlobalAdmin → Systemrolle `admin`. Rechte ohne Gegenstück (Wasserzeichen, Dateinamen, Derivat- und Kantentypen …) und persönliche Property-Sets geben nichts.
- **Knotenrechte:** `NodePermission.Status` 0 und 1 werden beide übernommen: beide sind in `MaterializedNodePermission` materialisiert, also in Sharedien wirksam. Zugriff, den Sharedien über Link-Kanten (zweiter Ort) vererbt, entfällt: Shareme vererbt nur entlang des Baums.
- **Nach dem Lauf:** Shareme neu starten oder fünf Minuten warten (Rechte-Cache), Reindex starten (Hinweis in der Ausgabe). `--max-problems` verhindert das Schreiben, wenn mehr Probleme gefunden werden.
- **Nur Einfügen:** bis Upsert (Id + Hash, `import.IdMap`) umgesetzt ist, darf ein Lauf nur gegen ein Wegwerf- oder frisches Ziel gehen; spätere Phasen müssen Status, Eltern (Workspaces) und Kataloge aktualisieren können.
- **UserContexts und Property-Restriktionen** übernommen: Werte ohne Kontext an Kontext-Properties gehen an den Standardkontext; Lese-/Schreibverbote von Rollen werden zu `hiddenProperty`/`readOnlyProperty` der Mandantenrolle. Shareme wendet Restriktionen nur über Mandantenrollen an: Verbote an reinen Knotenrollen werden gemeldet (`restriction_on_item_role`), GlobalAdmin-Rollen bleiben ohne (in Sharedien umgehen sie alle Prüfungen).
- **Workspaces** übernommen (Sharedien hängt alle Workspaces unter einen State eines System-Workspace; dieser Container bleibt draußen, Workspaces sind immer Wurzeln; genau eine Startspalte je Board, States ohne Ausgang bei vorhandenem Workflow sind Erledigt-Spalten; Workspaces mit Tasks werden Kollaboration): Workspace als eigene Wurzel (Art, Vorlage, Abschlussordner, Metadaten-Kopie), States als `WorkflowState` mit Übergängen, Tasks direkt im Workspace mit State, Zuständigem und Erledigt-Zeitpunkt, Dateien eines Tasks im Workspace plus Link vom Task, Kommentare mit Autor. Sharedien-Systemrollen `workspaceOwner`/`collectionOwner`/`workspaceCollaborator` werden zu den Workspace-Rollen von Shareme (bei Bedarf angelegt). Lokaler Lauf: 10 554 Knoten, 1 070 States, 158 Kommentare, 641 Knotenrechte in ≈ 11 s.
- **Dateien** übernommen (`--blobs-folder <StorageHandler-Wurzel>` oder `--blobs-container <Azure-Container>` mit `SHAREME_IMPORT_BLOBS`): je Sharedien-Version das Original-Derivat, vor der Transaktion ins Shareme-Storage kopiert (SHA-256 beim Kopieren, MD5 gegen die Quelle → `checksum_mismatch`, fehlende Datei → `missing_blob`, aktuelles Asset dann `failed`); Version nur bei angekommener Datei. Der Blob-Schlüssel folgt der Versions-Id (ein erneuter Lauf überschreibt statt neu zu kopieren), eine unlesbare Datei wird einmal wiederholt und dann gemeldet. Dateien kommen nur zusammen mit ihren Assets (sonst `asset_imported_without_files`). Nach dem Commit plant der Importer die Renditions der aktuellen Versionen (`Derivation`-Zeilen) und schickt `DerivationsRequested` über die Outbox – keine Automationsregeln laufen für Altbestand; ein weiterer Lauf holt Fehlendes nach. Variant-Kanten kommen nur in Läufen mit Dateien. Ältere Versionen ohne Metadaten-Snapshot (offen).
- **DRM** übernommen: Rechnungskontakte, Rechteinhaber, Lizenzen (Rechte aus `license_rights`, Zeitraum, Status) mit lizenzierten Assets aus LicenseDrm-Kanten, Kontextgruppen und Preislisten (nur die von Buyouts des Mandanten genutzten), Buyouts mit Währung; Buyouts mit Rechteinhaber anderer Mandanten werden gemeldet.
- **Rendition-Typen** übernommen: Thumbnail/Screen/Thumbnail_Animated → Shareme-Kinds `thumbnail`/`preview`/`animated`, `Original` bleibt die Datei; alle anderen (auch `Print`) als Mandanten-Rendition-Typ (WebP, Größe aus dem `-resize`/`-thumbnail`-Argument des ImageMagick-Jobs, sonst 1600 px), `IsDefault`/`IsOnDemand`/`AllowManualUpload` und Asset-Typen aus `AssetTypeDerivationType`. Leserechts-Sperren (`DerivationTypeRestriction` Action Read/View/Any) → `blockedRendition` auf der Mandantenrolle; Sperren aufs Original → `restriction_on_original` (regelt das Download-Recht), Admin-Rollen und Create/Update/Delete-Sperren übersprungen.
- **Listenwert-Sperren** übernommen: Sharedien hält den gesperrten Listenwert an einer Property ohne Knoten (`PropertyValueRestriction` → `Property` → `PropertyValue`); Leserechts-Sperren werden `hiddenListValue` (Definition + Listenwert) auf der Mandantenrolle, Änderungs-Sperren übersprungen. Alle Restriktionsarten teilen die Rollenregeln (Item-Rollen → `restriction_on_item_role`/`restriction_not_on_item_role`, GlobalAdmin übersprungen).
- **Regeln** übernommen, immer inaktiv: Knotentypen, Filter/Trigger (Operatoren 1:1, `IsNewCategory` → `isNewNode`, Vergleichswert aus der knotenlosen Property so gerendert wie Shareme vergleicht: Listen-Key, Text, invariante Zahl, `true`/`false`, ISO-Datum), Aktionen mit Template (gleiche Sprache; `find`/`findInAscendants`-Argumente von Sharedien-Identifier auf Shareme-Key umgeschrieben; leeres Template leert den Wert wie in Sharedien). Nicht abbildbar – `HasRelationType`, verkettete Funktionen (`increment`, `write`, `regexExtract` am Input), Ziele mit Sprache/Kontext – wird weggelassen, gemeldet (`rule_condition_not_mapped`/`rule_action_not_mapped`) und die Regel heißt „… (import incomplete)".
- Offen: Translation-Schlüssel mit `ObjectType` (6 Kollisionen), Katalog nur auf genutzte Definitionen beschränken.
- **Suche**: nach dem Commit veröffentlicht der Importer `ReindexRequested` am Root-Ordner (Outbox), das Search-Modul stellt einen Rebuild des Mandanten ein; schlägt das fehl → `reindex_not_requested`, Rebuild per Hand (Admin › Suche).

1. `MetadataRules`-Refactor + Importer-Gerüst (CLI, Quellleser, IdMap, Import-Kontext, Bericht) + Katalog-Phase.
2. Identity + Tree + Metadata.
3. Permissions, Workspaces, DRM, Automation.
4. Blobs + Nachlauf + `verify` + `notify`.

## 6. Offene Fragen und Risiken

| # | Thema | Vorschlag / Risiko |
|---|---|---|
| 1 | **Echte Quell-DB/Storage**: die Analyse-DB ist von 2021 und eine Demo-Instanz; Produktivbestand, Storage-Typ und Zugangsdaten sind unbekannt | Vor dem Plan klären; Größe skaliert v. a. mit Blobs (Demo 34,6 GB), DB-Teil ist klein |
| 2 | **Mandanten**: 35 Tenants, aber nur einer mit Nodes; User/Gruppen sind in Sharedien mandantenlos, in Coworkee mandantengebunden; 7 Gruppen haben Rollen in mehreren Mandanten | Ein Lauf = ein Quellmandant → ein Zielmandant; leere Mandanten nicht anlegen; mehrfach genutzte Gruppen je Zielmandant duplizieren |
| 3 | **PermissionMap**: 295 Permission-Ints auf ~40 Shareme-Rechte; feine Unterschiede (z. B. Property-Rechte je Objekttyp) gehen verloren | Tabelle mit Fachseite abnehmen; Custom-Rollen nach Import sichten |
| 4 | **Manuelle Derivate** sind in `AssetDerivation` nicht markiert | Heuristik über `AssetTypeDerivationType` (manuell erlaubt, nicht Default); alternativ alle Nicht-System-Derivate kopieren (+ ≈ 8 GB) |
| 5 | **Shares** (Links an Externe): Shareme speichert nur Token-Hashes | Nur gültige Shares übernehmen, alten Token hashen und alte URL per Redirect auf neue Route abbilden – oder bewusst neu versenden (Vorschlag: nicht migrieren) |
| 6 | **SavedSearches/EntityPermissions**: JSON-Format inkompatibel | nicht migrieren; Liste je Owner im Bericht, damit Nutzer neu anlegen |
| 7 | Rules mit Template-Sprache sind nicht 1:1 abbildbar | inaktiv importieren, manuell nachziehen |
| 8 | Zeitzone fachlicher DateTime-Werte unklar (API speicherte vermutlich UTC) | Stichprobe mit Fachseite, sonst `--value-timezone` |
| 9 | Externe Logins: `ProviderSubjectId` evtl. nicht der Claim, den Shareme später nutzt | Fallback-Verknüpfung per verifizierter E-Mail beim ersten Login |
| 10 | Gelöschte Nodes (2 606) und Papierkorb | Standard nicht übernehmen; `--include-deleted` für Aufbewahrungspflichten |
| 11 | Datenschutz: Quelle enthält personenbezogene Daten | Importer loggt nur PKs/Zähler; Bericht ohne Inhalte; Quell-DB nur lesend, Zugang zeitlich begrenzt |
| 12 | Renditions für ~8 000+ Assets erzeugen Last | gedrosselt, Thumbnails zuerst; Umschaltung erst nach Thumbnail-Abschluss |
| 13 | Alte URLs/Integrationen (Identifier in Links, Event-Stream-Konsumenten) | GUID-Identifier bleiben Ids; Redirect-Tabelle aus `IdMap` für nicht-GUID-Identifier optional |
