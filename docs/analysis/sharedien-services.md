# Sharedien – Analyse der Nebendienste (Inventar für Shareme)

Stand: 2026-09-29. Quelle: `C:\dev\privat\github\sharedien` (Version 3.11.x, .NET Core 3.1). Ziel: lückenloses Inventar aller Nebendienste, damit "Shareme" 100 % der Funktionalität abdecken kann. Alles ohne Beleg im Code ist als **unklar** markiert.

Inhalt:
1. Gesamtarchitektur & Deployment
2. Querschnitt: Sharedien.Common (Hosting, Config, Logging, Auth)
3. Processing
4. StorageHandler + Storage-Abstraktion
5. Search (Elasticsearch)
6. EventHub (SignalR)
7. Identity (IdentityServer4)
8. SDK
9. UAA
10. Common.Infrastructure.AI
11. Common.Infrastructure.Mail
12. Common.Infrastructure.Queue
13. Common.Infrastructure.FileTypeChecker
14. Externe Tools & Abhängigkeiten (Gesamtliste)

---

## 1. Gesamtarchitektur & Deployment

### 1.1 Dienste-Landschaft (Stand 3.11.0)

| Dienst | Image / Artefakt | Container-Port | Host-Port (dev) | Windows-Service (HTTP/HTTPS) | Rolle |
|---|---|---|---|---|---|
| Sharedien API (Backend, Hauptdienst) | `sharedien/sharedien-api` / `Sharedien.Api.dll` | 5001 | 5051 | – (Backend-Ordner, nicht Teil dieser Analyse) | Haupt-API, hostet seit 3.7.0 auch das Search-Modul |
| Processing | `sharedien/processing` (+ `-no-deps`) / `Processing.Api.dll` | 5002 | 5052 | "Sharedien3 Processing" 5002 / 4002 | Derivate/Renditionen, Metadaten-Extraktion |
| StorageHandler | `sharedien/storagehandler` / `StorageHandler.Api.dll` | 5000 | 5050 | "Sharedien3 StorageHandler" 5000 / 4000 | Binär-Persistenz, Chunk-Upload, Download |
| Search API | `sharedien/sharedien-search` / `Sharedien.Search.Api.dll` | 9201 | 9251 | – | **Nur bis 3.6.x eigener Dienst**; ab 3.7.0 als Bibliothek in Sharedien API integriert (Update-Notes 3.7.0: "The Search API service can be uninstalled"). `docker/_build/Search/Dockerfile` existiert noch, im Repo gibt es aber kein `Sharedien.Search.Api`-Projekt mehr. |
| EventHub | `sharedien/sharedien-eventhub` / `Sharedien.EventHub.dll` | 5008 | 5058 | "Sharedien3 EventHub" 5008 / 4008 | SignalR-Hub `/hubs/notification` für Echtzeit-Benachrichtigungen (seit 3.8.0, optional) |
| Identity | `sharedien/sharedien-identity` / `Sharedien.Identity.dll` | 5007 | 5057 | "Sharedien3 Identity" 5051 / 4041 (install.bat) | IdentityServer4-basierter OIDC/OAuth2-Provider, SAML-SP, Login-UI |
| Web-Frontend | `sharedien/sharedien-web` (nginx 1.17.1-alpine) / `Frontend.WindowsService.dll` | 80 | 4250 | "Sharedien3 Web" 80 / 443 | Angular-SPA, statisch ausgeliefert |
| UAA | eigenes Image (`src/UAA/docker`) | siehe §9 | – | – | CloudFoundry UAA als SSO-Vorschaltung (siehe §9) |

Infrastruktur (`docker/infrastructure`):

| Komponente | Image | Ports | Hinweis |
|---|---|---|---|
| SQL Server | `mcr.microsoft.com/mssql/server:2017-CU12-ubuntu` (bis 3.10.0), `2019-CU10-ubuntu-16.04` (ab 3.10.1) | 1483→1433 | Collation `Latin1_General_CI_AS`; DBs `Sharedien3`, `Sharedien3StorageHandler`; Hangfire-DB "BackgroundJobs" = gleiche DB |
| Message Broker | `vromero/activemq-artemis:2.9.0-alpine` (AMQP 1.0) | 61666→61616, 8211→8161 (Konsole) | Alternative in Azure: Azure Service Bus (Topics/Subscriptions) |
| Elasticsearch | `6.5.4` (bis 3.9.x), `7.10.2` (ab 3.10.0) | 9250→9200, 9350→9300 | Single Node, `ES_JAVA_OPTS -Xms512m -Xmx512m`; `vm.max_map_count=262144` nötig |
| SMTP-Testserver | `jijiechen/papercut:latest` | 75→25, 37458→37408 | nur Integration-Tests |

Alle Sharedien-Container hängen am externen Docker-Netzwerk `sharedien`. Logs in Volume `sharedien-logs` (`/logs/<dienst>`), Uploads in `sharedien-storagehandler-uploads` (`/uploads`).

### 1.2 Kommunikationswege

| Von → Nach | Protokoll | Zweck |
|---|---|---|
| Frontend → API | HTTPS REST (`/api/v{n}/...`), Bearer-JWT | alles Fachliche |
| Frontend → Identity | OIDC Authorization Code (PKCE, `RequireClientSecret: false`) | Login, Silent Renew (`AllowOfflineAccess`) |
| Frontend → EventHub | SignalR WebSocket `/hubs/notification`, Bearer-JWT | Echtzeit-Notifications |
| Frontend → StorageHandler | HTTPS (Chunk-Upload/Download über signed URLs, `SharedAccessStorageHandlerApiUrl`) | Upload/Download direkt am Storage |
| API/Processing → StorageHandler | HTTP intern (`StorageHandlerApiUrl`), Header-AccessKey | Binärzugriff |
| API ↔ Processing / Search / EventHub / Identity / StorageHandler | Message Broker (AMQP 1.0 Artemis oder Azure Service Bus) | Jobs, Callbacks, Index-Events, Notifications, interne Events (`Advellence.Sharedien.Internal`-Topic) |
| API/Processing/StorageHandler/EventHub/Identity → SQL Server | TDS | Daten; Processing & StorageHandler haben ab 3.11.0 zusätzlich den `Sharedien`-Connection-String (Konfiguration aus DB, siehe §2.3) |
| API → Elasticsearch | HTTP (NEST) | nur API (Search-Modul) |

### 1.3 Konfigurations-Mechanik (alle .NET-Dienste identisch)

`KestrelStartup<TStartup>` (`src/Backend.Common/Sharedien.Common/Infrastructure/Startup/KestrelStartup.cs`). Priorität (1 gewinnt):

1. Kommandozeilen-Argumente
2. Umgebungsvariablen (`Sharedien__Email__IsEnabled`, Arrays mit `__0__`, Azure-App-Service-Stil)
3. YAML-Text in Env-Var `SHAREDIEN3_CONFIG_YAML` (so werden alle Docker-Container konfiguriert)
4. Pfad in Env-Var `SHAREDIEN3_CONFIG_FILE_YAML`
5. `appsettings.Overrides.yml` (Kunden-Overrides, wird von `update.bat` nie überschrieben)
6. `appsettings.yml` (Basis, darf nicht geändert werden)
7. Zusätzlich User-Secrets in Development; YAML via `NetEscapades.Configuration.Yaml`
8. **SQL-Konfigurationsquelle** `SqlConfigurationProvider`: liest `Admin.Configuration (Path, Value)` aus der Sharedien-DB (Prefix = Top-Level-Section wie `Processing`), validiert jeden Wert gegen die typisierte Config-Klasse; ermöglicht Admin-UI-gepflegte Settings (`ConfigClassAttribute`/`ConfigPropertyAttribute` mit `RequiresRestart`, `IsPrivate`, `DefaultValue`, Anzeigenamen aus `Texts.resx`). `DefaultConfigurationReloader` lädt bei `ApplicationSettingsChangedEvent` (interner Event über Broker) die Config neu und rekonfiguriert Serilog.

Jede Top-Level-Config (`TopLevelConfigurationBase`) enthält `Logging`, `Hosting`, `InternalCallbackJob`.

### 1.4 Hosting-Varianten

| Variante | Mechanik | Quelle |
|---|---|---|
| Docker/Linux | `mcr.microsoft.com/dotnet/core/aspnet:3.1`, Kestrel, `ASPNETCORE_URLS` | `docker/_build/*/Dockerfile` |
| Windows-Service (On-Premise) | `WindowsServiceStartup<T>` → `HttpSysStartup<T>` (HTTP.sys als Server, `RunAsService`), Working-Dir = Installationsordner | `Infrastructure/Startup/HttpSysWindowsService/*.cs` |
| IIS | `UseIIS()/UseIISIntegration()` in KestrelStartup, `web.config` in Identity/StorageHandler | Docs `run-identity-service-with-iis.rst` |
| Azure App Service | Env-Var-Konfiguration, App Insights, Azure Service Bus, Azure Blob Storage; Processing-Binaries per FTP nach `/site/Processing.Dependencies/win-x64` | Docs `configuration/azure/*`, Update-Notes |

Windows-Service-Skripte (`src/windows-service/*.bat`, per `pre-build.ps1` in jedes WindowsService-Projekt kopiert):

| Skript | Funktion |
|---|---|
| `install-windows-service.bat <Name> <Dir> <Dll> <HttpPort> <HttpsPort> [args]` | kopiert nach `%ProgramFiles%\Sharedien3\<Dir>` (oder `%SHAREDIEN3_HOME%`), `sc create ... start=delayed-auto group=Sharedien3 binPath="dotnet <dll> --environment OnPremise --urls http://+:P;https://+:S"`, `netsh http add urlacl` für SYSTEM |
| `update-windows-service.bat` | stoppt Dienst, löscht alles außer `appsettings.Overrides.yml`, `*.config`, `configuration.json`, `configuration.overrides.js`, kopiert neue Dateien |
| `uninstall-windows-service.bat` | `sc stop/delete`, Ordner löschen |
| `bind-https-certificate.ps1` | `netsh http add sslcert` für Ports 4000, 4001, 4008, 4041, 443 (Thumbprint-Eingabe) |

Standard-Ports On-Premise: StorageHandler 5000/4000, API 5001/4001, Processing 5002/4002, EventHub 5008/4008, Identity 5051/4041 (install.bat) bzw. 5007 (Docker), Web 80/443. Firewall-Doku nennt HTTPS 443, 4000, 4001, 4040 und HTTP 80, 5000, 5001, 5050.

`Frontend.WindowsService` (`src/Frontend.WindowsService`): Kestrel/HTTP.sys-Static-File-Server für den Angular-`dist`-Ordner. Parameter `--static-files-dir`, `--iis-redirect-rules-file` (IIS-Rewrite-XML), `--apache-redirect-rules-file` (mod_rewrite; install.bat nutzt `dist\rewrite.rules` → SPA-Fallback), `--no-windows-service`. Config `Frontend.Hosting` (HTTPS-Redirect, Forwarded Headers) und `Frontend.StaticFiles.CacheConfigurationItems` (`/configuration`, `/locales` → `Cache-Control: no-cache`). Entspricht 1:1 der `nginx.conf` im Docker-Image (SPA-Rewrite auf `index.html`, no-cache für `/configuration` und `/locales`). Frontend-Runtime-Config über `configuration.js` + `configuration.overrides.js` (`sharedienAppConfig.*`; in Docker via Env `UI_CONF` in die Datei geschrieben). Übersetzungs-Overrides über `dist/locales/<lang>/override.json`; Themes als CSS-Custom-Properties in `configuration/themes/*.css`.

### 1.5 Reverse Proxy / Hosting-Config (`<APP>.Hosting.*`, `HostingConfiguration.cs`)

| Key | Bedeutung |
|---|---|
| `PathBase` | Sub-Pfad (z. B. `/storage-handler`) |
| `UseForwardedHeaders` | `X-Forwarded-For/-Proto` auswerten |
| `KnownProxies` / `KnownNetworks[{Address,Prefix}]` | vertrauenswürdige Proxies (IPv4/IPv6/`::ffff:`), Pflicht hinter Reverse Proxy |
| `OverrideForwardedHeaders`, `ForwardedSchemeOverride`, `ForwardedHostOverride` | manuelles Überschreiben von Scheme/Host (`ForwardedHeadersOverrideMiddleware`) |
| `EnableHttpsRedirection` | HTTP→HTTPS, wenn direkt exponiert |

NGINX-Beispiel in `installation/hosting-with-reverse-proxy-load-balancer.rst` (WebSocket-Upgrade-Header für SignalR). Zertifikate: ein SAN-PFX pro Umgebung (`dam.`, `dam-api.`, `dam-id.`).

### 1.6 Dev-Skripte (`scripts/`)

| Skript | Funktion |
|---|---|
| `spawn_*.cmd/.sh` | `dotnet run --environment Development` je Dienst; `spawn_webui` = `npm run start:local` |
| `rebuild_all.cmd` | bin/obj löschen, Solutions bauen (nennt zusätzlich `Processing.Contracts`, `Search.Contracts`, `StorageHandler.Client` – im Repo nicht vorhanden → **unklar/obsolet**) |
| `reset_data.cmd` | `Databases_recreate.sql`, Upload-Ordner löschen, Dienste neu starten |
| `reindex.cmd` / `_ps/reindex.ps1` | Token via ROPC (`POST /connect/token`, client `sharedien-api`, user `advadmin`), dann `POST /api/v3/search/reindex` mit `entityTypeIdentifiers: [AssetSet, Workspace, Category, CustomEntity]` |
| `_ps/save_settings.ps1` | zippt alle Override-Configs |
| `start_docker.ps1` | interaktives Compose-Startmenü (infrastructure → identity → sharedien) |
| `_database/_scripts/*.sql` | Wartung: Index-Defrag, Orphans (`Storage_GetOrphans.sql`), 0-Byte-Assets, Property-Duplikate, Node-Permissions reparieren, Dev-Seed (`Databases_init.sql`: Kategorien `media`/`animals`, Agent-Gruppe/Rolle) |
| `_test/*` | Backup/Restore für Integrationstests, `AssetDerivations`-Szenario |

### 1.7 Versionierung / Build

`GitVersion.yml`: master = Release, develop = `beta` (ContinuousDeployment), feature = `alpha`, hotfix = `beta`, release-Branches `rc`. Beta-Images nach `sharedienregistry.azurecr.io`, Release-Images nach Docker Hub `sharedien/*`. Jedes Dienst-Verzeichnis hat `build_alpha_docker_image.ps1`. NuGet-Feed: `sharedien.pkgs.visualstudio.com/_packaging/Sharedien.Packages`. Hersteller: Advellence Solutions AG (Zürich). Doku: Sphinx (`docs/technical-manual`, RST, Docker-Builder). `docs/api-diff`: Swagger-Spec-Diff v1/v2 (`patch_swagger.ps1`).

### 1.8 Standard-Renditionen (Doku `renditions/index.rst`)

| Name | Format | Auflösung | DPI | Farbraum/Profil |
|---|---|---|---|---|
| Thumbnail | jpg | 240×240 | 72 | RGB / sRGB |
| Animated thumbnail | gif | 240×240 | 72 | RGB / sRGB (nur Video) |
| Screen | jpg | 1600×1200 | 96 | RGB / sRGB |
| Print | tiff | 3508×3508 | 300 | CMYK / ISO Coated v2 (ECI) |

Mapping Eingabetyp → Renditionen: Bilder (jpg, png, bmp, tif, gif, eps, psd, psb) → Thumbnail+Screen+Print; Video (avi, mp4, mov, wmv, ogv, mpg, webm, 3gp, 3g2, mkv, mk3d) → Thumbnail + Animated; pdf, ai, ppt(x), doc(x), xls(x) → Thumbnail; indd → keine. Eigene ICC-Profile: Ordner `ColorProfiles/` im Processing-Installationsordner (Docker: Volume-Mount).

### 1.9 Feature-Historie aus den Update-Notes (relevant für Nachbau)

| Version | Änderung |
|---|---|
| 3.5.1 | Contact-Print-Konfiguration `Sharedien.ContactPrintMetadata.{One,Two,Six,Fifteen}PicturesPerPage` (Kontaktabzug-PDF, benötigt libgdiplus im API-Image) |
| 3.5.2 | Asset-Index: Dateiname-Feld für Exact-Search |
| 3.6.0 | Storage-Provider-Modell (`StorageProviders.{FileSystem,AzureBlob,StorageHandler}StorageProviders`), AccessKeys + `SharedAccessSigningKey`, `Upload.UploadSigningKey`, ExifTool als Dependency, `Processing.FileCache.CacheExpirationInSeconds` |
| 3.7.0 | Frontend auf Authorization Code Flow; ImageMagick 7.0.10-16 portable; Search-API in API-Dienst integriert; PermissionDto-Umbenennung `AssetRelation* → Relation*` |
| 3.7.2 | Application Insights statt ELK; ImageMagick 7.0.10-30 |
| 3.8.0 | Neues MessageBroker-Modell (`MessageBroker.AMQP` / `MessageBroker.AzureServiceBus`, `Receivers[].MessageIngestion.CpuCoreMultiplier`), EventHub-Dienst, E-Mail von Identity in API verschoben, Identity braucht `BackgroundJobs`-Connection-String, `Processing.FileCache.TempRootPath` |
| 3.9.0 | API v5; Blender-Support; Topic `Advellence.Sharedien.EventHub`; Reindex BillingContact/RightsOwner/License (DRM-Lizenzmodul); Favicon-Branding |
| 3.10.0 | API v6, IE11-Support entfällt; Elasticsearch 7.10.2; Theming; EventHub On-Prem-Installation; Reindex LicensePurchase |
| 3.10.1 | `/signin-oidc`-Redirect-URIs für alle Dienste (Swagger-Login), `Authentication.ExternalServiceUrl` überall |
| 3.11.0 | Topic `Advellence.Sharedien.Internal` mit Subscriptions Api/Processing/Identity/EventHub; `Cleanup.EventStreamMessageCleanup`; Processing/StorageHandler erhalten `Sharedien`-Connection-String und MessageBroker-Config; PropertyDefinition `IgnoreCopyingOnNewVersion → IgnoreCopying` |

---

## 2. Querschnitt: Sharedien.Common

`src/Backend.Common/Sharedien.Common` (netcoreapp3.1), von allen Diensten referenziert. Wichtige NuGets: AMQPNetLite 2.4.0, Autofac.Extensions.DependencyInjection 7.0.1, AutoMapper 10.0.0, Dapper 2.0.35, FluentValidation 9.2.2, Microsoft.AspNetCore.Authentication.OpenIdConnect 3.1.16, Mvc.Versioning 4.1.1, Microsoft.Data.SqlClient 2.0.1, NetEscapades.Configuration.Yaml 2.0.1, Newtonsoft.Json 12.0.3, Serilog 2.10 (+ Sinks File/Console/Http/ApplicationInsights/Debug, Enrichers Environment/Thread, Serilog.Exceptions, Formatting.Compact, Settings.Reloader), Swashbuckle 5.6.3, System.Linq.Dynamic.Core 1.2.2, ConcurrentHashSet.

### 2.1 Logging (`Infrastructure/Logging/LogSetup.cs`)

- Serilog; JSON-Datei (CompactJsonFormatter, tägliche Rotation, `flushToDiskInterval 1s`), optional Plain-Text-Datei, Console, Durable-HTTP-Sink (Logstash-ähnlich, Basic-Auth, Puffer-Dateien `http-sink-buffer-{Date}.log`), Application Insights (`ApplicationInsights.InstrumentationKey`).
- Enricher: Application, Tenant, Environment, SourceContext, MachineName, ThreadId, ExceptionDetails, Diagnostics-Activity (`DiagnosticsActivityLogEventEnricher`).
- `LogSanitation`: ersetzt konfigurierte Property-Werte (User-Identifier etc.) durch `<redacted>`.
- Log-Level zur Laufzeit umschaltbar (`LoggingLevelSwitch`, `SwitchableLogger`), Reconfigure bei Config-Änderung.
- `InstanceIdentifierEnvironmentVariable` → Instanz-ID im Dateinamen (Azure Scale-Out).
- Korrelation: Header `X-Operation-ID` in jeder Antwort (`AddOperationIdHeaderMiddleware`), Property `OperationId` in allen Log-Events, wird an nachgelagerte Service-Calls weitergereicht (Doku `monitoring/log-events-correlation.rst`).
- Bekannte Log-Properties (`LogProperties.cs`): MethodName, ExecutingUserIdentifier, UserIdentifier, UserGroupIdentifier, TaskIdentifier, ProcessingIdentifier, Status, UserGroups, ResetPasswordCode, ExternalGroupProvider, Token, JobType, Request/ResponseMessage, OperationId, TransactionIdentifier.
- Config-Keys `<APP>.Logging.*`: LogLevel (Information), MicrosoftLogLevel, LogDirectory (LogFiles), LogFileBaseName (log), LogFileExtension, WritePlainTextLogFile (true), PlainTextLogFileBaseName (log-text), InstanceIdentifierEnvironmentVariable, Application, Tenant (Pflicht), Environment (Pflicht), DefaultSourceContext, Shared, SelfLogEnabled, HttpApiRequestResponseLoggingEnabled (`RequestResponseLoggingMiddleware`), ConsoleLogging.Enabled (true), HttpLogging.{Enabled, ServerUrl, BasicAuthUserName, BasicAuthPassword}, LogSanitation.{IsEnabled, LogPropertiesToSanitize[], LogValueReplacement}.
- Azure WebJob `App_Data/jobs/triggered/CleanupLogFiles/run.ps1` in API/Processing/EventHub/Identity: Log-Cleanup.

### 2.2 Weitere Querschnitts-Bausteine

| Baustein | Datei | Funktion |
|---|---|---|
| API-Versionierung | `Infrastructure/Api/ApiVersions.cs`, `SwaggerSetupExtensions.cs` | `Microsoft.AspNetCore.Mvc.Versioning`, Swagger pro Version, OAuth2-Login in Swagger UI (`RedirectUrl .../swagger/oauth2-redirect.html`), optional geschützt (`Authentication.IsApiDocumentationAuthenticationEnabled`) |
| Auth (Bearer) | `Infrastructure/Authentication/*` | `SharedienAuthenticationHandler`, `TokenValidationService` (Details §7) |
| Middleware | `Infrastructure/Middleware/*` | `AddHeaderMiddleware`, `AddOperationIdHeaderMiddleware`, `ForwardedHeadersOverrideMiddleware`, `InterceptRequestMiddleware`, `RequestResponseLoggingMiddleware` |
| Signed URLs | `Infrastructure/SignedUrlProvider.cs` | HMAC-signierte URLs (Details §4) |
| HTTP-Client-Basis | `Infrastructure/WebserviceClient/HttpClientBase.cs` | Basis für Service-zu-Service-Aufrufe, Operation-ID-Weitergabe |
| Background-Jobs | `Core/BaseClasses/BackgroundJobBase.cs`, `Infrastructure/BackgroundJobs/*` | Hosted-Service-Runner für Queue-Consumer (Details §12) |
| Validation | `Infrastructure/Validation/*` | FluentValidation-Wrapper |
| Cache | `Cache/ExpiringMemoryCache.cs`, `Core/Caching/*` | In-Memory mit Ablauf, Token-Cache; Config `Cache.{DisableCache, CacheExpirationInSeconds=10}` |
| Attribute | `AllowSynchronousIoAttribute`, `DisableFormValueModelBindingAttribute` | für Streaming-Upload-Endpoints |
| DB-Schema-Konstanten | `Sharedien.Common.DbScheme` (Projekt-Referenz) | Tabellen-/Spaltennamen (z. B. `Admin.Configuration`) |

---

## 3. Processing

Pfad: `src/Processing`. Readme: "microservice that allows to derivate files (images, videos, pdf, office, ...) in the storage handler through messages in the message queue". **Rein Queue-getrieben** – es gibt keinen Job-REST-Endpoint.

### 3.1 Architektur

| Projekt | Inhalt |
|---|---|
| `Processing.Api` | Web-Host (netcoreapp3.1). NuGet: Microsoft.ApplicationInsights.AspNetCore 2.15.0, Autofac.Extensions.DependencyInjection 7.0.1, JsonSubTypes 1.8.0. Enthält `ColorProfiles/*.icc`, Azure-WebJob `CleanupLogFiles`, `_backup.filter`. Kopiert `Processing.Dependencies/bin/**` (Binaries) ins Publish-Verzeichnis. |
| `Processing.Api.WindowsService` | gleicher Code als HTTP.sys-Windows-Service (`install.bat` → Port 5002/4002) |
| `Processing.Core` | Contracts (`IWorker`, `IStepProcessor`, `IMetadataExtractor`, `ICachedStorageHandlerFileService`, …) und Business-Modelle (Derivation-Definitionen, `OverlayOffsetLogic`) |
| `Processing.Infrastructure` | Worker, Step-Prozessoren, Extractors, Services. NuGet: CliWrap 3.2.0 (Prozessaufrufe), CoreCLR-NCalc 2.2.88 (Ausdrucksauswertung in Step-Variablen) |
| `ProcessingTests` | xUnit-Tests (siehe 3.9) |
| `Processing.Dependencies` | **Git-Submodul** (`../../../Processing.Dependencies/_git/Processing.Dependencies`, hier leer). Enthält die Tool-Binaries als zip/tgz unter `bin/win-x64`, `bin/linux-x64`; Windows-Kunden erhalten sie als "Processing Dependencies win-x64-<ver>.zip" per SFTP |
| Referenzen | `Sharedien.Common`, `Sharedien.Common.Infrastructure.Queue`, `Sharedien.Common.Infrastructure.Storage`, `Sharedien.Api.Contracts` (Message-DTOs) |

Startup (`Processing.Api/Startup.cs`): Config binden (+ SQL-Config-Source mit Prefix `Processing`, benötigt `ConnectionStrings.Sharedien`), Serilog, App Insights, `AddSharedienAuthentication` + OIDC (nur für Swagger-Schutz), MVC + API-Versioning, Swagger, `AddHttpClient`, Autofac-Container: `StorageProviderDependencies.RegisterStorageProvider` (StorageHandler- oder Azure-Blob-Provider), `ExpiringMemoryCache`, alle Worker/Prozessoren/Konfigurationen, `RegisterMessageBroker`, `RegisterInternalCallbackJob` (Config-Reload-Events), `JobCallbackQueueProducer`, `JobReaderCallbackJob` als `IHostedService` via `GenericBackgroundJobRunner`. Anschließend `IMessageBrokerProvider.InitializeMessageBroker()` (verbindet Sender/Receiver).

API-Endpoints: nur `GET /api/v1/info` (`AllowAnonymous`; liefert `version`, `informationalVersion`) sowie `/swagger`. Alle anderen Endpoints `RequireAuthorization` (es gibt keine).

### 3.2 Job-Pipeline (Message Queue)

| Element | Wert (appsettings.yml) |
|---|---|
| Receiver Jobs | `sharedien_processing_processingjobstart_receiver` → AMQP `queue://Advellence.Sharedien.ProcessingJobStart` / ASB `Advellence.Sharedien.ProcessingJobStart`; `MessageIngestion.UseCpuCoreCount: true, CpuCoreMultiplier: 2` (= parallele Jobs = CPU-Kerne × 2) |
| Sender Callback | `sharedien_processing_derivationjobcallback_sender` → `queue://Advellence.Sharedien.DerivationJobCallback` |
| Receiver intern | `sharedien_processing_internal_receiver` → `topic://VirtualTopic.Sharedien.Internal` bzw. ASB `Advellence.Sharedien.Internal/Subscriptions/Processing` (`IsTopic: true`) – für `ApplicationSettingsChangedEvent` (Config-Reload) |
| `JobReader.MessageBrokerConnectionIdentifier` / `JobCallback.MessageBrokerConnectionIdentifier` | Zuordnung der obigen Identifier |
| Message-Property | `x_sharedien_message_type` = .NET-FullName des Nachrichtentyps; außerdem `x_sharedien_priority_forced`, `x_sharedien_priority_selector`, `x_sharedien_operation_id` (`SharedienMessageApplicationProperties`) |
| Serialisierung | JSON (Newtonsoft); `TransformationStepDto` polymorph über Discriminator `StepType` (FullName) via JsonSubTypes / `MessageSerializerSubTypesRegistration` |

Ablauf `JobReaderCallbackJob` (`Processing.Infrastructure/Jobs/JobReaderCallbackJob.cs`): Handler registrieren → Nachricht anhand `x_sharedien_message_type` deserialisieren → `ProcessingJobWorkerRepository.GetWorker(type)` → `worker.StartJobAsync` (mit Stopwatch + GC-Memory-Logging) → `MessageOutcome.Accepted`; unbekannter Typ/kein Worker → `Rejected`. **Kein eigenes Retry** im Processing; Wiederholung nur über Broker (Azure: Max delivery count 10, Lock 5 min laut Update-Notes 3.8.0).

Nachrichten-Contracts (`src/Backend.Contracts/Sharedien.Api.Contracts/Public/MessageQueue/V1/Processing/**`):

| Nachricht (Start → Processing) | Felder | Worker |
|---|---|---|
| `JobStartMessage` (Basis) | `ProcessingId` (GUID), `JobInitiatorProvidedMetaData` (Dictionary, wird 1:1 zurückgegeben) | – |
| `CustomDerivationStartMessage` | `SourceDataId`, `OriginalFileName/-Extension/-Size`, `AssetType`, `OptionalFiles[{DataId, FileName}]` (z. B. Wasserzeichen-Datei), `OutputFileExtension`, `OutputMimeType`, `DerivationTypeIdentifier`, `JobDefinitionDto{Steps[]}` | `CustomDerivationWorker` |
| `ImageDerivationStartMessage` | `SourceDataId`, `OriginalFileName`, `OutputFileExtension`, `OutputMimeType`, `DerivationTypeIdentifier`, `JobDefinitionDto` (`ImageDerivationJobDefinitionDto`) | `ImageDerivationWorker` |
| `MetadataExtractionStartMessage` / `MetadataExtractionRestartMessage` | `SourceDataId`, `OriginalFileName/-Extension/-Size`, `OutputFileExtension`, `OutputMimeType`, `AssetSetIdentifier`, `AssetVersionIdentifier`, `AssetType` | `MetadataExtractionWorker` |
| `OfficeDerivationStartMessage` (`OfficeToThumbnail`, `OfficeToPdf`), `PrintDerivationStartMessage` (`PrintToThumbnail`, `PrintToPdf`), `VideoDerivationStartMessage` (`VideoToGif`, `VideoToThumbnail`) | nur Contracts, **kein Worker registriert** → Legacy (alle heutigen Job-Definitionen sind `DerivationDataType.Custom = 4`; Backend-Enum: Image=0, Movie=1, PrintDocument=2, OfficeDocument=3, Custom=4) | – |

| Nachricht (Callback → API) | Felder |
|---|---|
| `JobStatusMessage` (Basis) | `ProcessingIdentifier`, `JobStatus` (Started=0, Processing=1, Finished=2, Failed=3), `Message`, `JobInitiatorProvidedMetaData`, `JobMetaData` |
| `DerivationStatusMessage` | + `DataIdentifier` (neue Datei im Storage), `FileSize`, `FileHash` |
| `MetadataExtractionStatusMessage` | + `Properties[{Name, Value, Tag}]`, `AssetSetIdentifier`, `AssetVersionIdentifier` (**Auffälligkeit**: im Konstruktor `assetVersionIdentifier = AssetVersionIdentifier` – Parameter wird nie zugewiesen, Feld bleibt null) |

Datei-Handling: `CachedStorageHandlerFileService` lädt Quelldateien über `IStorageProvider.RetrieveAsync` in ein Temp-Verzeichnis (`FileCache.TempRootPath`, sonst System-Temp), Semaphore pro `SourceDataId`, `TempFile.Lock/Unlock`, Cache-Ablauf `FileCache.CacheExpirationInSeconds` (600) – abgelaufene, nicht gesperrte Dateien werden beim nächsten Zugriff gelöscht; zusätzlich Cache der extrahierten Metadaten je DataId/Dateipfad. Ergebnisse: `_storageProvider.GenerateIdentifier()` + `UploadAsync(id, stream, mimeType)` → `ContentLength`, `ContentHash` (Hash kommt vom Storage-Provider, siehe §4).

Fehlerbehandlung: Worker fangen alle Exceptions → `JobStatus.Failed` + Message; Step-Fehler (Exit-Code ≠ 0) → Abbruch der Step-Kette; CLI-Aufrufe mit `CancellationTokenSource(JobTimeoutInSeconds)`; Prozessbaum wird im `finally` gekillt (`process.Kill(true)`); Overlay-Fehler in der Image-Pipeline werden nur geloggt und ignoriert.

### 3.3 Custom-Derivation-Engine (generische Step-Pipeline)

`CustomDerivationService.CreateDerivationAsync`: Temp-Job-Verzeichnis → Quelldatei cachen → globale Variablen setzen → optionale Dateien laden (Variable = `FileName` → Temp-Pfad) → je Step: Prozessor per `StepProcessorFactory` (Autofac, Lookup über `StepTypeIdentifier`) → `InitializeInputOutputFileVariables` (Templates `{…}` ersetzen) → Pfade absolut machen → `InitializeStepSystemVariablesAsync` (prozessorspezifisch) → `InitializeStepUserVariables` (Step-Variablen, optional NCalc-`Evaluate`) → `ProcessStep` (CliWrap, stdout/stderr gepuffert, Exit-Code) → nach letztem Step Upload von `{JobOutputFile}`.

Step-Definition (`CustomDerivationStep`): `CustomStepType`, `StepInputFile`, `StepOutputFile`, `ArgumentString` (Template), `Variables[{Name, Expression, Evaluate}]`, `TargetProfile`, `IntentString`, `UseColorProfileOverrides`.

System-Variablen (`CustomDerivationDefaultVariables`): `JobDirectory`, `JobInputFile`, `JobOutputFile`, `JobInputFileNameWithoutExtension`, `StepInputFile`, `StepOutputFile`, `SourceProfile`, `TargetProfile`, `StepVideoDurationSeconds`, `StepLibreOfficeEnvironmentArgument`.

| Step-Typ (`CustomDerivationStepTypesDto`) | Prozessor registriert | Besonderheit (`InitializeStepSystemVariablesAsync`) | Default-Executable win-x64 / linux-x64 |
|---|---|---|---|
| `ImageMagickConvert` | ja | Farbmanagement (siehe unten): setzt `{SourceProfile}`, `{TargetProfile}` anhand ExifTool-Metadaten der Step-Eingabe (`*` in Pfad → `001` für Sequenzen) | `win-x64\ImageMagick\magick.exe` / `linux-x64/imagemagick/bin/magick` |
| `ImageMagickIdentify` | nur Pfad-Config | – | `magick.exe` / `magick` |
| `FFMpeg` | ja | ruft vorher FFProbe auf und setzt `{StepVideoDurationSeconds}` (**Auffälligkeit**: Wert = Laufzeit des ffprobe-Prozesses in Sekunden `RunTime.Seconds`, nicht die Videodauer) | `win-x64\ffmpeg\bin\ffmpeg.exe` / `linux-x64/ffmpeg/ffmpeg` |
| `FFProbe` | ja | wie FFMpeg | `ffprobe.exe` / `linux-x64/ffmpeg/ffprobe` |
| `GhostScript` | ja | – | `win-x64\Ghostscript\bin\gswin64c.exe` / `linux-x64/ghostscript/bin/gs` |
| `LibreOffice` | ja | `LibreOfficeEnvironmentManager`: pro paralleler Konvertierung eigenes `-env:UserInstallation="file:///<temp>/P_L/p<n>"` (sonst blockiert soffice parallele Instanzen); Basisordner wird beim Start gelöscht | `win-x64\LibreOfficePortable\App\libreoffice\program\soffice.exe` / `linux-x64/libreoffice/opt/libreoffice6.0/program/soffice` |
| `Blender` | ja | Working-Dir = `FileCache.TempRootPath` | `win-x64\blender\blender.exe` / `linux-x64/blender/blender` |
| `DCRaw` | Klasse vorhanden, **nicht registriert** | – | `win-x64\ImageMagick\dcraw.exe` / – |
| `ExifTool` | nur Pfad-Config (für Metadaten) | – | `win-x64\exiftool\exiftool.exe` / `linux-x64/exiftool/exiftool` |
| `Archive` | nur Konstante, kein Prozessor | – | – |

Executable-Auflösung (`StepProcessorPathResolver`): 1. Config `CustomDerivation.ExecutablePaths.<StepType>Executable` → 2. Env-Var `SHAREDIEN3_PROCESSING_<STEPTYPE>` → 3. Default-Pfad relativ zum Assembly (OS-abhängig); Datei muss existieren. `StepProcessorBinariesInitializer` entpackt einmalig `*.zip/*.tgz` in `win-x64|linux-x64|osx-x64` (Marker `binariesExtracted.txt`) – registriert, Aufruf im Startup **unklar**.

Farbmanagement (`ImageMagickConvertStepProcessor.UpdateImageProfilesVariablesAsync`):
- Metadaten der Eingabe (`image_color_space` = `ColorSpaceKey`, `image_icc_profile` = `IccProfileKey`).
- Kein eingebettetes Profil → `ColorSpaceDefaultProfiles` (Default: sRGB→`sRGB_v4.icc`, CMYK→`ISOcoated_v2_eci.icc`) bzw. `FallbackSourceProfile` → `{SourceProfile}` = `-profile <abs. Pfad>`; sonst `{SourceProfile}` leer (eingebettetes Profil wird genutzt) und Profilname über `ColorProfileHelper` (eciCMYK, eciRGB v2, ISO Coated v2 300% (ECI), ISO Coated v2 (ECI), sRGB v4, U.S. Web Coated (SWOP) v2 → Dateien).
- Zielprofil = `step.TargetProfile`, bei `UseColorProfileOverrides` durch `IccProfileOverrides` (Default: `eciRGB v2` → `eciRGB_v2.icc`) ersetzt; wenn Quelle ≠ Ziel: `{TargetProfile}` = `"{IntentString} -profile <ziel>"` (z. B. `-intent relative -black-point-compensation`).
- Mitgelieferte ICC-Profile (`Processing.Api/ColorProfiles`): `eciCMYK.icc`, `eciRGB_v2.icc`, `ISOcoated_v2_300_eci.icc`, `ISOcoated_v2_eci.icc`, `sRGB_v4.icc`, `USWebCoatedSWOP.icc`. Zusätzlich `ImageProfile.ImageProfilePath: ImageProfiles` (`ImageProfileFileRepository`) – Verwendung **unklar**.

### 3.4 Standard-Job-Definitionen (Backend-DB `MAM.DerivationJobDefinition`, Stand Migration 0239/0256)

Die konkreten Renditionen werden im Backend gepflegt (Tabellen `MAM.DerivationType`, `MAM.DerivationJobDefinition` (JSON), `MAM.DerivationJobDefinitionInputFileType`, `MAM.AssetTypeDerivationType` mit Flags `IsDefaultDerivation, AllowManualUpload, IsOnDemand, ShowInDownloads, UseAsThumbnail, UseAsPreview`, `IsSystem`, übersetzbare Namen, Berechtigungen pro DerivationType, `DerivationTypeRestrictions` pro AssetType). Alle sind `DerivationDataType: 4` (Custom):

| Identifier | Eingabe | Steps / Kommandos |
|---|---|---|
| `Thumbnail` | jpg, jpeg, png, bmp, tif(f), gif, psd, psb | ImageMagick: `-quiet "{in}[0]" -density 72x72 -resize "240x240>" -quality 92 -flatten -background white -alpha remove {SourceProfile} {TargetProfile} -colorspace sRGB -type truecolor -strip +repage "{out}"`; TargetProfile `sRGB_v4.icc`, Intent `-intent relative -black-point-compensation`, UseColorProfileOverrides |
| `Screen` | wie Thumbnail | wie Thumbnail mit `-resize "1600x1200>" -quality 95` |
| `Print` | Bilder (inkl. psd/psb) | `-density 300x300 -resize "3508x3508>" -quality 95 -flatten -background white -alpha remove {SourceProfile} {TargetProfile} -strip +repage`; Ausgabe tiff; TargetProfile laut Migration 0149 `sRGB_v4.icc`, laut Doku CMYK/ISO Coated v2 – Stand nach Migration 0153 (`Fix_JobDefinitions_For_Image_Channels`) **unklar** |
| `Thumbnail_Video` | avi, mp4, mov, wmv, ogv, mpg/mpeg, webm, 3gp, 3g2, mkv, mk3d, mxf | Variable `seekPosition = ({StepVideoDurationSeconds} < 2) ? 1 : 2` (NCalc); FFMpeg `-ss {seekPosition} -i "{in}" -loglevel error -f image2 -vframes 1 -y output.jpg`; ImageMagick `-density 72x72 -resize "240x240>" -quality 92 -colorspace sRGB -type truecolor` |
| `Thumbnail_Video_Animated` | Video | Variablen `imagesPerGif=16`, `imageRate = Max((Floor({StepVideoDurationSeconds} - 1) / {imagesPerGif}), 1)`; FFMpeg `-ss 1 -i in -f image2 -vframes 16 -vf "scale=iw*min(240/iw,240/ih):ih*min(240/iw,240/ih),fps=fps=1/{imageRate}" -loop 0 -y -quality 92 output_%03d.png`; ImageMagick `-delay 75 -loop 0 "output_*.png" -colorspace sRGB -type truecolor out.gif` |
| `Thumbnail_Print_Document` | pdf | Ghostscript `-sDEVICE=jpeg -dJPEGQ=100 -dNOPAUSE -dBATCH -dQUIET -dSAFER -r300 -dGraphicsAlphaBits=4 -dUseCIEColor -dEPSCrop -sstdout=%stderr -sOutputFile=ghost_output.jpg in` (erste Seite); ImageMagick 240×240 |
| `Thumbnail_Office_Document` | doc(x), xls(x), ppt(x) | LibreOffice `-nocrashreport --nofirststartwizard --norestore {StepLibreOfficeEnvironmentArgument} --nolockcheck --headless --invisible --convert-to pdf --outdir out_libre in`; Ghostscript auf `out_libre/{JobInputFileNameWithoutExtension}.pdf`; ImageMagick 240×240 |
| `Thumbnail_Eps`, `Screen_Eps`, `Print_Eps` | eps, ai | Ghostscript (wie oben) → ImageMagick mit Profilen (240 / 1600×1200 / Print); `Print_Eps` Ausgabe jpg (statt tiff, Migration 0230); `ai` seit 0244 |
| `Thumbnail_3D`, `Screen_3D` | fbx (AssetType CAD) | Blender `-b -noaudio -E CYCLES --python-expr "<bpy-Skript: Szene leeren, FBX importieren, Kamera/Licht ergänzen falls fehlend, camera_to_view_selected, Render 320×320 bzw. 1600×1200 PNG RGBA transparent>"`; Ausgabe png |
| `Crop` / `Jpg_Crop` (Migration 0060) | jpg | Crop-Derivation (Image-Pipeline, siehe 3.5) – Details **unklar** |
| `Original` | alle | Originaldatei (kein Job) |
| PDF-Preview (Migration 0156) | pdf | Screen-Derivation für PDF = Original (Vorschau im Browser) |

Weitere Derivation-Typen im Backend: Wasserzeichen-Varianten (`WatermarkCacheService`: Watermark-Definitionen mit Triggern auf Property-Werte/Listenwerte erzeugen/löschen Derivationen mit Watermark-Suffix; Wasserzeichen-Bild wird als `OptionalFiles`/Overlay mitgegeben), Contact-Print-PDF (im API-Dienst mit System.Drawing/libgdiplus, nicht Processing).

### 3.5 Image-Derivation-Pipeline (programmatisch, `ImageMagickImageDerivationService`)

Für `ImageDerivationStartMessage` (Backend `ProcessingMessageGeneratorService`, u. a. Crop/Wasserzeichen). Modell `ImageDerivationJobDefinition`:

| Teil | Felder | ImageMagick-Umsetzung |
|---|---|---|
| `SourceInputImageQualityDefinition` (`InputImageDefinition`) | `ColorSpace, ColorType, ColorProfileName, Dpi, CompressionMethod, FrameIndex` | Eingabe immer `file[0]` (FrameIndex nicht ausgewertet) |
| `ImageTransformation` | `TransformationSteps[]`, `SetOpacity/OpacityValue`, `SetBackgroundColor/BackgroundColorDefinition{RgbA|CmykA, Alpha, R,G,B / C,M,Y,K}` | `-background rgba(r,g,b,a)` bzw. `cmyka(...)`; Opacity: `-alpha Set -channel Alpha -evaluate Multiply <v> +channel`; Abschluss `-trim +repage` |
| `CropStep{CropDefinition: SizeX, SizeY, XAxisAnchor(Left/Center/Right), YAxisAnchor(Top/Center/Bottom), X/YAxisAnchorOffset, NaturalWidth/Height}` | Koordinaten werden von "Natural"-Größe (UI-Anzeige) auf Originalgröße skaliert | `-crop WxH+L+T` |
| `ScaleStep{ScaleDefinition: ScaleType Absolute/Relative, IgnoreAspectRatio, AbsoluteScale{AbsolutePixelWidth/Height, NaturalWidth/Height}, RelativeScale{Width/HeightPercent}, ScaleToFillArea, PreventDownscaling, PreventUpscaling}` | nur `Absolute` implementiert; Flags/Relative werden **ignoriert** | `-scale WxH!` |
| `RotateStep{RotationDirection CW/CCW, RotationDegrees}` | | `-rotate ±deg` |
| `BlurStep{BlurRadiusInPixels, StandardLaplaceDeviationInPixels}` | | `-blur rxσ` |
| `SharpenStep{...}` | | `-sharpen rxσ` |
| `OverlayDefinitions[]{OverlayDataId, ImageDefinition, ImageTransformationDefinition, AnchorPixelOffsetX/Y, SourceImageX/YAxisAnchor, OverlayImageX/YAxisAnchor}` | Overlay wird geladen, transformiert, Offset per `OverlayOffsetLogic` (Anker Quelle + Anker Overlay + Pixel-Offset) | `<src>[0] <overlay> -geometry +x+y -composite` (**Auffälligkeit**: Code hängt `file.TempFilePath` statt `overlayFile` an – vermutlich Bug) |
| `OutputImageQualityDefinition` (`OutputImageDefinition`) | + `ImageType`, `ImageQuality` | `-set colorspace X -type Y -quality q (Default 75) -density dpi -units PixelsPerInch -profile <name>` |

Bildgröße wird vorher via ExifTool (`image_width/height`) ermittelt und in jeden Step (`OriginalWidth/Height`) geschrieben.

### 3.6 Metadaten-Extraktion

| Extractor | Bedingung | Tool/Aufruf |
|---|---|---|
| `ImageMetadataExtractor` | `AssetType == "image"` und MimeType ≠ `application/postscript` | `ExiftoolWrapper` (Long-running `exiftool -fast -m -q -q -stay_open True -@ - -common_args -d "%Y.%m.%d %H:%M:%S" -s -t`; Kommandos über stdin + `-execute`, Ausgabe `Tag\tWert` bis `{ready}`; ein Semaphore = seriell; Timeout 10 s (`DerivationCommandsExecutionTimeoutInMilliseconds`, als Feld hart codiert); Neustart bei Fehler) |
| `AudioVideoMetadataExtractor` | `AssetType == "movie"` oder `"audio"` | `ffprobe -loglevel error -print_format json -show_format -show_streams "<file>"`; erster Audio- und Video-Stream + `format` |

Mapping-Konfiguration `Processing.MetadataExtraction.Directories[{Name, Mappings[{FromTag, ToPropertyDefinitionIdentifier, MergeMultipleOccurrences}]}]` – erste Übereinstimmung gewinnt. Verzeichnisse `iptc`, `exif`, `xmp`, `file` → ExifTool; `audio`, `audio_format`, `video`, `video_format` → ffprobe (JSON-Pfad, z. B. `tags.title`). Default-Mappings:

| Directory | FromTag → Property-Definition |
|---|---|
| iptc | Credit→iptc_credit, Contact→iptc_contact, Keywords→iptc_keywords (merge), City→iptc_city, Sub-location→iptc_sublocation, Province-State→iptc_province_state, Country-PrimaryLocationName→iptc_country_name, Country-PrimaryLocationCode→iptc_country_code, Caption-Abstract→iptc_caption, Headline→iptc_headline, DateTimeCreated/DateTimeOriginal→iptc_datecreated, ObjectName→iptc_objectname, Category→iptc_category, SupplementalCategories→iptc_supplemental_category, Source→iptc_source, Writer-Editor→iptc_writer_editor, CopyrightNotice→iptc_copyright_notice |
| exif | Model→exif_model, Make→exif_make, Flash→exif_flash, ExposureTime→exif_exposure_time, ExposureProgram→exif_exposure_program, ModifyDate→exif_datetime, FocalLength→exif_focal_length, ApertureValue→exif_aperture_value, MeteringMode→exif_metering_mode, SubjectDistance→exif_subject_distance, ISO→exif_iso_speed_ratings, ExposureCompensation→exif_exposure_bias_value, WhiteBalance→exif_white_balance, OwnerName→exif_owner_name, LensMake→exif_lens |
| file | PixelsPerMeterX/XResolution→image_horizontal_resolution, PixelsPerMeterY/YResolution→image_vertical_resolution, ImageWidth→image_width, ImageHeight→image_height, ColorSpace/DeviceModelDesc/ColorSpaceData→image_color_space, BitsPerSample/BitsPerPixel/BitDepth→image_bit_depth, ImageWidthAt300Dpi→image_width_at_300dpi, ImageHeightAt300Dpi→image_height_at_300dpi (berechnet), Compression→image_compression_type, Quality→image_compression_quality, FileType→image_file_format, ProfileDescription→image_icc_profile |
| audio | codec_name, codec_long_name, sample_rate, channels, duration, bit_rate → audio_* |
| audio_format | format_name, format_long_name, tags.title, tags.artist, tags.publisher, tags.album, tags.genre, tags.date → audio_format_name, audio_format_long_name, audio_title, audio_artist, audio_publisher, audio_album, audio_genre, audio_recording_date |
| video | codec_name, codec_long_name, width, height, pix_fmt, avg_frame_rate, duration, bit_rate → video_* |
| video_format | format_name, format_long_name → video_format_name, video_format_long_name |
| xmp | Default leer; Doku-Beispiel `DocumentID→xmp_documentid` |

Nachbearbeitung (`ImageMetadataService`): Auflösung PixelsPerMeter → /100, Dezimalpunkt→Komma; `image_file_format` erstes Wort, Groß-/Kleinschreibung; `exif_exposure_program` (Not Defined=0 … Landscape=8), `exif_white_balance` (Auto=0, Manual=1), `exif_metering_mode` (Unknown=0 … Other=7) → numerische Codes; `exif_focal_length` nur Zahl; `exif_datetime` `YYYY.MM.DD HH:MM:SS` → `YYYY-MM-DD HH:MM:SS`; `iptc_datecreated` → `YYYY-MM-DD`; `exif_subject_distance` m → cm; `image_color_space`/`image_icc_profile` enthält "sRGB" oder "Calibrated RGB" → `sRGB`; `Uncalibrated` → ExifTool `ColorMode`; fehlender Farbraum → `sRGB`; `Deflate/Inflate` → `Zip`; berechnet `image_width_at_300dpi = px·2.54/300` (cm, 4 Dezimalen), `image_compression_quality` Default `0`. Audio/Video: `avg_frame_rate` Bruch → Dezimal (2 Stellen), `duration` Sekunden → `TimeSpan`-String.

### 3.7 Konfiguration (`Processing.*`, `ProcessingConfiguration`)

| Key | Default / Bedeutung |
|---|---|
| `Logging`, `Hosting`, `InternalCallbackJob{ExecutionLoopSleepTimeSeconds 10, RetryJobStartDelaySeconds 3, Receiver-/SenderMessageBrokerConnectionIdentifier}` | Querschnitt (§2) |
| `Authentication{IsEnabled, IsApiDocumentationAuthenticationEnabled, WebClientId sharedien-web, ExternalServiceUrl, ServiceUrl, ApiClientSecret, RedirectUrl}` | Identity-Anbindung (nur Swagger/OIDC) |
| `MessageBroker.AMQP{IsEnabled, Protocol, ServerAddress, ServerPort, UserName, Password, Senders[], Receivers[]}` / `MessageBroker.AzureServiceBus{IsEnabled, ConnectionString, Senders[], Receivers[]}` | §12 |
| `JobReader.MessageBrokerConnectionIdentifier`, `JobCallback.MessageBrokerConnectionIdentifier` | s. o. |
| `StorageProviders.StorageHandlerStorageProviders[{Identifier, IsEnabled, StorageHandlerApiUrl, AccessKey}]` / `AzureBlobStorageProviders[{IsEnabled, ConnectionString, AccountKey, ContainerName}]` | §4 |
| `FileCache{CacheExpirationInSeconds 600, TempRootPath}` | Temp/Cache (Azure: `D:\home\site\temp`) |
| `ImageProfile.ImageProfilePath` (`ImageProfiles`) | unklar |
| `JobTimeoutInSeconds` | Klassen-Default 60, appsettings 86400 |
| `MetadataExtraction.Directories[]` | s. 3.6 |
| `CustomDerivation{ExecutablePaths{ImageMagickConvertExecutable, ImageMagickIdentifyExecutable, FFMpegExecutable, FFProbeExecutable, LibreOfficeExecutable, GhostScriptExecutable, ExifToolExecutable, BlenderExecutable}, ColorProfilesPath 'ColorProfiles\', IccProfileOverrides[{SourceProfile, TargetProfile}], ColorSpaceDefaultProfiles[{SourceColorSpace, UseColorProfile}], FallbackSourceProfile '', IccProfileKey 'image_icc_profile', ColorSpaceKey 'image_color_space'}` | s. 3.3 |
| `ConnectionStrings.Sharedien` | für SQL-Config-Source (ab 3.11.0) |

### 3.8 Docker-Image / Tool-Versionen

`docker/_build/Processing/Dockerfile`: Basis `mcr.microsoft.com/dotnet/core/aspnet:3.1` (Debian); `apt-get install ghostscript ffmpeg libreoffice blender libimage-exiftool-perl perl-doc` (Distro-Versionen, nicht gepinnt → konkrete Versionen **unklar**); ImageMagick **7.0.10-30** aus Quellen (`./configure --with-gslib=yes --with-rsvg=yes`, mit libraw, libwebp, libgs) → `/usr/local/bin/magick`. Lizenzhinweis im Compose (Ghostscript AGPL, LibreOffice, ffmpeg). Variante `Dockerfile-no-deps` ohne Tools. Windows: ImageMagick portable 7.0.10-30 (Update 3.7.2), ExifTool (ab 3.6.0), Blender (ab 3.9.0), ffmpeg/Ghostscript/LibreOfficePortable (Versionen unklar).

### 3.9 Tests (`ProcessingTests`)

`CustomDerivationTests`, `ImageMagickDerivationTests`, `ImageMetadataExtractorTests` (Referenz-Set `metadata-extractor-images`: eps, jpg Canon EOS-1D/Nokia Lumia, png, tif mit erwarteten `.txt`-Ausgaben), `ExifToolWrapperTests`, `CachedStorageHandlerFileServiceTest`, `OverlayLogicTests`, `MapperTests`; Testdaten `alfa_romeo.jpeg`, `HOME.docx`, `big_buck_bunny.webm`, `text_file.txt`; Fakes für Storage-Provider, Config, StepProcessorFactory/PathResolver, ImageMetadataService. Konfiguration `appsettings.Tests.yml`.

---

## 4. StorageHandler + Storage-Abstraktion

Pfad: `src/StorageHandler`, `src/StorageHandler.Common`, `src/StorageHandler.Contracts`, `src/Backend.Common/Sharedien.Common.Infrastructure.Storage`. Readme: "microservice that allows applications to store binary data in different configurable storages, while working against a common abstraction layer with a consistent API".

### 4.1 Architektur

| Projekt | Inhalt |
|---|---|
| `StorageHandler.Api` | Web-Host (netcoreapp3.1; NuGet AppInsights 2.15.0, Autofac 7.0.1). `web.config` für IIS: `maxAllowedContentLength 2147483648` (2 GB), `requestTimeout 23:00:00`, `startupTimeLimit 3600`. |
| `StorageHandler.Api.WindowsService` | HTTP.sys-Service ("Sharedien3 StorageHandler", 5000/4000) |
| `StorageHandler.Core` | `IStorageHandlerService`/`StorageHandlerService` (Fachlogik), `IDataRepository`, Modelle `Data`, `DataInfo`, `DataResult` |
| `StorageHandler.Infrastructure` | `DataRepository` (Dapper, MERGE-Upsert), `ContainerFactory` (Autofac), `DbNames` |
| `StorageHandler.DbMigration` | DbUp: eingebettete Skripte `{yyyyMMdd}_{nnnn}_*.sql`, Journal `dbo.SchemaVersionsStorageHandler`, exklusiver `sp_getapplock 'storage_handler_db_migration'` (300 s) gegen parallele Migration, DB-Anlage (`CreateDatabaseIfNotExists`, Collation `Latin1_General_CI_AS`, Setup-Skript `SetupSqlServerDb.sql` bzw. `SetupAzureDb.sql`); läuft beim Start des API (`Startup.Configure`) |
| `StorageHandler.Database` | SSDT-Projekt (Schema `STH`, Tabellen Chunk/Data/Session/Storage) – **veralteter Stand** gegenüber Migrationen |
| `StorageHandler.Infrastructure.Tests` | `StorageHandlerSignedUrlProviderTests`, `TestStartup` + `appsettings.IntegrationTests.yml` |
| `StorageHandler.Common` | **Legacy-Client** (netstandard2.0, NuGet `Applicationbase.Core 1.0.2-CI-20181010`, WebUtilities 2.1.1, Http 2.1.1, Newtonsoft 11): `StorageHandlerClient` mit Endpoints `api/v1/data/upload/chunks/start`, `.../sessions/{id}/chunkSequenceNumbers/{n}`, `.../finish`, `.../syncfinish`, `api/v1/data/signed/{id}?expires=&signature=`; DTOs `StartUploadResponseDto`, `AppendChunkResponseDto`, `FinishUploadSessionDto{CallbackBrokerName, DataMetaInformation, InitiatorProvidedMetaData}`, `FinishUploadSyncResponseDto{DataId, DataHash, HashAlgorithm, HashFormat, DataSize}`, `ArchiveExtractionResponseDto{Entries[{EntryType Folder/File, EntryStatus Extracted/Filtered/Error/Nothing, Path, DataIdentifier, Message}]}`, `DefaultDataMetaInformationKeys` (FileName, FileExtension, MimeType), MQ-Nachrichten `StartFileCompletionJob`/`FileCompletionJobFinished`, `IPreSignedUrlService`. **Diese Endpoints existieren im heutigen `DataController` nicht mehr** – Session-Upload wurde ins Backend verlagert. |
| `StorageHandler.Contracts` | `CopyDataDto{DataIdentifier}`, `DeleteDataDto{DataIdentifiers[]}`, `FinishUploadDto{ChunkDataIdentifiers[], ContentType}` |

Startup (`StorageHandler.Api/Startup.cs`): Config (+ SQL-Config-Source Prefix `StorageHandler`), Serilog, App Insights, **CORS** (`AllowAnyOrigin`; Header Content-Type, Authorization, Cache-Control, Expires, Pragma, If-Modified-Since; exposed ETag, Last-Modified, Content-MD5, Content-Type, Content-Length; Methoden GET/HEAD/PUT/PATCH/POST/DELETE; Preflight-Max-Age 600 s – nötig für Browser-Direktupload), Auth-Schemes `AccessKey` (Default) + `SignedUrl` + OIDC (Swagger), MVC/Versioning/Swagger, Autofac (`RegisterMessageBroker` nur für internen Topic/Config-Reload, `RegisterInternalCallbackJob`, `ExtendableIdentifierGeneratorService`, `DataRepository`, `StorageHandlerService`, `StorageProviderDependencies`), Broker-Init, DB-Migration, OperationId-Middleware, `MapControllers().RequireAuthorization()`.

### 4.2 Authentifizierung

| Scheme | Mechanik |
|---|---|
| `AccessKey` (`StorageHandlerAccessKeyAuthenticationHandler`) | Header `Authorization: AccessKey <Base64(Key)>`; Vergleich mit `Authentication.AccessKeys[{Name, Key}]`; Identity-Name = Key-Name. Wird von API/Processing (`StorageHandlerStorageProvider`) benutzt. |
| `SignedUrl` (`StorageHandlerSignedUrlAuthenticationHandler` + `StorageHandlerSignedUrlProvider` + `SignedUrlProvider`) | Query-Parameter `comp` (`c` Chunk / `f` File), `sp` (Permissions `r`,`w`,`d`), `se` (Ablauf UTC `yyyy-MM-ddTHH:mm:ss.fffZ`), optional `rsct` (Content-Type), `rscd` (Content-Disposition), `sig`. Signatur = `Base64(SHA256(SigningKey + AbsolutePath + Werte der relevanten Parameter, sortiert nach Key absteigend ordinal))` – **kein HMAC, einfache SHA-256-Konkatenation**. Prüfung: Signatur → Ablauf → benötigte Permission je HTTP-Verb (GET/HEAD `r`, PUT `w`, DELETE `d`). Ergebnisse `Valid, MissingSignature, SignatureMismatch, Expired, PermissionMismatch`. Key: `StorageHandler.SharedAccessSigningKey` = API `StorageProviders.StorageHandlerStorageProviders[].SharedAccessSigningKey`. |
| OIDC | nur Swagger-UI-Schutz |

### 4.3 REST-API (`api/v{1..6}/data`, `DataController`)

| Verb & Route | Auth | Verhalten |
|---|---|---|
| `GET /{dataIdentifier}?rsct=&rscd=` | AccessKey/SignedUrl | Datei streamen; `rsct` überschreibt Content-Type (Default gespeicherter MIME oder `application/octet-stream`), `rscd` Content-Disposition (Default `inline`); `If-None-Match` → 304; `ResponseCache Any, 6 h`; Range-Requests (`File(..., enableRangeProcessing: true)` → 206) |
| `HEAD /{dataIdentifier}` | beide | nur Header (inkl. Content-Length) |
| `GET /{dataIdentifier}/chunks` | beide | Liste der Chunk-Identifier (Resume-Unterstützung) |
| `PUT /{dataIdentifier}` | beide (`w`) | Roh-Body als Datei speichern (`RequestSizeLimit 1 000 000 000`), 201 |
| `PUT /{dataIdentifier}/{chunkDataIdentifier}` | beide (`w`) | Chunk speichern (Data-Zeile `IsChunk=1`, `MasterDataIdentifier`), 201 |
| `PUT /{dataIdentifier}/finish` | **nur AccessKey** | `FinishUploadDto{ChunkDataIdentifiers[], ContentType}` → Chunks in Reihenfolge der Liste zusammenfügen, unbekannte Chunks → 400, nicht genannte Chunks werden gelöscht, Data-Zeile anlegen, 201 |
| `POST /{dataIdentifier}/copy` | beide | `CopyDataDto{DataIdentifier}` → Storage-Copy + DB-Zeile kopieren (nur Nicht-Chunks), 201 |
| `DELETE /{dataIdentifier}` | beide (`d`) | 202 / 404 |
| `DELETE /{dataIdentifier}/{chunkDataIdentifier}` | beide | 202 |
| `POST /bulk-delete` | beide | `DeleteDataDto{DataIdentifiers[]}` → 202 |
| `GET /api/v1/info` | anonym | Version |

Antwort-Header bei allen Schreib-/Lese-Operationen: `ETag` (quoted), `Content-MD5` (Base64), `Content-Type`, `Last-Modified` (RFC1123), `Content-Length` (nur HEAD), `X-Content-Length` (immer). Fehler: `FileNotFound/DirectoryNotFound` → 404, `IOException`/sonst → 400.

### 4.4 Upload-Protokoll End-to-End (API ↔ Client ↔ Storage)

Doku `integrations/uploading-assets-sharedien.rst` + `Sharedien.Api/Controllers/AssetUploadController.cs`:

1. `POST /api/v3/assets/upload/start` (Bearer-JWT) mit `UploadStartRequestDto{assetUploadType: Asset|AssetDerivation|AssetVersion, fileName, asset{parentCategoryIdentifier, workspaceIdentifier?, taskIdentifier?, extractCreatedArchiveAsset{archiveExtractionMode, deleteAssetAfterExtraction}?}, assetDerivation{assetSetIdentifier, derivationTypeIdentifier, assetVersionIdentifier?}, assetVersion{assetSetIdentifier}}` → Rechteprüfung, neue DataId, Antwort `UploadStartResponseDto{chunkUploadRequestUrl, maxChunkSizeBytes}`. Die URL ist mit `Upload.UploadSigningKey` signiert (`UploadSignedUrlProvider`, Parameter `ut` UploadType, `ai` AssetSet, `wi` Workspace, `di` DataId, `se` Expires, `fn` FileName, `pe` ParentEntity, `le` LinkEntity, `dt` DerivationType, `av` AssetVersion, `aem` ArchiveExtractionMode, `aed` DeleteAfterExtraction, `sig`).
2. `GET /api/v3/assets/upload/chunk?...` (signiert) → `ChunkUploadResponseDto{uploadUrl, nextChunkUploadRequestUrl, finishUploadRequestUrl, chunkIdentifier…}`; `uploadUrl` = `IStorageProvider.GetChunkUploadAccessUrl` → StorageHandler `PUT {SharedAccessUrl}/api/v1/data/{id}/{chunkId}?comp=c&sp=rw&se=…&sig=…` bzw. Azure Block-Blob-SAS `?comp=block&blockid=<base64>&<sas>`.
3. Client `PUT` Chunk direkt zum Storage (Browser → CORS). Chunks sind unabhängig (parallel möglich), keine Sequenznummer im Storage – die Reihenfolge wird beim Finish durch die ID-Liste bestimmt. Resume: `GET {id}/chunks`.
4. `PUT /api/v3/assets/upload/finish` (signiert) mit `UploadFinishRequestDto{chunkIdentifiers[]}` → `CombineChunksToFileAsync` (StorageHandler `PUT {id}/finish` per AccessKey bzw. Azure `CommitBlockList`) → Asset/Version/Derivation anlegen, Metadaten-Extraktion + Derivate über Processing-Queue, optional Archiv-Extraktion (`ArchiveExtractionMode`: `ExtractWithFullPath`, `ExtractFilesOnly`, `NoExtraction`; `deleteAssetAfterExtraction`).
- Ablaufzeiten: `Sharedien.Upload.RequestChunkUploadMinimumExpiryTimeInMinutes` (60), `FinishUploadMinimumExpiryTimeInMinutes` (1440).
- Max. Chunk-Größe: `IStorageProvider.MaxChunkSizeBytes` (Basis 100 MB, FileSystem `int.MaxValue`).
- Legacy (API v≤6, `[Obsolete("Remove in API v7…")]`): sessionbasierte Endpoints `POST /assets/upload/chunks/start`, `POST .../sessions/{id}/sequencenumber/{n}`, `DELETE .../sessions/{id}`, `POST .../sessions/{id}/finish` (auch für Versionen, Derivationen, Workspaces) – Chunks laufen dann durch die API; Tabelle `UploadSession` im Backend (Migration 0185), `UploadSessions.MessageCleanup`.
- Kein tus-Protokoll, keine Deduplizierung (Identifier = GUID; Orphan-Erkennung nur per Skript `Storage_GetOrphans.sql`).

Hashing: MD5 wird beim Schreiben im Stream berechnet (`HashCalculationStream`), Base64-kodiert als `Content-MD5`/`FileHash` gespeichert und im Backend als Metadaten-Property des Assets abgelegt (Doku `metadata/file-hash.rst`); Suchindex-Feld `FileHashes` erlaubt Suche nach Hash (Duplikat-Erkennung im UI **unklar**). FileSystem-ETag = MD5-Base64; Azure-ETag = Blob-ETag.

### 4.5 Datenbank `Sharedien3StorageHandler` (Migrationen 0001–0005)

| Tabelle (Schema `STH`) | Spalten |
|---|---|
| `Data` | `DataId bigint identity PK`, `Identifier nvarchar(255) unique`, `FileHash nvarchar(850)`, `FileSize bigint default 0`, `StorageId bigint FK→Storage`, `MimeType nvarchar(850)`, `MutDate STH.utMutDate (datetime2, default GETUTCDATE)`, `LastModifiedAt datetime2 not null default GETUTCDATE` (0004), `IsChunk bit default 0` (0004), `MasterDataIdentifier nvarchar(255) null` (0005). Entfernt in 0004: `Name`, `Extension`, `HashAlgorithm` |
| `Storage` | `StorageId`, `Identifier unique` (Default-Zeile `'default'`), `ConfigurationType int`, `Configuration nvarchar(max)`, `ConfigurationVersion int`, `MutDate` – nur die Zeile `default` wird genutzt (`_storageIdentifier = "default"`) |
| `ChunkObsolete` (ehem. `Chunk`) | `Identifier`, `ChunkDataId`, `ChunkSequenceNumber`, `SessionId`, `StorageId` – obsolet, nur wegen Orphan-Cleanup behalten |
| `Session` | in 0004 gelöscht |

Setup-Skripte setzen DB-Optionen (Query Store, Full-Text enable falls installiert, vardecimal). Repository: Dapper; `CreateDataAsync` = `MERGE WITH (HOLDLOCK)` (Upsert), `GetChunksAsync` sortiert nach `LastModifiedAt`.

### 4.6 Storage-Abstraktion (`Sharedien.Common.Infrastructure.Storage`, NuGet Azure.Storage.Blobs 12.6.0, SharpZipLib 1.2.0)

`IStorageProvider`: `MaxChunkSizeBytes`, `Identifier`, `Type`, `UploadAsync(id, stream, contentType)`, `CombineFilesToFileAsync`, `UploadChunkAsync(chunkId, id, stream)`, `CombineChunksToFileAsync(chunkIds, id, contentType)`, `DeleteAsync(id | set)`, `DeleteChunkAsync`, `CopyAsync`, `ExistsAsync`, `GetFileInfoAsync`, `RetrieveAsync`, `RetrieveRangeAsync(id, offset, length)`, `GetReadAccessUrl(id, expiresUtc, fileName, mimeType, asAttachment)`, `GetUploadAccessUrl`, `GetChunkUploadAccessUrl`, `GetDeleteAccessUrl`, `GenerateIdentifier()` (GUID). `StorageFileInfo{Identifier, ContentHash (Base64 MD5), ETag, LastModified, ContentLength, ContentType}`.

| Provider | Details |
|---|---|
| `FileSystemStorageProvider` | Pfad `StorageRootPath/<id[0..2]>/<id[0..4]>/<id>` (`FilenameStartLocationStrategy`); Retry `DataAccessRetryCount 3` × `DataAccessRetryDelayMilliseconds 5000`; MD5 beim Schreiben; Chunk-Kombination via `MultiStream` → neue Datei, Chunks löschen; `RetrieveRange` kopiert in MemoryStream; Signed URLs `NotSupported`; `MaxChunkSizeBytes = int.MaxValue`. Nur im StorageHandler selbst aktiv. |
| `AzureBlobStorageProvider` | `BlobServiceClient(ConnectionString)`, Container `ContainerName`, `StorageSharedKeyCredential(AccountKey)`; Upload mit MD5 + `SetHttpHeaders(ContentHash, ContentType)`; Chunks = `StageBlock(base64(chunkId))`, Combine = `CommitBlockList` + **erneuter Download zur MD5-Berechnung**; `CombineFiles` = Download+MultiStream+Upload+Delete; Copy = `StartCopyFromUri`; Ranges nativ; SAS-URLs (Read mit Content-Disposition/-Type, Write, Chunk `comp=block&blockid=`, Delete; HTTPS-only). Azure-Blob-CORS muss PUT erlauben (Update-Notes 3.6.0). |
| `StorageHandlerStorageProvider` | HttpClient mit `Authorization: AccessKey`; Basis `StorageHandlerApiUrl/api/v1/data`; Timeouts `UploadTimeout 120 s`, `FinishUploadTimeout 120 s`, `DeleteTimeout 10 s` (× Anzahl bei Bulk), `ReadTimeout 120 s`; Range via `Range`-Header; Signed URLs auf `SharedAccessStorageHandlerApiUrl` mit `SharedAccessSigningKey`; `StorageFileInfo` aus Antwort-Headern. |

Aktiver Provider: `StorageProvidersConfiguration.ActiveStorageProviderConfiguration` = erster mit `IsEnabled` in Reihenfolge AzureBlob → StorageHandler → FileSystem (**genau ein** Provider pro Dienst). Azure-Deployments nutzen Azure Blob direkt (StorageHandler entfällt), On-Premise StorageHandler+FileSystem.

Archive (`Extraction/*`): `IZipArchiveProvider.ExtractArchiveAsync(stream) → IAsyncEnumerable<ArchiveEntry{EntryType Unknown/Folder/File, Path, FileName, Stream}>` und `CreateArchiveAsync(stream, items[{FileName, Size, State}], callback)`; Implementierungen `NativeZipProvider` (System.IO.Compression, `NoCompression`) und `SharpZipLibZipProvider` (streaming, `Stored`, Zip64 nur wenn alle Größen bekannt, Unicode-Namen, Level 1). Verwendung im Backend: Archiv-Extraktion beim Upload/`ExtractionsController`, ZIP-Download (`FileTransferBaseController`, `PublicController`), CSV-Export. Welche Implementierung registriert ist: **unklar** (Backend-DI nicht analysiert). Nur ZIP – keine RAR/7z/TAR.

Streams: `HashCalculationStream` (Read-Through-Hash), `HashCalculationStreamWrite`, `MultiStream` (Konkatenation), `NonDisposableStream`.

### 4.7 Konfiguration (`StorageHandler.*`)

| Key | Default / Bedeutung |
|---|---|
| `Logging`, `Hosting`, `InternalCallbackJob` | Querschnitt |
| `Authentication.{IsEnabled, IsApiDocumentationAuthenticationEnabled, WebClientId, ExternalServiceUrl, ServiceUrl, ApiClientId/Secret, Scopes, RedirectUrl, ValidationCache, UserGroupCache, RequestTimeout}` + `AccessKeys[{Name, Key}]` | Auth |
| `Database.{CreateDatabaseIfNotExists false, DbSetupScriptName 'SetupSqlServerDb.sql', DbMigrationTimeout 60, UseTrustedConnection, ThrowOnClientEvaluation}` | DB |
| `StorageProviders.FileSystemStorageProviders[{Identifier, IsEnabled, StorageRootPath 'Upload', DataAccessRetryCount, DataAccessRetryDelayMilliseconds}]` (bzw. AzureBlob) | Storage |
| `SharedAccessSigningKey` | Signed URLs |
| `MessageBroker.{AMQP|AzureServiceBus}.Receivers[sharedien_storagehandler_internal_receiver → topic://VirtualTopic.Sharedien.Internal / Advellence.Sharedien.Internal/Subscriptions/StorageHandler]` | nur interner Topic |
| `ConnectionStrings.StorageHandler`, `ConnectionStrings.Sharedien` | eigene DB + Sharedien-DB (SQL-Config) |
| Legacy-Keys in älterer Doku: `StorageConfigurationType FileStorage|AzureBlobStorage`, `FileStorage.StorageRootPath`, `AzureBlobStorage.{ContainerName, AccountName, AccountKey, EndpointSuffix}`, `PreSignedUrl.Signing.SigningKey` | bis 3.5.x |

### 4.8 Deployment

Docker `sharedien/storagehandler` (aspnet:3.1), Volume `/uploads`; Windows-Service 5000/4000; IIS möglich (web.config). Reverse Proxy: Pfad `/storage-handler/` (Doku-Beispiel), muss `X-Forwarded-*` setzen; `SharedAccessStorageHandlerApiUrl` = extern erreichbare URL für Browser-Uploads/Downloads.

---

## 5. Search (Elasticsearch)

Pfad: `src/Search`. Seit 3.7.0 **Bibliothek im API-Prozess** (`SearchModule` wird vom Backend geladen; Config-Section `Sharedien.Search`), davor eigener Dienst `Sharedien.Search.Api` (Port 9201, Dockerfile noch vorhanden, Projekt nicht mehr im Repo).

### 5.1 Architektur

| Projekt | Inhalt |
|---|---|
| `Sharedien.Search.Core` | Contracts (`IAssetSearchService`, `IEntitySearchService`, `IIndexingService`, `IReIndexingService`, `IIndexNameResolverService`, `ITransformationService`, `ILifeCycleManaged`), Modelle (Suchobjekte, Queries, Facetten, MQ-DTOs, Konfiguration), `TransformationService` (AutoMapper DTO→Dokument) |
| `Sharedien.Search.Infrastructure` | `SearchModule` (Autofac), `SearchProfile` (AutoMapper), `SearchCallbackJob` (Queue-Consumer); referenziert `Sharedien.Api.Core` und Queue |
| `Sharedien.Search.Infrastructure.Elasticsearch` | NEST **7.10.0** + NEST.JsonNetSerializer 7.10.0; `ElasticsearchClient`, Mapping, Services |
| `Sharedien.Search.Tests` | Unit- und Integrationstests (echtes ES; `ElasticsearchWikipedia.docx/.pdf/.pptx` für Textextraktion) |

`ElasticsearchClient` (`Connector/ElasticsearchClient.cs`): `SingleNodeConnectionPool(BaseUrl)`, Basic-Auth, **Server-Zertifikat wird nie validiert** (`AllowAll`), JsonNet-Serializer (`NullValueHandling.Ignore`), Feldnamen-Inferrer (Prefix `[PropDef]` entfernen, camelCase), `DefaultMappingFor` je Dokumenttyp (Alias-Namen), Request-Logging optional. `OnApplicationStartAsync` → `InitAsync`: Indizes `<name><IndexSuffix>` + Alias `<name><IndexAliasSuffix>` für `asset`, `workspace`, `task`, `category`, `custom`, `billing-contact`, `rights-owner`, `license`, `license-purchase` anlegen (nur wenn Alias fehlt), ES-Version lesen, Ingest-Pipeline `attachment` anlegen (Processor `attachment`: Feld `attachmentContent` → `attachment`, `indexed_chars -1`; anschließend `remove attachmentContent`). Voraussetzung: **Plugin `ingest-attachment`** (Readme; Installation im Docker-Compose nicht enthalten → **unklar** wie in Docker bereitgestellt).

Index-Settings (`CreateIndexIfNotThereAsync`): Tokenizer `edge_ngram_tokenizer` (min/max aus AutoComplete-Config, TokenChars letter+digit); Analyzer `custom_pattern` (Pattern `\W|_`, für Dateinamen) und `edge_ngram_analyzer` (+ lowercase); Normalizer `custom_keyword_normalizer` (lowercase); `index.mapping.total_fields.limit = int.MaxValue`. Alias-Regel: genau **ein** Index pro Alias (sonst Exception).

### 5.2 Indizes und Dokumente

Entity-Identifier-Format im Index: `<EntityType>__<Identifier>` (`EntityIdentifierHelper`). Typ-Mapping (`EntityMapping`): AssetSet/Version → `Asset`; Workspace, Task (`SearchTask`), Category, BillingContact, RightsOwner, License, LicensePurchase → eigene Klassen; alles andere → `CustomEntity` (Index `custom`, Id = `EntityIdentifier`).

| Dokument | Felder (statisch) |
|---|---|
| `Asset` (Index `asset`) | `identifier` (keyword, Id), `created` (date), `assetTypeIdentifier`, `fileName` (text; Subfelder `tokenized` = Analyzer `custom_pattern`, `exact` = keyword), `parentCategoryIdentifier` (keyword, `Category__<id>`), `property` (Object mit dynamischen Property-Feldern), `attachmentContent` (transient, Base64), `attachment` (Ingest-Ergebnis: author, content, content_length, content_type, date, detect_language, indexed_characters, keywords, language, name, title), `fileHashes` (keyword[]), `categoryPaths` (nested `Path{directParent, fullPath[], principalIdentifiers[]}` – mehrere Pfade, da Assets in mehreren Kategorien verlinkt sein können), `version` (long), `relatedEntities` (nested Baum: `identifier`, `entityIdentifier`, `entityTypeIdentifier`, `property{…}`, `children` – Tiefe = max. Tiefe des `NodeTypeTree`, `include_in_root`) |
| `BaseEntity` (übrige Indizes) | `identifier`, `entityIdentifier`, `entityTypeIdentifier` (keyword), `path` (nested Path), `property`; `LicensePurchase` zusätzlich `includedEntityIdentifiers` (keyword[]) |

Dynamische Property-Felder (`PropertyMapping.MapPropertyType`, aus `PropertyDefinitionChangeDto`; nur `Active && (FullTextField || AdvancedSearchTextField)`):

| `PropertyValueType` | ES-Mapping unter `property.<id>` |
|---|---|
| String | keyword (index=false) mit Subfeldern `exact` (keyword, Normalizer lowercase), `autocomplete` (text, Index-Analyzer edge_ngram, Search-Analyzer standard), `tokenized` (text, standard) – `tokenized` entfällt bei `ExactField` |
| Text | text (index=false) mit denselben Subfeldern |
| Date / DateTime / Time | date (`date` / `date_time_no_millis` / `hour_minute_second||hour_minute_second_millis`) als `.exact` |
| Integer / TimeSpan | long (`.exact`; TimeSpan als Ticks) |
| Float | float (`.exact`) |
| Decimal | double (`.exact`) |
| Boolean | boolean (`.exact`) |
| `HasListValues` | zusätzlich `property.<id>_identifiers_` (keyword) mit Listenwert-Identifiern |
| `Translatable` | Object; Dynamic Template pro Sprache: `_<iso>*` → Subfelder `exact`, `tokenized` (Sprach-Analyzer, z. B. `german`), `autocomplete`; Sprachen aus Enum `Language` (Sharedien.Common) via `CultureInfo` → ISO-2; Fallback `""` → `english` |

Werte-Transformation (`SearchProfile.MapProperties`): Werte > 10 922 Zeichen werden gekürzt (ES-Limit 32 766 Bytes); Boolean lowercase; TimeSpan → Ticks; sprachunabhängig (Key `""`) vs. sprachabhängig (`_de`, `_en` …); Einzel- vs. Mehrfachwerte; Dynamic Template `parentCategoryIdentifiers` (`categoryPaths.fullPath` → keyword).

### 5.3 Indexierung (Events aus dem Backend)

`SearchCallbackJob` (`BackgroundJobBase`; Receiver `Search.SearchCallbackJob.MessageBrokerConnectionIdentifier`, Quelle laut Update-Notes `Advellence.Sharedien.Search`; Nachrichtentyp aus `MessageQueueEventProperties.TypeName`):

| DTO | Felder | Verarbeitung |
|---|---|---|
| `AssetChangeDto` | `Identifier, Created, AssetTypeIdentifier, FileName, Paths[PathDto{Path[EntityIdentifierDto], PrincipalIdentifiers[]}], ParentCategoryIdentifier, Properties[PropertyValueChangeDto{Identifier, PropertyDefinitionIdentifier, ValueType, Values{lang→[…]}, ValueIdentifiers[]}], FileHashes[], Version, RelationEntities[{Identifier, Properties, Children}], EventAction` | `IndexAction`: Create/FullUpdate → Index; PartialUpdate → Update-Doc (`retry_on_conflict 3`); PartialUpdateFullProperties → Painless `ctx._source.property = params.properties`; Delete |
| `EntityChangeDto` | `EntityIdentifier, Path, Properties, IsAssetSearchRelated, CustomFields{IncludedEntities}` | eigener Index je Typ (Refresh `wait_for`); bei `IsAssetSearchRelated` (Relations-Entität) → Painless-Update der nested `relatedEntities[...].property` in allen Assets entlang des Pfads bzw. Entfernen bei Delete |
| `EntityMoveDto` | `MovedEntityIdentifier, PrincipalsToRemove, InterruptingChildren, TargetPath, RestrictionsToAdd/Remove[AssetRestrictionDto{PrincipalIdentifier, RestrictedProperties{propDef→[values]}}], ChildNodeTypeIdentifiers` | `update_by_query` mit Painless: `fullPath` umschreiben, Principals entfernen/hinzufügen (außer bei "Interruptions" = Kind-Knoten mit eigenen Rechten), Asset-Restrictions (Principal entfernen, wenn Property-Wert eingeschränkt); Assets selbst dürfen nicht "gemoved" werden (Exception) |
| `PropertyDefinitionChangeDto` | `Identifier, ValueType, HasListValues, AllowMultipleValues, Active, Translatable, FullTextField, AdvancedSearchTextField, ExactField, EntityTypeIdentifiers[], RelatedNodeTypes[NodeTypeTree]` | Put-Mapping auf aktivem Index je Entitätstyp (Assets inkl. Relations-Baum) |
| `PermissionUpdateDto` | `EntityIdentifier, PrincipalIdentifier, InterruptingEntities[], AffectedEntityTypes, EventAction Create/Delete` | `update_by_query`: Principal in `path/categoryPaths.principalIdentifiers` aller Dokumente unterhalb (fullPath contains) hinzufügen/entfernen, außer bei Interruptions; Timeout `UpdateQueryTimeoutInSeconds` |
| `BinaryDocumentExtractionDto` | `EntityIdentifier, DataId` | Datei via `IStorageProvider.RetrieveAsync` streamen (Timeout `DownloadTimeoutInSeconds`), Base64 direkt in `update_by_query` mit Pipeline `attachment` (Tika-Extraktion in ES, `conflicts=proceed`), einmaliger Retry bei Versionskonflikt; nur für AssetSet. Welche Dateitypen: Backend `MAM.FileType.EnableBinaryDocumentExtraction` |

Batch-DTOs für Reindex (`SearchBatchEventBase{MessageNumber, TotalMessages}`): `AssetChangeBatchDto{Assets[]}`, `EntityChangeBatchDto{EntityTypeIdentifier, Entities[], PropertyDefinitions[]}`, `PropertyDefinitionChangeBatchDto`, `BinaryDocumentBatchExtractionDto`; Steuerung `TriggerStartReIndexing`/`TriggerFinishReIndexing`/`CallbackFinishReIndexing{StartDate, EntityTypeIdentifiers}`.

Bulk: `BulkCreateAsync` (IndexMany, `BulkCreateException` mit fehlgeschlagenen Entitäten), `ReIndexAsync` (IndexMany in Reindex-Index). Fehler im Handler → Log + `MessageOutcome.Rejected` (kein internes Retry).

### 5.4 Query-Features

Query-Modell `BaseSearchQuery`: `EntityType`, `SearchTerm`, `Operator` (Or/And), `SearchInProperties[]`, `LanguageIdentifiers[]`, `ExactSearch`, `EntityFilters[]` + `RecursivelyFilterEntityIdentifiers`, `PropertyFilters[Filter]`, `UserAndGroupsVisible[]` (Principals des Nutzers), `Sort[{PropertyDefinitionIdentifier, Direction, LanguageIdentifier}]`, `FacetPropertyDefinitionIdentifiers[]`, `FacetFilters[{PropertyDefinitionIdentifier, Terms[], Operator}]`, `AutoComplete? (Exact|Fuzzy|ExactAndFuzzy)`, `Cursor`. `AssetSearchQuery` zusätzlich `Context`, `Filename`, `FileHash`, `SearchInDocumentExtraction`, `RelationQueries[RelationQuery{SearchInProperties, PropertyFilters, FacetFilters, FacetDefinitions[{PropertyDefinitionIdentifier, HasListItems}], ChildrenFilters[]}]`. `LicensePurchaseSearchQuery{IncludedEntities[]}`.

Aufbau der Asset-Suche (`AssetSearchService.SearchInternalAsync`), `bool`-Query:
- `should` (`minimum_should_match 1`, wenn SearchTerm/FileHash/Filename gesetzt): `term identifier = SearchTerm`; `term fileHashes = FileHash`; `match fileName = Filename`; `multi_match SearchTerm` über `fileName.exact^ExactTermBoost(1000)`, `fileName.tokenized^MultiTokenTermBoost(1)` (nicht bei ExactSearch), je `SearchInProperties` `property.<p>[._<lang>|._*].exact^1000`, `.tokenized^1`, `.autocomplete^0.5` (auch Relations-Felder) – Typ `cross_fields`+AND bei Operator And, sonst `best_fields`+OR; Dokumentinhalt (`SearchInDocumentExtraction`, nicht exact): `multi_match attachment.title^1.5, attachment.content^0.8` + `match_phrase attachment.content^5`; AutoComplete: `multi_match` auf `property.*.autocomplete` (bzw. gewählte Properties) exakt (AND, Boost 10) und/oder fuzzy (`AUTO`, Prefix 2).
- `filter`: **Berechtigungsfilter** nested `categoryPaths.principalIdentifiers ∈ UserAndGroupsVisible`; optional `fullPath` (rekursiv) bzw. `directParent` ∈ `EntityFilters`; Property-Filter; Facetten-Filter (`Equals` → terms `.exact`, `IdentifierEquals` → terms `_identifiers_`); Relations-Filter als nested Query auf `relatedEntities(.children)*` mit `entityTypeIdentifier`-Term. Negative Operatoren → `must_not`.

| Filter-`Operator` | ES-Umsetzung (`EntitySearchService.MapToQuery`) |
|---|---|
| HasValue / HasNoValue | `exists property.<p>.*` (bzw. je Sprache `.exact`) |
| Equals / NotEquals | `multi_match` auf `.exact` über Sprachfelder; mehrere Terms → `terms` |
| Contains / DoesNotContain | `wildcard *term*` auf `.exact` |
| FuzzyEquals | `fuzzy` (AUTO, prefix 1) auf `.exact` |
| FuzzyContains | `fuzzy` auf `.tokenized` |
| GreaterThanOrEquals / LessThanOrEquals / Between | `range` numerisch oder Datum (`DateMath`) auf `.exact`; Terms müssen alle numerisch oder Datum/Zeit sein |
| IdentifierEquals / IdentifierNotEquals | `terms` auf `property.<p>_identifiers_` |

Sortierung: `property.<p>[._<lang>].exact` (`unmapped_type long`), danach `_score desc`, `_doc desc`. Paging: `from/size` (Default limit 1000), `Cursor` = Base64-JSON der Sort-Werte des letzten Treffers → `search_after`; `MaxResultWindow 10000`; `SearchBeyondMaxResultWindowAsync` nutzt Scroll (`ScrollPageSize`, `ScrollTimeoutInSeconds`) für Exporte. `track_total_hits: true`; `attachment` wird aus `_source` ausgeschlossen. Facetten: `terms`-Aggregationen `<p>_bucket` (auf `property.<p>`) und `<p>_identifiers__bucket` (Listen-IDs), Größe `MaxFacetSize`; Relations-Facetten als nested Aggregation oder Painless-Script über mehrere Pfade; Ergebnis `Facet{PropertyDefinitionIdentifier, Items[{Term, Count}]}`. AutoComplete-Vorschläge: Highlighting (unified, `FragmentLength` 10, Tags `{ }`) auf autocomplete-Feldern → distinct Fragmente, max. `NumberOfAutoCompleteSuggestions` (10); `_source` deaktiviert. **Nicht vorhanden:** Synonyme, Highlighting im Ergebnis, Suggester/Did-you-mean, Geo; Stemming nur über Sprach-Analyzer bei übersetzbaren Properties.

`EntitySearchService.SearchEntitiesAsync` (übrige Entitäten): gleiches Muster mit `path.principalIdentifiers`-Berechtigungsfilter, `entityTypeIdentifier`-Term, `EntityFilters` auf `path.fullPath` bzw. `entityIdentifier`; LicensePurchase-Filter `includedEntityIdentifiers`.

Backend-API (nicht Teil der Analyse, Hinweis): `POST /api/v{n}/search/reindex {entityTypeIdentifiers[]}`, `GET /search/reindex`, `GET /search/reindex/{id}` (Report); Asset-/Entitätssuche über AssetsController u. a.; `SavedSearchesController` (gespeicherte Suchen mit Resource-Permissions).

### 5.5 Reindex (Zero-Downtime per Alias-Swap)

1. Backend-Activities (`StartReIndexActivity`, `ExecuteReIndexActivity`, `SetReIndexStatusActivity`, `GetReIndexReportActivity`, `CancelReIndexingActivity`) → `CreateReIndexAliasAsync(types)`: alten Reindex-Alias lösen, neuen Index `<alias>_<yyyyMMdd_HHmmss><IndexSuffix>` anlegen, auf `yellow` warten, Alias `<alias><AliasSuffix>` setzen.
2. Backend streamt Batches (`PropertyDefinitionChangeBatchDto` → Mapping auf Reindex-Index, `AssetChangeBatchDto`/`EntityChangeBatchDto` → `ReIndexAsync`).
3. `SwapIndicesAsync`: `RestoreAttachmentsAsync` (Scroll über alten Index nach Dokumenten mit `attachment`, Bulk-Partial-Update in neuen Index – vermeidet erneute Textextraktion; `ReIndexBatchSize` 1000, `ScrollTimeoutInSeconds` 100) → Alias umhängen → alten Index löschen (`DeleteIndexAfterSwap`).
- Spezialtyp `AssetSetBinaryDocument` = nur Textextraktion neu ausführen. Auslösung über Admin-UI (*Admin → Configuration → Search*) oder `scripts/reindex.ps1`; Reindex nach Updates ist häufig nötig (Update-Notes). Hilfsfunktion `RemoveFieldAsync` (Feld per Script entfernen).

### 5.6 Konfiguration (`Sharedien.Search.*`)

| Key | Default |
|---|---|
| `SearchCallbackJob.MessageBrokerConnectionIdentifier` (+ `BackgroundJobConfiguration`) | – |
| `Elasticsearch.{BaseUrl, Username, Password, IndexSuffix (z. B. `_ix`), IndexAliasSuffix, EnableRequestLogging, PrettyPrintJsonLogging, MaxFacetSize, UpdateQueryTimeoutInSeconds 600, ScrollTimeoutInSeconds, ScrollPageSize}` | IndexSuffix ≠ IndexAliasSuffix Pflicht |
| `Elasticsearch.Query.{ExactTermBoost 1000, MultiTokenTermBoost 1, DocumentExtractionTermBoost 0.8, DocumentTitleExtractionTermBoost 1.5, DocumentExtractionPhraseTermBoost 5}` | Boosting |
| `Elasticsearch.AutoComplete.{FragmentLength 10, NumberOfAutoCompleteSuggestions 10, MinimumTokenLengthToIndex 2, MaximumTokenLengthToIndex 10, DefaultPrefixTermBoost 10, Fuzziness AUTO, FuzzinessPrefixLength 2}` | |
| `Elasticsearch.BinaryDocumentExtraction.{DownloadTimeoutInSeconds 30, ReIndexBatchSize 1000, ScrollTimeoutInSeconds 100}` | |
| `SearchReIndexingService.{AliasSuffix, DeleteIndexAfterSwap}` | |

Alle Keys sind `ConfigClass`-annotiert → in der Admin-UI pflegbar (SQL-Config).

### 5.7 Tests / Infrastruktur

Unit: `LanguageMappingTests`, `AssetSearchServiceTests`, `SearchServiceTests`, `TransformationServiceTests`; Integration (echtes ES): `ElasticsearchClientTests`, `IndexingServiceTests`, `SearchServiceTests`, `AssetSearchServiceTests`. Elasticsearch 7.10.2 (Docker `docker.elastic.co/elasticsearch/elasticsearch:7.10.2`, Single Node, 512 MB Heap); Migration von 6.5.4 laut Update-Notes 3.10.0 mit separatem Guide (nicht im Repo).


---

## 6. EventHub (SignalR)

Pfad: `src/EventHub`. Readme: "relays message queue events of the Sharedien.EventStream topic to connected SignalR clients" (heute: eigener Topic `…EventHub`). Optional (seit 3.8.0 Azure, 3.10.0 On-Premise).

### 6.1 Architektur

| Element | Details |
|---|---|
| Host | `Sharedien.EventHub` (netcoreapp3.1), Windows-Service 5008/4008, Docker `sharedien/sharedien-eventhub` (aspnet:3.1, Port 5008) |
| NuGet | Microsoft.AspNetCore.SignalR.Protocols.NewtonsoftJson 3.1.8, **Microsoft.Azure.SignalR 1.5.1** (optionaler Backplane/Scale-Out über Azure SignalR Service), AppInsights 2.15.0, Autofac 7.0.1 |
| Referenzen | `Sharedien.Api.Infrastructure` (dort liegt `EventHubCallbackJob`), `Sharedien.Common`, `Sharedien.Api.Contracts` |
| Config-Quelle | zusätzlich SQL-Config (Prefix `EventHub`, Connection-String `EventHub` = Sharedien-DB) |
| CORS | `SetIsOriginAllowed(_ => true)` + `AllowCredentials`; Header u. a. `X-Data-Languages`, `X-Requested-With` (SignalR-JS), `X-User-Contexts`; exposed `X-Total-Count`, `X-Data-Languages`, `X-User-Contexts`, `X-Operation-ID` |
| Endpoints | Hub `/hubs/notification` (`MapHub<NotificationHub>().RequireAuthorization()`), `GET /api/v{1..3}/info` (anonym), Swagger |

### 6.2 Hub, Auth, Zustellung

- `NotificationHub : Hub<INotificationHubClient>` – **keine Client→Server-Methoden**, nur Connect/Disconnect-Logging. Einzige Client-Methode: `Publish(Message payload)`; `Message{ActivityLogUpdate: ActivityLogUpdateDto, UtcTimeStamp}`; `ActivityLogUpdateDto{Identifier, Type (ActivityLogTypeDto), TriggeredByUserIdentifier, AffectedData{ObjectType, Identifier, EntityTypeIdentifier}, CreatedAt}`.
- Auth: `SharedienAuthenticationHandler` (Bearer-Token, für Hub-Pfade auch Query-Parameter `access_token`, WebSocket-Fall), Token-Validierung per Introspection beim Identity-Dienst (siehe §7.7). `UserIdProvider` → `IUserInformationProvider.GetCurrentUserId()` (Claim `Claims.UserIdClaimType = sub`; Gruppen-Claim `scope`).
- Zustellung: nur an **User** (`Clients.Users(InboxUserIdentifiers)`); keine Gruppen, kein Broadcast.
- Protokoll: JSON (Newtonsoft); SignalR-Optionen `EventHub.SignalR.{DetailedErrors, HandshakeTimeoutInSeconds, KeepAliveIntervalInSeconds, ClientTimeoutIntervalInSeconds, MaximumReceiveMessageSize, StreamBufferCapacity}`; `ConnectionStrings.AzureSignalRService` aktiviert `AddAzureSignalR`.

### 6.3 Event-Fluss API → Client

1. Backend erzeugt `ActivityLogEvent : EventHubEventBase` (`Identifier, UserIdentifier, Type, EntityIdentifier, EntityTypeIdentifier, ObjectType, ObjectIdentifier, CreatedAt, InboxUserIdentifiers[]`) und sendet auf Topic `topic://VirtualTopic.Sharedien.EventHub` / ASB `Advellence.Sharedien.EventHub`.
2. EventHub-Receiver `sharedien_eventhub_receiver` (AMQP durable, ASB Subscription `EventHub`, Max delivery 1, TTL 7 d) → `EventHubCallbackJob`: Nachrichten **älter als 5 min werden verworfen**; `ActivityLogEvent` → `NotificationHubService.ForwardActivityLogEventAsync` → Mapping auf `ActivityLogUpdateDto` → `Publish` an die Inbox-User. Ergebnis immer `Accepted`.
3. Frontend (`sharedienAppConfig.hubUrl = …/hubs/notification`) zeigt Glocken-Icon/Benachrichtigungen (Update-Notes 3.10.0).

`ActivityLogType`: AssetCreated, AssetUpdated, AssetDeleted, CategoryCreated/Updated/Deleted, TaskCreated/Updated/Deleted, WorkspaceCreated/Updated/Deleted, JobUpdated (z. B. Massenbearbeitung fertig), ApplicationSettingsUpdated.

Zweiter Receiver `sharedien_eventhub_internal_receiver` (Topic `Internal`, Subscription `EventHub`) nur für Config-Reload.

### 6.4 Eventstream-Topic für Integrationen (Doku `using-the-eventstream-topic.rst`)

Getrennt vom EventHub: Backend publiziert alle Systemereignisse auf `topic://VirtualTopic.Sharedien.EventStream` (Artemis/ActiveMQ multicast) bzw. ASB-Topic `advellence.sharedien.eventstream` (je Integration eigene Subscription; Doku empfiehlt TTL 7 d, Lock 1 min, Max delivery 1, Listen-Policy). Application-Properties für Filter:

| Property | Inhalt | immer |
|---|---|---|
| `x_sharedien_event_action` | Create/Read/Update/Delete | ja |
| `x_sharedien_event_action_details` | z. B. `NewAssetSetCreated`, `SuccessfulLogin`, `ApplicationSettingsUpdated` | ja |
| `x_sharedien_object_type` | z. B. `AssetSet`, `Property` | ja |
| `x_sharedien_object_id` | Id des Objekts | ja |
| `x_sharedien_user_id` | auslösender User | nein |
| `x_sharedien_property_definition_id` | bei Property-Events | nein |
| `x_sharedien_message_type`, `x_sharedien_operation_id`, `x_sharedien_priority_*` | Core-Properties | – |

AMQP-Filter über `apache.org:selector-filter:string` (JMS-Selector, AND/OR), ASB über Correlation/SQL-Filter; durable Consumer brauchen feste Client-Id. Nachrichtenkörper-Schema der Eventstream-Events: im Backend definiert (**nicht analysiert/unklar**). Aufräumen der gespeicherten Event-Nachrichten: `Sharedien.Cleanup.EventStreamMessageCleanup.{RetentionPeriodInSeconds, ChunkSize, ChunkTimeoutInSeconds}` (3.11.0) – d. h. Events werden auch in der DB gespeichert (Staging/Outbox, `IEventStagingService`).

### 6.5 SampleSignalRConsumer

Konsolen-Testclient: holt Token per ROPC (`/connect/token`, client `sharedien-api`, IdentityModel 4.4.0), verbindet mit `WithAutomaticReconnect`, `AccessTokenProvider`, abonniert `Publish` und druckt Tabellen. Config `SharedienSampleSignalRConsumer.Consumers{Name: {ConsumerName, Protocol, Address, Port, Hub, AuthenticationServiceUrl, Username, Password}}`; Docker-Image `sharedien/sharedien-sample-signalr-consumer`.

### 6.6 Konfiguration (`EventHub.*`)

`Logging`, `Hosting`, `MessageBroker` (Receivers s. o.), `Authentication{IsEnabled, IsApiDocumentationAuthenticationEnabled, ServiceUrl, ExternalServiceUrl, CheckTokenEndpointUri 'connect/introspect', OAuthTokenEndpointUri 'connect/token', OAuthImplicitTokenEndpointUri 'connect/authorize', ApiClientId sharedien-api, ApiClientSecret, WebClientId sharedien-web, RedirectUrl, Scopes[sharedien-api], ValidationCache{DisableCache false, CacheExpirationInSeconds 3600}, RequestTimeout 60, UserGroupCache{DisableCache true, 900}}`, `Claims{UserGroupClaimType 'scope', UserIdClaimType 'sub'}`, `EventHubCallbackJob{ExecutionLoopSleepTimeSeconds 10, RetryJobStartDelaySeconds 3, MessageBrokerConnectionIdentifier}`, `InternalCallbackJob`, `SignalR{…}`; `ConnectionStrings.{EventHub, AzureSignalRService}`.

---

## 7. Identity (IdentityServer4)

Pfad: `src/Identity`. OIDC/OAuth2-Provider + Login-UI (MVC/Razor, "Quickstart"-Basis von IdentityServer4) + SAML-/OIDC-/Windows-Federation. Löste die frühere **CloudFoundry UAA** ab (siehe §9).

### 7.1 Architektur

| Element | Details |
|---|---|
| Framework | **IdentityServer4 4.1.0** (netcoreapp3.1), **Sustainsys.Saml2.AspNetCore2 2.7.0**, AppInsights 2.15.0, Autofac 7.0.1, BouncyCastle (PEM-Parsing in `CertificateHelper`, Paket-Version unklar) |
| Referenzen | `Sharedien.Api.Infrastructure` (nutzt **direkt Backend-Services/DB**: UserService, ProvisioningService, PasswordResetService, RegistrationService, DataLanguageService, EventStagingService, UnitOfWork), `Sharedien.Common`, `Sharedien.Common.Infrastructure.Mail` |
| Datenhaltung | IdentityServer-Konfiguration **In-Memory aus YAML** (`AddInMemoryIdentityResources/ApiResources/ApiScopes/Clients`) – kein EF-ConfigurationStore/OperationalStore; Grants/Refresh-Tokens daher In-Memory (**Verlust bei Neustart/Scale-Out – Implikation, nicht explizit dokumentiert**). Benutzer liegen in der Sharedien-DB (`ConnectionStrings.Identity` = Sharedien3), Hangfire (`ConnectionStrings.BackgroundJobs`) für Hintergrundjobs (z. B. Mails). |
| Hosting | Docker `sharedien/sharedien-identity` (5007), Windows-Service ("Sharedien3 Identity" 5051/4041), **IIS Pflicht bei Windows-Auth** (Doku `run-identity-service-with-iis.rst`: AppPool "No Managed Code", Anonymous + Windows Auth, `forwardWindowsAuthToken=true`, Port 5050) |
| UI | Razor-Views: Login, Register, ResetPassword, ConfirmPasswordReset, VerifyEmail, RenewAccount, AccountStatus, Disclaimer, LoggedOut, Redirect, Error, Diagnostics; jQuery/Bootstrap/i18next (Sprachen **de, en**; Overrides über `wwwroot/locales/<lang>/override.json`); `SecurityHeadersAttribute` (CSP etc.) |

### 7.2 Ressourcen, Clients, Tokens

| Typ | Inhalt |
|---|---|
| IdentityResources | `openid`, `profile`, `email`, `profile.extended` (Claims `department`, `user_name`) |
| ApiResource / Scope | `sharedien-api` (ApiSecrets SHA256, für Introspection) |
| Client `sharedien-api` | Grant `ResourceOwnerPasswordAndClientCredentials` (Integrationen, Skripte, SDK, SampleConsumer), Secret, Scopes openid/profile/email/profile.extended/sharedien-api, AccessTokenLifetime 86400 (Docker), optional Refresh |
| Client `sharedien-web` | Grant `AuthorizationCode` (seit 3.7.0, vorher Implicit), `RequireClientSecret: false`, PKCE (`RequirePkce`-Flag konfigurierbar), `AllowOfflineAccess: true`, `AllowAccessTokensViaBrowser`, RedirectUris (Frontend, `/swagger/oauth2-redirect.html`, `/signin-oidc` aller Dienste), PostLogoutRedirectUris, AllowedCorsOrigins, Zusatzfelder `RedirectAfterLogin`, `ShowOnLoginPage`, `LoginUrl`, `IconUrl` (App-Kachel auf Login-Seite) |
| Client `ci-hub` (Doku) | AuthorizationCode + Refresh 30 d, Access 24 h (Integration CI HUB für Adobe/Office) |
| Unterstützte `AllowedGrantTypes` | Implicit, AuthorizationCode, ClientCredentials, ResourceOwnerPassword, ResourceOwnerPasswordAndClientCredentials (`ClientConverter`) |
| Refresh-Token-Optionen | `AllowOfflineAccess`, `ReUseRefreshToken` (OneTimeOnly default), `UpdateAccessTokenClaimsOnRefresh`, `SlidingRefreshTokenExpiration`, `AbsoluteRefreshTokenLifetime` (30 d), `SlidingRefreshTokenLifetime` (15 d) – nur für Code/Hybrid/ROPC |
| Token-Format | JWT (IdentityServer-Default), Signatur RSA aus `Login.TokenSigningCertificate` + `…Key` (PEM, optional verschlüsselt, `…KeyPassword`) **oder** `TokenSigningCertificatePath` (PFX) **oder** Developer-Credential (`tempkey.rsa`, nur Development) |
| Issuer | `General.IssuerUri` (wichtig hinter Proxy, sonst Audience/Issuer-Fehler IDX10214) |
| Redirect-Validierung | `CustomRedirectUriValidator`: Scheme+Host gleich und registrierte Pfadsegmente als **Präfix** (erlaubt tiefe Links) |

Endpoints: Standard-IdentityServer (`/.well-known/openid-configuration`, `/connect/authorize`, `/connect/token`, `/connect/introspect`, `/connect/userinfo`, `/connect/endsession`, `/connect/revocation`), `/Saml2` (SP-Metadaten), `/signin-oidc` (externe OIDC), MVC-Routen `Home/*`, `External/Challenge|Callback`, `Disclaimer/Index|Accept`, `Diagnostics/Index`.

### 7.3 Flows & Features

| Feature | Umsetzung |
|---|---|
| Lokaler Login | `HomeController.Login` → `UserStore.LoginByCredentialsAsync`: Passwort-Hash **PBKDF2-HMACSHA512, 50 000 Iterationen, 24 Byte, Salt 32 Byte** (`Sharedien.Api.Core/Helper/PasswordHelper.cs`), Login-Versuch mit Referer/User-Agent/IP protokollieren (`RecordLoginAttemptAsync`), Events über EventStaging |
| ROPC | `ResourceOwnerPasswordValidator` (gleiche UserStore-Logik) |
| Lockout | `Login.Lockout.{FailedLoginAttemptsThreshold 10, LockoutDurationInSeconds 900, ResetLockoutCounterAfterSeconds 600}`; Status `Locked`, Auto-Unlock nach Dauer; 0 = nie sperren bzw. nur manuell entsperren |
| Account-Status (`AccountStatus`) | Active, Unknown, EmailVerificationPending, EmailVerificationExpired, ApprovalPending, ApprovalDenied, Locked, AccountRenewalPending, AccountRenewalExpired, Inactive – `UserProfileService.IsActiveAsync` erlaubt nur aktive |
| Registrierung | `Registration.{IsEnabled, EmailVerificationRequired, EmailVerificationExpirationInHours 720, PasswordResetExpirationInHours 48, ApprovalRequired, ApprovalMaxRecipients 100, VerifyEmailEndpoint, ApprovalEndpoint (Admin-UI-Link), ApprovalEmailFallback, ConfirmPasswordResetEndpoint}`; Formular mit Mandant (Tenant) und Sprache; Freigabe durch Admins per Mail |
| E-Mail-Verifikation | `Home/VerifyEmail?token=` (VerifyUserEmailActivity) |
| Passwort vergessen | `Home/ResetPassword` (Mail mit Token) → `Home/ConfirmPasswordReset?token=` → `SetPasswordFromResetCodeAsync` |
| Account-Erneuerung | `Home/RenewAccount?token=` – Gegenstück zum API-Job `Sharedien.UserAccounts` (Renewal: `LastLoginDateThresholdInSeconds`, `RenewalWindowInSeconds`, `ReminderIntervalInSeconds`, `RenewAccountEndpoint`; Deletion je Status `Inactive`/`AccountRenewalExpired`/`ApprovalDenied` mit `RetentionPeriodInSeconds`; `CronExpression`); betrifft nur Access-Type **Expiring** (Registrierte), externe IdP-User immer Permanent |
| Disclaimer/AGB | `Disclaimer.IsEnabled` → Zwischenseite, Cookie `DisclaimerAccepted` 30 Tage |
| Anonymer Zugang | `AnonymousAccess.{IsEnabled, Username 'anonymous', Password}` – Button auf Login-Seite loggt technischen User ein; Frontend `authentication.anonymousAccess` |
| Externe OIDC (Azure AD) | `Login.OpenIdConnect.Providers.<name>{DisplayName, Authority, MetadataAddress, ClientId, ClientSecret, Scopes, RequireHttpsMetadata, SaveTokens, AttributeMapping{ExternalGroupProvider, GivenName, FamilyName, UserName, Email, ExternalGroups}, AssignedUserGroups}`; Gruppen via **Microsoft Graph** (`Login.ExternalGroupProviders.MicrosoftGraphApi{TenantId, ClientId, ClientSecret, MembershipType Direct|Transitive}`, App-Permission `Directory.Read.All`), Gruppen-Mapping über Azure-Group-GUID |
| SAML 2.0 SP | Sustainsys: `Login.Saml.Providers.<name>{EntityID, DisplayName, ServiceProviderIdentifier, IdpMetadata (URL/XML), SignRequest, SignatureAlgorithm, ProtocolBinding (HttpPost/HttpRedirect…), NameIdFormat, NameIdAllowCreate, ModulePath, ServiceProviderCertificate/Key/KeyPassword bzw. …CertificatePath, AttributeMapping, AssignedUserGroups}`; mehrere IdPs; Metadaten unter `/Saml2`; Start-Validierung (doppelte ModulePaths, Zertifikate, Enums) |
| Windows-Auth | nur IIS (`IISDefaults`), `Login.WindowsAuth{IsEnabled, DisplayName, AutomaticAuthentication, AttributeMapping{ExternalGroups [memberOf], GivenName givenName, FamilyName sn, UserName sAMAccountName, Email mail}, AssignedUserGroups, Ldap{Host, Port, UseSSL, BindDistinguishedName, BindCredentials, SearchBase, SearchFilter '(sAMAccountName={0})'}}` – Benutzerattribute/Gruppen per LDAP (`ILdapService`) |
| Auto-Provisioning | `ExternalController.Callback` → `UserStore.AutoProvisionUserAsync(provider, principal)` (Backend `IProvisioningService`): User anlegen/aktualisieren, externe Gruppen → Sharedien-Gruppen (`external_group_mapping`-Konzept), `AssignedUserGroups` |
| Default-IdP / SSO-Weiterleitung | `Login.DefaultIdentityProvider`; Frontend `defaultExternalIdentityProvider`; `?external=true` erzwingt normale Login-Seite |
| Logout | `Home/Logout` (IdentityServer-Logout-Kontext, PostLogoutRedirect, externe Sign-Outs) – Front-/Back-Channel-Logout-Details **unklar** |
| Session | Cookie mit Sliding Expiration; `Login.AuthenticationTicketStorage.StorageType Default|InMemory` (InMemory = `MemoryCacheTicketStore`, nur Key im Cookie); SameSite-Workaround `ConfigureNonBreakingSameSiteCookies` |
| 2FA/MFA | **nicht vorhanden** |
| Passwort-Policy | **unklar** (Validierung in `PasswordViewModel`/Backend nicht analysiert) |
| Logging | `Login.ExtendedAuthenticationLogging.{IsEnabled, PIILoggingEnabled}` (IdentityModel ShowPII) |
| Events | `SystemEventStream.{PublishUserEvents, MessageBrokerConnectionIdentifier}` (Login-Events wie `SuccessfulLogin` auf EventStream-Topic), `ActivityLog` (EventHub-Topic); interner Receiver für Config-Reload |
| Branding | `Branding.{FaviconUrl, MainBackgroundImageUrl, TopbarBackgroundColor, TopbarColor, TopbarAppLogoUrl, TopbarBrandLogoUrl, TopbarHeaderText, TopbarHeaderTextSize}` |
| Mock | `MockIdentityConfiguration` (Tests/Entwicklung, Details unklar) |

### 7.4 Token-Validierung in den Ressourcen-Diensten (API, EventHub, Processing, StorageHandler-Swagger)

`SharedienAuthenticationHandler` (Scheme "Sharedien Scheme"): Token aus `Authorization: Bearer`, alternativ Form-Feld `Authorization` oder bei `/hubs/*` Query `access_token` → `TokenValidationService`: **Introspection** gegen `ServiceUrl + CheckTokenEndpointUri (connect/introspect)` mit `ApiClientId/ApiClientSecret` (`IdentityTokenService`, Details der Implementierung im Backend), Ergebnis gecacht (`ValidationCache.CacheExpirationInSeconds`, Default 3600) → danach JWT lokal geparst (`ValidateLifetime`, **Signatur/Issuer/Audience nicht geprüft** – Vertrauen basiert auf Introspection). Swagger-UI-Schutz via OIDC-Cookie (`AddOpenIdConnectAuthentication`, Client `sharedien-web`, Code-Flow + `offline_access`).

### 7.5 Tests

`Sharedien.Identity.Tests/CustomRedirectUriValidatorTests.cs` (nur Redirect-Validierung).

---

## 8. SDK (`src/SDK/Sharedien.Sdk`)

| Aspekt | Befund |
|---|---|
| Stand | **Platzhalter** – `SharedienClient` ist eine leere statische Klasse (`Init()` ohne Inhalt, Kommentar "WORK IN PROGRESS"); keine API-Methoden, kein Generator (weder NSwag/AutoRest/Refit) |
| Projekt | TargetFrameworks `net5.0;netstandard2.0`, PackageId `Sharedien.Sdk`, Symbols (snupkg), Lizenzhinweis "requires a valid license" |
| Distribution | internes Azure-Artifacts-Feed `sharedien.pkgs.visualstudio.com/SharedienNext/_packaging/Sharedien.Sdk`, extern GitHub Packages "TBD" |
| Faktische Integrationsschnittstellen | REST-API mit OpenAPI/Swagger je Version (`/swagger/<v>/swagger.json`), Token via ROPC (`POST /connect/token`, Basic `sharedien-api:`), Upload-Protokoll (§4.4), Eventstream-Topic (§6.4), CI-HUB-Client (§7.2). API-Versionierung: max. 3 Vorversionen unterstützt (v6 aktuell in 3.10/3.11, v1/v2 deprecated), versionierte Operationsgruppen laut `api-versions.rst` (Assets, Upload, Categories, Comments, Custom Entities, Data Languages, Derivation Types, Extractions, Jobs, Property Definitions, Mass-Edit, Relations, User Contexts, Tasks, Workspaces). `docs/api-diff`: openapi-diff-Tooling für Changelogs. |

---

## 9. UAA (`src/UAA`)

| Aspekt | Befund |
|---|---|
| Was | **CloudFoundry UAA 4.14.0** (User Account and Authentication, Java/Spring, Tomcat `tomcat:9.0.12-jre8-alpine`, WAR aus GitHub-Tag gebaut via `Dockerbuild.bat`/Gradle), Datenbank MSSQL `Sharedien3.UAA` (Profil `sqlserver`) |
| Rolle | **Vorgänger des Identity-Dienstes** (Sharedien 3.0 früh): OAuth2/OIDC + SAML-SP (`login.entityID sharedien-saml-login`, SP-Key/Cert), SCIM-Benutzer/Gruppen. Heute nur noch Relikte: Frontend `sharedien-uaa-api.service.ts`, Doku-Reste (`?external=true`, ADFS "UAA relying party"), `AuthenticationConfiguration.CheckTokenEndpointUri` (UAA: `/check_token`). In docker-compose ≥3.5.3 nicht mehr enthalten → **obsolet** (ob noch Kunden darauf laufen: unklar). |
| Patch `https.patch` | ändert `OpenIdConnectEndpoints.getOpenIdConfiguration`: Issuer im Discovery-Dokument = konfiguriertes `issuer.uri` statt aus Request-URL abgeleitet (Fix für UAA-Issue #692 – falsches http/https hinter Reverse Proxy/SSL-Terminierung) |
| Konfiguration | `appsettings.yml`/`appsettings.Local.yml` (UAA-YAML): `issuer.uri`, `encryption.encryption_keys`, SAML-SP-Key/Cert, Logout-Redirect-Whitelist, SMTP (auskommentiert), OAuth-Clients `sharedien-web` (password, implicit, refresh_token; Scopes openid, password.write, scim.*, zones.*), `sharedien-admin` (Tools wie Massuploader), `sharedien-api` (client_credentials/password, `check_token`), `accessTokenValiditySeconds` 86400; Branding `appsettings.Branding.yml` (Logo, Footer-Links) |
| Identity-Zones | `sharedien-workspace` (Mandanten-Zone für Workspaces: "für jeden Workspace wird eine Gruppe erstellt"), Zone-Admin `sharedien-ws`, Membership `zones.sharedien-workspace.admin`; Zone `sharedien` vorbereitet, aber ungenutzt (Subdomain-Zwang) |
| `initial-configuration.ps1` | holt Admin-Token (`sharedien-admin`), legt Zone `sharedien-workspace`, Zone-Admin-User und Gruppen-Mitgliedschaft per UAA-REST (`/identity-zones`, `/Users`, `/Groups/zones`) an |
| SQL-Skripte | `dropAllTables.sql` (UAA-Schema: users, groups, group_membership, external_group_mapping, identity_provider, identity_zone, oauth_client_details, oauth_code, revocable_tokens, sec_audit, service_provider, authz_approvals, expiring_code_store, mfa_providers, user_google_mfa_credentials, user_info), `queryUaaUsers.sql` (Diagnose, Scope `password.write` nachtragen) |
| Relevanz für Shareme | nur als Referenz für Konzepte (Zonen/Multi-Tenancy, SCIM); nicht nachzubauen |

---

## 10. Common.Infrastructure.AI

Pfad: `src/Backend.Common/Sharedien.Common.Infrastructure.AI` (+ Abstraktion `Sharedien.Common/AI`). NuGet: **ClarifaiGrpc 7.3.0**, **Microsoft.Azure.CognitiveServices.Vision.ComputerVision 7.0.0**.

| Aspekt | Befund |
|---|---|
| Funktionsumfang | **Nur Auto-Tagging**: `ExtractionType` hat genau einen Wert `DetectTags`. **Kein OCR, keine Gesichts-/Promi-/Landmark-Erkennung, keine Beschreibung/Farben/Adult** (Clarifai-Modelle wie NSFW/Celebrity sind nur als Custom-Model-IDs hinterlegt und über `CustomModelId` wählbar) |
| Schnittstelle | `IArtificialIntelligenceProvider.ExtractAsync(url, ExtractionRequest{Extractions[], Language, CustomModelId})` → `ExtractionResult{Success, ErrorDescription, TagExtractionResults[{Name, Confidence 0..1}]}`; `GetSupportedLanguages`, `IsRequestPossible` (Sprache muss unterstützt sein, sonst `ArgumentException`); Bild wird **per URL** übergeben (Backend nutzt `GetReadAccessUrl` → SAS/Signed-URL, d. h. der KI-Dienst muss den Storage erreichen können) |
| Azure Cognitive Services | `ComputerVisionClient.AnalyzeImageAsync(url, [VisualFeatureTypes.Tags], language)`; Config `{IsEnabled, Identifier, ComputerVisionEndpoint, ComputerVisionSubscriptionKey}`; Sprachen en, zh, ja, pt, es |
| Clarifai | gRPC `V2Client.PostModelOutputsAsync` (Header `Authorization: Key <ApiKey>`), Default-Modell General `aaa03c23b3724a16a56b629203edc62c`, weitere: NSFW, Food, Weddings, Apparel(2), Moderation, Travel, Celebrity; Custom-Model als `"modelId,versionId"`; Config `{ApiUrl, ApiKey}`; 23 Sprachen (ar, bn, da, de, en, es, fi, fr, hi, hu, it, ja, ko, nl, no, pa, pl, pt, ru, sv, tr, zh, zh-TW) |
| Imagga | REST `GET /v2/tags?image_url=&language=&tagger_id=` (Basic Auth Key:Secret), Confidence/100, Auswertung `Monthly-Limit`-Header bei Fehlern; Config `{ApiUrl, ApiKey, ApiSecret}`; ~47 Sprachen |
| Registrierung | `RegisterArtificialIntelligenceProviders` (Assembly-Scan, alle aktiven Konfigurationen) |
| Auslöser im Backend | `Sharedien.PropertyExtraction.{ClarifaiAiProviders[], AzureCognitiveServicesAiProviders[], ImaggaAiProviders[], ChunkSize, CooldownBetweenChunksInSeconds, UseMocks, PropertyValueSeparator}`; `BackgroundPropertyExtractor` (Job über `JobsController`/`PropertiesController`, chunkweise mit Cooldown) |
| Datenmodell (Migration 0264) | `MAM.PropertyExtractionDefinition{Identifier, IsSystem, InputDerivationTypeId (welche Rendition z. B. Screen analysiert wird), CreatedAt}`, `PropertyExtractionDefinitionAssetType` (für welche Asset-Typen), `PropertyExtractionSource{Provider, ExtractionType, CustomModel, Language …, ConfidenceThreshold decimal(18,17)}`, `PropertyExtractionMapping{FromValue (Tag) → ToPropertyDefinitionId, ToPropertyListValueId}`; Ergebnis: Tags über Schwelle → Property-Werte (Text mit Separator, Listenwerte per Mapping, übersetzbare Properties in Default-Sprache) |
| Mocks | `ArtificialIntelligenceProviderMockHelper` (Integrationstests, `UseMocks`) |

---

## 11. Common.Infrastructure.Mail

| Aspekt | Befund |
|---|---|
| Bibliothek | **MailKit 2.9.0 / MimeKit 2.9.2** (`SmtpClientFactory` → `MailKit.Net.Smtp.SmtpClient`) |
| Versand (`EmailSender.SendTemplatedEmailsAsync(IList<EmailTemplate>)`) | Validierung (Empfänger, Subject, Html- oder Text-Inhalt); **Allow-List** `AllowedRecipients` (exakte Adressen oder `*@domain`; nicht erlaubte werden als "gesendet" markiert und verworfen – für Testsysteme); eine SMTP-Verbindung pro Batch, Warnung >50 Mails; `ConnectAsync(server, port, useSsl)` (**Server-Zertifikat wird nicht validiert**), optional `AuthenticateAsync(UserName, Password)`, `CooldownBetweenMessagesInMilliseconds` zwischen Mails; HTML **oder** Text (kein multipart/alternative), Pre-/Post-Body (Anrede/Grußformel) |
| `EmailTemplate` | `Subject, HtmlContent, PreHtmlBody, PostHtmlBody, TextContent, PreTextBody, PostTextBody, OutgoingEmailId, RecipientUserIdentifier, RecipientAddress, RecipientFullName, Sent` |
| Konfiguration (`Sharedien.Email`, seit 3.8.0 nur API) | `IsEnabled, BaseUrl (Links in Mails), SmtpServer, SmtpPort, UserName, Password, SenderName, SenderAddress, UseSsl, Connect/Authenticate/Send/DisconnectTimeoutInSeconds (5), CooldownBetweenMessagesInMilliseconds, AllowedRecipients[]`, `EmailQueue{RetentionPeriodInSeconds, CronExpression, DeleteChunkSize 500, DeleteChunkTimeoutInSeconds}` |
| Queue/Outbox (Backend) | Tabelle `OutgoingEmail` (Migration 0228), `SendOrEnqueueOutgoingEmailsActivity`, `BackgroundEmailSender` (Hangfire), `DeleteOutdatedEmailsActivity`; User-Einstellungen `UserNotificationSettings` (Opt-in/out je Benachrichtigung, Details unklar) |
| Templates | Ressourcen `NotificationTemplateRepository.{de,en}.resx` (+ Tabelle/Repository `NotificationTemplate`), Platzhalter `{{…}}` (Mustache-ähnlich, `RuleTemplateProcessor` auch für Rule-Engine-Mails) |

Benachrichtigungstypen (Template-Keys, jeweils Subject/HtmlContent/TextContent, de+en):

| Template | Auslöser / Empfänger | Platzhalter |
|---|---|---|
| AddedTaskAssignee | Nutzer einer Aufgabe zugewiesen | username, baseurl, taskurl, taskname, workspaceurl, workspacename |
| TransitionExecuted | Statuswechsel einer Aufgabe | …, oldstatus, newstatus |
| AssetUploadedToWorkspace / AssetUploadedToTask | Upload in Workspace/Task | …, filename |
| AssetDeletedFromWorkspace / AssetDeletedFromTask | Löschung | …, filename |
| AddedUserToWorkspace | Workspace geteilt | username, workspaceurl, workspacename |
| ShareCreated | Medienauswahl (Share) für externe/interne Empfänger | username, baseurl, token (`/shares/{token}`), sharename |
| UserRegistrationEmailVerification | Registrierung | emailVerificationLink |
| UserRegistrationReview | an Approver (max. `ApprovalMaxRecipients`, Fallback-Adresse) | fullUserName, userRegistrationReviewLink |
| UserRegistrationConfirmed / UserRegistrationDeclined | Freigabe/Ablehnung | username, baseurl |
| PasswordReset | Passwort vergessen | passwordResetLink |
| AccountRenewalPending | Account-Erneuerung (inkl. Reminder) | username, accountRenewalLink |
| WorkspaceDeletionReminder | automatische Workspace-Löschung naht | workspaceCount, Liste (`WorkspaceEntryDeletionInfo` "{0} will be deleted on {1} at {2}") |
| UploadSessionDeletionReminder | unvollständige Upload-Sessions werden gelöscht | workspaceCount (sic), Liste |
| LicensePurchaseBillingContact / LicensePurchaseCustomer / LicensePurchaseAdmin | DRM-Lizenzkauf | purchase.OrderId, licenseOffer.License.Name, customer.FirstName/LastName … |
| PreBody / PostBody | Rahmen ("Dear Sir or Madame" / "Kind regards, Your support team") | – |
| Rule-Engine-Mails | konfigurierbare Regeln (`RuleActionTemplate`) | frei |

Tests: `EmailSenderTests` (Absender, Empfänger(-name), Subject, Pflichtfelder, Konfig ohne Server/Port, ohne Credentials).

---

## 12. Common.Infrastructure.Queue

Pfad: `src/Backend.Common/Sharedien.Common.Infrastructure.Queue`. NuGet: **AMQPNetLite 2.4.0** (via Sharedien.Common; AMQP 1.0), **Microsoft.Azure.ServiceBus 5.0.0**, JsonSubTypes 1.8.0.

### 12.1 Abstraktion

| Typ | Beschreibung |
|---|---|
| `IMessageBrokerProvider` | `InitializeMessageBroker()` (verbindet alle Sender/Receiver), `GetMessageSender(id)`, `GetMessageReceiver(id)`; genau **ein** Broker aktiv (AMQP **oder** Azure Service Bus, sonst Exception) |
| `IMessageSender` | `SendMessageAsync(object body, contentType, IDictionary<string,string> properties)`, Connect/Disconnect |
| `IMessageReceiver` | `RegisterMessageHandler(IncomingMessageHandler(body, userProps, systemProps) → MessageOutcome)` |
| `MessageOutcome` | `Accepted` / `Rejected` (AMQP Accept/Reject, ASB Complete/**DeadLetter**) – kein Abandon/Retry-Delay |
| `MessageSystemProperties` | `EnqueueDateTimeUtc` |
| `MessageQueueEventProperties` | `TypeName` (`x_sharedien_message_type`), `PriorityForced`, `PrioritySelector` (Priorisierung, Auswertung im Backend – Details unklar), EventStream-Properties (`x_sharedien_event_action`, `…_details`, `…_object_type`, `…_object_id`, `…_property_definition_id`, `…_user_id`); `ToKeyValuePairs`/`FromKeyValuePairs` (**Bug**: `PriorityForced` wird aus dem MessageType-Wert geparst) |
| Serialisierung | `JsonMessageBrokerMessageSerializer` (Newtonsoft, `application/json`, `DateParseHandling.None`); Typauflösung über registrierte FullNames (`RegisterTypes`), Polymorphie über `RegisterSubTypes` (Discriminator-Property) |
| Korrelation | Sender setzt `x_sharedien_operation_id` = `Activity.Current.Id`; AMQP-Receiver startet Activity mit Parent-Id |

### 12.2 Implementierungen

| | AMQP 1.0 (`AmqpMessageSender/Receiver`, `AmqpMessageBrokerBase`) | Azure Service Bus (`AzureServiceBusMessageSender/Receiver`) |
|---|---|---|
| Verbindung | eine Connection+Session **pro Sender/Receiver** (ContainerId = Identifier), Adresse `amqp(s)://user:pass@host:port`, optional X.509-Client-Zertifikat, Server-Zertifikatsprüfung abschaltbar; bei Verbindungsabbruch Endlos-Reconnect (`ReconnectRetryDelayInSeconds` 5) | `QueueClient`/`TopicClient` aus ConnectionString |
| Adressierung | `queue://Name` (Capability `queue`) bzw. `topic://VirtualTopic.Name` (Artemis/ActiveMQ multicast) | Queue-/Topic-Name, Receiver auf `Topic/Subscriptions/Sub` |
| Senden | Durable, ContentType, CreationTime, ApplicationProperties, `SendTimeoutInSeconds` 30 | UTF-8-Body, UserProperties |
| Empfangen | `ReceiverLink.Start(credit = maxConcurrent)`; durable Subscription (`ExpiryPolicy never`, `Durable=1`) wenn `MessageIngestion.IsDurable` | `RegisterMessageHandler(AutoComplete=false, MaxConcurrentCalls, MaxAutoRenewDuration = 1 Tag)` (Lock-Renewal) |
| Parallelität | `MessageIngestion.{MaxConcurrentIncomingMessages 1, UseCpuCoreCount, CpuCoreMultiplier 2}` → `ProcessorCount × Multiplier` | gleich |
| Fehler | Exception im Handler → Reject | Exception → DeadLetter |
| Sessions/Scheduling/Duplicate Detection | nicht verwendet | nicht verwendet |

### 12.3 Hintergrund-Jobs

`BackgroundJobBase` (`OnJobStartedAsync` → Handler registrieren; Schleife `DoWorkAsync` + `ExecutionLoopSleepTimeSeconds`; Start-Retry `RetryJobStartDelaySeconds`) in `GenericBackgroundJobRunner<T>` (`IHostedService`). Konkrete Jobs: `JobReaderCallbackJob` (Processing), `SearchCallbackJob` (API), `EventHubCallbackJob` (EventHub), `InternalCallbackJob<T>` (alle Dienste; verarbeitet `InternalEventBase`-Ableitungen, aktuell nur `ApplicationSettingsChangedEvent{Keys[]}` → `IConfigurationReloader.ReloadAsync`), `InternalEventProducer` (sendet interne Events, `InternalCallbackJob.SenderMessageBrokerConnectionIdentifier`). Zusätzlich im Backend: **Hangfire** (SQL-Storage `BackgroundJobs`, Dashboard `BackgroundJobs.EnableDashboard`) für Cron-/Batch-Jobs.

### 12.4 Queue-/Topic-Inventar

| Entity (AMQP / ASB) | Typ | Producer → Consumer | Inhalt |
|---|---|---|---|
| `queue://Advellence.Sharedien.ProcessingJobStart` / `Advellence.Sharedien.ProcessingJobStart` | Queue | API → Processing | Derivations-/Metadaten-Jobs |
| `queue://Advellence.Sharedien.DerivationJobCallback` / `…DerivationJobCallback` | Queue | Processing → API | Job-Status |
| `queue://Advellence.Sharedien.Search` / `…Search` | Queue | API → API (Search-Modul) | Index-Events (§5.3) |
| `topic://VirtualTopic.Sharedien.EventHub` / `Advellence.Sharedien.EventHub` (Sub `EventHub`) | Topic | API, Identity → EventHub | ActivityLog-Events |
| `topic://VirtualTopic.Sharedien.EventStream` / `advellence.sharedien.eventstream` (Subs je Integration) | Topic | API, Identity → externe Integrationen | Systemereignisse |
| `topic://VirtualTopic.Sharedien.Internal` / `Advellence.Sharedien.Internal` (Subs `Api`, `Processing`, `Identity`, `EventHub`, `StorageHandler`) | Topic | API → alle | Config-Änderungen |
| Legacy: StorageHandler-`StartFileCompletionJob`/`FileCompletionJobFinished`, `UploadCallbackJob`, `SearchSyncQueue`, `SystemEventStream` Broker-Sources | – | – | vor 3.6/3.8 |

Azure-Vorgaben (Update-Notes): Lock 5 min, Max delivery 10 (Queues) bzw. 1 (Subscriptions), TTL 7 d, Topics 5 GB partitioniert.

---

## 13. Common.Infrastructure.FileTypeChecker

Pfad: `src/Backend.Common/Sharedien.Common.Infrastructure.FileTypeChecker` – Fork von github.com/0xbrock/FileTypeChecker "with modifications". Genutzt vom Backend-Feature **File Recognition** (`FileRecognitionService`, `RecognizedFileNameBuilder`; nicht vom StorageHandler).

Mechanik: `IFileTypeChecker.GetFileTypesAsync(stream)` → alle passenden `FileType{Name, Extensions, Matcher}`; `GetMaxLength()` (benötigte Header-Bytes), `GetKnownExtensions()`. Matcher: `ExactFileTypeMatcher(bytes, startPosition)`, `FuzzyFileTypeMatcher(byte?[])` (null = Wildcard), `RangeFileTypeMatcher(matcher, maxStart)` (Signatur innerhalb der ersten n Bytes suchen), `OfficeOpenXmlFileTypeMatcher(entry)` (ZIP-Signatur + Scan des **Central Directory** nach `word/document.xml`, `xl/workbook.xml`, `ppt/presentation.xml` – liest gesamte Datei, benötigt seekable Stream).

Signaturen (46 Einträge, `FileTypeChecker.cs`):

| Extension(s) | Signatur (hex) / Methode |
|---|---|
| png | 89 50 4E 47 0D 0A 1A 0A |
| jpg | FF D8 FF; zusätzlich Fuzzy `FF 0D FF E0 ?? ?? 4A 46 49 46 00` (**Tippfehler 0x0D statt 0xD8**) |
| gif | 47 49 46 38 37 61 (87a), 47 49 46 38 39 61 (89a) |
| pdf | `%PDF` (25 50 44 46) innerhalb der ersten 1019 Bytes |
| ico | 00 00 01 00 |
| 3g2 | Fuzzy `?? ?? ?? ?? 66 74 79 70 33 …` (ftyp3g2) |
| mkv | 1A 45 DF A3 |
| bmp | 42 4D |
| tif / tiff | 49 49 2A 00 / 4D 4D 00 2A |
| psd | 38 42 50 53 |
| rar | 52 61 72 21 1A 07 00 (1.5), 52 61 72 21 1A 07 01 00 (5.0) |
| zip | 50 4B 03 04, 50 4B 05 06 (leer), 50 4B 07 08 (spanned) |
| xar | 78 61 72 21 |
| tar | 75 73 74 61 72 ("ustar") |
| 7z | 37 7A BC AF 27 1C |
| gz | **fehlerhaft**: gleiche Bytes wie TAR (korrekt wäre 1F 8B) |
| doc (OLE/CFB 97-2003, auch xls/ppt) | D0 CF 11 E0 A1 B1 1A E1 |
| docx / xlsx / pptx | OfficeOpenXml-Matcher |
| xml | `<?xml version` |
| avi | 52 49 46 46 ("RIFF") |
| mp4 / m4v / 3gp5 | diverse `ftyp`-Varianten (ftypMSNV, ftypisom, ftypmp42, Offsets 3/4) |
| mpg | 00 00 01 B3 |
| aac | FF F1, FF F9 |
| mp3 | 49 44 33 ("ID3") |
| avi, cda, qcp, rmi, wav, webp | RIFF (mehrdeutig) |
| oga, ogg, ogv, ogx | 4F 67 67 53 ("OggS") |
| flv | 46 4C 56 |
| flac | 66 4C 61 43 00 00 00 22 |
| m4a | 00 00 00 20 66 74 79 70 4D 34 41 |
| eps | Fuzzy `%!PS-Adobe…EPSF` innerhalb eines Bereichs |
| indd | 06 06 ED F5 D8 1D 46 E5 |
| ai | "Adobe Illustrator" innerhalb eines Bereichs |

Backend-Konfiguration `Sharedien.FileRecognition` (Doku `file-recognition/index.rst`): `IsEnabled`; **Scope** `FilesWithoutExtension` (Default) | `AllFiles`; **Mode** `Enrich` (best effort, Default) | `Strict` (unbestätigte Typen abweisen; mehrdeutig → FileType "Unknown"); **ExtensionBehavior** `Append` (`file.gif.jpg`) | `Replace`; Konfliktauflösung: bekannte Extension bevorzugen → längste Magic-Byte-Sequenz → sonst "Unknown". Gilt für Asset-, Versions-, Derivations-Upload und Archiv-Extraktion. Tests: `FileTypeCheckerTests` (Integration), `FileRecognitionBaseTest`, `RecognizedFileNameBuilderBaseTest`.

---

## 14. Externe Tools & Abhängigkeiten (Gesamtliste)

| Kategorie | Komponente | Version | Verwendet von | Zweck |
|---|---|---|---|---|
| Runtime | .NET Core / ASP.NET Core | 3.1 (SDK teils net5.0) | alle | Hosting |
| DB | Microsoft SQL Server | 2017 CU12 → 2019 CU10 (Docker); Azure SQL | API, Identity, EventHub, Processing (Config), StorageHandler | Daten, Konfiguration, Hangfire, Outbox |
| Broker | ActiveMQ Artemis (AMQP 1.0) / ActiveMQ 5 | 2.9.0 (Docker) | alle | Messaging |
| Broker | Azure Service Bus | – | Azure-Deployments | Messaging |
| Suche | Elasticsearch + Plugin `ingest-attachment` (Apache Tika) | 7.10.2 (vorher 6.5.4) | API (Search-Modul) | Index, Volltext aus Dokumenten |
| Storage | Dateisystem/NAS, Azure Blob Storage | Azure.Storage.Blobs 12.6.0 | StorageHandler, API, Processing | Binärdaten |
| Realtime | ASP.NET Core SignalR, optional Azure SignalR Service | 3.1.8 / 1.5.1 | EventHub | Notifications |
| Identity | IdentityServer4 | 4.1.0 | Identity | OIDC/OAuth2 |
| Identity | Sustainsys.Saml2.AspNetCore2 | 2.7.0 | Identity | SAML-SP |
| Identity | Azure AD (OIDC) + Microsoft Graph, LDAP/AD, IIS Windows-Auth | – | Identity | Föderation, Gruppen |
| Identity (Legacy) | CloudFoundry UAA auf Tomcat 9 / JRE 8 | 4.14.0 | – (obsolet) | Vorgänger |
| Processing | ImageMagick (`magick`, built mit gslib, rsvg, libraw, libwebp) | 7.0.10-30 | Processing | Bild-Derivate, Farbmanagement |
| Processing | FFmpeg / FFprobe | Distro (unklar) | Processing | Video-Thumbnails/GIF, AV-Metadaten |
| Processing | Ghostscript (`gs`/`gswin64c`) | Distro (unklar) | Processing | PDF/EPS/AI-Rasterung |
| Processing | LibreOffice (`soffice`, Portable unter Windows) | Distro / "libreoffice6.0"-Pfad | Processing | Office → PDF |
| Processing | ExifTool (Perl) | Distro (unklar) | Processing | EXIF/IPTC/XMP/ICC |
| Processing | Blender | Distro (unklar) | Processing | 3D (FBX) → PNG |
| Processing | dcraw | nur Windows-Pfad, Prozessor nicht registriert | – | RAW (inaktiv) |
| Processing | ICC-Profile ECI/ICC (eciRGB v2, ISO Coated v2, ISO Coated v2 300 %, eciCMYK, sRGB v4, US Web Coated SWOP) | – | Processing | Farbkonvertierung |
| Processing | CliWrap 3.2.0, CoreCLR-NCalc 2.2.88, SharpZipLib (Binaries-Entpacken) | – | Processing | Prozessaufrufe, Ausdrücke |
| KI | Azure Computer Vision, Clarifai (gRPC), Imagga (REST) | SDK 7.0.0 / ClarifaiGrpc 7.3.0 | API (PropertyExtraction) | Auto-Tagging |
| Mail | MailKit/MimeKit (SMTP) | 2.9.0 / 2.9.2 | API | Benachrichtigungen |
| Queue | AMQPNetLite, Microsoft.Azure.ServiceBus | 2.4.0 / 5.0.0 | alle | Broker-Clients |
| Suche | NEST / Elasticsearch.Net | 7.10.0 | API | ES-Client |
| Archive | SharpZipLib, System.IO.Compression | 1.2.0 | API, StorageHandler-Common | ZIP lesen/schreiben |
| Jobs | Hangfire (SQL Server) | Version unklar | API, Identity | Cron-/Hintergrundjobs |
| Querschnitt | Autofac 7, AutoMapper 10, Dapper 2.0.35, FluentValidation 9.2.2, Serilog 2.10 (+Sinks), Swashbuckle 5.6.3, API-Versioning 4.1.1, NetEscapades.Configuration.Yaml, Newtonsoft.Json 12, DbUp (StorageHandler) | – | alle | Framework |
| Monitoring | Azure Application Insights | 2.15.0 | alle | Telemetrie/Logs |
| Frontend-Hosting | nginx 1.17.1-alpine / HTTP.sys-Static-Server | – | Web | SPA |
| Doku/Tooling | Sphinx (RTD-Theme, LaTeX), openapi-diff (Docker), GitVersion | – | Build | – |

### Besonders komplex / risikoreich für den Nachbau

1. **Berechtigungs-denormalisierte Suche**: Principals werden in jedes ES-Dokument (`categoryPaths.principalIdentifiers`) geschrieben und per Painless `update_by_query` bei Rechte-/Verschiebe-Änderungen (inkl. "Interruptions" und Property-basierter Asset-Restrictions) nachgezogen; Mehrfach-Kategoriepfade je Asset.
2. **Dynamisches ES-Mapping** aus Property-Definitionen (Typen, Listen-IDs, übersetzbare Felder mit Sprach-Analyzern, Relations-Baum nested beliebiger Tiefe, Facetten über Relationen per Script).
3. **Generische Derivations-Engine** (JSON-Step-Pipelines in der DB, Variablen/NCalc, 7 externe Tools, LibreOffice-Profil-Isolation, ICC-Farbmanagement mit Override-Tabellen) – plus separate programmatische ImageMagick-Pipeline (Crop/Overlay/Wasserzeichen).
4. **Direkt-Upload-Protokoll** mit zwei Signaturschichten (API-Upload-URL + Storage-Signed-URL/Azure-SAS), Chunk-Kombination, MD5-Berechnung, Archiv-Extraktion.
5. **Identity-Föderation**: SAML (mehrere IdPs), Azure AD + Graph-Gruppen, Windows/LDAP, Auto-Provisioning mit Gruppenmapping, Registrierung mit Freigabe-Workflow, Account-Renewal/Deletion-Lebenszyklus, Lockout.
6. **Dual-Broker-Abstraktion** (Artemis AMQP 1.0 vs. Azure Service Bus) mit Topic-/Subscription-Topologie, Eventstream für Integrationen und Config-Reload per internem Topic; Konfiguration hybrid aus YAML/Env/SQL mit Laufzeit-Reload.

### Auffälligkeiten / potentielle Bugs (nicht übernehmen)

`MetadataExtractionStatusMessage` setzt `AssetVersionIdentifier` nie; `StepVideoDurationSeconds` = ffprobe-Laufzeit statt Videodauer; Overlay-Composite nutzt Quell- statt Overlay-Datei; Scale-Optionen (Relative, PreventUpscaling …) ignoriert; FileTypeChecker-Signaturen für gz und JPEG-JFIF fehlerhaft; `PriorityForced`-Parsing falsch; TLS-Zertifikatsprüfung für ES, SMTP und optional AMQP deaktiviert; Signed-URL = SHA-256(Key+Pfad+Werte) statt HMAC; JWT-Signatur wird in Ressourcen-Diensten nicht lokal geprüft (nur Introspection-Cache); IdentityServer-Grants nur In-Memory.
