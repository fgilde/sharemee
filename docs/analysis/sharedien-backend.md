# Sharedien Backend – Feature- und Domänenanalyse

Analysiert: `src/Backend`, `src/Backend.Contracts`, `src/Backend.Common` des Sharedien-3.0-Repositories (Stand des Quellcodes: letzte Migration `20210804_0280`, API-Version V6).
Ziel: lückenloses Inventar der Backend-Funktionalität als Grundlage für "Shareme".

Konventionen in diesem Dokument:

- Technische Begriffe bleiben englisch (Node, Edge, AssetSet, PropertyDefinition …).
- "unklar" markiert Stellen, an denen der Code keine eindeutige Aussage erlaubt.
- Zahlen (Entities, Endpoints) wurden per Skript aus dem Code extrahiert, nicht geschätzt.

Kennzahlen (extrahiert):

| Metrik | Wert |
|---|---|
| Controller (inkl. 7 partial-Dateien des DRM-Controllers) | 43 Dateien / 37 logische Controller |
| HTTP-Endpoints (Methoden mit `[Http*]`-Attribut, inkl. versionierter Duplikate und obsoleter Endpunkte) | 463 |
| DB-Tabellen/Views laut `Sharedien.Common.DbScheme` | 124 (Schemas `Admin` und `MAM`) |
| DbUp-Migrationsskripte | 280 |
| Business-Objects (Core `Models/BusinessObjects`, ohne Mapping/CQRS) | ~300 Klassen |
| Service-Interfaces (Core `Contracts/Services`) | 126 |
| Activities (Use-Case-Klassen, Core `Activities`) | ~330 |
| DTO-Klassen in `Sharedien.Api.Contracts` | 518 Dateien |
| Enums (Core + Contracts + Common) | 162 |
| FluentValidation-Validatoren | 190 |
| Hangfire-Job-Typen (`BackgroundJobType`) | 17 |
| Permissions (`Permission`-Enum) | 311 |

---

## 1. Projektstruktur und Verantwortlichkeiten

Alle Projekte: .NET Core 3.1 (Contracts/DbScheme: netstandard2.0, FileTypeChecker: netstandard2.1), C# 8, Firma "Advellence Solutions AG".

### 1.1 Backend (`src/Backend`)

| Projekt | Typ | Verantwortung |
|---|---|---|
| `Sharedien.Api` | ASP.NET Core Web-API (Kestrel) | Host, `Startup`/`Program`, 43 Controller-Dateien, Hangfire-Setup (`Setup/`), Multipart/Streaming-Helfer (`Controllers/Helpers`), `appsettings.yml` (vollständige Konfigurationsoberfläche), Dockerfile, App-Service-Jobs (`App_Data/jobs/triggered/CleanupLogFiles`). Führt beim Start die DB-Migration aus, initialisiert Caches (Watermark, Rules, FileRecognition) und Message-Broker-Verbindungen. |
| `Sharedien.Api.Core` | Class Library | Fachkern: Business-Objects, CQRS-Kommandos (`*/CQRS`), Service-Interfaces (`Contracts/Services`), Repository-Interfaces (`Contracts/Repositories`), Service-Implementierungen (`Services`), Use-Case-Klassen (`Activities`, ~330), UnitOfWork-Contracts, E-Mail-Templating (DotLiquid-Filter), Export/Import (CSV, XLS/XLSX via NPOI/CsvHelper), Konfigurationsmodelle. Enthält **keine** Persistenz. |
| `Sharedien.Api.Contracts` (in `src/Backend.Contracts`) | netstandard2.0 Library | Alle öffentlichen DTOs: Webservice-DTOs (`Public/Webservice`, 400+), Upload-DTOs, Image-Derivation-DTOs, Message-Queue-Contracts (`Public/MessageQueue/V1`, `V2`: EventStream-Events, Processing-Job-Messages, MetadataExtraction), SignalR-Contracts. Wird an Drittprojekte (Frontend, Processing, EventHub, Search) weitergegeben. |
| `Sharedien.Api.Dto` | – | **Existiert nicht.** Die DTO-Rolle übernimmt `Sharedien.Api.Contracts`. |
| `Sharedien.Api.Infrastructure` | Class Library | Technische Implementierungen: Autofac-DI-Module (`DependencyInjection/*Module`), UnitOfWork-Implementierung (Dapper/SqlConnection, AsyncLocal), AutoMapper-Profile, Hangfire-Background-Jobs (`ProcessingCallbackJob`, `EventHubCallbackJob`), Queue-Producer (`ProcessingQueueProducer`, `SearchSyncQueueProducer`), `EventStagingService` (transaktionales Outbox-Pattern), Upload-Services, Search-Services (Adapter auf Elasticsearch-Projekt), LDAP, File-Recognition, Contact-Print-PDF (PdfSharpCore), 190 FluentValidation-Validatoren (`Validation/`), AI-Provider-Mocks. |
| `Sharedien.Api.Infrastructure.SqlServer` | Class Library | Dapper-Repositories (60+) mit handgeschriebenem T-SQL, DAL-Modelle (`DbModels`), Hangfire-SqlServer-Storage, `HangfireBackgroundJobRepository`, Notification-Template-Ressourcen (`.resx` de/en). Keine EF-Nutzung mehr (Readme-Hinweis auf EF ist veraltet – unklar, ob Rest-Artefakte existieren; im Projekt sind keine EF-Pakete referenziert). |
| `Sharedien.Api.Infrastructure.Mock` | Class Library | `ServiceMocks/MockFactory.cs` – Mocks für Tests. |
| `Sharedien.Api.DbMigration` | Konsole + Library | DbUp-basierte Migration (280 SQL-Skripte als EmbeddedResource, Journal-Tabelle `dbo.SchemaVersionsSharedien`, App-Lock `api_db_migration`), `DbSetupScripts/SetupSqlServerDb.sql|SetupAzureDb.sql`. Wird von `Sharedien.Api.Startup` beim Start aufgerufen (`MigrationHelper.ExecuteDbMigration`). |
| `Sharedien.Api.Client` | Class Library | NSwag-generierter, typisierter C#-Client (`Generated/ClientGenerated.cs`, `ClientBase`, `ClientExtended`), `PartitionedUploader`/`StreamPartition` für Chunk-Upload, `ClientAuthenticationConfiguration` (Client-Credentials gegen Identity). Regeneriert per `NSwag.MSBuild`, wenn `Sharedien.Api.dll` neuer ist. |
| `Sharedien.Client` | Class Library | Handgeschriebener, schlanker HTTP-Client (`SharedienClient : HttpClientBase`): Chunk-Upload-Session, Download-Link, Zip-Download, Kategorie anlegen/prüfen, Kategorie-Baum, Metadata-Import starten, Watermark-Upload, Info. Für Integrationsszenarien/Scripts. |
| `Sharedien.Api.WindowsService` | Exe | Hostet die API als Windows-Dienst (HttpSys), inkl. `install.bat/uninstall.bat/update.bat`. |
| `Sharedien.Api.Common.SchemaGenerator` | Test-Projekt | Erzeugt JSON-Schemas (NJsonSchema) aus Contracts. |
| `Sharedien.Api.Tests` / `.Tests.Integration` / `.Tests.Load` | xUnit | Unit-Tests (Activities, Authorization, Repositories, Validators, UoW, Serializer …), Integrationstests (CRUD, Scenarios, AI, ContactPrint, Export, FileRecognition, Identity, Storage) mit eigener `appsettings.IntegrationTests.yml`, Lasttests. |
| `EventConsumers/SampleEventConsumer` | Exe | Beispiel-Konsument für den EventStream (AMQP-Receiver, schreibt `messageLog.txt`), Dockerfile. |
| `EventConsumers/TestMessageSender|Receiver` | Exe | Test-Tools für den Message-Broker. |
| `Testclient` | Exe | Kleiner Test-Client. |

### 1.2 Backend.Common (`src/Backend.Common`) – gemeinsam genutzte Bibliotheken

| Projekt | Verantwortung |
|---|---|
| `Sharedien.Common` | Basis-Infrastruktur für alle Sharedien-Services: Kestrel-/HttpSys-Startup, YAML-Konfiguration (`NetEscapades.Configuration.Yaml`), **SQL-Konfigurationsprovider** (`SqlConfigurationProvider` – Settings aus DB-Tabelle `Admin.Configuration`), Serilog-Logging (Console/File/Http/AppInsights, Log-Sanitizing), Authentifizierung (`SharedienAuthenticationHandler`, `TokenValidationService`, `IdentityTokenService` = Token-Introspection gegen Identity-Service), OpenID-Connect-Setup, Swagger-Setup mit API-Versioning, Middleware (OperationId-Header, ForwardedHeaders-Override, Request/Response-Logging), `BaseController` (Response→HTTP-Mapping), FluentValidation-Basis, AutoMapper-Basis, Generic `Response<T>`/`Error`/`ErrorType`, `BackgroundJobBase`, Caches (`ExpiringMemoryCache`, `ExpiringTokenCache`), Paging, TempFile, Identifier-Generator, Konfigurationsmodelle für Identity/Processing/StorageHandler/EventHub (geteilt mit anderen Services), AI-Abstraktionen (`IArtificialIntelligenceProvider`, `ExtractionRequest/Result`), `SignedUrlProvider` (HMAC-signierte URLs), Resources (`Texts`, `ErrorNames`). |
| `Sharedien.Common.DbScheme` | Konstanten für alle Tabellen/Views/Spalten (`Admin.cs`, `Mam.cs`) – Single Source of Truth für SQL in Repositories. |
| `Sharedien.Common.Infrastructure.Queue` | Message-Broker-Abstraktion (`IMessageSender`, `IMessageReceiver`, `IMessageBrokerProvider`) mit zwei Implementierungen: **AMQP 1.0** (`AMQPNetLite`, ActiveMQ) und **Azure Service Bus**; JSON-Serializer mit `JsonSubTypes`-Typregistrierung; `InternalCallbackJob`/`InternalEventProducer` für interne Events (z. B. `ApplicationSettingsChangedEvent`). |
| `Sharedien.Common.Infrastructure.Storage` | `IStorageProvider` mit Implementierungen **FileSystem**, **Azure Blob** (`Azure.Storage.Blobs`), **StorageHandler** (eigener Sharedien-Storage-Service mit signierten URLs); Zip-Provider (Native, SharpZipLib); Hash-Streams; `StorageFileInfo`. |
| `Sharedien.Common.Infrastructure.Mail` | `EmailSender` (MailKit/MimeKit SMTP), `SmtpClientFactory`. |
| `Sharedien.Common.Infrastructure.AI` | AI-Provider für Tag-Extraktion: **Clarifai** (gRPC, inkl. Custom-Models), **Azure Cognitive Services Computer Vision**, **Imagga** (REST). |
| `Sharedien.Common.Infrastructure.FileTypeChecker` | Magic-Number-basierte Dateityp-Erkennung (exact/fuzzy/range/OfficeOpenXml-Matcher, Signatures). |
| `Sharedien.Common.Testing` / `.Tests` | Test-Helfer. |
| `StaticFiles`, `TestClient.*Startup` | Beispiel-/Test-Hosts (Kestrel, HttpSys, WindowsService, StaticFiles). |

### 1.3 Externe Sharedien-Services (nicht Teil dieser Analyse, aber Schnittstellen des Backends)

| Service | Rolle aus Backend-Sicht |
|---|---|
| `Identity` (IdentityServer-basiert, `src/Identity`) | OAuth2/OIDC-Provider; Backend validiert Tokens per Introspection (`connect/introspect`), holt Client-Credentials-Tokens (`connect/token`), UI-Login per OIDC (`connect/authorize`). Ruft Backend-Endpunkte (User-Provisioning, Registration) – unklar welche genau. |
| `Processing` (`src/Processing`) | Rendering-Pipeline: empfängt `*DerivationStartMessage`/`MetadataExtractionStartMessage` über Queue `ProcessingJobStart`, antwortet mit `DerivationStatusMessage`/`MetadataExtractionStatusMessage` über Queue `DerivationJobCallback`. |
| `Search` (`src/Search`, Elasticsearch) | Backend referenziert `Sharedien.Search.Core` und `Sharedien.Search.Infrastructure.Elasticsearch` **direkt als Projektreferenz** (In-Process-Indexierung + Suche) und zusätzlich über Queue `Advellence.Sharedien.Search` (SearchSync). |
| `StorageHandler` (`src/StorageHandler`) | Storage-Service mit signierten Upload/Download-URLs (`StorageHandler.Api.Contracts` referenziert). |
| `EventHub` (`src/EventHub`) | SignalR-Hub; Backend publiziert ActivityLog-Events auf Topic `EventHub`, `EventHubCallbackJob` konsumiert und leitet an `INotificationHubService` weiter. |

---

## 2. Verwendete Technologien und Packages

| Bereich | Package / Technologie | Version |
|---|---|---|
| Runtime | .NET Core 3.1, C# 8 | – |
| Web | ASP.NET Core MVC, `Microsoft.AspNetCore.Mvc.Versioning` (URL-Segment `api/v{version}`, V1–V6), `Microsoft.AspNetCore.Mvc.NewtonsoftJson` | 4.1.1 / 3.1.8 |
| OpenAPI | Swashbuckle.AspNetCore (+Annotations, +Newtonsoft), NSwag.MSBuild (Client-Generierung) | 5.6.3 / 13.8.2 |
| DI | Autofac + `Autofac.Extensions.DependencyInjection` | 7.0.1 |
| Datenzugriff | Dapper, `Microsoft.Data.SqlClient`, handgeschriebenes T-SQL, `System.Linq.Dynamic.Core` | 2.0.35 / 2.0.1 |
| Migration | DbUp | 4.4.0 |
| Background-Jobs | Hangfire.AspNetCore + Hangfire.SqlServer (eigene Connection `ConnectionStrings.BackgroundJobs`, Dashboard optional unter `/jobs`, AutomaticRetry=0) | 1.7.14 |
| Message-Broker | AMQPNetLite (ActiveMQ), Microsoft.Azure.ServiceBus, JsonSubTypes | 2.4.0 / 5.0.0 |
| Storage | Azure.Storage.Blobs, Microsoft.Azure.Storage.Blob (in Api), SharpZipLib, Microsoft.IO.RecyclableMemoryStream | 12.6.0 / 11.2.2 / 1.2.0 |
| Suche | Elasticsearch (über `Sharedien.Search.Infrastructure.Elasticsearch`) | – |
| Mapping/Validation | AutoMapper, FluentValidation | 10.0.0 / 9.2.2 |
| Logging/Monitoring | Serilog (+Sinks Console/File/Http/AppInsights/Debug, Enrichers, Exceptions, Compact), Application Insights | 2.10 |
| Auth | `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `System.IdentityModel.Tokens.Jwt`, `Microsoft.AspNetCore.Cryptography.KeyDerivation` (PBKDF2-Passwort-Hashing) | 3.1.16 / 6.7.1 / 3.1.8 |
| Verzeichnisdienste | `Novell.Directory.Ldap.NETStandard` (LDAP), `Microsoft.Graph` + `Microsoft.Graph.Auth` (Azure AD Gruppen) | 3.3.1 / 3.15.0 |
| Office/Export | NPOI (XLS/XLSX), CsvHelper | 2.5.1 / 15.0.6 |
| PDF | PdfSharpCore (Contact Prints) | 1.2.6 |
| Templating | DotLiquid (E-Mail-Templates) | 2.1.436 |
| AI | ClarifaiGrpc, Microsoft.Azure.CognitiveServices.Vision.ComputerVision, Imagga (REST) | 7.3.0 / 7.0.0 |
| Mail | MailKit, MimeKit | 2.9.x |
| Konfiguration | NetEscapades.Configuration.Yaml, eigener SQL-Provider, Environment-Variablen, CommandLine | 2.0.1 |
| Tests | xunit, Microsoft.NET.Test.Sdk, NJsonSchema | – |
| Hosting | Kestrel, HttpSys (Windows Service), Docker (`Dockerfile`, `build_alpha_docker_image.ps1`), Azure App Service Publish-Profile | – |

---

## 3. Architektur-Muster (Request-Verarbeitung)

1. **Controller** (`Sharedien.Api/Controllers`) erben von `BaseController` (Common). Sie validieren DTOs per `IValidator` (FluentValidation), mappen DTO→BusinessObject (AutoMapper), rufen eine **Activity** auf und mappen die `Response<T>` per `HandleResponse` auf HTTP-Status (ErrorType → 400/404/409/403 …). Custom-Header: `X-Data-Languages` (gewünschte Datensprachen), `X-User-Contexts`, `X-Total-Count`, `X-Unread-Count`, `X-Operation-ID`.
2. **Activities** (`Core/Activities/*`, ~330 Klassen) sind Use-Case-Objekte (`Activity<TResponse, TParam…>`), die einen **UnitOfWork** öffnen (`UsingUnitOfWorkAsync`), den aktuellen User + Permissions in den UoW laden, den Service aufrufen und bei Erfolg committen. `ReadOnlyActivity` für Lesezugriffe; `EventStagingActivity` zusätzlich: nach Commit werden gestagte Events reduziert/gesendet und Hangfire-Jobs gestartet (`HandlePendingJobsAndEventsAsync`). `ExecuteWithoutUserAsync` für Systemkontext (Hintergrundjobs). `actAsSystem`-Flags in Services umgehen Berechtigungsprüfungen.
3. **UnitOfWork** (`IUnitOfWork`, `IActiveUnitOfWork`): kapselt `SqlConnection`+Transaktion (`IsTransactional`, `IsReadOnly`), `TransactionIdentifier`, `UserId`, geladene `Permissions`, gesammelte `ActivityLog`s; verschachtelbar (`Outer`, `UnitOfWorkScope.JoinExisting|ForceCreateNew`), `AsyncLocalCurrentUnitOfWorkProvider`.
4. **Services** (`Core/Services`) enthalten die Fachlogik, nutzen Repositories (Interfaces in Core, Dapper-Implementierung in `Infrastructure.SqlServer`), prüfen Berechtigungen über `IAuthorizationService`/`I*PermissionService`, schreiben ActivityLogs in den UoW und stagen Events (`IEventStagingService.StageEventsAsync`).
5. **Event Staging / Outbox**: Events (EventStream, Search-Sync, Processing-Jobstart, EventHub) werden während der Transaktion in `MAM.OutgoingMessageQueueEvent` (transaktional) bzw. In-Memory (nicht-transaktional) gesammelt, Asset/Entity-Search-Events werden pro Transaktion **reduziert/gemerged** (`MessageMergeHelper`), und nach Commit gesendet (`SendPendingEventsAsync(transactionIdentifier)`), gesendete werden geflaggt; Cleanup-Job löscht alte Einträge.
6. **Antwortmodell**: `Response`/`Response<T>` mit `Success`, `ResultValue`, `Errors` (ErrorType: ParameterValidation, ModelValidation, NotFound, AlreadyExists, NotAllowed, ConcurrencyConflict, MissingUnitOfWork). Optimistic Concurrency via `RowVersion` (byte[]) in fast allen Tabellen; Identifier sind öffentliche String-IDs (`IIdentifiable.Identifier`), interne `long`-IDs bleiben in der DB.
7. **Konfiguration**: YAML (`appsettings.yml`, `appsettings.Overrides.yml`), Env-Vars, CommandLine und **DB** (`Admin.Configuration`, Provider `Database`); Werte sind zur Laufzeit über `ApplicationSettingsController` änderbar, Änderungen lösen `ApplicationSettingsChangedEvent` über das interne Topic aus → `SharedienConfigurationReloader` lädt neu (auch in anderen Instanzen/Services).

---

## 4. Domänenmodell

Das Datenmodell ist ein **Objektgraph**: fast alle fachlichen Entitäten sind `Node`s (Tabelle `MAM.Node`) mit einem `NodeType`, verbunden durch `Edge`s mit `EdgeType`. Metadaten hängen als `Property`/`PropertyValue` an Nodes (oder Edges). Mehrsprachige Bezeichnungen liegen in `MAM.Translation`.

### 4.1 Objektgraph (Node / NodeType / Edge / EdgeType / Tenant)

| Entity | Tabelle | Zweck | Wichtigste Felder | Beziehungen |
|---|---|---|---|---|
| **Node** | `MAM.Node` (View `NodeView` = nicht gelöscht) | Generischer Graph-Knoten für Category, AssetSet, Version, Workspace, Task, Comment, State, BillingContact, RightsOwner, License, LicensePurchase, CustomNode | `NodeId`, `Identifier` (öffentliche ID), `NodeTypeId`, `TenantId`, `SequenceNr` (Sortierung), `IsTransparent`, `InterruptInheritance` (unterbricht Berechtigungs-Vererbung), `IsDeleted` (Soft-Delete), `CreateUser`, `CreateDate` | 1:1 zu Fachtabelle (`Category.CategoryNodeId`, `AssetSet.AssetSetNodeId`, …); n:m über `Edge` |
| **NodeType** | `MAM.NodeType` | Typ eines Nodes; System-Typen + benutzerdefinierte ("Entity Types"/Custom Entities) | `Identifier`, `TranslationIdentifier` (Name/Description), `NodeSemantic` (`NodeTypeSemantic`: Undefined, Category, AssetSet, Version, Workspace, Task, Comment, State, BillingContact, RightsOwner, License, LicensePurchase, Custom=1000), `Status`, `IsSystem`, `IsDeleted` | `PropertyDefinitionNodeType` (welche PropertyDefinitions gelten), `EdgeTypeAllowedNodeTypes`, `RuleNodeType`, `PropertyNodeTypeInheritance` |
| **Edge** | `MAM.Edge` | Gerichtete Beziehung zwischen zwei Nodes ("Relation" in der API) | `EdgeId`, `Identifier`, `FromNodeId`, `ToNodeId`, `EdgeTypeId`, `MutUser/MutDate` | Properties können an Edges hängen (`Property.EdgeId`) |
| **EdgeType** | `MAM.EdgeType` | Beziehungstyp ("Relation Type") | `Identifier`, `EdgeSemantic` (`EdgeTypeSemantic`: Undefined, ParentChild, Version, Link, Crop, Translation, Variant, Licence, Duplicate, Transition, DrmLicense), `IsDirected`, `IsSystem`, `IsUsedForAssetSearch`, `Status`, `TranslationIdentifier` | `EdgeTypeAllowedNodeTypes` (FromNodeTypeId, ToNodeTypeId) = erlaubte Kombinationen |
| **EdgeTypeAllowedNodeTypes** | `MAM.EdgeTypeAllowedNodeTypes` | Whitelist erlaubter From/To-NodeType-Paare pro EdgeType | `EdgeTypeId`, `FromNodeTypeId`, `ToNodeTypeId` | |
| **Tenant** | `Admin.Tenant` | Mandant (siehe 6.5) | `Identifier`, `Status`, `IsDefaultTenant`, `TranslationIdentifier`, `AcceptsRegistrations`, `IsDeleted` | `Node.TenantId`, `UserGroupToRoleMap.TenantId`, `FileNamingDefinitionTenant`, `UserRegistration.TenantIdentifier` |
| **CustomNode** | `MAM.CustomNode` | Instanz eines benutzerdefinierten NodeTypes ("Custom Entity"), z. B. Personen, Produkte, Marken | `NodeId`, `TranslationIdentifier` (Name) | Root-Node pro Custom-NodeType (`CustomNodeHelper.RootCustomNodeIdentifier`) |

System-Identifier der NodeTypes: `Category`, `AssetSet`, `Version`, `Workspace`, `Task`, `Comment`, `State`, `BillingContact`, `RightsOwner`, `License`, `LicensePurchase`.
System-Identifier der EdgeTypes: `Undefined`, `ParentChild`, `Version`, `Link`, `Crop`, `TranslationEntry`, `Licence`, `Variant`, `Duplicate`, `Transition`, `LicenseDrm`.
Regeln im `EdgeTypeHelper`: `ParentChild` und `Link` schließen sich gegenseitig aus (ein Asset ist entweder Kind oder verlinkt), sind "movable"; vom Benutzer editierbare System-EdgeTypes: `ParentChild`, `Link`, `LicenseDrm`.
Hierarchie-Semantiken (Vererbung von Berechtigungen läuft entlang `ParentChild`): Category, Workspace, Task, Comment, Custom, State, BillingContact, RightsOwner, License. "Objekt"-Semantiken: AssetSet, Version.
Root-Nodes: Root-Category (`CategoryHelper.RootCategoryIdentifier`), Recycle-Bin-Category (`RecycleBinCategoryIdentifier`), Root-Workspace, Root-CustomNode je Custom-NodeType.
Erlaubte Property-Vererbung (`AllowedPropertyInheritanceMappings`): nur `Category → AssetSet`.

### 4.2 Assets, Versionen, Derivate

| Entity | Tabelle | Zweck | Wichtigste Felder | Beziehungen |
|---|---|---|---|---|
| **AssetSet** ("Asset") | `MAM.AssetSet` | Das logische Asset (Container aller Versionen) | `AssetSetNodeId`, `Created`, `AssetSetStatus` (Importing, Imported, Duplicate) | Node; Edge `Version` → AssetVersion-Nodes; Edge `ParentChild` von Category/Workspace/Task; `Link` von weiteren Categories; `Duplicate`, `Crop`, `Variant`, `TranslationEntry`, `LicenseDrm`, benutzerdefinierte Edges |
| **AssetVersion** ("Version") | `MAM.AssetVersion` | Eine Datei-Version eines Assets (trägt die Metadaten!) | `AssetVersionNodeId`, `Version` (long, laufend), `AssetVersionStatus` (WorkInProgress, Unpublished, Published), `ProcessingStatus` (Pending, Started, Finished, Failed), `OriginalFilename`, `OriginalName`, `OriginalFileEnding`, `AssetTypeId`, `Created` | Node; Edge `Version` von AssetSet; `AssetDerivation`s; Properties hängen an der Version |
| **AssetType** | `MAM.AssetType` | Grobe Medienklasse (Image, Movie, Document …; 13 per Migration eingefügt) | `Identifier`, `DownloadOriginalAlways`, `PropertyListValueIdentifier` (Listenwert der Systemproperty `asset_type`) | `FileType.AssetTypeId`, `AssetTypeDerivationType`, `DerivationTypeRestriction.AssetTypeId`, `PropertyExtractionDefinitionAssetType` |
| **FileType** | `MAM.FileType` | Dateityp anhand Endung (50+ Einträge) | `Identifier`, `AssetTypeId`, `FileEnding`, `MimeType`, `QualityChecksActive`, `EnableBinaryDocumentExtraction` (Volltext-Extraktion in Search) | `DerivationJobDefinition.OutputFileTypeId`, `DerivationJobDefinitionInputFileType`, `AssetDerivation.FileTypeId` |
| **DerivationType** | `MAM.DerivationType` | Art eines Derivats (Original, Thumbnail, Preview/Screen, Watermark-Varianten, Print, benutzerdefiniert) | `Identifier`, `TranslationIdentifier`, `IsSystem`, `IsDeleted` | `AssetTypeDerivationType` (Einstellungen je AssetType) |
| **AssetTypeDerivationType** | `MAM.AssetTypeDerivationType` | Konfiguration eines DerivationType pro AssetType | `IsDefaultDerivation` (automatisch erzeugen), `AllowManualUpload`, `IsOnDemand`, `ShowInDownloads`, `UseAsThumbnail`, `UseAsPreview` | |
| **DerivationJobDefinition** | `MAM.DerivationJobDefinition` | Technische Rendering-Vorschrift (JSON `JobDefinition`) für einen DerivationType | `InputDerivationDataType`/`OutputDerivationDataType` (`DerivationDataType`: Image, Movie, PrintDocument, OfficeDocument, Custom), `DerivationTypeId`, `OutputFileTypeId`, `IsSystem`; typisierte Definitionen: `ImageDerivationJobDefinition` (Source/Overlay/Output, Transformation-Steps Crop/Scale/Rotate/Sharpen/Blur, Opacity, BackgroundColor, ColorSpace/Profile/DPI/Compression, ImageType Jpeg/Bmp/Png/Tiff/Gif, Quality), `CustomDerivationJobDefinition` (Steps mit Variablen/Expressions, StepType, Argument-String, Color-Profile-Overrides), Movie/Office/PrintDocument | `DerivationJobDefinitionInputFileType` (erlaubte Eingabe-FileTypes) |
| **AssetDerivation** | `MAM.AssetDerivation` | Konkretes Derivat (Binärdatei) einer AssetVersion | `AssetVersionId`, `DerivationTypeId`, `DataId` (Storage-Identifier), `FileTypeId`, `FileSize`, `FileHash`, `Status` (Pending, Processing, Finished, Failed), `DerivationFeedback` | Original-Derivat trägt die hochgeladene Datei |
| **AssetDerivationJob** / `…Report` | `MAM.AssetDerivationJob`, `MAM.AssetDerivationJobReport` | Hintergrundjob für (Re-)Derivation vieler Assets, mit Report je Derivat | `BackgroundJobId`, `Status`, Zähler; Report: AssetSet/Version/Derivation/AssetType/DerivationType/FileType/FileSize/FileName/Status | Hangfire |
| **AssetSetDuplicate** | `MAM.AssetSetDuplicate` | Duplikat-Paar (gleicher FileHash) | `AssetSetIdentifier`, `DuplicateAssetSetIdentifier`, `FileName`, `ImportedBy`, `CreatedAt` (beide Seiten) | Edge `Duplicate` |
| **UploadSession** / `UploadSessionChunk` | `MAM.UploadSession`, `MAM.UploadSessionChunk` | Chunked-Upload-Sitzung (alt: server-seitig; neu: Storage-signierte URLs) | `SessionIdentifier`, `UploadType` (AssetSet, AssetVersion, WorkspaceAssetSet, WorkspaceTaskAssetSet, LatestAssetVersionDerivation, AssetVersionDerivation), Ziel-Nodes, `DerivationTypeId`, `ArchiveExtractionMode`, `DeleteAssetAfterExtraction` | Cleanup-Job |
| **ArchiveExtractionJob** / `…Entry` | `MAM.ArchiveExtractionJob`, `…Entry` | Entpacken eines ZIP-Assets in eine Kategorie/Workspace/Task | `ArchiveAssetSetIdentifier`, `OutputCategoryIdentifier`, `WorkspaceIdentifier`, `TaskIdentifier`, `ArchiveExtractionMode` (ExtractWithFullPath = Ordner→Unterkategorien, ExtractFilesOnly), `DeleteAssetAfterExtraction`, `Status`, Zähler; Entry: `EntryType` (Folder/File), `Status` (Extracted/Filtered/Error), `Path`, `Name`, `CreatedEntityIdentifier` | Hangfire; Dateinamen-Filter aus Konfiguration (`.`-Dateien, `__MACOSX`, `Thumbs.db`) |
| **AssetCleanup** (DAL) | – | Hilfsstruktur für Hard-Delete soft-gelöschter Assets/Versionen/Derivate nach Retention | | Cleanup-Job |

Semantik: Ein Asset = `AssetSet`-Node + n `AssetVersion`-Nodes (Edge `Version`). Jede Version hat ein `Original`-Derivat und weitere Derivate. Beim Anlegen einer neuen Version werden die Properties der Vorgängerversion kopiert (`CopyPropertiesFromVersionToVersionAsync`), Systemproperties (`file_name`, `file_size`, `file_hash`, `file_extension`, `asset_type`, `asset_version`, `asset_status`, `imported_by`, `system_import_date`, `asset_identifier`) neu gesetzt, Duplikat-Erkennung per Content-Hash durchgeführt, Default-Derivationen eingeplant und Rules mit Trigger `IsNewVersion` ausgeführt.

### 4.3 Kategorien, Workspaces, Tasks, States, Comments, Shares, Custom Entities

| Entity | Tabelle | Zweck | Wichtigste Felder | Beziehungen |
|---|---|---|---|---|
| **Category** | `MAM.Category` | Ordnerbaum (mandantenfähig), inkl. Root und Recycle Bin | `CategoryNodeId`, `TranslationIdentifier` (Name), `SequenceNr` | Node; `ParentChild` zu Sub-Categories/AssetSets; `Link` zu AssetSets; Properties (mit Vererbung an AssetSets); NodePermissions |
| **Workspace** | `MAM.Workspace` | Arbeitsbereich (Upload-Workspace, Collection, Collaboration) und Workspace-**Templates** | `WorkspaceNodeId`, `TranslationIdentifier`, `Status`, `CreatedBy`, `IsTemplate`, `PropertySetIdentifier` (Pflichtfelder), `FinalizationCategoryId` (Zielkategorie beim Finalisieren), `CopyPropertiesToAssets`, `WorkspaceType` (Upload, Collection, Collaboration), `CreatedAt`, `SequenceNr`, `StateIdentifier` (Kanban-Spalte), `ReminderSentAt`, `AvatarFileReferenceId` | Node; `ParentChild` zu AssetSets, Tasks, Comments, States; Shares; Watchers; NodePermissions (Rollen `workspaceOwner`, `workspaceCollaborator`); Template-Permissions (`EntityPermission.WorkspaceId`) |
| **Task** | `MAM.Task` | Aufgabe in einem Workspace (Kanban) | `NodeId`, `Name`, `Description`, `DueDate`, `Status` (Open/Closed), `TaskTypeId`, `AssignedUserId`, `SequenceNr`, `CreatedBy/At`; `TaskAssignee` (DAL) | Node; `ParentChild` von Workspace; `ParentChild`/`Link` zu AssetSets; Comments; Watchers; State-Zuordnung über Edge `Transition`?/State (unklar: Zuordnung Task→State erfolgt über `StateIdentifier` in BO, Persistenz via Edge) |
| **TaskType** | `MAM.TaskType` | Aufgabentypen (Liste) | `Identifier` | |
| **State** | `MAM.State` | Kanban-Status/Spalte innerhalb eines Workspace (für Tasks) bzw. global für Workspaces | `StateNodeId`, `SequenceNr`, `TranslationIdentifier`, `IsDefault`; `StateTransition` (erlaubte Übergänge From→To, Edge `Transition`) | Node; NodePermissions je State (wer darf in diesen Status verschieben) |
| **Comment** | `MAM.Comment` | Kommentar an Workspace/Task | `NodeId`, `Text`, `CreatedBy`, `CreatedAt` | Node; `ParentChild` |
| **Share** / **ShareRecipient** / `ShareAssetTypeDerivationType` | `MAM.Share`, `MAM.ShareRecipient`, `MAM.ShareAssetTypeDerivationType` | Öffentlicher Share eines Workspace (E-Mail-Empfänger mit Token, Ablaufdatum, freigegebene Derivate je AssetType) | `WorkspaceNodeId`, `ValidUntil`, `Name`; Recipient: `EmailAddress`, `Token`; erlaubte (AssetTypeId, DerivationTypeId) | Public-Endpoints ohne Login |
| **SavedSearch** | `MAM.SavedSearch` | Gespeicherte Suchabfrage (JSON `SearchQuery`) | `Name`, `SearchQuery`, `CreatedBy` | `EntityPermission.SavedSearchId` (Teilen mit Usern/Gruppen/Rollen) |
| **Watcher** (Repository) | `MAM.NodeActivityLogSettings` | Beobachter eines Workspace/Task (Inbox/E-Mail) | `NodeId`, `UserId`, `SendToInbox`, `SendEmail` | ActivityLog-Zustellung |
| **UserNotificationSettings** | `Admin.UserNotificationSettings` | Pro User & Node: Benachrichtigungen deaktivieren | `NodeId`, `UserId`, `DisableNotifications` | |

### 4.4 Property-System (dynamische Metadaten)

| Entity | Tabelle | Zweck | Wichtigste Felder | Beziehungen |
|---|---|---|---|---|
| **PropertyGroup** | `MAM.PropertyGroup` | Gruppierung von PropertyDefinitions (UI-Sektionen) | `Identifier`, `TranslationIdentifier`, `Status`, `SequenceNr`, `IsDeleted` | 1:n PropertyDefinition |
| **PropertyDefinition** | `MAM.PropertyDefinition` | Metadaten-Feld-Definition | `Identifier`, `PropertyGroupId`, `ValueType` (String, Text, Date, DateTime, Time, Integer, Float, Decimal, Boolean, TimeSpan), `HasListValues`, `AllowCustomListValue`, `AllowMultipleValues`, `IsLanguageDependant` (übersetzbar), `HasUserContext`, `Editable`, `IgnoreCopying` (nicht auf neue Version/Workspace-Assets kopieren), `IsSystemProperty`, `SearchOption` (NotSearchable, AdvancedSearchable, FulltextSearchable, AdvancedAndFulltextSearchable), `IsSearchableFacet`, `IsFileNamingUnlocked` (für Dateinamens-Tokens), `DefinitionType` (Standard, Url, Html), `SortKey`, `BlacklistedFields` (Felder, die im Admin nicht editierbar sind), `Status`, `TranslationIdentifier` (Name, Description) | `PropertyDefinitionNodeType` (gilt für welche NodeTypes), `PropertyListValue`, `PropertyDefinitionSystemPropertyStrategy`, Restrictions, FileNamingDefinitionProperty, WatermarkDefinition, RuleAction/Trigger, PropertyExtractionMapping |
| **PropertyListValue** ("List Item") | `MAM.PropertyListValue` | Vordefinierte Listenwerte (typisiert wie PropertyValue) | `PropertyDefinitionId`, `SortKey`, `Value*`, `TranslationIdentifier`, `Status`, `IsSystemListValue`, `IsDeleted` | |
| **PropertyDefinitionSystemPropertyStrategy** | `MAM.PropertyDefinitionSystemPropertyStrategy` | Verknüpft Systemproperty + NodeType mit einer Mirror-Strategie (`SystemPropertyStrategy`: MirrorFileNamePropertyToAssetVersionEntity, MirrorWorkspaceNameProperty…, MirrorCategoryName…, MirrorAssetStatus…, MirrorCustomEntityName…, MirrorWorkspaceType…, MirrorTaskProperties…, MirrorBillingContact…, MirrorRightsOwner…, MirrorLicense…) | | Implementiert in `Services/Metadata/CorePropertyChangeHandlers` |
| **Property** | `MAM.Property` | Instanz eines Feldes an einem Node (oder Edge) | `Identifier`, `NodeId`, `EdgeId`, `PropertyDefinitionId`, `IsDeleted` | 1:n PropertyValue; `PropertyNodeTypeInheritance` (Category-Property an AssetSets vererben) |
| **PropertyValue** | `MAM.PropertyValue` | Einzelwert (Mehrfachwerte = mehrere Zeilen) | `PropertyId`, `PropertyListValueId`, `UserContextId`, `TranslationIdentifier` (sprachabhängige Textwerte in `Translation`), `ValueBool/Int/Decimal/Date/DateTime/DateTimeDurationTicks/Text`, `IsDeleted` | |
| **PropertyNodeTypeInheritance** | `MAM.PropertyNodeTypeInheritance` | Markiert, an welche NodeTypes (nur AssetSet) eine Category-Property vererbt wird | `PropertyId`, `NodeTypeId` | |
| **PropertyValueRestriction** | `Admin.PropertyValueRestriction` | Rollenbasierte Einschränkung auf konkrete Property-Werte (Blacklist; wird beim Asset-Permission-Check per SQL gejoint) | `RoleId`, `PropertyId`, `Action` | |
| **PropertyDefinitionRestriction** | `Admin.PropertyDefinitionRestriction` | Rolle darf PropertyDefinition nicht lesen/schreiben (Blacklist) | `RoleId`, `PropertyDefinitionId`, `Action`, `Status` | |
| **PropertySet** / `PropertySetPropertyDefinitions` / `PropertySetCondition` | `MAM.PropertySet`, … | Formular-/Pflichtfeld-Set pro EntityType: Liste von PropertyDefinitions mit `FillMode` (Optional, Recommended, Mandatory); **Conditions** (PropertyDefinition + ListValue) bestimmen, wann ein Set greift (z. B. asset_type=Image); `IsGlobal`, `IsDefault`, `CreatedBy` | | `EntityPermission.PropertySetId` (Freigabe); Validierung beim Finalisieren von Workspace-Assets |
| **UserContext** | `MAM.UserContext` | Kontext-Dimension für Property-Werte (mehrere Wertesätze pro Property, z. B. Markt/Region/Marke – Semantik im Code nicht dokumentiert) | `Identifier`, `TranslationIdentifier`, `SequenceNr`, `IsDefault`, `IsDeleted` | `PropertyValue.UserContextId`, `UserSettings.UserContextId`, Header `X-User-Contexts`, `PriceListUserContext`, `UserContextGroup` |
| **Language** ("DataLanguage") | `MAM.Language` | Datensprachen für übersetzbare Werte | `Identifier` (z. B. `en`, `de`), `Status`, `IsDefaultLanguage`, `TranslationIdentifier`, `IsDeleted` | Header `X-Data-Languages` |
| **Translation** | `MAM.Translation` | Zentrale Übersetzungstabelle | `ObjectType`, `TranslationIdentifier`, `FieldName`, `LanguageId`, `TranslatedValue` | |
| **PropertyMassEdit** (+`Asset`, `Mutation`, `Result`) | `MAM.PropertyMassEdit*` | Massenbearbeitung als Job: Ziel-AssetSets, Mutationen (PropertyDefinition, ListItem, DataLanguage, Value, UserContext, `PropertyStrategy`/`PropertyValueStrategy`/`TranslationStrategy` = `PropertyActionStrategy`: KeepExisting, OverwriteExisting, Abort, DeleteExisting, Merge), Ergebnisse (`PropertyMutationResult`) und Zähler, `ApplicableAssetCountLimit` (Rollenlimit `PropertyEditEntityLimit`) | | Hangfire |
| **PropertyImport** (+`Entity`, `Mutation`, `Message`) | `MAM.PropertyImport*` | Import aus XLS/XLSX/CSV als Job: Datei (`DataId`, `MimeType`), Status, Zähler, Meldungen (Typ/Severity/Row/Column), betroffene Entities (inkl. Version) und Mutationen | | Hangfire |
| **PropertyExtractionDefinition** (+`AssetType`, `Source`, `Mapping`) | `MAM.PropertyExtraction*` | AI-Tagging-Definition: `ExtractionType` (DetectTags), `InputDerivationTypeId` (welches Derivat an die AI geht), erlaubte AssetTypes, Quellen (`MetadataProvider` AzureCognitiveServices/Imagga/Clarifai, `ConfidenceThreshold`, `Language`, `CustomModelId`), Mappings (`FromValue` → PropertyDefinition/ListValue) | | |
| **PropertyExtractionJob** (+`AssetDerivation`, `ResultSource`, `Result`) | `MAM.PropertyExtractionJob*` | Ausführung einer Extraction-Definition auf Assets; `ImportAfterExtraction`; Ergebnisse je Asset/Quelle mit `ExtractedValue`, `Confidence`, Status (Extracted/Imported/Skipped/Error) | | Hangfire |

### 4.5 Berechtigungen (Entities)

| Entity | Tabelle | Zweck | Wichtigste Felder | Beziehungen |
|---|---|---|---|---|
| **Role** | `Admin.Role` | Rolle = Bündel von Permissions + Restriktionen | `Identifier`, `TranslationIdentifier`, `Status`, `IsSystem`, `PropertyEditEntityLimit`, `PropertyExportEntityLimit`, `IsDeleted` | `RoleObjectType` (auf welche ObjectTypes darf die Rolle als Node-/Tenant-Rolle zugewiesen werden), `RoleObjectTypePermission`, Restrictions |
| **RoleObjectType** | `Admin.RoleObjectType` | Rolle ist für ObjectType X zuweisbar (z. B. Category, Workspace, Task, State, Tenant) | `RoleId`, `ObjectType` | |
| **RoleObjectTypePermission** ("ObjectTypePermission") | `Admin.RoleObjectTypePermission` | Einzelrecht der Rolle (`Permission`-Enum, 311 Werte, Muster `<ObjectType><Create|Read|Update|Delete|View>` + Spezialrechte wie `GlobalAdmin`, `WorkspaceFinalize`, `CategoryDownloadRead`, `AssetDownloadRead`, `CropDerivationCreate`, `RuleReApplyJobCreate`, `PermissionMaterializationJobCreate`, Frontend-Views `DashboardView`, `AssetBrowserView`, `AdminView`, `UploadView`, `DownloadBasketView`, `CollectionView`, `CustomEntityBrowserView`) | `RoleId`, `Permission` | |
| **UserGroupToRoleMap** | `Admin.UserGroupToRoleMap` | **Globale/mandantenweite** Rollenzuweisung: UserGroup hat Rolle im Tenant | `TenantId`, `RoleId`, `UserGroupId` | |
| **NodePermission** ("ResourcePermission") | `Admin.NodePermission` | **Ressourcenbezogene** Rollenzuweisung an einem Node für User oder UserGroup | `NodeId`, `UserGroupId` / `UserId`, `RoleId`, `Status`, `Identifier` | |
| **MaterializedNodePermission** | `Admin.MaterializedNodePermission` | Materialisierte (vererbte) Kopie jeder NodePermission auf alle Nachkommen im `ParentChild`-Baum (bis `InterruptInheritance`) | `NodeId`, `NodePermissionId` | Aufbau durch `PermissionMaterialization`-Jobs |
| **PermissionMaterialization** / `…Operation` | `Admin.PermissionMaterialization*` | Job-Tabelle für (Re-)Materialisierung (`JobType`: EntityCreated, EntityDeleted, FullPermissionUpdate, PermissionCreated) | `TransactionIdentifier`, `UserIdentifier`, `Status`, `CreatedAt`, `FinishedAt`; Operation: `EntityIdentifier`, `EntityTypeIdentifier`, `PermissionIdentifier` | Hangfire |
| **EntityPermission** | `Admin.EntityPermission` | Rollenzuweisung an **Nicht-Node-Objekte** (SavedSearch, PropertySet, AssetDerivationJob, PropertyMassEdit, PropertyImport, PropertyExtractionJob, ArchiveExtractionJob, FileNamingDefinition, Workspace-Template-Permissions) für User/UserGroup | je eine FK-Spalte pro Objektart + `RoleId`, `UserGroupId`, `UserId` | |
| **DerivationTypeRestriction** | `Admin.DerivationTypeRestriction` | Rolle darf DerivationType (optional je AssetType) nicht lesen/erzeugen/… (Blacklist) | `RoleId`, `DerivationTypeId`, `AssetTypeId`, `Action` (`AuthorizableAction`: Read, Create, Update, Delete, View, Any) | |
| **PropertyDefinitionRestriction**, **PropertyValueRestriction** | s. 4.4 | | | |

Standard-Rollen (`RoleHelper`): `admin`, `workspaceOwner`, `workspaceCollaborator`, `entityOwner`, `collectionOwner`, `agent`, `visitor` (10 Rollen-Inserts in Migrationen insgesamt, weitere unklar).

### 4.6 User Management

| Entity | Tabelle | Zweck | Wichtigste Felder | Beziehungen |
|---|---|---|---|---|
| **User** | `Admin.User` | Benutzerkonto (lokal oder extern provisioniert) | `Identifier`, `UserName`, `FirstName`, `LastName`, `EmailAddress`, `Department`, `Company`, `ContactPerson`, `PhoneNumber`, `AccessType` (Permanent/Expiring), `Status` (`AccountStatus`: Active, Inactive, EmailVerificationPending/Expired, ApprovalPending/Denied, Locked, AccountRenewalPending/Expired), `StatusChangeDate`, `PasswordSalt/Hash` (PBKDF2), `ProviderName`, `ProviderSubjectId` (externer IdP), `LastLoginDate`, `LastLoginAttemptDate`, `LoginAttemptsCount`, `LastPasswordResetDate`, `CreatedAt`, `IsDeleted` | `UserGroupUser`, `UserSettings`, `UserRegistration`, `ExpiringCode`, NodePermission/EntityPermission (UserId) |
| **UserGroup** / **UserGroupUser** | `Admin.UserGroup`, `Admin.UserGroupUser` | Gruppen; Mitgliedschaft mit `IsProvisioned` (aus IdP-Claims/Graph synchronisiert) | `Identifier`, `Name`, `Description`, `IsDeleted` | `UserGroupToRoleMap`, `NodePermission`, `ExternalGroupUserGroups` |
| **ExternalGroup** / `ExternalGroupUserGroups` | `Admin.ExternalGroup*` | Mapping externer Gruppen (AD/AAD-Name) auf interne UserGroups | `ExternalGroupName`, `ExternalGroupDisplayName` | Provisioning |
| **UserSettings** | `Admin.UserSettings` | Profil-Einstellungen | `DisplayLanguage`, `Locale`, `LanguageId` (Datensprache), `UserContextId`, `UiSettings` (JSON-Blob), `AvatarFileReferenceId`, `SearchTermOperator` (Or/And) | |
| **UserRegistration** | `Admin.UserRegistration` | Selbstregistrierung mit Mandant und Freigabe | `UserId`, `TenantIdentifier`, `ApprovedAt` | Tenant.AcceptsRegistrations |
| **ExpiringCode** | `Admin.ExpiringCode` | Einmal-Codes (`CodeType`: EmailVerification, PasswordReset, AccountRenewal) | `UserId`, `Code`, `CodeType`, `CreatedAt`, `ExpiresAt` | |
| **FileReference** | `MAM.FileReference` | Hochgeladene Hilfsdateien (`FileUsageType`: Watermark, Avatar) | `DataIdentifier`, `FileName`, `FileUsageType` | Watermark-Trigger, Avatare (User, Workspace, RightsOwner) |

### 4.7 Jobs, Regeln, Ereignisse, Audit

| Entity | Tabelle | Zweck | Wichtigste Felder | Beziehungen |
|---|---|---|---|---|
| **BackgroundJob** | `MAM.BackgroundJob` | Verknüpfung Sharedien-Job ↔ Hangfire-Job | `Identifier`, `Type` (`BackgroundJobType`: PropertyMassEdit, ArchiveExtraction, AssetDerivation, RecycleBinCleanup, Import, PermissionMaterialization, OutgoingMessageQueueEventCleanup, UploadSessionCleanup, EmailQueue, ExecuteRulesJob, RulesJobCleanup, ActivityLogCleanup, AssetCleanup, UserAccountManagement, WorkspacesJob, PropertyExtraction), `HangfireJobId` | |
| **Rule** / **RuleNodeType** / **RuleTrigger** / **RuleAction** | `Admin.Rule*` | Regel-Engine (s. 8): Regel gilt für NodeTypes; Trigger/Filter (`TriggerOperator`, Property-Vergleichswert, `IsFilter`), Actions (Ziel-Property + `Template` oder `PropertyExtractionDefinitionIdentifier`, `OnTriggerMatched` true/false) | | |
| **RuleJob** (+`Entity`, `EntityValueExecuted`, `EntityTypeIdentifier`, `EntityIdentifier`, `RuleIdentifier`) | `Admin.RuleJob*` | Nachträgliche Massenausführung von Regeln mit Report (Zähler matched/notMatched/executed/skipped/error, pro Entity Ergebnis und ausgeführte Werte) | | Hangfire |
| **ActivityLog** (+`AdditionalData`, `User`) | `MAM.ActivityLog*` | Audit-/Benachrichtigungs-Log (s. 7) | `ParentActivityLogId`, `NodeId`, `ObjectType`, `ObjectIdentifier`, `Type` (`ActivityLogType`), `ChangeType` (`ActivityLogChangeType`), `UserIdentifier`, `CreatedAt`; AdditionalData Name/Value; `ActivityLogUser` (Inbox-Zustellung, `ReadAt`) | |
| **OutgoingMessageQueueEvent** | `MAM.OutgoingMessageQueueEvent` | Transaktionale Outbox für Broker-Nachrichten | `TransactionIdentifier`, `Payload`, `TypeName`, `Sent`, `PriorityForced/PrioritySelector`, `EventAction`, `ObjectIdentifier`, `ObjectType`, `PropertyDefinitionIdentifier`, `UserIdentifier` | |
| **OutgoingEmail** (+`RecipientUser`, `RecipientAddress`, `TemplateParameter`) | `MAM.OutgoingEmail*` | E-Mail-Warteschlange (Template-ID + Parameter + Empfänger) | | EmailQueue-Job |
| **SearchReIndex** / `SearchReIndexFailedEntities` | `MAM.SearchReIndex*` | Re-Indexierungsjob (Elasticsearch) mit Report | `EntityTypeIdentifiers`, Status, Zähler, `IsCancelled`, `FailureReason`; failed entities mit `BatchNumber` | |
| **Configuration** | `Admin.Configuration` | Application Settings in der DB (Pfad/Wert) | `Path`, `Value` | SQL-ConfigurationProvider |
| **FileNamingDefinition** (+`Property`, `Tenant`) | `MAM.FileNamingDefinition*` | Download-Dateinamensschema: `Format` mit Tokens (Property-Tokens `IsFileNamingUnlocked` + Variablen `var.seq_number|year|month|day|hour|minute|second|name|extension|type`), `CharacterCasing`, `ReplaceIllegalCharacters`, `AbsentPropertyStrategy` (PropertyDefinitionIdentifier, Skip, Abort); Standard je Tenant | | `EntityPermission.FileNamingDefinitionId` |
| **WatermarkDefinition** / **WatermarkTrigger** | `MAM.Watermark*` | Wasserzeichen-Regel: PropertyDefinition (Liste/Bool) + Trigger (ListValue oder Bool → Watermark-Datei `FileReferenceId`), rendert über `DerivationJobDefinitionId` ein zusätzliches Derivat (`<DerivationType>_watermark…`), `DisplayInUI` | | Cache `WatermarkCacheService` |

### 4.8 Lizenz-/Rechtemanagement (DRM)

| Entity | Tabelle | Zweck | Wichtigste Felder | Beziehungen |
|---|---|---|---|---|
| **License** | `MAM.License` | Nutzungslizenz (Node) mit Laufzeit | `NodeId`, `TranslationIdentifier` (Name/Description), `Status`, `PeriodStart`, `PeriodEnd`, `CreatedBy/At`, `UpdatedBy/At`; BO zusätzlich `PurchaseDurationInDays` | Edge `LicenseDrm` zu AssetSets ("Entities der Lizenz"); Buyouts; Systemproperties `license_name`, `license_rights` (Liste), `license_description`, `license_period_start/end` |
| **Buyout** | `MAM.Buyout` | Preisvereinbarung: RightsOwner × PriceList × License | `Identifier`, `RightsOwnerId`, `PriceListId`, `LicenseId`, `CurrencyId`, `Description`, `Basis` (Grundpreis), `AdditionalFeePercentage`, `FromDate`, `ToDate` | |
| **RightsOwner** | `MAM.RightsOwner` | Rechteinhaber (Node) mit Avatar | `NodeId`, `Name`, `AvatarFileReferenceId`, `BillingContactId`, `Status` | Properties `rightsowner_name`, `rights_owner_type`, Adress-/Bank-Properties |
| **BillingContact** | `MAM.BillingContact` | Abrechnungskontakt (Node) | `NodeId`, `Name`, `EmailAddress`, `Status` | Properties `billingcontact_name/_email`, `contact_*`, `banking_*` |
| **PriceList** / **PriceListUserContext** | `MAM.PriceList`, `MAM.PriceListUserContext` | Preisliste mit Prozentsätzen je UserContext oder UserContextGroup (`Percentage`) | `Name`, `Description`, `Status` | |
| **UserContextGroup** / `UserContextGroupUserContext` | `MAM.UserContextGroup*` | Gruppierung von UserContexts (z. B. Regionen-Bundle) für Preisbildung | `TranslationIdentifier` | |
| **Purchase** / **PurchasePrice** | `MAM.Purchase`, `MAM.PurchasePrice` | Lizenzkauf (Node vom Typ LicensePurchase) für einen Kontext/Gruppe, mit Start/Ablauf, Preisen je Währung, `OrderId` | `LicenseId`, `UserContextId`/`UserContextGroupId`, `UserId`, `StartDate`, `ExpiryDate` | Systemproperties `license_purchase_*`; E-Mails an Kunde/BillingContact/Admin |
| **Currency** | `MAM.Currency` | Währungen | `Identifier`, `ShortName` | |
| **LicenseOffer** (kein Table, Query-Ergebnis) | `LicenseOfferRepository` | Berechnetes Angebot: License + Buyouts + `PriceCalculationData` (Basis, Fee%, Kontext-%, TotalPurchaseDays) → `Prices` je Währung, `AlternativeGroups`, `IncludedAssetsCount`, Thumbnail | | |

Vollständige Tabellenliste mit Spalten: siehe Anhang A.

---

## 5. Controller und Endpoints (vollständig)

Basis-Route aller Controller: `api/v{version}/…` mit API-Versionen 1–6 (`[ApiVersion]`), Standard-`[Authorize]` (Bearer-Token, Schema `SharedienAuthentication`; Swagger optional per OIDC geschützt). Ausnahmen: `InfoController` und `PublicController` (`[AllowAnonymous]`, Zugriff über Share-Token). `MapToApiVersion` markiert versionsspezifische Varianten (z. B. V6-Suchergebnis mit Cursor). Obsolete Endpunkte (Chunk-Upload alt, Relations-GET, POST/PUT/DELETE einzelner Resource-Permissions) sind für Vollständigkeit aufgeführt. Paging überall via `limit`/`offset`, Gesamtzahl im Header `X-Total-Count`.

Gemeinsame Muster:

- **Property-Endpunkte** je Entität (Asset, Category, CustomEntity, Task, Workspace, License, RightsOwner, BillingContact): `GET properties`, `GET properties/{id}`, `POST properties` (Create), `PUT properties/{id}` (Replace), `PATCH properties/{id}` (Edit ohne Löschen fehlender Werte), `DELETE properties/{id}`, `PUT properties` (Set mehrere, ersetzt Werte), `PATCH properties` (Patch mehrere), `POST bulk-delete-properties`.
- **Resource-Permissions** je Node-Entität (Category, CustomEntity, Workspace, State, SavedSearch, Licenses global): `GET/PUT resource-permissions` (+ `force` zum Überschreiben), `resource-permission-settings` (InterruptInheritance).
- **Search** je Entitätstyp: `POST search` (Volltext + Filter + Facetten + Sort + Cursor), `POST quicksearch` (Autocomplete/"did you mean").
- **Activity-Logs**: `GET {id}/activity-logs` für Asset, Task, Workspace, ApplicationSettings.
- **Download**: `POST stream` (Zip als Stream, Accept-Header) und `POST stream-formdata` (Browser-Formular-Download über `FormDataJsonModelBinder`).

Aufgezählt: 463 Endpunkte (Skript-Extraktion aus `[HttpGet|HttpPost|HttpPut|HttpPatch|HttpDelete]`).

### ApplicationSettingsController — `api/v{v}/application-settings`

Datei: `ApplicationSettingsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Application settings operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetApplicationSettings | Retrieve a list of application settings. | `IList<ConfigurationValueExtendedDto>` |  |
| PATCH | `/` | SetApplicationSettings | Set multiple application settings. | `204` |  |
| GET | `/{path}` | GetApplicationSetting | Retrieve an application setting by path. | `ConfigurationValueExtendedDto` |  |
| DELETE | `/{path}` | DeleteApplicationSetting | Delete an application setting. | `204` |  |
| POST | `/bulk-delete` | DeleteApplicationSettings | Delete multiple application settings. | `204` |  |
| GET | `/activity-logs` | GetApplicationSettingsActivityLogs | Retrieve the activity log for application settings. | `IList<ActivityLogDto>` |  |

### AssetTypesController — `api/v{v}/asset-types`

Datei: `AssetTypesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Asset type operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetAssetTypes | Returns a list of all existing asset type identifiers | `ISet<string>` |  |

### AssetUploadController — `api/v{v}/assets`

Datei: `AssetUploadController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3 · Authorize: ja · Swagger-Gruppe: Asset upload operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/upload/start` | RequestAssetUpload | Requests an asset upload. A URL to request the first chunk upload is returned along with the storage provider's maximum chunk size | `UploadStartResponseDto` |  |
| GET | `/upload/chunk` | RequestAssetChunkUpload | Requests a chunk upload for an asset. Returns the chunk identifier, the storage provider URL to upload the chunk to, a URL to request the ne | `ChunkUploadResponseDto` |  |
| POST | `/upload/finish` | RequestAssetUploadFinish | Requests to finish an asset upload. Assembles the final asset from the provided chunks. Any remaining chunks will be discarded | `UploadFinishResponseDto` |  |

### AssetsController — `api/v{v}/assets`

Datei: `AssetsController.cs` · Basis: `FileTransferBaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Asset operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/search` | SearchAssets | Returns a list of assets for the defined search-query. | `SearchResultOfAssetV6Dto` | nur V6 |
| POST | `/search` | SearchAssets | Returns a list of assets for the defined search-query. | `SearchResultOfAssetDto` | obsolet, nur V5,V4,V3,V2,V1 |
| POST | `/quicksearch` | QuickSearchAssets | Returns a list of auto-complete suggestions or quick search assets ("did you mean" assets / or direct hits) for the defined search-query. | `QuickSearchResultOfAssetDto` |  |
| GET | `/{assetIdentifier}` | Get | Returns a single asset for the provided asset identifier. | `AssetDto` |  |
| GET | `/{assetIdentifier}/properties/{propertyIdentifier}` | GetAssetProperty | Returns a specific metadata property of an asset. If no version is provided, the metadata property of the latest version will be returned. | `PropertyDto` |  |
| GET | `/{assetIdentifier}/properties` | GetAssetProperties | Returns an asset's metadata properties. If no version is provided, the metadata properties of the latest version will be returned. | `IList<PropertyDto>` |  |
| POST | `/{assetIdentifier}/properties` | CreateAssetProperty | Create and add a metadata property to an asset. | `IdentifierRowVersionDto` |  |
| PUT | `/{assetIdentifier}/properties/{propertyIdentifier}` | UpdateAssetProperty | Update a existing metadata property of an asset. Will drop property values not included in this operation. | `RowVersionDto` |  |
| PATCH | `/{assetIdentifier}/properties/{propertyIdentifier}` | EditAssetProperty | Edit a metadata property of an asset. Does not delete existing property values that were not sent as part of the request. | `RowVersionDto` |  |
| DELETE | `/{assetIdentifier}/properties/{propertyIdentifier}` | DeleteAssetProperty | Delete a metadata property of an asset. | `204` |  |
| PUT | `/{assetIdentifier}/properties` | PutAssetProperties | Create or update multiple properties on an asset. Values of existing properties will be replaced by the values from the request. In case of  | `IList<SetPropertyResultDto>` |  |
| PATCH | `/{assetIdentifier}/properties` | PatchAssetProperties | Create or patch multiple properties on an asset. Values of existing properties will be patched by the values from the request. In case of tr | `IList<SetPropertyResultDto>` |  |
| POST | `/{assetIdentifier}/bulk-delete-properties` | DeleteAssetProperties | Deletes existing properties of an asset.  Returns a result for each property indicating whether the property was successfully deleted. | `IList<DeletePropertyResultDto>` |  |
| POST | `/stream-formdata` | DownloadCompressedAssetDerivationsForm | Generates a Zip file of specified derivations for the specified assets. This endpoint can be used to facilitate the download of a Zip file b | `Stream/204` |  |
| POST | `/stream` | DownloadCompressedAssetDerivations | Generates a Zip file of specified derivations for the specified assets. | `Stream/204` |  |
| DELETE | `/{assetIdentifier}` | Delete | Deletes the specified asset. | `204` |  |
| POST | `/bulk-delete` | BulkDeleteAssets | Deletes assets in bulk. A success status response indicates all assets have been deleted. | `204` |  |
| GET | `/{assetIdentifier}/activity-logs` | GetAssetActivityLogs | Retrieve the activity log for the specified asset. | `IList<ActivityLogDto>` |  |
| GET | `/{assetIdentifier}/derivations` | GetDerivationTypes | Returns a list of derivations for the specified asset. If no version is provided, the latest version will be used. | `IList<AssetDerivationDto>` |  |
| GET | `/{assetIdentifier}/derivations/{derivationTypeIdentifier}` | GetAvailableDerivationsByType | Retrieve a derivation for the specified asset. | `AssetDerivationDto` |  |
| POST | `/{assetIdentifier}/derivations/{derivationTypeIdentifier}` | StartOnDemandDerivation | Creates and enqueues a derivation of a on-demand derivation type for execution. | `IdentifierRowVersionDto` |  |
| GET | `/{assetIdentifier}/derivation-types` | GetPotentialDerivationTypesOfAsset | Retrieve all potential derivation types for the specified asset. | `IList<DerivationTypeDto>` |  |
| POST | `/{assetIdentifier}/derivations/upload/chunks/start` | StartNewDerivationUpload | (obsolet) This operation is obsolete and will be removed in a future A… | `StartUploadResponseDto` | obsolet |
| POST | `/{assetIdentifier}/derivations/upload/chunks/sessions/{uploadSessionId}/sequencenumber/{chunkUploadSequenceNumber}` | AppendDerivationChunk | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| DELETE | `/{assetIdentifier}/derivations/upload/chunks/sessions/{uploadSessionId}` | CancelDerivationUpload | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| POST | `/{assetIdentifier}/derivations/upload/chunks/sessions/{uploadSessionId}/finish` | FinishUpload | (obsolet) This operation is obsolete and will be removed in a future A… | `CreateAssetDerivationResponseDto` | obsolet |
| POST | `/upload/chunks/start` | StartNewUpload | (obsolet) This operation is obsolete and will be removed in a future A… | `StartUploadResponseDto` | obsolet |
| POST | `/upload/chunks/sessions/{uploadSessionId}/sequencenumber/{chunkUploadSequenceNumber}` | AppendChunk | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| DELETE | `/upload/chunks/sessions/{uploadSessionId}` | CancelUpload | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| POST | `/upload/chunks/sessions/{uploadSessionId}/finish` | FinishUpload | (obsolet) This operation is obsolete and will be removed in a future A… | `CreateAssetResponseDto` | obsolet |
| POST | `/{assetIdentifier}/upload/chunks/start` | StartNewVersionUploadV1V2 | (obsolet) This operation is obsolete and will be removed in a future A… | `StartUploadResponseDto` | obsolet, nur V2,V1 |
| POST | `/{assetIdentifier}/upload/chunks/start` | StartNewVersionUploadV3 | (obsolet) This operation is obsolete and will be removed in a future A… | `StartUploadResponseDto` | obsolet, nur V3 |
| POST | `/{assetIdentifier}/upload/chunks/sessions/{uploadSessionId}/finish` | FinishVersionUploadV1V2 | (obsolet) This operation is obsolete and will be removed in a future A… | `CreateAssetVersionResponseDto` | obsolet, nur V2,V1 |
| POST | `/{assetIdentifier}/upload/chunks/sessions/{uploadSessionId}/finish` | FinishVersionUploadV3 | (obsolet) This operation is obsolete and will be removed in a future A… | `CreateAssetVersionResponseDto` | obsolet, nur V3 |
| GET | `/{assetIdentifier}/versions` | GetVersions | Returns a list of versions available for the provided assetIdentifier. Paging is expressed through the offset and limit parameters. To displ | `IList<AssetVersionDto>` |  |
| GET | `/{assetIdentifier}/relations` | GetRelationsOfAsset | (obsolet) This operation is obsolete and will be removed in a future A… | `IList<RelationDto>` | obsolet, nur V4,V3,V2,V1 |
| POST | `/{assetIdentifier}/relations` | CreateRelation | Creates a new relationship from an asset to the provided entities. | `IdentifierRowVersionDto` |  |
| DELETE | `/{assetIdentifier}/relations/{relationIdentifier}` | DeleteRelation | Deletes a relation from an asset set to any other entity, or to an asset set from any other entity, by relation identifier. | `204` |  |
| POST | `/{assetIdentifier}/relations/search` | SearchRelationsOfAsset | Search for relations of an asset. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of 10, use | `IList<RelationDto>` |  |
| GET | `/duplicates` | GetAssetSetDuplicates | Returns a list of duplicated assets. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of 10,  | `IList<AssetDuplicateDto>` |  |
| POST | `/contactprint/export` | ExportContactPrint | Exports the contact print pdf as stream. | `Stream/204` |  |
| POST | `/contactprint/export-formdata` | ExportContactPrintForm | Exports the contact print pdf as stream. This endpoint can be used to facilitate the download of the export file by a browser through an HTM | `Stream/204` |  |
| POST | `/relations/search` | SearchRelations | (obsolet) This operation is obsolete and will be removed in a future A… | `IList<AssetSetRelationsDto>` | obsolet, nur V4,V3,V2,V1 |
| POST | `/relations/search` | SearchAssetSetRelations | Returns a list of relations for the requested asset sets. | `IList<AssetSetRelationsDto>` | nur V6,V5 |
| POST | `/tasks/get` | GetTasksOfAssets | Retrieve the tasks linked to a list of assets. | `AssetTasksDto` |  |
| POST | `/validate` | ValidateAssetSetProperties | Validates all properties from an asset set based on the specified property set conditions. | `IList<ValidateAssetSetPropertiesResultDto>` |  |
| POST | `/derivations/get` | GetBulkAssetDerivations | Gets asset derivation information for a list of asset identifiers, optionally filtered by a list of derivation type identifiers. | `AssetDerivationsResponseDto` |  |

### CategoriesController — `api/v{v}/categories`

Datei: `CategoriesController.cs` · Basis: `FileTransferBaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Category operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/{categoryIdentifier}/stream-formdata` | DownloadCompressedCategoryForm | Generates a Zip file of specified derivations for the specified category with all containing assets. This endpoint can be used to facilitate | `Stream/204` |  |
| POST | `/{categoryIdentifier}/stream` | DownloadCompressedCategory | Generates a Zip file of specified derivations for the specified category with all containing assets | `Stream/204` |  |
| GET | `/` | GetCategoryTree | Returns the category tree. | `CategoryDto` |  |
| GET | `/{categoryIdentifier}` | GetCategory | Returns a single category and its direct children | `CategoryDto` |  |
| POST | `/{parentCategoryIdentifier}` | CreateNewCategory | Creates a sub-category | `PropertyEntityCreationResultDto` |  |
| DELETE | `/{categoryIdentifier}` | DeleteCategory | Deletes the specified category | `204` |  |
| PATCH | `/{categoryIdentifier}` | UpdateCategory | Updates an existing category | `RowVersionDto` |  |
| PUT | `/{categoryIdentifier}/move` | MoveCategory | Moves a category to another parent category. | `204` |  |
| PUT | `/{categoryIdentifier}/assets/move` | MoveAssetsToCategory | Moves existing assets from a category into another already existing category | `204` |  |
| PUT | `/{sourceCategoryIdentifier}/copy` | CopyCategory | Copies an existing category to a new parent category, the contained assets are going to be linked to the copied categories | `IdentifierRowVersionDto` |  |
| PUT | `/{categoryIdentifier}/link/assets/{assetIdentifier}/` | LinkAssetToCategory | Links an existing asset into another already existing category. Existing links to other categories will be left as is. | `204` |  |
| PUT | `/{categoryIdentifier}/unlink/assets/{assetIdentifier}/` | UnlinkAssetFromCategory | Unlinks an existing asset from a category. | `204` |  |
| GET | `/{categoryIdentifier}/resource-permissions` | GetResourcePermissions | Returns the resource permissions on a single category | `IList<ResourcePermissionDto>` |  |
| PUT | `/{categoryIdentifier}/resource-permissions` | UpdateCategoryResourcePermissions | Assigns resource permissions to this category | `IList<IdentifierRowVersionDto>` |  |
| POST | `/{categoryIdentifier}/resource-permissions` | CreateResourcePermissions | (obsolet) This operation is obsolete and will be removed in a future A… | `IList<IdentifierRowVersionDto>` | obsolet |
| PUT | `/{categoryIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | UpdateResourcePermission | (obsolet) This operation is obsolete and will be removed in a future A… | `RowVersionDto` | obsolet |
| DELETE | `/{categoryIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | DeleteResourcePermission | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| PUT | `/{categoryIdentifier}/resource-permission-settings` | UpdateResourcePermissionSettings | Update the resource permission settings for a category | `204` |  |
| GET | `/{categoryIdentifier}/resource-permission-settings` | GetResourcePermissionSettings | Returns the resource permission settings for the selected category | `ResourcePermissionSettingsDto` |  |
| GET | `/{categoryIdentifier}/properties` | GetProperties | Returns the metadata properties of a category. | `IList<PropertyDto>` |  |
| GET | `/{categoryIdentifier}/properties/{propertyIdentifier}` | GetProperty | Returns a specific metadata property of a category. | `PropertyDto` |  |
| POST | `/{categoryIdentifier}/properties` | CreateProperty | Create and add a metadata property to a category. | `IdentifierRowVersionDto` |  |
| PUT | `/{categoryIdentifier}/properties/{propertyIdentifier}` | UpdateProperty | Update an existing metadata Property of the specified category. Will drop property values not included in this operation. | `RowVersionDto` |  |
| PATCH | `/{categoryIdentifier}/properties/{propertyIdentifier}` | EditProperty | Edit a metadata property of a category. Does not delete existing property values that were not sent as part of the request. | `RowVersionDto` |  |
| DELETE | `/{categoryIdentifier}/properties/{propertyIdentifier}` | DeleteProperty | Delete a metadata property of a category. | `204` |  |
| PUT | `/{categoryIdentifier}/properties` | PutCategoryProperties | Create or update multiple properties on a category. Values of existing properties will be replaced by the values from the request. In case o | `IList<SetPropertyResultDto>` |  |
| PATCH | `/{categoryIdentifier}/properties` | PatchCategoryProperties | Create or patch multiple properties on a category. Values of existing properties will be patched by the values from the request. In case of  | `IList<SetPropertyResultDto>` |  |
| POST | `/{categoryIdentifier}/bulk-delete-properties` | DeleteCategoryProperties | Deletes existing properties of a category.  Returns a result for each property indicating whether the property was successfully deleted. | `IList<DeletePropertyResultDto>` |  |
| PUT | `/{categoryIdentifier}/properties/{propertyIdentifier}/inheritance-node-types` | UpdatePropertyInheritance | Update the inheritance mapping for the Property propertyIdentifier Will remove all existing mappings and re | `204` |  |
| POST | `/quicksearch` | QuickSearchCategories | Returns a list of quick search categories for the defined search-query. | `QuickSearchResultOfCategoryDto` |  |

### CommentsController — `api/v{v}/comments`

Datei: `CommentsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Comment operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{commentIdentifier}` | GetComment | Returns a single comment for the provided identifier | `CommentDto` |  |
| PUT | `/{commentIdentifier}` | UpdateComment | Updates an existing comment | `RowVersionDto` |  |
| DELETE | `/{commentIdentifier}` | DeleteComment | Deletes the specified comment | `204` |  |

### CustomEntitiesController — `api/v{v}/custom-entities`

Datei: `CustomEntitiesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Custom entity operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{entityTypeIdentifier}/{customEntityIdentifier}` | GetCustomEntity | Returns a single custom entity. | `CustomEntityDto` |  |
| GET | `/{entityTypeIdentifier}` | GetCustomEntities | Returns a list of custom entities. | `IList<CustomEntityDto>` |  |
| POST | `/search` | SearchCustomEntities | Returns a list of custom entities for the defined search-query. | `SearchResultOfCustomEntityV6Dto` | nur V6 |
| POST | `/search` | SearchCustomEntities | Returns a list of custom entities for the defined search-query. | `SearchResultOfCustomEntityDto` | obsolet, nur V5,V4,V3,V2,V1 |
| POST | `/quicksearch` | QuickSearchCustomEntities | Returns a list of auto-complete suggestions or quick search custom entities ("did you mean" / or direct hits) for the defined search-query. | `QuickSearchResultOfCustomEntityDto` |  |
| POST | `/` | CreateCustomEntity | Creates a custom entity. | `PropertyEntityCreationResultDto` |  |
| PUT | `/{entityTypeIdentifier}/{customEntityIdentifier}` | UpdateCustomEntity | Updates a custom entity. | `RowVersionDto` |  |
| DELETE | `/{entityTypeIdentifier}/{customEntityIdentifier}` | DeleteCustomEntity | Deletes a custom entity. | `204` |  |
| GET | `/{entityTypeIdentifier}/{customEntityIdentifier}/resource-permissions` | GetResourcePermissions | Returns the resource permissions on a custom entity. | `IList<ResourcePermissionDto>` |  |
| PUT | `/{entityTypeIdentifier}/{customEntityIdentifier}/resource-permissions` | UpdateCustomEntityResourcePermissions | Assigns resource permissions to this custom entity | `IList<IdentifierRowVersionDto>` |  |
| POST | `/{entityTypeIdentifier}/{customEntityIdentifier}/resource-permissions` | CreateResourcePermissions | (obsolet) This operation is obsolete and will be removed in a future A… | `IList<IdentifierRowVersionDto>` | obsolet |
| PUT | `/{entityTypeIdentifier}/{customEntityIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | UpdateResourcePermission | (obsolet) This operation is obsolete and will be removed in a future A… | `RowVersionDto` | obsolet |
| DELETE | `/{entityTypeIdentifier}/{customEntityIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | DeleteResourcePermission | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| GET | `/{entityTypeIdentifier}/{customEntityIdentifier}/relations` | GetRelations | (obsolet) This operation is obsolete and will be removed in a future A… | `IList<RelationDto>` | obsolet, nur V4,V3,V2,V1 |
| POST | `/{entityTypeIdentifier}/{customEntityIdentifier}/relations` | CreateRelation | Creates a new relationship from a custom entity to the provided entities. | `IdentifierRowVersionDto` |  |
| DELETE | `/{entityTypeIdentifier}/{customEntityIdentifier}/relations/{relationIdentifier}` | DeleteRelation | Deletes a relation from the custom entity to any other entity by relation identifier. | `204` |  |
| POST | `/{entityTypeIdentifier}/{customEntityIdentifier}/relations/search` | SearchRelationsOfCustomEntity | Search for relations of a custom entity. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of  | `IList<RelationDto>` |  |
| GET | `/{entityTypeIdentifier}/{customEntityIdentifier}/properties` | GetProperties | Returns the metadata properties of a custom entity. | `IList<PropertyDto>` |  |
| GET | `/{entityTypeIdentifier}/{customEntityIdentifier}/properties/{propertyIdentifier}` | GetProperty | Returns a specific metadata property of a custom entity. | `PropertyDto` |  |
| POST | `/{entityTypeIdentifier}/{customEntityIdentifier}/properties` | CreateProperty | Create and add a list of metadata properties to a custom entity. | `IList<IdentifierRowVersionDto>` |  |
| PUT | `/{entityTypeIdentifier}/{customEntityIdentifier}/properties/{propertyIdentifier}` | UpdateProperty | Update an existing metadata property of the specified custom entity. Will drop property values not included in this operation. | `RowVersionDto` |  |
| PATCH | `/{entityTypeIdentifier}/{customEntityIdentifier}/properties/{propertyIdentifier}` | EditProperty | Edit a metadata property of a custom entity. Does not delete existing property values that were not sent as part of the request. | `RowVersionDto` |  |
| DELETE | `/{entityTypeIdentifier}/{customEntityIdentifier}/properties/{propertyIdentifier}` | DeleteProperty | Delete a metadata property of a custom entity. | `204` |  |
| PUT | `/{entityTypeIdentifier}/{customEntityIdentifier}/properties` | PutCustomEntityProperties | Create or update multiple properties on a custom entity. Values of existing properties will be replaced by the values from the request. In c | `IList<SetPropertyResultDto>` |  |
| PATCH | `/{entityTypeIdentifier}/{customEntityIdentifier}/properties` | PatchCustomEntityProperties | Create or patch multiple properties on a custom entity. Values of existing properties will be patched by the values from the request. In cas | `IList<SetPropertyResultDto>` |  |
| POST | `/{entityTypeIdentifier}/{customEntityIdentifier}/bulk-delete-properties` | DeleteCustomEntityProperties | Deletes existing properties of a custom entity.  Returns a result for each property indicating whether the property was successfully deleted | `IList<DeletePropertyResultDto>` |  |

### DataLanguagesController — `api/v{v}/data-languages`

Datei: `DataLanguagesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Data language operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{dataLanguageIdentifier}` | GetDataLanguage | Returns a single data language for the provided identifier | `DataLanguageDto` |  |
| GET | `/` | GetDataLanguages | Returns a list of data languages. | `IList<DataLanguageDto>` |  |
| POST | `/` | CreateDataLanguage | Create a new data language | `IdentifierRowVersionDto` |  |
| PUT | `/{dataLanguageIdentifier}` | UpdateDataLanguage | Updates an existing data language | `RowVersionDto` |  |
| DELETE | `/{dataLanguageIdentifier}` | DeleteDataLanguage | Deletes the specified data language | `204` |  |

### DerivationJobDefinitionsController — `api/v{v}/derivation-job-definitions`

Datei: `DerivationJobDefinitionsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Derivation job definition operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{derivationJobDefinitionIdentifier}` | GetDerivationJobDefinition | Retrieve a specific derivation job definition. | `DerivationJobDefinitionDto` |  |
| GET | `/` | GetDerivationJobDefinitions | Retrieve a list of derivation job definitions. | `IList<DerivationJobDefinitionDto>` |  |
| POST | `/` | CreateDerivationJobDefinition | Create a new derivation job definition. | `IdentifierRowVersionDto` |  |
| PUT | `/{derivationJobDefinitionIdentifier}` | UpdateDerivationJobDefinition | Updates an existing derivation job definition. | `RowVersionDto` |  |
| DELETE | `/{derivationJobDefinitionIdentifier}` | DeleteDerivationJobDefinition | Deletes the specified derivation job definition. | `204` |  |

### DerivationTypesController — `api/v{v}/derivation-types`

Datei: `DerivationTypesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Derivation type operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{derivationTypeIdentifier}` | GetDerivationType | Retrieve a specific derivation type. | `DerivationTypeDto` |  |
| GET | `/` | GetDerivationTypes | Retrieve a list of derivation types. | `IList<DerivationTypeDto>` |  |
| POST | `/` | CreateDerivationType | Create a new derivation type. | `IdentifierRowVersionDto` |  |
| PUT | `/{derivationTypeIdentifier}` | UpdateDerivationType | Updates an existing derivation type. | `RowVersionDto` |  |
| DELETE | `/{derivationTypeIdentifier}` | DeleteDerivationType | Deletes an existing derivation type. | `204` |  |
| GET | `/grouped` | GetAvailableDerivationsForAssetTypes | Returns a list of available derivations grouped by asset types | `IList<AssetTypeDto>` |  |

### DerivationsController — `api/v{v}/derivations`

Datei: `DerivationsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Derivation operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/` | StartDerivationFromJobDefinition | Creates and enqueues derivations of a derivation job definition for execution. | `IdentifierRowVersionDto` |  |
| POST | `/restart` | StartAssetReDerivationFromAssetIdentifiers | Restart the process of generating derivative files of multiple assets. Returns a background job identifier which can be used to retrieve a r | `IdentifierRowVersionDto` |  |
| POST | `/restart/search` | StartAssetReDerivationFromSearchQuery | Restart the process of generating derivative files of multiple assets from a search-query. Returns a background job identifier which can be  | `IdentifierRowVersionDto` |  |
| GET | `/status/overview` | GetStatusOverview | Returns the count of current pending or failed derivations. | `DerivationStatusOverviewDto` |  |
| GET | `/status/types` | GetStatusTypes | Returns an aggregated, paged list of current derivations, containing asset type, derivation type, status and count. | `IList<DerivationStatusTypesDto>` |  |
| GET | `/status/details` | GetStatusDetails | Returns a paged list of current derivations, containing asset information, derivation type, status and retry count. | `IList<DerivationStatusDetailsDto>` |  |

### EntityTypesController — `api/v{v}/entity-types`

Datei: `EntityTypesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Entity type operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{entityTypeIdentifier}` | GetEntityType | Returns a single entity type. | `EntityTypeDto` |  |
| GET | `/` | GetEntityTypes | Returns a list of entity types. | `IList<EntityTypeDto>` |  |
| GET | `/asset-search-types` | GetEntityTypesForAssetSearch | Returns node types in hierarchy that are used for asset search | `IList<EntityTypeDto>` |  |
| POST | `/` | CreateEntityType | Creates a entity type. | `IdentifierRowVersionDto` |  |
| PUT | `/{entityTypeIdentifier}` | UpdateEntityType | Updates an entity type. | `RowVersionDto` |  |
| DELETE | `/{entityTypeIdentifier}` | DeleteEntityType | Deletes a entity type. System entity types can not be deleted. | `204` |  |

### ExternalGroupsController — `api/v{v}/external-groups`

Datei: `ExternalGroupsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: External group operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{externalGroupIdentifier}` | GetExternalGroup | Retrieve a external group | `ExternalGroupDto` |  |
| GET | `/` | GetExternalGroupsV1 | Retrieve all external groups | `IList<ExternalGroupDto>` | nur V1 |
| GET | `/` | GetExternalGroupsV2 | Retrieve all external groups. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use off | `IList<ExternalGroupDto>` | nur V6,V5,V4,V3,V2 |
| POST | `/` | CreateExternalGroup | Create an external group | `IdentifierRowVersionDto` |  |
| PUT | `/{externalGroupIdentifier}` | UpdateExternalGroup | Update an external group | `RowVersionDto` |  |
| DELETE | `/{externalGroupIdentifier}` | DeleteExternalGroup | Deletes an external group | `Stream/204` |  |

### ExtractionsController — `api/v{v}/extractions`

Datei: `ExtractionsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Extraction operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/` | ExtractArchiveAsset | Starts a job to extract a zip archive asset | `IdentifierRowVersionDto` |  |
| GET | `/{jobIdentifier}` | GetArchiveExtractionStatusAsync | Gets the status of a job. | `JobStatusDto` |  |

### FileNamingDefinitionsController — `api/v{v}/file-naming-definitions`

Datei: `FileNamingDefinitionsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: File naming operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetFileNamingDefinitions | Returns a list of file naming definitions. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size o | `IList<FileNamingDefinitionDto>` |  |
| POST | `/` | CreateFileNamingDefinition | Create a new file naming definition. | `IdentifierRowVersionDto` |  |
| GET | `/tokens` | GetFileNamingTokens | Returns a list of available file naming tokens. | `IList<FileNamingTokenDto>` |  |
| GET | `/{fileNamingDefinitionIdentifier}` | GetFileNamingDefinition | Retrieve a single file naming definition. | `FileNamingDefinitionDto` |  |
| PUT | `/{fileNamingDefinitionIdentifier}` | UpdateFileNamingDefinition | Updates a file naming definition. | `RowVersionDto` |  |
| DELETE | `/{fileNamingDefinitionIdentifier}` | DeleteFileNamingDefinition | Deletes the specified file naming definition. | `204` |  |
| POST | `/{fileNamingDefinitionIdentifier}/preview` | PreviewFileNamingDefinition | Apply a file naming definition to a set of assets and retrieve a preview of the resulting file names. | `IList<AssetSetDerivationFileNameDto>` |  |

### FileTypesController — `api/v{v}/file-types`

Datei: `FileTypesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: File type operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetFileTypes | Returns a list of file types. | `IList<FileTypeDto>` |  |
| GET | `/forbidden` | GetForbiddenFileTypes | Returns a list of forbidden upload file types | `IList<string>` |  |

### FileReferencesController — `api/v{v}/files`

Datei: `FilesReferenceController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: File reference operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/upload` | UploadFileReference | Uploads a new file or a changed file. No identifier is specified for a new file. | `CreateFileResponseDto` |  |
| GET | `/` | GetFileReferences | Retrieve a list of files. | `IList<FileDto>` |  |
| DELETE | `/{fileIdentifier}` | DeleteFileReference | Deletes an existing file. | `204` |  |

### InfoController — `api/v{v}/info`

Datei: `InfoController.cs` · Basis: `ControllerBase` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: nein (anonym) · Swagger-Gruppe: Info operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | Get | Returns a dictionary of information about this API | `Dictionary<string, string>` |  |

### JobsController — `api/v{v}/jobs`

Datei: `JobsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Job operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/mass-edits` | GetPropertyMassEdits | Get a list of mass edit jobs. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use off | `IList<PropertyMassEditDto>` | obsolet, nur V1,V2,V3 |
| GET | `/mass-edits` | GetPropertyMassEditsV4 | Get a list of mass edit jobs. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of 10, use off | `IList<PropertyMassEditV4Dto>` | nur V6,V5,V4 |
| GET | `/mass-edits/{massEditIdentifier}/report` | GetPropertyMassEdit | Get the report of a mass edit job. | `PropertyMassEditReportDto` | obsolet, nur V1,V2,V3 |
| GET | `/mass-edits/{massEditIdentifier}/report` | GetPropertyMassEditJobReportV4 | Get the report of a mass edit job. | `PropertyMassEditReportV4Dto` | nur V6,V5,V4 |
| GET | `/mass-edits/{massEditIdentifier}/mutations` | GetPropertyMassEditAssetMutations | Get the property mutations of a mass edit job. Paging is expressed through the offset and limit parameters. To display page 3 with a page si | `IList<AssetPropertyMutationDto>` | nur V6,V5,V4 |
| GET | `/derivations` | GetAssetDerivationJobs | Get a list of asset derivation jobs. | `IList<AssetDerivationJobDto>` |  |
| GET | `/derivations/{backgroundJobIdentifier}` | GetAssetDerivationJob | Get an asset derivation job. | `AssetDerivationJobReportDto` |  |
| GET | `/imports` | GetPropertyImports | Get a list of property imports. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use o | `IList<PropertyImportDto>` |  |
| GET | `/imports/{propertyImportIdentifier}` | GetPropertyImport | Get a single property import. | `PropertyImportDto` |  |
| GET | `/extractions` | GetArchiveExtractionJobs | Get a list of archive extraction jobs. | `IList<ArchiveExtractionJobDto>` |  |
| GET | `/extractions/{archiveExtractionJobIdentifier}/report` | GetArchiveExtractionJobReport | Get an archive extraction job report. | `ArchiveExtractionJobReportDto` |  |
| GET | `/property-extractions` | GetPropertyExtractionJobs | Get a list of property extraction jobs. | `IList<PropertyExtractionJobDto>` |  |
| GET | `/property-extractions/{propertyExtractionJobIdentifier}/report` | GetPropertyExtractionJob | Get a property extraction job. | `PropertyExtractionJobDto` |  |
| GET | `/property-extractions/{propertyExtractionJobIdentifier}/extractions` | GetPropertyExtractionJobResults | Get the extraction results of a property extraction job. | `IList<PropertyExtractionJobAssetResultDto>` |  |

### DrmLicensesController (partial) — `api/v{v}/drm`

Datei: `LicenseManagement\BillingContactsController.cs` · Basis: `?` · API-Versionen: V6,V5 · Authorize: ja · Swagger-Gruppe: DRM License operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/billing-contacts` | CreateBillingContact | Creates a new billing contact | `PropertyEntityCreationResultDto` |  |
| GET | `/billing-contacts/{billingContactIdentifier}` | GetBillingContact | Get a specific billing contact | `BillingContactDto` |  |
| PUT | `/billing-contacts/{billingContactIdentifier}` | UpdateBillingContact | Update a specific billing contact | `RowVersionDto` |  |
| DELETE | `/billing-contacts/{billingContactIdentifier}` | DeleteBillingContact | Delete a specific billing contact | `204` |  |
| POST | `/billing-contacts/search` | SearchBillingContacts | Returns a list of billing contacts for the defined search-query. | `SearchResultOfBillingContactDto` | obsolet, nur V5 |
| POST | `/billing-contacts/search` | SearchBillingContacts | Returns a list of billing contacts for the defined search-query. | `SearchResultOfBillingContactV6Dto` | nur V6 |
| POST | `/billing-contacts/quicksearch` | QuickSearchBillingContacts | Returns a list of auto-complete suggestions or quick search billing contact ("did you mean" / or direct hits) for the defined search-query. | `QuickSearchResultOfBillingContactDto` |  |
| GET | `/billing-contacts/{billingContactIdentifier}/properties` | GetBillingContactProperties | Return the metadata properties of a billing contact. | `IList<PropertyDto>` |  |
| PUT | `/billing-contacts/{billingContactIdentifier}/properties` | PutBillingContactProperties | Create or update multiple properties on a billing contact. Values of existing properties will be replaced by the values from the request. In | `IList<SetPropertyResultDto>` |  |
| PATCH | `/billing-contacts/{billingContactIdentifier}/properties` | PatchBillingContactProperties | Create or patch multiple properties on a billing contact. Values of existing properties will be patched by the values from the request. In c | `IList<SetPropertyResultDto>` |  |
| POST | `/billing-contacts/{billingContactIdentifier}/bulk-delete-properties` | DeleteBillingContactProperties | Deletes existing properties of a billing contact.  Returns a result for each property indicating whether the property was successfully delet | `IList<DeletePropertyResultDto>` |  |

### DrmLicensesController (partial) — `api/v{v}/drm`

Datei: `LicenseManagement\LicenseOffersController.cs` · Basis: `?` · API-Versionen: V6,V5 · Authorize: ja · Swagger-Gruppe: DRM License operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/license-offers/search` | SearchLicenseOffers | Search for license offers | `IList<LicenseOfferDto>` |  |

### DrmLicensesController (partial) — `api/v{v}/drm`

Datei: `LicenseManagement\LicensesController.cs` · Basis: `?` · API-Versionen: V6,V5 · Authorize: ja · Swagger-Gruppe: DRM License operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/licenses` | CreateLicense | Creates a new license | `IdentifierRowVersionDto` |  |
| GET | `/licenses/{licenseIdentifier}` | GetLicense | Get a specific license | `LicenseDto` |  |
| PUT | `/licenses/{licenseIdentifier}` | UpdateLicense | Update a specific license | `RowVersionDto` |  |
| DELETE | `/licenses/{licenseIdentifier}` | DeleteLicense | Delete a specific license | `204` |  |
| POST | `/licenses/search` | SearchLicenses | Returns a list of licenses for the defined search-query. | `SearchResultOfLicenseDto` | obsolet, nur V5 |
| POST | `/licenses/search` | SearchLicenses | Returns a list of licenses for the defined search-query. | `SearchResultOfLicenseV6Dto` | nur V6 |
| POST | `/licenses/quicksearch` | QuickSearchLicenses | Returns a list of auto-complete suggestions or quick search license ("did you mean" / or direct hits) for the defined search-query. | `QuickSearchResultOfLicenseDto` |  |
| GET | `/licenses/resource-permissions` | GetResourcePermissions | Returns the resource permissions of all licenses, rights owners and billing contacts | `IList<ResourcePermissionDto>` |  |
| PUT | `/licenses/resource-permissions` | UpdateLicenseResourcePermissions | Assigns resource permissions to all licenses, rights owners and billing contacts | `IList<IdentifierRowVersionDto>` |  |
| GET | `/licenses/{licenseIdentifier}/entities` | GetLicenseEntities | Get entities that are assigned to a license | `IList<EntityExtendedDto>` |  |
| POST | `/licenses/{licenseIdentifier}/entities` | AddEntitiesToLicense | Adds entities to an existing license | `IList<CreateRelationResultDto>` |  |
| POST | `/licenses/{licenseIdentifier}/entities/remove` | RemoveEntitiesFromLicense | Removes entities from an existing license | `204` |  |
| GET | `/licenses/{licenseIdentifier}/properties` | GetLicenseProperties | Return the metadata properties of a license. | `IList<PropertyDto>` |  |
| PUT | `/licenses/{licenseIdentifier}/properties` | PutLicenseProperties | Create or update multiple properties on a license. Values of existing properties will be replaced by the values from the request. In case of | `IList<SetPropertyResultDto>` |  |
| PATCH | `/licenses/{licenseIdentifier}/properties` | PatchLicenseProperties | Create or patch multiple properties on a license. Values of existing properties will be patched by the values from the request. In case of t | `IList<SetPropertyResultDto>` |  |
| POST | `/licenses/{licenseIdentifier}/bulk-delete-properties` | DeleteLicenseProperties | Deletes existing properties of a license.  Returns a result for each property indicating whether the property was successfully deleted. | `IList<DeletePropertyResultDto>` |  |
| GET | `/licenses/{licenseIdentifier}/buyouts` | GetBuyouts | Get a list of buyouts | `IList<BuyoutDto>` |  |
| POST | `/licenses/{licenseIdentifier}/buyouts` | CreateBuyout | Creates a new buyout | `IdentifierRowVersionDto` |  |
| GET | `/licenses/{licenseIdentifier}/buyouts/{buyoutIdentifier}` | GetBuyout | Get a specific buyout | `BuyoutDto` |  |
| PUT | `/licenses/{licenseIdentifier}/buyouts/{buyoutIdentifier}` | UpdateBuyout | Update a specific buyout | `RowVersionDto` |  |
| DELETE | `/licenses/{licenseIdentifier}/buyouts/{buyoutIdentifier}` | DeleteBuyout | Delete a specific buyout | `204` |  |

### DrmLicensesController (partial) — `api/v{v}/drm`

Datei: `LicenseManagement\PriceListsController.cs` · Basis: `?` · API-Versionen: V6,V5 · Authorize: ja · Swagger-Gruppe: DRM License operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/price-lists` | GetPriceLists | Returns a list of price lists. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use of | `IList<PriceListDto>` |  |
| POST | `/price-lists` | CreatePriceList | Creates a new price list | `IdentifierRowVersionDto` |  |
| GET | `/price-lists/{priceListIdentifier}` | GetPriceList | Get a specific price list | `PriceListDto` |  |
| PUT | `/price-lists/{priceListIdentifier}` | UpdatePriceList | Update a specific price list | `RowVersionDto` |  |
| DELETE | `/price-lists/{priceListIdentifier}` | DeletePriceList | Delete a specific price list | `204` |  |

### DrmLicensesController (partial) — `api/v{v}/drm`

Datei: `LicenseManagement\PurchasesController.cs` · Basis: `?` · API-Versionen: V6,V5 · Authorize: ja · Swagger-Gruppe: DRM License operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/purchases` | CreatePurchase | Creates a new purchase | `IdentifierRowVersionDto` |  |
| POST | `/purchases/search` | SearchPurchases | Search for purchases | `SearchResultOfPurchaseDto` |  |

### DrmLicensesController (partial) — `api/v{v}/drm`

Datei: `LicenseManagement\RightsOwnersController.cs` · Basis: `?` · API-Versionen: V6,V5 · Authorize: ja · Swagger-Gruppe: DRM License operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/rights-owners` | CreateRightsOwner | Creates a new rights owner If the creation of properties fails, the rights owner will still be created | `PropertyEntityCreationResultDto` |  |
| GET | `/rights-owners/{rightsOwnerIdentifier}` | GetRightsOwner | Get a specific rights owner | `RightsOwnerDto` |  |
| PUT | `/rights-owners/{rightsOwnerIdentifier}` | UpdateRightsOwner | Update a specific rights owner | `RowVersionDto` |  |
| DELETE | `/rights-owners/{rightsOwnerIdentifier}` | DeleteRightsOwner | Delete a specific rights owner | `204` |  |
| POST | `/rights-owners/search` | SearchRightsOwners | Returns a list of rights owners for the defined search-query. | `SearchResultOfRightsOwnerDto` | obsolet, nur V5 |
| POST | `/rights-owners/search` | SearchRightsOwners | Returns a list of rights owners for the defined search-query. | `SearchResultOfRightsOwnerV6Dto` | nur V6 |
| POST | `/rights-owners/quicksearch` | QuickSearchRightsOwners | Returns a list of auto-complete suggestions or quick search rights owner ("did you mean" / or direct hits) for the defined search-query. | `QuickSearchResultOfRightsOwnerDto` |  |
| GET | `/rights-owners/{rightsOwnerIdentifier}/properties` | GetRightsOwnerProperties | Return the metadata properties of a rights owner. | `IList<PropertyDto>` |  |
| PUT | `/rights-owners/{rightsOwnerIdentifier}/properties` | PutRightsOwnerProperties | Create or update multiple properties on a rights owner. Values of existing properties will be replaced by the values from the request. In ca | `IList<SetPropertyResultDto>` |  |
| PATCH | `/rights-owners/{rightsOwnerIdentifier}/properties` | PatchRightsOwnerProperties | Create or patch multiple properties on a rights owner. Values of existing properties will be patched by the values from the request. In case | `IList<SetPropertyResultDto>` |  |
| POST | `/rights-owners/{rightsOwnerIdentifier}/bulk-delete-properties` | DeleteRightsOwnerProperties | Deletes existing properties of a rights owner.  Returns a result for each property indicating whether the property was successfully deleted. | `IList<DeletePropertyResultDto>` |  |
| POST | `/rights-owners/{rightsOwnerIdentifier}/avatar/upload` | UploadRightsOwnerAvatar | Uploads the avatar file for this rights owner. | `CreateFileResponseDto` |  |
| DELETE | `/rights-owners/{rightsOwnerIdentifier}/avatar` | DeleteRightsOwnerAvatar | Deletes the avatar of this rights owner. | `204` |  |

### DrmLicensesController (partial) — `api/v{v}/drm`

Datei: `LicenseManagement\UserContextGroupsController.cs` · Basis: `?` · API-Versionen: V6,V5 · Authorize: ja · Swagger-Gruppe: DRM License operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/user-context-groups` | CreateUserContextGroup | Creates a new user context group | `IdentifierRowVersionDto` |  |
| GET | `/user-context-groups` | GetUserContextGroups | Get all user context groups | `IList<UserContextGroupDto>` |  |
| GET | `/user-context-groups/{contextGroupIdentifier}` | GetUserContextGroup | Get a specific user context group by identifier | `UserContextGroupDto` |  |
| PUT | `/user-context-groups/{userContextGroupIdentifier}` | UpdateUserContextGroup | Update a user context group | `RowVersionDto` |  |
| DELETE | `/user-context-groups/{userContextGroupIdentifier}` | DeleteUserContextGroup | Delete a user context group | `204` |  |

### MyController — `api/v{v}/my`

Datei: `MyController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: My operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/profile` | GetProfile | Returns the settings and all assigned roles from the current user. | `UserProfileDto` |  |
| PUT | `/profile/settings` | CreateOrUpdateSettings | Create or update the settings of the current user. | `204` |  |
| PUT | `/password` | ChangePassword | Changes the current user's password. | `204` |  |
| GET | `/activity-logs` | GetActivityLogNotifications | Retrieve the activity log notifications of the user. | `IList<ActivityLogNotificationDto>` |  |
| PUT | `/activity-logs/read` | SetActivityLogNotificationsRead | Set activity log notifications of the user to 'read'. | `204` |  |
| DELETE | `/activity-logs` | ClearActivityLogNotifications | Clear all the activity log notifications of the user. | `204` |  |
| POST | `/activity-logs/bulk-delete` | DeleteActivityLogNotifications | Delete activity log notifications of the user. | `204` |  |

### ObjectTypePermissionsController — `api/v{v}/object-type-permissions`

Datei: `ObjectTypePermissionsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Object type permission operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | Get | Returns a list of all available object type permissions | `IList<ObjectTypePermissionDto>` |  |

### PermissionMaterializationsController — `api/v{v}/permission-materializations`

Datei: `PermissionMaterializationsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Permission materialization operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetMaterializationJobs | Returns a list of materialization jobs according to the status filter | `IList<PermissionMaterializationDto>` |  |
| POST | `/` | ReMaterializeSubtree | Triggers a re-materialization of the subtree of the selected entity (including the selected entity) | `IdentifierRowVersionDto` |  |

### PropertiesController — `api/v{v}/properties`

Datei: `PropertiesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Property operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/mass-edit-jobs` | MassEditProperties | Create or replace metadata properties on assets. Returns a mass edit identifier. | `IdentifierRowVersionDto` |  |
| POST | `/mass-edit-jobs/search` | CreateMassEditJobFromSearchQuery | Create or replace metadata properties on multiple assets from a search-query. Returns a mass edit identifier. | `IdentifierRowVersionDto` |  |
| POST | `/asset/export-formdata` | ExportAssetPropertiesForm | Exports properties of one or many assets as defined in the options. This endpoint can be used to facilitate the download of the export file  | `200` |  |
| POST | `/asset/export` | ExportAssetProperties | Exports properties of one or many assets as defined in the options | `200` |  |
| POST | `/asset/import` | ImportAssetProperties | Imports properties of one or many assets | `IdentifierRowVersionDto` |  |
| POST | `/extract` | ExtractMetadata | Extracts metadata of multiple assets. This operation is obsolete and will be removed in a future API version. Please use the new extract ope | `204` | obsolet, nur V5,V4,V3,V2,V1 |
| POST | `/extract/search` | ExtractMetadataFromSearchQuery | Extracts metadata of multiple assets from a search-query. This operation is obsolete and will be removed in a future API version. Please use | `204` | obsolet, nur V5,V4,V3,V2,V1 |
| POST | `/extract` | ExtractMetadataV6 | Extracts metadata of multiple assets. | `IdentifierRowVersionDto` | nur V6 |
| POST | `/extract/search` | ExtractMetadataFromSearchQueryV6 | Extracts metadata of multiple assets from a search-query. | `IdentifierRowVersionDto` | nur V6 |
| GET | `/property-extraction-definitions` | GetPropertyExtractionDefinitions | Get a list of property extraction definitions. | `IList<PropertyExtractionDefinitionDto>` |  |
| POST | `/property-extraction-definitions` | CreatePropertyExtractionDefinition | Create a property extraction definition. | `IdentifierRowVersionDto` |  |
| GET | `/property-extraction-definitions/{propertyExtractionDefinitionIdentifier}` | GetPropertyExtractionDefinition | Get a property extraction definition. | `PropertyExtractionDefinitionDto` |  |
| PUT | `/property-extraction-definitions/{propertyExtractionDefinitionIdentifier}` | UpdatePropertyExtractionDefinition | Update a property extraction definition. | `RowVersionDto` |  |
| DELETE | `/property-extraction-definitions/{propertyExtractionDefinitionIdentifier}` | DeletePropertyExtractionDefinition | Delete a property extraction definition. | `204` |  |

### PropertyDefinitionsController — `api/v{v}/property-definitions`

Datei: `PropertyDefinitionsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Property definition operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetPropertyDefinitionsV1 | Returns a list of property definitions. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 1 | `IList<PropertyDefinitionDto>` | nur V1 |
| GET | `/` | GetPropertyDefinitionsV2 | Returns a list of property definitions. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 1 | `IList<PropertyDefinitionDto>` | nur V5,V4,V3,V2 |
| GET | `/` | GetPropertyDefinitionsV3 | Returns a list of property definitions. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 1 | `IList<PropertyDefinitionDto>` | nur V6 |
| GET | `/{propertyDefinitionIdentifier}` | GetPropertyDefinition | Retrieves a single property definition. | `PropertyDefinitionDto` |  |
| GET | `/value-types` | GetPropertyDefinitionValueTypes | Retrieve all value types. | `IList<string>` |  |
| POST | `/` | CreatePropertyDefinition | Create a new property definition. | `IdentifierRowVersionDto` |  |
| PUT | `/{propertyDefinitionIdentifier}` | UpdatePropertyDefinition | Updates a property definition. | `RowVersionDto` |  |
| DELETE | `/{propertyDefinitionIdentifier}` | DeletePropertyDefinition | Deletes the specified property definition. | `204` |  |

### PropertyGroupsController — `api/v{v}/property-groups`

Datei: `PropertyGroupsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Property group operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetPropertyGroups | Returns a list of property groups. | `IList<PropertyGroupDto>` |  |
| GET | `/{propertyGroupIdentifier}` | GetPropertyGroup | Returns a specific property group. | `PropertyGroupDto` |  |
| GET | `/{propertyGroupIdentifier}/property-definitions` | GetPropertyDefinitions | Returns a list of property definitions for the specified property group identifier. | `IList<PropertyDefinitionDto>` |  |
| POST | `/` | CreatePropertyGroup | Creates a property group. | `IdentifierRowVersionDto` |  |
| PATCH | `/{propertyGroupIdentifier}` | UpdatePropertyGroup | Updates a property group. | `RowVersionDto` |  |
| DELETE | `/{propertyGroupIdentifier}` | DeletePropertyGroup | Deletes the specified property group. | `204` |  |

### PropertySetsController — `api/v{v}/propertysets`

Datei: `PropertySetsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Property set operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{propertySetIdentifier}` | GetPropertySet | Returns a single property set for the provided identifier | `PropertySetDto` |  |
| GET | `/` | GetPropertySets | Returns a list of property sets | `IList<PropertySetDto>` |  |
| POST | `/` | CreatePropertySet | Create a new property set | `IdentifierRowVersionDto` |  |
| PUT | `/{propertySetIdentifier}` | UpdatePropertySet | Updates an existing property set | `RowVersionDto` |  |
| DELETE | `/{propertySetIdentifier}` | DeletePropertySet | Deletes the specified property set | `204` |  |

### PublicController — `api/v{v}/public`

Datei: `PublicController.cs` · Basis: `FileTransferBaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: nein (anonym) · Swagger-Gruppe: Public operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/shares` | GetShareDerivations | Get the derivations from the share recipient token | `PublicShareDto` |  |
| POST | `/stream-formdata` | DownloadCompressedAssetDerivationsForm | Generates a Zip file of specified derivations for the specified assets. This endpoint can be used to facilitate the download of a Zip file b | `Stream/204` |  |
| POST | `/stream` | DownloadCompressedAssetDerivations | Generates a Zip file of specified derivations for the specified assets. | `Stream/204` |  |

### RecycleBinController — `api/v{v}/recycle-bin`

Datei: `RecycleBinController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Recycle bin operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetRecycleBinTree | Returns the recycle-bin tree. | `CategoryDto` |  |
| DELETE | `/` | EmptyRecycleBin | Delete all items in the recycle-bin. | `204` |  |

### RelationTypesController — `api/v{v}/relation-types`

Datei: `RelationTypesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Relation type operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{relationTypeIdentifier}` | GetRelationType | Returns a single relation type for the provided relation type identifier. | `RelationTypeDto` |  |
| GET | `/` | GetRelationTypes | Returns a list of relation types. | `IList<RelationTypeDto>` |  |
| POST | `/` | CreateRelationType | Creates a relation type. | `IdentifierRowVersionDto` |  |
| PUT | `/{relationTypeIdentifier}` | UpdateRelationType | Updates a relation type by relation type identifier. | `RowVersionDto` |  |
| DELETE | `/{relationTypeIdentifier}` | DeleteRelationType | Deletes a relation type by relation type identifier. | `204` |  |
| PUT | `/{relationTypeIdentifier}/allowed-entity-relations` | SetAllowedEntityRelations | Sets edge type to node type relations. All existing relations from that edge type are deleted and replaced with the new ones. | `204` |  |

### RelationsController — `api/v{v}/relations`

Datei: `RelationsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Relation operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/bulk-delete` | BulkDeleteRelations | Deletes relations in bulk. | `204` |  |

### RolesController — `api/v{v}/roles`

Datei: `RolesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Role operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetRoles | Returns a list of role summaries | `IList<RoleSummaryDto>` |  |
| GET | `/{roleIdentifier}` | GetRole | Returns a single role for the provided role identifier | `RoleSummaryDto` |  |
| POST | `/` | CreateRole | Creates a role | `IdentifierRowVersionDto` |  |
| PUT | `/{roleIdentifier}` | UpdateRole | Updates a role by identifier | `RowVersionDto` |  |
| DELETE | `/{roleIdentifier}` | DeleteRole | Deletes a role by identifier | `204` |  |
| PUT | `/{roleIdentifier}/object-type-permissions` | SetObjectTypePermissions | Sets object type permission of a role | `204` |  |
| PUT | `/{roleIdentifier}/property-definition-restrictions` | SetPropertyDefinitionRestrictions | Sets property restrictions of a role | `204` |  |
| PUT | `/{roleIdentifier}/derivation-type-restrictions` | SetDerivationTypeRestrictions | Sets derivation type restrictions of a role | `204` |  |
| PUT | `/{roleIdentifier}/property-value-restrictions` | SetPropertyValueRestrictions | Sets property value restrictions of a role | `204` |  |

### RulesController — `api/v{v}/rules`

Datei: `RulesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Rule operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/` | CreateRule | Create a new rule from a CreateRuleDto. | `IdentifierRowVersionDto` |  |
| GET | `/` | GetRules | Get a list of all RuleDto. | `IList<RuleDto>` |  |
| GET | `/{ruleIdentifier}` | GetRule | Get a single RuleDto by ruleIdentifier. | `RuleDto` |  |
| PUT | `/{ruleIdentifier}` | UpdateRule | Updates a specific rule by ruleIdentifier and an UpdateRuleDto. | `void` |  |
| DELETE | `/{ruleIdentifier}` | DeleteRule | Deletes a rule by ruleIdentifier. | `204` |  |
| POST | `/execute` | ExecuteRules | Executes rules on entities according to the filter specification in executeRulesDto. | `IList<IdentifierRowVersionDto>` |  |
| GET | `/report` | GetRuleExecutionReports | Get a paged list of rule execution reports, latest on top. | `IList<RuleExecutionReportDto>` |  |
| GET | `/{ruleJobIdentifier}/report` | GetRuleExecutionReport | Get a specific rule execution report by ruleJobIdentifier. | `RuleExecutionReportDto` |  |
| GET | `/{ruleJobIdentifier}/report/details` | GetRuleExecutionReportDetails | Get a page of a rule execution detail report. | `IList<RuleExecutionDetailReportDto>` |  |

### SavedSearchesController — `api/v{v}/saved-searches`

Datei: `SavedSearchesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Saved search operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetSavedSearches | Returns a list of saved searches from the actual user. | `IList<SavedSearchDto>` |  |
| POST | `/` | CreateSavedSearch | Create a new saved search. | `IdentifierRowVersionDto` |  |
| GET | `/users` | GetSavedSearchUsers | Returns a list of users eligible for saved search related actions. | `IList<UserInfoDto>` |  |
| GET | `/user-groups` | GetSavedSearchUserGroups | Returns a list of user groups eligible for saved search related actions. | `IList<UserGroupDto>` |  |
| GET | `/{savedSearchIdentifier}` | GetSavedSearch | Get a saved search by identifier. | `SavedSearchDto` |  |
| PUT | `/{savedSearchIdentifier}` | UpdateSavedSearch | Updates an existing saved search. | `RowVersionDto` |  |
| DELETE | `/{savedSearchIdentifier}` | DeleteSavedSearch | Deletes the specified saved search. | `Stream/204` |  |
| GET | `/{savedSearchIdentifier}/resource-permissions` | GetResourcePermissions | Returns the resource permissions on a single saved search. | `IList<ResourcePermissionDto>` |  |
| PUT | `/{savedSearchIdentifier}/resource-permissions` | UpdateSavedSearchResourcePermissions | Assigns resource permissions to this saved search | `IList<IdentifierRowVersionDto>` |  |
| POST | `/{savedSearchIdentifier}/resource-permissions` | CreateResourcePermissions | (obsolet) This operation is obsolete and will be removed in a future A… | `IList<IdentifierRowVersionDto>` | obsolet |
| DELETE | `/{savedSearchIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | DeleteResourcePermission | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |

### SearchController — `api/v{v}/search`

Datei: `SearchController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Search operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| POST | `/reindex` | ReIndex | Trigger a complete re-index of the desired entity types. It's an asynchronous process. | `IdentifierRowVersionDto` |  |
| GET | `/reindex` | GetReIndexingJobs | Get a list of the latest re-indexing jobs | `IList<ReIndexingJobDto>` |  |
| GET | `/reindex/{reIndexIdentifier}` | GetReIndexReport | Get a specific re-indexing job report | `ReIndexingJobReportDto` |  |
| DELETE | `/reindex/{reIndexIdentifier}` | CancelReIndexJob | Cancel a re-indexing job | `void` |  |

### SharesController — `api/v{v}/shares`

Datei: `SharesController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Share operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| PUT | `/{shareIdentifier}` | UpdateShare | Update a share | `RowVersionDto` |  |
| POST | `/{shareIdentifier}/recipients` | AddShareRecipient | Add a recipient to a share | `204` |  |
| DELETE | `/{shareIdentifier}/recipients/{token}` | RemoveShareRecipient | Remove a recipient from a share | `204` |  |
| DELETE | `/{shareIdentifier}` | DeleteShare | Delete a share | `204` |  |

### TasksController — `api/v{v}/tasks`

Datei: `TasksController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Task operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{taskIdentifier}` | GetTaskByIdentifier | Get a specific task by identifier | `TaskDto` |  |
| POST | `/search` | SearchTasks | Returns a list of tasks for the defined search-query. | `SearchResultOfTaskV6Dto` | nur V6 |
| POST | `/search` | SearchTasks | Returns a list of tasks for the defined search-query. | `SearchResultOfTaskDto` | obsolet, nur V5,V4,V3,V2,V1 |
| POST | `/quicksearch` | QuickSearchTasks | Returns a list of auto-complete suggestions or quick search tasks ("did you mean" / or direct hits) for the defined search-query. | `QuickSearchResultOfTaskDto` |  |
| PUT | `/{taskIdentifier}` | UpdateTask | Update a specific task by identifier | `RowVersionDto` |  |
| POST | `/{taskIdentifier}/assignees` | AddAssigneeToTask | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| DELETE | `/{taskIdentifier}/assignees/{userIdentifier}` | RemoveAssigneeFromTask | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| GET | `/{taskIdentifier}/activity-logs` | GetTaskActivityLogs | Retrieve the activity log for the provided task identifier. | `IList<ActivityLogDto>` |  |
| POST | `/{taskIdentifier}/move` | MoveTask | Move this task to another state, or change the position of this task in a state | `RowVersionDto` |  |
| PUT | `/{taskIdentifier}/transition` | ExecuteStatusTransition | Execute a status transition on a task | `RowVersionDto` |  |
| PUT | `/{taskIdentifier}/link/assets/{assetIdentifier}/` | LinkAssetToTask | Links an existing asset into an already existing task. Existing links to other tasks will be left as is. | `204` |  |
| PUT | `/{taskIdentifier}/unlink/assets/{assetIdentifier}/` | UnlinkAssetFromTask | Unlinks an existing asset from a task. | `204` |  |
| POST | `/{taskIdentifier}/comments` | CreateComment | Create a new comment for the specified task. | `IdentifierRowVersionDto` |  |
| GET | `/{taskIdentifier}/comments` | GetComments | Returns a list of comments for the provided task identifier. | `IList<CommentDto>` |  |
| GET | `/{taskIdentifier}/assets` | GetAssetsByTask | Returns a list of assets for the defined task identifier. Paging is expressed through the offset and limit parameters. To display page 3 wit | `IList<AssetDto>` |  |
| GET | `/task-types` | GetTaskTypes | Returns a list of task types. | `IList<string>` |  |
| DELETE | `/{taskIdentifier}` | DeleteTask | Deletes an existing task. All attached assignees, comments and assets links will also be deleted. | `204` |  |
| POST | `/{taskIdentifier}/properties` | CreateTaskProperties | Create and add a list of metadata properties to a task. | `IList<IdentifierRowVersionDto>` |  |
| GET | `/{taskIdentifier}/properties` | GetTaskProperties | Return the metadata properties of a task. | `IList<PropertyDto>` |  |
| PATCH | `/{taskIdentifier}/properties/{propertyIdentifier}` | EditTaskProperty | Edit a metadata property of a task. Does not delete existing property values that were not sent as part of the request. | `RowVersionDto` |  |
| PUT | `/{taskIdentifier}/properties/{propertyIdentifier}` | UpdateTaskProperty | Update an existing metadata property of the specified task. Will drop property values not included in this operation. | `RowVersionDto` |  |
| DELETE | `/{taskIdentifier}/properties/{propertyIdentifier}` | DeleteTaskProperty | Delete a metadata property of a task. | `204` |  |
| PUT | `/{taskIdentifier}/properties` | PutTaskProperties | Create or update multiple properties on a task. Values of existing properties will be replaced by the values from the request. In case of tr | `IList<SetPropertyResultDto>` |  |
| PATCH | `/{taskIdentifier}/properties` | PatchTaskProperties | Create or patch multiple properties on a task. Values of existing properties will be patched by the values from the request. In case of tran | `IList<SetPropertyResultDto>` |  |
| POST | `/{taskIdentifier}/bulk-delete-properties` | DeleteTaskProperties | Deletes existing properties of a task.  Returns a result for each property indicating whether the property was successfully deleted. | `IList<DeletePropertyResultDto>` |  |
| POST | `/{taskIdentifier}/watchers` | AddWatcherToTask | Add a list of watchers to a task | `204` |  |
| GET | `/{taskIdentifier}/watchers` | GetTaskWatchers | Get the watchers of a task | `IList<UserInfoDto>` |  |
| POST | `/{taskIdentifier}/watchers/bulk-delete` | RemoveWatchersFromTask | Remove a list of watchers from a task | `204` |  |

### TenantsController — `api/v{v}/tenants`

Datei: `TenantsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Tenant operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{tenantIdentifier}` | GetTenant | Returns a single tenant for the provided identifier | `TenantExtendedDto` |  |
| GET | `/` | GetTenants | Returns a list of tenants. | `IList<TenantExtendedDto>` |  |
| POST | `/` | CreateTenant | Create a new tenant | `IdentifierRowVersionDto` |  |
| PUT | `/{tenantIdentifier}` | UpdateTenant | Updates an existing tenant | `RowVersionDto` |  |
| DELETE | `/{tenantIdentifier}` | DeleteTenant | Deletes the specified tenant | `204` |  |
| PUT | `/{tenantIdentifier}/usergroup-permissions` | SetUserGroupPermissions | Define global user group permissions by assigning a role to a user group. That mapping can be made by tenant. That allows to globally assign | `204` |  |
| GET | `/permissions` | GetTenantsPermissions | Returns a list of tenants for the current user with create, update, delete permissions on users and user groups. | `IList<TenantPermissionsDto>` |  |

### UserContextsController — `api/v{v}/user-context`

Datei: `UserContextsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: User context operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetUserContexts | Retrieve a list of all user context. | `IList<UserContextDto>` |  |
| GET | `/{userContextIdentifier}` | GetUserContext | Retrieves a single user context. | `UserContextDto` |  |
| POST | `/` | CreateUserContext | Create a new user context. If no identifier is specified in CreateUserContextDto, a new one is generated. | `IdentifierRowVersionDto` |  |
| PUT | `/{userContextIdentifier}` | UpdateUserContext | Updates a user context. | `RowVersionDto` |  |
| DELETE | `/{userContextIdentifier}` | DeleteUserContext | Deletes the specified user context. | `204` |  |

### UserGroupsController — `api/v{v}/user-groups`

Datei: `UserGroupsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: User group operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | SearchUserGroups | Returns a list of user groups for the defined search-query | `IList<UserGroupExtendedDto>` |  |
| GET | `/{userGroupIdentifier}` | GetUserGroup | Retrieves a specific user group | `UserGroupExtendedDto` |  |
| POST | `/` | CreateUserGroup | Creates a user group | `IdentifierRowVersionDto` |  |
| PUT | `/{userGroupIdentifier}` | UpdateUserGroup | Updates a user group | `RowVersionDto` |  |
| DELETE | `/{userGroupIdentifier}` | DeleteUserGroup | Deletes a user group | `204` |  |
| POST | `/{userGroupIdentifier}/users` | AddUserToUserGroup | Adds a user to a user group | `204` |  |
| DELETE | `/{userGroupIdentifier}/users/{userIdentifier}` | RemoveUserFromUserGroup | Removes a user from a user group | `204` |  |

### UsersController — `api/v{v}/users`

Datei: `UsersController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: User operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | SearchUsers | Returns a list of users for the defined search-query | `IList<UserAccountInfoDto>` |  |
| GET | `/{userIdentifier}` | GetUser | Retrieves a specific user | `UserDto` |  |
| POST | `/` | CreateUser | Creates a user | `IdentifierRowVersionDto` |  |
| PUT | `/{userIdentifier}` | UpdateUser | Updates a user | `RowVersionDto` |  |
| DELETE | `/{userIdentifier}` | DeleteUser | Deletes a user | `204` |  |
| PUT | `/{userIdentifier}/password` | SetPassword | Sets a user's password | `204` |  |
| PUT | `/{userIdentifier}/unlock` | UnlockUser | Unlock a locked user account | `RowVersionDto` |  |
| GET | `/{userIdentifier}/user-groups` | GetUserGroupsForUser | Retrieves the user groups of specific user | `IList<UserGroupDto>` |  |
| GET | `/registrations` | SearchUserRegistrations | Retrieves the registered users. | `IList<UserRegistrationDto>` |  |
| GET | `/registrations/{userIdentifier}` | GetUserRegistration | Retrieves a specific user registration | `UserRegistrationDto` |  |
| POST | `/{userIdentifier}/registrations` | FinalizeUserRegistration | Finalize the registration process for the given user. | `RowVersionDto` |  |
| POST | `/{userIdentifier}/avatar/upload` | UploadAvatar | Uploads a avatar file for the specified user. | `CreateFileResponseDto` |  |
| DELETE | `/{userIdentifier}/avatar` | DeleteAvatar | Deletes the avatar file for the specified user. | `204` |  |

### WatermarkDefinitionsController — `api/v{v}/watermark-definitions`

Datei: `WatermarkDefinitionsController.cs` · Basis: `BaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Watermark definition operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/` | GetWatermarks | Retrieve a list of all WatermarkDefinition. | `IList<WatermarkDefinitionDto>` |  |
| GET | `/{watermarkDefinitionIdentifier}` | GetWatermarkDefinition | Retrieves a single WatermarkDefinition. | `WatermarkDefinitionDto` |  |
| POST | `/` | CreateWatermarkDefinition | Create a new WatermarkDefinition. If no identifier is specified in CreateWatermarkDefinitionDto, a new one is ge | `IdentifierRowVersionDto` |  |
| PUT | `/{watermarkDefinitionIdentifier}` | UpdateWatermarkDefinition | Updates a WatermarkDefinition. | `RowVersionDto` |  |
| DELETE | `/{watermarkDefinitionIdentifier}` | DeleteWatermarkDefinition | Deletes the specified WatermarkDefinition. | `204` |  |
| GET | `/{watermarkDefinitionIdentifier}/triggers/{watermarkTriggerIdentifier}` | GetWatermarkTrigger | Retrieves a single WatermarkTrigger. | `WatermarkTriggerDto` |  |
| POST | `/{watermarkDefinitionIdentifier}/triggers` | CreateWatermarkTrigger | Create a new WatermarkTrigger. If no identifier is specified in CreateWatermarkTriggerDto, a new one is generate | `IdentifierRowVersionDto` |  |
| PUT | `/{watermarkDefinitionIdentifier}/triggers/{watermarkTriggerIdentifier}` | UpdateWatermarkTrigger | Updates a WatermarkTrigger. | `RowVersionDto` |  |
| DELETE | `/{watermarkDefinitionIdentifier}/triggers/{watermarkTriggerIdentifier}` | DeleteWatermarkTrigger | Deletes the specified WatermarkTrigger. | `204` |  |

### WorkspacesController — `api/v{v}/workspaces`

Datei: `WorkspacesController.cs` · Basis: `FileTransferBaseController` · API-Versionen: V6,V5,V4,V3,V2,V1 · Authorize: ja · Swagger-Gruppe: Workspace operations

| Verb | Route (relativ) | Methode | Zweck | Rückgabe | Hinweise |
|---|---|---|---|---|---|
| GET | `/{workspaceIdentifier}` | GetWorkspace | Retrieve a specific workspace. | `WorkspaceDto` |  |
| POST | `/search` | SearchWorkspaces | Returns a list of workspaces for the defined search-query. | `SearchResultOfWorkspaceV6Dto` | nur V6 |
| POST | `/search` | SearchWorkspaces | Returns a list of workspaces for the defined search-query. | `SearchResultOfWorkspaceDto` | obsolet, nur V5,V4,V3,V2,V1 |
| POST | `/quicksearch` | QuickSearchWorkspaces | Returns a list of auto-complete suggestions or quick search workspaces ("did you mean" / or direct hits) for the defined search-query. | `QuickSearchResultOfWorkspaceDto` |  |
| POST | `/` | CreateWorkspace | Create a new workspace. | `PropertyEntityCreationResultDto` |  |
| PUT | `/{workspaceIdentifier}` | UpdateWorkspace | Updates an existing workspace. Only own workspaces can be updated, otherwise a 404 (Not Found) is returned. | `RowVersionDto` |  |
| DELETE | `/{workspaceIdentifier}` | DeleteWorkspace | Deletes an existing workspace. Only own workspaces can be deleted, otherwise a 404 (Not Found) is returned. | `204` |  |
| GET | `/{workspaceIdentifier}/activity-logs` | GetWorkspaceActivityLogs | Retrieve the activity log for the provided workspace identifier. | `IList<ActivityLogDto>` |  |
| POST | `/{workspaceIdentifier}/avatar/upload` | UploadWorkspaceAvatar | Uploads the avatar file for this workspace. | `CreateFileResponseDto` |  |
| DELETE | `/{workspaceIdentifier}/avatar` | DeleteWorkspaceAvatar | Deletes the avatar of this workspace. | `204` |  |
| POST | `/{workspaceIdentifier}/comments` | CreateComment | Create a new comment for the specified workspace. | `IdentifierRowVersionDto` |  |
| GET | `/{workspaceIdentifier}/comments` | GetComments | Returns a list of comments for the provided workspace identifier. | `IList<CommentDto>` |  |
| POST | `/{workspaceIdentifier}/tasks` | CreateTask | Create a new task for the specified workspace. | `PropertyEntityCreationResultDto` |  |
| GET | `/{workspaceIdentifier}/tasks` | GetTasksByWorkspace | (obsolet) This operation is obsolete and will be removed in a future A… | `IList<TaskDto>` | obsolet |
| POST | `/{workspaceIdentifier}/assets/upload/chunks/start` | StartNewUpload | (obsolet) This operation is obsolete and will be removed in a future A… | `StartUploadResponseDto` | obsolet |
| POST | `/upload/chunks/sessions/{uploadSessionId}/sequencenumber/{chunkUploadSequenceNumber}` | AppendChunk | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| DELETE | `/upload/chunks/sessions/{uploadSessionId}` | CancelUpload | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| POST | `/{workspaceIdentifier}/assets/upload/chunks/sessions/{uploadSessionId}/finish` | FinishUpload | (obsolet) This operation is obsolete and will be removed in a future A… | `CreateAssetResponseDto` | obsolet |
| GET | `/{workspaceIdentifier}/resource-permissions` | GetResourcePermissions | Returns the resource permissions on a single workspace | `IList<ResourcePermissionDto>` |  |
| PUT | `/{workspaceIdentifier}/resource-permissions` | UpdateWorkspaceResourcePermissions | Assigns resource permissions to this workspace | `IList<IdentifierRowVersionDto>` |  |
| POST | `/{workspaceIdentifier}/resource-permissions` | CreateResourcePermissions | (obsolet) This operation is obsolete and will be removed in a future A… | `IList<IdentifierRowVersionDto>` | obsolet |
| PUT | `/{workspaceIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | UpdateResourcePermission | (obsolet) This operation is obsolete and will be removed in a future A… | `RowVersionDto` | obsolet |
| DELETE | `/{workspaceIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | DeleteResourcePermission | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| GET | `/{workspaceIdentifier}/templated-resource-permissions` | GetTemplateResourcePermissions | Returns the resource permissions on a single workspace template. These permissions are applied when a workspace is created from a template. | `IList<ResourcePermissionDto>` |  |
| PUT | `/{workspaceIdentifier}/templated-resource-permissions` | UpdateWorkspaceTemplatedResourcePermissions | Assigns resource permissions to this workspace template. These permissions are applied when a workspace is created from this template. | `IList<IdentifierRowVersionDto>` |  |
| POST | `/{workspaceIdentifier}/templated-resource-permissions` | CreateTemplateResourcePermissions | (obsolet) This operation is obsolete and will be removed in a future A… | `IList<IdentifierRowVersionDto>` | obsolet |
| DELETE | `/{workspaceIdentifier}/templated-resource-permissions/{resourcePermissionIdentifier}` | DeleteTemplateResourcePermission | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| POST | `/{workspaceIdentifier}/templated-resource-permissions/bulk-delete` | DeleteTemplateResourcePermissions | (obsolet) This operation is obsolete and will be removed in a future A… | `204` | obsolet |
| GET | `/{workspaceIdentifier}/properties` | GetWorkspaceProperties | Returns a workspace's metadata properties. Does not support user context. | `IList<PropertyDto>` |  |
| GET | `/{workspaceIdentifier}/properties/{propertyIdentifier}` | GetWorkspaceProperty | Returns a specific metadata property of a workspace | `PropertyDto` |  |
| POST | `/{workspaceIdentifier}/properties` | CreateProperty | Create and add a metadata Property to a Workspace. | `IdentifierRowVersionDto` |  |
| PUT | `/{workspaceIdentifier}/properties/{propertyIdentifier}` | UpdateProperty | Update an existing metadata Property of the specified workspace Will drop property values not included in this operation | `RowVersionDto` |  |
| PATCH | `/{workspaceIdentifier}/properties/{propertyIdentifier}` | EditWorkspaceProperty | Edit a metadata property of a workspace. Does not delete existing property values that were not sent as part of the request. | `RowVersionDto` |  |
| DELETE | `/{workspaceIdentifier}/properties/{propertyIdentifier}` | DeleteWorkspaceProperty | Delete a metadata property of a workspace. | `204` |  |
| PUT | `/{workspaceIdentifier}/properties` | PutWorkspaceProperties | Create or update multiple properties on a workspace. Values of existing properties will be replaced by the values from the request. In case  | `IList<SetPropertyResultDto>` |  |
| PATCH | `/{workspaceIdentifier}/properties` | PatchWorkspaceProperties | Create or patch multiple properties on a workspace. Values of existing properties will be patched by the values from the request. In case of | `IList<SetPropertyResultDto>` |  |
| POST | `/{workspaceIdentifier}/bulk-delete-properties` | DeleteWorkspaceProperties | Deletes existing properties of a workspace.  Returns a result for each property indicating whether the property was successfully deleted. | `IList<DeletePropertyResultDto>` |  |
| GET | `/users` | GetUsers | Returns a list of users eligible for workspace related actions. | `IList<UserInfoDto>` |  |
| GET | `/user-groups` | GetSavedSearchUserGroups | Returns a list of user groups eligible for workspace related actions. | `IList<UserGroupDto>` |  |
| GET | `/{workspaceIdentifier}/settings/my/notifications` | GetWorkspaceUserNotificationSettings | Returns the notification settings for the provided identifier and current user. | `UserNotificationSettingsDto` |  |
| PUT | `/{workspaceIdentifier}/settings/my/notifications` | CreateOrUpdateUserNotificationSettings | Create or update the notification settings. | `RowVersionDto` |  |
| GET | `/{workspaceIdentifier}/tasks/users` | GetWorkspaceTaskUsers | (obsolet) This operation is obsolete and will be removed in a future A… | `IList<UserInfoDto>` | obsolet, nur V4,V3,V2,V1 |
| GET | `/{workspaceIdentifier}/users` | GetWorkspaceUsers | Returns a list of users eligible for certain actions in this workspace. | `IList<UserInfoDto>` |  |
| GET | `/{workspaceIdentifier}/tasks/{taskIdentifier}/users` | GetTaskUsers | Returns a list of users eligible for certain actions on a specific task in this workspace. | `IList<UserInfoDto>` |  |
| POST | `/{workspaceIdentifier}/shares` | CreateExternalWorkspaceShare | Creates a public share for the selected workspace | `IdentifierRowVersionDto` |  |
| GET | `/{workspaceIdentifier}/shares` | GetWorkspaceShares | Get the shares of a workspace | `IList<ShareDto>` |  |
| POST | `/{workspaceIdentifier}/move` | MoveWorkspace | Move this workspace to another state, or change the position of this workspace in a state | `RowVersionDto` |  |
| GET | `/{workspaceIdentifier}/states` | GetStates | Get all states of this workspace | `IList<StateDto>` |  |
| POST | `/{workspaceIdentifier}/states` | CreateState | Create a new state in this workspace | `IdentifierRowVersionDto` |  |
| PUT | `/{workspaceIdentifier}/states/transitions` | UpdateTransitions | Update the transitions of all the states in this workspace | `204` |  |
| GET | `/{workspaceIdentifier}/states/users` | GetStatesUsers | Returns a list of users eligible for states related actions in this workspace | `IList<UserInfoDto>` |  |
| GET | `/{workspaceIdentifier}/states/user-groups` | GetStatesUserGroups | Returns a list of user groups eligible for states related actions in this workspace | `IList<UserGroupDto>` |  |
| PATCH | `/{workspaceIdentifier}/states/{stateIdentifier}` | UpdateState | Update a state in this workspace | `RowVersionDto` |  |
| DELETE | `/{workspaceIdentifier}/states/{stateIdentifier}` | DeleteState | Delete a state in this workspace | `204` |  |
| PUT | `/{workspaceIdentifier}/states/{stateIdentifier}/move` | MoveState | Change the position of a state in this workspace | `RowVersionDto` |  |
| GET | `/{workspaceIdentifier}/states/{stateIdentifier}/resource-permissions` | GetStateResourcePermissions | Returns the resource permissions on this state | `IList<ResourcePermissionDto>` |  |
| PUT | `/{workspaceIdentifier}/states/{stateIdentifier}/resource-permissions` | UpdateStateResourcePermissions | Assigns resource permissions to this state | `IList<IdentifierRowVersionDto>` |  |
| PUT | `/{workspaceIdentifier}/states/{stateIdentifier}/transitions` | UpdateStateTransitions | Update the transitions of a state in this workspace | `RowVersionDto` |  |
| POST | `/{workspaceIdentifier}/assets/finalize` | FinalizeAssets | Finalize workspace assets. Assets with successful validation are moved to the specified category, other assets are not moved. (Optional, ass | `AssetSetFinalizationResultDto` |  |
| POST | `/{workspaceIdentifier}/assets/validate` | ValidateAssets | Validates all assets from a workspace. | `IList<ValidateAssetSetPropertiesResultDto>` |  |
| POST | `/{workspaceIdentifier}/copy-properties` | CopyPropertiesToAssets | Copy workspace properties to assets. If asset set identifiers are set, only those are affected, otherwise all. | `IdentifierRowVersionDto` |  |
| POST | `/{workspaceIdentifier}/copy-all-properties` | CopyAllPropertiesToAssets | Copy all workspace properties to assets. If asset set identifiers are set, only those are affected, otherwise all. | `IdentifierRowVersionDto` |  |
| POST | `/{workspaceIdentifier}/watchers` | AddWatchersToWorkspace | Add a list of watchers to a workspace | `204` |  |
| GET | `/{workspaceIdentifier}/watchers` | GetWorkspaceWatchers | Get the watchers of a workspace | `IList<UserInfoDto>` |  |
| POST | `/{workspaceIdentifier}/watchers/bulk-delete` | RemoveWatchersFromWorkspace | Remove a list of watchers from a workspace | `204` |  |
---

## 6. Berechtigungsmodell

### 6.1 Bausteine

| Baustein | Beschreibung |
|---|---|
| **Permission** (Enum, 311 Werte) | Atomare Rechte, Schema `<ObjectType><Create/Read/Update/Delete/View>` (z. B. `AssetSetRead`, `CategoryPropertyUpdate`, `WorkspaceTemplateCreate`) plus Spezialrechte: `GlobalAdmin` (Superuser, umgeht alle Prüfungen), `AdminView`, `DashboardView`, `AssetBrowserView`, `AssetDetailView`, `UploadView`, `DownloadBasketView`, `CollectionView`, `CustomEntityBrowserView` (Frontend-Sichtbarkeiten), `AssetDownloadRead`, `CategoryDownloadRead`, `WorkspaceFinalize`, `CropDerivationCreate`, `RuleReApplyJobCreate`, `RuleJobReportRead`, `ArchiveExtractionJob`, `PropertyExtractionJob`, `PermissionMaterializationJob*`, `*ActivityLogRead/View`, `*PropertySetGlobal*` (globale vs. eigene PropertySets), `CategoryPropertyInheritance*`. `View` = Sichtbarkeit in UI/Listen, `Read` = Daten lesen. |
| **AuthorizableAction** | Read, Create, Update, Delete, View, Any – für Restriktionen (Blacklists). |
| **ObjectType** (Enum, ~90 Werte) | Objektklassen für Rollen-Zuweisbarkeit (`RoleObjectType`), Translations, ActivityLogs, EntityPermissions. Enthält auch Frontend-Objekte (Dashboard, AssetBrowser, Admin …) und Auth-Objekte (Role=1000, UserGroupToRoleMap, ObjectTypePermission, NodePermission, User, UserGroup). |
| **Role** | Bündel von Permissions (`RoleObjectTypePermission`), zuweisbar auf ObjectTypes (`RoleObjectType`), mit Blacklists (`PropertyDefinitionRestriction`, `DerivationTypeRestriction`, `PropertyValueRestriction`) und Limits (`PropertyEditEntityLimit` = max. Assets pro Mass-Edit, `PropertyExportEntityLimit`). System-Rollen nicht löschbar. |
| **Globale Zuweisung** (`UserGroupToRoleMap`) | UserGroup → Rolle **pro Tenant** (`PUT tenants/{id}/usergroup-permissions`). Ergibt die "General Permissions" des Users (`IUnitOfWork.Permissions`), ermittelt per SQL `User → UserGroupUser → UserGroup → UserGroupToRoleMap → Role → RoleObjectTypePermission`. Prüfung: `IAuthorizationService.CanPerformActionAsync(Permission)`. |
| **Ressourcen-Zuweisung** (`NodePermission`, API: "resource-permissions") | User oder UserGroup → Rolle an einem konkreten Node (Category, Workspace, Task, State, CustomEntity; für Lizenzobjekte "global" über einen Wurzelknoten). `Status`, `IsInherited` (berechnet). Erzeugen/Ändern erzeugt `ActivityLogChangeType.ResourcePermission*` und Notifications (Workspace-Collaborator hinzugefügt). |
| **EntityPermission** | Wie NodePermission, aber für Nicht-Node-Objekte (SavedSearch, PropertySet, Jobs, FileNamingDefinition, Workspace-Template-Vorlagenrechte). Löschung eines Users/Gruppe/Rolle räumt sie mit (`DeleteEntityPermissionsOfUserGroupAsync`, `…ByRoleAsync`). |

### 6.2 Vererbung in der Node-Hierarchie und Materialisierung

- NodePermissions vererben sich entlang `ParentChild`-Kanten an alle Nachkommen (Kategorien → Unterkategorien → AssetSets → Versions; Workspace → Tasks/Comments/States/AssetSets).
- `Node.InterruptInheritance` (API: `resource-permission-settings`) stoppt die Vererbung an diesem Knoten ("Permission Interruption"); `abortOnInterrupt` in der rekursiven CTE (`NodeSqlLogic.GetNodeWithChildrenNoPermissionsAsync`).
- Die effektiven Rechte werden **materialisiert**: `Admin.MaterializedNodePermission(NodeId, NodePermissionId)` enthält für jeden Node alle geltenden NodePermissions (eigene + geerbte). Pflege:
  - `ApplyEntityCreatedAsync`: neuer Node kopiert die materialisierten Permissions des `ParentChild`-Elternknotens (`CopyPermissionsFromNodeAsync`).
  - `ApplyEntitiesDeletedAsync`: löscht materialisierte Einträge.
  - `ApplyNodePermissionsCreatedAsync`: neue NodePermission wird auf den Teilbaum (bis Interrupt) verteilt.
  - `ApplyNodePermissionsUpdatedAsync` / `ReMaterializeSubTreeAsync`: Voll-Neuaufbau eines Teilbaums (nach Move, Interrupt-Änderung, Löschung, manuell via `POST permission-materializations`).
  - Ausführung entweder synchron (`synchronousPermissionMaterialization`) oder als Hangfire-Job `PermissionMaterialization` (`Admin.PermissionMaterialization` + `…Operation`, `BackgroundJobTriggerType`); es darf nur ein Job gleichzeitig laufen (sonst `ConcurrencyConflict`). Aufräumen alter Jobs per Cleanup-Job (Retention 14 Tage).
- Auflösung zur Laufzeit (`MaterializedPermissionRepository.GetApplicablePermissionsAsync`): `MaterializedNodePermission ⋈ NodePermission ⋈ Role ⋈ RoleObjectTypePermission`, gefiltert auf den User (`NodePermission.UserId`) oder seine Gruppen (`UserGroupId`, Gruppen aus `IUserGroupCache`, cachebar). Für **AssetSets** wird zusätzlich über die `ParentChild`/`Link`-Kante zur Kategorie gejoint und `PropertyValueRestriction` (Rolle × konkreter Property-Wert) als Ausschluss angewendet (`GetAssetPermissionsAsync`); für **Versions** über die `Version`-Kante (`GetVersionPermissionsAsync`).
- `GlobalAdmin` (globale Permission) gewährt alles ohne Node-Prüfung.
- Ergebnis pro Entity: `ApplicablePermissions` (Set) wird an Business-Objects (`AuthorizableBusinessObjectBase`) gehängt und in DTOs ausgeliefert, damit das Frontend Aktionen ein-/ausblenden kann.
- Die Suche (Elasticsearch) erhält bei Permission-Änderungen `PermissionUpdateDto`/Path-Principals (`SearchablePath.PrincipalIdentifiers`), damit Suchergebnisse serverseitig nach User/Gruppen gefiltert werden (`BaseSearchQuery.UserAndGroupIdentifiers`).

### 6.3 Restriktionen (Blacklists pro Rolle)

| Restriktion | Wirkung |
|---|---|
| `PropertyDefinitionRestriction` (Role, PropertyDefinition, Action) | Rolle darf bestimmte Properties nicht lesen/schreiben; `IPropertyPermissionService.FilterRestrictedPropertiesAsync` entfernt sie aus Antworten; `SetPropertyResultReason.CreateOrUpdateRestricted`. Wird pro Node ausgeliefert (`PropertyDefinitionRestrictions` an Asset/Category/Workspace). |
| `DerivationTypeRestriction` (Role, DerivationType, AssetType, Action) | Rolle darf Derivat-Typ nicht sehen/laden/erzeugen; `FilterRestrictedDerivationsAsync`, `CanReadDerivationOnAssetVersionAsync`. |
| `PropertyValueRestriction` (Role, Property(-Wert), Action) | Rolle wird an Assets **ausgeschlossen**, deren Property den Wert hat (z. B. "vertraulich"); direkt im Permission-SQL berücksichtigt und in `GetFilteredAssetPermissionsAsync`. |
| `PropertyEditEntityLimit` / `PropertyExportEntityLimit` | Mengenbegrenzung für Mass-Edit/Export je Rolle (`GetEntityLimitsOfCurrentUserAsync`). |

### 6.4 Prüfpunkte in der Verarbeitung

- Jede Activity lädt `LoadGeneralPermissionsAsync` in den UoW; Services fragen `IAuthorizationService` (global) bzw. `CanPerformOnNodesAsync`/`CanPerformByNodesAsync` (Node).
- Spezialisierte Services: `IAssetPermissionService` (Create in Parent, Read/Update/Delete/Move AssetSets, Derivations), `IAssetPropertyPermissionService`, `IPropertyPermissionService` (inkl. `CanUpdatePropertyInheritanceAsync`), `IRelationEnrichmentService.CanCreateOrDeleteRelationsAsync`, `IUserSearchService.SearchUsersWithPermissionAsync` (User/Gruppen, die ein Recht auf einem Workspace/Task haben – für Zuweisungs-Dropdowns).
- Uploads: `CheckUploadPermissionsAsync` erneut beim Finish (Rechte können sich zwischen Start und Finish geändert haben); signierte Upload-URLs mit `UploadSigningKey`.
- `TenantPermissions` (`GET tenants/permissions`): welche Tenants der User mit User/UserGroup-CUD-Rechten verwalten darf.

### 6.5 Tenant-Konzept

- `Admin.Tenant` (`default`-Tenant per Migration, `IsDefaultTenant`), Status, `AcceptsRegistrations` (Selbstregistrierung erlaubt).
- Jeder `Node` hat `TenantId`; Kategorien sind mandantenbezogen (unklar, ob Tenant-Wechsel innerhalb des Baums erlaubt ist; AssetVersions werden laut Code-Kommentar immer mit `default`-Tenant angelegt).
- Rollenzuweisungen (`UserGroupToRoleMap`) und FileNamingDefinitions (`FileNamingDefinitionTenant.IsDefault`) sind tenant-spezifisch; User-/Gruppen-Listen filterbar nach `tenantIdentifiers`; Lizenzobjekte gehören zur Gruppe `TenantPermissionTypeIdentifiers` (BillingContact, RightsOwner, License, LicensePurchase) mit mandantenweiter statt node-basierter Berechtigung.
- `Sharing.AllowSharingAcrossTenants` (Konfiguration) steuert mandantenübergreifende Freigaben (SavedSearch/Workspace).
- Multi-Tenancy ist **innerhalb einer Datenbank** realisiert (kein DB-per-Tenant).

---

## 7. Versionierung und Audit

### 7.1 AssetVersion

- Jede Datei-Änderung erzeugt eine neue `AssetVersion` (Nummer `Version` fortlaufend) unter dem `AssetSet` (Edge `Version`); die vorherige Version bleibt inkl. Derivaten erhalten. `GET assets/{id}/versions` (paged), Zugriff auf jede Version über `assetVersionIdentifier`-Parameter an den meisten Asset-Endpoints; ohne Parameter gilt die neueste veröffentlichte Version.
- Status-Lebenszyklus: `AssetVersionStatus` WorkInProgress → Unpublished → Published (konfigurierbar `DesiredVersionStatusBefore/AfterUploadComplete`), `ProcessingStatus` Pending → Started → Finished/Failed (Rendering), `AssetSetStatus` Importing → Imported | Duplicate.
- Beim Anlegen einer Version: Properties der Vorgängerversion kopieren (außer `IgnoreCopying`), Systemproperties setzen, Original-Derivat anlegen, Duplikat-Erkennung (Hash) → Status Duplicate + Edge `Duplicate` (+ Ablage im Recycle Bin bei Neu-Upload eines Duplikats, `AssetService` Zeile ~462), Default-Derivate einplanen, Rules (`IsNewVersion`) ausführen, Events (`AssetEvent`, `AssetChangeDto`).
- Versions-Permissions: eigene `Version*`-Permissions und `VersionPropertySet*`.
- Löschen: Soft-Delete (`IsDeleted`) → Recycle Bin (`recycleBin=true`: `ParentChild`-Relation wird auf die Kategorie `recyclebin` umgehängt) oder direkt; `ParentChildRelationDeleteBehavior` (DeleteAssetSet | PromoteLinkToParentChild) steuert, was beim Löschen der letzten ParentChild-Relation passiert; `AssetCleanup`-Job löscht nach Retention (Binär 14 Tage, DB 90 Tage) endgültig inkl. Storage-Dateien.
- Kein Versionieren von Metadaten-Änderungen (nur Datei-Versionen); Metadaten-Historie nur über ActivityLog/EventStream.

### 7.2 ActivityLog (Audit + Benachrichtigungen)

- Erzeugung: Services bauen `ActivityLog`-Objekte (Builder-API `ActivityLogBuilder`, `AffectedDataBuilder`) und legen sie im UoW ab; `ActivityLogWriter.PersistCurrentActivityLogsAsync` schreibt sie beim Commit, wenn der `ObjectType` in `ActivityLog.EnabledObjectTypes` konfiguriert ist (Default: ApplicationSettings, ArchiveExtractionJob, AssetDerivationJob, AssetPropertyMassEdit, PropertyImport, PropertyExtractionJob, Task, Workspace, WorkspaceTemplate) und – wenn `RequireTriggeredByUser` – ein User (kein System) auslöst.
- Typen (`ActivityLogType`): AssetCreated/Updated/Deleted, CategoryCreated/Updated/Deleted, TaskCreated/Updated/Deleted, WorkspaceCreated/Updated/Deleted, JobUpdated, ApplicationSettingsUpdated.
- Detail-Änderungen (`ActivityLogChangeType`): AssigneeSet/Updated/Cleared, CommentCreated/Updated/Deleted, PropertyValueCreated/Updated/Deleted, RelationCreated/Deleted, ResourcePermissionAdded/Updated/Removed, StateUpdated, ShareCreated/Updated/Deleted, ConfigurationValueCreated/Updated/Deleted. Jeder Log hat `AffectedData` (ObjectType, Identifier, EventAction, Details, AdditionalData) und ggf. Parent-Log; Workspace-Kontext wird per Pfad-zur-Wurzel angereichert.
- Zustellung: `ActivityLogUser` = Inbox-Einträge für Watcher (`NodeActivityLogSettings.SendToInbox/SendEmail`) und Beteiligte; API `my/activity-logs` (mit `X-Unread-Count`), read/clear/bulk-delete; objektbezogen `assets|tasks|workspaces/{id}/activity-logs`, `application-settings/activity-logs`.
- Echtzeit: Logs werden als `ActivityLogEvent` auf das Topic `EventHub` publiziert; `EventHubCallbackJob` empfängt sie und leitet über `INotificationHubService` an SignalR-Clients (`INotificationHubClient.Publish(Message)`, `ActivityLogUpdateDto`).
- Aufbewahrung: `ActivityLogCleanup`-Job (Cron, Retention 365 Tage, chunked).
- Zusätzlich: Login-Audit (`IUserNotificationService.NotifyAboutUserLoginAsync/LogoutAsync`, `User.LastLoginDate/LoginAttemptsCount`), `MutUser/MutDate/RowVersion` auf fast allen Tabellen, `CreateUser/CreateDate` auf Nodes, `CreatedBy/UpdatedBy` bei Lizenzobjekten, SystemEventStream (siehe 8.3) als externes Audit.

---

## 8. Regeln, Hintergrundjobs, Events

### 8.1 Rule Engine (`Admin.Rule*`, `RuleService`, `RuleJobService`, `RuleCacheService`, `RuleTemplateProcessor`)

- Aktivierbar per `RuleEngine.IsEnabled`. Regeln werden beim Start und nach Änderungen in einen In-Memory-Cache geladen (`RefreshRuleCacheActivity`).
- **Rule**: Name (übersetzt), gilt für `NodeTypeIdentifiers`, hat **Filters** (`RuleTrigger` mit `IsFilter=true`, Vorbedingung), **Triggers** und **Actions**.
- **TriggerOperator**: Equals, NotEquals, GreaterThan, LessThan, GreaterThanOrEquals, LessThanOrEquals, Contains, HasRelationType, IsNewVersion, ValueChanged, IsNewCategory, RegexMatch. Trigger/Filter referenzieren eine Property (Vergleichswert, ggf. pro UserContext/Sprache) oder `AdditionalData`.
- **Auslösung**: (a) bei jeder Property-Änderung (`PropertyService` → `IRuleCacheService.IsPossibleMatch(changes)` → `ExecuteRulesAsync`, pro UserContext), (b) bei neuer AssetVersion (`IsNewVersion`), (c) bei neuer Kategorie (`IsNewCategory`), (d) manuell/nachträglich als **RuleJob** (`POST rules/execute` mit Entity-IDs oder EntityTypes → Hangfire `ExecuteRulesJob`, chunked Staging in `RuleJobEntity`, Report mit Zählern; Reports `GET rules/report`, `{jobId}/report`, `/details`; Cleanup-Job).
- **Actions** (`RuleAction`): entweder Ziel-Property + `Template` oder Start einer `PropertyExtractionDefinition` (AI-Tagging). `OnTriggerMatched=true` läuft bei erfüllten Triggern, `false` bei nicht erfüllten (Else-Zweig).
- **Template-Sprache** (`RuleActionTemplate` → Segmente String/Variable/Function): Variablen `var.year|month|day|hour|minute|second|millisecond`; Funktionen mit Prefix `fnc.`: Input `find(<propertyDefinition>)`, `findInAscendants(<propertyDefinition>)` (sucht Property in Elternkette), Verkettung `increment` (Zähler), `regexExtract(<pattern>)`, `write` (schreibt geänderten Input zurück). Ergebnis wird als Wert in die Ziel-Property geschrieben (pro UserContext).
- Rechte: `RuleCreate/Read/Update/Delete`, `RuleReApplyJobCreate`, `RuleJobReportRead`; Regex mit Timeout (`RegexHelper`).

### 8.2 Hintergrundjobs (Hangfire, SQL-Storage, eigene Connection `BackgroundJobs`)

Registrierung beim Start über `IBackgroundJobService.UpdateRecurringJobsAsync` (Cron aus Konfiguration, ein-/ausschaltbar). Fehlgeschlagene Jobs werden nicht automatisch wiederholt (manuelles Re-Queue). `HangfireActivator` löst Activities per DI, `DiagnosticsActivity*Filter` überträgt die OperationId.

| Job (`BackgroundJobType`) | Auslöser | Funktion |
|---|---|---|
| PropertyMassEdit | API `properties/mass-edit-jobs[/search]` | Massenbearbeitung von Asset-Properties (Chunks `MassEditAssetsChunkSize`/`MassEditPropertiesChunkSize`), Report je Asset/Property |
| Import | API `properties/asset/import` | XLS/XLSX/CSV-Import (Sheet "Metadata", Spalten `PropertyDefinition[Sprache]`), Meldungen je Zeile/Spalte |
| PropertyExtraction | API `properties/extract[/search]`, Rule-Action | AI-Tagging über Provider, optionaler Auto-Import (`ImportAfterExtraction`), Mapping auf Listenwerte, Cooldown/Chunks |
| ArchiveExtraction | Upload mit `ExtractArchive`, API `extractions` | ZIP entpacken → Kategorien/Assets anlegen (Modus full path / files only), Filter, optional Archiv-Asset löschen |
| AssetDerivation | API `derivations`, `derivations/restart[/search]` | (Re-)Rendering vieler Assets, max. `MaxSimultaneousDerivationsStart`, Report |
| PermissionMaterialization | Permission-/Struktur-Änderungen, API `permission-materializations` | s. 6.2 |
| ExecuteRulesJob / RulesJobCleanup | API `rules/execute` / Cron | s. 8.1 |
| RecycleBinCleanup | Cron (`RecycleBin.CronExpression`, Retention 7 Tage) + API `DELETE recycle-bin` | Endgültiges Löschen des Papierkorbs |
| AssetCleanup | Cron (`Cleanup.CronExpression`, täglich 23:00) | Hard-Delete soft-gelöschter Assets/Versionen/Derivate (Binär 14 d, DB 90 d), Materialization-Cleanup, EventStream-Message-Cleanup (3 d) |
| OutgoingMessageQueueEventCleanup | Cron | Outbox aufräumen |
| UploadSessionCleanup | Cron (30 d) | verwaiste Upload-Sessions/Chunks |
| EmailQueue | Cron (`0 */3 * * *`), sofortiger Versand wenn möglich | `OutgoingEmail` senden, Retention 1 d |
| ActivityLogCleanup | Cron (täglich 02:00, 365 d) | s. 7.2 |
| UserAccountManagement | Cron (`0 8-17/3 * * 1-5`) | Konto-Erneuerung (`AccessType.Expiring`: nach 60 Tagen ohne Login → `AccountRenewalPending`, Mail mit Token, Reminder alle 14 d, nach 21 d → `AccountRenewalExpired`), Löschung inaktiver/abgelehnter/abgelaufener Konten nach Retention (je einzeln aktivierbar) |
| WorkspacesJob | Cron (`0 7 * * 1-5`) | Automatische Löschung von Upload-Workspaces nach Retention (90 d) mit Erinnerungs-Mail (28 d vorher), konfigurierbar je WorkspaceType |
| SearchReIndex (eigene Tabelle, kein `BackgroundJobType`) | API `search/reindex` | Vollständige Neuindexierung (Alias-Swap, Batch 1000, Binär-Dokument-Extraktion, Abbruch möglich) |

Zusätzlich laufen dauerhafte `BackgroundJobBase`-Worker: `ProcessingCallbackJob` (Queue `DerivationJobCallback`) und `EventHubCallbackJob` (Topic `EventHub`), sowie `InternalCallbackJob` (internes Topic für Konfigurationsänderungen) und ein `SearchCallbackJob` (Search-Queue; Implementierung im Search-Projekt).

### 8.3 Message Queue / Events

Broker: **ActiveMQ (AMQP 1.0)** oder **Azure Service Bus**, wählbar per Konfiguration; benannte Sender/Receiver (`MessageBroker.*.Senders/Receivers`). Nachrichten sind JSON mit Typname in Message-Properties (`MessageQueueEventProperties`: TypeName, Priority, EventAction, ObjectType, ObjectIdentifier, PropertyDefinitionIdentifier, UserIdentifier, TransactionIdentifier).

| Kanal | Entity/Queue | Richtung | Inhalt |
|---|---|---|---|
| **SystemEventStream** | Topic `Advellence.Sharedien.EventStream` | Backend → extern (Integrationen, SampleEventConsumer) | `DataEventBase`-Events (V1): `AssetEvent`, `CategoryEvent`, `CustomEntityEvent`, `DerivationEvent`, `DerivationTypeEvent`, `EntityTypeEvent`, `PropertyDefinitionEvent`, `PropertyEvent`, `RoleEvent`, `ShareSearchEvent`, `StatisticsEvent`, `TaskEvent`, `TenantEvent`, `UserEvent`, `UserGroupMapEvent`, `WorkspaceEvent`; V2: `ArchiveExtractionEvent`, `AssetRederivationEvent`, `PropertyImportEvent`, `PropertyMassEditEvent`, `RelationEvent`. Jedes Event: `AffectedData[]` (ObjectType, Identifier, EventAction Read/Create/Update/Delete, Details, AdditionalData), `UtcTimeStamp`, `EventCategory` (SystemHealth/DataEntity), `User`. Pro Typ per Konfiguration `SystemEventStream.Publish*Events` ein-/ausschaltbar (User-Events default aus). |
| **EventHub** | Topic `Advellence.Sharedien.EventHub` | Backend → EventHub-Service → SignalR | `ActivityLogEvent` (EventHubEventBase) für Echtzeit-Benachrichtigungen |
| **ProcessingJobStart** | Queue `Advellence.Sharedien.ProcessingJobStart` | Backend → Processing | `ImageDerivationStartMessage`, `CustomDerivationStartMessage`, `VideoDerivationStartMessage`, `OfficeDerivationStartMessage`, `PrintDerivationStartMessage`, `MetadataExtractionStartMessage`/`RestartMessage` (jeweils `JobStartMessage`: `ProcessingId`, `JobInitiatorProvidedMetaData` mit AssetDerivationIdentifier, AssetDerivationJobIdentifier, UserIdentifier, MetadataExtractionJobIdentifier); Priorität (`Priority` Lowest…Highest, `PrioritySelector` z. B. "Metadata") |
| **DerivationJobCallback** | Queue `Advellence.Sharedien.DerivationJobCallback` | Processing → Backend (`ProcessingCallbackJob`) | `DerivationStatusMessage` (JobStatus Started/Processing/Finished/Failed, DataIdentifier, FileHash, FileSize) → `UpdateAssetDerivationActivity`; `MetadataExtractionStatusMessage` (extrahierte EXIF/IPTC/XMP-Properties als Name/Value) → `CreateAssetPropertiesActivity` (als System) |
| **SearchSync** | Queue `Advellence.Sharedien.Search` | Backend → Search (und in-process) | `AssetChangeDto`, `EntityChangeDto` (Create/FullUpdate/PartialUpdate/PartialUpdateFullProperties/Delete), `EntityMoveDto`, `PermissionUpdateDto`, `RelationEntityChangeDto`, `PropertyDefinitionChangeDto`, `BinaryDocumentExtractionDto`, ReIndexing-Batches |
| **Internal** | Topic `Advellence.Sharedien.Internal` | Backend ↔ alle Services | `ApplicationSettingsChangedEvent` (Konfig-Reload) |

Alle ausgehenden Nachrichten laufen durch das Outbox-Pattern (`OutgoingMessageQueueEvent`, siehe Abschnitt 3); Search-Events werden pro Transaktion gemerged; `Priority` steuerbar, "Forced"-Priorität möglich.

---

## 9. Property-System (dynamische Metadaten) – Verhalten

### 9.1 Definitionen und Typen

- **Wertetypen** (`PropertyValueType`): String, Text (mehrzeilig), Date, DateTime, Time, Integer, Float, Decimal, Boolean, TimeSpan. Persistenz in typisierten Spalten der `PropertyValue` (`ValueBool/Int/Decimal/Date/DateTime/DateTimeDurationTicks/Text`), Text sprachabhängig über `Translation`.
- **Listen**: `HasListValues` + `PropertyListValue` (Single/Multi-Select via `AllowMultipleValues`), `AllowCustomListValue` (freie Werte zusätzlich), `PropertyDefinitionListType` (NoList, List, Multiselect, Singleselect) als Query-Filter, Listenwerte übersetzbar, `IsSystemListValue` (z. B. `asset_status_importing|imported|duplicate`, `workspace_type_upload`).
- **DefinitionType**: Standard, Url, Html (Rendering-Hinweis).
- **Suche**: `SearchOption` (NotSearchable, AdvancedSearchable, FulltextSearchable, AdvancedAndFulltextSearchable), `IsSearchableFacet`; Änderungen an Definitionen werden an Elasticsearch als `PropertyDefinitionChangeDto` gesendet (Index-Mapping).
- **Zuordnung** zu NodeTypes (`PropertyDefinitionNodeType`): eine Definition kann für mehrere Entity-Typen gelten (Asset, Category, Workspace, Task, CustomEntity, License …). PropertySets gruppieren Definitionen pro EntityType als Formular.
- **BlacklistedFields**: Felder der Definition, die in der Admin-UI nicht änderbar sind (Name, Description, EntityTypes, PropertyGroup, SortKey, DefinitionType, IsSearchableFacet, Editable, SearchOption, IgnoreCopying, Status, IsFileNamingUnlocked, HasUserContext, AllowMultipleValues, AllowCustomListValue, ListItems) – für Systemproperties.
- **Verwendung prüfen** (`PropertyDefinitionUsage`): vor Löschung wird geprüft, ob die Definition in Properties, Watermark-Definitionen/Triggern, Rule-Actions/Triggern, FileNamingDefinitions, PropertyExtraction-Mappings verwendet wird.
- Endpunkt `GET property-definitions/value-types` liefert die Typliste; Listen-Query mit Filtern (Status, Group, ValueType, ListType, Facet, Editable, System, FileNaming, UserContext, DefinitionType, SearchFilter) und Sortierung.

### 9.2 Systemproperties (Mirror-Strategien)

Systemproperties (`IsSystemProperty`) spiegeln Entity-Felder in Properties und umgekehrt (`ICorePropertyChangeHandler` je `SystemPropertyStrategy`, ausgeführt durch `ISystemPropertyService` bei Create/Update/Delete):

| Entity | Systemproperties (Identifier) | Strategie |
|---|---|---|
| AssetVersion | `asset_identifier`, `asset_status`, `asset_type`, `asset_version`, `file_name`, `file_hash`, `file_extension`, `file_size`, `imported_by`, `system_import_date`, `entity_status` | `MirrorFileNamePropertyToAssetVersionEntity` (Umbenennen der Datei über Property `file_name`, Endung darf nicht wechseln), `MirrorAssetStatusPropertyToAssetSetEntity` |
| Workspace | `workspace_name`, `workspace_type`, `workspace_is_template`, `workspace_identifier`, `created_at` | `MirrorWorkspaceNameProperty…`, `MirrorWorkspaceTypeProperty…` |
| Task | `task_name`, `task_description`, `task_status`, `task_assignee`, `task_type`, `task_due_date`, `task_update_date`, `created_at`, `workspace_identifier` | `MirrorTaskPropertiesToTaskEntity` (Property-Änderung setzt Name/Description/Status/Assignee/DueDate am Task; löst Benachrichtigung "assigned" aus) |
| Category | `category_name`, `created_at` | `MirrorCategoryNamePropertyToCategoryEntity` |
| CustomNode | `customnode_name`, `created_at`, `entity_identifier` | `MirrorCustomEntityNamePropertyToCustomEntity` |
| BillingContact | `billingcontact_name`, `billingcontact_email`, `entity_status`, `contact_street/zip_code/city/country/phone_number/first_name/last_name/email`, `banking_bank_name/swift_code/account_name/iban/account_number` | `MirrorBillingContactPropertiesToBillingContact` |
| RightsOwner | `rightsowner_name`, `rights_owner_type`, `entity_status`, `license_rights` | `MirrorRightsOwnerPropertiesToRightsOwner` |
| License | `license_name`, `license_rights`, `license_description`, `license_period_start`, `license_period_end`, `entity_status` | `MirrorLicensePropertiesToLicense` |
| LicensePurchase | `license_purchase_order_id`, `license_purchase_region_identifier`, `license_purchase_from_date`, `license_purchase_to_date`, `license_purchase_price`, `license_purchase_purchased_at` | (nur lesend, unklar ob Mirror) |

Gemeinsame Felder `Name`/`Description` (Translation-Feldnamen) existieren für NodeType, EdgeType, Role, Tenant, Category, Workspace, DerivationType, PropertyDefinition, PropertyGroup, Rule, State, UserContext, UserContextGroup, Language.

### 9.3 Werte-Operationen

- Pro Entity: Get (alle / gefiltert nach Definitionen), Create, Update (ersetzt Werte), Edit (PATCH, behält nicht gesendete Werte), Delete, Set-mehrere (PUT/PATCH mit `SetPropertiesDto` → `SetPropertyResult` je Property mit `SetPropertyResultReason`: Success, CreateOrUpdateRestricted, InheritancePermissionDenied, PropertyDefinitionNotExists, NotEditable, ChangeSystemPropertyNotAllowed, Invalid, CreateOrUpdatePropertyValuesFailed, CreateInEditNotAllowed), Bulk-Delete (`DeletePropertyResultReason`).
- **Sprachen**: übersetzbare Werte als `TranslatedValue[]` (Language + Value); Antwortsprachen über Header `X-Data-Languages` (`LanguageOptions`: gewünschte Sprachen, Fallback auf Default-Sprache, `LoadAllLanguages`).
- **UserContext**: Werte können pro `UserContext` differieren (`HasUserContext`); Anfrage-Kontexte über Header `X-User-Contexts`; Mass-Edit/Import/Export unterstützen UserContext-Spalten.
- **Vererbung**: Category-Properties können an AssetSets vererbt werden (`PUT categories/{id}/properties/{pid}/inheritance-node-types`, `PropertyNodeTypeInheritance`); `GetPropertiesOfEntitiesWithInheritanceFilterAsync` liefert vererbte Werte (nur `Category → AssetSet` erlaubt; Recht `CategoryPropertyInheritance*`).
- **Kopieren**: neue Version ← alte Version; Workspace → Assets (`copy-properties`, `copy-all-properties` mit `PropertyActionStrategy` KeepExisting/OverwriteExisting/Abort/DeleteExisting/Merge; Workspace-Flag `CopyPropertiesToAssets`); beim Upload `CopyPropertiesFromEntities` (z. B. aus Kategorie/Workspace) und direkte `Properties`; Kategorie-Copy kopiert Properties mit.
- **Validierung**: `PropertyValidationService`/FluentValidation (Typ, Listenwert, Sprache existiert, UserContext existiert); Cache (`Property.ValidationCache`).
- **PropertySets als Pflichtfeld-Regeln**: `AssetValidationService.ValidateAssetSetPropertiesAsync` prüft für jedes Asset alle PropertySets mit passenden Conditions (z. B. `asset_type = Image`) auf Mandatory-Felder → Ergebnis je Asset/Definition; genutzt von `POST assets/validate`, `workspaces/{id}/assets/validate` und beim **Finalisieren** eines Workspace (nur valide Assets werden in die Zielkategorie verschoben).
- **Mass-Edit**: Ziel = explizite AssetSets oder Suchabfrage (`mass-edit-jobs/search`); je Property Strategie für Property/Wert/Übersetzung; Limit je Rolle; Report mit `PropertyMutationResult` (Created, AppliedKeepExisting, AppliedOverwriteExisting, MissingAsset, MissingPropertyDefinition, Failed, NotAllowed, AppliedDeleteExisting, IgnoredDelete, NotEditable, AppliedMerge, IgnoredMerge).
- **Export**: `POST properties/asset/export[-formdata]` → XLS/XLSX/CSV (`PropertyExportOptions`: Format, Label-Sprache, Locale, Datensprachen, UserContexts, Definitionen) für Assets inkl. Versions-Spalten; Limit `PropertyExportEntityLimit`.
- **Import**: `POST properties/asset/import` (multipart) → Job; Datei wird im Storage abgelegt (`DataId`), Ergebnis mit Meldungen (`PropertyImportMessageType`: FileFormatNotSupported, FileFormatError, MetadataTabNotFound, EntityIdentifierNotFound, EntityTypeNotFound, PropertyDefinitionIdentifierOrLanguageParseError, NoReadPermissionOnEntity, CouldNotLoadPropertiesForEntity), Mutationen je Entity.
- **Metadaten-Extraktion aus Dateien** (EXIF/IPTC/XMP etc.): erfolgt im Processing-Service (`MetadataExtractionStartMessage` mit Tag-Mapping-Konfiguration), Ergebnis wird als Properties auf die Version geschrieben; Neustart via `properties/extract` (V1–V5) bzw. `assets` Re-Derivation.
- **AI-Tagging** (`PropertyExtractionDefinition`/`Job`): Provider Clarifai/Azure CV/Imagga (Mock-Modus `PropertyExtraction.UseMocks`), `ConfidenceThreshold`, Sprach- und Custom-Model-Angabe, Mapping Tag → Definition/Listenwert (`IsCustomListValue`), Ergebnisse einsehbar (`jobs/property-extractions/{id}/extractions`), Auto-Import optional, Auslösung per API oder Rule-Action.

### 9.4 Weitere property-nahe Funktionen

- **PropertyGroups**: Reihenfolge (`SortKey`), Status, Zähler der Definitionen.
- **Facetten**: `FacetPropertyDefinitionIdentifiers` + `FacetFilters` (Operator Equals/IdentifierEquals) in allen Suchabfragen; `MaxFacetSize` 50.
- **File Naming**: Tokens aus `IsFileNamingUnlocked`-Definitionen + Variablen → Dateinamen beim Download/Zip/Share (`GenerateAssetDerivationFileNamesAsync`, Vorschau `file-naming-definitions/{id}/preview`, Override-Format pro Anfrage `OverrideFileNameFormat`, Fallback-Format aus Konfiguration).
- **Watermarks**: Property (Liste/Bool) an der Version steuert, ob zusätzlich ein Wasserzeichen-Derivat (`<Type>_Watermark`) über eine `DerivationJobDefinition` (Overlay) gerendert wird; Cache-Refresh bei Property-Änderung (`IsTriggerMatch`, `RefreshWatermarkOnMatchAsync` löscht/erzeugt Derivate); `DisplayInUI`; Watermark-Dateien als `FileReference` (Usage `Watermark`, max 10 MB, jpg/png/gif/tif).

---

## 10. Lizenz-/Rechtemanagement (DRM) – Verhalten

- Objekte: `License`, `RightsOwner`, `BillingContact`, `PriceList`, `Buyout`, `UserContextGroup`, `Purchase` (siehe 4.8); alle unter Route `api/v{5|6}/drm/...`, Rechte `License*`, `LicenseOffer*`, `LicensePurchase*`; Resource-Permissions gelten global für alle Lizenzobjekte (`drm/licenses/resource-permissions`).
- **Lizenz ↔ Assets**: `POST drm/licenses/{id}/entities` verknüpft AssetSets über Edge `LicenseDrm`; `GET …/entities` mit Typfilter und Property-Include; Assets zeigen ihre Lizenzen über Relations-Suche.
- **Preismodell**: Buyout (RightsOwner × PriceList × Lizenz) definiert Basis + Zusatzgebühr-% + Gültigkeit; PriceList enthält Prozentsätze je UserContext/UserContextGroup (`PriceListUserContext.Percentage`); `LicenseOffer` berechnet daraus je Kontext die Preise (`PriceCalculationData`: Basis, Fee %, Kontext-%, `TotalPurchaseDays` aus `PurchaseDurationInDays`) in allen Währungen, ermittelt alternative Kontextgruppen (`GetAlternativeContextGroupsAsync`), Anzahl enthaltener Assets und Thumbnail (erstes lizenziertes Asset).
- **Suche nach Angeboten**: `POST drm/license-offers/search` (`LicenseOfferSearchQuery`: SearchTerm, ContextIdentifiers (Group/UserContext), ValidFrom/Until, RightsIdentifiers (Listenwerte `license_rights`), PurchasedForContext, LicenseIdentifiers, EntityIdentifiers, Paging, Sprachen).
- **Kauf**: `POST drm/purchases` erzeugt `Purchase`-Node (OrderId, Kontext/Gruppe, Start/Ablauf, Preise je Währung, enthaltene Entities) und löst E-Mails aus (`LicensePurchaseCustomer`, `LicensePurchaseBillingContact`, `LicensePurchaseAdmin` – Adminadressen aus `LicensePurchase.EmailContacts`); Käufe durchsuchbar (`purchases/search`), Report der enthaltenen Entities.
- **Rechteinhaber/Abrechnungskontakte**: CRUD + Properties (Adresse, Bank) + Avatar (RightsOwner) + Suche/Quicksearch; Status Active/Inactive.
- Abgrenzung: Der ältere EdgeType `Licence` (Semantic 7) existiert neben `LicenseDrm` (10) – Verwendung des alten Typs unklar (vermutlich Legacy).

---

## 11. Suche, Derivate-Pipeline, Storage

### 11.1 Suche (Elasticsearch, Projekt `Sharedien.Search`)

- Backend nutzt `Sharedien.Search.Core`/`Infrastructure.Elasticsearch` **in-process** (`IAssetSearchService`, `IEntitySearchService`, `IIndexingService`, `ITransformationService`, `IReIndexingService`) und synchronisiert Änderungen zusätzlich per Queue (`SearchSync`). Adapter: `Infrastructure/Services/SearchService.cs` (implementiert `ISearchService` des Core), `SearchReIndexingService`, `SearchTransformationService` (BO → `AssetChangeDto`/`EntityChangeDto`), `BinaryDocumentExtractionService` (Volltext aus Dokumenten, signierter Download-Link, max. 40 MB).
- Indizierte Objekte: Assets (Version-basiert: Identifier, Created, AssetType, FileName, Paths mit Principal-Berechtigungen, ParentCategory, Properties, FileHashes, Version, RelationEntities), Entities (Workspace, Task, Category, CustomEntity, BillingContact, RightsOwner, License, Purchase mit Path/Properties/CustomFields), Relationen, PropertyDefinitions (Mapping).
- **Query-Modell** (`BaseSearchQuery`): `SearchTerm`, `SearchInProperties`, `LanguageIdentifiers`, `ExactSearch`, `AdvancedSearchFilters` (Filter: PropertyDefinition, EntityType, Terms, `Operator`: HasValue, HasNoValue, Equals, NotEquals, Contains, DoesNotContain, FuzzyEquals, FuzzyContains, GreaterThanOrEquals, LessThanOrEquals, Between, IdentifierEquals, IdentifierNotEquals; Sprachen), `Sort[]` (PropertyDefinition, Richtung, Sprache), `IncludePropertyDefinitionIdentifiers` (Projektion), `FacetPropertyDefinitionIdentifiers`, `FacetFilters`, `Operator` (Or/And, Default aus User-Settings/Konfiguration), `Cursor` (V6, für Scrollen über `MaxResultWindow`). Asset-spezifisch: `Categories` + `RecursivelyFilterCategories`, `Context` (UserContext), `Filename`, `FileHash`, `SearchInDocumentExtraction`, `EnrichAssetQuery` (Thumbnails, Categories, Properties, Permissions, Restrictions). Task: `ParentEntities`; Workspace: `IsTemplate`, Thumbnails; CustomEntity: `EntityTypeIdentifier`.
- **Ergebnis** (`SearchResultSet`): Items (angereicherte Extended-Objekte), `Facets` (FacetDto mit Items/Counts), `AutoCompleteSuggestions`, `Cursor`, `X-Total-Count`. Quicksearch: Autocomplete (Fragment 15, 10 Vorschläge, Fuzziness AUTO) + Direkttreffer.
- Berechtigungsfilter serverseitig über Principal-IDs im Index; Restriktionen (`SearchableAssetRestriction`) für PropertyDefinition-Blacklists.
- Re-Index: Jobs mit Report, Cancel, Alias-Swap, Batching; PropertyDefinition-Mapping-Update.
- Relationen-Suche (`assets/{id}/relations/search`, `custom-entities/…/relations/search`, `assets/relations/search`) läuft über SQL (`RelationRepository`, Pfad-Segmente, Richtung, Typ-/NodeType-Filter, Thumbnails), nicht über Elasticsearch.

### 11.2 Derivate-/Rendering-Pipeline (Schnittstelle zu Processing)

1. Upload (Original-Derivat, Status Pending) → `DerivationNotificationService.StartMetadataExtractionAndDerivationsAsync`: Metadata-Extraktion (Priorität "Metadata") und alle **Default**-DerivationJobDefinitions für den FileType (`AssetTypeDerivationType.IsDefaultDerivation`, `DerivationJobDefinitionInputFileType`) werden als `*DerivationStartMessage` auf die Queue gelegt (`ProcessingQueueProducer`), Derivat-Datensätze mit Status Pending angelegt.
2. Processing rendert und lädt das Ergebnis in den Storage (`DataIdentifier`), meldet `DerivationStatusMessage` → Backend setzt Status/FileType/Size/Hash (`UpdateAssetDerivationActivity`), erzeugt `DerivationEvent`, aktualisiert Thumbnail/Preview.
3. **On-Demand**-Derivate (`IsOnDemand`) werden erst per `POST assets/{id}/derivations/{type}` gestartet; **manueller Upload** von Derivaten (`AllowManualUpload`, Upload-Typ `AssetDerivation`); `CropDerivationCreate` für Crop-Varianten (ImageDerivation-DTOs mit `CropDataDto`).
4. **Re-Derivation**: `derivations/restart` (Asset-Liste × AssetType→DerivationTypes, `RestartFailedDerivations`) oder per Suchabfrage; als Hangfire-Job mit Report; Status-Übersichten `derivations/status/overview|types|details`.
5. **Custom Derivations** (`CustomDerivationJobDefinition`): Schrittfolge mit `CustomStepType`, Ein-/Ausgabedateien, Argument-Strings mit Variablen/Expressions, Zielprofil/Intent (ICC), Color-Profile-Overrides – erlaubt beliebige Konverter-Pipelines im Processing-Service.
6. Image-Definitionen erlauben Overlays (Watermark), Crop/Scale (absolut/relativ, Fill, Up-/Downscaling verhindern), Rotate, Sharpen, Blur, Opacity, Hintergrundfarbe (RGBA/CMYKA), Farbraum/Profil/DPI/Kompression, Ausgabeformat/Qualität, Frame-Index (Video/Mehrseitig).
7. Thumbnails: `UseAsThumbnail`/`UseAsPreview`; animierte Thumbnails für konfigurierte AssetTypes (`AnimatedThumbnailAssetTypeIdentifiers`, Derivat `Thumbnail_Animated`); Derivat-Typen `Original`, `Thumbnail`, `Thumbnail_Animated`, `Screen`, `Print`, `Metadata`, `Watermark` als System-Identifier.
8. Download: `IDownloadLinkService` erzeugt signierte, zeitlich begrenzte URLs (`SharedAccess.Read/WriteMinimumExpiryTimeInMinutes`), Zip-Download mehrerer Assets/Derivate bzw. ganzer Kategorie (rekursiv, Ordnerstruktur, FileNaming), Größenlimit `GeneratedZipMaxFileSizeInBytes`; `DownloadOriginalAlways` je AssetType; Contact-Print-PDF (Formate 1x1…5x3 Portrait/Landscape, Property-Beschriftung je Layout konfigurierbar).

### 11.3 Storage-Schnittstelle

- `IStorageProvider` (Common): `UploadAsync`, `UploadChunkAsync`, `CombineChunksToFileAsync`, `CombineFilesToFileAsync`, `RetrieveAsync`, `RetrieveRangeAsync`, `DeleteAsync`, `DeleteChunkAsync`, `CopyAsync`, `ExistsAsync`, `GetFileInfoAsync` (Größe, ContentHash), `GetReadAccessUrl`, `GetUploadAccessUrl`, `GetChunkUploadAccessUrl`, `GetDeleteAccessUrl`, `GenerateIdentifier`, `MaxChunkSizeBytes`.
- Implementierungen: FileSystem (Hierarchie-Pfade aus Identifier), Azure Blob (SAS-URLs), StorageHandler (Sharedien-eigener Dienst mit HMAC-signierten URLs, `AccessKey`/`SharedAccessSigningKey`).
- **Upload-Flow (neu, V3+)**: `POST assets/upload/start` (`UploadStartRequestDto`: Typ Asset/AssetVersion/AssetDerivation, Dateiname, Ziel-Kategorie/Workspace/Task/AssetSet, Properties, CopyPropertiesFromEntities, CreateRelations, ExtractArchive) → Antwort mit `ChunkUploadRequestUrl` + `MaxChunkSizeBytes` → `GET assets/upload/chunk?ut=<signed token>` liefert Storage-Upload-URL je Chunk → Client lädt direkt in den Storage → `POST assets/upload/finish` (`ut`-Token) kombiniert Chunks, erkennt FileType (Endung + optional Magic-Number-Erkennung `FileRecognition`, verbotene Endungen), legt Asset/Version/Derivat an, kopiert/setzt Properties, erzeugt Relationen, startet Archiv-Extraktion, meldet Duplikate (`ExistingAssetSetIdentifier`). Signierte Tokens verhindern Manipulation (`SignedUrlValidationResult`: MissingSignature, SignatureMismatch, Expired, PermissionMismatch).
- **Upload-Flow (alt, obsolet)**: server-seitige Chunk-Sessions (`upload/chunks/start|sessions/{id}/sequencenumber/{n}|finish|cancel`) für Assets, Versionen, Workspace-Assets, Derivate.
- `FileReference`-Uploads (Avatare, Watermark-Bilder) laufen über multipart (`files/upload`, `users/{id}/avatar/upload`, `workspaces/{id}/avatar/upload`, `drm/rights-owners/{id}/avatar/upload`) mit Größen-/Endungslimits je `FileUsageType`.

---

## 12. Auth/Identity, Multi-Tenancy, Lokalisierung, Benachrichtigungen

### 12.1 Authentifizierung und Identity-Integration

- API-Authentifizierung: eigenes Schema `SharedienAuthenticationHandler` – Bearer-Token aus Header (alternativ Form-Feld `Authorization` oder Query `access_token` für SignalR-Hubs) → `TokenValidationService` → Introspection beim Identity-Service (`POST {ServiceUrl}/connect/introspect`, Client `sharedien-api`/Secret), Ergebnis gecacht (`ValidationCache`, 1 h) → JWT wird ohne Signaturprüfung geparst (Introspection gilt als Prüfung), Claims → `ClaimsPrincipal` (User-ID aus Claim `sub`, Gruppen aus Claim `scope` – konfigurierbar `Claims.UserIdClaimType/UserGroupClaimType`).
- OIDC (Authorization-Code) nur für Swagger-UI/Dokumentationsschutz (`WebClientId sharedien-web`, Scopes `sharedien-api`, `offline_access`).
- Service-to-Service: `IdentityTokenService.GetNewTokenAsync` (Client-Credentials, opaque Token) – genutzt von Clients/Services.
- **Identity-Service teilt Code**: `src/Identity` referenziert `Sharedien.Api.Infrastructure` direkt; damit laufen Registrierung (`IRegistrationService`: RegisterUser → E-Mail-Verifikation (`ExpiringCode` EmailVerification) → Admin-Freigabe `FinalizeUserRegistration` mit Gruppenzuweisung → Mails Confirmed/Declined), Passwort-Reset (`IPasswordResetService`, Code `PasswordReset`), Konto-Erneuerung (`AccountRenewal`-Token), Login-Audit (`RecordLoginAttemptAsync`, Lockout via `LoginAttemptsCount`, Status `Locked`, `PUT users/{id}/unlock`), Kontostatus-Handling (`IAccountStatusService`) und **Provisioning externer Nutzer** (`IProvisioningService.ProvisionExternalUserAsync(providerName, claims)`: User anlegen/aktualisieren aus Claims per `AttributeMappingConfiguration`, Gruppen aus Claims oder **Microsoft Graph** (direkte/transitive Gruppen), Mapping über `ExternalGroup` → UserGroups, automatische Anlage fehlender Gruppen als "Provisioned user group", Entfernen aus nicht mehr zutreffenden provisionierten Gruppen) im Prozess des Identity-Services. LDAP (`ILdapService.GetUserInfo`) liefert Name/E-Mail/Gruppen für Windows-Auth-Nutzer.
- Lokale Nutzer: PBKDF2-Passwort (`PasswordHelper`), `users/{id}/password` (Admin setzt), `my/password` (Änderung mit altem Passwort), `AccessType` Permanent/Expiring.
- Konfigurationsmodelle für Identity (in Common, vom Identity-Service genutzt): OpenIdConnect-Provider, SAML, Windows-Auth/LDAP, Lockout, Registration, Branding, Disclaimer, Anonymous Access, Mock – d. h. Sharedien unterstützt lokale Konten, OIDC-Föderation (z. B. Azure AD), SAML und Windows/LDAP.

### 12.2 Lokalisierung

- **UI-Sprache** des Users (`UserSettings.DisplayLanguage`, `Locale`) vs. **Datensprachen** (`MAM.Language`, verwaltbar über `data-languages`, Status, Default-Sprache).
- Alle Bezeichnungen (NodeType-, EdgeType-, Rollen-, Tenant-, Kategorie-, Workspace-, DerivationType-, PropertyDefinition-, PropertyGroup-, Listenwert-, Rule-, State-, UserContext-Namen/-Beschreibungen) und übersetzbare Property-Textwerte liegen in `MAM.Translation` (`ObjectType`, `TranslationIdentifier`, `FieldName`, `LanguageId`); `ITranslationService` liest/schreibt mit Fallback auf Default-Sprache; `TranslatedValueDto` (Language, Value) in allen DTOs.
- Header `X-Data-Languages` bestimmt die gelieferten Sprachen; Export mit Label-Sprache und Locale (Datums-/Zahlenformat).
- Server-Ressourcen: `Texts.resx` (Common), `ErrorNames`, Notification-Templates de/en (`NotificationTemplateRepository.de|en.resx`), `ResourcesPath = "Resources"`.

### 12.3 Benachrichtigungen und E-Mail

- **E-Mail-Infrastruktur**: SMTP über MailKit (`Email.*`: Server, Port, SSL, Sender, `AllowedRecipients`-Whitelist, `BaseUrl` für Links), Warteschlange `OutgoingEmail` (Template-ID + Parameter + Empfänger-User/-Adressen), Versand sofort oder per `EmailQueue`-Job; Templates mit **DotLiquid** (Subject, HtmlContent, TextContent, Pre/PostBody, Filter für Datum und TranslatedValue) aus Ressourcen (de/en).
- **Template-Identifier** (`NotificationHelper`): `TransitionExecuted`, `AddedTaskAssignee`, `AssetUploadedToWorkspace`, `AssetDeletedFromWorkspace`, `AssetUploadedToTask`, `AssetDeletedFromTask`, `AddedUserToWorkspace`, `ShareCreated`, `UserRegistrationConfirmed`, `UserRegistrationDeclined`, `UserRegistrationEmailVerification`, `UserRegistrationReview`, `PasswordReset`, `AccountRenewalPending`, `WorkspaceEntryDeletionInfo`, `UploadSessionDeletionReminder`, `WorkspaceDeletionReminder`, `LicensePurchaseBillingContact`, `LicensePurchaseCustomer`, `LicensePurchaseAdmin`, `PreBody`/`PostBody`.
- **Trigger** (`INotificationTriggerService`): Asset in Workspace/Task hochgeladen/gelöscht, Collaborator zu Workspace hinzugefügt (Resource-Permission), Task-Transition ausgeführt, User einem Task zugewiesen (auch via Property `task_assignee`), Share erstellt (Mail mit Token-Link an Empfänger), Registrierung finalisiert, Konto-Erneuerung, Workspace-Löschungserinnerung, Lizenzkauf.
- Empfängerlogik: Watcher (`watchers`-Endpoints an Workspace/Task), Beteiligte (Owner, Assignee), `UserNotificationSettings.DisableNotifications` pro Workspace, `NodeActivityLogSettings.SendEmail/SendToInbox`.
- **Inbox/Echtzeit**: ActivityLog-Notifications (`my/activity-logs`) + SignalR-Push über EventHub (siehe 7.2).

### 12.4 Konfigurationsoberfläche (Auszug `appsettings.yml`, alle über DB/API überschreibbar)

Logging, StorageProviders (StorageHandler/Azure/FileSystem), SharedAccess (URL-Ablauf), Processing/InternalCallbackJob/ProcessingCallbackJob (Queue-Namen), Database (CreateIfNotExists, Timeouts), Email (+Queue, AllowedRecipients), MessageBroker (AzureServiceBus | AMQP mit Sendern/Empfängern), Hosting (PathBase, ForwardedHeaders, KnownProxies/Networks, HttpsRedirection), SystemEventStream (Publish-Flags je Eventtyp), ActivityLog (EnabledObjectTypes, RequireTriggeredByUser, Cleanup), Search (Elasticsearch-Index/Alias, AutoComplete, BinaryDocumentExtraction, Facet-/Scroll-Limits, ReIndexing), SearchService (Autocomplete-Methode ExactAndFuzzy, Default-Operator), BackgroundJobs (Poll-Intervall, Dashboard), Assets (verbotene Endungen, Zip-Limits/Namen, Status nach Upload, animierte Thumbnails, Duplikat-Erkennung an/aus, FileNaming-Fallback, ParentChild-Delete-Verhalten), Authentication (Service-URLs, Endpoints, Client-IDs/Secret, Scopes, Caches, Swagger-Schutz), Claims, Property (Validation-Cache, Mass-Edit-Chunks), Derivation (MaxSimultaneousDerivationsStart), RecycleBin (Cron, Retention), File (FileUsageTypes mit Limits), ContactPrintMetadata (Beschriftungsfelder je Layout), Sharing (AllowSharingAcrossTenants), RuleEngine (Enabled, Chunk, Cleanup), UploadSessions (Cleanup), Upload (SigningKey), Extractions (FileNameFilters), FileRecognition (Enabled, Scope FilesWithoutExtension/AllFiles, Mode Enrich/Strict, Extension Append/Replace, Byte-Fenster), UserAccounts (Cron, Renewal, Deletion), Cleanup (Cron, AssetCleanup, MaterializationCleanup, EventStreamMessageCleanup), Workspaces (Cron, Deletion je Typ), LicensePurchase (EmailContacts), PropertyExtraction (Chunk, Cooldown, Separator, Provider-Konfigurationen Clarifai/Azure/Imagga).

---

## 13. Offene Punkte / "unklar"

| Thema | Befund |
|---|---|
| Task ↔ State-Zuordnung | BO `Task.StateIdentifier`; Persistenz vermutlich über Edge zum State-Node (`MoveEntity`), nicht als Spalte – im Repository nicht verifiziert. |
| EdgeType `Licence` vs. `LicenseDrm` | Beide existieren; `Licence` (Semantic 7) wird in aktuellem Code nicht aktiv verwendet (Legacy). |
| `IsTransparent` auf Node | Spalte vorhanden, Verwendung im Core nicht gefunden (vermutlich Root-/technische Knoten). |
| EF-Hinweis im Readme | Readme beschreibt EF-Modellgenerierung; Projekte referenzieren kein EF – veraltet. |
| `Sharedien.Api.Dto` | Projekt existiert nicht; DTOs liegen in `Sharedien.Api.Contracts`. |
| UserContext-Semantik | Technisch: paralleler Wertesatz je Property + Preis-Prozentsätze; fachliche Bedeutung (Markt/Region/Marke) im Code nicht dokumentiert. |
| Tenant an AssetVersion | Wird fest mit `default` angelegt (Kommentar "not tenant-specific for now"). |
| Statistik-Events | `StatisticsEvent` (EventCategory SystemHealth) existiert im Contract; Erzeuger im Backend nicht identifiziert. |
| SearchCallbackJob | Konfiguriert (`Search.SearchCallbackJob`), Implementierung liegt im Search-Projekt (nicht analysiert). |
| Identity-Endpunkte | Identity-Service nutzt Core-Services in-process; welche Backend-HTTP-Endpunkte das Identity-Frontend zusätzlich aufruft, wurde nicht analysiert. |
| Default-Rollen/Permissions je Rolle | 10 Rollen-Inserts über Migrationen verteilt; exakte Permission-Sets je Rolle nur aus 280 SQL-Skripten rekonstruierbar (nicht ausgewertet). |

---

## Anhang A – Vollständiges Tabellenverzeichnis (aus `Sharedien.Common.DbScheme`)

Alle 124 Tabellen/Views mit Spalten (Schema `Admin` = Verwaltung/Berechtigungen, `MAM` = Media Asset Management). Views (`*View`) filtern in der Regel `IsDeleted = 0`. Spaltennamen ohne FK-Suffix-Auflösung; `MutUser/MutDate/RowVersion` = Audit/Concurrency.

| Tabelle | Spalten |
|---|---|
| `Admin.Configuration` | MutUser, MutDate, RowVersion, ConfigurationId, Path, Value |
| `Admin.DerivationTypeRestriction` | Identifier, RoleId, DerivationTypeId, Action, AssetTypeId |
| `Admin.EntityPermission` | Identifier, RowVersion, EntityPermissionId, ArchiveExtractionJobId, AssetDerivationJobId, FileNamingDefinitionId, PropertyExtractionJobId, PropertyImportId, PropertyMassEditId, PropertySetId, RoleId, SavedSearchId, UserGroupId, UserId, WorkspaceId |
| `Admin.ExternalGroup` | Identifier, RowVersion, ExternalGroupName, ExternalGroupDisplayName, ExternalGroupId |
| `Admin.ExternalGroupUserGroups` | RowVersion, ExternalGroupUserGroupsId, ExternalGroupId, UserGroupId |
| `Admin.MaterializedNodePermission` | RowVersion, MaterializedNodePermissionId, NodeId, NodePermissionId |
| `Admin.NodePermission` | Identifier, RowVersion, Status, NodePermissionId, NodeId, UserGroupId, UserId, RoleId |
| `Admin.PermissionMaterialization` | RowVersion, PermissionMaterializationId, Identifier, UserIdentifier, TransactionIdentifier, CreatedAt, FinishedAt, Status |
| `Admin.PermissionMaterializationOperation` | PermissionMaterializationOperationId, PermissionMaterializationId, EntityIdentifier, EntityTypeIdentifier, PermissionIdentifier, JobType |
| `Admin.PropertyDefinitionRestriction` | Identifier, RoleId, PropertyDefinitionId, Action, Status |
| `Admin.PropertyValueRestriction` | PropertyValueRestrictionId, RoleId, PropertyId, Action, MutUser, MutDate, RowVersion |
| `Admin.RoleView` | RoleId, Identifier, TranslationIdentifier, Status, IsDeleted, IsSystem, PropertyEditEntityLimit, PropertyExportEntityLimit, MutUser, MutDate, RowVersion |
| `Admin.RoleObjectType` | RoleObjectTypeId, RoleId, ObjectType |
| `Admin.RoleObjectTypePermission` | RoleObjectTypePermissionId, RoleId, Permission |
| `Admin.Rule` | RuleId, Identifier, TranslationIdentifier, RowVersion |
| `Admin.RuleAction` | RuleActionId, RuleId, PropertyId, Template, OnTriggerMatched, AdditionalData, RowVersion, PropertyExtractionDefinitionIdentifier |
| `Admin.RuleNodeType` | RuleNodeTypeId, RuleId, NodeTypeId |
| `Admin.RuleTrigger` | RuleTriggerId, RuleId, TriggerOperator, PropertyId, IsFilter, AdditionalData, RowVersion |
| `Admin.RuleJob` | RuleJobId, Identifier, Status, CreatedAt, InitiatedBy, InitiatedAt, FinishedAt, BackgroundJobId, TotalCount, CurrentCount, MatchedTriggerCount, NotMatchedTriggerCount, MatchedActionExecutedCount, NotMatchedActionExecutedCount, ActionSkippedCount, ErrorCount, MutDate, RowVersion |
| `Admin.RuleJobEntity` | RuleJobEntityId, RuleJobId, RuleId, NodeId, UserContextId, IsProcessed, IsMatchedTrigger, IsMatchedActionExecuted, IsNotMatchedActionExecuted, IsError, ErrorReason |
| `Admin.RuleJobEntityValueExecuted` | RuleJobEntityValueAppliedId, RuleJobEntityId, Identifier, ValueExecuted |
| `Admin.RuleJobEntityTypeIdentifier` | RuleJobId, EntityTypeIdentifier |
| `Admin.RuleJobEntityIdentifier` | RuleJobId, EntityIdentifier |
| `Admin.RuleJobRuleIdentifier` | RuleJobId, RuleActionId, RuleIdentifier |
| `Admin.TenantView` | TenantId, Identifier, Status, IsDefaultTenant, TranslationIdentifier, IsDeleted, AcceptsRegistrations, MutUser, MutDate, RowVersion |
| `Admin.UserView` | UserId, Identifier, UserName, FirstName, LastName, EmailAddress, Department, AccessType, Status, StatusChangeDate, IsDeleted, PasswordSalt, PasswordHash, ProviderName, ProviderSubjectId, LastLoginDate, LastLoginAttemptDate, LastPasswordResetDate, LoginAttemptsCount, CreatedAt, Company, ContactPerson, PhoneNumber, MutUser, MutDate, RowVersion |
| `Admin.UserGroupView` | UserGroupId, Identifier, Name, Description, IsDeleted, MutUser, MutDate, RowVersion |
| `Admin.UserGroupUser` | UserGroupUserId, UserGroupId, UserId, IsProvisioned, MutUser, MutDate |
| `Admin.UserSettings` | MutUser, MutDate, RowVersion, UserSettingId, UserId, DisplayLanguage, Locale, LanguageId, UserContextId, UiSettings, AvatarFileReferenceId, SearchTermOperator |
| `Admin.UserGroupToRoleMap` | UserGroupToRoleMapId, TenantId, RoleId, UserGroupId, MutUser, MutDate, RowVersion |
| `Admin.UserNotificationSettings` | RowVersion, UserNotificationSettingsId, NodeId, UserId, DisableNotifications |
| `Admin.UserRegistration` | UserRegistrationId, UserId, TenantIdentifier, ApprovedAt, MutUser, MutDate, RowVersion |
| `Admin.ExpiringCode` | ExpiringCodeId, UserId, Code, CodeType, CreatedAt, ExpiresAt, MutUser, MutDate, RowVersion |
| `MAM.ActivityLog` | Identifier, RowVersion, ActivityLogId, ParentActivityLogId, NodeId, ObjectType, ObjectIdentifier, Type, ChangeType, UserIdentifier, CreatedAt |
| `MAM.ActivityLogAdditionalData` | ActivityLogAdditionalDataId, ActivityLogId, Name, Value |
| `MAM.ActivityLogUser` | RowVersion, ActivityLogUserId, ActivityLogId, UserId, ReadAt |
| `MAM.ArchiveExtractionJob` | RowVersion, ArchiveExtractionJobId, ArchiveAssetSetIdentifier, OutputCategoryIdentifier, WorkspaceIdentifier, TaskIdentifier, ArchiveExtractionMode, DeleteAssetAfterExtraction, Status, BackgroundJobId, CreatedAt, CreatedBy, InitiatedAt, FinishedAt |
| `MAM.ArchiveExtractionJobEntry` | ArchiveExtractionJobEntryId, ArchiveExtractionJobId, EntryType, Status, Path, Name, CreatedEntityIdentifier, CreatedEntityTypeIdentifier |
| `MAM.AssetDerivation` | Identifier, RowVersion, MutDate, MutUser, AssetDerivationId, AssetVersionId, DerivationTypeId, DataId, FileTypeId, FileSize, FileHash, Status, DerivationFeedback |
| `MAM.AssetDerivationJob` | Identifier, RowVersion, MutDate, MutUser, AssetDerivationJobId, BackgroundJobId, CreatedAt, CreatedBy, FinishedAt, InitiatedAt, Status |
| `MAM.AssetDerivationJobReport` | AssetDerivationJobReportId, AssetDerivationJobId, AssetSetIdentifier, AssetVersionIdentifier, AssetDerivationIdentifier, AssetTypeId, DerivationTypeId, FileTypeId, FileSize, FileName, Status |
| `MAM.AssetSet` | MutDate, MutUser, RowVersion, AssetSetId, AssetSetNodeId, Created, AssetSetStatus |
| `MAM.AssetSetDuplicate` | MutUser, MutDate, RowVersion, AssetSetDuplicateId, AssetSetIdentifier, CreatedAt, FileName, ImportedBy, DuplicateAssetSetIdentifier, DuplicateCreatedAt, DuplicateFileName, DuplicateImportedBy |
| `MAM.AssetType` | Identifier, MutDate, MutUser, RowVersion, AssetTypeId, DownloadOriginalAlways, PropertyListValueIdentifier |
| `MAM.AssetTypeDerivationType` | MutDate, MutUser, RowVersion, AssetTypeId, DerivationTypeId, IsDefaultDerivation, AllowManualUpload, IsOnDemand, ShowInDownloads, UseAsThumbnail, UseAsPreview |
| `MAM.AssetVersion` | MutDate, MutUser, RowVersion, AssetVersionId, AssetVersionNodeId, Created, AssetVersionStatus, ProcessingStatus, OriginalFilename, OriginalName, OriginalFileEnding, Version, AssetTypeId |
| `MAM.BackgroundJob` | Identifier, RowVersion, MutDate, MutUser, BackgroundJobId, Type, HangfireJobId |
| `MAM.BillingContact` | BillingContactId, NodeId, Name, EmailAddress, Status, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, RowVersion, MutDate, MutUser |
| `MAM.Buyout` | BuyoutId, Identifier, RightsOwnerId, PriceListId, LicenseId, CurrencyId, Description, Basis, AdditionalFeePercentage, FromDate, ToDate, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, RowVersion |
| `MAM.Category` | TranslationIdentifier, RowVersion, MutDate, MutUser, CategoryId, CategoryNodeId, SequenceNr |
| `MAM.Comment` | MutUser, MutDate, RowVersion, CommentId, NodeId, Text, CreatedBy, CreatedAt |
| `MAM.Currency` | CurrencyId, Identifier, ShortName, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, RowVersion |
| `MAM.CustomNode` | CustomNodeId, NodeId, TranslationIdentifier, MutUser, MutDate, RowVersion |
| `MAM.DerivationJobDefinition` | Identifier, MutDate, MutUser, RowVersion, DerivationJobDefinitionId, InputDerivationDataType, JobDefinition, OutputDerivationDataType, DerivationTypeId, OutputFileTypeId, IsSystem |
| `MAM.DerivationJobDefinitionInputFileType` | Identifier, DerivationJobDefinitionInputFileTypeId, DerivationJobDefinitionId, FileTypeId |
| `MAM.DerivationTypeView` | Identifier, RowVersion, TranslationIdentifier, DerivationTypeId, IsSystem, IsDeleted |
| `MAM.Edge` | Identifier, RowVersion, MutDate, MutUser, EdgeId, FromNodeId, ToNodeId, EdgeTypeId |
| `MAM.EdgeTypeView` | Identifier, IsDeleted, TranslationIdentifier, Status, RowVersion, EdgeTypeId, EdgeSemantic, IsDirected, IsSystem, IsUsedForAssetSearch |
| `MAM.EdgeTypeAllowedNodeTypes` | EdgeTypeAllowedNodeTypeId, EdgeTypeId, FromNodeTypeId, ToNodeTypeId |
| `MAM.FileNamingDefinition` | Identifier, MutDate, MutUser, RowVersion, TranslationIdentifier, FileNamingDefinitionId, Format, CharacterCasing, ReplaceIllegalCharacters, AbsentPropertyStrategy, CreatedBy |
| `MAM.FileNamingDefinitionProperty` | FileNamingDefinitionPropertyId, FileNamingDefinitionId, PropertyDefinitionId |
| `MAM.FileNamingDefinitionTenant` | FileNamingDefinitionTenantId, FileNamingDefinitionId, TenantId, IsDefault |
| `MAM.FileReference` | Identifier, RowVersion, FileReferenceId, DataIdentifier, FileName, FileUsageType |
| `MAM.FileType` | Identifier, MutDate, MutUser, RowVersion, FileTypeId, AssetTypeId, FileEnding, MimeType, QualityChecksActive, EnableBinaryDocumentExtraction |
| `MAM.LanguageView` | Identifier, MutDate, MutUser, RowVersion, LanguageId, Status, IsDefaultLanguage, TranslationIdentifier, IsDeleted |
| `MAM.License` | LicenseId, NodeId, TranslationIdentifier, Status, PeriodStart, PeriodEnd, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, RowVersion, MutDate, MutUser |
| `MAM.NodeView` | Identifier, IsDeleted, MutDate, NodeId, NodeTypeId, SequenceNr, IsTransparent, TenantId, CreateUser, CreateDate, InterruptInheritance |
| `MAM.NodeActivityLogSettings` | NodeActivityLogSettingsId, NodeId, UserId, SendToInbox, SendEmail, RowVersion |
| `MAM.NodeTypeView` | Identifier, RowVersion, NodeTypeId, TranslationIdentifier, Status, NodeSemantic, IsSystem, IsDeleted |
| `MAM.OutgoingEmail` | MutDate, MutUser, OutgoingEmailId, TemplateIdentifier, CreatedAt |
| `MAM.OutgoingEmailRecipientUser` | OutgoingEmailId, UserIdentifier |
| `MAM.OutgoingEmailRecipientAddress` | OutgoingEmailId, EmailAddress |
| `MAM.OutgoingEmailTemplateParameter` | OutgoingEmailId, Name, Value |
| `MAM.OutgoingMessageQueueEvent` | Identifier, MutDate, MutUser, OutgoingMessageQueueEventId, TransactionIdentifier, Payload, TypeName, CreatedAt, Sent, PriorityForced, PrioritySelector, EventAction, EventActionDetails, ObjectIdentifier, ObjectType, PropertyDefinitionIdentifier, UserIdentifier |
| `MAM.PriceList` | PriceListId, Identifier, Name, Description, Status, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, RowVersion |
| `MAM.PriceListUserContext` | PriceListUserContextId, PriceListId, UserContextId, UserContextGroupId, Percentage, CreatedAt, CreatedBy, RowVersion |
| `MAM.PropertyView` | Identifier, IsDeleted, RowVersion, MutDate, MutUser, PropertyId, NodeId, EdgeId, PropertyDefinitionId |
| `MAM.PropertyValueView` | Identifier, IsDeleted, RowVersion, MutDate, MutUser, PropertyValueId, PropertyId, PropertyListValueId, UserContextId, TranslationIdentifier, ValueBool, ValueInt, ValueDecimal, ValueDate, ValueDateTime, ValueDateTimeDurationTicks, ValueText |
| `MAM.PropertyDefinitionView` | Identifier, Status, IsDeleted, TranslationIdentifier, MutDate, MutUser, RowVersion, PropertyDefinitionId, PropertyGroupId, Editable, IgnoreCopying, IsSystemProperty, IsLanguageDependant, ValueType, HasListValues, AllowCustomListValue, AllowMultipleValues, SearchOption, IsSearchableFacet, IsFileNamingUnlocked, HasUserContext, DefinitionType, SortKey, BlacklistedFields |
| `MAM.PropertyDefinitionSystemPropertyStrategy` | MutDate, MutUser, RowVersion, PropertyDefinitionSystemPropertyStrategyId, PropertyDefinitionId, NodeTypeId, SystemPropertyStrategy |
| `MAM.PropertyDefinitionNodeType` | NodeTypeId, PropertyDefinitionId |
| `MAM.PropertyExtractionAssetDerivation` | PropertyExtractionAssetDerivationId, PropertyExtractionJobId, AssetSetIdentifier, AssetVersionIdentifier, AssetVersionFileName, AssetVersionNumber, AssetDerivationIdentifier, Status |
| `MAM.PropertyExtractionResultSource` | PropertyExtractionResultSourceId, PropertyExtractionAssetDerivationId, Status, StatusMessage, MetadataProvider |
| `MAM.PropertyExtractionResult` | PropertyExtractionResultId, PropertyExtractionResultSourceId, ExtractedValue, Confidence, Status, ToPropertyDefinitionIdentifier, ToPropertyListValueIdentifier |
| `MAM.PropertyExtractionDefinition` | PropertyExtractionDefinitionId, CreatedAt, RowVersion, Identifier, IsSystem, ExtractionType, InputDerivationTypeId, Status |
| `MAM.PropertyExtractionDefinitionAssetType` | PropertyExtractionDefinitionAssetTypeId, PropertyExtractionDefinitionId, AssetTypeId |
| `MAM.PropertyExtractionMapping` | PropertyExtractionMappingId, PropertyExtractionSourceId, FromValue, ToPropertyDefinitionId, ToPropertyListValueId |
| `MAM.PropertyExtractionJob` | PropertyExtractionJobId, Identifier, RowVersion, PropertyExtractionDefinitionId, ImportAfterExtraction, Status, BackgroundJobId, CreatedAt, CreatedBy, InitiatedAt, FinishedAt |
| `MAM.PropertyExtractionSource` | PropertyExtractionSourceId, PropertyExtractionDefinitionId, MetadataProvider, ConfidenceThreshold, Language, CustomModelId |
| `MAM.PropertyGroupView` | IsDeleted, Identifier, Status, RowVersion, TranslationIdentifier, PropertyGroupId, SequenceNr |
| `MAM.PropertyListValueView` | Identifier, IsDeleted, TranslationIdentifier, RowVersion, MutUser, MutDate, PropertyListValueId, PropertyDefinitionId, SortKey, ValueBool, ValueInt, ValueDecimal, ValueDate, ValueDateTime, ValueDateTimeDurationTicks, ValueText, Status, IsSystemListValue |
| `MAM.PropertyImport` | Identifier, MutUser, MutDate, RowVersion, PropertyImportId, FileName, DataId, MimeType, Status, BackgroundJobId, CreatedAt, CreatedBy, InitiatedAt, FinishedAt, TotalPropertyMutationCount, FailedPropertyMutationCount, SucceededPropertyMutationCount, SkippedPropertyMutationCount |
| `MAM.PropertyImportEntity` | MutUser, MutDate, RowVersion, PropertyImportEntityId, EntityIdentifier, NodeTypeId, PropertyImportId, VersionFileName, VersionIdentifier, VersionNumber |
| `MAM.PropertyImportMutation` | MutUser, MutDate, RowVersion, PropertyImportMutationId, PropertyImportEntityId, PropertyImportId, PropertyDefinitionIdentifier, Success, Result |
| `MAM.PropertyImportMessage` | MutUser, MutDate, RowVersion, PropertyImportMessageId, PropertyImportId, MessageType, Severity, Result, RowNumber, ColumnNumber |
| `MAM.PropertyMassEdit` | Identifier, RowVersion, PropertyMassEditId, Status, BackgroundJobId, CreatedAt, CreatedBy, InitiatedAt, FinishedAt, TotalPropertyMutationCount, FailedPropertyMutationCount, SucceededPropertyMutationCount, TotalAssetCount, ApplicableAssetCountLimit |
| `MAM.PropertyMassEditAsset` | RowVersion, PropertyMassEditAssetId, PropertyMassEditId, AssetIdentifier, AssetVersionIdentifier, AssetVersionFileName, AssetVersionNumber |
| `MAM.PropertyMassEditMutation` | RowVersion, PropertyMassEditMutationId, PropertyMassEditId, PropertyDefinitionIdentifier, PropertyListItemIdentifier, DataLanguageIdentifier, Value, UserContextIdentifier, PropertyStrategy, PropertyValueStrategy, TranslationStrategy |
| `MAM.PropertyMassEditResult` | RowVersion, PropertyMassEditMutationId, PropertyMassEditAssetId, Result, PropertyIdentifier |
| `MAM.PropertyNodeTypeInheritance` | RowVersion, PropertyNodeTypeInheritanceId, PropertyId, NodeTypeId |
| `MAM.PropertySet` | Identifier, MutDate, RowVersion, Name, IsGlobal, IsDefault, NodeTypeIdentifier, CreatedBy, PropertySetId |
| `MAM.PropertySetCondition` | Identifier, PropertySetConditionId, PropertySetId, PropertyDefinitionId, PropertyListValueId |
| `MAM.PropertySetPropertyDefinitions` | Identifier, PropertySetPropertyDefinitionsId, PropertySetId, PropertyDefinitionId, FillMode |
| `MAM.Purchase` | PurchaseId, OrderId, NodeId, LicenseId, UserContextId, UserContextGroupId, UserId, UserIdentifier, StartDate, ExpiryDate, CreatedAt, RowVersion |
| `MAM.PurchasePrice` | PurchasePriceId, PurchaseId, Price, CurrencyId |
| `MAM.RightsOwner` | RightsOwnerId, NodeId, Name, AvatarFileReferenceId, BillingContactId, Status, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, RowVersion, MutDate, MutUser |
| `MAM.SavedSearch` | Identifier, MutUser, MutDate, RowVersion, SavedSearchId, Name, SearchQuery, CreatedBy |
| `MAM.SearchReIndex` | RowVersion, Identifier, SearchReIndexId, EntityTypeIdentifiers, InitiatedBy, InitiatedAt, FinishedAt, Status, FailureReason, TotalEntitiesCount, SuccessfulEntitiesCount, FailedEntitiesCount, IsCancelled |
| `MAM.SearchReIndexFailedEntities` | SearchReIndexFailedEntitiesId, SearchReIndexId, NodeIdentifier, NodeTypeIdentifier, DisplayName, BatchNumber |
| `MAM.ShareView` | Identifier, IsDeleted, MutUser, MutDate, RowVersion, ShareId, WorkspaceNodeId, ValidUntil, Name |
| `MAM.ShareRecipientView` | MutUser, MutDate, RowVersion, IsDeleted, ShareRecipientId, ShareId, EmailAddress, Token |
| `MAM.ShareAssetTypeDerivationType` | MutUser, MutDate, RowVersion, ShareId, AssetTypeId, DerivationTypeId |
| `MAM.State` | MutUser, MutDate, RowVersion, StateId, StateNodeId, SequenceNr, TranslationIdentifier, IsDefault |
| `MAM.Task` | RowVersion, TaskId, NodeId, Name, Description, DueDate, Status, CreatedBy, CreatedAt, TaskTypeId, AssignedUserId, SequenceNr |
| `MAM.TaskType` | Identifier, TaskTypeId |
| `MAM.Translation` | Identifier, MutDate, MutUser, RowVersion, TranslationId, ObjectType, TranslationIdentifier, FieldName, LanguageId, TranslatedValue |
| `MAM.UploadSession` | MutDate, UploadSessionId, SessionIdentifier, UploadType, AssetSetNodeId, AssetVersionNodeId, FileName, UploadEntityNodeId, TaskNodeId, WorkspaceNodeId, DerivationTypeId, ArchiveExtractionMode, DeleteAssetAfterExtraction |
| `MAM.UploadSessionChunk` | MutDate, UploadSessionChunkId, UploadSessionId, ChunkIdentifier |
| `MAM.UserContextView` | Identifier, MutUser, MutDate, RowVersion, UserContextId, TranslationIdentifier, SequenceNr, IsDefault, IsDeleted |
| `MAM.UserContextGroup` | Identifier, UserContextGroupId, TranslationIdentifier, CreatedAt, RowVersion |
| `MAM.UserContextGroupUserContext` | UserContextGroupUserContextId, UserContextGroupId, UserContextId |
| `MAM.WatermarkDefinition` | Identifier, MutUser, MutDate, RowVersion, WatermarkDefinitionId, DisplayInUI, DerivationJobDefinitionId, PropertyDefinitionId |
| `MAM.WatermarkTrigger` | Identifier, MutUser, MutDate, RowVersion, WatermarkTriggerId, WatermarkDefinitionId, PropertyListValueId, ValueBool, FileReferenceId |
| `MAM.Workspace` | TranslationIdentifier, Status, MutUser, MutDate, RowVersion, WorkspaceId, CreatedBy, ReminderSentAt, WorkspaceNodeId, IsTemplate, PropertySetIdentifier, FinalizationCategoryId, CopyPropertiesToAssets, WorkspaceType, CreatedAt, SequenceNr, AvatarFileReferenceId |

Anzahl Tabellen/Views: 124

---

## Anhang B – Enums (Core/Common; DTO-Spiegelungen weggelassen)

| Enum (Datei) | Werte |
|---|---|
| `FileNamingTokenType` (`Contracts/Services/FileNaming/FileNamingTokenType.cs`) | Unknown = 0, Property = 1, Variable = 2 |
| `SystemPropertyStrategy` (`Contracts/Services/Metadata/SystemPropertyStrategy.cs`) | Unknown = 0, MirrorFileNamePropertyToAssetVersionEntity = 1, MirrorWorkspaceNamePropertyToWorkspaceEntity = 2, MirrorCategoryNamePropertyToCategoryEntity = 3, MirrorAssetStatusPropertyToAssetSetEntity = 4, MirrorCustomEntityNamePropertyToCustomEntity = 5, MirrorWorkspaceTypePropertyToWorkspaceEntity = 6, MirrorTaskPropertiesToTaskEntity = 7, MirrorBillingContactPropertiesToBillingContact = 8, MirrorRightsOwnerPropertiesToRightsOwner = 9, MirrorLicensePropertiesToLicense = 10 |
| `UnitOfWorkScope` (`Contracts/UnitOfWork/UnitOfWorkScope.cs`) | JoinExisting = 0, ForceCreateNew |
| `AuthorizableAction` (`Models/BusinessObjects/AuthorizableAction.cs`) | Read = 0, Create = 1, Update = 2, Delete = 3, View = 4, Any = 5 |
| `JobStatus` (`Models/BusinessObjects/JobStatus.cs`) | Unknown = 0, EnqueuedForExecution = 1, Executing = 2, Executed = 3, Failed = 4 |
| `PropertyActionStrategy` (`Models/BusinessObjects/PropertyActionStrategy.cs`) | Unknown = 0, KeepExisting = 1, OverwriteExisting = 2, Abort = 3, DeleteExisting = 4, Merge = 5 |
| `PropertyMutationResult` (`Models/BusinessObjects/PropertyMutationResult.cs`) | Unknown = 0, Created = 1, AppliedKeepExisting = 2, AppliedOverwriteExisting = 3, MissingAsset = 4, MissingPropertyDefinition = 5, Failed = 6, NotAllowed = 7, AppliedDeleteExisting = 8, IgnoredDelete = 9, NotEditable = 10, AppliedMerge = 11, IgnoredMerge = 12 |
| `ActivityLogChangeType` (`Models/BusinessObjects/ActivityLogs/ActivityLogChangeType.cs`) | AssigneeSet = 0, AssigneeUpdated = 1, AssigneeCleared = 2, CommentCreated = 3, CommentUpdated = 4, CommentDeleted = 5, PropertyValueCreated = 6, PropertyValueUpdated = 7, PropertyValueDeleted = 8, RelationCreated = 9, RelationDeleted = 10, ResourcePermissionAdded = 11, ResourcePermissionUpdated = 12, ResourcePermissionRemoved = 13, StateUpdated = 14, ShareCreated = 15, ShareUpdated = 16, ShareDeleted = 17, ConfigurationValueCreated = 18, ConfigurationValueUpdated = 19, ConfigurationValueDeleted = 20 |
| `ActivityLogType` (`Models/BusinessObjects/ActivityLogs/ActivityLogType.cs`) | AssetCreated = 0, AssetUpdated = 1, AssetDeleted = 2, CategoryCreated = 3, CategoryUpdated = 4, CategoryDeleted = 5, TaskCreated = 6, TaskUpdated = 7, TaskDeleted = 8, WorkspaceCreated = 9, WorkspaceUpdated = 10, WorkspaceDeleted = 11, JobUpdated = 12, ApplicationSettingsUpdated = 13 |
| `ConfigurationProvider` (`Models/BusinessObjects/ApplicationSettings/ConfigurationProvider.cs`) | Unknown = 0, AppSettingsYaml = 1, AppSettingsOverridesYaml = 2, Yaml = 3, EnvironmentVariables = 4, CommandLine = 5, Database = 6 |
| `ArchiveEntryStatus` (`Models/BusinessObjects/Archive/ArchiveEntryStatus.cs`) | Unknown = 0, Extracted = 1, Filtered = 2, Error = 3 |
| `ArchiveEntryType` (`Models/BusinessObjects/Archive/ArchiveEntryType.cs`) | Unknown = 0, Folder = 1, File = 2 |
| `ArchiveExtractionMode` (`Models/BusinessObjects/Archive/ArchiveExtractionMode.cs`) | ExtractWithFullPath = 0, ExtractFilesOnly = 1 |
| `AssetProcessingStatus` (`Models/BusinessObjects/Asset/AssetProcessingStatus.cs`) | Pending = 0, Started = 1, Finished = 2, Failed = 3 |
| `AssetSetStatus` (`Models/BusinessObjects/Asset/AssetSetStatus.cs`) | Importing = 0, Imported = 1, Duplicate = 2 |
| `AssetVersionStatus` (`Models/BusinessObjects/Asset/AssetVersionStatus.cs`) | WorkInProgress = 0, Unpublished = 1, Published = 2 |
| `Permission` (`Models/BusinessObjects/Authorization/Permission.cs`) | Unknown = 0, AdminView = 1, AssetBrowserView = 2, AssetDerivationCreate = 3, AssetDerivationDelete = 4, AssetDerivationRead = 5, AssetDerivationUpdate = 6, AssetDerivationView = 7, AssetDerivationJobCreate = 8, AssetDerivationJobDelete = 9, AssetDerivationJobRead = 10, AssetDerivationJobUpdate = 11, AssetDerivationJobView = 12, AssetDetailView = 13, AssetDownloadRead = 14, AssetPropertyCreate = 15, AssetPropertyDelete = 16, AssetPropertyRead = 17, AssetPropertyUpdate = 18, AssetPropertyView = 19, AssetPropertyMassEditCreate = 20, AssetPropertyMassEditDelete = 21, AssetPropertyMassEditRead = 22, AssetPropertyMassEditUpdate = 23, AssetPropertyMassEditView = 24, AssetPropertySetCreate = 25, AssetPropertySetDelete = 26, AssetPropertySetGlobalCreate = 27, AssetPropertySetGlobalDelete = 28, AssetPropertySetGlobalRead = 29, AssetPropertySetGlobalUpdate = 30, AssetPropertySetGlobalView = 31, AssetPropertySetRead = 32, AssetPropertySetUpdate = 33, AssetPropertySetView = 34, RelationCreate = 35, RelationDelete = 36, RelationRead = 37, RelationUpdate = 38, RelationView = 39, AssetSetCreate = 40, AssetSetDelete = 41, AssetSetRead = 42, AssetSetUpdate = 43, AssetSetView = 44, AssetSetDuplicateRead = 45, CategoryCreate = 46, CategoryDelete = 47, CategoryRead = 48, CategoryUpdate = 49, CategoryView = 50, CategoryPropertyCreate = 51, CategoryPropertyDelete = 52, CategoryPropertyRead = 53, CategoryPropertyUpdate = 54, CategoryPropertyView = 55, CategoryPropertyInheritanceCreate = 56, CategoryPropertyInheritanceDelete = 57, CategoryPropertyInheritanceRead = 58, CategoryPropertyInheritanceUpdate = 59, CategoryPropertySetCreate = 60, CategoryPropertySetDelete = 61, CategoryPropertySetGlobalCreate = 62, CategoryPropertySetGlobalDelete = 63, CategoryPropertySetGlobalRead = 64, CategoryPropertySetGlobalUpdate = 65, CategoryPropertySetGlobalView = 66, CategoryPropertySetRead = 67, CategoryPropertySetUpdate = 68, CategoryPropertySetView = 69, CommentCreate = 70, CommentDelete = 71, CommentRead = 72, CommentUpdate = 73, CommentView = 74, DashboardView = 75, DataLanguageCreate = 76, DataLanguageDelete = 77, DataLanguageRead = 78, DataLanguageUpdate = 79, DataLanguageView = 80, DerivationJobDefinitionCreate = 81, DerivationJobDefinitionDelete = 82, DerivationJobDefinitionRead = 83, DerivationJobDefinitionUpdate = 84, DerivationJobDefinitionView = 85, DerivationTypeCreate = 86, DerivationTypeDelete = 87, DerivationTypeRead = 88, DerivationTypeUpdate = 89, DownloadBasketView = 90, EdgeTypeCreate = 91, EdgeTypeDelete = 92, EdgeTypeRead = 93, EdgeTypeUpdate = 94, EdgeTypeView = 95, FileNamingDefinitionCreate = 96, FileNamingDefinitionDelete = 97, FileNamingDefinitionRead = 98, FileNamingDefinitionUpdate = 99, FileNamingDefinitionView = 100, GlobalAdmin = 101, NodePermissionCreate = 102, NodePermissionDelete = 103, NodePermissionRead = 104, NodePermissionUpdate = 105, NodePermissionView = 106, PropertyDefinitionCreate = 107, PropertyDefinitionDelete = 108, PropertyDefinitionRead = 109, PropertyDefinitionUpdate = 110, PropertyDefinitionView = 111, PropertyGroupCreate = 112, PropertyGroupDelete = 113, PropertyGroupRead = 114, PropertyGroupUpdate = 115, PropertyGroupView = 116, PropertyImportCreate = 117, PropertyImportDelete = 118, PropertyImportRead = 119, PropertyImportUpdate = 120, PropertyImportView = 121, RecycleBinDelete = 122, RecycleBinRead = 123, RecycleBinView = 124, ReIndexingJobCreate = 125, ReIndexingJobRead = 126, ReIndexingJobView = 127, RoleCreate = 128, RoleDelete = 129, RoleRead = 130, RoleUpdate = 131, RoleView = 132, SavedSearchCreate = 133, SavedSearchDelete = 134, SavedSearchRead = 135, SavedSearchUpdate = 136, SavedSearchView = 137, ShareCreate = 138, ShareDelete = 139, ShareRead = 140, ShareUpdate = 141, ShareView = 142, ShareFileNamingDefinitionCreate = 143, ShareFileNamingDefinitionDelete = 144, ShareFileNamingDefinitionRead = 145, ShareFileNamingDefinitionUpdate = 146, ShareFileNamingDefinitionView = 147, TaskCreate = 148, TaskDelete = 149, TaskRead = 150, TaskUpdate = 151, TaskView = 152, TaskPropertyCreate = 153, TaskPropertyDelete = 154, TaskPropertyRead = 155, TaskPropertyUpdate = 156, TaskPropertyView = 157, TaskPropertySetCreate = 158, TaskPropertySetDelete = 159, TaskPropertySetGlobalCreate = 160, TaskPropertySetGlobalDelete = 161, TaskPropertySetGlobalRead = 162, TaskPropertySetGlobalUpdate = 163, TaskPropertySetGlobalView = 164, TaskPropertySetRead = 165, TaskPropertySetUpdate = 166, TaskPropertySetView = 167, TenantCreate = 168, TenantDelete = 169, TenantRead = 170, TenantUpdate = 171, TenantView = 172, TenantFileNamingDefinitionCreate = 173, TenantFileNamingDefinitionDelete = 174, TenantFileNamingDefinitionRead = 175, TenantFileNamingDefinitionUpdate = 176, TenantFileNamingDefinitionView = 177, UploadView = 178, UserCreate = 179, UserDelete = 180, UserRead = 181, UserUpdate = 182, UserView = 183, UserContextCreate = 184, UserContextDelete = 185, UserContextUpdate = 186, UserGroupCreate = 187, UserGroupDelete = 188, UserGroupRead = 189, UserGroupUpdate = 190, UserGroupView = 191, VersionCreate = 192, VersionDelete = 193, VersionRead = 194, VersionUpdate = 195, VersionView = 196, VersionPropertySetCreate = 197, VersionPropertySetDelete = 198, VersionPropertySetGlobalCreate = 199, VersionPropertySetGlobalDelete = 200, VersionPropertySetGlobalRead = 201, VersionPropertySetGlobalUpdate = 202, VersionPropertySetGlobalView = 203, VersionPropertySetRead = 204, VersionPropertySetUpdate = 205, VersionPropertySetView = 206, WatermarkCreate = 207, WatermarkDelete = 208, WatermarkRead = 209, WatermarkUpdate = 210, WatermarkView = 211, WorkspaceCreate = 212, WorkspaceDelete = 213, WorkspaceRead = 214, WorkspaceUpdate = 215, WorkspaceView = 216, WorkspacePropertyCreate = 217, WorkspacePropertyDelete = 218, WorkspacePropertyRead = 219, WorkspacePropertyUpdate = 220, WorkspacePropertyView = 221, WorkspacePropertySetCreate = 222, WorkspacePropertySetDelete = 223, WorkspacePropertySetGlobalCreate = 224, WorkspacePropertySetGlobalDelete = 225, WorkspacePropertySetGlobalRead = 226, WorkspacePropertySetGlobalUpdate = 227, WorkspacePropertySetGlobalView = 228, WorkspacePropertySetRead = 229, WorkspacePropertySetUpdate = 230, WorkspacePropertySetView = 231, WorkspaceTemplateCreate = 232, WorkspaceTemplateDelete = 233, WorkspaceTemplateRead = 234, WorkspaceTemplateUpdate = 235, WorkspaceTemplateView = 236, CropDerivationCreate = 237, EntityCreate = 238, EntityDelete = 239, EntityRead = 240, EntityUpdate = 241, EntityView = 242, CustomEntityCreate = 243, CustomEntityDelete = 244, CustomEntityRead = 245, CustomEntityUpdate = 246, CustomEntityView = 247, CustomEntityPropertyCreate = 248, CustomEntityPropertyDelete = 249, CustomEntityPropertyRead = 250, CustomEntityPropertyUpdate = 251, CustomEntityPropertyView = 252, ApplicationSettingsCreate = 253, ApplicationSettingsDelete = 254, ApplicationSettingsRead = 255, ApplicationSettingsUpdate = 256, ApplicationSettingsView = 257, RuleCreate = 258, RuleDelete = 259, RuleRead = 260, RuleUpdate = 261, RuleView = 262, RuleReApplyJobCreate = 263, CustomEntityBrowserView = 264, CustomEntityPropertySetCreate = 265, CustomEntityPropertySetDelete = 266, CustomEntityPropertySetRead = 267, CustomEntityPropertySetUpdate = 268, CustomEntityPropertySetView = 269, CustomEntityPropertySetGlobalCreate = 270, CustomEntityPropertySetGlobalDelete = 271, CustomEntityPropertySetGlobalRead = 272, CustomEntityPropertySetGlobalUpdate = 273, CustomEntityPropertySetGlobalView = 274, ArchiveExtractionJob = 275, ReIndexingJobDelete = 276, StateCreate = 277, StateDelete = 278, StateRead = 279, StateUpdate = 280, StateView = 281, CollectionView = 282, WorkspaceFinalize = 283, RuleJobReportRead = 284, LicenseView = 285, LicenseRead = 286, LicenseCreate = 287, LicenseUpdate = 288, LicenseDelete = 289, WorkspaceActivityLogRead = 290, WorkspaceActivityLogView = 291, TaskActivityLogRead = 292, TaskActivityLogView = 293, AssetActivityLogRead = 294, AssetActivityLogView = 295, LicenseOfferView = 296, LicenseOfferRead = 297, PropertyExtractionDefinitionCreate = 298, PropertyExtractionDefinitionDelete = 299, PropertyExtractionDefinitionRead = 300, PropertyExtractionDefinitionUpdate = 301, PropertyExtractionDefinitionView = 302, LicensePurchaseCreate = 303, LicensePurchaseRead = 304, PropertyExtractionJob = 305, PermissionMaterializationJobRead = 306, PermissionMaterializationJobCreate = 307, PermissionMaterializationJobView = 308, CategoryDownloadRead = 309, ApplicationSettingsActivityLogRead = 310, ApplicationSettingsActivityLogView = 311 |
| `BackgroundJobState` (`Models/BusinessObjects/BackgroundJob/BackgroundJobState.cs`) | Unknown = 0, Awaiting, Deleted, Enqueued, Failed, Processing, Scheduled, Succeeded |
| `BackgroundJobTriggerType` (`Models/BusinessObjects/BackgroundJob/BackgroundJobTriggerType.cs`) | Unknown = 0, EntityCreated = 1, EntityDeleted = 2, FullPermissionUpdate = 3, PermissionCreated = 4 |
| `BackgroundJobType` (`Models/BusinessObjects/BackgroundJob/BackgroundJobType.cs`) | Unknown = 0, PropertyMassEdit = 1, ArchiveExtraction = 2, AssetDerivation = 3, RecycleBinCleanup = 4, Import = 5, PermissionMaterialization = 6, OutgoingMessageQueueEventCleanup = 7, UploadSessionCleanup = 8, EmailQueue = 10, ExecuteRulesJob = 11, RulesJobCleanup = 12, ActivityLogCleanup = 13, AssetCleanup = 14, UserAccountManagement = 15, WorkspacesJob = 16, PropertyExtraction = 17 |
| `CommentSortBy` (`Models/BusinessObjects/Comment/CommentSortBy.cs`) | CreatedAt |
| `ContactPrintFormat` (`Models/BusinessObjects/ContactPrint/ContactPrintFormat.cs`) | Unknown = 0, Portrait1x1 = 1, Portrait1x2 = 2, Portrait2x3 = 3, Portrait3x5 = 4, Landscape1x1 = 5, Landscape2x1 = 6, Landscape3x2 = 7, Landscape5x3 = 8 |
| `ContactPrintPageOrientation` (`Models/BusinessObjects/ContactPrint/ContactPrintPageOrientation.cs`) | Portrait = 0, Landscape = 1 |
| `AssetDerivationStatus` (`Models/BusinessObjects/Derivation/AssetDerivationStatus.cs`) | Pending = 0, Processing = 1, Finished = 2, Failed = 3 |
| `DerivationDataType` (`Models/BusinessObjects/Derivation/DerivationDataType.cs`) | Image = 0, Movie = 1, PrintDocument = 2, OfficeDocument = 3, Custom = 4 |
| `ImageType` (`Models/BusinessObjects/Derivation/ImageDerivationJobDefinition.cs`) | Jpeg = 0, Bmp = 1, Png = 2, Tiff = 3, Gif = 4 |
| `ColorDefinitionType` (`Models/BusinessObjects/Derivation/ImageDerivationJobDefinition.cs`) | RgbA, CmykA |
| `RotateDirection` (`Models/BusinessObjects/Derivation/ImageDerivationJobDefinition.cs`) | CounterClockWise, ClockWise |
| `ScaleType` (`Models/BusinessObjects/Derivation/ImageDerivationJobDefinition.cs`) | Absolute, Relative |
| `XAxisAnchor` (`Models/BusinessObjects/Derivation/ImageDerivationJobDefinition.cs`) | Left, Center, Right |
| `YAxisAnchor` (`Models/BusinessObjects/Derivation/ImageDerivationJobDefinition.cs`) | Top, Center, Bottom |
| `AbsentPropertyStrategy` (`Models/BusinessObjects/FileNaming/AbsentPropertyStrategy.cs`) | Unknown = 0, PropertyDefinitionIdentifier = 1, Skip = 2, Abort = 3 |
| `CharacterCasing` (`Models/BusinessObjects/FileNaming/CharacterCasing.cs`) | Normal = 0, Upper = 1, Lower = 2 |
| `FileUsageType` (`Models/BusinessObjects/FileReference/FileUsageType.cs`) | Undefined = 0, Watermark = 1, Avatar = 2 |
| `ContextGroupType` (`Models/BusinessObjects/LicenseManagement/ContextGroupType.cs`) | Group = 0, UserContext = 1 |
| `PriceListSortBy` (`Models/BusinessObjects/LicenseManagement/PriceListSortBy.cs`) | Identifier = 0, Name = 1, Description = 2, Status = 3, CreatedAt = 4, UpdatedAt = 5 |
| `PropertyDefinitionFields` (`Models/BusinessObjects/Metadata/PropertyDefinitionFields.cs`) | Name = 0, Description, EntityTypes, PropertyGroup, SortKey, DefinitionType, IsSearchableFacet, Editable, SearchOption, IgnoreCopying, Status, IsFileNamingUnlocked, HasUserContext, AllowMultipleValues, AllowCustomListValue, ListItems |
| `PropertyDefinitionListType` (`Models/BusinessObjects/Metadata/PropertyDefinitionListType.cs`) | NoList = 0, List = 1, Multiselect = 2, Singleselect = 3 |
| `PropertyDefinitionSortBy` (`Models/BusinessObjects/Metadata/PropertyDefinitionSortBy.cs`) | Identifier = 0, PropertyGroupIdentifier = 1, ValueType = 2, Editable = 3, IsSystem = 4, IsSearchableFacet = 5, Active = 6, SortKey = 7 |
| `PropertyDefinitionType` (`Models/BusinessObjects/Metadata/PropertyDefinitionType.cs`) | Standard = 0, Url = 1, Html = 2 |
| `PropertyExportFileFormat` (`Models/BusinessObjects/Metadata/PropertyExportFileFormat.cs`) | Unknown = 0, Xls = 1, Xlsx = 2, Csv = 3 |
| `PropertyGroupSortBy` (`Models/BusinessObjects/Metadata/PropertyGroupSortBy.cs`) | PropertyDefinitionsCount = 0, SortKey = 1, Active = 2 |
| `PropertyImportMessageSeverity` (`Models/BusinessObjects/Metadata/PropertyImportMessageSeverity.cs`) | Info = 1, Warning = 2, Error = 3 |
| `PropertyImportMessageType` (`Models/BusinessObjects/Metadata/PropertyImportMessageType.cs`) | Unknown = 0, FileFormatNotSupported = 1, FileFormatError = 2, MetadataTabNotFound = 3, EntityIdentifierNotFound = 4, EnttiyTypeNotFound = 5, PropertyDefinitionIdentifierOrLanguageParseError = 6, NoReadPermissionOnEntity = 7, CouldNotLoadPropertiesForEntity = 8 |
| `PropertyValueType` (`Models/BusinessObjects/Metadata/PropertyValueType.cs`) | String = 0, Text = 1, Date = 2, DateTime = 3, Time = 4, Integer = 5, Float = 6, Decimal = 7, Boolean = 8, TimeSpan = 9 |
| `SearchOption` (`Models/BusinessObjects/Metadata/SearchOption.cs`) | NotSearchable = 0, AdvancedSearchable = 1, FulltextSearchable = 2, AdvancedAndFulltextSearchable = 3 |
| `DeletePropertyResultReason` (`Models/BusinessObjects/Metadata/CQRS/DeletePropertyResultReason.cs`) | Success = 0, NonExistingPropertyDefinition = 1, DeleteSystemPropertyNotAllowed = 2, NotEditable = 3, NonExistingProperty = 4, NotAllowed = 5 |
| `SetPropertyResultReason` (`Models/BusinessObjects/Metadata/CQRS/SetPropertyResultReason.cs`) | Success = 0, CreateOrUpdateRestricted = 1, InheritancePermissionDenied = 2, PropertyDefinitionNotExists = 3, NotEditable = 4, ChangeSystemPropertyNotAllowed = 5, Invalid = 6, CreateOrUpdatePropertyValuesFailed = 7, CreateInEditNotAllowed = 8 |
| `PropertyExtractionJobAssetResultStatus` (`Models/BusinessObjects/Metadata/Extraction/PropertyExtractionJobAssetResultStatus.cs`) | Pending = 0, Extracted = 1, Imported = 2, Error = 3 |
| `PropertyExtractionResultStatus` (`Models/BusinessObjects/Metadata/Extraction/PropertyExtractionResultStatus.cs`) | Extracted = 0, Imported = 1, Skipped = 2, Error = 3 |
| `PropertyExtractionSourceStatus` (`Models/BusinessObjects/Metadata/Extraction/PropertyExtractionSourceStatus.cs`) | Extracted = 0, Imported = 1, Error = 2 |
| `PropertyExtractionType` (`Models/BusinessObjects/Metadata/Extraction/PropertyExtractionType.cs`) | DetectTags = 0 |
| `EdgeTypeSemantic` (`Models/BusinessObjects/ObjectGraph/EdgeTypeSemantic.cs`) | Undefined = 0, , Version = 2, Link = 3, Crop = 4, Translation = 5, Variant = 6, Licence = 7, Duplicate = 8, Transition = 9, DrmLicense = 10 |
| `NodeTypeSemantic` (`Models/BusinessObjects/ObjectGraph/NodeTypeSemantic.cs`) | , Category = 1, AssetSet = 2, Version = 3, Workspace = 4, Task = 5, Comment = 6, State = 7, BillingContact = 8, RightsOwner = 9, License = 10, LicensePurchase = 11,  |
| `FillMode` (`Models/BusinessObjects/PropertySet/FillMode.cs`) | Optional = 0, Recommended = 1, Mandatory = 2 |
| `AssetParentChildRelationDeleteBehavior` (`Models/BusinessObjects/Relation/AssetParentChildRelationDeleteBehavior.cs`) | DeleteAssetSet = 0, PromoteLinkToParentChild = 1 |
| `CreateRelationResultReason` (`Models/BusinessObjects/Relation/CreateRelationResultReason.cs`) | Created = 0, Error = 1, AlreadyExists = 2, MutuallyExclusive = 3, PermissionDenied = 4, Invalid = 5 |
| `TriggerOperator` (`Models/BusinessObjects/RuleEngine/TriggerOperator.cs`) | Equals = 0, NotEquals = 1, GreaterThan = 2, LessThan = 3, GreaterThanOrEquals = 4, LessThanOrEquals = 5, Contains = 6, HasRelationType = 7, IsNewVersion = 8, ValueChanged = 9, IsNewCategory = 10, RegexMatch = 11 |
| `MultiTermOperator` (`Models/BusinessObjects/Search/MultiTermOperator.cs`) | Or = 0, And = 1 |
| `Operator` (`Models/BusinessObjects/Search/Operator.cs`) | HasValue = 0, HasNoValue = 1, Equals = 2, NotEquals = 3, Contains = 4, DoesNotContain = 5, FuzzyEquals = 6, FuzzyContains = 7, GreaterThanOrEquals = 8, LessThanOrEquals = 9, Between = 10, IdentifierEquals = 11, IdentifierNotEquals = 12 |
| `ReIndexEntityType` (`Models/BusinessObjects/Search/ReIndexEntityType.cs`) | Asset = 0 |
| `TaskStatus` (`Models/BusinessObjects/Task/TaskStatus.cs`) | Open = 0, Closed = 1 |
| `TaskUpdateType` (`Models/BusinessObjects/Task/TaskUpdateType.cs`) | ExecuteTransition = 0, AddAssignee = 1, RemoveAssignee = 2 |
| `AssetUploadType` (`Models/BusinessObjects/Upload/AssetUploadType.cs`) | Asset = 0, AssetDerivation = 1, AssetVersion = 2 |
| `UploadType` (`Models/BusinessObjects/Upload/UploadType.cs`) | Unknown = 0, AssetSet = 1, AssetVersion = 2, WorkspaceAssetSet = 3, WorkspaceTaskAssetSet = 4, LatestAssetVersionDerivation = 5, AssetVersionDerivation = 6 |
| `AccessType` (`Models/BusinessObjects/UserManagement/AccessType.cs`) | Permanent = 0, Expiring = 1 |
| `AccountStatus` (`Models/BusinessObjects/UserManagement/AccountStatus.cs`) | Unknown = 0, Active = 1, Inactive = 2, EmailVerificationPending = 10, EmailVerificationExpired = 11, ApprovalPending = 20, ApprovalDenied = 21, Locked = 30, AccountRenewalPending = 31, AccountRenewalExpired = 32 |
| `CodeType` (`Models/BusinessObjects/UserManagement/CodeType.cs`) | Unknown = 0, EmailVerification = 1, PasswordReset = 2, AccountRenewal = 3 |
| `ExternalGroupSortBy` (`Models/BusinessObjects/UserManagement/ExternalGroupSortBy.cs`) | ExternalGroupName = 0, ExternalGroupDisplayName = 1 |
| `UserGroupSortBy` (`Models/BusinessObjects/UserManagement/UserGroupSortBy.cs`) | Name = 0, Created = 1 |
| `UserOrigin` (`Models/BusinessObjects/UserManagement/UserOrigin.cs`) | Unknown = 0, Sharedien = 1, ExternalProvider = 2 |
| `UserSortBy` (`Models/BusinessObjects/UserManagement/UserSortBy.cs`) | FirstName = 0, LastName = 1, Email = 2, UserName = 3 |
| `WorkspaceType` (`Models/BusinessObjects/Workspace/WorkspaceType.cs`) | Upload = 0, Collection = 1, Collaboration = 2 |
| `FileExtensionBehavior` (`Models/Configuration/FileExtensionBehavior.cs`) | Append = 0, Replace = 1 |
| `FileNameFilterScope` (`Models/Configuration/FileNameFilterScope.cs`) | FullPath, FileName |
| `FileNameFilterType` (`Models/Configuration/FileNameFilterType.cs`) | StartsWith, EndsWith, Contains |
| `FileRecognitionMode` (`Models/Configuration/FileRecognitionMode.cs`) | Enrich = 0, Strict = 1 |
| `FileRecognitionScope` (`Models/Configuration/FileRecognitionScope.cs`) | FilesWithoutExtension = 0, AllFiles = 1 |
| `PreSignedUrlType` (`Models/Configuration/PreSignedUrlType.cs`) | StorageHandler, AzureBlobStorage |
| `ExtractionType` (`AI/ExtractionType.cs`) | DetectTags |
| `Priority` (`Core/Contracts/MessageBroker/Priority.cs`) | Lowest = 0, VeryLow = 1, Lower = 2, Low = 3, Default = 4, Middle = 4, High = 5, Higher = 6, VeryHigh = 7, Urgent = 8, Highest = 9 |
| `ErrorType` (`Core/GenericResponse/ErrorType.cs`) | Unknown = 0, ParameterValidation = 1, ModelValidation = 2, NotFound = 3, AlreadyExists = 4, NotAllowed = 5, ConcurrencyConflict = 6, MissingUnitOfWork = 7 |
| `ExternalGroupMembershipType` (`Core/Models/ExternalGroupMembershipType.cs`) | Direct, Transitive |
| `ExternalGroupProvider` (`Core/Models/ExternalGroupProvider.cs`) | MicrosoftGraphApi |
| `HashAlgorithm` (`Core/Models/HashAlgorithm.cs`) | MD5 = 0, SHA256 = 1, SHA384 = 2, SHA512 = 3 |
| `HashFormat` (`Core/Models/HashFormat.cs`) | Base64 = 0, Hex = 1, HexWithoutDashes = 2 |
| `EntityStatus` (`Core/Models/BusinessObjects/EntityStatus.cs`) | Inactive = 0, Active = 1 |
| `ObjectType` (`Core/Models/BusinessObjects/ObjectType.cs`) | AssetSet = 0, Version = 1, AssetDerivation = 2, Category = 3, DerivationJobDefinition = 4, DerivationType = 5, Language = 7, PropertyDefinition = 8, AssetProperty = 9, PropertyValue = 10, PropertyListEntry = 11, Task = 12, PropertyGroup = 13, Tenant = 14, Workspace = 15, RelationType = 16, Comment = 17, AssetRelation = 18, SavedSearch = 19, AssetPropertySet = 20, AssetPropertySetGlobal = 21, WorkspaceTemplate = 23, CategoryProperty = 24, CategoryPropertySet = 25, CategoryPropertySetGlobal = 26, WorkspaceProperty = 27, WorkspacePropertySet = 28, WorkspacePropertySetGlobal = 29, TaskProperty = 30, TaskPropertySet = 31, TaskPropertySetGlobal = 32, CropDerivation = 33, AssetPropertyMassEdit = 34, AssetDerivationJob = 35, Share = 36, RecycleBin = 37, AssetSetDuplicate = 38, Watermark = 39, CategoryPropertyInheritance = 40, ReIndexingJob = 41, PropertyImport = 42, VersionPropertySet = 43, VersionPropertySetGlobal = 44, UserContext = 45, FileNamingDefinition = 46, TenantFileNamingDefinition = 47, ShareFileNamingDefinition = 48, AssetDownload = 49, NodeType = 50, CustomNode = 51, ApplicationSettings = 52, Rule = 53, ArchiveExtractionJob = 54, State = 55, Collection = 56, BillingContact = 57, RightsOwner = 58, License = 59, Relation = 60, PriceList = 61, Buyout = 62, UserContextGroup = 63, LicensePurchase = 64, PropertyExtractionJob = 65, , UserGroupToRoleMap = 1020, ObjectTypePermission = 1030, NodePermission = 1040, DerivationTypeRestriction = 1050, PropertyDefinitionRestriction = 1060, User = 1070, UserGroup = 1080, , AssetBrowser = 2010, DownloadBasket = 2020, Uploads = 2030, Admin = 2040, AssetDetail = 2050, Any = 2060,  |
| `PropertyExtractionProvider` (`Core/Models/BusinessObjects/PropertyExtractionProvider.cs`) | AzureCognitiveServices = 0, Imagga = 1, Clarifai = 2 |
| `SortDirection` (`Core/Models/BusinessObjects/SortDirection.cs`) | Asc = 0, Desc = 1 |
| `AuthenticationTicketStorageType` (`Core/Models/Configuration/Identity/AuthenticationTicketStorageType.cs`) | Default = 0, InMemory = 1 |
| `SignedUrlValidationResult` (`Infrastructure/SignedUrlValidationResult.cs`) | Valid, MissingSignature, SignatureMismatch, Expired, PermissionMismatch |

Anzahl: 92 Enums.
