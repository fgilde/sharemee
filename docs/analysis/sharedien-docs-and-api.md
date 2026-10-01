# Sharedien – Analyse der Dokumentation und der API-Spezifikation

Zweck: lückenloses Funktionsinventar des DAM/MAM-Systems **Sharedien** (Advellence AG) als Grundlage für die 100-%-Abdeckung durch das Nachfolgeprodukt **Shareme**. Stand der Quelle: Repository-Snapshot `C:\dev\privat\github\sharedien` (einziger Commit vom 2023-02-10, Codestand ca. Release 3.11.x, API v6).

## 0. Quellen, Methode, Kennzahlen

### 0.1 Ausgewertete Quellen

| Quelle | Umfang | Verwendung |
|---|---|---|
| `docs/technical-manual/**/*.rst` | 58 RST-Dateien, ~250 KB (Installation, Update Notes 3.5.1–3.11.0, Konfiguration aller Dienste, Monitoring, Integrationen, Metadata, Renditions, File Recognition, Web UI) | vollständig gelesen → Teil A |
| `docs/api-diff/specs/v1.json`, `v2.json`, `README.md`, `patch_swagger.ps1` | 3,4 KB / 5,3 KB | **Nur Stubs**: enthalten je genau einen Pfad (`/api/application-settings`) mit 3–4 Schemas. Sie dienen als Beispiel für den `openapi-diff`-Workflow, nicht als vollständige Spezifikation. |
| `src/Backend/Sharedien.Api.Client/Generated/ClientGenerated.cs` | 39 505 Zeilen, 2,5 MB, NSwag 13.8.2, aus `Sharedien.Api.dll` generiert | **Ersatz für die vollständige OpenAPI-Spec**: enthält alle 463 Operationen mit Verb, Pfad (inkl. API-Version), Parametern, Request-Body-Typ, Response-Typ und XML-Summary. Cross-Check gegen die 50 Controller in `src/Backend/Sharedien.Api/Controllers` (156 GET + 152 POST + 78 PUT + 17 PATCH + 60 DELETE = 463 `[Http*]`-Attribute) → **1:1 vollständig**. |
| `src/Backend.Contracts/Sharedien.Api.Contracts/Public/**/*.cs` | 518 C#-Dateien | Alle DTOs/Enums der öffentlichen API (Webservice), der Message Queue (EventStream V1/V2, Processing) und SignalR: **424 DTO-Klassen/Interfaces, 70 Enums, 65 statische Helper-Klassen** (Event-Konstanten). |
| `src/Backend/Sharedien.Api.Client/Generated/ClientExtended.cs`, `Helpers/PartitionedUploader.cs`, `NSwag/Sharedien.nswag` | | Upload-Komfortmethoden, Auth-Flow des Clients |
| `GitVersion.yml`, `README.md` (leer), 27 weitere `*.md` im Repo (docker, Backend, Processing, Search, StorageHandler, UAA, SDK, EventHub, Frontend, DbMigration …) | | Architektur, Build, Betrieb |

### 0.2 Methode
Endpoint- und Schema-Tabellen wurden per Python-Skript aus `ClientGenerated.cs` (Regex über Signaturen, `urlBuilder_.Append("/api/…")`, `HttpMethod("…")`, `status_ == …`, `SerializeObject(…)`) und aus den Contracts-Quellen (Klassen-/Enum-/Property-Parser inkl. Vererbungsauflösung generischer Basisklassen wie `SearchResultDto<AssetDto>`) extrahiert. Die Prosa-Teile (A, D) sind manuell aus der Doku abgeleitet. Sprache: Deutsch, Fachbegriffe/Identifier englisch.

Legende in den Tabellen: `*` = `[Required]`, `~` = `[Obsolete]`, `IList<T>` = JSON-Array, `IDictionary<K,V>` = JSON-Objekt, `byte[] RowVersion` = Base64-Concurrency-Token, `?` = nullable.

### 0.3 Kennzahlen

| Kennzahl | Wert |
|---|---|
| API-Operationen (Sharedien API, alle Versionen) | **463** (v6: 440, v5: 11, v4: 4, v3: 2, v2: 2, v1: 4) |
| Tags / Controller | **43** (50 Controller-Klassen; `FileTransferBaseController` ist abstrakt, `LicenseManagement/*` = Tag `DrmLicenses`) |
| Größte Bereiche | Workspaces 65, DrmLicenses 58, Assets 47, Categories 30, Tasks 28, CustomEntities 26, Jobs 14, Properties 14, Users 13, SavedSearches 11 |
| DTO-Klassen (öffentliche Contracts) | **424** (Webservice 329 + ImageDerivation 20 + Upload 9 + MessageQueue 62 + SignalR 4) |
| Enums | **70** (größte: `PermissionDto` 331 Werte, `ObjectTypeDto` 84, `ActivityLogChangeTypeDto` 21) |
| Parameter gesamt | 614 Query, 389 Path, 218 Body, 5 File, 4 Form |
| Obsolete DTOs (`[Obsolete]`) | 7 (`CreateAssetDto`, `CreateAssetResponseDto`, `CreateAssetVersionDto`, `CreateAssetVersionResponseDto`, `CreateAssetDerivationResponseDto`, `CreateWorkspaceAssetDto`, `StartUploadResponseDto` – Legacy-Chunk-Upload, Entfernung in v7 angekündigt) |
| Dokumentierte Konfigurations-Settings | ca. 260 Schlüssel über 7 Dienste + Web-UI (Teil A.3) |
| EventStream-Ereignistypen | 21 Event-Klassen (V1: 16, V2: 5), ~110 `ActionEventDetails`-Werte, 32 `ObjectType`-Werte |


## Teil A: Technical Manual (Sphinx-Doku `docs/technical-manual`)

Die Doku ist in Sphinx/RST geschrieben (Docker-Builder-Image `sphinx-doc-builder`, RTD-Theme, HTML + LaTeX/PDF, Versionsangabe via `-D version/release`). Kapitelstruktur laut `index.rst`: Installation, Update, Configuration, Monitoring, Integrations, Metadata, Assets (Renditions, File recognition), Web user interface. Hersteller: Advellence AG (Zürich); Produktname "Sharedien 3.x".

### A.1 Architektur und Komponenten

| Komponente | Rolle | Technik / Ports (Default) | Quelle |
|---|---|---|---|
| **Sharedien API** (`Sharedien.Api`) | Haupt-API: Assets, Metadaten, Kategorien, Workspaces/Tasks, Suche (Elasticsearch-Fassade seit 3.7), Regeln, Lizenzen, Hintergrundjobs (Hangfire), Systemeventstream-Publisher | ASP.NET Core (netcoreapp3.1 zur Zeit des Clients), HTTP 5001 / HTTPS 4001; Swagger unter `/swagger`; Hangfire-Dashboard optional | configuration/sharedien-api, Backend/Readme |
| **Identity Service** (`Sharedien.Identity`) | OAuth2/OIDC-Provider (IdentityServer-artig): Clients, ApiResources, lokale Benutzerkonten, Registrierung, Disclaimer, Anonymous Access, externe IdPs (SAML2, OIDC/Azure AD, Windows Auth/LDAP), Branding, Übersetzungs-Overrides | HTTP 5050/5057, HTTPS 4040; Endpunkte `/connect/token`, `/connect/authorize`, `/Saml2`, `/signin-oidc` | configuration/identity-service |
| **Storage Handler API** (`StorageHandler.Api`) | Persistenz der Binärdaten; Storage-Provider FileSystem oder Azure Blob; Pre-signed/Shared-Access-URLs zum Download/Upload | HTTP 5000/5050 (Docker), HTTPS 4000; eigene DB `StorageHandler` | configuration/storage-handler-api |
| **Processing API** (`Processing.Api`) | Erzeugt Derivate/Renditions (ImageMagick, FFmpeg/FFprobe, LibreOffice, Ghostscript, ExifTool, Blender ab 3.9), Metadaten-Extraktion (IPTC/EXIF/XMP/Audio/Video), Farbprofile (ICC) | HTTP 5002/5052; Message-Queue-getrieben; ab 3.11 eigener DB-Zugriff (`ConnectionStrings.Sharedien`) | configuration/processing-api |
| **Search API** (`Sharedien.Search`) | Elasticsearch-Fassade (bis 3.6); ab 3.7.0 in die Sharedien API integriert (Service deaktivierbar) | Port 9201; Elasticsearch 6.x → 7.10.2 (ab 3.10.0), Ingest-Attachment-Plugin | Search/Readme, 3.7.0 Update Notes |
| **EventHub** (`Sharedien.EventHub`) | Relais von Message-Queue-Events (Topic `Sharedien.EventStream` / `Advellence.Sharedien.EventHub`) an SignalR-Clients (`/hubs/notification`) für Echtzeit-Benachrichtigungen im Web-Client; optional, ab 3.8/3.10 | HTTP 5008, HTTPS 4008; optional Azure SignalR Service | configuration/eventhub, 3.10.0 Notes |
| **Web Frontend** (`Sharedien.Web`) | Angular-SPA, State-Management NGXS, Font Awesome Pro; statisch gehostet (Http.Sys-Windows-Service, IIS/Azure Web App, Docker); Konfiguration über `configuration.js` + `configuration.overrides.js` | Port 4200 (dev) / 4250 (Docker); Supported Browser ab 3.10: Chrome, Firefox, Edge (kein IE11) | configuration/frontend, Frontend/README |
| **Frontend Windows Service** | Http.Sys-Static-File-Server mit IIS-/Apache-Rewrite-Regeln | `--static-files-dir`, `--iis-redirect-rules-file`, `--apache-redirect-rules-file`, `--server.urls` | Frontend.WindowsService/Readme |
| **DB-Migration** (`Sharedien.Api.DbMigration`, `StorageHandler.DbMigration`) | DbUp-basierte SQL-Migrationen (`{Date}_{Version}_{Description}.sql` als EmbeddedResource), automatische Migration beim Start, `CreateDatabaseIfNotExists` | SQL Server 2016+, Azure SQL | DbMigration/Readme |
| **Infrastruktur** | SQL Server (DB `Sharedien3`, Connection Strings `Sharedien`, `BackgroundJobs`, `Identity`, `StorageHandler`, `EventHub`), Message Queue AMQP 1.0 (ActiveMQ 5 classic / Artemis empfohlen) oder Azure Service Bus, Elasticsearch, SMTP | Docker-Compose-Setups (infrastructure/identity/sharedien), min. 6 GB RAM, `vm.max_map_count=262144` | docker/README |
| **UAA (Legacy)** | Cloud Foundry UAA 4.14 als früherer Identity-Provider (Identity Zones = Mandanten, Gruppe pro Workspace), abgelöst durch den Identity Service | Java/Gradle, MSSQL | UAA/Readme |
| **SDK / API-Client** | `Sharedien.Sdk` (NuGet, nur auf Anfrage, Lizenzpflicht) wrappt `Sharedien.Api.Client` (NSwag-generiert, `GeneratedClient`), inkl. `PartitionedUploader` (Chunk-Upload), Komfortmethoden `Assets_UploadAsync`, `Assets_VersionUploadAsync`, `FileReference_UploadAsync`, `Properties_ExportAssetPropertiesToFileAsync`, `UserContexts_CreateUserContextIfNotExistsAsync` | Auth via Resource-Owner-Password-Flow, Header `X-Data-Languages: en` | SDK/README, Api.Client |
| **Sample-Consumer** | `SampleEventConsumer` (AMQP-Topic-Test, `ConsumerIdentifier` je Instanz) und `SampleSignalRConsumer` (SignalR-Test), als Docker-Images | | EventConsumers, EventHub |

Konfigurationsprinzip aller .NET-Dienste: `appsettings.yml` (unveränderlich) + `appsettings.Overrides.yml` (Kunde) bzw. Environment-Variablen (`Section__Sub__Key`, Arrays via `__0`), Azure App-Service-Settings. Konfigurationsabschnitt je Dienst: `Sharedien`, `Identity`, `StorageHandler`, `Processing`, `EventHub`, `Frontend`. Restart nach Änderungen nötig. Zusätzlich gibt es eine **DB-gestützte Konfiguration** (`ConfigurationProviderDto.Database`, API `application-settings`), d. h. Settings sind zur Laufzeit per API/Admin-UI überschreibbar.

Versionierung (GitVersion.yml): GitFlow – `master` (ContinuousDelivery, kein Tag), `release/*` (`rc`), `develop` (`beta`, Minor-Increment), `feature/*` (`alpha`), `hotfix/*` (`beta`, Patch). Docker-Images: `sharedien/sharedien-api`, `sharedien/sharedien-web`, `sharedien/processing`, `sharedien/storagehandler`, `sharedien/sharedien-search`, `sharedien/sharedien-eventhub`, `sharedien/uaa`; Beta-Images aus `sharedienregistry.azurecr.io`.

### A.2 Installation und Hosting-Varianten

| Variante | Beschreibung | Besonderheiten |
|---|---|---|
| **Windows Services (on-premise)** | Alle Dienste als Windows-Services mit HTTP.SYS als vollwertigem Webserver; Frontend als Static-File-Service | Zertifikat (pfx) in LocalMachine\My importieren, `bind-https-certificate.bat` bindet Thumbprint an alle Ports; Firewall HTTPS 443/4000/4001/4040, HTTP 80/5000/5001/5050; HTTP→HTTPS-Redirect je Dienst via `Hosting.EnableHttpsRedirection` |
| **Reverse Proxy / Load Balancer** | Üblicher Fall: SSL-Terminierung/LB davor (NGINX-Beispiel) | Proxy muss `X-Forwarded-For`/`X-Forwarded-Proto` setzen; Dienste: `Hosting.UseForwardedHeaders=true` und `KnownProxies`/`KnownNetworks` **müssen** gesetzt sein, sonst "Unknown proxy" (Debug-Log) |
| **IIS (nur Identity Service)** | Nötig für Windows Authentication | Site `SharedienIdentity`, App-Pool "No Managed Code", .NET Core Hosting Bundle, Anonymous + Windows Auth aktiv, `forwardWindowsAuthToken=true`, Ports 5050/4040 |
| **Docker / docker-compose** | Dev/Test-Setups (nicht produktionsreif) in `docker/` (infrastructure, sharedien, identity), Netzwerk `sharedien`; Reihenfolge: Infrastruktur → Sharedien → Identity | Alpha (lokal gebaut), Beta (ACR), Release (Docker Hub); Variante "API on host" |
| **Azure (PaaS)** | App Services je Dienst, Azure SQL, Azure Service Bus (Topics `Advellence.Sharedien.EventStream`, `.EventHub`, `.Internal`), Azure Blob Storage, Azure SignalR Service, Application Insights | Settings als App-Service-Konfiguration (`__`-Notation, Arrays mit Index), Storage-Handler-Service ab 3.6 bei Blob-Storage nicht mehr nötig, Blob-CORS `PUT` erlauben, Service-Bus-Queues: Lock 5 min, MaxDelivery 10 |

**Zertifikate:** ein SAN-Zertifikat pro Umgebung für Web, API und Identity (z. B. `dam.company.com`, `dam-api.company.com`, `dam-id.company.com`), pfx mit Private Key.

**First Run:** Bootstrap-Admin `advadmin`; empfohlener Ablauf: Gruppe `Administrators` anlegen, Benutzer zuweisen, unter *Admin → Tenants* der Gruppe die Rolle *Administrator* geben, dasselbe für den Kategoriebaum (*Admin → Categories → Settings → Permissions*) und den Recycle Bin; danach *Admin → Configuration → Search → Reindex* (Elasticsearch-Indizes werden erst durch Reindex nutzbar). Zeigt: Berechtigungen werden auf Tenant, Kategorie-Root und Recycle-Bin getrennt vergeben (Node-Permissions).

### A.3 Konfiguration je Dienst

#### A.3.1 Sharedien API (`Sharedien.*`)

| Setting | Wert | Pflicht | Bedeutung |
|---|---|---|---|
| Sharedien.Logging | Sektion | Ja | siehe Logging (A.3.9) |
| Sharedien.Hosting | Sektion | Nein | siehe Hosting |
| Sharedien.MessageBroker | Sektion | Ja | siehe Message Queue |
| Sharedien.StorageHandler.InternalApiCallUrl | URL | Ja (bis 3.5) | interne URL des Storage Handlers (Upload, wird an Processing weitergegeben) |
| Sharedien.StorageHandler.ExternalBaseUrl | URL | Ja (bis 3.5) | externe Download-URL für Konsumenten |
| Sharedien.StorageHandler.PreSignedUrl.Signing.SigningKey | Key | Ja (bis 3.5) | Signierschlüssel für Download-URLs (muss mit Storage Handler übereinstimmen) |
| Sharedien.StorageProviders.StorageHandlerStorageProviders[n].{IsEnabled, StorageHandlerApiUrl, AccessKey, SharedAccessStorageHandlerApiUrl, SharedAccessSigningKey} | Sektion | ab 3.6 | Storage-Provider-Modell (mehrere Provider möglich); Access Keys statt Signing Key |
| Sharedien.StorageProviders.AzureBlobStorageProviders[n].{IsEnabled, ConnectionString, AccountKey, ContainerName} | Sektion | Azure ab 3.6 | Direkter Blob-Provider |
| Sharedien.Upload.UploadSigningKey | Key | ab 3.6 | Signiert Chunk-Upload-URLs |
| Sharedien.SystemEventStream.MessageCleanup.{RetentionPeriodInSeconds, CronExpression, ChunkSize, ChunkTimeoutInSeconds} | int/cron | Nein | Aufräumen persistierter Eventstream-Nachrichten (ab 3.11: `Sharedien.Cleanup.EventStreamMessageCleanup`, gemeinsamer Cron für alle Cleanup-Jobs) |
| Sharedien.Database.CreateDatabaseIfNotExists | bool | Nein | DB automatisch anlegen |
| Sharedien.Database.DbMigrationTimeout | Sekunden | Nein | Timeout der Migration beim Start |
| Sharedien.BackgroundJobs.EnableDashboard | bool | Nein | Hangfire-Dashboard |
| Sharedien.BackgroundJobs.SearchService.InternalApiCallUrl / Sharedien.SearchService.InternalApiCallUrl | URL | Ja (bis 3.6) | Search-API-URL |
| Sharedien.Search.Elasticsearch.{UserName, Password, BaseUrl} | | ab 3.7 | Elasticsearch-Zugang direkt in der API |
| Sharedien.Assets.ForbiddenFileExtensions | Array (`.exe`) | Nein | Upload-Blacklist |
| Sharedien.Assets.ParentChildRelationDeleteBehavior | `DeleteAssetSet` / `PromoteLinkToParentChild` | Nein | Verhalten beim Löschen einer Parent-Child-Relation (Asset-Set löschen oder bestehende Link-Relation zur Parent-Child-Relation befördern) |
| Sharedien.Authentication.{ServiceUrl, ExternalServiceUrl, ApiClientSecret, RedirectUrl} | | Ja | Identity-Service (intern/extern), Client-Secret des Clients `sharedien-api`, Swagger-OAuth-Redirect |
| Sharedien.Authentication.ValidationCache.CacheExpirationInSeconds | Sekunden | Nein | Token-Validierungs-Cache (auf Token-Lifetime abstimmen) |
| Sharedien.Email.* | Sektion | Nein | siehe E-Mail; `IsEnabled`, `BaseUrl` (Frontend-URL für Deep-Links) |
| Sharedien.Extractions.FileNameFilters[] {Scope: FullPath/FileName, Type: StartsWith/EndsWith/Contains, Value} | Liste | Nein | Filter beim Entpacken von Archiven; Defaults: `FileName StartsWith .`, `FullPath Contains __MACOSX`, `FileName EndsWith Thumbs.db` |
| Sharedien.FileRecognition.{IsEnabled, Scope: FilesWithoutExtension/AllFiles, Mode: Enrich/Strict, FileExtension: Append/Replace} | | Nein | Magic-Number-Dateityperkennung (siehe A.8) |
| Sharedien.RuleEngine.IsEnabled | bool | Nein | Rule Engine aktivieren (Default aus) |
| Sharedien.RuleEngine.QueryBatchSize | int | Ja (wenn aktiv) | Batchgröße der Hintergrundverarbeitung |
| Sharedien.UserAccounts.* | Sektion | Nein | siehe A.3.2 |
| Sharedien.ContactPrintMetadata.{OnePicturePerPage, TwoPicturesPerPage, SixPicturesPerPage, FifteenPicturesPerPage} | Listen von Property-Identifiern | Nein | Metadaten pro Kontaktabzug-Layout (Default `file_name`; `asset_type` seit 3.5.1 entfernt) |
| Sharedien.SearchReIndexingService.StartReIndexDelaySeconds | | obsolet ab 3.10 | |
| ConnectionStrings.{Sharedien, BackgroundJobs} | | Ja | SQL Server |

#### A.3.2 User Accounts (Sharedien API, `Sharedien.UserAccounts`)

Wiederkehrender Hintergrundjob (Cron, Default nur Bürozeiten `0 8-17/3 * * 1-5`, weil E-Mails versendet werden) mit zwei Aufgaben:

* **Account Renewal** – nur für Benutzer mit `AccessType = Expiring` (Selbstregistrierung; externe IdP-Benutzer sind immer `Permanent`; Admin kann auf `Permanent` setzen). Statusübergänge `Active → AccountRenewalPending → AccountRenewalExpired`; Reaktivierung per E-Mail-Link (`RenewAccountEndpoint`).
* **Account Deletion** – pro Status (`Inactive`, `AccountRenewalExpired`, `ApprovalDenied`, erweiterbar) `IsEnabled` + `RetentionPeriodInSeconds`; Default aus.

| Setting | Bedeutung |
|---|---|
| UserAccounts.CronExpression | Intervall; leer = Job läuft nicht |
| Renewal.IsEnabled | Default true |
| Renewal.LastLoginDateThresholdInSeconds | max. Abwesenheit bis Renewal (Beispiel 60 Tage); muss > Token-Lifetime sein |
| Renewal.RenewalWindowInSeconds | Fenster zur Reaktivierung (0 = sofort Expired ohne Einladung) |
| Renewal.ReminderIntervalInSeconds | Erinnerungsintervall (0 = keine Reminder) |
| Renewal.RenewAccountEndpoint | Link-Ziel (z. B. `Home/RenewAccount`) |
| Deletion.<STATUS>.IsEnabled / RetentionPeriodInSeconds | Löschregel je Status |

Account-Status laut API-Enum `AccountStatusDto`: Unknown, Active, Inactive, EmailVerificationPending, EmailVerificationExpired, ApprovalPending, ApprovalDenied, Locked, AccountRenewalPending, AccountRenewalExpired.

#### A.3.3 Identity Service (`Identity.*`)

| Setting | Wert | Pflicht | Bedeutung |
|---|---|---|---|
| Identity.Logging / Identity.Hosting | Sektion | Ja / Nein | |
| Identity.MessageBroker (AMQP / AzureServiceBus) | Sektion | ab 3.8 | Identity publiziert Events (Login etc.) in den Eventstream |
| Identity.Branding.{FaviconUrl, MainbackgroundImageUrl (16:9, ≤300 KB), TopbarBackgroundColor, TopbarColor, TopbarAppLogoUrl, TopbarBrandLogoUrl (≤35 px), TopbarHeaderText, TopbarHeaderTextSize} | | Nein | Branding der Login-UI |
| Identity.General.IssuerUri | URL | Nein | expliziter Token-Issuer (sonst Request-Host → IDX10214-Fehler bei internen/externen Adressen) |
| Identity.ApiResources[] {Name, DisplayName, ApiSecrets} | Liste | Ja | z. B. `sharedien-api` |
| Identity.Clients[] {ClientId, ClientName, AllowedGrantTypes (ResourceOwnerPasswordAndClientCredentials / AuthorizationCode), RequireConsent, RequireClientSecret, ClientSecrets, AllowAccessTokensViaBrowser, RedirectUris, PostLogoutRedirectUris, AllowedCorsOrigins, AllowedScopes (openid, profile, email, profile.extended, sharedien-api, offline_access), AccessTokenLifetime (Default 3600), AllowOfflineAccess, AbsoluteRefreshTokenLifetime (Default 30 Tage), SlidingRefreshTokenExpiration, SlidingRefreshTokenLifetime (Default 15 Tage), ReUseRefreshToken, RedirectAfterLogin, ShowOnLoginPage, LoginUrl, IconUrl} | Liste | Ja | Standard-Clients: `sharedien-api` (Password/ClientCredentials), `sharedien-web` (Authorization Code + PKCE seit 3.7, AllowOfflineAccess seit 3.8, RedirectUris aller Dienste `/signin-oidc` seit 3.10.1), `ci-hub` |
| Identity.Login.TokenSigningCertificate / -Key / -KeyPassword | Base64 PEM | Ja | JWT-Signaturzertifikat (unverschlüsselter Key empfohlen; auf Azure einzeilig mit `\n`) |
| Identity.Login.ExtendedAuthenticationLogging.{IsEnabled, PIILoggingEnabled} | bool | Nein | Debug-Logging von Claims/Gruppen |
| Identity.Login.AuthenticationTicketStorage.StorageType | Default / InMemory | Nein | Cookie-Größe (InMemory bei zu großen Cookies) |
| Identity.Login.Lockout.{FailedLoginAttemptsThreshold (10), LockoutDurationInSeconds (900; 0 = bis Admin entsperrt), ResetLockoutCounterAfterSeconds (600)} | | Nein | Brute-Force-Schutz; Status `Locked` |
| Identity.Login.DefaultIdentityProvider | Provider-Id | Nein | Default-IdP im Frontend |
| Identity.Login.Saml.Providers.<name>.* | | Nein | siehe A.4.1 |
| Identity.Login.OpenIdConnect.Providers.<name>.* / Identity.Login.ExternalGroupProviders.MicrosoftGraphApi.* | | Nein | siehe A.4.2 |
| Identity.Login.WindowsAuth.* | | Nein | siehe A.4.3 |
| Identity.Registration.{IsEnabled, EmailVerificationRequired, EmailVerificationExpirationInHours, PasswordResetExpirationInHours, ApprovalRequired, ApprovalMaxRecipients, VerifyEmailEndpoint, ApprovalEndpoint, ApprovalEmailFallback, ConfirmPasswordResetEndpoint} | | Nein | Selbstregistrierung mit E-Mail-Verifikation, Admin-Genehmigung, Passwort-Reset |
| Identity.Disclaimer.IsEnabled | bool | Nein | Disclaimer beim Login |
| Identity.AnonymousAccess.{IsEnabled, Username, Password} | | Nein | anonymer Zugriff (Frontend braucht zusätzlich `authentication.anonymousAccess`) |
| Identity.EmailSender.* | | bis 3.7 | ab 3.8 nach `Sharedien.Email` verschoben |
| ConnectionStrings.{Identity, BackgroundJobs (ab 3.8)} | | Ja | |

Übersetzungen der Identity-UI: `wwwroot/locales/<lang>/override.json`.

#### A.3.4 Storage Handler API (`StorageHandler.*`)

| Setting | Wert | Pflicht | Bedeutung |
|---|---|---|---|
| StorageHandler.Logging / Hosting | | Ja / Nein | |
| StorageHandler.Database.{CreateDatabaseIfNotExists, DbMigrationTimeout, ThrowOnClientEvaluation} | | Nein | |
| StorageHandler.StorageConfigurationType | `FileStorage` / `AzureBlobStorage` | bis 3.5 | ab 3.6 ersetzt durch `StorageProviders.FileSystemStorageProviders[n].{IsEnabled, StorageRootPath}` |
| StorageHandler.FileStorage.StorageRootPath | Pfad (NAS/lokal) | Ja | Dienstkonto braucht R/W |
| StorageHandler.AzureBlobStorage.{ContainerName, AccountName, AccountKey, EndpointSuffix} | | bei Blob | |
| StorageHandler.PreSignedUrl.Signing.SigningKey → ab 3.6 `Authentication.AccessKeys[] {Name, Key}` + `SharedAccessSigningKey` | | Ja | Zugriffsschlüssel für API/Processing, Signierschlüssel für Shared-Access-URLs |
| StorageHandler.Authentication.ExternalServiceUrl | URL | ab 3.10.1 | Identity-Service extern |
| StorageHandler.MessageBroker.AMQP.* | | ab 3.11 (on-prem) | |
| ConnectionStrings.{StorageHandler, Sharedien (ab 3.11)} | | Ja | |

#### A.3.5 Processing API (`Processing.*`)

| Setting | Bedeutung |
|---|---|
| Processing.Logging / Hosting / MessageBroker | Standard; `MessageBroker.AMQP.Receivers[0].MessageIngestion.CpuCoreMultiplier` (ab 3.8, ersetzt `JobListenerCount`) steuert Parallelität |
| Processing.Authentication.ExternalServiceUrl | Identity extern (ab 3.10.1) |
| Processing.StorageProviders.StorageHandlerStorageProviders[n].{IsEnabled, StorageHandlerApiUrl, AccessKey} / AzureBlobStorageProviders[n].{IsEnabled, ConnectionString, AccountKey, ContainerName} | Storage-Zugriff (ab 3.6) |
| Processing.FileCache.{TempRootPath, CacheExpirationInSeconds (600)} | Temp-Pfad (Azure `D:\home\site\temp`) |
| Processing.CustomDerivation.ExecutablePaths.{ImageMagickConvertExecutable, ImageMagickIdentifyExecutable, FFMpegExecutable, FFProbeExecutable, LibreOfficeExecutable, GhostScriptExecutable, ExifToolExecutable} | Pfade der Tools (Windows: `Processing.Dependencies\win-x64`, Linux: apt-Pakete inkl. `blender`) |
| Processing.CustomDerivation.ColorProfilesPath | ICC-Verzeichnis (Docker: Volume nach `/app/ColorProfiles`) |
| Processing.CustomDerivation.IccProfileOverrides[] {SourceProfile, TargetProfile} | Mapping ICC-Profilname → Datei |
| Processing.CustomDerivation.ColorSpaceDefaultProfiles[] {SourceColorSpace, UseColorProfile} | Default-Profil je Farbraum |
| Processing.CustomDerivation.FallbackSourceProfile | Fallback-Profil (leer = keines) |
| Processing.CustomDerivation.IccProfileKey / ColorSpaceKey | Property-Definition-Ids für ICC-Profil/Farbraum (`image_icc_profile`, `image_color_space`) |
| Processing.MetadataExtraction.Directories[] {Name: iptc/exif/xmp/file (ExifTool), audio/audio_format/video/video_format (FFprobe), Mappings[] {FromTag, ToPropertyDefinitionIdentifier}} | Konfigurierbares Tag→Property-Mapping, geordnete Mehrfachdeklarationen, "first match wins" |
| ConnectionStrings.Sharedien | ab 3.11 |

Systemeigene Property-Ids aus der Doku: `iptc_credit`, `iptc_category`, `iptc_supplemental_category`, `exif_model`, `exif_make`, `image_width`, `image_height`, `image_color_space`, `image_icc_profile`, `audio_codec_name`, `audio_sample_rate`, `audio_format_name`, `audio_artist`, `video_codec_name`, `video_duration`, `video_format_name`, `xmp_documentid`, `file_name`, `asset_type`, `title`.

#### A.3.6 EventHub (`EventHub.*`)

| Setting | Bedeutung |
|---|---|
| EventHub.Logging / Hosting / MessageBroker | Standard |
| EventHub.EventHubCallbackJob (BrokerSources.SourceName) | Quelle im Broker (Topic `Advellence.Sharedien.EventHub`, Subscription `EventHub`) |
| EventHub.Authentication.{ServiceUrl, ExternalServiceUrl, ApiClientSecret} | Identity-Anbindung (Client `sharedien-api`) |
| EventHub.SignalR.DetailedErrors | SignalR-Debug |
| ConnectionStrings.{EventHub, AzureSignalRService} | DB; optional Azure SignalR (`Endpoint=…;AccessKey=…;Version=1.0;`) |

#### A.3.7 Web UI (`configuration.overrides.js`, Objekt `sharedienAppConfig`)

| Setting | Wert | Bedeutung |
|---|---|---|
| logLevel | 0 SILENT … 4 DEBUG | Frontend-Loglevel |
| apiUrl | URL | Sharedien API |
| hubUrl | URL `…/hubs/notification` | EventHub (optional, Echtzeit) |
| enforceNoCachingOnApiCalls | bool | Cache-Buster (IE11) |
| authentication.{identityProviderUrl, identityProviderLogoutEndpoint, issuerEndpoint, discoveryDocumentEndpoint, requireHttps, skipIssuerCheck, clientId, scope} | | OIDC-Konfiguration |
| authentication.defaultExternalIdentityProvider | IdP-Id | Direktweiterleitung zum externen IdP (Umgehung: Query `external=true`) |
| authentication.userCanChangePassword / userCanLogout | bool | UI-Funktionen |
| authentication.anonymousAccess.{isEnabled, userName} | | anonymer Benutzer |
| branding.sidebar.appLogo.url, branding.topbar.{brandLogoUrl, headerText, appLogo.url}, branding.bottombar.links[] {name/href oder nameTranslationKey/hrefTranslationKey}, branding.faviconUrl (png) | | Logos/Links (relativ zu `configuration/`) |
| branding.theming.themes[] {id, name, extends}, branding.theming.default, branding.theming.userCanSwitchThemes | | Themes (PREVIEW): CSS-Dateien in `configuration/themes/<id>.css` mit CSS Custom Properties (`--colorPrimary`, `--sidebar…`, `--topbar…`, `--bottombar…`), Vererbung via `extends`, Basis `sharedien_light_theme`; Benutzerauswahl wird gespeichert |
| uiSpecification.entityCardInfoIconEnabled | bool | Info-Icon auf Karten |
| uiSpecification.entityCardExpiryBannerThresholdDays | Zahl | Ablauf-Banner (Lizenzen/Expiry) |
| uiSpecification.avatar.maxFileSizeBytes | Zahl | Avatar-Limit |
| uiSpecification.parentChildRelationDeleteBehavior | `DeleteAssetSet` / `PromoteLinkToParentChild` | wie API-Setting |
| uiSpecification.archiveExtractionSettings.{archiveExtractionMode: ExtractWithFullPath/ExtractFilesOnly/NoExtraction, deleteAssetAfterExtraction} | | Default für Archiv-Entpacken |
| uiSpecification.paging.pageSizes | Array | Seitengrößen (erste = Default) |
| uiSpecification.urlMaxLength | Zahl | max. URL-Länge (Query-Parameter) |
| uiSpecification.sidebar.pinnedCustomEntities[] {entityTypeIdentifier, icon (FontAwesome)} | | Custom Entities in der Hauptnavigation statt Untermenü "Data" |
| drm.purchaseHelpLink {link, text} | | Hilfe-Link im Kaufdialog des DRM-/Lizenzmoduls |
| support.emailAddress | String | Support-Adresse in der UI |

Übersetzungs-Overrides: `dist/locales/<lang>/override.json` (z. B. `entities.user`). Deprecated Query-Parameter `c` → `sq_c` (ab 3.10.2, Entfernung 3.15). Migration 3.10: alte Branding-Flags (`sidebar.appLogo.isEnabled`, `topbar.isEnabled`, Farben …) sind nur noch als Theme-Properties möglich. CI HUB benötigt zusätzlich `configuration.json` mit `apiUrl` und `identityProviderUrl`.

#### A.3.8 Frontend Windows Service (`Frontend.Hosting`) – nur Hosting-Sektion.

#### A.3.9 Querschnittskonfiguration (alle Dienste, `<APPNAME>.*`)

**Hosting:** `PathBase` (Sub-Pfad, muss mit `/` beginnen; Swagger-Pfade werden umgeschrieben), `UseForwardedHeaders`, `KnownProxies[]` (IPv4/IPv6/`::ffff:`), `KnownNetworks[] {Address, Prefix}`, `OverrideForwardedHeaders`, `ForwardedSchemeOverride`, `ForwardedHostOverride`, `EnableHttpsRedirection`.

**Logging (Serilog-artig, strukturierte JSON-Logs):** `LogLevel` (Verbose…Fatal, Default Information), `MicrosoftLogLevel`, `LogDirectory`, `LogFileBaseName` (Pflicht), `LogFileExtension`, `WritePlainTextLogFile`, `PlainTextLogFileBaseName/-Extension`, `InstanceIdentifierEnvironmentVariable` (Instanz-Id im Dateinamen bei Scale-out), `Application`, `Tenant` (Pflicht), `Environment` (Pflicht), `DefaultSourceContext`, `Shared` (Multi-Prozess, Performance), `SelfLogEnabled`, `HttpApiRequestResponseLoggingEnabled` (temporär), `ConsoleLogging.Enabled`, `HttpLogging.{Enabled, ServerUrl, BasicAuthUserName, BasicAuthPassword}` (Logstash/HTTP-Sink; auf Azure durch App Insights abgelöst), `LogSanitation.{IsEnabled, LogPropertiesToSanitize[], LogValueReplacement}` (PII-Maskierung, Default: Benutzername, E-Mail …). Application Insights: `ApplicationInsights__InstrumentationKey` (alle Dienste, on-prem und Azure).

**E-Mail (`Sharedien.Email`, ab 3.8 nur in der API):** `IsEnabled`, `BaseUrl`, `SmtpServer`, `SmtpPort`, `UseSSL`, `Username`, `Password`, `SenderName`, `SenderAddress`, `ConnectTimeoutInSeconds`/`AuthenticateTimeoutInSeconds`/`SendTimeoutInSeconds`/`DisconnectTimeoutInSeconds` (je 5), `CooldownBetweenMessagesInMilliseconds` (Bulk), `AllowedRecipients[]` (Whitelist mit `*@domain`-Wildcards, für Testsysteme), `EmailQueue.RetentionPeriodInSeconds` (86400) und `EmailQueue.CronExpression` (`*/30 * * * *`) – **persistente Mail-Queue mit Retry**. Beispiele Office365 und SendGrid (`apikey`).

**Message Queue (`<APPNAME>.MessageBroker`):** AMQP 1.0 (`AMQP.{IsEnabled, Protocol amqp/amqps, ServerAddress, ServerPort, UserName, Password, X509ClientCertificateFile (ASN.1 DER), X509ClientCertificatePassword, X509ServerCertificateNoValidation}`) – offiziell ActiveMQ classic und Artemis; oder `AzureServiceBus.{IsEnabled, ConnectionString}`. Bis 3.7 wurden Quellen/Ziele pro Job konfiguriert (`…MessageBrokerManager__MessageBrokers__0__BrokerSources__0__SourceName`), ab 3.8 zentral.

**Connection Strings:** SQL Server, `Encrypt=True` nur mit gültigen Zertifikaten, Windows Integrated Security möglich (Dienstkonto). Azure-Export mit Auditing → Master-Key-Patch (`RemoveMasterKeyLT4GB.ps1`) für On-Prem-Import.

**Azure:** Settings als `A__B__C`, Arrays `A__B__0`, Restart nötig; App Insights vorkonfiguriert.

### A.4 Identity-Varianten

#### A.4.1 SAML 2.0 (Identity Service als SP/Relying Party)
Mehrere IdPs parallel (Auswahl beim Login). SP-Metadaten unter `/Saml2` (bzw. `ModulePath` je Provider bei mehreren). Pro Provider `Identity.Login.Saml.Providers.<id>`: `EntityID`, `DisplayName`, `ServiceProviderIdentifier` (mit Umgebungssuffix, z. B. `sharedien-saml-login-q`), `IdpMetadata` (URL oder Datei), `SignRequest`, `ModulePath`, `SignatureAlgorithm` (SHA1/256/512), `ServiceProviderCertificate`/`ServiceProviderKey`/`ServiceProviderKeyPassword` (Base64 PEM, aus pfx via openssl), `ProtocolBinding` (HttpPost), `NameIdFormat` (unspecified/emailAddress/persistent/transient), `AssignedUserGroups[]` (interne Gruppen für alle Logins über diesen IdP), `AttributeMapping.{GivenName, FamilyName, Email, UserName, ExternalGroups[] (Claim-Typen; externe Gruppe → mehrere interne Gruppen)}`. Benötigte Claims: NameID, First/Last Name, Email, User name, Group (mehrfach). ADFS-Claim-Rules-Beispiele (tokenGroups, Filter per Regex). Zertifikatswechsel: Base64 austauschen, Restart, IdP-Metadaten-Refresh. Fehlerbilder IDX10214 (Issuer/Audience).

#### A.4.2 Azure AD via OpenID Connect + Microsoft Graph
App-Registrierung (delegated: email, offline_access, openid, profile, user.read; application: directory.read.all mit Admin-Consent; Redirect `<identity>/signin-oidc`; ID-Token implicit grant; optionalClaims email/given_name/family_name). Konfiguration `Identity.Login.OpenIdConnect.Providers.<id>.{DisplayName, Authority, MetadataAddress, ClientId, ClientSecret, Scopes[], AttributeMapping.{ExternalGroupProvider=MicrosoftGraphApi, GivenName, FamilyName, UserName (objectidentifier), Email}, AssignedUserGroups[]}` und `Identity.Login.ExternalGroupProviders.MicrosoftGraphApi.{TenantId, ClientId, ClientSecret, MembershipType Direct/Transitive}`. Gruppen werden per Graph API gelesen (Token-Limit 200/150 Gruppen), Mapping über Gruppen-GUID (stabil), nicht Namen.

#### A.4.3 Windows Authentication (IIS + LDAP)
`Identity.Login.WindowsAuth.{IsEnabled, DisplayName, AutomaticAuthentication, Ldap.{Host, Port, UseSSL, BindDistinguishedName, BindCredentials, SearchBase, SearchFilter (mit `{0}`)}, AttributeMapping.{ExternalGroups[] (memberOf), GivenName, FamilyName, UserName (sAMAccountName), Email}, AssignedUserGroups[]}`.

#### A.4.4 Token-Handling
Access Token 60 min Default; Refresh Tokens nur mit `AllowOfflineAccess` (Flows: authorization code, hybrid, ROPC), absolute (30 d) und sliding (15 d) Lifetime, `ReUseRefreshToken`; Public Clients sollen rotieren. Token-Request: `POST /connect/token` mit `grant_type=password|refresh_token`, Client-Id `sharedien-api` (Basic-Header `c2hhcmVkaWVuLWFwaTo=` oder Body), Scope `sharedien-api offline_access`; Beispiele curl/C#/IdentityModel.

### A.5 Monitoring: Log-Korrelation
Jeder Dienst liefert `X-Operation-ID` im Response-Header; die Id wird über alle Folgeaufrufe propagiert und steht in allen JSON-Log-Events als `OperationId`; das Frontend zeigt sie bei Fehlern (Helpdesk). Azure: Kusto-Queries über `operation_Id` bzw. `customDimensions.OperationId` (requests/dependencies/traces/exceptions). On-prem: Kunde ingestiert JSON-Logs (Splunk/ELK).

### A.6 Integrationen

* **OpenAPI/Swagger:** jede API bietet `/swagger` (UI mit OAuth2/PKCE) und `/swagger/<version>/swagger.json`; Tags nach Controller-`ApiExplorerSettings.GroupName`; Schema-Ids für Generics (`SearchResultOfAssetDto`); XML-Kommentare als Beschreibungen.
* **API-Versionen:** max. 4 parallele Versionen (aktuell v6, plus v5, v4, v3; v1/v2 abgekündigt, laut Code noch vorhanden); URL-Versionierung `/api/v{n}/…`; neue Version nur bei Breaking Changes (DTO-Felder entfernt, Parameter geändert). Versioniert/abwärtskompatibel sind nur: Asset-Ops (ohne Activity Log), Asset Types (GET), Asset Upload, Categories (ohne Resource Permissions), Comments, Custom Entities (ohne Resource Permissions), Data Languages (GET), Derivation Types (GET), Extractions, Jobs, Property Definitions (GET), Properties (nur Mass-Edit-Jobs), Relations (GET), User Contexts (GET), Tasks (ohne Activity Log), Workspaces (ohne Activity Log). Alles andere "use at own risk". 3.11: `IgnoreCopyingOnNewVersion` → `IgnoreCopying`.
* **Eventstream-Topic:** AMQP `topic://VirtualTopic.Sharedien.EventStream` (ab 3.4; durable Consumer mit Client-Id, connect/disconnect/reconnect) bzw. Azure Service Bus Topic `advellence.sharedien.eventstream` (eigene Subscription je Integration, TTL 7 d, Lock 1 min, MaxDelivery 1, Listen-only SAS-Policy). Message-Properties für Filter: `x_sharedien_event_action` (Create/Read/Update/Delete), `x_sharedien_event_action_details` (z. B. NewAssetSetCreated, SuccessfulLogin), `x_sharedien_object_type` (AssetSet, Property …), `x_sharedien_object_id`, `x_sharedien_user_id`, `x_sharedien_property_definition_id`; AMQP-Selector-Filter (`apache.org:selector-filter:string`) bzw. Service-Bus-Correlation/SQL-Filter. Payload = `DataEventBase` (AffectedData[], UtcTimeStamp, EventCategory SystemHealth/DataEntity, AdditionalData, User) – vollständige Event-Liste in Teil C.3. Interne Topics: `Advellence.Sharedien.EventHub` (SignalR-Relais), `Advellence.Sharedien.Internal` (ab 3.11, Subscriptions Api/Processing/Identity/EventHub).
* **Upload-Flow (Integrationen):** `POST /assets/upload/start` (`UploadStartRequestDto` mit `assetUploadType` Asset/AssetDerivation/AssetVersion, `fileName`, `asset.{parentCategoryIdentifier, workspaceIdentifier, taskIdentifier}` bzw. `assetDerivation.{assetSetIdentifier, derivationTypeIdentifier, assetVersionIdentifier}` bzw. `assetVersion.assetSetIdentifier`) → `UploadStartResponseDto` (chunkUploadRequestUrl, maxChunkSizeBytes) → `GET /assets/upload/chunk` (`ChunkUploadResponseDto`: uploadUrl, chunkIdentifier, nextChunkUploadRequestUrl, finishUploadRequestUrl) → `PUT <Storage-URL>` je Chunk → `PUT/POST /assets/upload/finish` (`UploadFinishRequestDto`: chunkIdentifiers, optional `copyMetadataFrom[]`, `createRelations[]`) → `UploadFinishResponseDto` (assetSetIdentifier / assetVersionIdentifier, CopyMetadataResults, CreateRelationResults). Binärdaten gehen direkt an den Storage-Provider (Shared-Access-URLs), nicht durch die API.
* **CI HUB (Adobe/Office-Panel-Integration):** Identity-Client `ci-hub` (AuthorizationCode, ohne Secret, Access Token 24 h, Refresh 30 d, Redirect `https://ci-hub.azurewebsites.net/api/v1/auth/login/sharedien`, nicht auf Login-Seite) + `configuration.json` im Frontend.

### A.7 Metadata
* **Value Types** (`PropertyValueTypeDto`): String (2^30-1 Bytes), Text (mehrzeilig), Date, DateTime, Time, Integer (int64), Float, Decimal (Precision 18, Scale 10), Boolean, TimeSpan.
* **File Hash:** MD5 als Base64 pro Datei, gespeichert als Metadaten-Property (Duplikaterkennung, `AssetSetDuplicate`-Permission).

### A.8 Assets: Renditions und File Recognition
* **Standard-Renditions:** Thumbnail (jpg 240×240, 72 dpi, sRGB), Animated thumbnail (gif 240×240, Video-Sequenz), Screen (jpg 1600×1200, 96 dpi, sRGB), Print (tiff 3508×3508, 300 dpi, CMYK ISO Coated v2 ECI). Mapping: Bilder (jpg/png/bmp/tif/gif/eps/psd/psb) → Thumbnail+Screen+Print; Videos (avi/mp4/mov/wmv/ogv/mpg/webm/3gp/3g2/mkv/mk3d) → Thumbnail+Animated; pdf/ai/ppt(x)/doc(x)/xls(x) → Thumbnail; indd → keine. Custom ICC-Profile im `ColorProfiles`-Ordner.
* **File Recognition (Magic Numbers):** optional (`Sharedien.FileRecognition`), gilt für Asset-, Versions-, Derivations-Upload und Archivinhalte. Scope `FilesWithoutExtension` (Default) / `AllFiles`; Mode `Enrich` (best effort, evtl. Umbenennung) / `Strict` (unbekannt = Ablehnung; bei Mehrfachtreffern Typ "Unknown"); FileExtension `Append` (`file.gif.jpg`) / `Replace`. Konfliktauflösung: bekannte Extension gewinnt → längste Signatur → sonst Unknown. Tabelle mit ~110 Endungen und Asset-Typen: Adobe Illustrator (ai), Adobe InDesign (indd), Archive (7z, rar, zip), Audio (aac, aif, au, f4a, flac, m4a, m4b, mka, mp3, oga, ogg, wav, wma), CAD (3ds, dwg, dxf, iges, igs, obj, sat, stl, stp, x_t), Image (bmp, eps, gif, ico, jpeg/jpg, pbm, png, psb, psd, raw, svg, tga, tif, tiff), Movie (3g2, 3gp, avi, f4v, flv, mk3d, mkv, webm, mov, mp4, mpeg, mpg, mxf, ogv, wmv), MS Excel (xls, xlsb, xlsx, xlt, xltx, xlw), MS PowerPoint (pot, potx, pps, ppsx, ppt, pptx), MS Word (doc, docx, dot, dotm, dotx, rtf, txt), PDF, Unknown/RAW-Kameraformate (3fr, arw, cr2, crw, dcr, dng, erf, mef, mos, mrw, nef, nrw, orf, pef, pgm, ppm, raf, rw2, sr2, srf, srw, x3f). Basis: FileTypeChecker (0xbrock) mit Anpassungen.

### A.9 Web User Interface
Erzwungener manueller Login trotz Auto-SSO über Query-Parameter `external=true`. UI-Bereiche laut Doku/Permissions: Dashboard, Asset Browser, Asset Detail, Download Basket, Uploads, Admin (Users/Groups/Tenants/Categories/Configuration/Search-Reindex), Custom Entity Browser, Collections, Workspaces, Notifications (Glocke), Kontaktabzüge (Contact Prints), DRM-/Lizenz-Kaufdialog, Themes, Theme-Switch in Benutzereinstellungen.

### A.10 Update Notes – Feature-Historie (3.5.1 → 3.11.0)

| Release | Änderungen / neue Features (laut Update Notes) |
|---|---|
| 3.5.1 | `asset_type` aus Contact-Print-Defaults entfernt (Sharedien.ContactPrintMetadata konfigurierbar) |
| 3.5.2 | Asset-Index: exaktes Filename-Feld (Reindex) |
| 3.6.0 | Storage-Provider-Modell (StorageHandler-/AzureBlob-Provider in API und Processing), Access Keys + SharedAccessSigningKey, `Upload.UploadSigningKey`, Storage Handler auf Azure überflüssig, ExifTool als neue Processing-Abhängigkeit, Blob-CORS PUT |
| 3.7.0 | Frontend auf OAuth Authorization Code Flow, ImageMagick 7.0.10-16, Search API in die Sharedien API integriert (`Sharedien.Search.Elasticsearch`), Reindex AssetSet/Workspace/Category/Task, `PermissionDto.AssetRelation*` → `Relation*` |
| 3.7.2 | Application Insights für Logging (ELK abgelöst), ImageMagick 7.0.10-30 |
| 3.8.0 | Zentrale `MessageBroker.AMQP`/`AzureServiceBus`-Konfiguration für API/Identity/Processing, `CpuCoreMultiplier`, Processing-Temp-Pfad, Service-Bus Lock 5 min/MaxDelivery 10, Identity braucht `BackgroundJobs`-DB, E-Mail-Konfiguration von Identity in die API verschoben, `sharedien-web` AllowOfflineAccess, **EventHub-Service + SignalR-Echtzeit-Updates** (`hubUrl`) |
| 3.9.0 | **API v5** (v1 abgekündigt), Custom Favicon, Service-Bus-Topic `Advellence.Sharedien.EventHub`, **Blender** (3D-Renditions), Reindex BillingContact/RightsOwner/License (**Lizenzmodul**) |
| 3.10.0 | **API v6** (v2 abgekündigt), kein IE11, EventHub on-prem (Windows Service/Docker, Ports 5008/4008), Elasticsearch 7.10.2, **Theming**, Reindex LicensePurchase |
| 3.10.1 | RedirectUris `/signin-oidc` aller Dienste, `Authentication.ExternalServiceUrl` in Storage Handler/Processing/EventHub |
| 3.10.2 | UI-Query-Parameter `c` → `sq_c` |
| 3.11.0 | Breaking: `IgnoreCopyingOnNewVersion` → `IgnoreCopying` (Property Definitions); Cleanup-Sektion `Sharedien.Cleanup.EventStreamMessageCleanup`; Topic `Advellence.Sharedien.Internal` mit Subscriptions Api/Processing/Identity/EventHub; Processing und Storage Handler erhalten `ConnectionStrings.Sharedien`; Storage Handler on-prem erhält MessageBroker |

### A.11 Weitere Repo-Dokumente
* `docs/api-diff`: Workflow zum Diff zweier Swagger-Versionen mit `openapitools/openapi-diff` (HTML/Markdown/Text); `patch_swagger.ps1` ersetzt `/api/vX/` durch `/api/` zur Vermeidung falscher Diffs. Die abgelegten `v1.json`/`v2.json` sind **nur Beispiel-Stubs** mit dem Endpoint `GET/PATCH /api/application-settings` (Schemas `ConfigurationValueDto`, `ConfigurationValueExtendedDto`, `ConfigurationProviderDto`, `SetConfigurationValuesDto`; Security-Scheme OAuth2 Authorization Code gegen `adv-sharedien-id-dev.azurewebsites.net`, Scope `sharedien-api`). Unterschied v1→v2 in diesem Stub: neuer Query-Parameter `overrides` und neue `PATCH`-Operation `SetApplicationSettings`.
* `src/Backend/Readme.md`: Build (.NET Core 2.1+/3.1, SQL Server 2016 SP1, EF-Codegen `RebuildDbContextAndModels.ps1`), privater NuGet-Feed, Launch-Profile, lokale End-to-End-Startreihenfolge (`run-all.bat`: API 5001, StorageHandler 5000, Processing 5002, Search 9201, Web).
* `src/Search/Readme.md`: Elasticsearch-Konzept (Ingest-Attachment, Aliase, Zero-Downtime-Reindex via Alias-Swap, Mapping-Änderungen erfordern Reindex), Tools ElasticHQ/DejaVu.
* `src/UAA/Readme.md`: historisches UAA-Setup (Identity Zones als Mandanten, `accessTokenValiditySeconds` 86400).
* `src/SDK`: internes Azure-Artifacts-Feed, externes GitHub-Packages-Feed (TBD), Lizenzhinweis.

## Teil B: API-Inventar – alle 463 Operationen der Sharedien API

Quelle: `ClientGenerated.cs` (NSwag, generiert aus `Sharedien.Api.dll`, Stand API v6) – deckungsgleich mit den 50 Controllern. Die abgelegten `v1.json`/`v2.json` enthalten nur `application-settings` und sind als Spec unbrauchbar (siehe 0.1, A.11).

Lesehinweise:
* **Pfad** enthält die API-Version (`/api/v6/…`). Operationen älterer Versionen (v1–v5) sind nur dort enthalten, wo die Signatur abweicht; NSwag hängt dann `2`, `V1`, `V2`, `V3`, `V1V2` an die OperationId an (z. B. `SearchAssets2` = v5-Variante mit Offset-Paging).
* **Parameter**: `x_Data_Languages` und `x_User_Contexts` sind die HTTP-Header `X-Data-Languages` (kommaseparierte Datensprachen) und `X-User-Contexts`, keine Query-Parameter. `langFallback`, `allDataLanguages`, `limit`, `offset`, `cursor` sind Query-Parameter. *(path)* markiert Pfadparameter.
* **Request-Body**: DTO-Typ (JSON) bzw. `form` für multipart/form-data (Datei-Uploads: `FileParameter`).
* **Response**: Typ + erwarteter Statuscode; `(leer)` = 204 No Content. `FileResponse` = Binärstream (Zip, PDF, Excel).
* Authentifizierung: OAuth2 Bearer (Scope `sharedien-api`), Ausnahme Tag `Public` (Token des Share-Empfängers).
* Alle Operationen sind auch in B.1 nach Tag gezählt; Tag = Controller-Name.

### B.1 Übersicht nach Tag/Controller

| Tag (Controller) | Ops | GET | POST | PUT | PATCH | DELETE | Versionen | Pfad-Präfixe |
|---|---|---|---|---|---|---|---|---|
| ApplicationSettings | 6 | 3 | 1 | 0 | 1 | 1 | v6 | `/application-settings` |
| Assets | 47 | 10 | 28 | 2 | 2 | 5 | v2, v3, v4, v5, v6 | `/assets` |
| AssetTypes | 1 | 1 | 0 | 0 | 0 | 0 | v6 | `/asset-types` |
| AssetUpload | 3 | 1 | 2 | 0 | 0 | 0 | v6 | `/assets` |
| Categories | 30 | 6 | 7 | 11 | 3 | 3 | v6 | `/categories` |
| Comments | 3 | 1 | 0 | 1 | 0 | 1 | v6 | `/comments` |
| CustomEntities | 26 | 6 | 9 | 5 | 2 | 4 | v4, v5, v6 | `/custom-entities` |
| DataLanguages | 5 | 2 | 1 | 1 | 0 | 1 | v6 | `/data-languages` |
| DerivationJobDefinitions | 5 | 2 | 1 | 1 | 0 | 1 | v6 | `/derivation-job-definitions` |
| Derivations | 6 | 3 | 3 | 0 | 0 | 0 | v6 | `/derivations` |
| DerivationTypes | 6 | 3 | 1 | 1 | 0 | 1 | v6 | `/derivation-types` |
| EntityTypes | 6 | 3 | 1 | 1 | 0 | 1 | v6 | `/entity-types` |
| ExternalGroups | 6 | 3 | 1 | 1 | 0 | 1 | v1, v5, v6 | `/external-groups` |
| Extractions | 2 | 1 | 1 | 0 | 0 | 0 | v6 | `/extractions` |
| FileNamingDefinitions | 7 | 3 | 2 | 1 | 0 | 1 | v6 | `/file-naming-definitions` |
| FileReferences | 3 | 1 | 1 | 0 | 0 | 1 | v6 | `/files` |
| FileTypes | 2 | 2 | 0 | 0 | 0 | 0 | v6 | `/file-types` |
| Info | 1 | 1 | 0 | 0 | 0 | 0 | v6 | `/info` |
| Jobs | 14 | 14 | 0 | 0 | 0 | 0 | v1, v6 | `/jobs` |
| DrmLicenses | 58 | 14 | 24 | 10 | 3 | 7 | v5, v6 | `/drm` |
| My | 7 | 2 | 1 | 3 | 0 | 1 | v6 | `/my` |
| ObjectTypePermissions | 1 | 1 | 0 | 0 | 0 | 0 | v6 | `/object-type-permissions` |
| PermissionMaterializations | 2 | 1 | 1 | 0 | 0 | 0 | v6 | `/permission-materializations` |
| Properties | 14 | 2 | 10 | 1 | 0 | 1 | v5, v6 | `/properties` |
| PropertyDefinitions | 8 | 5 | 1 | 1 | 0 | 1 | v1, v5, v6 | `/property-definitions` |
| PropertyGroups | 6 | 3 | 1 | 0 | 1 | 1 | v6 | `/property-groups` |
| PropertySets | 5 | 2 | 1 | 1 | 0 | 1 | v6 | `/propertysets` |
| Public | 3 | 1 | 2 | 0 | 0 | 0 | v6 | `/public` |
| RecycleBin | 2 | 1 | 0 | 0 | 0 | 1 | v6 | `/recycle-bin` |
| Relations | 1 | 0 | 1 | 0 | 0 | 0 | v6 | `/relations` |
| RelationTypes | 6 | 2 | 1 | 2 | 0 | 1 | v6 | `/relation-types` |
| Roles | 9 | 2 | 1 | 5 | 0 | 1 | v6 | `/roles` |
| Rules | 9 | 5 | 2 | 1 | 0 | 1 | v6 | `/rules` |
| SavedSearches | 11 | 5 | 2 | 2 | 0 | 2 | v6 | `/saved-searches` |
| Search | 4 | 2 | 1 | 0 | 0 | 1 | v6 | `/search` |
| Shares | 4 | 0 | 1 | 1 | 0 | 2 | v6 | `/shares` |
| Tasks | 28 | 7 | 10 | 6 | 2 | 3 | v5, v6 | `/tasks` |
| Tenants | 7 | 3 | 1 | 2 | 0 | 1 | v6 | `/tenants` |
| UserContexts | 5 | 2 | 1 | 1 | 0 | 1 | v6 | `/user-context` |
| UserGroups | 7 | 2 | 2 | 1 | 0 | 2 | v6 | `/user-groups` |
| Users | 13 | 5 | 3 | 3 | 0 | 2 | v6 | `/users` |
| WatermarkDefinitions | 9 | 3 | 2 | 2 | 0 | 2 | v6 | `/watermark-definitions` |
| Workspaces | 65 | 20 | 24 | 11 | 3 | 7 | v4, v5, v6 | `/workspaces` |

### B.2 Vollständige Endpoint-Liste (nach Tag)

#### ApplicationSettings (6 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/application-settings` | GetApplicationSettings | Retrieve a list of application settings. | `path`:string; `overrides`:bool? |  | IList<ConfigurationValueExtendedDto> (200) |
| PATCH | `/api/v6/application-settings` | SetApplicationSettings | Set multiple application settings. |  | SetConfigurationValuesDto | (leer) (204) |
| GET | `/api/v6/application-settings/{path}` | GetApplicationSetting | Retrieve an application setting by path. | `path`:string *(path)* |  | ConfigurationValueExtendedDto (200) |
| DELETE | `/api/v6/application-settings/{path}` | DeleteApplicationSetting | Delete an application setting. | `path`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/application-settings/bulk-delete` | DeleteApplicationSettings | Delete multiple application settings. |  | DeleteConfigurationValuesDto | (leer) (204) |
| GET | `/api/v6/application-settings/activity-logs` | GetApplicationSettingsActivityLogs | Retrieve the activity log for application settings. | `path`:string; `x_Data_Languages`:string; `langFallback`:bool?; `allDataLanguages`:bool?; `limit`:int?; `offset`:int? |  | IList<ActivityLogDto> (200) |

#### Assets (47 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/assets/search` | SearchAssets | Returns a list of assets for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `cursor`:string | AssetSearchQueryDto | SearchResultOfAssetV6Dto (200) |
| POST | `/api/v5/assets/search` | SearchAssets2 |  | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int? | AssetSearchQueryDto | SearchResultOfAssetDto (200) |
| POST | `/api/v6/assets/quicksearch` | QuickSearchAssets | Returns a list of auto-complete suggestions or quick search assets ("did you mean" assets / or direct hits) for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `includeQuickSearch`:bool?; `includeSuggestions`:bool?; `limit`:int?; `offset`:int? | AssetSearchQueryDto | QuickSearchResultOfAssetDto (200) |
| GET | `/api/v6/assets/{assetIdentifier}` | Get | Returns a single asset for the provided asset identifier. | `assetIdentifier`:string *(path)*; `assetVersionIdentifier`:string; `x_Data_Languages`:string; `langFallback`:bool?; `includeCategoryProperties`:bool?; `includeRestrictions`:bool? |  | AssetDto (200) |
| DELETE | `/api/v6/assets/{assetIdentifier}` | Delete | Deletes the specified asset. | `assetIdentifier`:string *(path)*; `recycleBin`:bool? |  | (leer) (204) |
| GET | `/api/v6/assets/{assetIdentifier}/properties/{propertyIdentifier}` | GetAssetProperty | Returns a specific metadata property of an asset. If no version is provided, the metadata property of the latest version will be returned. | `assetIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | PropertyDto (200) |
| PUT | `/api/v6/assets/{assetIdentifier}/properties/{propertyIdentifier}` | UpdateAssetProperty | Update a existing metadata property of an asset. Will drop property values not included in this operation. | `assetIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyDto | RowVersionDto (200) |
| PATCH | `/api/v6/assets/{assetIdentifier}/properties/{propertyIdentifier}` | EditAssetProperty | Edit a metadata property of an asset. Does not delete existing property values that were not sent as part of the request. | `assetIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyDto | RowVersionDto (200) |
| DELETE | `/api/v6/assets/{assetIdentifier}/properties/{propertyIdentifier}` | DeleteAssetProperty | Delete a metadata property of an asset. | `assetIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/assets/{assetIdentifier}/properties` | GetAssetProperties | Returns an asset's metadata properties. If no version is provided, the metadata properties of the latest version will be returned. | `assetIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool?; `assetVersionIdentifier`:string; `propertyDefinitionIdentifiers`:IList<string> |  | IList<PropertyDto> (200) |
| POST | `/api/v6/assets/{assetIdentifier}/properties` | CreateAssetProperty | Create and add a metadata property to an asset. | `assetIdentifier`:string *(path)*; `assetVersionIdentifier`:string | CreatePropertyDto | IdentifierRowVersionDto (200) |
| PUT | `/api/v6/assets/{assetIdentifier}/properties` | PutAssetProperties | Create or update multiple properties on an asset. Values of existing properties will be replaced by the values from the request. In case of translated properties, omitting a language will drop the value of that language. In case of user context properties, omitting a user context will drop the value of that user context. Properties that exist on the asset but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `assetIdentifier`:string *(path)*; `assetVersionIdentifier`:string | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| PATCH | `/api/v6/assets/{assetIdentifier}/properties` | PatchAssetProperties | Create or patch multiple properties on an asset. Values of existing properties will be patched by the values from the request. In case of translated properties, omitting a language will ignore the value of that language. In case of user context properties, omitting a user context will ignore the value of that user context. Properties that exist on the asset but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `assetIdentifier`:string *(path)*; `assetVersionIdentifier`:string | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| POST | `/api/v6/assets/{assetIdentifier}/bulk-delete-properties` | DeleteAssetProperties | Deletes existing properties of an asset.  Returns a result for each property indicating whether the property was successfully deleted. | `assetIdentifier`:string *(path)*; `assetVersionIdentifier`:string | DeletePropertiesDto | IList<DeletePropertyResultDto> (200) |
| POST | `/api/v6/assets/stream-formdata` | DownloadCompressedAssetDerivationsForm | Generates a Zip file of specified derivations for the specified assets. This endpoint can be used to facilitate the download of a Zip file by a Browser through an HTML form with an encoding type of 'multipart/form-data'. The "zipDownloadRequest" object is expected as JSON encoded form data part. | `zipDownloadRequest`:IO.Stream |  | FileResponse () |
| POST | `/api/v6/assets/stream` | DownloadCompressedAssetDerivations | Generates a Zip file of specified derivations for the specified assets. | `accept`:string | ZipDownloadRequestDto | FileResponse () |
| POST | `/api/v6/assets/bulk-delete` | BulkDeleteAssets | Deletes assets in bulk. A success status response indicates all assets have been deleted. | `recycleBin`:bool? | DeleteAssetsDto | (leer) (204) |
| GET | `/api/v6/assets/{assetIdentifier}/activity-logs` | GetAssetActivityLogs | Retrieve the activity log for the specified asset. | `assetIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `allDataLanguages`:bool?; `limit`:int?; `offset`:int? |  | IList<ActivityLogDto> (200) |
| GET | `/api/v6/assets/{assetIdentifier}/derivations` | GetDerivationTypes | Returns a list of derivations for the specified asset. If no version is provided, the latest version will be used. | `assetIdentifier`:string *(path)*; `assetVersionIdentifier`:string |  | IList<AssetDerivationDto> (200) |
| GET | `/api/v6/assets/{assetIdentifier}/derivations/{derivationTypeIdentifier}` | GetAvailableDerivationsByType | Retrieve a derivation for the specified asset. | `assetIdentifier`:string *(path)*; `derivationTypeIdentifier`:string *(path)*; `assetVersionIdentifier`:string |  | AssetDerivationDto (200) |
| POST | `/api/v6/assets/{assetIdentifier}/derivations/{derivationTypeIdentifier}` | StartOnDemandDerivation | Creates and enqueues a derivation of a on-demand derivation type for execution. | `assetIdentifier`:string *(path)*; `derivationTypeIdentifier`:string *(path)*; `assetVersionIdentifier`:string |  | IdentifierRowVersionDto (200) |
| GET | `/api/v6/assets/{assetIdentifier}/derivation-types` | GetPotentialDerivationTypesOfAsset | Retrieve all potential derivation types for the specified asset. | `x_Data_Languages`:string; `langFallback`:bool?; `assetIdentifier`:string *(path)*; `assetVersionIdentifier`:string |  | IList<DerivationTypeDto> (200) |
| POST | `/api/v6/assets/{assetIdentifier}/derivations/upload/chunks/start` | StartNewDerivationUpload |  | `assetIdentifier`:string *(path)*; `assetVersionIdentifier`:string | CreateAssetDerivationDto | StartUploadResponseDto (200) |
| POST | `/api/v6/assets/{assetIdentifier}/derivations/upload/chunks/sessions/{uploadSessionId}/sequencenumber/{chunkUploadSequenceNumber}` | AppendDerivationChunk |  | `assetIdentifier`:string *(path)*; `uploadSessionId`:string *(path)*; `chunkUploadSequenceNumber`:long *(path)* |  | (leer) (204) |
| DELETE | `/api/v6/assets/{assetIdentifier}/derivations/upload/chunks/sessions/{uploadSessionId}` | CancelDerivationUpload |  | `assetIdentifier`:string *(path)*; `uploadSessionId`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/assets/{assetIdentifier}/derivations/upload/chunks/sessions/{uploadSessionId}/finish` | FinishUpload |  | `assetIdentifier`:string *(path)*; `uploadSessionId`:string *(path)*; `assetVersionIdentifier`:string |  | CreateAssetDerivationResponseDto (200) |
| POST | `/api/v6/assets/upload/chunks/start` | StartNewUpload |  |  | CreateAssetDto | StartUploadResponseDto (200) |
| POST | `/api/v6/assets/upload/chunks/sessions/{uploadSessionId}/sequencenumber/{chunkUploadSequenceNumber}` | AppendChunk |  | `uploadSessionId`:string *(path)*; `chunkUploadSequenceNumber`:long *(path)* |  | (leer) (204) |
| DELETE | `/api/v6/assets/upload/chunks/sessions/{uploadSessionId}` | CancelUpload |  | `uploadSessionId`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/assets/upload/chunks/sessions/{uploadSessionId}/finish` | FinishUpload2 |  | `uploadSessionId`:string *(path)* |  | CreateAssetResponseDto (200) |
| POST | `/api/v2/assets/{assetIdentifier}/upload/chunks/start` | StartNewVersionUploadV1V2 |  | `assetIdentifier`:string *(path)* |  | StartUploadResponseDto (200) |
| POST | `/api/v3/assets/{assetIdentifier}/upload/chunks/start` | StartNewVersionUploadV3 |  | `assetIdentifier`:string *(path)* | CreateAssetVersionDto | StartUploadResponseDto (200) |
| POST | `/api/v2/assets/{assetIdentifier}/upload/chunks/sessions/{uploadSessionId}/finish` | FinishVersionUploadV1V2 |  | `assetIdentifier`:string *(path)*; `uploadSessionId`:string *(path)* | CreateAssetVersionDto | CreateAssetVersionResponseDto (200) |
| POST | `/api/v3/assets/{assetIdentifier}/upload/chunks/sessions/{uploadSessionId}/finish` | FinishVersionUploadV3 |  | `assetIdentifier`:string *(path)*; `uploadSessionId`:string *(path)* |  | CreateAssetVersionResponseDto (200) |
| GET | `/api/v6/assets/{assetIdentifier}/versions` | GetVersions | Returns a list of versions available for the provided assetIdentifier. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of 10, use offset = 20, limit = 10. | `assetIdentifier`:string *(path)*; `limit`:int?; `offset`:int? |  | IList<AssetVersionDto> (200) |
| GET | `/api/v4/assets/{assetIdentifier}/relations` | GetRelationsOfAsset |  | `assetIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int?; `assetVersionIdentifier`:string; `includePropertyDefinitionIdentifiers`:IList<string>; `includeRelationTypeIdentifiers`:IList<string>; `includeEntityTypes`:IList<string>; `includePaths`:bool? |  | IList<RelationDto> (200) |
| POST | `/api/v6/assets/{assetIdentifier}/relations` | CreateRelation | Creates a new relationship from an asset to the provided entities. | `assetIdentifier`:string *(path)*; `assetVersionIdentifier`:string | CreateRelationDto | IdentifierRowVersionDto (200) |
| DELETE | `/api/v6/assets/{assetIdentifier}/relations/{relationIdentifier}` | DeleteRelation | Deletes a relation from an asset set to any other entity, or to an asset set from any other entity, by relation identifier. | `assetIdentifier`:string *(path)*; `relationIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/assets/{assetIdentifier}/relations/search` | SearchRelationsOfAsset | Search for relations of an asset. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of 10, use offset = 20, limit = 10. | `assetIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int?; `assetVersionIdentifier`:string | RelationsSearchQueryDto | IList<RelationDto> (200) |
| GET | `/api/v6/assets/duplicates` | GetAssetSetDuplicates | Returns a list of duplicated assets. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of 10, use offset = 20, limit = 10. | `limit`:int?; `offset`:int? |  | IList<AssetDuplicateDto> (200) |
| POST | `/api/v6/assets/contactprint/export` | ExportContactPrint | Exports the contact print pdf as stream. | `assetSetIdentifiers`:IList<string>; `format`:ContactPrintFormatDto?; `dataLanguages`:IList<string>; `langFallback`:bool? |  | FileResponse () |
| POST | `/api/v6/assets/contactprint/export-formdata` | ExportContactPrintForm | Exports the contact print pdf as stream. This endpoint can be used to facilitate the download of the export file by a browser through an HTML form with an encoding type of 'multipart/form-data'. | `contactPrintDto`:IO.Stream |  | FileResponse () |
| POST | `/api/v4/assets/relations/search` | SearchRelations |  | `x_Data_Languages`:string; `langFallback`:bool?; `includePropertyDefinitionIdentifiers`:IList<string>; `includeRelationTypeIdentifiers`:IList<string>; `includeEntityTypes`:IList<string>; `includePaths`:bool? | AssetSetRelationsQueryDto | IList<AssetSetRelationsDto> (200) |
| POST | `/api/v6/assets/relations/search` | SearchAssetSetRelations | Returns a list of relations for the requested asset sets. | `x_Data_Languages`:string; `langFallback`:bool? | AssetSetRelationsQueryDto | IList<AssetSetRelationsDto> (200) |
| POST | `/api/v6/assets/tasks/get` | GetTasksOfAssets | Retrieve the tasks linked to a list of assets. |  | IList<string> | AssetTasksDto (200) |
| POST | `/api/v6/assets/validate` | ValidateAssetSetProperties | Validates all properties from an asset set based on the specified property set conditions. |  | IList<string> | IList<ValidateAssetSetPropertiesResultDto> (200) |
| POST | `/api/v6/assets/derivations/get` | GetBulkAssetDerivations | Gets asset derivation information for a list of asset identifiers, optionally filtered by a list of derivation type identifiers. |  | AssetDerivationsRequestDto | AssetDerivationsResponseDto (200) |

#### AssetTypes (1 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/asset-types` | GetAssetTypes | Returns a list of all existing asset type identifiers |  |  | IList<string> (200) |

#### AssetUpload (3 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/assets/upload/start` | RequestAssetUpload | Requests an asset upload. A URL to request the first chunk upload is returned along with the storage provider's maximum chunk size |  | UploadStartRequestDto | UploadStartResponseDto (200) |
| GET | `/api/v6/assets/upload/chunk` | RequestAssetChunkUpload | Requests a chunk upload for an asset. Returns the chunk identifier, the storage provider URL to upload the chunk to, a URL to request the next chunk upload and a URL to finish the upload | `ut`:AssetUploadTypeDto; `di`:string; `fn`:string; `pe`:string; `ai`:string; `dt`:string; `av`:string; `wi`:string; `le`:string; `se`:string; `sig`:string; `aem`:ArchiveExtractionModeDto?; `aed`:bool? |  | ChunkUploadResponseDto (200) |
| POST | `/api/v6/assets/upload/finish` | RequestAssetUploadFinish | Requests to finish an asset upload. Assembles the final asset from the provided chunks. Any remaining chunks will be discarded | `ut`:AssetUploadTypeDto; `di`:string; `fn`:string; `pe`:string; `ai`:string; `dt`:string; `av`:string; `wi`:string; `le`:string; `se`:string; `sig`:string; `aem`:ArchiveExtractionModeDto?; `aed`:bool? | UploadFinishRequestDto | UploadFinishResponseDto (200) |

#### Categories (30 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/categories/{categoryIdentifier}/stream-formdata` | DownloadCompressedCategoryForm | Generates a Zip file of specified derivations for the specified category with all containing assets. This endpoint can be used to facilitate the download of a Zip file by a Browser through an HTML form with an encoding type of 'multipart/form-data'. The "categoryDownloadRequest" object is expected as JSON encoded form data part. | `categoryIdentifier`:string *(path)*; `zipCategoryDownloadRequest`:IO.Stream |  | FileResponse () |
| POST | `/api/v6/categories/{categoryIdentifier}/stream` | DownloadCompressedCategory | Generates a Zip file of specified derivations for the specified category with all containing assets | `categoryIdentifier`:string *(path)*; `accept`:string | ZipCategoryDownloadRequestDto | FileResponse () |
| GET | `/api/v6/categories` | GetCategoryTree | Returns the category tree. | `categoryIdentifier`:string; `x_Data_Languages`:string; `langFallback`:bool? |  | CategoryDto (200) |
| GET | `/api/v6/categories/{categoryIdentifier}` | GetCategory | Returns a single category and its direct children | `categoryIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `includeRestrictions`:bool? |  | CategoryDto (200) |
| DELETE | `/api/v6/categories/{categoryIdentifier}` | DeleteCategory | Deletes the specified category | `categoryIdentifier`:string *(path)*; `recycleBin`:bool? |  | (leer) (204) |
| PATCH | `/api/v6/categories/{categoryIdentifier}` | UpdateCategory | Updates an existing category | `categoryIdentifier`:string *(path)* | UpdateCategoryDto | RowVersionDto (200) |
| POST | `/api/v6/categories/{parentCategoryIdentifier}` | CreateNewCategory | Creates a sub-category | `parentCategoryIdentifier`:string *(path)* | CreateCategoryDto | PropertyEntityCreationResultDto (200) |
| PUT | `/api/v6/categories/{categoryIdentifier}/move` | MoveCategory | Moves a category to another parent category. | `categoryIdentifier`:string *(path)* | MoveCategoryDto | (leer) (204) |
| PUT | `/api/v6/categories/{categoryIdentifier}/assets/move` | MoveAssetsToCategory | Moves existing assets from a category into another already existing category | `categoryIdentifier`:string *(path)* | MoveAssetsToCategoryDto | (leer) (204) |
| PUT | `/api/v6/categories/{sourceCategoryIdentifier}/copy` | CopyCategory | Copies an existing category to a new parent category, the contained assets are going to be linked to the copied categories | `sourceCategoryIdentifier`:string *(path)* | CopyCategoryDto | IdentifierRowVersionDto (200) |
| PUT | `/api/v6/categories/{categoryIdentifier}/link/assets/{assetIdentifier}` | LinkAssetToCategory | Links an existing asset into another already existing category. Existing links to other categories will be left as is. | `categoryIdentifier`:string *(path)*; `assetIdentifier`:string *(path)* |  | (leer) (204) |
| PUT | `/api/v6/categories/{categoryIdentifier}/unlink/assets/{assetIdentifier}` | UnlinkAssetFromCategory | Unlinks an existing asset from a category. | `categoryIdentifier`:string *(path)*; `assetIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/categories/{categoryIdentifier}/resource-permissions` | GetResourcePermissions | Returns the resource permissions on a single category | `categoryIdentifier`:string *(path)* |  | IList<ResourcePermissionDto> (200) |
| PUT | `/api/v6/categories/{categoryIdentifier}/resource-permissions` | UpdateCategoryResourcePermissions | Assigns resource permissions to this category | `categoryIdentifier`:string *(path)*; `force`:bool? | IList<UpdateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| POST | `/api/v6/categories/{categoryIdentifier}/resource-permissions` | CreateResourcePermissions |  | `categoryIdentifier`:string *(path)* | IList<CreateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| PUT | `/api/v6/categories/{categoryIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | UpdateResourcePermission |  | `categoryIdentifier`:string *(path)*; `resourcePermissionIdentifier`:string *(path)* | UpdateResourcePermissionDto | RowVersionDto (200) |
| DELETE | `/api/v6/categories/{categoryIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | DeleteResourcePermission |  | `categoryIdentifier`:string *(path)*; `resourcePermissionIdentifier`:string *(path)*; `force`:bool? |  | (leer) (204) |
| PUT | `/api/v6/categories/{categoryIdentifier}/resource-permission-settings` | UpdateResourcePermissionSettings | Update the resource permission settings for a category | `categoryIdentifier`:string *(path)* | ResourcePermissionSettingsDto | (leer) (204) |
| GET | `/api/v6/categories/{categoryIdentifier}/resource-permission-settings` | GetResourcePermissionSettings | Returns the resource permission settings for the selected category | `categoryIdentifier`:string *(path)* |  | ResourcePermissionSettingsDto (200) |
| GET | `/api/v6/categories/{categoryIdentifier}/properties` | GetProperties | Returns the metadata properties of a category. | `categoryIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | IList<PropertyDto> (200) |
| POST | `/api/v6/categories/{categoryIdentifier}/properties` | CreateProperty | Create and add a metadata property to a category. | `categoryIdentifier`:string *(path)* | CreatePropertyDto | IdentifierRowVersionDto (200) |
| PUT | `/api/v6/categories/{categoryIdentifier}/properties` | PutCategoryProperties | Create or update multiple properties on a category. Values of existing properties will be replaced by the values from the request. In case of translated properties, omitting a language will drop the value of that language. In case of user context properties, omitting a user context will drop the value of that user context. Properties that exist on the category but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `categoryIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| PATCH | `/api/v6/categories/{categoryIdentifier}/properties` | PatchCategoryProperties | Create or patch multiple properties on a category. Values of existing properties will be patched by the values from the request. In case of translated properties, omitting a language will ignore the value of that language. In case of user context properties, omitting a user context will ignore the value of that user context. Properties that exist on the category but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `categoryIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| GET | `/api/v6/categories/{categoryIdentifier}/properties/{propertyIdentifier}` | GetProperty | Returns a specific metadata property of a category. | `categoryIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | PropertyDto (200) |
| PUT | `/api/v6/categories/{categoryIdentifier}/properties/{propertyIdentifier}` | UpdateProperty | Update an existing metadata Property of the specified category. Will drop property values not included in this operation. | `categoryIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyDto | RowVersionDto (204) |
| PATCH | `/api/v6/categories/{categoryIdentifier}/properties/{propertyIdentifier}` | EditProperty | Edit a metadata property of a category. Does not delete existing property values that were not sent as part of the request. | `categoryIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyDto | RowVersionDto (200) |
| DELETE | `/api/v6/categories/{categoryIdentifier}/properties/{propertyIdentifier}` | DeleteProperty | Delete a metadata property of a category. | `categoryIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/categories/{categoryIdentifier}/bulk-delete-properties` | DeleteCategoryProperties | Deletes existing properties of a category.  Returns a result for each property indicating whether the property was successfully deleted. | `categoryIdentifier`:string *(path)* | DeletePropertiesDto | IList<DeletePropertyResultDto> (200) |
| PUT | `/api/v6/categories/{categoryIdentifier}/properties/{propertyIdentifier}/inheritance-node-types` | UpdatePropertyInheritance | Update the inheritance mapping for the Property propertyIdentifier Will remove all existing mappings and replace them with updatePropertyInheritanceDto | `categoryIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyInheritanceDto | (leer) (204) |
| POST | `/api/v6/categories/quicksearch` | QuickSearchCategories | Returns a list of quick search categories for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int? | CategorySearchQueryDto | QuickSearchResultOfCategoryDto (200) |

#### Comments (3 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/comments/{commentIdentifier}` | GetComment | Returns a single comment for the provided identifier | `commentIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | CommentDto (200) |
| PUT | `/api/v6/comments/{commentIdentifier}` | UpdateComment | Updates an existing comment | `commentIdentifier`:string *(path)* | CreateOrUpdateCommentDto | RowVersionDto (200) |
| DELETE | `/api/v6/comments/{commentIdentifier}` | DeleteComment | Deletes the specified comment | `commentIdentifier`:string *(path)* |  | (leer) (204) |

#### CustomEntities (26 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}` | GetCustomEntity | Returns a single custom entity. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | CustomEntityDto (200) |
| PUT | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}` | UpdateCustomEntity | Updates a custom entity. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)* | UpdateCustomEntityDto | RowVersionDto (200) |
| DELETE | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}` | DeleteCustomEntity | Deletes a custom entity. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/custom-entities/{entityTypeIdentifier}` | GetCustomEntities | Returns a list of custom entities. | `entityTypeIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | IList<CustomEntityDto> (200) |
| POST | `/api/v6/custom-entities/search` | SearchCustomEntities | Returns a list of custom entities for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `includeThumbnailUris`:int?; `limit`:int?; `cursor`:string | CustomEntitySearchQueryDto | SearchResultOfCustomEntityV6Dto (200) |
| POST | `/api/v5/custom-entities/search` | SearchCustomEntities2 |  | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int?; `includeThumbnailUris`:int? | CustomEntitySearchQueryDto | SearchResultOfCustomEntityDto (200) |
| POST | `/api/v6/custom-entities/quicksearch` | QuickSearchCustomEntities | Returns a list of auto-complete suggestions or quick search custom entities ("did you mean" / or direct hits) for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `includeQuickSearch`:bool?; `includeSuggestions`:bool?; `limit`:int?; `offset`:int? | CustomEntitySearchQueryDto | QuickSearchResultOfCustomEntityDto (200) |
| POST | `/api/v6/custom-entities` | CreateCustomEntity | Creates a custom entity. |  | CreateCustomEntityDto | PropertyEntityCreationResultDto (200) |
| GET | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/resource-permissions` | GetResourcePermissions | Returns the resource permissions on a custom entity. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)* |  | IList<ResourcePermissionDto> (200) |
| PUT | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/resource-permissions` | UpdateCustomEntityResourcePermissions | Assigns resource permissions to this custom entity | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `force`:bool? | IList<UpdateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| POST | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/resource-permissions` | CreateResourcePermissions |  | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)* | IList<CreateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| PUT | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | UpdateResourcePermission |  | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `resourcePermissionIdentifier`:string *(path)* | UpdateResourcePermissionDto | RowVersionDto (200) |
| DELETE | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | DeleteResourcePermission |  | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `resourcePermissionIdentifier`:string *(path)*; `force`:bool? |  | (leer) (204) |
| GET | `/api/v4/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/relations` | GetRelations |  | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int?; `includePropertyDefinitionIdentifiers`:IList<string>; `includeRelationTypeIdentifiers`:IList<string>; `includeEntityTypes`:IList<string>; `includePaths`:bool? |  | IList<RelationDto> (200) |
| POST | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/relations` | CreateRelation | Creates a new relationship from a custom entity to the provided entities. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)* | CreateRelationDto | IdentifierRowVersionDto (200) |
| DELETE | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/relations/{relationIdentifier}` | DeleteRelation | Deletes a relation from the custom entity to any other entity by relation identifier. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `relationIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/relations/search` | SearchRelationsOfCustomEntity | Search for relations of a custom entity. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of 10, use offset = 20, limit = 10. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int? | RelationsSearchQueryDto | IList<RelationDto> (200) |
| GET | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/properties` | GetProperties | Returns the metadata properties of a custom entity. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | IList<PropertyDto> (200) |
| POST | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/properties` | CreateProperty | Create and add a list of metadata properties to a custom entity. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)* | IList<CreatePropertyDto> | IList<IdentifierRowVersionDto> (200) |
| PUT | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/properties` | PutCustomEntityProperties | Create or update multiple properties on a custom entity. Values of existing properties will be replaced by the values from the request. In case of translated properties, omitting a language will drop the value of that language. In case of user context properties, omitting a user context will drop the value of that user context. Properties that exist on the custom entity but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| PATCH | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/properties` | PatchCustomEntityProperties | Create or patch multiple properties on a custom entity. Values of existing properties will be patched by the values from the request. In case of translated properties, omitting a language will ignore the value of that language. In case of user context properties, omitting a user context will ignore the value of that user context. Properties that exist on the custom entity but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| GET | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/properties/{propertyIdentifier}` | GetProperty | Returns a specific metadata property of a custom entity. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | PropertyDto (200) |
| PUT | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/properties/{propertyIdentifier}` | UpdateProperty | Update an existing metadata property of the specified custom entity. Will drop property values not included in this operation. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyDto | RowVersionDto (204) |
| PATCH | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/properties/{propertyIdentifier}` | EditProperty | Edit a metadata property of a custom entity. Does not delete existing property values that were not sent as part of the request. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyDto | RowVersionDto (200) |
| DELETE | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/properties/{propertyIdentifier}` | DeleteProperty | Delete a metadata property of a custom entity. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/custom-entities/{entityTypeIdentifier}/{customEntityIdentifier}/bulk-delete-properties` | DeleteCustomEntityProperties | Deletes existing properties of a custom entity.  Returns a result for each property indicating whether the property was successfully deleted. | `entityTypeIdentifier`:string *(path)*; `customEntityIdentifier`:string *(path)* | DeletePropertiesDto | IList<DeletePropertyResultDto> (200) |

#### DataLanguages (5 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/data-languages/{dataLanguageIdentifier}` | GetDataLanguage | Returns a single data language for the provided identifier | `dataLanguageIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | DataLanguageDto (200) |
| PUT | `/api/v6/data-languages/{dataLanguageIdentifier}` | UpdateDataLanguage | Updates an existing data language | `dataLanguageIdentifier`:string *(path)* | UpdateDataLanguageDto | RowVersionDto (200) |
| DELETE | `/api/v6/data-languages/{dataLanguageIdentifier}` | DeleteDataLanguage | Deletes the specified data language | `dataLanguageIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/data-languages` | GetDataLanguages | Returns a list of data languages. | `statusFilter`:EntityStatusDto? |  | IList<DataLanguageDto> (200) |
| POST | `/api/v6/data-languages` | CreateDataLanguage | Create a new data language |  | DataLanguageDto | IdentifierRowVersionDto (200) |

#### DerivationJobDefinitions (5 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/derivation-job-definitions/{derivationJobDefinitionIdentifier}` | GetDerivationJobDefinition | Retrieve a specific derivation job definition. | `derivationJobDefinitionIdentifier`:string *(path)* |  | DerivationJobDefinitionDto (200) |
| PUT | `/api/v6/derivation-job-definitions/{derivationJobDefinitionIdentifier}` | UpdateDerivationJobDefinition | Updates an existing derivation job definition. | `derivationJobDefinitionIdentifier`:string *(path)* | UpdateDerivationJobDefinitionDto | RowVersionDto (200) |
| DELETE | `/api/v6/derivation-job-definitions/{derivationJobDefinitionIdentifier}` | DeleteDerivationJobDefinition | Deletes the specified derivation job definition. | `derivationJobDefinitionIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/derivation-job-definitions` | GetDerivationJobDefinitions | Retrieve a list of derivation job definitions. |  |  | IList<DerivationJobDefinitionDto> (200) |
| POST | `/api/v6/derivation-job-definitions` | CreateDerivationJobDefinition | Create a new derivation job definition. |  | CreateDerivationJobDefinitionDto | IdentifierRowVersionDto (200) |

#### Derivations (6 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/derivations` | StartDerivationFromJobDefinition | Creates and enqueues derivations of a derivation job definition for execution. | `derivationJobDefinitionIdentifier`:string | IList<CreateDerivationDto> | IdentifierRowVersionDto (200) |
| POST | `/api/v6/derivations/restart` | StartAssetReDerivationFromAssetIdentifiers | Restart the process of generating derivative files of multiple assets. Returns a background job identifier which can be used to retrieve a report. If there is nothing to re derivate 204 is returned. |  | DerivationRestartDto | IdentifierRowVersionDto (200) |
| POST | `/api/v6/derivations/restart/search` | StartAssetReDerivationFromSearchQuery | Restart the process of generating derivative files of multiple assets from a search-query. Returns a background job identifier which can be used to retrieve a report. If there is nothing to re derivate 204 is returned. |  | DerivationRestartSearchQueryDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/derivations/status/overview` | GetStatusOverview | Returns the count of current pending or failed derivations. |  |  | DerivationStatusOverviewDto (200) |
| GET | `/api/v6/derivations/status/types` | GetStatusTypes | Returns an aggregated, paged list of current derivations, containing asset type, derivation type, status and count. | `assetDerivationStatusDto`:AssetDerivationStatusDto; `limit`:int?; `offset`:int? |  | IList<DerivationStatusTypesDto> (200) |
| GET | `/api/v6/derivations/status/details` | GetStatusDetails | Returns a paged list of current derivations, containing asset information, derivation type, status and retry count. | `assetDerivationStatusDto`:AssetDerivationStatusDto; `assetTypeIdentifier`:string; `derivationTypeIdentifier`:string; `limit`:int?; `offset`:int? |  | IList<DerivationStatusDetailsDto> (200) |

#### DerivationTypes (6 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/derivation-types/{derivationTypeIdentifier}` | GetDerivationType | Retrieve a specific derivation type. | `derivationTypeIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | DerivationTypeDto (200) |
| PUT | `/api/v6/derivation-types/{derivationTypeIdentifier}` | UpdateDerivationType | Updates an existing derivation type. | `derivationTypeIdentifier`:string *(path)* | UpdateDerivationTypeDto | RowVersionDto (200) |
| DELETE | `/api/v6/derivation-types/{derivationTypeIdentifier}` | DeleteDerivationType | Deletes an existing derivation type. | `derivationTypeIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/derivation-types` | GetDerivationTypes | Retrieve a list of derivation types. | `x_Data_Languages`:string; `langFallback`:bool?; `assetTypes`:IList<string>; `categoryIdentifier`:string |  | IList<DerivationTypeDto> (200) |
| POST | `/api/v6/derivation-types` | CreateDerivationType | Create a new derivation type. |  | CreateDerivationTypeDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/derivation-types/grouped` | GetAvailableDerivationsForAssetTypes | Returns a list of available derivations grouped by asset types | `x_Data_Languages`:string; `langFallback`:bool?; `assetTypes`:IList<string>; `categoryIdentifier`:string |  | IList<AssetTypeDto> (200) |

#### EntityTypes (6 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/entity-types/{entityTypeIdentifier}` | GetEntityType | Returns a single entity type. | `entityTypeIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | EntityTypeDto (200) |
| PUT | `/api/v6/entity-types/{entityTypeIdentifier}` | UpdateEntityType | Updates an entity type. | `entityTypeIdentifier`:string *(path)* | UpdateEntityTypeDto | RowVersionDto (200) |
| DELETE | `/api/v6/entity-types/{entityTypeIdentifier}` | DeleteEntityType | Deletes a entity type. System entity types can not be deleted. | `entityTypeIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/entity-types` | GetEntityTypes | Returns a list of entity types. | `x_Data_Languages`:string; `statusFilter`:EntityStatusDto?; `langFallback`:bool? |  | IList<EntityTypeDto> (200) |
| POST | `/api/v6/entity-types` | CreateEntityType | Creates a entity type. |  | CreateEntityTypeDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/entity-types/asset-search-types` | GetEntityTypesForAssetSearch | Returns node types in hierarchy that are used for asset search | `x_Data_Languages`:string; `langFallback`:bool? |  | IList<EntityTypeDto> (200) |

#### ExternalGroups (6 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/external-groups/{externalGroupIdentifier}` | GetExternalGroup | Retrieve a external group | `externalGroupIdentifier`:string *(path)* |  | ExternalGroupDto (200) |
| PUT | `/api/v6/external-groups/{externalGroupIdentifier}` | UpdateExternalGroup | Update an external group | `externalGroupIdentifier`:string *(path)* | CreateOrUpdateExternalGroupDto | RowVersionDto (200) |
| DELETE | `/api/v6/external-groups/{externalGroupIdentifier}` | DeleteExternalGroup | Deletes an external group | `externalGroupIdentifier`:string *(path)* |  | FileResponse () |
| GET | `/api/v1/external-groups` | GetExternalGroupsV1 | Retrieve all external groups |  |  | IList<ExternalGroupDto> (200) |
| GET | `/api/v5/external-groups` | GetExternalGroupsV2 | Retrieve all external groups. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use offset = 20, limit = 10. | `limit`:int?; `offset`:int?; `sortBy`:ExternalGroupSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string |  | IList<ExternalGroupDto> (200) |
| POST | `/api/v6/external-groups` | CreateExternalGroup | Create an external group |  | CreateOrUpdateExternalGroupDto | IdentifierRowVersionDto (200) |

#### Extractions (2 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/extractions` | ExtractArchiveAsset | Starts a job to extract a zip archive asset | `assetIdentifier`:string; `categoryIdentifier`:string; `archiveExtractionMode`:ArchiveExtractionModeDto? |  | IdentifierRowVersionDto (200) |
| GET | `/api/v6/extractions/{jobIdentifier}` | GetArchiveExtractionStatus | Gets the status of a job. | `jobIdentifier`:string *(path)* |  | JobStatusDto (200) |

#### FileNamingDefinitions (7 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/file-naming-definitions` | GetFileNamingDefinitions | Returns a list of file naming definitions. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use offset = 20, limit = 10. | `limit`:int?; `offset`:int?; `x_Data_Languages`:string; `langFallback`:bool? |  | IList<FileNamingDefinitionDto> (200) |
| POST | `/api/v6/file-naming-definitions` | CreateFileNamingDefinition | Create a new file naming definition. |  | CreateFileNamingDefinitionDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/file-naming-definitions/tokens` | GetFileNamingTokens | Returns a list of available file naming tokens. | `x_Data_Languages`:string; `langFallback`:bool? |  | IList<FileNamingTokenDto> (200) |
| GET | `/api/v6/file-naming-definitions/{fileNamingDefinitionIdentifier}` | GetFileNamingDefinition | Retrieve a single file naming definition. | `fileNamingDefinitionIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | FileNamingDefinitionDto (200) |
| PUT | `/api/v6/file-naming-definitions/{fileNamingDefinitionIdentifier}` | UpdateFileNamingDefinition | Updates a file naming definition. | `fileNamingDefinitionIdentifier`:string *(path)* | UpdateFileNamingDefinitionDto | RowVersionDto (200) |
| DELETE | `/api/v6/file-naming-definitions/{fileNamingDefinitionIdentifier}` | DeleteFileNamingDefinition | Deletes the specified file naming definition. | `fileNamingDefinitionIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/file-naming-definitions/{fileNamingDefinitionIdentifier}/preview` | PreviewFileNamingDefinition | Apply a file naming definition to a set of assets and retrieve a preview of the resulting file names. | `fileNamingDefinitionIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? | IList<AssetSetDerivationTypeDto> | IList<AssetSetDerivationFileNameDto> (200) |

#### FileReferences (3 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/files/upload` | UploadFileReference | Uploads a new file or a changed file. No identifier is specified for a new file. |  | form `fileUpload`:FileParameter; form `createFileDto`:CreateFileDto | CreateFileResponseDto (200) |
| GET | `/api/v6/files` | GetFileReferences | Retrieve a list of files. | `fileUsageType`:FileUsageTypeDto |  | IList<FileDto> (200) |
| DELETE | `/api/v6/files/{fileIdentifier}` | DeleteFileReference | Deletes an existing file. | `fileIdentifier`:string *(path)* |  | (leer) (204) |

#### FileTypes (2 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/file-types` | GetFileTypes | Returns a list of file types. |  |  | IList<FileTypeDto> (200) |
| GET | `/api/v6/file-types/forbidden` | GetForbiddenFileTypes | Returns a list of forbidden upload file types |  |  | IList<string> (200) |

#### Info (1 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/info` | Get | Returns a dictionary of information about this API |  |  | IDictionary<string, string> (200) |

#### Jobs (14 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v1/jobs/mass-edits` | GetPropertyMassEdits |  | `limit`:int?; `offset`:int? |  | IList<PropertyMassEditDto> (200) |
| GET | `/api/v6/jobs/mass-edits` | GetPropertyMassEditsV4 | Get a list of mass edit jobs. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of 10, use offset = 20, limit = 10. | `limit`:int?; `offset`:int? |  | IList<PropertyMassEditV4Dto> (200) |
| GET | `/api/v1/jobs/mass-edits/{massEditIdentifier}/report` | GetPropertyMassEdit |  | `massEditIdentifier`:string *(path)* |  | PropertyMassEditReportDto (200) |
| GET | `/api/v6/jobs/mass-edits/{massEditIdentifier}/report` | GetPropertyMassEditJobReportV4 | Get the report of a mass edit job. | `massEditIdentifier`:string *(path)* |  | PropertyMassEditReportV4Dto (200) |
| GET | `/api/v6/jobs/mass-edits/{massEditIdentifier}/mutations` | GetPropertyMassEditAssetMutations | Get the property mutations of a mass edit job. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of 10, use offset = 20, limit = 10. | `massEditIdentifier`:string *(path)*; `limit`:int?; `offset`:int? |  | IList<AssetPropertyMutationDto> (200) |
| GET | `/api/v6/jobs/derivations` | GetAssetDerivationJobs | Get a list of asset derivation jobs. | `limit`:int?; `offset`:int? |  | IList<AssetDerivationJobDto> (200) |
| GET | `/api/v6/jobs/derivations/{backgroundJobIdentifier}` | GetAssetDerivationJob | Get an asset derivation job. | `backgroundJobIdentifier`:string *(path)* |  | AssetDerivationJobReportDto (200) |
| GET | `/api/v6/jobs/imports` | GetPropertyImports | Get a list of property imports. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use offset = 20, limit = 10. | `limit`:int?; `offset`:int? |  | IList<PropertyImportDto> (200) |
| GET | `/api/v6/jobs/imports/{propertyImportIdentifier}` | GetPropertyImport | Get a single property import. | `propertyImportIdentifier`:string *(path)* |  | PropertyImportDto (200) |
| GET | `/api/v6/jobs/extractions` | GetArchiveExtractionJobs | Get a list of archive extraction jobs. | `limit`:int?; `offset`:int?; `filterByWorkspaceIdentifier`:IList<string> |  | IList<ArchiveExtractionJobDto> (200) |
| GET | `/api/v6/jobs/extractions/{archiveExtractionJobIdentifier}/report` | GetArchiveExtractionJobReport | Get an archive extraction job report. | `archiveExtractionJobIdentifier`:string *(path)* |  | ArchiveExtractionJobReportDto (200) |
| GET | `/api/v6/jobs/property-extractions` | GetPropertyExtractionJobs | Get a list of property extraction jobs. | `limit`:int?; `offset`:int? |  | IList<PropertyExtractionJobDto> (200) |
| GET | `/api/v6/jobs/property-extractions/{propertyExtractionJobIdentifier}/report` | GetPropertyExtractionJob | Get a property extraction job. | `propertyExtractionJobIdentifier`:string *(path)* |  | PropertyExtractionJobDto (200) |
| GET | `/api/v6/jobs/property-extractions/{propertyExtractionJobIdentifier}/extractions` | GetPropertyExtractionJobResults | Get the extraction results of a property extraction job. | `propertyExtractionJobIdentifier`:string *(path)*; `limit`:int?; `offset`:int? |  | IList<PropertyExtractionJobAssetResultDto> (200) |

#### DrmLicenses (58 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/drm/billing-contacts` | CreateBillingContact | Creates a new billing contact |  | CreateBillingContactDto | PropertyEntityCreationResultDto (200) |
| GET | `/api/v6/drm/billing-contacts/{billingContactIdentifier}` | GetBillingContact | Get a specific billing contact | `billingContactIdentifier`:string *(path)* |  | BillingContactDto (200) |
| PUT | `/api/v6/drm/billing-contacts/{billingContactIdentifier}` | UpdateBillingContact | Update a specific billing contact | `billingContactIdentifier`:string *(path)* | UpdateBillingContactDto | RowVersionDto (200) |
| DELETE | `/api/v6/drm/billing-contacts/{billingContactIdentifier}` | DeleteBillingContact | Delete a specific billing contact | `billingContactIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v5/drm/billing-contacts/search` | SearchBillingContacts |  | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int? | BillingContactSearchQueryDto | SearchResultOfBillingContactDto (200) |
| POST | `/api/v6/drm/billing-contacts/search` | SearchBillingContacts2 | Returns a list of billing contacts for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `cursor`:string | BillingContactSearchQueryDto | SearchResultOfBillingContactV6Dto (200) |
| POST | `/api/v6/drm/billing-contacts/quicksearch` | QuickSearchBillingContacts | Returns a list of auto-complete suggestions or quick search billing contact ("did you mean" / or direct hits) for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `includeQuickSearch`:bool?; `includeSuggestions`:bool?; `limit`:int?; `offset`:int? | BillingContactSearchQueryDto | QuickSearchResultOfBillingContactDto (200) |
| GET | `/api/v6/drm/billing-contacts/{billingContactIdentifier}/properties` | GetBillingContactProperties | Return the metadata properties of a billing contact. | `billingContactIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | IList<PropertyDto> (200) |
| PUT | `/api/v6/drm/billing-contacts/{billingContactIdentifier}/properties` | PutBillingContactProperties | Create or update multiple properties on a billing contact. Values of existing properties will be replaced by the values from the request. In case of translated properties, omitting a language will drop the value of that language. In case of user context properties, omitting a user context will drop the value of that user context. Properties that exist on the billingContact but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `billingContactIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| PATCH | `/api/v6/drm/billing-contacts/{billingContactIdentifier}/properties` | PatchBillingContactProperties | Create or patch multiple properties on a billing contact. Values of existing properties will be patched by the values from the request. In case of translated properties, omitting a language will ignore the value of that language. In case of user context properties, omitting a user context will ignore the value of that user context. Properties that exist on the billing contact but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `billingContactIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| POST | `/api/v6/drm/billing-contacts/{billingContactIdentifier}/bulk-delete-properties` | DeleteBillingContactProperties | Deletes existing properties of a billing contact.  Returns a result for each property indicating whether the property was successfully deleted. | `billingContactIdentifier`:string *(path)* | DeletePropertiesDto | IList<DeletePropertyResultDto> (200) |
| POST | `/api/v6/drm/license-offers/search` | SearchLicenseOffers | Search for license offers | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int? | LicenseOfferSearchQueryDto | IList<LicenseOfferDto> (200) |
| POST | `/api/v6/drm/licenses` | CreateLicense | Creates a new license |  | CreateLicenseDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/drm/licenses/{licenseIdentifier}` | GetLicense | Get a specific license | `licenseIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | LicenseDto (200) |
| PUT | `/api/v6/drm/licenses/{licenseIdentifier}` | UpdateLicense | Update a specific license | `licenseIdentifier`:string *(path)* | UpdateLicenseDto | RowVersionDto (200) |
| DELETE | `/api/v6/drm/licenses/{licenseIdentifier}` | DeleteLicense | Delete a specific license | `licenseIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v5/drm/licenses/search` | SearchLicenses |  | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int? | LicenseSearchQueryDto | SearchResultOfLicenseDto (200) |
| POST | `/api/v6/drm/licenses/search` | SearchLicenses2 | Returns a list of licenses for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `cursor`:string | LicenseSearchQueryDto | SearchResultOfLicenseV6Dto (200) |
| POST | `/api/v6/drm/licenses/quicksearch` | QuickSearchLicenses | Returns a list of auto-complete suggestions or quick search license ("did you mean" / or direct hits) for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `includeQuickSearch`:bool?; `includeSuggestions`:bool?; `limit`:int?; `offset`:int? | LicenseSearchQueryDto | QuickSearchResultOfLicenseDto (200) |
| GET | `/api/v6/drm/licenses/resource-permissions` | GetResourcePermissions | Returns the resource permissions of all licenses, rights owners and billing contacts |  |  | IList<ResourcePermissionDto> (200) |
| PUT | `/api/v6/drm/licenses/resource-permissions` | UpdateLicenseResourcePermissions | Assigns resource permissions to all licenses, rights owners and billing contacts | `force`:bool? | IList<UpdateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| GET | `/api/v6/drm/licenses/{licenseIdentifier}/entities` | GetLicenseEntities | Get entities that are assigned to a license | `licenseIdentifier`:string *(path)*; `entityTypeFilter`:IList<string>; `includePropertyDefinitionIdentifiers`:IList<string>; `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int? |  | IList<EntityExtendedDto> (200) |
| POST | `/api/v6/drm/licenses/{licenseIdentifier}/entities` | AddEntitiesToLicense | Adds entities to an existing license | `licenseIdentifier`:string *(path)* | EntityIdentifiersDto | IList<CreateRelationResultDto> (200) |
| POST | `/api/v6/drm/licenses/{licenseIdentifier}/entities/remove` | RemoveEntitiesFromLicense | Removes entities from an existing license | `licenseIdentifier`:string *(path)* | EntityIdentifiersDto | (leer) (204) |
| GET | `/api/v6/drm/licenses/{licenseIdentifier}/properties` | GetLicenseProperties | Return the metadata properties of a license. | `licenseIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | IList<PropertyDto> (200) |
| PUT | `/api/v6/drm/licenses/{licenseIdentifier}/properties` | PutLicenseProperties | Create or update multiple properties on a license. Values of existing properties will be replaced by the values from the request. In case of translated properties, omitting a language will drop the value of that language. In case of user context properties, omitting a user context will drop the value of that user context. Properties that exist on the license but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `licenseIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| PATCH | `/api/v6/drm/licenses/{licenseIdentifier}/properties` | PatchLicenseProperties | Create or patch multiple properties on a license. Values of existing properties will be patched by the values from the request. In case of translated properties, omitting a language will ignore the value of that language. In case of user context properties, omitting a user context will ignore the value of that user context. Properties that exist on the license but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `licenseIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| POST | `/api/v6/drm/licenses/{licenseIdentifier}/bulk-delete-properties` | DeleteLicenseProperties | Deletes existing properties of a license.  Returns a result for each property indicating whether the property was successfully deleted. | `licenseIdentifier`:string *(path)* | DeletePropertiesDto | IList<DeletePropertyResultDto> (200) |
| GET | `/api/v6/drm/licenses/{licenseIdentifier}/buyouts` | GetBuyouts | Get a list of buyouts | `licenseIdentifier`:string *(path)* |  | IList<BuyoutDto> (200) |
| POST | `/api/v6/drm/licenses/{licenseIdentifier}/buyouts` | CreateBuyout | Creates a new buyout | `licenseIdentifier`:string *(path)* | CreateBuyoutDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/drm/licenses/{licenseIdentifier}/buyouts/{buyoutIdentifier}` | GetBuyout | Get a specific buyout | `licenseIdentifier`:string *(path)*; `buyoutIdentifier`:string *(path)* |  | BuyoutDto (200) |
| PUT | `/api/v6/drm/licenses/{licenseIdentifier}/buyouts/{buyoutIdentifier}` | UpdateBuyout | Update a specific buyout | `licenseIdentifier`:string *(path)*; `buyoutIdentifier`:string *(path)* | UpdateBuyoutDto | RowVersionDto (200) |
| DELETE | `/api/v6/drm/licenses/{licenseIdentifier}/buyouts/{buyoutIdentifier}` | DeleteBuyout | Delete a specific buyout | `licenseIdentifier`:string *(path)*; `buyoutIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/drm/price-lists` | GetPriceLists | Returns a list of price lists. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use offset = 20, limit = 10. | `statusFilter`:EntityStatusDto?; `limit`:int?; `offset`:int?; `sortBy`:PriceListSortByDto; `sortDir`:SortDirectionDto?; `searchFilter`:string |  | IList<PriceListDto> (200) |
| POST | `/api/v6/drm/price-lists` | CreatePriceList | Creates a new price list |  | CreatePriceListDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/drm/price-lists/{priceListIdentifier}` | GetPriceList | Get a specific price list | `priceListIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | PriceListDto (200) |
| PUT | `/api/v6/drm/price-lists/{priceListIdentifier}` | UpdatePriceList | Update a specific price list | `priceListIdentifier`:string *(path)* | UpdatePriceListDto | RowVersionDto (200) |
| DELETE | `/api/v6/drm/price-lists/{priceListIdentifier}` | DeletePriceList | Delete a specific price list | `priceListIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/drm/purchases` | CreatePurchase | Creates a new purchase |  | CreatePurchaseDto | IdentifierRowVersionDto (200) |
| POST | `/api/v6/drm/purchases/search` | SearchPurchases | Search for purchases | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `cursor`:string | LicensePurchaseSearchQueryDto | SearchResultOfPurchaseDto (200) |
| POST | `/api/v6/drm/rights-owners` | CreateRightsOwner | Creates a new rights owner If the creation of properties fails, the rights owner will still be created |  | CreateRightsOwnerDto | PropertyEntityCreationResultDto (200) |
| GET | `/api/v6/drm/rights-owners/{rightsOwnerIdentifier}` | GetRightsOwner | Get a specific rights owner | `rightsOwnerIdentifier`:string *(path)* |  | RightsOwnerDto (200) |
| PUT | `/api/v6/drm/rights-owners/{rightsOwnerIdentifier}` | UpdateRightsOwner | Update a specific rights owner | `rightsOwnerIdentifier`:string *(path)* | UpdateRightsOwnerDto | RowVersionDto (200) |
| DELETE | `/api/v6/drm/rights-owners/{rightsOwnerIdentifier}` | DeleteRightsOwner | Delete a specific rights owner | `rightsOwnerIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v5/drm/rights-owners/search` | SearchRightsOwners |  | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int? | RightsOwnerSearchQueryDto | SearchResultOfRightsOwnerDto (200) |
| POST | `/api/v6/drm/rights-owners/search` | SearchRightsOwners2 | Returns a list of rights owners for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `cursor`:string | RightsOwnerSearchQueryDto | SearchResultOfRightsOwnerV6Dto (200) |
| POST | `/api/v6/drm/rights-owners/quicksearch` | QuickSearchRightsOwners | Returns a list of auto-complete suggestions or quick search rights owner ("did you mean" / or direct hits) for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `includeQuickSearch`:bool?; `includeSuggestions`:bool?; `limit`:int?; `offset`:int? | RightsOwnerSearchQueryDto | QuickSearchResultOfRightsOwnerDto (200) |
| GET | `/api/v6/drm/rights-owners/{rightsOwnerIdentifier}/properties` | GetRightsOwnerProperties | Return the metadata properties of a rights owner. | `rightsOwnerIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | IList<PropertyDto> (200) |
| PUT | `/api/v6/drm/rights-owners/{rightsOwnerIdentifier}/properties` | PutRightsOwnerProperties | Create or update multiple properties on a rights owner. Values of existing properties will be replaced by the values from the request. In case of translated properties, omitting a language will drop the value of that language. In case of user context properties, omitting a user context will drop the value of that user context. Properties that exist on the rights owner but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `rightsOwnerIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| PATCH | `/api/v6/drm/rights-owners/{rightsOwnerIdentifier}/properties` | PatchRightsOwnerProperties | Create or patch multiple properties on a rights owner. Values of existing properties will be patched by the values from the request. In case of translated properties, omitting a language will ignore the value of that language. In case of user context properties, omitting a user context will ignore the value of that user context. Properties that exist on the rights owner but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `rightsOwnerIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| POST | `/api/v6/drm/rights-owners/{rightsOwnerIdentifier}/bulk-delete-properties` | DeleteRightsOwnerProperties | Deletes existing properties of a rights owner.  Returns a result for each property indicating whether the property was successfully deleted. | `rightsOwnerIdentifier`:string *(path)* | DeletePropertiesDto | IList<DeletePropertyResultDto> (200) |
| POST | `/api/v6/drm/rights-owners/{rightsOwnerIdentifier}/avatar/upload` | UploadRightsOwnerAvatar | Uploads the avatar file for this rights owner. | `rightsOwnerIdentifier`:string *(path)* | form `fileUpload`:FileParameter; form `createFileDto`:CreateFileDto | CreateFileResponseDto (200) |
| DELETE | `/api/v6/drm/rights-owners/{rightsOwnerIdentifier}/avatar` | DeleteRightsOwnerAvatar | Deletes the avatar of this rights owner. | `rightsOwnerIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/drm/user-context-groups` | CreateUserContextGroup | Creates a new user context group |  | CreateUserContextGroupDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/drm/user-context-groups` | GetUserContextGroups | Get all user context groups | `x_Data_Languages`:string; `langFallback`:bool? |  | IList<UserContextGroupDto> (200) |
| GET | `/api/v6/drm/user-context-groups/{contextGroupIdentifier}` | GetUserContextGroup | Get a specific user context group by identifier | `x_Data_Languages`:string; `contextGroupIdentifier`:string *(path)*; `langFallback`:bool? |  | UserContextGroupDto (200) |
| PUT | `/api/v6/drm/user-context-groups/{userContextGroupIdentifier}` | UpdateUserContextGroup | Update a user context group | `userContextGroupIdentifier`:string *(path)* | UpdateUserContextGroupDto | RowVersionDto (200) |
| DELETE | `/api/v6/drm/user-context-groups/{userContextGroupIdentifier}` | DeleteUserContextGroup | Delete a user context group | `userContextGroupIdentifier`:string *(path)* |  | (leer) (204) |

#### My (7 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/my/profile` | GetProfile | Returns the settings and all assigned roles from the current user. | `x_Data_Languages`:string; `langFallback`:bool |  | UserProfileDto (200) |
| PUT | `/api/v6/my/profile/settings` | CreateOrUpdateSettings | Create or update the settings of the current user. |  | CreateOrUpdateUserSettingsDto | (leer) (204) |
| PUT | `/api/v6/my/password` | ChangePassword | Changes the current user's password. | `oldPassword`:string; `password`:string |  | (leer) (204) |
| GET | `/api/v6/my/activity-logs` | GetActivityLogNotifications | Retrieve the activity log notifications of the user. | `x_Data_Languages`:string; `langFallback`:bool?; `allDataLanguages`:bool?; `limit`:int?; `offset`:int? |  | IList<ActivityLogNotificationDto> (200) |
| DELETE | `/api/v6/my/activity-logs` | ClearActivityLogNotifications | Clear all the activity log notifications of the user. |  |  | (leer) (204) |
| PUT | `/api/v6/my/activity-logs/read` | SetActivityLogNotificationsRead | Set activity log notifications of the user to 'read'. |  | ActivityLogIdentifiersDto | (leer) (204) |
| POST | `/api/v6/my/activity-logs/bulk-delete` | DeleteActivityLogNotifications | Delete activity log notifications of the user. |  | ActivityLogIdentifiersDto | (leer) (204) |

#### ObjectTypePermissions (1 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/object-type-permissions` | Get | Returns a list of all available object type permissions |  |  | IList<ObjectTypePermissionDto> (200) |

#### PermissionMaterializations (2 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/permission-materializations` | GetMaterializationJobs | Returns a list of materialization jobs according to the status filter | `statusFilter`:IList<JobStatusDto>; `limit`:int?; `offset`:int? |  | IList<PermissionMaterializationDto> (200) |
| POST | `/api/v6/permission-materializations` | ReMaterializeSubtree | Triggers a re-materialization of the subtree of the selected entity (including the selected entity) |  | EntityIdentifierDto | IdentifierRowVersionDto (200) |

#### Properties (14 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/properties/mass-edit-jobs` | MassEditProperties | Create or replace metadata properties on assets. Returns a mass edit identifier. |  | CreatePropertyMassEditDto | IdentifierRowVersionDto (200) |
| POST | `/api/v6/properties/mass-edit-jobs/search` | CreateMassEditJobFromSearchQuery | Create or replace metadata properties on multiple assets from a search-query. Returns a mass edit identifier. |  | CreatePropertyMassEditSearchQueryDto | IdentifierRowVersionDto (200) |
| POST | `/api/v6/properties/asset/export-formdata` | ExportAssetPropertiesForm | Exports properties of one or many assets as defined in the options. This endpoint can be used to facilitate the download of the export file by a Browser through an HTML form with an encoding type of 'multipart/form-data'. The "propertyExport" object is expected as JSON encoded form data part. | `propertyExport`:IO.Stream |  | (leer) (200) |
| POST | `/api/v6/properties/asset/export` | ExportAssetProperties | Exports properties of one or many assets as defined in the options |  | PropertyExportDto | (leer) (200) |
| POST | `/api/v6/properties/asset/import` | ImportAssetProperties | Imports properties of one or many assets |  | form `importFile`:FileParameter | IdentifierRowVersionDto (200) |
| POST | `/api/v5/properties/extract` | ExtractMetadata |  |  | MetadataExtractionDto | (leer) (204) |
| POST | `/api/v5/properties/extract/search` | ExtractMetadataFromSearchQuery |  |  | AssetSearchQueryDto | (leer) (204) |
| POST | `/api/v6/properties/extract` | ExtractMetadataV6 | Extracts metadata of multiple assets. |  | MetadataExtractionV6Dto | IdentifierRowVersionDto (200) |
| POST | `/api/v6/properties/extract/search` | ExtractMetadataFromSearchQueryV6 | Extracts metadata of multiple assets from a search-query. |  | ExtractFromAssetSearchQueryDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/properties/property-extraction-definitions` | GetPropertyExtractionDefinitions | Get a list of property extraction definitions. | `statusFilter`:EntityStatusDto?; `limit`:int?; `offset`:int? |  | IList<PropertyExtractionDefinitionDto> (200) |
| POST | `/api/v6/properties/property-extraction-definitions` | CreatePropertyExtractionDefinition | Create a property extraction definition. |  | CreatePropertyExtractionDefinitionDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/properties/property-extraction-definitions/{propertyExtractionDefinitionIdentifier}` | GetPropertyExtractionDefinition | Get a property extraction definition. | `propertyExtractionDefinitionIdentifier`:string *(path)* |  | PropertyExtractionDefinitionDto (200) |
| PUT | `/api/v6/properties/property-extraction-definitions/{propertyExtractionDefinitionIdentifier}` | UpdatePropertyExtractionDefinition | Update a property extraction definition. | `propertyExtractionDefinitionIdentifier`:string *(path)* | UpdatePropertyExtractionDefinitionDto | RowVersionDto (200) |
| DELETE | `/api/v6/properties/property-extraction-definitions/{propertyExtractionDefinitionIdentifier}` | DeletePropertyExtractionDefinition | Delete a property extraction definition. | `propertyExtractionDefinitionIdentifier`:string *(path)* |  | (leer) (204) |

#### PropertyDefinitions (8 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v1/property-definitions` | GetPropertyDefinitionsV1 | Returns a list of property definitions. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use offset = 20, limit = 10. | `statusFilter`:EntityStatusDto?; `limit`:int?; `offset`:int?; `x_Data_Languages`:string; `langFallback`:bool?; `sortBy`:PropertyDefinitionSortByDto?; `sortDir`:SortDirectionDto?; `isSystem`:bool?; `isEditable`:bool?; `isSearchableFacet`:bool?; `hasUserContext`:bool?; `isMultiselect`:bool?; `isCustomValueAllowed`:bool?; `valueType`:PropertyValueTypeDto?; `entityTypeIdentifiers`:IList<string>; `searchFilter`:string; `isUrl`:bool? |  | IList<PropertyDefinitionDto> (200) |
| GET | `/api/v5/property-definitions` | GetPropertyDefinitionsV2 | Returns a list of property definitions. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use offset = 20, limit = 10. | `statusFilter`:EntityStatusDto?; `limit`:int?; `offset`:int?; `x_Data_Languages`:string; `langFallback`:bool?; `sortBy`:PropertyDefinitionSortByDto?; `sortDir`:SortDirectionDto?; `isSystem`:bool?; `isEditable`:bool?; `isSearchableFacet`:bool?; `hasUserContext`:bool?; `listType`:PropertyDefinitionListTypeDto?; `isCustomValueAllowed`:bool?; `valueTypes`:IList<PropertyValueTypeDto>; `entityTypeIdentifiers`:IList<string>; `searchFilter`:string; `isUrl`:bool?; `propertyGroupIdentifiers`:IList<string> |  | IList<PropertyDefinitionDto> (200) |
| GET | `/api/v6/property-definitions` | GetPropertyDefinitionsV3 | Returns a list of property definitions. Paging is expressed through the offset and limit parameters. To display Page 3 with a page Size of 10, use offset = 20, limit = 10. | `statusFilter`:EntityStatusDto?; `limit`:int?; `offset`:int?; `x_Data_Languages`:string; `langFallback`:bool?; `sortBy`:PropertyDefinitionSortByDto?; `sortDir`:SortDirectionDto?; `isSystem`:bool?; `isEditable`:bool?; `isSearchableFacet`:bool?; `hasUserContext`:bool?; `listType`:PropertyDefinitionListTypeDto?; `isCustomValueAllowed`:bool?; `valueTypes`:IList<PropertyValueTypeDto>; `entityTypeIdentifiers`:IList<string>; `searchFilter`:string; `definitionTypes`:IList<PropertyDefinitionTypeDto>; `propertyGroupIdentifiers`:IList<string> |  | IList<PropertyDefinitionDto> (200) |
| POST | `/api/v6/property-definitions` | CreatePropertyDefinition | Create a new property definition. |  | CreatePropertyDefinitionDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/property-definitions/{propertyDefinitionIdentifier}` | GetPropertyDefinition | Retrieves a single property definition. | `propertyDefinitionIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | PropertyDefinitionDto (200) |
| PUT | `/api/v6/property-definitions/{propertyDefinitionIdentifier}` | UpdatePropertyDefinition | Updates a property definition. | `propertyDefinitionIdentifier`:string *(path)* | UpdatePropertyDefinitionDto | RowVersionDto (200) |
| DELETE | `/api/v6/property-definitions/{propertyDefinitionIdentifier}` | DeletePropertyDefinition | Deletes the specified property definition. | `propertyDefinitionIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/property-definitions/value-types` | GetPropertyDefinitionValueTypes | Retrieve all value types. |  |  | IList<string> (200) |

#### PropertyGroups (6 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/property-groups` | GetPropertyGroups | Returns a list of property groups. | `statusFilter`:EntityStatusDto?; `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int?; `sortBy`:PropertyGroupSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string |  | IList<PropertyGroupDto> (200) |
| POST | `/api/v6/property-groups` | CreatePropertyGroup | Creates a property group. |  | CreatePropertyGroupDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/property-groups/{propertyGroupIdentifier}` | GetPropertyGroup | Returns a specific property group. | `propertyGroupIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | PropertyGroupDto (200) |
| PATCH | `/api/v6/property-groups/{propertyGroupIdentifier}` | UpdatePropertyGroup | Updates a property group. | `propertyGroupIdentifier`:string *(path)* | UpdatePropertyGroupDto | RowVersionDto (200) |
| DELETE | `/api/v6/property-groups/{propertyGroupIdentifier}` | DeletePropertyGroup | Deletes the specified property group. | `propertyGroupIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/property-groups/{propertyGroupIdentifier}/property-definitions` | GetPropertyDefinitions | Returns a list of property definitions for the specified property group identifier. | `propertyGroupIdentifier`:string *(path)*; `statusFilter`:EntityStatusDto?; `x_Data_Languages`:string; `langFallback`:bool? |  | IList<PropertyDefinitionDto> (200) |

#### PropertySets (5 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/propertysets/{propertySetIdentifier}` | GetPropertySet | Returns a single property set for the provided identifier | `propertySetIdentifier`:string *(path)* |  | PropertySetDto (200) |
| PUT | `/api/v6/propertysets/{propertySetIdentifier}` | UpdatePropertySet | Updates an existing property set | `propertySetIdentifier`:string *(path)* | UpdatePropertySetDto | RowVersionDto (200) |
| DELETE | `/api/v6/propertysets/{propertySetIdentifier}` | DeletePropertySet | Deletes the specified property set | `propertySetIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/propertysets` | GetPropertySets | Returns a list of property sets | `entityTypeIdentifier`:string; `isDefault`:bool?; `isGlobal`:bool? |  | IList<PropertySetDto> (200) |
| POST | `/api/v6/propertysets` | CreatePropertySet | Create a new property set |  | CreatePropertySetDto | IdentifierRowVersionDto (200) |

#### Public (3 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/public/shares` | GetShareDerivations | Get the derivations from the share recipient token | `token`:string |  | PublicShareDto (200) |
| POST | `/api/v6/public/stream-formdata` | DownloadCompressedAssetDerivationsForm | Generates a Zip file of specified derivations for the specified assets. This endpoint can be used to facilitate the download of a Zip file by a Browser through an HTML form with an encoding type of 'multipart/form-data'. The "zipDownloadRequest" object is expected as JSON encoded form data part. | `zipDownloadRequest`:IO.Stream; `token`:string |  | FileResponse () |
| POST | `/api/v6/public/stream` | DownloadCompressedAssetDerivations | Generates a Zip file of specified derivations for the specified assets. | `accept`:string; `token`:string | ZipDownloadRequestDto | FileResponse () |

#### RecycleBin (2 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/recycle-bin` | GetRecycleBinTree | Returns the recycle-bin tree. | `x_Data_Languages`:string; `langFallback`:bool? |  | CategoryDto (200) |
| DELETE | `/api/v6/recycle-bin` | EmptyRecycleBin | Delete all items in the recycle-bin. |  |  | (leer) (204) |

#### Relations (1 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/relations/bulk-delete` | BulkDeleteRelations | Deletes relations in bulk. |  | DeleteRelationsDto | (leer) (204) |

#### RelationTypes (6 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/relation-types/{relationTypeIdentifier}` | GetRelationType | Returns a single relation type for the provided relation type identifier. | `relationTypeIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool |  | RelationTypeDto (200) |
| PUT | `/api/v6/relation-types/{relationTypeIdentifier}` | UpdateRelationType | Updates a relation type by relation type identifier. | `relationTypeIdentifier`:string *(path)* | UpdateRelationTypeDto | RowVersionDto (200) |
| DELETE | `/api/v6/relation-types/{relationTypeIdentifier}` | DeleteRelationType | Deletes a relation type by relation type identifier. | `relationTypeIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/relation-types` | GetRelationTypes | Returns a list of relation types. | `statusFilter`:EntityStatusDto?; `x_Data_Languages`:string; `langFallback`:bool |  | IList<RelationTypeDto> (200) |
| POST | `/api/v6/relation-types` | CreateRelationType | Creates a relation type. |  | CreateRelationTypeDto | IdentifierRowVersionDto (200) |
| PUT | `/api/v6/relation-types/{relationTypeIdentifier}/allowed-entity-relations` | SetAllowedEntityRelations | Sets edge type to node type relations. All existing relations from that edge type are deleted and replaced with the new ones. | `relationTypeIdentifier`:string *(path)* | IList<EntityTypeMappingDto> | (leer) (204) |

#### Roles (9 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/roles` | GetRoles | Returns a list of role summaries | `statusFilter`:EntityStatusDto?; `x_Data_Languages`:string; `langFallback`:bool; `objectTypeIdentifiers`:IList<ObjectTypeDto> |  | IList<RoleSummaryDto> (200) |
| POST | `/api/v6/roles` | CreateRole | Creates a role |  | CreateRoleDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/roles/{roleIdentifier}` | GetRole | Returns a single role for the provided role identifier | `roleIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool |  | RoleSummaryDto (200) |
| PUT | `/api/v6/roles/{roleIdentifier}` | UpdateRole | Updates a role by identifier | `roleIdentifier`:string *(path)* | RoleDto | RowVersionDto (200) |
| DELETE | `/api/v6/roles/{roleIdentifier}` | DeleteRole | Deletes a role by identifier | `roleIdentifier`:string *(path)* |  | (leer) (204) |
| PUT | `/api/v6/roles/{roleIdentifier}/object-type-permissions` | SetObjectTypePermissions | Sets object type permission of a role | `roleIdentifier`:string *(path)* | IList<PermissionDto> | (leer) (204) |
| PUT | `/api/v6/roles/{roleIdentifier}/property-definition-restrictions` | SetPropertyDefinitionRestrictions | Sets property restrictions of a role | `roleIdentifier`:string *(path)* | IList<PropertyDefinitionRestrictionDto> | (leer) (204) |
| PUT | `/api/v6/roles/{roleIdentifier}/derivation-type-restrictions` | SetDerivationTypeRestrictions | Sets derivation type restrictions of a role | `roleIdentifier`:string *(path)* | IList<DerivationTypeRestrictionDto> | (leer) (204) |
| PUT | `/api/v6/roles/{roleIdentifier}/property-value-restrictions` | SetPropertyValueRestrictions | Sets property value restrictions of a role | `roleIdentifier`:string *(path)* | IList<CreatePropertyValueRestrictionDto> | (leer) (204) |

#### Rules (9 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/rules` | CreateRule | Create a new rule from a CreateRuleDto. |  | CreateRuleDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/rules` | GetRules | Get a list of all RuleDto. | `x_Data_Languages`:string; `langFallback`:bool? |  | IList<RuleDto> (200) |
| GET | `/api/v6/rules/{ruleIdentifier}` | GetRule | Get a single RuleDto by ruleIdentifier. | `ruleIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | RuleDto (200) |
| PUT | `/api/v6/rules/{ruleIdentifier}` | UpdateRule | Updates a specific rule by ruleIdentifier and an UpdateRuleDto. | `ruleIdentifier`:string *(path)* | UpdateRuleDto | (leer) (204) |
| DELETE | `/api/v6/rules/{ruleIdentifier}` | DeleteRule | Deletes a rule by ruleIdentifier. | `ruleIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/rules/execute` | ExecuteRules | Executes rules on entities according to the filter specification in executeRulesDto. |  | ExecuteRulesDto | IList<IdentifierRowVersionDto> (200) |
| GET | `/api/v6/rules/report` | GetRuleExecutionReports | Get a paged list of rule execution reports, latest on top. | `limit`:int?; `offset`:int?; `x_Data_Languages`:string; `langFallback`:bool? |  | IList<RuleExecutionReportDto> (200) |
| GET | `/api/v6/rules/{ruleJobIdentifier}/report` | GetRuleExecutionReport | Get a specific rule execution report by ruleJobIdentifier. | `ruleJobIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | RuleExecutionReportDto (200) |
| GET | `/api/v6/rules/{ruleJobIdentifier}/report/details` | GetRuleExecutionReportDetails | Get a page of a rule execution detail report. | `ruleJobIdentifier`:string *(path)*; `limit`:int?; `offset`:int?; `x_Data_Languages`:string; `langFallback`:bool? |  | IList<RuleExecutionDetailReportDto> (200) |

#### SavedSearches (11 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/saved-searches` | GetSavedSearches | Returns a list of saved searches from the actual user. |  |  | IList<SavedSearchDto> (200) |
| POST | `/api/v6/saved-searches` | CreateSavedSearch | Create a new saved search. |  | CreateSavedSearchDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/saved-searches/users` | GetSavedSearchUsers | Returns a list of users eligible for saved search related actions. | `sortBy`:UserSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `limit`:int?; `offset`:int? |  | IList<UserInfoDto> (200) |
| GET | `/api/v6/saved-searches/user-groups` | GetSavedSearchUserGroups | Returns a list of user groups eligible for saved search related actions. | `tenantIdentifiers`:IList<string>; `sortBy`:UserGroupSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `limit`:int?; `offset`:int? |  | IList<UserGroupDto> (200) |
| GET | `/api/v6/saved-searches/{savedSearchIdentifier}` | GetSavedSearch | Get a saved search by identifier. | `savedSearchIdentifier`:string *(path)* |  | SavedSearchDto (200) |
| PUT | `/api/v6/saved-searches/{savedSearchIdentifier}` | UpdateSavedSearch | Updates an existing saved search. | `savedSearchIdentifier`:string *(path)* | UpdateSavedSearchDto | RowVersionDto (200) |
| DELETE | `/api/v6/saved-searches/{savedSearchIdentifier}` | DeleteSavedSearch | Deletes the specified saved search. | `savedSearchIdentifier`:string *(path)* |  | FileResponse () |
| GET | `/api/v6/saved-searches/{savedSearchIdentifier}/resource-permissions` | GetResourcePermissions | Returns the resource permissions on a single saved search. | `savedSearchIdentifier`:string *(path)* |  | IList<ResourcePermissionDto> (200) |
| PUT | `/api/v6/saved-searches/{savedSearchIdentifier}/resource-permissions` | UpdateSavedSearchResourcePermissions | Assigns resource permissions to this saved search | `savedSearchIdentifier`:string *(path)*; `force`:bool? | IList<UpdateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| POST | `/api/v6/saved-searches/{savedSearchIdentifier}/resource-permissions` | CreateResourcePermissions |  | `savedSearchIdentifier`:string *(path)* | IList<CreateResourcePermissionDto> | IList<IdentifierRowVersionDto> (204) |
| DELETE | `/api/v6/saved-searches/{savedSearchIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | DeleteResourcePermission |  | `savedSearchIdentifier`:string *(path)*; `resourcePermissionIdentifier`:string *(path)* |  | (leer) (204) |

#### Search (4 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| POST | `/api/v6/search/reindex` | ReIndex | Trigger a complete re-index of the desired entity types. It's an asynchronous process. |  | ReIndexEntityTypeDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/search/reindex` | GetReIndexingJobs | Get a list of the latest re-indexing jobs | `limit`:int?; `offset`:int? |  | IList<ReIndexingJobDto> (200) |
| GET | `/api/v6/search/reindex/{reIndexIdentifier}` | GetReIndexReport | Get a specific re-indexing job report | `reIndexIdentifier`:string *(path)* |  | ReIndexingJobReportDto (200) |
| DELETE | `/api/v6/search/reindex/{reIndexIdentifier}` | CancelReIndexJob | Cancel a re-indexing job | `reIndexIdentifier`:string *(path)* |  | (leer) (204) |

#### Shares (4 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| PUT | `/api/v6/shares/{shareIdentifier}` | UpdateShare | Update a share | `shareIdentifier`:string *(path)* | UpdateShareDto | RowVersionDto (200) |
| DELETE | `/api/v6/shares/{shareIdentifier}` | DeleteShare | Delete a share | `shareIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/shares/{shareIdentifier}/recipients` | AddShareRecipient | Add a recipient to a share | `shareIdentifier`:string *(path)* | CreateShareRecipientDto | (leer) (204) |
| DELETE | `/api/v6/shares/{shareIdentifier}/recipients/{token}` | RemoveShareRecipient | Remove a recipient from a share | `shareIdentifier`:string *(path)*; `token`:string *(path)* |  | (leer) (204) |

#### Tasks (28 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/tasks/{taskIdentifier}` | GetTaskByIdentifier | Get a specific task by identifier | `taskIdentifier`:string *(path)* |  | TaskDto (200) |
| PUT | `/api/v6/tasks/{taskIdentifier}` | UpdateTask | Update a specific task by identifier | `taskIdentifier`:string *(path)* | UpdateTaskDto | RowVersionDto (200) |
| DELETE | `/api/v6/tasks/{taskIdentifier}` | DeleteTask | Deletes an existing task. All attached assignees, comments and assets links will also be deleted. | `taskIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/tasks/search` | SearchTasks | Returns a list of tasks for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `cursor`:string | TaskSearchQueryDto | SearchResultOfTaskV6Dto (200) |
| POST | `/api/v5/tasks/search` | SearchTasks2 |  | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int? | TaskSearchQueryDto | SearchResultOfTaskDto (200) |
| POST | `/api/v6/tasks/quicksearch` | QuickSearchTasks | Returns a list of auto-complete suggestions or quick search tasks ("did you mean" / or direct hits) for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `includeQuickSearch`:bool?; `includeSuggestions`:bool?; `limit`:int?; `offset`:int? | TaskSearchQueryDto | QuickSearchResultOfTaskDto (200) |
| POST | `/api/v6/tasks/{taskIdentifier}/assignees` | AddAssigneeToTask |  | `taskIdentifier`:string *(path)* | TaskAssigneeDto | (leer) (204) |
| DELETE | `/api/v6/tasks/{taskIdentifier}/assignees/{userIdentifier}` | RemoveAssigneeFromTask |  | `taskIdentifier`:string *(path)*; `userIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/tasks/{taskIdentifier}/activity-logs` | GetTaskActivityLogs | Retrieve the activity log for the provided task identifier. | `taskIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `allDataLanguages`:bool?; `limit`:int?; `offset`:int? |  | IList<ActivityLogDto> (200) |
| POST | `/api/v6/tasks/{taskIdentifier}/move` | MoveTask | Move this task to another state, or change the position of this task in a state | `taskIdentifier`:string *(path)* | MoveEntityDto | RowVersionDto (200) |
| PUT | `/api/v6/tasks/{taskIdentifier}/transition` | ExecuteStatusTransition | Execute a status transition on a task | `taskIdentifier`:string *(path)* | ExecuteTaskStatusTransitionDto | RowVersionDto (200) |
| PUT | `/api/v6/tasks/{taskIdentifier}/link/assets/{assetIdentifier}` | LinkAssetToTask | Links an existing asset into an already existing task. Existing links to other tasks will be left as is. | `taskIdentifier`:string *(path)*; `assetIdentifier`:string *(path)* |  | (leer) (204) |
| PUT | `/api/v6/tasks/{taskIdentifier}/unlink/assets/{assetIdentifier}` | UnlinkAssetFromTask | Unlinks an existing asset from a task. | `taskIdentifier`:string *(path)*; `assetIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/tasks/{taskIdentifier}/comments` | CreateComment | Create a new comment for the specified task. | `taskIdentifier`:string *(path)* | CreateOrUpdateCommentDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/tasks/{taskIdentifier}/comments` | GetComments | Returns a list of comments for the provided task identifier. | `taskIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `sortBy`:CommentSortByDto?; `sortDir`:SortDirectionDto?; `limit`:int?; `offset`:int? |  | IList<CommentDto> (200) |
| GET | `/api/v6/tasks/{taskIdentifier}/assets` | GetAssetsByTask | Returns a list of assets for the defined task identifier. Paging is expressed through the offset and limit parameters. To display page 3 with a page size of 10, use offset = 20, limit = 10. | `taskIdentifier`:string *(path)*; `limit`:int; `offset`:int |  | IList<AssetDto> (200) |
| GET | `/api/v6/tasks/task-types` | GetTaskTypes | Returns a list of task types. |  |  | IList<string> (200) |
| POST | `/api/v6/tasks/{taskIdentifier}/properties` | CreateTaskProperties | Create and add a list of metadata properties to a task. | `taskIdentifier`:string *(path)* | IList<CreatePropertyDto> | IList<IdentifierRowVersionDto> (200) |
| GET | `/api/v6/tasks/{taskIdentifier}/properties` | GetTaskProperties | Return the metadata properties of a task. | `taskIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | IList<PropertyDto> (200) |
| PUT | `/api/v6/tasks/{taskIdentifier}/properties` | PutTaskProperties | Create or update multiple properties on a task. Values of existing properties will be replaced by the values from the request. In case of translated properties, omitting a language will drop the value of that language. In case of user context properties, omitting a user context will drop the value of that user context. Properties that exist on the task but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `taskIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| PATCH | `/api/v6/tasks/{taskIdentifier}/properties` | PatchTaskProperties | Create or patch multiple properties on a task. Values of existing properties will be patched by the values from the request. In case of translated properties, omitting a language will ignore the value of that language. In case of user context properties, omitting a user context will ignore the value of that user context. Properties that exist on the task but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `taskIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| PATCH | `/api/v6/tasks/{taskIdentifier}/properties/{propertyIdentifier}` | EditTaskProperty | Edit a metadata property of a task. Does not delete existing property values that were not sent as part of the request. | `taskIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyDto | RowVersionDto (200) |
| PUT | `/api/v6/tasks/{taskIdentifier}/properties/{propertyIdentifier}` | UpdateTaskProperty | Update an existing metadata property of the specified task. Will drop property values not included in this operation. | `taskIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyDto | RowVersionDto (200) |
| DELETE | `/api/v6/tasks/{taskIdentifier}/properties/{propertyIdentifier}` | DeleteTaskProperty | Delete a metadata property of a task. | `taskIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/tasks/{taskIdentifier}/bulk-delete-properties` | DeleteTaskProperties | Deletes existing properties of a task.  Returns a result for each property indicating whether the property was successfully deleted. | `taskIdentifier`:string *(path)* | DeletePropertiesDto | IList<DeletePropertyResultDto> (200) |
| POST | `/api/v6/tasks/{taskIdentifier}/watchers` | AddWatcherToTask | Add a list of watchers to a task | `taskIdentifier`:string *(path)* | UserIdentifiersDto | (leer) (204) |
| GET | `/api/v6/tasks/{taskIdentifier}/watchers` | GetTaskWatchers | Get the watchers of a task | `taskIdentifier`:string *(path)* |  | IList<UserInfoDto> (200) |
| POST | `/api/v6/tasks/{taskIdentifier}/watchers/bulk-delete` | RemoveWatchersFromTask | Remove a list of watchers from a task | `taskIdentifier`:string *(path)* | UserIdentifiersDto | (leer) (204) |

#### Tenants (7 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/tenants/{tenantIdentifier}` | GetTenant | Returns a single tenant for the provided identifier | `tenantIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | TenantExtendedDto (200) |
| PUT | `/api/v6/tenants/{tenantIdentifier}` | UpdateTenant | Updates an existing tenant | `tenantIdentifier`:string *(path)* | TenantDto | RowVersionDto (200) |
| DELETE | `/api/v6/tenants/{tenantIdentifier}` | DeleteTenant | Deletes the specified tenant | `tenantIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/tenants` | GetTenants | Returns a list of tenants. | `x_Data_Languages`:string; `langFallback`:bool?; `userGroupIdentifier`:IList<string> |  | IList<TenantExtendedDto> (200) |
| POST | `/api/v6/tenants` | CreateTenant | Create a new tenant |  | TenantDto | IdentifierRowVersionDto (200) |
| PUT | `/api/v6/tenants/{tenantIdentifier}/usergroup-permissions` | SetUserGroupPermissions | Define global user group permissions by assigning a role to a user group. That mapping can be made by tenant. That allows to globally assign limited rights to users from a specific tenant to access resources from another tenant. | `tenantIdentifier`:string *(path)* | IList<CreateUserGroupRoleMapDto> | (leer) (204) |
| GET | `/api/v6/tenants/permissions` | GetTenantsPermissions | Returns a list of tenants for the current user with create, update, delete permissions on users and user groups. |  |  | IList<TenantPermissionsDto> (200) |

#### UserContexts (5 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/user-context` | GetUserContexts | Retrieve a list of all user context. | `x_Data_Languages`:string; `langFallback`:bool? |  | IList<UserContextDto> (200) |
| POST | `/api/v6/user-context` | CreateUserContext | Create a new user context. If no identifier is specified in CreateUserContextDto, a new one is generated. |  | CreateUserContextDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/user-context/{userContextIdentifier}` | GetUserContext | Retrieves a single user context. | `x_Data_Languages`:string; `userContextIdentifier`:string *(path)*; `langFallback`:bool? |  | UserContextDto (200) |
| PUT | `/api/v6/user-context/{userContextIdentifier}` | UpdateUserContext | Updates a user context. | `userContextIdentifier`:string *(path)* | UpdateUserContextDto | RowVersionDto (200) |
| DELETE | `/api/v6/user-context/{userContextIdentifier}` | DeleteUserContext | Deletes the specified user context. | `userContextIdentifier`:string *(path)* |  | (leer) (204) |

#### UserGroups (7 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/user-groups` | SearchUserGroups | Returns a list of user groups for the defined search-query | `tenantIdentifiers`:IList<string>; `sortBy`:UserGroupSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `limit`:int?; `offset`:int? |  | IList<UserGroupExtendedDto> (200) |
| POST | `/api/v6/user-groups` | CreateUserGroup | Creates a user group |  | CreateUserGroupDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/user-groups/{userGroupIdentifier}` | GetUserGroup | Retrieves a specific user group | `userGroupIdentifier`:string *(path)* |  | UserGroupExtendedDto (200) |
| PUT | `/api/v6/user-groups/{userGroupIdentifier}` | UpdateUserGroup | Updates a user group | `userGroupIdentifier`:string *(path)*; `name`:string; `description`:string |  | RowVersionDto (200) |
| DELETE | `/api/v6/user-groups/{userGroupIdentifier}` | DeleteUserGroup | Deletes a user group | `userGroupIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/user-groups/{userGroupIdentifier}/users` | AddUserToUserGroup | Adds a user to a user group | `userGroupIdentifier`:string *(path)* | UserGroupMemberDto | (leer) (204) |
| DELETE | `/api/v6/user-groups/{userGroupIdentifier}/users/{userIdentifier}` | RemoveUserFromUserGroup | Removes a user from a user group | `userGroupIdentifier`:string *(path)*; `userIdentifier`:string *(path)* |  | (leer) (204) |

#### Users (13 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/users` | SearchUsers | Returns a list of users for the defined search-query | `tenantIdentifiers`:IList<string>; `sortBy`:UserSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `statusFilter`:EntityStatusDto?; `userOriginFilter`:UserOriginDto?; `accessType`:AccessTypeDto?; `userGroupsFilter`:IList<string>; `userContextFilter`:IList<string>; `accountStatusFilter`:IList<AccountStatusDto>; `limit`:int?; `offset`:int? |  | IList<UserAccountInfoDto> (200) |
| POST | `/api/v6/users` | CreateUser | Creates a user |  | CreateUserDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/users/{userIdentifier}` | GetUser | Retrieves a specific user | `userIdentifier`:string *(path)* |  | UserDto (200) |
| PUT | `/api/v6/users/{userIdentifier}` | UpdateUser | Updates a user | `userIdentifier`:string *(path)* | UpdateUserDto | RowVersionDto (200) |
| DELETE | `/api/v6/users/{userIdentifier}` | DeleteUser | Deletes a user | `userIdentifier`:string *(path)* |  | (leer) (204) |
| PUT | `/api/v6/users/{userIdentifier}/password` | SetPassword | Sets a user's password | `userIdentifier`:string *(path)* | SetPasswordDto | (leer) (204) |
| PUT | `/api/v6/users/{userIdentifier}/unlock` | UnlockUser | Unlock a locked user account | `userIdentifier`:string *(path)* |  | RowVersionDto (200) |
| GET | `/api/v6/users/{userIdentifier}/user-groups` | GetUserGroupsForUser | Retrieves the user groups of specific user | `userIdentifier`:string *(path)* |  | IList<UserGroupDto> (200) |
| GET | `/api/v6/users/registrations` | SearchUserRegistrations | Retrieves the registered users. | `tenantIdentifiers`:IList<string>; `sortBy`:UserSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `accessType`:AccessTypeDto?; `statusFilter`:IList<AccountStatusDto>; `limit`:int?; `offset`:int? |  | IList<UserRegistrationDto> (200) |
| GET | `/api/v6/users/registrations/{userIdentifier}` | GetUserRegistration | Retrieves a specific user registration | `userIdentifier`:string *(path)* |  | UserRegistrationDto (200) |
| POST | `/api/v6/users/{userIdentifier}/registrations` | FinalizeUserRegistration | Finalize the registration process for the given user. | `userIdentifier`:string *(path)* | FinalizeUserRegistrationDto | RowVersionDto (200) |
| POST | `/api/v6/users/{userIdentifier}/avatar/upload` | UploadAvatar | Uploads a avatar file for the specified user. | `userIdentifier`:string *(path)* | form `fileUpload`:FileParameter; form `createFileDto`:CreateFileDto | CreateFileResponseDto (200) |
| DELETE | `/api/v6/users/{userIdentifier}/avatar` | DeleteAvatar | Deletes the avatar file for the specified user. | `userIdentifier`:string *(path)* |  | (leer) (204) |

#### WatermarkDefinitions (9 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/watermark-definitions` | GetWatermarks | Retrieve a list of all WatermarkDefinition. |  |  | IList<WatermarkDefinitionDto> (200) |
| POST | `/api/v6/watermark-definitions` | CreateWatermarkDefinition | Create a new WatermarkDefinition. If no identifier is specified in CreateWatermarkDefinitionDto, a new one is generated. |  | CreateWatermarkDefinitionDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/watermark-definitions/{watermarkDefinitionIdentifier}` | GetWatermarkDefinition | Retrieves a single WatermarkDefinition. | `watermarkDefinitionIdentifier`:string *(path)* |  | WatermarkDefinitionDto (200) |
| PUT | `/api/v6/watermark-definitions/{watermarkDefinitionIdentifier}` | UpdateWatermarkDefinition | Updates a WatermarkDefinition. | `watermarkDefinitionIdentifier`:string *(path)* | UpdateWatermarkDefinitionDto | RowVersionDto (200) |
| DELETE | `/api/v6/watermark-definitions/{watermarkDefinitionIdentifier}` | DeleteWatermarkDefinition | Deletes the specified WatermarkDefinition. | `watermarkDefinitionIdentifier`:string *(path)* |  | (leer) (204) |
| GET | `/api/v6/watermark-definitions/{watermarkDefinitionIdentifier}/triggers/{watermarkTriggerIdentifier}` | GetWatermarkTrigger | Retrieves a single WatermarkTrigger. | `watermarkDefinitionIdentifier`:string *(path)*; `watermarkTriggerIdentifier`:string *(path)* |  | WatermarkTriggerDto (200) |
| PUT | `/api/v6/watermark-definitions/{watermarkDefinitionIdentifier}/triggers/{watermarkTriggerIdentifier}` | UpdateWatermarkTrigger | Updates a WatermarkTrigger. | `watermarkDefinitionIdentifier`:string *(path)*; `watermarkTriggerIdentifier`:string *(path)* | UpdateWatermarkTriggerDto | RowVersionDto (200) |
| DELETE | `/api/v6/watermark-definitions/{watermarkDefinitionIdentifier}/triggers/{watermarkTriggerIdentifier}` | DeleteWatermarkTrigger | Deletes the specified WatermarkTrigger. | `watermarkDefinitionIdentifier`:string *(path)*; `watermarkTriggerIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/watermark-definitions/{watermarkDefinitionIdentifier}/triggers` | CreateWatermarkTrigger | Create a new WatermarkTrigger. If no identifier is specified in CreateWatermarkTriggerDto, a new one is generated | `watermarkDefinitionIdentifier`:string *(path)* | CreateWatermarkTriggerDto | IdentifierRowVersionDto (200) |

#### Workspaces (65 Operationen)

| Verb | Pfad | OperationId | Zweck | Parameter (path/query) | Request-Body | Response |
|---|---|---|---|---|---|---|
| GET | `/api/v6/workspaces/{workspaceIdentifier}` | GetWorkspace | Retrieve a specific workspace. | `workspaceIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `includeRestrictions`:bool? |  | WorkspaceDto (200) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}` | UpdateWorkspace | Updates an existing workspace. Only own workspaces can be updated, otherwise a 404 (Not Found) is returned. | `workspaceIdentifier`:string *(path)* | UpdateWorkspaceDto | RowVersionDto (200) |
| DELETE | `/api/v6/workspaces/{workspaceIdentifier}` | DeleteWorkspace | Deletes an existing workspace. Only own workspaces can be deleted, otherwise a 404 (Not Found) is returned. | `workspaceIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/workspaces/search` | SearchWorkspaces | Returns a list of workspaces for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `includeThumbnailUris`:int?; `limit`:int?; `cursor`:string | WorkspaceSearchQueryDto | SearchResultOfWorkspaceV6Dto (200) |
| POST | `/api/v5/workspaces/search` | SearchWorkspaces2 |  | `x_Data_Languages`:string; `langFallback`:bool?; `limit`:int?; `offset`:int?; `includeThumbnailUris`:int? | WorkspaceSearchQueryDto | SearchResultOfWorkspaceDto (200) |
| POST | `/api/v6/workspaces/quicksearch` | QuickSearchWorkspaces | Returns a list of auto-complete suggestions or quick search workspaces ("did you mean" / or direct hits) for the defined search-query. | `x_Data_Languages`:string; `langFallback`:bool?; `includeQuickSearch`:bool?; `includeSuggestions`:bool?; `limit`:int?; `offset`:int? | WorkspaceSearchQueryDto | QuickSearchResultOfWorkspaceDto (200) |
| POST | `/api/v6/workspaces` | CreateWorkspace | Create a new workspace. | `fromWorkspaceTemplateIdentifier`:string | CreateWorkspaceDto | PropertyEntityCreationResultDto (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/activity-logs` | GetWorkspaceActivityLogs | Retrieve the activity log for the provided workspace identifier. | `workspaceIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `allDataLanguages`:bool?; `limit`:int?; `offset`:int? |  | IList<ActivityLogDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/avatar/upload` | UploadWorkspaceAvatar | Uploads the avatar file for this workspace. | `workspaceIdentifier`:string *(path)* | form `fileUpload`:FileParameter; form `createFileDto`:CreateFileDto | CreateFileResponseDto (200) |
| DELETE | `/api/v6/workspaces/{workspaceIdentifier}/avatar` | DeleteWorkspaceAvatar | Deletes the avatar of this workspace. | `workspaceIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/comments` | CreateComment | Create a new comment for the specified workspace. | `workspaceIdentifier`:string *(path)* | CreateOrUpdateCommentDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/comments` | GetComments | Returns a list of comments for the provided workspace identifier. | `workspaceIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `sortBy`:CommentSortByDto?; `sortDir`:SortDirectionDto?; `limit`:int?; `offset`:int? |  | IList<CommentDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/tasks` | CreateTask | Create a new task for the specified workspace. | `workspaceIdentifier`:string *(path)* | CreateTaskDto | PropertyEntityCreationResultDto (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/tasks` | GetTasksByWorkspace |  | `workspaceIdentifier`:string *(path)* |  | IList<TaskDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/assets/upload/chunks/start` | StartNewUpload |  | `workspaceIdentifier`:string *(path)* | CreateWorkspaceAssetDto | StartUploadResponseDto (200) |
| POST | `/api/v6/workspaces/upload/chunks/sessions/{uploadSessionId}/sequencenumber/{chunkUploadSequenceNumber}` | AppendChunk |  | `uploadSessionId`:string *(path)*; `chunkUploadSequenceNumber`:long *(path)* |  | (leer) (204) |
| DELETE | `/api/v6/workspaces/upload/chunks/sessions/{uploadSessionId}` | CancelUpload |  | `uploadSessionId`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/assets/upload/chunks/sessions/{uploadSessionId}/finish` | FinishUpload |  | `workspaceIdentifier`:string *(path)*; `uploadSessionId`:string *(path)* |  | CreateAssetResponseDto (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/resource-permissions` | GetResourcePermissions | Returns the resource permissions on a single workspace | `workspaceIdentifier`:string *(path)* |  | IList<ResourcePermissionDto> (200) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}/resource-permissions` | UpdateWorkspaceResourcePermissions | Assigns resource permissions to this workspace | `workspaceIdentifier`:string *(path)*; `force`:bool? | IList<UpdateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/resource-permissions` | CreateResourcePermissions |  | `workspaceIdentifier`:string *(path)* | IList<CreateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | UpdateResourcePermission |  | `workspaceIdentifier`:string *(path)*; `resourcePermissionIdentifier`:string *(path)* | UpdateResourcePermissionDto | RowVersionDto (200) |
| DELETE | `/api/v6/workspaces/{workspaceIdentifier}/resource-permissions/{resourcePermissionIdentifier}` | DeleteResourcePermission |  | `workspaceIdentifier`:string *(path)*; `resourcePermissionIdentifier`:string *(path)*; `force`:bool? |  | (leer) (204) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/templated-resource-permissions` | GetTemplateResourcePermissions | Returns the resource permissions on a single workspace template. These permissions are applied when a workspace is created from a template. | `workspaceIdentifier`:string *(path)* |  | IList<ResourcePermissionDto> (200) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}/templated-resource-permissions` | UpdateWorkspaceTemplatedResourcePermissions | Assigns resource permissions to this workspace template. These permissions are applied when a workspace is created from this template. | `workspaceIdentifier`:string *(path)* | IList<UpdateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/templated-resource-permissions` | CreateTemplateResourcePermissions |  | `workspaceIdentifier`:string *(path)* | IList<CreateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| DELETE | `/api/v6/workspaces/{workspaceIdentifier}/templated-resource-permissions/{resourcePermissionIdentifier}` | DeleteTemplateResourcePermission |  | `workspaceIdentifier`:string *(path)*; `resourcePermissionIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/templated-resource-permissions/bulk-delete` | DeleteTemplateResourcePermissions |  | `workspaceIdentifier`:string *(path)* | DeleteResourcePermissionsDto | (leer) (204) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/properties` | GetWorkspaceProperties | Returns a workspace's metadata properties. Does not support user context. | `workspaceIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | IList<PropertyDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/properties` | CreateProperty | Create and add a metadata Property to a Workspace. | `workspaceIdentifier`:string *(path)* | CreatePropertyDto | IdentifierRowVersionDto (200) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}/properties` | PutWorkspaceProperties | Create or update multiple properties on a workspace. Values of existing properties will be replaced by the values from the request. In case of translated properties, omitting a language will drop the value of that language. In case of user context properties, omitting a user context will drop the value of that user context. Properties that exist on the workspace but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `workspaceIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| PATCH | `/api/v6/workspaces/{workspaceIdentifier}/properties` | PatchWorkspaceProperties | Create or patch multiple properties on a workspace. Values of existing properties will be patched by the values from the request. In case of translated properties, omitting a language will ignore the value of that language. In case of user context properties, omitting a user context will ignore the value of that user context. Properties that exist on the workspace but are not part of the request are not changed.  Returns a result for each property indicating whether the property was successfully modified. | `workspaceIdentifier`:string *(path)* | SetPropertiesDto | IList<SetPropertyResultDto> (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/properties/{propertyIdentifier}` | GetWorkspaceProperty | Returns a specific metadata property of a workspace | `workspaceIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)*; `x_Data_Languages`:string; `x_User_Contexts`:string; `langFallback`:bool?; `allDataLanguages`:bool? |  | PropertyDto (200) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}/properties/{propertyIdentifier}` | UpdateProperty | Update an existing metadata Property of the specified workspace Will drop property values not included in this operation | `workspaceIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyDto | RowVersionDto (204) |
| PATCH | `/api/v6/workspaces/{workspaceIdentifier}/properties/{propertyIdentifier}` | EditWorkspaceProperty | Edit a metadata property of a workspace. Does not delete existing property values that were not sent as part of the request. | `workspaceIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* | UpdatePropertyDto | RowVersionDto (200) |
| DELETE | `/api/v6/workspaces/{workspaceIdentifier}/properties/{propertyIdentifier}` | DeleteWorkspaceProperty | Delete a metadata property of a workspace. | `workspaceIdentifier`:string *(path)*; `propertyIdentifier`:string *(path)* |  | (leer) (204) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/bulk-delete-properties` | DeleteWorkspaceProperties | Deletes existing properties of a workspace.  Returns a result for each property indicating whether the property was successfully deleted. | `workspaceIdentifier`:string *(path)* | DeletePropertiesDto | IList<DeletePropertyResultDto> (200) |
| GET | `/api/v6/workspaces/users` | GetUsers | Returns a list of users eligible for workspace related actions. | `sortBy`:UserSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `limit`:int?; `offset`:int? |  | IList<UserInfoDto> (200) |
| GET | `/api/v6/workspaces/user-groups` | GetSavedSearchUserGroups | Returns a list of user groups eligible for workspace related actions. | `tenantIdentifiers`:IList<string>; `sortBy`:UserGroupSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `limit`:int?; `offset`:int? |  | IList<UserGroupDto> (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/settings/my/notifications` | GetWorkspaceUserNotificationSettings | Returns the notification settings for the provided identifier and current user. | `workspaceIdentifier`:string *(path)* |  | UserNotificationSettingsDto (200) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}/settings/my/notifications` | CreateOrUpdateUserNotificationSettings | Create or update the notification settings. | `workspaceIdentifier`:string *(path)* | UserNotificationSettingsDto | RowVersionDto (200) |
| GET | `/api/v4/workspaces/{workspaceIdentifier}/tasks/users` | GetWorkspaceTaskUsers |  | `workspaceIdentifier`:string *(path)*; `sortBy`:UserSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `limit`:int?; `offset`:int? |  | IList<UserInfoDto> (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/users` | GetWorkspaceUsers | Returns a list of users eligible for certain actions in this workspace. | `workspaceIdentifier`:string *(path)*; `requiredPermissions`:IList<PermissionDto>; `sortBy`:UserSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `limit`:int?; `offset`:int? |  | IList<UserInfoDto> (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/tasks/{taskIdentifier}/users` | GetTaskUsers | Returns a list of users eligible for certain actions on a specific task in this workspace. | `workspaceIdentifier`:string *(path)*; `taskIdentifier`:string *(path)*; `requiredPermissions`:IList<PermissionDto>; `sortBy`:UserSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `limit`:int?; `offset`:int? |  | IList<UserInfoDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/shares` | CreateExternalWorkspaceShare | Creates a public share for the selected workspace | `workspaceIdentifier`:string *(path)* | CreateShareDto | IdentifierRowVersionDto (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/shares` | GetWorkspaceShares | Get the shares of a workspace | `workspaceIdentifier`:string *(path)* |  | IList<ShareDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/move` | MoveWorkspace | Move this workspace to another state, or change the position of this workspace in a state | `workspaceIdentifier`:string *(path)* | MoveEntityDto | RowVersionDto (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/states` | GetStates | Get all states of this workspace | `workspaceIdentifier`:string *(path)*; `x_Data_Languages`:string; `langFallback`:bool? |  | IList<StateDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/states` | CreateState | Create a new state in this workspace | `workspaceIdentifier`:string *(path)* | CreateStateDto | IdentifierRowVersionDto (200) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}/states/transitions` | UpdateTransitions | Update the transitions of all the states in this workspace | `workspaceIdentifier`:string *(path)* | UpdateTransitionsDto | (leer) (204) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/states/users` | GetStatesUsers | Returns a list of users eligible for states related actions in this workspace | `workspaceIdentifier`:string *(path)*; `sortBy`:UserSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `limit`:int?; `offset`:int? |  | IList<UserInfoDto> (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/states/user-groups` | GetStatesUserGroups | Returns a list of user groups eligible for states related actions in this workspace | `workspaceIdentifier`:string *(path)*; `tenantIdentifiers`:IList<string>; `sortBy`:UserGroupSortByDto?; `sortDir`:SortDirectionDto?; `searchFilter`:string; `limit`:int?; `offset`:int? |  | IList<UserGroupDto> (200) |
| PATCH | `/api/v6/workspaces/{workspaceIdentifier}/states/{stateIdentifier}` | UpdateState | Update a state in this workspace | `workspaceIdentifier`:string *(path)*; `stateIdentifier`:string *(path)* | UpdateStateDto | RowVersionDto (200) |
| DELETE | `/api/v6/workspaces/{workspaceIdentifier}/states/{stateIdentifier}` | DeleteState | Delete a state in this workspace | `workspaceIdentifier`:string *(path)*; `stateIdentifier`:string *(path)* |  | (leer) (204) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}/states/{stateIdentifier}/move` | MoveState | Change the position of a state in this workspace | `workspaceIdentifier`:string *(path)*; `stateIdentifier`:string *(path)* | EntitySortOrderDto | RowVersionDto (200) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/states/{stateIdentifier}/resource-permissions` | GetStateResourcePermissions | Returns the resource permissions on this state | `workspaceIdentifier`:string *(path)*; `stateIdentifier`:string *(path)* |  | IList<ResourcePermissionDto> (200) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}/states/{stateIdentifier}/resource-permissions` | UpdateStateResourcePermissions | Assigns resource permissions to this state | `workspaceIdentifier`:string *(path)*; `stateIdentifier`:string *(path)*; `force`:bool? | IList<UpdateResourcePermissionDto> | IList<IdentifierRowVersionDto> (200) |
| PUT | `/api/v6/workspaces/{workspaceIdentifier}/states/{stateIdentifier}/transitions` | UpdateStateTransitions | Update the transitions of a state in this workspace | `workspaceIdentifier`:string *(path)*; `stateIdentifier`:string *(path)* | UpdateStateTransitionsDto | RowVersionDto (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/assets/finalize` | FinalizeAssets | Finalize workspace assets. Assets with successful validation are moved to the specified category, other assets are not moved. (Optional, asset set identifiers) If not provided, all valid assets in the workspace are finalized. (Optional, target category identifier) If not provided, the target category of the workspace is used. | `workspaceIdentifier`:string *(path)* | FinalizeAssetSetsDto | AssetSetFinalizationResultDto (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/assets/validate` | ValidateAssets | Validates all assets from a workspace. | `workspaceIdentifier`:string *(path)* |  | IList<ValidateAssetSetPropertiesResultDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/copy-properties` | CopyPropertiesToAssets | Copy workspace properties to assets. If asset set identifiers are set, only those are affected, otherwise all. | `workspaceIdentifier`:string *(path)* | CopyPropertiesDto | IdentifierRowVersionDto (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/copy-all-properties` | CopyAllPropertiesToAssets | Copy all workspace properties to assets. If asset set identifiers are set, only those are affected, otherwise all. | `workspaceIdentifier`:string *(path)* | CopyAllPropertiesDto | IdentifierRowVersionDto (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/watchers` | AddWatchersToWorkspace | Add a list of watchers to a workspace | `workspaceIdentifier`:string *(path)* | UserIdentifiersDto | (leer) (204) |
| GET | `/api/v6/workspaces/{workspaceIdentifier}/watchers` | GetWorkspaceWatchers | Get the watchers of a workspace | `workspaceIdentifier`:string *(path)* |  | IList<UserInfoDto> (200) |
| POST | `/api/v6/workspaces/{workspaceIdentifier}/watchers/bulk-delete` | RemoveWatchersFromWorkspace | Remove a list of watchers from a workspace | `workspaceIdentifier`:string *(path)* | UserIdentifiersDto | (leer) (204) |

## Teil C: Schemas – alle DTOs und Enums (424 Klassen, 70 Enums)

Quelle: `src/Backend.Contracts/Sharedien.Api.Contracts/Public/**` (Namespace `Sharedien.Api.Contracts.Public.*`). Aufbau:

* **C.1 Schlüssel-DTOs im Detail** – eine Tabelle je DTO (Feld, Typ, Flags, Beschreibung aus XML-Doc), inkl. geerbter Felder (`geerbt:<Basisklasse>`), für alle DTOs, deren Name zu den Themen Asset/Category/Node/Property/Search/Permission/Upload/Derivation/License/Buyout/Purchase/Rule/User/Group/Role/Comment/Version/ActivityLog/Notification/Relation/Workspace/Task/Share/Tenant/Job/Extraction/Watermark/FileNaming/RightsOwner/BillingContact/PriceList/Offer/RecycleBin/SavedSearch/CustomEntity/EntityType/Filter/Facet/Sort/Paging/Chunk/Restriction/DataLanguage/Translated/Identifier/RowVersion/Configuration/Info/Public/Statistic/Report/Error passt – und danach alle Enums mit Werten und Nummern.
* **C.2 Alle DTOs kompakt nach Ordner** – vollständige Liste (eine Zeile je DTO: Basis, Beschreibung, Felder `Name: Typ`; `*` required, `~` obsolete, `**[geerbt: …]**` Felder aus Basisklassen; `(obsolete)`/`(abstract)` am Klassennamen) und alle Enums je Ordner (`(string)` = wird als String serialisiert, `StringEnumConverter`).
* **C.3 EventStream-Events** – Event-Klassen der Message Queue mit ihren `ActionEventDetails` (Werte des Message-Properties `x_sharedien_event_action_details`) und `DataKeys` (Schlüssel in `AdditionalData`).

Konventionen: geschlossene Generics wie `SearchResultOfAssetDto : SearchResultDto<AssetDto>` (Items + Facets, v6 zusätzlich `Cursor`) und `QuickSearchResultOfXDto : QuickSearchResultDto<X>` (Items + AutoCompleteSuggestions) sind die Swagger-Schema-Namen. `TranslatedValueDto {DataLanguageIdentifier, Value}` ist der Baustein aller mehrsprachigen Felder. `UserInfoDto` (Identifier, UserName, FirstName, LastName, EmailAddress, AvatarUrl, Status, Company) wird als eingebettete Benutzerreferenz verwendet. Fehlerantworten: `ValidationErrorDto {ParameterName, Errors[], FieldErrors{Field → [FieldErrorDto]}}`.

### C.1 Schlüssel-DTOs und Enums im Detail

#### ActivityLogAffectedDataDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| EntityTypeIdentifier | `string` |  |  |
| ObjectType | `ObjectTypeDto` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| AdditionalData | `IDictionary<string, string>` |  |  |
| AdditionalAffectedData | `IList<ActivityLogAffectedDataDto>` |  |  |

#### ActivityLogChangeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Type | `ActivityLogChangeTypeDto` |  |  |
| AffectedData | `ActivityLogAffectedDataDto` |  |  |

#### ActivityLogDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Type | `ActivityLogTypeDto` |  |  |
| TriggeredBy | `UserInfoDto` |  |  |
| AffectedData | `ActivityLogAffectedDataDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| Changes | `IList<ActivityLogChangeDto>` |  |  |

#### ActivityLogIdentifiersDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ActivityLogIdentifiers | `IList<string>` |  |  |

#### ActivityLogNotificationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| ReadAt | `DateTime?` |  |  |
| Type | `ActivityLogTypeDto` |  |  |
| TriggeredBy | `UserInfoDto` |  |  |
| AffectedData | `ActivityLogAffectedDataDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| Changes | `IList<ActivityLogChangeDto>` |  |  |

#### ActivityLogUpdateAffectedDataDto  <sub>(SignalR)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| EntityTypeIdentifier | `string` |  |  |
| ObjectType | `Webservice.ObjectTypeDto` |  |  |

#### ActivityLogUpdateDto  <sub>(SignalR)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Type | `Webservice.ActivityLogTypeDto` |  |  |
| TriggeredByUserIdentifier | `string` |  |  |
| AffectedData | `ActivityLogUpdateAffectedDataDto` |  |  |
| CreatedAt | `DateTime` |  |  |

#### ArchiveExtractionEntryDto  <sub>(Webservice)</sub>

Represents an entry in an archive.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntryType | `ArchiveEntryTypeDto` |  | The type of the entry. |
| Status | `ArchiveEntryStatusDto` |  | The status of the entry. |
| Path | `string` |  | The full path of the entry, starting from the archive root, including the file or folder name. |
| Name | `string` |  | The name of the entry, without the path. |
| CreatedEntity | `EntityDto` |  | The created entity, if any. |

#### ArchiveExtractionEvent : DataEventBase  <sub>(MessageQueue/V2/EventStream)</sub>

Event that is sent when an archive extraction job is changed

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### ArchiveExtractionJobDto  <sub>(Webservice)</sub>

An archive extraction job.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  | The job identifier. |
| Status | `JobStatusDto` |  | The status of the job. |
| CreatedBy | `UserInfoDto` |  | The user who created the job. |
| CreatedAt | `DateTime` |  | When the job was created. |
| InitiatedAt | `DateTime?` |  | When the job was initiated. |
| FinishedAt | `DateTime?` |  | When the job was finished. |
| ArchiveAssetSetIdentifier | `string` |  | The identifier of the archive asset to extract. |
| OutputCategoryIdentifier | `string` |  | The identifier of the output category. The content of the archive is extracted into this category. |
| ArchiveExtractionMode | `ArchiveExtractionModeDto` |  | Whether to extract with full path (default) or only files. |
| DeleteAssetAfterExtraction | `bool` |  | Whether the archive asset set should be deleted after extraction. |
| TotalEntryCount | `int` |  | The total number of entries of the archive. Populated during extraction, fully available upon completion of the job. |
| FailedEntryCount | `int` |  | The number of entries in the archive which failed to extract. Populated during extraction, fully available upon completion of the job. |
| ExtractedEntryCount | `int` |  | The number of extracted entries of the archive. Populated during extraction, fully available upon completion of the job. |
| FilteredEntryCount | `int` |  | The number of entries in the archive which were filtered. Populated during extraction, fully available upon completion of the job. |
| ApplicablePermissions | `IList<string>` |  |  |

#### ArchiveExtractionJobReportDto : ArchiveExtractionJobDto  <sub>(Webservice)</sub>

The report to an archive extraction job.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Entries | `IList<ArchiveExtractionEntryDto>` |  | The content of the archive. Populated during extraction, fully available upon completion of the job. |
| Identifier | `string` | geerbt:ArchiveExtractionJobDto | The job identifier. |
| Status | `JobStatusDto` | geerbt:ArchiveExtractionJobDto | The status of the job. |
| CreatedBy | `UserInfoDto` | geerbt:ArchiveExtractionJobDto | The user who created the job. |
| CreatedAt | `DateTime` | geerbt:ArchiveExtractionJobDto | When the job was created. |
| InitiatedAt | `DateTime?` | geerbt:ArchiveExtractionJobDto | When the job was initiated. |
| FinishedAt | `DateTime?` | geerbt:ArchiveExtractionJobDto | When the job was finished. |
| ArchiveAssetSetIdentifier | `string` | geerbt:ArchiveExtractionJobDto | The identifier of the archive asset to extract. |
| OutputCategoryIdentifier | `string` | geerbt:ArchiveExtractionJobDto | The identifier of the output category. The content of the archive is extracted into this category. |
| ArchiveExtractionMode | `ArchiveExtractionModeDto` | geerbt:ArchiveExtractionJobDto | Whether to extract with full path (default) or only files. |
| DeleteAssetAfterExtraction | `bool` | geerbt:ArchiveExtractionJobDto | Whether the archive asset set should be deleted after extraction. |
| TotalEntryCount | `int` | geerbt:ArchiveExtractionJobDto | The total number of entries of the archive. Populated during extraction, fully available upon completion of the job. |
| FailedEntryCount | `int` | geerbt:ArchiveExtractionJobDto | The number of entries in the archive which failed to extract. Populated during extraction, fully available upon completion of the job. |
| ExtractedEntryCount | `int` | geerbt:ArchiveExtractionJobDto | The number of extracted entries of the archive. Populated during extraction, fully available upon completion of the job. |
| FilteredEntryCount | `int` | geerbt:ArchiveExtractionJobDto | The number of entries in the archive which were filtered. Populated during extraction, fully available upon completion of the job. |
| ApplicablePermissions | `IList<string>` | geerbt:ArchiveExtractionJobDto |  |

#### AssetDerivationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetTypeIdentifier | `string` |  |  |
| FileTypeIdentifier | `string` |  |  |
| FileMimeType | `string` |  |  |
| DerivationTypeIdentifier | `string` |  |  |
| FileName | `string` |  |  |
| FileEnding | `string` |  |  |
| FileSize | `long` |  |  |
| Uri | `string` |  |  |
| DownloadUri | `string` |  |  |
| FileHash | `string` |  |  |
| Status | `AssetDerivationStatusDto` |  |  |

#### AssetDerivationJobAssetDerivationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` |  |  |
| AssetVersionIdentifier | `string` |  |  |
| DerivationTypeIdentifier | `string` |  |  |
| AssetTypeIdentifier | `string` |  |  |
| FileTypeIdentifier | `string` |  |  |
| FileMimeType | `string` |  |  |
| FileName | `string` |  |  |
| FileEnding | `string` |  |  |
| FileSize | `long` |  |  |
| Status | `AssetDerivationStatusDto` |  |  |
| AssetDerivation | `AssetDerivationDto` | obsolete |  |

#### AssetDerivationJobDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Status | `JobStatusDto` |  |  |
| CreatedBy | `UserInfoDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| InitiatedAt | `DateTime?` |  |  |
| FinishedAt | `DateTime?` |  |  |
| TotalAssetDerivationCount | `int` |  |  |
| FailedAssetDerivationCount | `int` |  |  |
| SucceededAssetDerivationCount | `int` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |

#### AssetDerivationJobReportDto : AssetDerivationJobDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetDerivations | `IList<AssetDerivationJobAssetDerivationDto>` |  |  |
| Identifier | `string` | geerbt:AssetDerivationJobDto |  |
| Status | `JobStatusDto` | geerbt:AssetDerivationJobDto |  |
| CreatedBy | `UserInfoDto` | geerbt:AssetDerivationJobDto |  |
| CreatedAt | `DateTime` | geerbt:AssetDerivationJobDto |  |
| InitiatedAt | `DateTime?` | geerbt:AssetDerivationJobDto |  |
| FinishedAt | `DateTime?` | geerbt:AssetDerivationJobDto |  |
| TotalAssetDerivationCount | `int` | geerbt:AssetDerivationJobDto |  |
| FailedAssetDerivationCount | `int` | geerbt:AssetDerivationJobDto |  |
| SucceededAssetDerivationCount | `int` | geerbt:AssetDerivationJobDto |  |
| ApplicablePermissions | `IList<string>` | geerbt:AssetDerivationJobDto |  |

#### AssetDerivationTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` | required |  |
| AssetVersionIdentifier | `string` |  |  |
| DerivationTypeIdentifier | `string` |  |  |

#### AssetDerivationUploadDto  <sub>(Webservice/Upload/AssetDerivation)</sub>

Information required to upload an asset derivation

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` | required | The asset for which to upload the derivation |
| DerivationTypeIdentifier | `string` | required | The identifier of the derivation type which will be uploaded |
| AssetVersionIdentifier | `string` |  | An optional version identifier of the asset for which to upload the derivation |

#### AssetDerivationsRequestDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` | required |  |
| AssetTypeDerivationTypes | `IDictionary<string, IList<string>>` |  |  |

#### AssetDerivationsResponseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DerivationsByAsset | `IDictionary<string, IList<AssetDerivationDto>>` |  |  |

#### AssetDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Status | `string` |  | The AssetVersion Status |
| ThumbnailUri | `string` |  |  |
| AlternativeThumbnailUri | `string` |  |  |
| WatermarkThumbnailUri | `string` |  |  |
| FileName | `string` |  |  |
| AssetType | `string` |  | The AssetType Identifier |
| Created | `DateTime` |  |  |
| Version | `long` |  |  |
| DisplayProperties | `IDictionary<string, string>` |  |  |
| Properties | `IList<PropertyDto>` |  |  |
| CategoryProperties | `IList<CategoryPropertiesDto>` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |
| PropertyDefinitionRestrictions | `ISet<PropertyDefinitionRestrictionDto>` |  |  |
| ParentCategoryIdentifier | `string` |  |  |
| LinkedCategoryIdentifiers | `IList<string>` |  |  |

#### AssetDuplicateDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` |  |  |
| FileName | `string` |  |  |
| ImportedBy | `UserInfoDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| DuplicateAssetSetIdentifier | `string` |  |  |
| DuplicateFileName | `string` |  |  |
| DuplicateImportedBy | `UserInfoDto` |  |  |
| DuplicateCreatedAt | `DateTime` |  |  |

#### AssetEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

Event that is sent when Asset-Data is changed

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### AssetPropertyMutationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` |  |  |
| AssetVersionIdentifier | `string` |  |  |
| AssetVersionFileName | `string` |  |  |
| AssetVersionNumber | `int` |  |  |
| PropertyMutations | `IList<PropertyMutationResultDto>` |  |  |

#### AssetReDerivationEvent : DataEventBase  <sub>(MessageQueue/V2/EventStream)</sub>

Event that is sent when an asset re-derivation job is changed

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### AssetSearchQueryDto : SearchQueryBaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SearchInDocumentExtraction | `bool` |  | Whether or not to search in the text extraction of the binary documents |
| Categories | `IList<string>` |  | Filter in categories (optional). OR conjunct. If not given, no filter is applied |
| RecursivelyFilterCategories | `bool` |  | Whether or not assets descendant to <see cref="Categories"/> are also subject to the filter. Example: if 'true' and the filter is 'root' none of the assets are filtered out. |
| Context | `string` |  | The context (= node type of the top parent) to be searched (e.g. 'Workspace' for Workspace assets or 'Category' for AssetBrowser) |
| Filename | `string` |  | Search for filename only (optional) |
| FileHash | `string` |  | Search for the file hash (optional). Currently only the file hash from the original derivate is stored. |
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### AssetSetDerivationFileNameDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` |  |  |
| DerivationTypeIdentifier | `string` |  |  |
| FileName | `string` |  |  |

#### AssetSetDerivationTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` | required |  |
| DerivationTypeIdentifier | `string` | required |  |
| OverrideFileNameFormat | `string` |  |  |
| Path | `string` |  |  |

#### AssetSetFinalizationResultDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| FinalizedAssetSetIdentifiers | `IList<string>` |  |  |
| ValidateAssetSetPropertiesResult | `IList<ValidateAssetSetPropertiesResultDto>` |  |  |

#### AssetSetPropertyValidationResultDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  |  |
| IsValid | `bool` |  |  |

#### AssetSetRelationsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` |  |  |
| Relations | `IList<RelationDto>` |  |  |

#### AssetSetRelationsQueryDto : RelationsSearchQueryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` | required |  |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:RelationsSearchQueryDto | List of property definition identifiers to be loaded (optional, will be ignored if provided list is NULL or empty). |
| IncludeRelationTypeIdentifiers | `IList<string>` | geerbt:RelationsSearchQueryDto | List of relation types to filter (optional, will be ignored if provided list is NULL or empty). |
| IncludeEntityTypes | `IList<string>` | geerbt:RelationsSearchQueryDto | List of entity types to filter (optional, will be ignored if provided list is NULL or empty). |
| IncludePaths | `bool?` | geerbt:RelationsSearchQueryDto | Whether the hierarchy path should be loaded (optional). |

#### AssetTasksDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| TasksByAssetSetIdentifier | `IDictionary<string, IList<TaskDto>>` |  |  |

#### AssetTypeDerivationTypesDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetTypeIdentifier | `string` | required |  |
| DerivationTypeIdentifiers | `IList<string>` | required |  |

#### AssetTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| DerivationTypeSettings | `IList<DerivationTypeSettingsDto>` |  |  |

#### AssetTypeSettingsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetTypeIdentifier | `string` |  |  |
| IsOnDemand | `bool` |  |  |
| IsDefaultDerivation | `bool` |  |  |
| AllowManualUpload | `bool` |  |  |
| ShowInDownloads | `bool` |  |  |
| UseAsThumbnail | `bool` |  |  |
| UseAsPreview | `bool` |  |  |

#### AssetUploadDto  <sub>(Webservice/Upload/Asset)</sub>

Information required to upload an asset

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ParentCategoryIdentifier | `string` | required | The category to add the asset to after the upload has been finished |
| WorkspaceIdentifier | `string` |  | An optional workspace if the asset is uploaded in context of a workspace |
| TaskIdentifier | `string` |  | An optional task to link the asset to after the upload has been finished |
| ExtractCreatedArchiveAsset | `ExtractCreatedArchiveAssetDto` |  | Optional instructions on whether to extract the asset after the upload. Only applies to supported archive formats |

#### AssetVersionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| AssetSetIdentifier | `string` |  |  |
| Created | `DateTime` |  |  |
| FileName | `string` |  |  |
| Version | `long` |  |  |
| ThumbnailUri | `string` |  |  |
| AssetType | `string` |  |  |

#### AssetVersionUploadDto  <sub>(Webservice/Upload/AssetVersion)</sub>

Information required to upload an asset version

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` | required | The asset for which to upload the version |

#### BillingContactDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `string` |  |  |
| EmailAddress | `string` |  |  |
| Status | `EntityStatusDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| UpdatedAt | `DateTime` |  |  |
| CreatedBy | `string` |  |  |
| UpdatedBy | `string` |  |  |
| RightsOwners | `IList<RightsOwnerDto>` |  |  |
| Properties | `IList<PropertyDto>` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |

#### BillingContactSearchQueryDto : SearchQueryBaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### BuyoutDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Description | `string` |  |  |
| Basis | `double` |  |  |
| Currency | `CurrencyDto` |  |  |
| AdditionalFeePercentage | `double` |  |  |
| FromDate | `DateTime` |  |  |
| ToDate | `DateTime` |  |  |
| RightsOwner | `RightsOwnerDto` |  |  |
| PriceList | `PriceListDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| CreatedBy | `string` |  |  |
| UpdatedAt | `DateTime` |  |  |
| UpdatedBy | `string` |  |  |
| RowVersion | `byte[]` |  |  |

#### CategoryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` | required |  |
| Name | `IList<TranslatedValueDto>` | required |  |
| TenantIdentifier | `string` |  | The identifier of the tenant the category belongs to |
| HasSubCategories | `bool` | required | Is set to 'true' if the category has children, regardless if they are loaded or not |
| SubCategories | `IList<CategoryDto>` |  | The child-categories within the hierarchy-tree |
| ApplicablePermissions | `IList<string>` |  | Contains the permissions the user who read the category can perform |
| PropertyDefinitionRestrictions | `ISet<PropertyDefinitionRestrictionDto>` |  | Property restrictions for this category |
| ParentCategoryIdentifier | `string` |  |  |

#### CategoryEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### CategoryPropertiesDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| CategoryIdentifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Properties | `IList<PropertyDto>` |  |  |

#### CategorySearchQueryDto : SearchQueryBaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### CategorySortOrderDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| BeforeCategoryIdentifier | `string` |  |  |
| AfterCategoryIdentifier | `string` |  |  |

#### ChunkUploadResponseDto  <sub>(Webservice/Upload)</sub>

Returned if the chunk upload request was successful

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ChunkIdentifier | `string` | required | The identifier of this chunk. Required when finishing the upload |
| UploadUrl | `string` | required | URL to be used to upload a chunk directly to the storage provider. Valid for a limited time |
| NextChunkUploadRequestUrl | `string` | required | URL to be used to request the next chunk upload to the storage provider. Valid for a limited time |
| FinishUploadRequestUrl | `string` | required | URL to be used to finish the upload of the asset. Valid for a limited time |

#### CommentDto : CreateOrUpdateCommentDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| RowVersion | `byte[]` |  |  |
| CreatedBy | `UserInfoDto` |  |  |
| CreatedAt | `string` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |
| Text | `string` | geerbt:CreateOrUpdateCommentDto |  |

#### ConfigurationValueDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Path | `string` |  |  |
| Value | `string` |  |  |

#### ConfigurationValueExtendedDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Path | `string` |  |  |
| Value | `string` |  |  |
| ConfigurationProvider | `ConfigurationProviderDto` |  |  |

#### ContextGroupIdentifierDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Type | `ContextGroupTypeDto` |  |  |

#### CopyAllPropertiesDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` |  |  |
| PropertyActionStrategy | `PropertyActionStrategyDto` |  |  |

#### CopyCategoryDto : CreateCategoryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DestinationParentCategoryIdentifier | `string` |  |  |
| SortOrder | `CategorySortOrderDto` |  |  |
| Identifier | `string` | geerbt:CreateCategoryDto |  |
| Name | `IList<TranslatedValueDto>` | geerbt:CreateCategoryDto |  |
| Properties | `IList<SetPropertyDto>` | geerbt:CreateCategoryDto | Optionally create properties on the category. |

#### CopyPropertiesDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` |  |  |
| CopyPropertiesStrategy | `IList<CopyPropertiesStrategyDto>` |  |  |

#### CopyPropertiesFromEntityDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntityIdentifier | `string` |  |  |
| EntityTypeIdentifier | `string` |  |  |
| PropertyDefinitionIdentifiers | `IList<string>` |  |  |
| CopyAllProperties | `bool` |  |  |

#### CopyPropertiesStrategyDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyActionStrategy | `PropertyActionStrategyDto` |  |  |
| PropertyDefinitionIdentifier | `string` |  |  |

#### CreateAssetDerivationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DerivationTypeIdentifier | `string` | required |  |
| FileName | `string` | required |  |

#### CreateAssetDerivationResponseDto  <sub>(Webservice)</sub> **(obsolete)**

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DerivationIdentifier | `string` | required |  |

#### CreateAssetDto  <sub>(Webservice)</sub> **(obsolete)**

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| FileName | `string` | required |  |
| ParentCategoryIdentifier | `string` | required |  |
| ExtractCreatedArchiveAsset | `ExtractCreatedArchiveAssetDto` |  |  |

#### CreateAssetResponseDto  <sub>(Webservice)</sub> **(obsolete)**

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetIdentifier | `string` | required |  |

#### CreateAssetVersionDto  <sub>(Webservice)</sub> **(obsolete)**

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| FileName | `string` | required |  |

#### CreateAssetVersionResponseDto  <sub>(Webservice)</sub> **(obsolete)**

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetVersionIdentifier | `string` | required |  |

#### CreateBillingContactDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `string` |  |  |
| EmailAddress | `string` |  |  |
| Status | `EntityStatusDto` |  |  |
| Properties | `IList<SetPropertyDto>` |  | Optionally create properties on the billing contact. |

#### CreateBuyoutDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Description | `string` |  |  |
| Basis | `double` |  |  |
| CurrencyIdentifier | `string` |  |  |
| AdditionalFeePercentage | `double` |  |  |
| FromDate | `DateTime` |  |  |
| ToDate | `DateTime` |  |  |
| RightsOwnerIdentifier | `string` |  |  |
| PriceListIdentifier | `string` |  |  |

#### CreateCategoryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Properties | `IList<SetPropertyDto>` |  | Optionally create properties on the category. |

#### CreateCustomEntityDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| EntityTypeIdentifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Properties | `IList<SetPropertyDto>` |  | Optionally create properties on the custom entity. |

#### CreateDerivationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Asset | `AssetDerivationTypeDto` | required |  |
| CategoryIdentifier | `string` | required |  |
| CropData | `CropDataDto` |  |  |

#### CreateDerivationJobDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| DerivationTypeIdentifier | `string` |  |  |
| OutputFileTypeIdentifier | `string` |  |  |
| InputFileTypeIdentifiers | `IList<string>` |  |  |
| JobDefinition | `string` |  | Contains the job definition json as string e.g. { "DerivationDataType": 4, "Steps": [] ... } |

#### CreateDerivationTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` | required |  |
| AssetTypeSettings | `IList<AssetTypeSettingsDto>` | required |  |

#### CreateEntityTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| TenantIdentifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Description | `IList<TranslatedValueDto>` |  |  |
| Status | `EntityStatusDto` |  |  |

#### CreateFileNamingDefinitionDto : UpdateFileNamingDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` | required, geerbt:UpdateFileNamingDefinitionDto |  |
| Tenants | `IList<FileNamingDefinitionTenantDto>` | geerbt:UpdateFileNamingDefinitionDto |  |
| IsGlobalDefault | `bool` | geerbt:UpdateFileNamingDefinitionDto |  |
| Format | `string` | required, geerbt:ValidateFileNamingDefinitionDto |  |
| CharacterCasing | `CharacterCasingDto` | geerbt:ValidateFileNamingDefinitionDto |  |
| ReplaceIllegalCharacters | `bool` | geerbt:ValidateFileNamingDefinitionDto |  |
| AbsentPropertyStrategy | `AbsentPropertyStrategyDto` | required, geerbt:ValidateFileNamingDefinitionDto |  |

#### CreateLicenseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Description | `IList<TranslatedValueDto>` |  |  |
| Status | `EntityStatusDto` |  |  |
| PeriodStart | `DateTime` |  |  |
| PeriodEnd | `DateTime` |  |  |
| Properties | `IList<SetPropertyDto>` |  | Optionally create properties on the license. |

#### CreateOrReplacePropertyDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyActionStrategy | `PropertyActionStrategyDto?` |  |  |
| PropertyDefinitionIdentifier | `string` |  |  |
| PropertyValues | `IList<MassEditPropertyValueDto>` |  |  |

#### CreateOrUpdateCommentDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Text | `string` |  |  |

#### CreateOrUpdateExternalGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ExternalGroupName | `string` |  |  |
| ExternalGroupDisplayName | `string` |  |  |
| UserGroupIdentifiers | `IList<string>` |  |  |

#### CreateOrUpdatePropertyListItemDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| SortKey | `int?` |  |  |
| Value | `IList<TranslatedValueDto>` |  |  |

#### CreateOrUpdateUserSettingsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DisplayLanguage | `string` |  |  |
| Locale | `string` |  |  |
| DataLanguageIdentifier | `string` |  |  |
| UiSettings | `string` |  |  |
| UserContextIdentifier | `string` |  |  |
| AvatarUrl | `string` |  |  |
| SearchTermOperator | `MultiTermOperatorDto?` |  |  |

#### CreatePriceListDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| ContextPercentages | `IList<SetContextPercentageDto>` |  |  |
| Name | `string` |  |  |
| Description | `string` |  |  |
| Status | `EntityStatusDto` |  |  |

#### CreatePropertyDefinitionDto : UpdatePropertyDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| IsList | `bool` |  |  |
| ValueType | `PropertyValueTypeDto` |  |  |
| Translatable | `bool` |  |  |
| PropertyGroupIdentifier | `string` | geerbt:UpdatePropertyDefinitionDto |  |
| AllowMultipleValues | `bool` | geerbt:UpdatePropertyDefinitionDto |  |
| AllowCustomListValue | `bool` | geerbt:UpdatePropertyDefinitionDto |  |
| Active | `bool` | geerbt:UpdatePropertyDefinitionDto |  |
| Editable | `bool` | geerbt:UpdatePropertyDefinitionDto |  |
| IgnoreCopying | `bool` | geerbt:UpdatePropertyDefinitionDto |  |
| IsSearchableFacet | `bool` | geerbt:UpdatePropertyDefinitionDto |  |
| SearchOption | `SearchOptionDto` | geerbt:UpdatePropertyDefinitionDto |  |
| Name | `IList<TranslatedValueDto>` | geerbt:UpdatePropertyDefinitionDto |  |
| Description | `IList<TranslatedValueDto>` | geerbt:UpdatePropertyDefinitionDto |  |
| ListItems | `IList<CreateOrUpdatePropertyListItemDto>` | geerbt:UpdatePropertyDefinitionDto |  |
| EntityTypeIdentifiers | `IList<string>` | geerbt:UpdatePropertyDefinitionDto |  |
| IsFileNamingUnlocked | `bool` | geerbt:UpdatePropertyDefinitionDto |  |
| HasUserContext | `bool` | geerbt:UpdatePropertyDefinitionDto |  |
| DefinitionType | `PropertyDefinitionTypeDto` | geerbt:UpdatePropertyDefinitionDto |  |
| SortKey | `int?` | geerbt:UpdatePropertyDefinitionDto |  |

#### CreatePropertyDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  |  |
| PropertyValues | `IList<PropertyValueDto>` |  |  |
| InheritToNodeTypeIdentifiers | `IList<string>` |  |  |

#### CreatePropertyExtractionDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| ExtractionType | `PropertyExtractionTypeDto` |  |  |
| InputDerivationTypeIdentifier | `string` |  |  |
| InputAssetTypeIdentifiers | `ISet<string>` |  |  |
| ExtractionSources | `IList<PropertyExtractionSourceDto>` |  |  |
| Status | `EntityStatusDto?` |  |  |

#### CreatePropertyGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Active | `bool` |  |  |
| SortKey | `int?` |  |  |

#### CreatePropertyMassEditDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` |  |  |
| Properties | `IList<CreateOrReplacePropertyDto>` |  |  |

#### CreatePropertyMassEditSearchQueryDto : AssetSearchQueryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Properties | `IList<CreateOrReplacePropertyDto>` |  |  |
| SearchInDocumentExtraction | `bool` | geerbt:AssetSearchQueryDto | Whether or not to search in the text extraction of the binary documents |
| Categories | `IList<string>` | geerbt:AssetSearchQueryDto | Filter in categories (optional). OR conjunct. If not given, no filter is applied |
| RecursivelyFilterCategories | `bool` | geerbt:AssetSearchQueryDto | Whether or not assets descendant to <see cref="Categories"/> are also subject to the filter. Example: if 'true' and the filter is 'root' none of the assets are filtered out. |
| Context | `string` | geerbt:AssetSearchQueryDto | The context (= node type of the top parent) to be searched (e.g. 'Workspace' for Workspace assets or 'Category' for AssetBrowser) |
| Filename | `string` | geerbt:AssetSearchQueryDto | Search for filename only (optional) |
| FileHash | `string` | geerbt:AssetSearchQueryDto | Search for the file hash (optional). Currently only the file hash from the original derivate is stored. |
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### CreatePropertySetDto : UpdatePropertySetDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntityTypeIdentifier | `string` |  |  |
| Name | `string` | geerbt:UpdatePropertySetDto |  |
| IsGlobal | `bool` | geerbt:UpdatePropertySetDto |  |
| IsDefault | `bool` | geerbt:UpdatePropertySetDto |  |
| PropertySetConditions | `IList<PropertySetConditionDto>` | geerbt:UpdatePropertySetDto |  |
| PropertySetPropertyDefinitions | `IList<PropertySetPropertyDefinitionDto>` | geerbt:UpdatePropertySetDto |  |

#### CreatePropertyValueRestrictionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Actions | `IList<AuthorizableActionDto>` |  |  |
| Property | `CreatePropertyDto` |  |  |

#### CreatePurchaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| StartDate | `DateTime` |  |  |
| ContextGroupIdentifier | `ContextGroupIdentifierDto` |  |  |
| LicenseIdentifier | `string` |  |  |

#### CreateRelationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ToEntityIdentifier | `string` |  |  |
| ToEntityTypeIdentifier | `string` |  |  |
| RelationTypeIdentifier | `string` |  |  |

#### CreateRelationResultDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| RowVersion | `byte[]` |  |  |
| Reason | `CreateRelationResultReasonDto` |  |  |
| FromEntityIdentifier | `string` |  |  |
| FromEntityTypeIdentifier | `string` |  |  |
| ToEntityIdentifier | `string` |  |  |
| ToEntityTypeIdentifier | `string` |  |  |
| RelationTypeIdentifier | `string` |  |  |

#### CreateRelationTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` | required |  |
| Description | `IList<TranslatedValueDto>` |  |  |
| Status | `EntityStatusDto` | required |  |
| IsDirected | `bool` |  |  |
| IsUsedForAssetSearch | `bool` |  |  |

#### CreateResourcePermissionDto : UpdateResourcePermissionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| UserGroupIdentifier | `string` | geerbt:UpdateResourcePermissionDto |  |
| UserIdentifier | `string` | geerbt:UpdateResourcePermissionDto |  |
| RoleIdentifier | `string` | required, geerbt:UpdateResourcePermissionDto |  |

#### CreateRightsOwnerDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `string` |  |  |
| Status | `EntityStatusDto` |  |  |
| BillingContactIdentifier | `string` |  |  |
| Properties | `IList<SetPropertyDto>` |  | Optionally create properties on the rights owner. |

#### CreateRoleDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` | required |  |
| Status | `EntityStatusDto` | required |  |
| ObjectTypes | `IList<ObjectTypeDto>` |  |  |
| PropertyEditEntityLimit | `int?` |  |  |
| PropertyExportEntityLimit | `int?` |  |  |

#### CreateRuleActionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Property | `CreatePropertyDto` |  | The property that should be created / updated if a rule action is executed |
| PropertyExtractionDefinitionIdentifier | `string` |  | Identifier of the property extraction definition that should be triggered when the action is executed. |
| AdditionalData | `string` |  | Additional data needed to perform special actions, like an edge type ('ParentChild', 'Link', ...) etc. |
| Template | `string` |  | An optional template that should be evaluated to perform the action. |
| OnTriggerMatched | `bool` | required | True if the action should be executed on trigger match, false if the action should be executed if the trigger doesn't match |

#### CreateRuleDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  | The public identifier of the rule. |
| Name | `IList<TranslatedValueDto>` | required | The translated name of the rule. |
| NodeTypeIdentifiers | `IList<string>` |  | The node type identifiers of the entities where the rule is to be executed. If empty list, the rule is executed on all node types. |
| Filters | `IList<CreateRuleTriggerDto>` |  | The filters an entity has to satisfy to be evaluated by the rule. |
| Triggers | `IList<CreateRuleTriggerDto>` | required | The comparisons to be evaluated by the rule. |
| Actions | `IList<CreateRuleActionDto>` | required | The actions to be performed by the rule. |

#### CreateRuleTriggerDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| TriggerOperator | `TriggerOperatorDto` | required | Trigger operator enum (equals, not equals ...) |
| Property | `CreatePropertyDto` |  | The property value that should be evaluated by this comparison. |
| AdditionalData | `string` |  | Additional data needed to evaluate special comparisons, like an edge type ('ParentChild', 'Link', ...) etc. |

#### CreateSavedSearchDto : UpdateSavedSearchDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `string` | geerbt:UpdateSavedSearchDto |  |
| SearchQuery | `string` | geerbt:UpdateSavedSearchDto | Represents a search query string as json object e.g. { "q" : "example", "languages" : "en", "exactSearch" : "true" } |

#### CreateShareDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` | required | The name of the share |
| AssetTypeDerivations | `IDictionary<string, IList<string>>` | required | Defines which derivations (=value) per asset type (=key) should be included in the share <returns>Dictionary with Key = AssetTypeIdentifier, Value = DerivationTypeIdentifier</returns> |
| ValidUntil | `DateTime?` | required | Defines how long the share should exist |
| EmailRecipients | `IList<string>` |  | The email receivers which are invited to the share |

#### CreateShareRecipientDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EmailAddress | `string` | required |  |

#### CreateTaskDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` | required |  |
| Description | `string` |  |  |
| DueDate | `DateTime?` |  |  |
| Identifier | `string` |  |  |
| TaskType | `string` |  |  |
| AssignedUserIdentifiers | `IList<string>` |  |  |
| StateIdentifier | `string` |  |  |
| Properties | `IList<SetPropertyDto>` |  | Optionally create properties on the task. |

#### CreateUserContextDto : UpdateUserContextDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` | geerbt:UpdateUserContextDto |  |
| SequenceNr | `int` | geerbt:UpdateUserContextDto |  |
| IsDefault | `bool` | geerbt:UpdateUserContextDto |  |

#### CreateUserContextGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| UserContextIdentifiers | `IList<string>` |  |  |

#### CreateUserDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| UserName | `string` |  |  |
| FirstName | `string` |  |  |
| LastName | `string` |  |  |
| EmailAddress | `string` |  |  |
| Password | `string` |  |  |
| UserSettings | `CreateOrUpdateUserSettingsDto` |  |  |
| Status | `EntityStatusDto` |  |  |
| ContactPerson | `string` |  |  |
| Company | `string` |  |  |
| PhoneNumber | `string` |  |  |
| AccessType | `AccessTypeDto` |  |  |

#### CreateUserGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` |  |  |
| Description | `string` |  |  |

#### CreateUserGroupRoleMapDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| UserGroupIdentifier | `string` |  |  |
| RoleIdentifier | `string` |  |  |

#### CreateWatermarkDefinitionDto : UpdateWatermarkDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| DisplayInUI | `bool` | geerbt:UpdateWatermarkDefinitionDto |  |
| DerivationJobDefinitionIdentifier | `string` | geerbt:UpdateWatermarkDefinitionDto |  |
| PropertyDefinitionIdentifier | `string` | geerbt:UpdateWatermarkDefinitionDto |  |

#### CreateWatermarkTriggerDto : UpdateWatermarkTriggerDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyListValueIdentifier | `string` | geerbt:UpdateWatermarkTriggerDto |  |
| ValueBool | `bool` | geerbt:UpdateWatermarkTriggerDto |  |
| FileReferenceIdentifier | `string` | geerbt:UpdateWatermarkTriggerDto |  |

#### CreateWorkspaceAssetDto : CreateAssetDto  <sub>(Webservice)</sub> **(obsolete)**

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| TaskIdentifier | `string` |  |  |
| FileName | `string` | required, geerbt:CreateAssetDto |  |
| ParentCategoryIdentifier | `string` | required, geerbt:CreateAssetDto |  |
| ExtractCreatedArchiveAsset | `ExtractCreatedArchiveAssetDto` | geerbt:CreateAssetDto |  |

#### CreateWorkspaceDto : UpdateWorkspaceDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| IsTemplate | `bool` |  |  |
| WorkspaceType | `WorkspaceTypeDto` |  |  |
| StateIdentifier | `string` |  |  |
| Properties | `IList<SetPropertyDto>` |  | Optionally create properties on the workspace. |
| Name | `IList<TranslatedValueDto>` | geerbt:UpdateWorkspaceDto |  |
| Status | `EntityStatusDto` | required, geerbt:UpdateWorkspaceDto |  |
| PropertySetIdentifier | `string` | geerbt:UpdateWorkspaceDto |  |
| FinalizationCategoryIdentifier | `string` | geerbt:UpdateWorkspaceDto |  |
| CopyPropertiesToAssets | `bool` | geerbt:UpdateWorkspaceDto |  |

#### CustomDerivationJobDefinitionDto  <sub>(MessageQueue/V1/Processing/Derivation/General)</sub>

Custom Job definition

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Steps | `IList<CustomDerivationStepDto>` |  | Steps to be executed for this custom job |

#### CustomDerivationOptionalFileDto  <sub>(MessageQueue/V1/Processing/Derivation/General)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DataId | `string` |  | StorageHandler data id |
| FileName | `string` |  | File name |

#### CustomDerivationStartMessage : JobStartMessage  <sub>(MessageQueue/V1/Processing/Derivation/General)</sub>

Message used to trigger processing a custom job

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SourceDataId | `string` |  | Source data id which is used as input |
| OriginalFileName | `string` |  | The original file name |
| OriginalFileExtension | `string` |  | The original file extension |
| OriginalFileSize | `long` |  | The original file size |
| AssetType | `string` |  | The original asset type |
| OptionalFiles | `IList<CustomDerivationOptionalFileDto>` |  | A List of optional storage handler files needed by derivation steps |
| OutputFileExtension | `string` |  | The file extension of the generated output |
| OutputMimeType | `string` |  | The mime type of the generated output |
| DerivationTypeIdentifier | `string` |  | Identifier of the derivation type |
| JobDefinitionDto | `CustomDerivationJobDefinitionDto` |  | Defines what happens during derivation |
| ProcessingId | `string` | geerbt:JobStartMessage | Used to identify status updates for the process that was started |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` | geerbt:JobStartMessage | Data the job initiator can add to later identify the context of status messages |

#### CustomDerivationStepDto  <sub>(MessageQueue/V1/Processing/Derivation/General)</sub>

Contains parameters for one step of a custom job

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Variables | `IList<CustomDerivationVariableDto>` |  | IList of variables defined for this step |
| CustomStepType | `string` |  | Type of this step |
| StepOutputFile | `string` |  | Output file name for this job (if last step, use "{jobOutputFile}") |
| StepInputFile | `string` |  | Input file name for this job (if last step, use "{jobInputFile}") |
| ArgumentString | `string` |  | Argument string for this job |
| TargetProfile | `string` |  | Argument string for this job with color profile |
| IntentString | `string` |  | This arguments will be added if the source color profile is different than the target color profile |
| UseColorProfileOverrides | `bool` |  | If set to true, the <see cref="TargetProfile"/> will be overriden by the mapping configured in the config file. If no applying mapping exists, the <see cref="TargetProfile"/> will be used |

#### CustomDerivationVariableDto  <sub>(MessageQueue/V1/Processing/Derivation/General)</sub>

Definition for a custom variable

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` |  | Name of the variable (to be used as {variable} in other variables / parameters) |
| Expression | `string` |  | Value expression of the variable |
| Evaluate | `bool` |  | If the variable is to be evaluated mathematically (contains calculations) |

#### CustomEntityDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| EntityTypeIdentifier | `string` |  |  |
| CreatedAt | `DateTime` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |
| ThumbnailUris | `IList<string>` |  |  |
| Properties | `IList<PropertyDto>` |  |  |

#### CustomEntityEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### CustomEntitySearchQueryDto : SearchQueryBaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntityTypeIdentifier | `string` |  |  |
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### DataLanguageDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Default | `bool` |  |  |
| Status | `EntityStatusDto` |  |  |

#### DeleteAssetsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` | required |  |

#### DeleteConfigurationValuesDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Paths | `IList<string>` |  |  |

#### DeletePropertiesDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifiers | `IList<string>` |  |  |

#### DeletePropertyResultDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  |  |
| IsSuccess | `bool` |  |  |
| Reason | `DeletePropertyResultReasonDto` |  |  |

#### DeleteRelationsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| RelationIdentifiers | `IList<string>` | required |  |
| RecycleBin | `bool` |  | Whether assets should be moved to the recycle bin when ParentChild relations are deleted (Optional, default false) |

#### DeleteResourcePermissionsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ResourcePermissionIdentifiers | `IList<string>` |  |  |

#### DerivationEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

Event that is created when derivation-Data is changed

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### DerivationJobDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| DerivationTypeIdentifier | `string` |  |  |
| InputFileTypeIdentifiers | `IList<string>` |  |  |
| OutputFileTypeIdentifier | `string` |  |  |
| JobDefinition | `string` |  | Contains the job definition json as string e.g. { "DerivationDataType": 4, "Steps": [] ... } |
| IsSystem | `bool` |  |  |

#### DerivationRestartDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` | required |  |
| AssetTypeDerivationTypes | `IDictionary<string, IList<string>>` |  |  |
| RestartFailedDerivations | `bool` |  |  |

#### DerivationRestartSearchQueryDto : AssetSearchQueryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetTypeDerivationTypes | `IDictionary<string, IList<string>>` |  |  |
| RestartFailedDerivations | `bool` |  |  |
| SearchInDocumentExtraction | `bool` | geerbt:AssetSearchQueryDto | Whether or not to search in the text extraction of the binary documents |
| Categories | `IList<string>` | geerbt:AssetSearchQueryDto | Filter in categories (optional). OR conjunct. If not given, no filter is applied |
| RecursivelyFilterCategories | `bool` | geerbt:AssetSearchQueryDto | Whether or not assets descendant to <see cref="Categories"/> are also subject to the filter. Example: if 'true' and the filter is 'root' none of the assets are filtered out. |
| Context | `string` | geerbt:AssetSearchQueryDto | The context (= node type of the top parent) to be searched (e.g. 'Workspace' for Workspace assets or 'Category' for AssetBrowser) |
| Filename | `string` | geerbt:AssetSearchQueryDto | Search for filename only (optional) |
| FileHash | `string` | geerbt:AssetSearchQueryDto | Search for the file hash (optional). Currently only the file hash from the original derivate is stored. |
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### DerivationStatusDetailsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` |  |  |
| AssetTypeIdentifier | `string` |  |  |
| DerivationTypeIdentifier | `string` |  |  |
| FileName | `string` |  |  |
| Status | `AssetDerivationStatusDto` |  |  |
| RetryCount | `int` |  |  |
| CreatedAt | `DateTime` |  |  |

#### DerivationStatusMessage : JobStatusMessage  <sub>(MessageQueue/V1/Processing/Derivation)</sub>

Status message for a derivation

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DataIdentifier | `string` |  | Data id from storagehandler if successful |
| FileSize | `long` |  | File size from storagehandler if successful |
| FileHash | `string` |  | File hash from storagehandler if successful |
| ProcessingIdentifier | `string` | geerbt:JobStatusMessage | The identifier of the job that was returned when the job was first started |
| JobStatus | `JobStatus` | geerbt:JobStatusMessage | Describes what status the job is in |
| Message | `string` | geerbt:JobStatusMessage | Contains output of the job |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` | geerbt:JobStatusMessage | Additional information provided by the job initiator to better identify what entities the callback relates to |
| JobMetaData | `IDictionary<string, string>` | geerbt:JobStatusMessage | Additional information created by the job, like DataIds that were created during processing etc. |

#### DerivationStatusOverviewDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PendingCount | `int` |  |  |
| FailedCount | `int` |  |  |

#### DerivationStatusTypesDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetTypeIdentifier | `string` |  |  |
| DerivationTypeIdentifier | `string` |  |  |
| Status | `AssetDerivationStatusDto` |  |  |
| Count | `int` |  |  |

#### DerivationTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| IsSystem | `bool` |  |  |
| AssetTypeSettings | `IList<AssetTypeSettingsDto>` |  |  |

#### DerivationTypeEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### DerivationTypeRestrictionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Action | `AuthorizableActionDto` |  |  |
| DerivationTypeIdentifier | `string` |  |  |
| AssetTypeIdentifier | `string` |  |  |

#### DerivationTypeSettingsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DerivationTypeIdentifier | `string` |  |  |
| DerivationName | `IList<TranslatedValueDto>` |  |  |
| IsOnDemand | `bool` |  |  |
| IsDefaultDerivation | `bool` |  |  |
| AllowManualUpload | `bool` |  |  |
| ShowInDownloads | `bool` |  |  |
| UseAsThumbnail | `bool` |  |  |
| UseAsPreview | `bool` |  |  |

#### EntityIdentifierDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| EntityTypeIdentifier | `string` |  |  |

#### EntityIdentifiersDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntityIdentifiers | `IList<EntityIdentifierDto>` |  |  |

#### EntitySortOrderDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| BeforeEntity | `EntityDto` |  |  |
| AfterEntity | `EntityDto` |  |  |

#### EntityTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Description | `IList<TranslatedValueDto>` |  |  |
| Status | `EntityStatusDto` |  |  |
| IsSystem | `bool` |  |  |

#### EntityTypeEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### EntityTypeMappingDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| FromEntityTypeIdentifier | `string` |  |  |
| ToEntityTypeIdentifier | `string` |  |  |

#### ExecuteRulesDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntityTypeIdentifiers | `IList<string>` |  | An optional list of the entity types that the rules should be executed on. If null or empty all entity types are taken into account. |
| EntityIdentifiers | `IList<string>` |  | An optional list of entity identifiers. If null all entities are taken into account. |
| RuleIdentifiers | `IList<string>` |  | A list of rule identifiers of rules to be executed. At least one rule must be defined. |

#### ExecuteTaskStatusTransitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| TransitionIdentifier | `string` |  |  |

#### ExternalGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| ExternalGroupName | `string` |  |  |
| ExternalGroupDisplayName | `string` |  |  |
| UserGroupIdentifiers | `IList<string>` |  |  |

#### ExtractCreatedArchiveAssetDto  <sub>(Webservice)</sub>

Used to configure whether an archive asset should be extracted and optionally be deleted after the extraction

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ArchiveExtractionMode | `ArchiveExtractionModeDto` | required | The extraction mode |
| DeleteAssetAfterExtraction | `bool` | required | Indicates whether the archive should be deleted after the extraction |

#### ExtractFromAssetSearchQueryDto : AssetSearchQueryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyExtractionDefinitionIdentifier | `string` |  |  |
| Import | `bool` |  |  |
| SearchInDocumentExtraction | `bool` | geerbt:AssetSearchQueryDto | Whether or not to search in the text extraction of the binary documents |
| Categories | `IList<string>` | geerbt:AssetSearchQueryDto | Filter in categories (optional). OR conjunct. If not given, no filter is applied |
| RecursivelyFilterCategories | `bool` | geerbt:AssetSearchQueryDto | Whether or not assets descendant to <see cref="Categories"/> are also subject to the filter. Example: if 'true' and the filter is 'root' none of the assets are filtered out. |
| Context | `string` | geerbt:AssetSearchQueryDto | The context (= node type of the top parent) to be searched (e.g. 'Workspace' for Workspace assets or 'Category' for AssetBrowser) |
| Filename | `string` | geerbt:AssetSearchQueryDto | Search for filename only (optional) |
| FileHash | `string` | geerbt:AssetSearchQueryDto | Search for the file hash (optional). Currently only the file hash from the original derivate is stored. |
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### FacetDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  | The property definition identifier |
| Items | `IList<FacetItemDto>` |  | The items (list items or true/false with counts) |

#### FacetFilterDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  | The property to filter for |
| Terms | `string[]` | json:q | The value (either the list item identifier or "true/false" for boolean properties). For now, only the first item is considered. In the future we will support multiple OR conjunct filter terms. |
| Operator | `FacetOperatorDto?` | json:op | The operator to apply on the filter (mandatory). |

#### FacetItemDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Term | `string` |  | The value (either the list item identifier or "true/false") |
| Count | `int` |  | The number of occurrences with respect to the search query (all filters and full text search). |

#### FieldErrorDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Field | `string` |  |  |
| Message | `string` |  |  |

#### FileNamingDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Format | `string` |  |  |
| CharacterCasing | `CharacterCasingDto` |  |  |
| ReplaceIllegalCharacters | `bool` |  |  |
| AbsentPropertyStrategy | `AbsentPropertyStrategyDto` |  |  |
| Tenants | `IList<FileNamingDefinitionTenantDto>` |  |  |
| IsGlobalDefault | `bool` |  |  |
| CreatedBy | `UserInfoDto` |  |  |

#### FileNamingDefinitionTenantDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| TenantIdentifier | `string` |  |  |
| IsDefault | `bool` |  |  |

#### FileNamingTokenDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| FileNamingTokenType | `FileNamingTokenTypeDto` |  |  |
| Identifier | `string` |  |  |
| Description | `IList<TranslatedValueDto>` |  |  |

#### FilterDto  <sub>(Webservice)</sub>

Filter <see cref="Terms"/> on property with identifier <see cref="PropertyDefinitionIdentifier"/> with <see cref="OperatorDto"/>.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  | The property to search for |
| EntityTypeIdentifier | `string` |  | The entity type the property definition belongs to (allowed are 'Category' and 'AssetSet') |
| Terms | `IList<string>` | json:q | The search filter terms (AND conjunct). For multi select properties more then one term can be specified. For <see cref="OperatorDto.Between"/> the first item is referring to the smaller and the second to the larger item (numeric or date). |
| Operator | `OperatorDto?` | json:op | The operator to apply on the search (mandatory). Possible values are from <see cref="OperatorDto"/>. |
| LanguageIdentifiers | `IList<string>` | json:languages | Text/String only: The languages to consider (optional) |

#### FinalizeAssetSetsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` |  |  |
| TargetCategoryIdentifier | `string` |  |  |

#### FinalizeUserRegistrationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Approve | `bool` |  |  |
| UserGroupIdentifiers | `IList<string>` |  |  |

#### INotificationHubClient  <sub>(SignalR)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|

#### IdentifierRowVersionDto : RowVersionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| RowVersion | `byte[]` | geerbt:RowVersionDto |  |

#### ImageDerivationJobDefinitionDto  <sub>(MessageQueue/V1/Processing/Derivation/Image)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SourceImageDefinition | `InputImageDefinitionDto` |  |  |
| SourceImageTransformation | `ImageTransformationDefinitionDto` |  |  |
| OverlayDefinitions | `IList<OverlayDefinitionDto>` |  |  |
| OutputImageDefinition | `OutputImageDefinitionDto` |  |  |

#### ImageDerivationStartMessage : JobStartMessage  <sub>(MessageQueue/V1/Processing/Derivation/Image)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SourceDataId | `string` |  | The source data, the derivation is based on |
| OriginalFileName | `string` |  | The original file name |
| OutputFileExtension | `string` |  | The file extension of the generated output |
| OutputMimeType | `string` |  | The mime type of the generated output |
| DerivationTypeIdentifier | `string` |  | Identifier of the derivation type |
| JobDefinitionDto | `ImageDerivationJobDefinitionDto` |  | Defines what happens during derivation |
| ProcessingId | `string` | geerbt:JobStartMessage | Used to identify status updates for the process that was started |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` | geerbt:JobStartMessage | Data the job initiator can add to later identify the context of status messages |

#### JobStartMessage  <sub>(MessageQueue/V1/Processing)</sub>

Message to start a processing job

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ProcessingId | `string` |  | Used to identify status updates for the process that was started |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` |  | Data the job initiator can add to later identify the context of status messages |

#### JobStatusMessage  <sub>(MessageQueue/V1/Processing)</sub>

Message that is created to pass status updates of a scheduled, running or finished job

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ProcessingIdentifier | `string` |  | The identifier of the job that was returned when the job was first started |
| JobStatus | `JobStatus` |  | Describes what status the job is in |
| Message | `string` |  | Contains output of the job |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` |  | Additional information provided by the job initiator to better identify what entities the callback relates to |
| JobMetaData | `IDictionary<string, string>` |  | Additional information created by the job, like DataIds that were created during processing etc. |

#### LicenseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Description | `IList<TranslatedValueDto>` |  |  |
| Status | `EntityStatusDto` |  |  |
| PeriodStart | `DateTime` |  |  |
| PeriodEnd | `DateTime` |  |  |
| CreatedAt | `DateTime` |  |  |
| UpdatedAt | `DateTime` |  |  |
| CreatedBy | `string` |  |  |
| UpdatedBy | `string` |  |  |
| Properties | `IList<PropertyDto>` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |
| PurchaseDurationInDays | `int` |  |  |

#### LicenseOfferDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ThumbnailUri | `string` |  |  |
| License | `LicenseDto` |  |  |
| RightsOwners | `IList<RightsOwnerDto>` |  |  |
| ContextGroup | `UserContextGroupDto` |  |  |
| AlternativeGroups | `IList<UserContextGroupDto>` |  |  |
| ValidFrom | `DateTime` |  |  |
| ValidUntil | `DateTime` |  |  |
| Prices | `IList<LicensePriceDto>` |  |  |
| IncludedAssetsCount | `int` |  |  |

#### LicenseOfferSearchQueryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SearchTerm | `string` |  | The search query string |
| ContextIdentifiers | `IList<ContextGroupIdentifierDto>` |  | User context identifiers for which the license should be valid |
| ValidFrom | `DateTime?` |  | Start date should be smaller or equal than this filter |
| ValidUntil | `DateTime?` |  | End date should be greater or equal than this filter |
| RightsIdentifiers | `IList<string>` |  | Filter by rights (list-item identifiers) |
| PurchasedForContext | `string` |  | Filter licenses that has been purchased for a specific user context |
| LicenseIdentifiers | `IList<string>` |  | Filter license offers by a specific license |
| EntityIdentifiers | `IList<EntityIdentifierDto>` |  | Filter by entities that are assigned to the license |

#### LicensePriceDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Price | `double` |  |  |
| Currency | `CurrencyDto` |  |  |

#### LicensePurchaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| OrderId | `long` |  |  |
| PurchaseDate | `DateTime` |  |  |
| StartDate | `DateTime` |  |  |
| ExpiryDate | `DateTime` |  |  |
| Prices | `IList<LicensePriceDto>` |  |  |
| ContextGroup | `UserContextGroupDto` |  |  |
| PurchasedBy | `UserInfoDto` |  |  |
| License | `LicenseDto` |  |  |
| IncludedEntities | `IList<EntityIdentifierDto>` |  |  |
| IncludedAssetsCount | `int` |  |  |
| Properties | `IList<PropertyDto>` |  |  |

#### LicensePurchaseSearchQueryDto : SearchQueryBaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| IncludedEntities | `IList<EntityIdentifierDto>` |  |  |
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### LicenseSearchQueryDto : SearchQueryBaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### MassEditPropertyValueDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyActionStrategy | `PropertyActionStrategyDto?` |  |  |
| PropertyListItemIdentifier | `string` |  |  |
| PropertyValue | `IList<MassEditTranslatedValueDto>` |  |  |
| UserContextIdentifier | `string` |  |  |

#### MassEditTranslatedValueDto : TranslatedValueDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyActionStrategy | `PropertyActionStrategyDto?` |  |  |
| DataLanguageIdentifier | `string` | geerbt:TranslatedValueDto |  |
| Value | `string` | geerbt:TranslatedValueDto |  |

#### MetadataExtractionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` |  |  |

#### MetadataExtractionProperty  <sub>(MessageQueue/V1/Processing/MetadataExtraction)</sub>

Single metadata information tag

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` |  | Name, usually the property definition identifier where the information is written to |
| Value | `string` |  | Value of the tag |
| Tag | `string` |  | Tag |

#### MetadataExtractionRestartMessage : MetadataExtractionStartMessage  <sub>(MessageQueue/V1/Processing/MetadataExtraction)</sub>

Message to re-start metadata extraction

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SourceDataId | `string` | geerbt:MetadataExtractionStartMessage | The source data, the derivation is based on |
| OriginalFileName | `string` | geerbt:MetadataExtractionStartMessage | The original file name |
| OriginalFileExtension | `string` | geerbt:MetadataExtractionStartMessage | The original file extension |
| OriginalFileSize | `long` | geerbt:MetadataExtractionStartMessage | The original file size |
| OutputFileExtension | `string` | geerbt:MetadataExtractionStartMessage | The file extension of the generated output |
| OutputMimeType | `string` | geerbt:MetadataExtractionStartMessage | The mime type of the generated output |
| AssetSetIdentifier | `string` | geerbt:MetadataExtractionStartMessage | Defines the asset set identifier for which to extract the metadata |
| AssetVersionIdentifier | `string` | geerbt:MetadataExtractionStartMessage | Defines the asset set version identifier for which to extract the metadata |
| AssetType | `string` | geerbt:MetadataExtractionStartMessage | Defines the asset type for which to extract the metadata |
| ProcessingId | `string` | geerbt:JobStartMessage | Used to identify status updates for the process that was started |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` | geerbt:JobStartMessage | Data the job initiator can add to later identify the context of status messages |

#### MetadataExtractionStartMessage : JobStartMessage  <sub>(MessageQueue/V1/Processing/MetadataExtraction)</sub>

Message to start metadata extraction

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SourceDataId | `string` |  | The source data, the derivation is based on |
| OriginalFileName | `string` |  | The original file name |
| OriginalFileExtension | `string` |  | The original file extension |
| OriginalFileSize | `long` |  | The original file size |
| OutputFileExtension | `string` |  | The file extension of the generated output |
| OutputMimeType | `string` |  | The mime type of the generated output |
| AssetSetIdentifier | `string` |  | Defines the asset set identifier for which to extract the metadata |
| AssetVersionIdentifier | `string` |  | Defines the asset set version identifier for which to extract the metadata |
| AssetType | `string` |  | Defines the asset type for which to extract the metadata |
| ProcessingId | `string` | geerbt:JobStartMessage | Used to identify status updates for the process that was started |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` | geerbt:JobStartMessage | Data the job initiator can add to later identify the context of status messages |

#### MetadataExtractionStatusMessage : JobStatusMessage  <sub>(MessageQueue/V1/Processing/MetadataExtraction)</sub>

Message containing information about the status of the metadata extraction

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Properties | `IList<MetadataExtractionProperty>` |  | Extracted metadata if processing ended successfully |
| AssetSetIdentifier | `string` |  | Asset set identifier for which the metadata was extracted |
| AssetVersionIdentifier | `string` |  | Asset version identifier for which the metadata was extracted |
| ProcessingIdentifier | `string` | geerbt:JobStatusMessage | The identifier of the job that was returned when the job was first started |
| JobStatus | `JobStatus` | geerbt:JobStatusMessage | Describes what status the job is in |
| Message | `string` | geerbt:JobStatusMessage | Contains output of the job |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` | geerbt:JobStatusMessage | Additional information provided by the job initiator to better identify what entities the callback relates to |
| JobMetaData | `IDictionary<string, string>` | geerbt:JobStatusMessage | Additional information created by the job, like DataIds that were created during processing etc. |

#### MetadataExtractionV6Dto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` |  |  |
| PropertyExtractionDefinitionIdentifier | `string` |  |  |
| Import | `bool` |  |  |

#### MoveAssetsToCategoryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifiers | `IList<string>` |  |  |
| FromParentCategoryIdentifier | `string` |  |  |

#### MoveCategoryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SourceParentCategoryIdentifier | `string` |  |  |
| DestinationParentCategoryIdentifier | `string` |  |  |
| SortOrder | `CategorySortOrderDto` |  |  |

#### ObjectTypePermissionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` | required |  |
| Permission | `PermissionDto` |  |  |
| Description | `string` |  |  |

#### OfficeDerivationJobDefinitionDto  <sub>(MessageQueue/V1/Processing/Derivation/Office)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DerivatorName | `string` |  |  |

#### OfficeDerivationStartMessage : JobStartMessage  <sub>(MessageQueue/V1/Processing/Derivation/Office)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SourceDataId | `string` |  | The source data, the derivation is based on |
| OriginalFileName | `string` |  | The original file name |
| OutputFileExtension | `string` |  | The file extension of the generated output |
| DerivationTypeIdentifier | `string` |  | Identifier of the derivation type |
| JobDefinitionDto | `OfficeDerivationJobDefinitionDto` |  | Defines what happens during derivation |
| ProcessingId | `string` | geerbt:JobStartMessage | Used to identify status updates for the process that was started |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` | geerbt:JobStartMessage | Data the job initiator can add to later identify the context of status messages |

#### PermissionMaterializationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| InitiatedBy | `UserInfoDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| FinishedAt | `DateTime?` |  |  |
| Status | `JobStatusDto` |  |  |
| Operations | `IList<PermissionMaterializationOperationDto>` |  |  |

#### PermissionMaterializationOperationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| JobType | `BackgroundJobTriggerTypeDto` |  |  |
| AffectedEntityIdentifier | `EntityIdentifierDto` |  |  |

#### PriceListDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `string` |  |  |
| Description | `string` |  |  |
| ContextPercentages | `IList<ContextPercentageDto>` |  |  |
| Status | `EntityStatusDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| UpdatedAt | `DateTime` |  |  |
| CreatedBy | `string` |  |  |
| UpdatedBy | `string` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |

#### PrintDerivationJobDefinitionDto  <sub>(MessageQueue/V1/Processing/Derivation/Print)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DerivatorName | `string` |  |  |

#### PrintDerivationStartMessage : JobStartMessage  <sub>(MessageQueue/V1/Processing/Derivation/Print)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SourceDataId | `string` |  | The source data, the derivation is based on |
| OriginalFileName | `string` |  | The original file name |
| OutputFileExtension | `string` |  | The file extension of the generated output |
| DerivationTypeIdentifier | `string` |  | Identifier of the derivation type |
| JobDefinitionDto | `PrintDerivationJobDefinitionDto` |  | Defines what happens during derivation |
| ProcessingId | `string` | geerbt:JobStartMessage | Used to identify status updates for the process that was started |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` | geerbt:JobStartMessage | Data the job initiator can add to later identify the context of status messages |

#### PropertyDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Description | `IList<TranslatedValueDto>` |  |  |
| PropertyGroupIdentifier | `string` |  |  |
| ValueType | `PropertyValueTypeDto` |  |  |
| AllowMultipleValues | `bool` |  |  |
| AllowCustomListValue | `bool` |  |  |
| Active | `bool` |  |  |
| Editable | `bool` |  |  |
| Translatable | `bool` |  |  |
| IgnoreCopying | `bool` |  |  |
| IsSystem | `bool` |  |  |
| IsSearchableFacet | `bool` |  |  |
| SearchOption | `SearchOptionDto` |  |  |
| ListItems | `IList<PropertyListItemDto>` |  |  |
| EntityTypeIdentifiers | `IList<string>` |  |  |
| IsFileNamingUnlocked | `bool` |  |  |
| HasUserContext | `bool` |  |  |
| IsUrl | `bool` | obsolete |  |
| DefinitionType | `PropertyDefinitionTypeDto` |  |  |
| SortKey | `int?` |  |  |
| IsList | `bool` |  |  |
| BlacklistedFields | `IList<PropertyDefinitionFieldsDto>` |  |  |

#### PropertyDefinitionEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### PropertyDefinitionRestrictionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Action | `AuthorizableActionDto` |  |  |
| PropertyDefinitionIdentifier | `string` |  |  |

#### PropertyDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| PropertyDefinitionIdentifier | `string` |  |  |
| PropertyValues | `IList<PropertyValueDto>` |  |  |
| InheritToNodeTypeIdentifiers | `IList<string>` |  |  |
| RowVersion | `byte[]` |  |  |

#### PropertyEntityCreationResultDto : IdentifierRowVersionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyResults | `IList<SetPropertyResultDto>` |  |  |
| Identifier | `string` | geerbt:IdentifierRowVersionDto |  |
| RowVersion | `byte[]` | geerbt:RowVersionDto |  |

#### PropertyEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### PropertyExportDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyExportIdentifiers | `IList<PropertyExportIdentifierDto>` | required |  |
| Options | `PropertyExportOptionsDto` | required |  |
| PropertyDefinitionIdentifiers | `IList<string>` | required |  |

#### PropertyExportIdentifierDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntityIdentifier | `string` | required |  |
| EntityVersion | `string` |  |  |

#### PropertyExportOptionsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| FileFormat | `PropertyExportFileFormatDto` | required |  |
| LabelDataLanguageIdentifier | `string` | required |  |
| LocaleIdentifier | `string` |  |  |
| PropertyDataLanguageIdentifiers | `IList<string>` | required |  |
| PropertyUserContextIdentifiers | `IList<string>` |  |  |

#### PropertyExtractionDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| RowVersion | `byte[]` |  |  |
| IsSystem | `bool` |  |  |
| ExtractionType | `PropertyExtractionTypeDto` |  |  |
| InputDerivationTypeIdentifier | `string` |  |  |
| InputAssetTypeIdentifiers | `ISet<string>` |  |  |
| ExtractionSources | `IList<PropertyExtractionSourceDto>` |  |  |
| CreatedAt | `DateTime` |  |  |
| Status | `EntityStatusDto` |  |  |

#### PropertyExtractionJobAssetResultDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetDerivationIdentifier | `string` |  |  |
| AssetSetIdentifier | `string` |  |  |
| AssetVersionIdentifier | `string` |  |  |
| AssetVersionNumber | `long` |  |  |
| FileName | `string` |  |  |
| Status | `PropertyExtractionJobAssetResultStatusDto` |  |  |
| Extracted | `IList<PropertyExtractionSourceResultDto>` |  |  |

#### PropertyExtractionJobDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| RowVersion | `byte[]` |  |  |
| PropertyExtractionDefinitionIdentifier | `string` |  |  |
| ImportAfterExtraction | `bool` |  |  |
| Status | `JobStatusDto` |  |  |
| CreatedBy | `UserInfoDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| InitiatedAt | `DateTime?` |  |  |
| FinishedAt | `DateTime?` |  |  |
| TotalAssetCount | `int` |  | The total number of assets. |
| FailedAssetCount | `int` |  | The number of assets which either failed to extract or import the properties. |
| ExtractedAssetCount | `int` |  | The number of assets where properties were extracted. |
| ImportedAssetCount | `int` |  | The number of assets where properties were extracted and imported. |
| ApplicablePermissions | `IList<string>` |  |  |

#### PropertyExtractionMappingDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| FromValue | `string` |  |  |
| ToPropertyDefinitionIdentifier | `string` |  |  |
| ToPropertyListValueIdentifier | `string` |  |  |

#### PropertyExtractionResultDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ExtractedValue | `string` |  |  |
| Confidence | `double` |  |  |
| ToPropertyDefinitionIdentifier | `string` |  |  |
| ToPropertyListValueIdentifier | `string` |  |  |
| Status | `PropertyExtractionResultStatusDto` |  |  |

#### PropertyExtractionSourceDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Provider | `PropertyExtractionProviderDto` |  |  |
| CustomModelId | `string` |  |  |
| Language | `string` |  |  |
| ConfidenceThreshold | `double?` |  |  |
| Mappings | `IList<PropertyExtractionMappingDto>` |  |  |

#### PropertyExtractionSourceResultDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyExtractionProvider | `PropertyExtractionProviderDto` |  |  |
| ConfidenceThreshold | `double?` |  |  |
| Status | `PropertyExtractionSourceStatusDto` |  |  |
| StatusMessage | `string` |  |  |
| Properties | `IList<PropertyExtractionResultDto>` |  |  |

#### PropertyGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Active | `bool` |  |  |
| SortKey | `int` |  |  |
| PropertyDefinitionsCount | `int` |  |  |

#### PropertyImportDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyImportIdentifier | `string` |  |  |
| FileName | `string` |  |  |
| DataId | `string` |  |  |
| Status | `JobStatusDto` |  |  |
| CreatedBy | `string` |  |  |
| CreatedAt | `DateTime` |  |  |
| InitiatedAt | `DateTime?` |  |  |
| FinishedAt | `DateTime?` |  |  |
| MimeType | `string` |  |  |
| FileUrl | `string` |  |  |
| TotalPropertyMutationCount | `int` |  |  |
| FailedPropertyMutationCount | `int` |  |  |
| SkippedPropertyMutationCount | `int` |  |  |
| SucceededPropertyMutationCount | `int` |  |  |
| Messages | `IList<PropertyImportMessageDto>` |  |  |
| ImportEntities | `IList<PropertyImportEntityDto>` |  |  |

#### PropertyImportEntityDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntityIdentifier | `string` |  |  |
| EntityTypeIdentifier | `string` |  |  |
| VersionIdentifier | `string` |  |  |
| VersionFileName | `string` |  |  |
| VersionNumber | `long` |  |  |
| PropertyMutations | `IList<PropertyImportMutationDto>` |  |  |

#### PropertyImportEvent : DataEventBase  <sub>(MessageQueue/V2/EventStream)</sub>

Event that is sent when a property import job is changed

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### PropertyImportMessageDto  <sub>(Webservice)</sub>

PropertyImport source file validation / parsing error messages

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| MessageType | `PropertyImportMessageTypeDto` |  |  |
| Severity | `PropertyImportMessageSeverityDto` |  |  |
| Result | `string` |  |  |
| RowNumber | `long?` |  |  |
| ColumnNumber | `long?` |  |  |

#### PropertyImportMutationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  |  |
| Result | `string` |  |  |
| Success | `bool` |  |  |

#### PropertyListItemDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| SortKey | `int?` |  |  |
| Value | `IList<TranslatedValueDto>` |  |  |
| Status | `EntityStatusDto` |  |  |
| IsSystemListValue | `bool` |  |  |

#### PropertyMassEditDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Status | `JobStatusDto` |  |  |
| AssetSetIdentifiers | `IList<string>` |  |  |
| Properties | `IList<CreateOrReplacePropertyDto>` |  |  |
| CreatedAt | `DateTime` |  |  |
| InitiatedAt | `DateTime?` |  |  |
| FinishedAt | `DateTime?` |  |  |
| TotalPropertyMutationCount | `int` |  |  |
| FailedPropertyMutationCount | `int` |  |  |
| SucceededPropertyMutationCount | `int` |  |  |
| TotalAssetCount | `long` |  |  |
| ApplicableAssetCountLimit | `int?` |  |  |

#### PropertyMassEditEvent : DataEventBase  <sub>(MessageQueue/V2/EventStream)</sub>

Event that is sent when a property mass edit job is changed

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### PropertyMassEditReportDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyMassEditIdentifier | `string` |  |  |
| Status | `JobStatusDto` |  |  |
| AssetPropertyMutations | `IList<AssetPropertyMutationDto>` |  |  |
| TotalPropertyMutationCount | `int` |  |  |
| FailedPropertyMutationCount | `int` |  |  |
| SucceededPropertyMutationCount | `int` |  |  |
| CreatedAt | `DateTime` |  |  |
| InitiatedAt | `DateTime?` |  |  |
| FinishedAt | `DateTime?` |  |  |
| TotalAssetCount | `long` |  |  |
| ApplicableAssetCountLimit | `int?` |  |  |

#### PropertyMassEditReportV4Dto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Status | `JobStatusDto` |  |  |
| Properties | `IList<CreateOrReplacePropertyDto>` |  |  |
| CreatedAt | `DateTime` |  |  |
| InitiatedAt | `DateTime?` |  |  |
| FinishedAt | `DateTime?` |  |  |
| TotalPropertyMutationCount | `int` |  |  |
| FailedPropertyMutationCount | `int` |  |  |
| SucceededPropertyMutationCount | `int` |  |  |
| TotalAssetCount | `long` |  |  |
| ApplicableAssetCountLimit | `int?` |  |  |

#### PropertyMassEditV4Dto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Status | `JobStatusDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| InitiatedAt | `DateTime?` |  |  |
| FinishedAt | `DateTime?` |  |  |
| TotalPropertyMutationCount | `int` |  |  |
| FailedPropertyMutationCount | `int` |  |  |
| SucceededPropertyMutationCount | `int` |  |  |
| TotalAssetCount | `long` |  |  |
| ApplicableAssetCountLimit | `int?` |  |  |

#### PropertyMutationResultDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  |  |
| PropertyIdentifier | `string` |  |  |
| Result | `string` |  |  |
| Success | `bool` |  |  |
| ValueMutations | `IList<PropertyValueMutationDto>` |  |  |

#### PropertySetConditionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  |  |
| PropertyListValueIdentifier | `string` |  |  |

#### PropertySetDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `string` |  |  |
| CreatedBy | `UserInfoDto` |  |  |
| IsGlobal | `bool` |  |  |
| IsDefault | `bool` |  |  |
| EntityTypeIdentifier | `string` |  |  |
| PropertySetPropertyDefinitions | `IList<PropertySetPropertyDefinitionDto>` |  |  |
| PropertySetConditions | `IList<PropertySetConditionDto>` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |

#### PropertySetPropertyDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  |  |
| FillMode | `FillModeDto` |  |  |

#### PropertyValueDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| PropertyListItemIdentifier | `string` |  |  |
| PropertyValue | `IList<TranslatedValueDto>` |  |  |
| UserContextIdentifier | `string` |  |  |

#### PropertyValueMutationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| UserContextIdentifier | `string` |  |  |
| LanguageIdentifier | `string` |  |  |
| Result | `string` |  |  |
| Success | `bool` |  |  |

#### PropertyValueRestrictionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Property | `PropertyDto` |  |  |
| Actions | `IList<AuthorizableActionDto>` |  |  |

#### PublicShareDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` |  |  |
| Derivations | `IList<ShareDerivationDto>` |  |  |

#### QuickSearchResultDto<T>  <sub>(Webservice)</sub>

For quick search retrieval of any entity such as Assets, Categories, etc.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<T>` |  | The found items of entity <see cref="T"/> |
| AutoCompleteSuggestions | `IList<string>` |  | The result of the auto-complete. The highlighting is indicated with "{}" E.g. {Swi}tzer{land} - whereas the term in the bracket is found and the other text is suggested. |

#### QuickSearchResultOfAssetDto : QuickSearchResultDto<AssetDto>  <sub>(Webservice)</sub>

For quick search retrieval of Asset entities.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<AssetDto>` | geerbt:QuickSearchResultDto | The found items of entity <see cref="T"/> |
| AutoCompleteSuggestions | `IList<string>` | geerbt:QuickSearchResultDto | The result of the auto-complete. The highlighting is indicated with "{}" E.g. {Swi}tzer{land} - whereas the term in the bracket is found and the other text is suggested. |

#### QuickSearchResultOfBillingContactDto : QuickSearchResultDto<BillingContactDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<BillingContactDto>` | geerbt:QuickSearchResultDto | The found items of entity <see cref="T"/> |
| AutoCompleteSuggestions | `IList<string>` | geerbt:QuickSearchResultDto | The result of the auto-complete. The highlighting is indicated with "{}" E.g. {Swi}tzer{land} - whereas the term in the bracket is found and the other text is suggested. |

#### QuickSearchResultOfCategoryDto : QuickSearchResultDto<CategoryDto>  <sub>(Webservice)</sub>

For quick search retrieval of Category entities.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<CategoryDto>` | geerbt:QuickSearchResultDto | The found items of entity <see cref="T"/> |
| AutoCompleteSuggestions | `IList<string>` | geerbt:QuickSearchResultDto | The result of the auto-complete. The highlighting is indicated with "{}" E.g. {Swi}tzer{land} - whereas the term in the bracket is found and the other text is suggested. |

#### QuickSearchResultOfCustomEntityDto : QuickSearchResultDto<CustomEntityDto>  <sub>(Webservice)</sub>

For quick search retrieval of Custom entities.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<CustomEntityDto>` | geerbt:QuickSearchResultDto | The found items of entity <see cref="T"/> |
| AutoCompleteSuggestions | `IList<string>` | geerbt:QuickSearchResultDto | The result of the auto-complete. The highlighting is indicated with "{}" E.g. {Swi}tzer{land} - whereas the term in the bracket is found and the other text is suggested. |

#### QuickSearchResultOfLicenseDto : QuickSearchResultDto<LicenseDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<LicenseDto>` | geerbt:QuickSearchResultDto | The found items of entity <see cref="T"/> |
| AutoCompleteSuggestions | `IList<string>` | geerbt:QuickSearchResultDto | The result of the auto-complete. The highlighting is indicated with "{}" E.g. {Swi}tzer{land} - whereas the term in the bracket is found and the other text is suggested. |

#### QuickSearchResultOfLicensePurchaseDto : QuickSearchResultDto<LicensePurchaseDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<LicensePurchaseDto>` | geerbt:QuickSearchResultDto | The found items of entity <see cref="T"/> |
| AutoCompleteSuggestions | `IList<string>` | geerbt:QuickSearchResultDto | The result of the auto-complete. The highlighting is indicated with "{}" E.g. {Swi}tzer{land} - whereas the term in the bracket is found and the other text is suggested. |

#### QuickSearchResultOfRightsOwnerDto : QuickSearchResultDto<RightsOwnerDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<RightsOwnerDto>` | geerbt:QuickSearchResultDto | The found items of entity <see cref="T"/> |
| AutoCompleteSuggestions | `IList<string>` | geerbt:QuickSearchResultDto | The result of the auto-complete. The highlighting is indicated with "{}" E.g. {Swi}tzer{land} - whereas the term in the bracket is found and the other text is suggested. |

#### QuickSearchResultOfTaskDto : QuickSearchResultDto<TaskDto>  <sub>(Webservice)</sub>

For quick search retrieval of Task entities.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<TaskDto>` | geerbt:QuickSearchResultDto | The found items of entity <see cref="T"/> |
| AutoCompleteSuggestions | `IList<string>` | geerbt:QuickSearchResultDto | The result of the auto-complete. The highlighting is indicated with "{}" E.g. {Swi}tzer{land} - whereas the term in the bracket is found and the other text is suggested. |

#### QuickSearchResultOfWorkspaceDto : QuickSearchResultDto<WorkspaceDto>  <sub>(Webservice)</sub>

For quick search retrieval of Workspace entities.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<WorkspaceDto>` | geerbt:QuickSearchResultDto | The found items of entity <see cref="T"/> |
| AutoCompleteSuggestions | `IList<string>` | geerbt:QuickSearchResultDto | The result of the auto-complete. The highlighting is indicated with "{}" E.g. {Swi}tzer{land} - whereas the term in the bracket is found and the other text is suggested. |

#### ReIndexEntityTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntityTypeIdentifiers | `IList<string>` |  | What entity types should be re-index (e.g. 'AssetSet', 'Workspace', ...) |

#### ReIndexingJobDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| InitiatedBy | `string` |  |  |
| InitiatedAt | `DateTime` |  |  |
| FinishedAt | `DateTime?` |  |  |
| EntityTypes | `IList<string>` |  |  |
| Status | `JobStatusDto` |  |  |
| FailureReason | `string` |  |  |
| TotalEntitiesCount | `int` |  |  |
| SuccessfulEntitiesCount | `int` |  |  |
| FailedEntitiesCount | `int` |  |  |
| IsCancelled | `bool` |  |  |

#### ReIndexingJobReportDto : ReIndexingJobDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| FailedEntities | `IList<ReportEntityDto>` |  |  |
| Identifier | `string` | geerbt:ReIndexingJobDto |  |
| InitiatedBy | `string` | geerbt:ReIndexingJobDto |  |
| InitiatedAt | `DateTime` | geerbt:ReIndexingJobDto |  |
| FinishedAt | `DateTime?` | geerbt:ReIndexingJobDto |  |
| EntityTypes | `IList<string>` | geerbt:ReIndexingJobDto |  |
| Status | `JobStatusDto` | geerbt:ReIndexingJobDto |  |
| FailureReason | `string` | geerbt:ReIndexingJobDto |  |
| TotalEntitiesCount | `int` | geerbt:ReIndexingJobDto |  |
| SuccessfulEntitiesCount | `int` | geerbt:ReIndexingJobDto |  |
| FailedEntitiesCount | `int` | geerbt:ReIndexingJobDto |  |
| IsCancelled | `bool` | geerbt:ReIndexingJobDto |  |

#### RelationDto  <sub>(Webservice)</sub>

Represents a directed Relationship between two Nodes

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| FromEntity | `EntityExtendedDto` |  |  |
| ToEntity | `EntityExtendedDto` |  |  |
| RelationTypeIdentifier | `string` |  |  |
| PathSegments | `IList<PathSegmentDto>` |  |  |
| CanDelete | `bool` |  |  |

#### RelationEvent : DataEventBase  <sub>(MessageQueue/V2/EventStream)</sub>

Event that is sent when relation data is changed

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### RelationTypeDto : CreateRelationTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| IsSystem | `bool` |  |  |
| AllowedEntityRelations | `IList<EntityTypeMappingDto>` |  |  |
| Identifier | `string` | geerbt:CreateRelationTypeDto |  |
| Name | `IList<TranslatedValueDto>` | required, geerbt:CreateRelationTypeDto |  |
| Description | `IList<TranslatedValueDto>` | geerbt:CreateRelationTypeDto |  |
| Status | `EntityStatusDto` | required, geerbt:CreateRelationTypeDto |  |
| IsDirected | `bool` | geerbt:CreateRelationTypeDto |  |
| IsUsedForAssetSearch | `bool` | geerbt:CreateRelationTypeDto |  |

#### RelationsSearchQueryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| IncludePropertyDefinitionIdentifiers | `IList<string>` |  | List of property definition identifiers to be loaded (optional, will be ignored if provided list is NULL or empty). |
| IncludeRelationTypeIdentifiers | `IList<string>` |  | List of relation types to filter (optional, will be ignored if provided list is NULL or empty). |
| IncludeEntityTypes | `IList<string>` |  | List of entity types to filter (optional, will be ignored if provided list is NULL or empty). |
| IncludePaths | `bool?` |  | Whether the hierarchy path should be loaded (optional). |

#### ReportEntityDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `EntityIdentifierDto` |  |  |
| DisplayName | `string` |  |  |
| BatchNumber | `int` |  |  |

#### ResourcePermissionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` | required |  |
| UserGroup | `UserGroupDto` |  |  |
| User | `UserInfoDto` |  |  |
| RoleIdentifier | `string` | required |  |
| IsInherited | `bool` |  |  |

#### ResourcePermissionSettingsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| InterruptInheritance | `bool` |  |  |

#### RightsOwnerDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `string` |  |  |
| Status | `EntityStatusDto` |  |  |
| AvatarUrl | `string` |  |  |
| BillingContact | `BillingContactDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| UpdatedAt | `DateTime` |  |  |
| CreatedBy | `string` |  |  |
| UpdatedBy | `string` |  |  |
| Properties | `IList<PropertyDto>` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |

#### RightsOwnerSearchQueryDto : SearchQueryBaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### RoleDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` | required |  |
| Status | `EntityStatusDto` | required |  |
| ObjectTypes | `IList<ObjectTypeDto>` |  |  |
| IsSystem | `bool` |  |  |
| PropertyEditEntityLimit | `int?` |  |  |
| PropertyExportEntityLimit | `int?` |  |  |
| RowVersion | `byte[]` |  |  |

#### RoleEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### RoleSummaryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` | required |  |
| Status | `EntityStatusDto` | required |  |
| ObjectTypes | `IList<ObjectTypeDto>` |  |  |
| Permissions | `IList<PermissionDto>` |  |  |
| DerivationTypeRestrictions | `IList<DerivationTypeRestrictionDto>` |  |  |
| PropertyDefinitionRestrictions | `IList<PropertyDefinitionRestrictionDto>` |  |  |
| PropertyValueRestrictions | `IList<PropertyValueRestrictionDto>` |  |  |
| IsSystem | `bool` |  |  |
| PropertyEditEntityLimit | `int?` |  |  |
| PropertyExportEntityLimit | `int?` |  |  |
| RowVersion | `byte[]` |  |  |

#### RowVersionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| RowVersion | `byte[]` |  |  |

#### RuleActionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Property | `PropertyDto` |  | The property that should be set as result of the action. |
| PropertyExtractionDefinitionIdentifier | `string` |  | Identifier of the property extraction definition that should be triggered when the action is executed. |
| AdditionalData | `string` |  | Additional data needed to perform special actions, like an edge type ('ParentChild', 'Link', ...) etc. |
| Template | `string` |  | An optional template that should be evaluated to perform the action. |
| OnTriggerMatched | `bool` | required | Indicator wether the action should be executed on success or on fail. |

#### RuleDto : RowVersionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  | The public identifier of the rule. |
| Name | `IList<TranslatedValueDto>` |  | The translated name of the rule. |
| NodeTypeIdentifiers | `IList<string>` |  | The node type identifiers of the entities where the rule is executed on. If empty list, the rule is executed on all node types. |
| Filters | `IList<RuleTriggerDto>` |  | The filters an entity has to satisfy to be evaluated by the rule. |
| Triggers | `IList<RuleTriggerDto>` |  | The triggers to be evaluated by the rule. |
| Actions | `IList<RuleActionDto>` |  | The actions to be performed by the rule. |
| RowVersion | `byte[]` | geerbt:RowVersionDto |  |

#### RuleExecutionDetailReportDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntityIdentifier | `EntityIdentifierDto` |  |  |
| EntityName | `string` |  |  |
| IsMatchedTrigger | `bool` |  |  |
| IsMatchedActionExecuted | `bool` |  |  |
| IsNotMatchedActionExecuted | `bool` |  |  |
| IsError | `bool` |  |  |
| ErrorReason | `string` |  |  |
| MatchedValuesExecuted | `IDictionary<string, string>` |  |  |
| NotMatchedValuesExecuted | `IDictionary<string, string>` |  |  |

#### RuleExecutionReportDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| JobIdentifier | `string` |  |  |
| Rule | `RuleDto` |  |  |
| Status | `JobStatusDto` |  |  |
| CreatedAt | `DateTime` |  |  |
| InitiatedBy | `string` |  |  |
| InitiatedAt | `DateTime?` |  |  |
| FinishedAt | `DateTime?` |  |  |
| TotalCount | `int?` |  | Number of entities that passed the filters defined on the rule |
| CurrentCount | `int?` |  | Current number of executed entities |
| MatchedTriggerCount | `int?` |  | Number of entities where the triggers evaluated to true |
| NotMatchedTriggerCount | `int?` |  | Number of entities where the triggers evaluated to false |
| MatchedActionExecutedCount | `int?` |  | Number of entities for which the "on matched" action has been executed |
| NotMatchedActionExecutedCount | `int?` |  | Number of entities for which the "on not matched" action has been executed |
| ActionSkippedCount | `int?` |  | Number of entities for which no action has been executed |
| ErrorCount | `int?` |  | Number of entities for which an error occurred during the execution of an action (e.g. wrong formatting in the action template) |

#### RuleTriggerDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| TriggerOperator | `TriggerOperatorDto` | required | Trigger operator enum (equals, not equals ...) |
| Property | `PropertyDto` |  | The property value that should be evaluated by this trigger. |
| AdditionalData | `string` |  | Additional data needed to evaluate special triggers, like an edge type ('ParentChild', 'Link', ...) etc. |

#### SavedSearchDto : CreateSavedSearchDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ApplicablePermissions | `IList<string>` |  |  |
| Identifier | `string` | geerbt:CreateSavedSearchDto |  |
| Name | `string` | geerbt:UpdateSavedSearchDto |  |
| SearchQuery | `string` | geerbt:UpdateSavedSearchDto | Represents a search query string as json object e.g. { "q" : "example", "languages" : "en", "exactSearch" : "true" } |

#### SearchQueryBaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SearchTerm | `string` | json:q | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` |  | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` |  | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` |  | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` |  | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` |  | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` |  | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` |  | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### SearchResultDto<T>  <sub>(Webservice)</sub>

For generic search retrieval of any entity such as Assets, Categories, etc.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<T>` |  | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` |  | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfAssetDto : SearchResultDto<AssetDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<AssetDto>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfAssetV6Dto : SearchResultV6Dto<AssetDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Cursor | `string` | geerbt:SearchResultV6Dto | Byte encoded json which includes information about the current page and improves performance if we navigate through more than 10'000 results |
| Items | `IList<T>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfBillingContactDto : SearchResultDto<BillingContactDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<BillingContactDto>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfBillingContactV6Dto : SearchResultV6Dto<BillingContactDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Cursor | `string` | geerbt:SearchResultV6Dto | Byte encoded json which includes information about the current page and improves performance if we navigate through more than 10'000 results |
| Items | `IList<T>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfCustomEntityDto : SearchResultDto<CustomEntityDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<CustomEntityDto>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfCustomEntityV6Dto : SearchResultV6Dto<CustomEntityDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Cursor | `string` | geerbt:SearchResultV6Dto | Byte encoded json which includes information about the current page and improves performance if we navigate through more than 10'000 results |
| Items | `IList<T>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfLicenseDto : SearchResultDto<LicenseDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<LicenseDto>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfLicenseV6Dto : SearchResultV6Dto<LicenseDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Cursor | `string` | geerbt:SearchResultV6Dto | Byte encoded json which includes information about the current page and improves performance if we navigate through more than 10'000 results |
| Items | `IList<T>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfPurchaseDto : SearchResultV6Dto<LicensePurchaseDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Cursor | `string` | geerbt:SearchResultV6Dto | Byte encoded json which includes information about the current page and improves performance if we navigate through more than 10'000 results |
| Items | `IList<T>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfRightsOwnerDto : SearchResultDto<RightsOwnerDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<RightsOwnerDto>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfRightsOwnerV6Dto : SearchResultV6Dto<RightsOwnerDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Cursor | `string` | geerbt:SearchResultV6Dto | Byte encoded json which includes information about the current page and improves performance if we navigate through more than 10'000 results |
| Items | `IList<T>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfTaskDto : SearchResultDto<TaskDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<TaskDto>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfTaskV6Dto : SearchResultV6Dto<TaskDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Cursor | `string` | geerbt:SearchResultV6Dto | Byte encoded json which includes information about the current page and improves performance if we navigate through more than 10'000 results |
| Items | `IList<T>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfWorkspaceDto : SearchResultDto<WorkspaceDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Items | `IList<WorkspaceDto>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultOfWorkspaceV6Dto : SearchResultV6Dto<WorkspaceDto>  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Cursor | `string` | geerbt:SearchResultV6Dto | Byte encoded json which includes information about the current page and improves performance if we navigate through more than 10'000 results |
| Items | `IList<T>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SearchResultV6Dto<T> : SearchResultDto<T>  <sub>(Webservice)</sub>

For generic search retrieval of any entity such as Assets, Categories, etc.

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Cursor | `string` |  | Byte encoded json which includes information about the current page and improves performance if we navigate through more than 10'000 results |
| Items | `IList<T>` | geerbt:SearchResultDto | The found items of entity <see cref="T"/> |
| Facets | `IList<FacetDto>` | geerbt:SearchResultDto | If the result matches, facets will be returned. I.e. how many times each property value of the property definition specified with parameter <see cref="AssetSearchQueryDto.FacetPropertyDefinitionIdentifiers"/> occurs. Sorted by relevance. |

#### SetConfigurationValuesDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ConfigurationValues | `IList<ConfigurationValueDto>` |  |  |

#### SetPropertiesDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Properties | `IList<SetPropertyDto>` |  |  |

#### SetPropertyDto : RowVersionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  |  |
| PropertyValues | `IList<SetPropertyValueDto>` |  |  |
| InheritToNodeTypeIdentifiers | `IList<string>` |  |  |
| RowVersion | `byte[]` | geerbt:RowVersionDto |  |

#### SetPropertyResultDto : IdentifierRowVersionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  |  |
| IsSuccess | `bool` |  |  |
| Reason | `SetPropertyResultReasonDto` |  |  |
| Messages | `IList<string>` |  |  |
| Identifier | `string` | geerbt:IdentifierRowVersionDto |  |
| RowVersion | `byte[]` | geerbt:RowVersionDto |  |

#### SetPropertyValueDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyListItemIdentifier | `string` |  |  |
| PropertyValue | `IList<TranslatedValueDto>` |  |  |
| UserContextIdentifier | `string` |  |  |

#### ShareDerivationDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Derivation | `AssetDerivationDto` |  |  |
| ThumbnailUri | `string` |  |  |
| AssetSetIdentifier | `string` |  |  |

#### ShareDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` | required |  |
| Name | `string` | required |  |
| AssetTypeDerivations | `IDictionary<string, ISet<string>>` | required |  |
| ValidUntil | `DateTime?` | required |  |
| EmailRecipients | `IList<ShareRecipientDto>` | required |  |

#### ShareRecipientDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EmailAddress | `string` |  |  |
| Token | `string` |  |  |

#### ShareSearchEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### SortDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyDefinitionIdentifier | `string` |  | The property to sort for |
| Direction | `SortDirectionDto` |  | The direction to sort for (if omitted: default is 'ascending') |
| LanguageIdentifier | `string` |  | If the field is translated, the designated language field to sort for (if not given, the sorting will not work) |

#### StartUploadResponseDto  <sub>(Webservice)</sub> **(obsolete)**

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| UploadSessionId | `string` | required |  |

#### StatisticsEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

An event triggered by statistics measurement

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### TaskAssigneeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| UserIdentifier | `string` |  |  |

#### TaskDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` | required |  |
| Description | `string` |  |  |
| DueDate | `DateTime?` |  |  |
| Identifier | `string` |  |  |
| CreatedAt | `DateTime?` |  |  |
| CreatedBy | `string` |  |  |
| UpdatedAt | `DateTime?` |  |  |
| Status | `TaskStatusDto` |  |  |
| TaskStatus | `TaskStateDto` |  |  |
| TaskType | `string` |  |  |
| AssignedUsers | `IList<UserInfoDto>` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |
| SequenceNr | `int` |  |  |
| StateIdentifier | `string` |  |  |
| WorkspaceIdentifier | `string` |  |  |
| WorkspaceName | `IList<TranslatedValueDto>` |  |  |
| Properties | `IList<PropertyDto>` |  |  |
| TotalComments | `int` |  |  |

#### TaskEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### TaskSearchQueryDto : SearchQueryBaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ParentEntities | `IList<EntityDto>` |  | Filter in parent entities (optional). OR conjunct. If not given, no filter is applied |
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### TaskStatusDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `string` |  |  |
| IsDefault | `bool` |  |  |
| IsEndStatus | `bool` |  |  |
| Transitions | `IList<TaskStatusTransitionDto>` |  |  |

#### TaskStatusTransitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `string` |  |  |

#### TenantDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Default | `bool` |  |  |
| Status | `string` |  |  |
| AcceptsRegistrations | `bool` |  |  |

#### TenantEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### TenantExtendedDto : TenantDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| UserGroupRoleMaps | `IList<TenantUserGroupRoleMapDto>` |  |  |
| Identifier | `string` | geerbt:TenantDto |  |
| Name | `IList<TranslatedValueDto>` | geerbt:TenantDto |  |
| Default | `bool` | geerbt:TenantDto |  |
| Status | `string` | geerbt:TenantDto |  |
| AcceptsRegistrations | `bool` | geerbt:TenantDto |  |

#### TenantPermissionsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| TenantIdentifier | `string` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |

#### TenantRoleExtendedDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  | The public identifier of the role |
| TenantIdentifiers | `IList<string>` |  | The public identifier of tenants where the role is assigned to |
| Name | `IList<TranslatedValueDto>` |  | The name of the role |
| ObjectTypePermissions | `IList<ObjectTypePermissionDto>` |  | List of allowed actions (permissions) |
| PropertyEditEntityLimit | `int?` |  |  |
| PropertyExportEntityLimit | `int?` |  |  |

#### TenantUserGroupRoleMapDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| UserGroup | `UserGroupDto` |  |  |
| TenantIdentifier | `string` |  |  |
| RoleIdentifier | `string` |  |  |
| RowVersion | `byte[]` |  |  |

#### TranslatedValueDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DataLanguageIdentifier | `string` |  |  |
| Value | `string` |  |  |

#### UpdateBillingContactDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` |  |  |
| EmailAddress | `string` |  |  |
| Status | `EntityStatusDto` |  |  |

#### UpdateBuyoutDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Description | `string` |  |  |
| Basis | `double` |  |  |
| CurrencyIdentifier | `string` |  |  |
| AdditionalFeePercentage | `double` |  |  |
| FromDate | `DateTime` |  |  |
| ToDate | `DateTime` |  |  |
| RightsOwnerIdentifier | `string` |  |  |
| PriceListIdentifier | `string` |  |  |

#### UpdateCategoryDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  |  |

#### UpdateCustomEntityDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  |  |

#### UpdateDataLanguageDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  |  |
| Status | `EntityStatusDto` |  |  |
| IsDefault | `bool` |  |  |

#### UpdateDerivationJobDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DerivationTypeIdentifier | `string` |  |  |
| OutputFileTypeIdentifier | `string` |  |  |
| InputFileTypeIdentifiers | `IList<string>` |  |  |
| JobDefinition | `string` |  | Contains the job definition json as string e.g. { "DerivationDataType": 4, "Steps": [] ... } |

#### UpdateDerivationTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  |  |
| AssetTypeSettings | `IList<AssetTypeSettingsDto>` |  |  |

#### UpdateEntityTypeDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  |  |
| Description | `IList<TranslatedValueDto>` |  |  |
| Status | `EntityStatusDto` |  |  |

#### UpdateFileNamingDefinitionDto : ValidateFileNamingDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` | required |  |
| Tenants | `IList<FileNamingDefinitionTenantDto>` |  |  |
| IsGlobalDefault | `bool` |  |  |
| Format | `string` | required, geerbt:ValidateFileNamingDefinitionDto |  |
| CharacterCasing | `CharacterCasingDto` | geerbt:ValidateFileNamingDefinitionDto |  |
| ReplaceIllegalCharacters | `bool` | geerbt:ValidateFileNamingDefinitionDto |  |
| AbsentPropertyStrategy | `AbsentPropertyStrategyDto` | required, geerbt:ValidateFileNamingDefinitionDto |  |

#### UpdateLicenseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  |  |
| Description | `IList<TranslatedValueDto>` |  |  |
| PeriodStart | `DateTime` |  |  |
| PeriodEnd | `DateTime` |  |  |
| Status | `EntityStatusDto` |  |  |

#### UpdatePriceListDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ContextPercentages | `IList<SetContextPercentageDto>` |  |  |
| Name | `string` |  |  |
| Description | `string` |  |  |
| Status | `EntityStatusDto` |  |  |

#### UpdatePropertyDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyGroupIdentifier | `string` |  |  |
| AllowMultipleValues | `bool` |  |  |
| AllowCustomListValue | `bool` |  |  |
| Active | `bool` |  |  |
| Editable | `bool` |  |  |
| IgnoreCopying | `bool` |  |  |
| IsSearchableFacet | `bool` |  |  |
| SearchOption | `SearchOptionDto` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| Description | `IList<TranslatedValueDto>` |  |  |
| ListItems | `IList<CreateOrUpdatePropertyListItemDto>` |  |  |
| EntityTypeIdentifiers | `IList<string>` |  |  |
| IsFileNamingUnlocked | `bool` |  |  |
| HasUserContext | `bool` |  |  |
| DefinitionType | `PropertyDefinitionTypeDto` |  |  |
| SortKey | `int?` |  |  |

#### UpdatePropertyDto : RowVersionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyValues | `IList<PropertyValueDto>` |  |  |
| RowVersion | `byte[]` | geerbt:RowVersionDto |  |

#### UpdatePropertyExtractionDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ExtractionType | `PropertyExtractionTypeDto` |  |  |
| InputDerivationTypeIdentifier | `string` |  |  |
| InputAssetTypeIdentifiers | `ISet<string>` |  |  |
| ExtractionSources | `IList<PropertyExtractionSourceDto>` |  |  |
| Status | `EntityStatusDto?` |  |  |

#### UpdatePropertyGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  |  |
| Active | `bool` |  |  |
| SortKey | `int?` |  |  |

#### UpdatePropertyInheritanceDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| InheritToNodeTypeIdentifiers | `IList<string>` |  |  |

#### UpdatePropertySetDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` |  |  |
| IsGlobal | `bool` |  |  |
| IsDefault | `bool` |  |  |
| PropertySetConditions | `IList<PropertySetConditionDto>` |  |  |
| PropertySetPropertyDefinitions | `IList<PropertySetPropertyDefinitionDto>` |  |  |

#### UpdateRelationTypeDto : RowVersionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` | required |  |
| Description | `IList<TranslatedValueDto>` |  |  |
| Status | `EntityStatusDto` | required |  |
| IsDirected | `bool` |  |  |
| IsUsedForAssetSearch | `bool` |  |  |
| RowVersion | `byte[]` | geerbt:RowVersionDto |  |

#### UpdateResourcePermissionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| UserGroupIdentifier | `string` |  |  |
| UserIdentifier | `string` |  |  |
| RoleIdentifier | `string` | required |  |

#### UpdateRightsOwnerDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` |  |  |
| BillingContactIdentifier | `string` |  |  |
| Status | `EntityStatusDto` |  |  |

#### UpdateRuleDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  | The translated name of the rule. |
| NodeTypeIdentifiers | `IList<string>` |  | The node type identifiers of the entities on which the rule is executed. If empty list, the rule is executed on all node types. |
| Filters | `IList<CreateRuleTriggerDto>` |  | The filters an entity has to satisfy to be evaluated by the rule. |
| Triggers | `IList<CreateRuleTriggerDto>` | required | The triggers to be evaluated by the rule. |
| Actions | `IList<CreateRuleActionDto>` | required | The actions to be performed by the rule. |

#### UpdateSavedSearchDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` |  |  |
| SearchQuery | `string` |  | Represents a search query string as json object e.g. { "q" : "example", "languages" : "en", "exactSearch" : "true" } |

#### UpdateShareDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` | required | The name of the share |
| ValidUntil | `DateTime?` | required | Defines how long the share should exist |
| AssetTypeDerivations | `IDictionary<string, ISet<string>>` | required | The asset types (= key) with the corresponding derivation types (= value) |

#### UpdateTaskDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` | required |  |
| Description | `string` |  |  |
| DueDate | `DateTime?` |  |  |
| AssignedUserIdentifier | `string` |  |  |
| TaskStatus | `TaskStateDto` | required |  |

#### UpdateUserContextDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  |  |
| SequenceNr | `int` |  |  |
| IsDefault | `bool` |  |  |

#### UpdateUserContextGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  |  |
| UserContextIdentifiers | `IList<string>` |  |  |

#### UpdateUserDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| UserName | `string` |  |  |
| FirstName | `string` |  |  |
| LastName | `string` |  |  |
| EmailAddress | `string` |  |  |
| UserSettings | `CreateOrUpdateUserSettingsDto` |  |  |
| Status | `EntityStatusDto?` |  |  |
| ContactPerson | `string` |  |  |
| Company | `string` |  |  |
| PhoneNumber | `string` |  |  |
| AccessType | `AccessTypeDto` |  |  |

#### UpdateUserGroupDto : CreateUserGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `string` | geerbt:CreateUserGroupDto |  |
| Description | `string` | geerbt:CreateUserGroupDto |  |

#### UpdateWatermarkDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DisplayInUI | `bool` |  |  |
| DerivationJobDefinitionIdentifier | `string` |  |  |
| PropertyDefinitionIdentifier | `string` |  |  |

#### UpdateWatermarkTriggerDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| PropertyListValueIdentifier | `string` |  |  |
| ValueBool | `bool` |  |  |
| FileReferenceIdentifier | `string` |  |  |

#### UpdateWorkspaceDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Name | `IList<TranslatedValueDto>` |  |  |
| Status | `EntityStatusDto` | required |  |
| PropertySetIdentifier | `string` |  |  |
| FinalizationCategoryIdentifier | `string` |  |  |
| CopyPropertiesToAssets | `bool` |  |  |

#### UploadFinishCreateRelationDto  <sub>(Webservice/Upload)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| EntityIdentifier | `string` |  |  |
| EntityTypeIdentifier | `string` |  |  |
| RelationTypeIdentifier | `string` |  |  |

#### UploadFinishRequestDto  <sub>(Webservice/Upload)</sub>

Used to finish an upload

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ChunkIdentifiers | `IList<string>` | required | The identifiers of the uploaded chunks to be combined into the final file in the order of the provided identifiers. Not included chunks will be ignored and discarded |
| CopyMetadataFrom | `IList<CopyPropertiesFromEntityDto>` |  | Optionally copy metadata to the uploaded asset or asset version. |
| CreateRelations | `IList<UploadFinishCreateRelationDto>` |  | Optionally create relations from or to the uploaded asset. |
| Properties | `IList<SetPropertyDto>` |  | Optionally create properties on the uploaded asset or asset version. |

#### UploadFinishResponseDto  <sub>(Webservice/Upload)</sub>

Returned if the request to finish the asset upload was successful

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` | required | The identifier of the finished asset |
| AssetVersionIdentifier | `string` | required | The identifier of the finished asset version |
| AssetStatus | `string` | required | The status of the finished asset |
| ExistingAssetSetIdentifier | `string` |  | The identifier of an existing asset if the finished asset is a duplicate |
| CopyMetadataResults | `IList<SetPropertyResultDto>` |  | The results of the optional attempt to copy metadata to the finished asset or asset version. |
| CreateRelationResults | `IList<CreateRelationResultDto>` |  | The results of the optional attempt to create relations from or to the finished asset. |
| PropertyResults | `IList<SetPropertyResultDto>` |  | The results of the optional attempt to create properties on the finished asset or asset version. |

#### UploadStartRequestDto  <sub>(Webservice/Upload)</sub>

Used to request an upload of an asset, asset derivation or asset version

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetUploadType | `AssetUploadTypeDto` | required | The type of the upload |
| FileName | `string` | required | The name of the file |
| Asset | `AssetUploadDto` |  | Information required to upload an asset |
| AssetDerivation | `AssetDerivationUploadDto` |  | Information required to upload an asset derivation |
| AssetVersion | `AssetVersionUploadDto` |  | Information required to upload an asset version |

#### UploadStartResponseDto  <sub>(Webservice/Upload)</sub>

Returned if the upload start request was successful

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ChunkUploadRequestUrl | `string` | required | The URL which must be used to request an URL to upload a chunk. Valid for a limited time |
| MaxChunkSizeBytes | `long` | required | The maximum size of a single chunk in bytes |

#### UserAccountInfoDto : UserInfoDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AccessType | `AccessTypeDto` |  |  |
| AccountStatus | `AccountStatusDto` |  |  |
| Identifier | `string` | geerbt:UserInfoDto |  |
| UserName | `string` | geerbt:UserInfoDto |  |
| FirstName | `string` | geerbt:UserInfoDto |  |
| LastName | `string` | geerbt:UserInfoDto |  |
| EmailAddress | `string` | geerbt:UserInfoDto |  |
| AvatarUrl | `string` | geerbt:UserInfoDto |  |
| Status | `EntityStatusDto` | geerbt:UserInfoDto |  |
| Company | `string` | geerbt:UserInfoDto |  |

#### UserContextDto : CreateUserContextDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| RowVersion | `byte[]` |  |  |
| Identifier | `string` | geerbt:CreateUserContextDto |  |
| Name | `IList<TranslatedValueDto>` | geerbt:UpdateUserContextDto |  |
| SequenceNr | `int` | geerbt:UpdateUserContextDto |  |
| IsDefault | `bool` | geerbt:UpdateUserContextDto |  |

#### UserContextGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `ContextGroupIdentifierDto` |  |  |
| Name | `IList<TranslatedValueDto>` |  |  |
| IncludedContexts | `IList<UserContextGroupDto>` |  |  |

#### UserDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| UserName | `string` |  |  |
| FirstName | `string` |  |  |
| LastName | `string` |  |  |
| EmailAddress | `string` |  |  |
| UserSettings | `UserSettingsDto` |  |  |
| Status | `EntityStatusDto` |  |  |
| AccountStatus | `AccountStatusDto` |  |  |
| UserOrigin | `UserOriginDto` |  |  |
| LastLoginDate | `DateTime?` |  |  |
| CreatedAt | `DateTime` |  |  |
| ContactPerson | `string` |  |  |
| Company | `string` |  |  |
| PhoneNumber | `string` |  |  |
| AccessType | `AccessTypeDto` |  |  |

#### UserEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

An event triggered by an operation that affects a user

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### UserGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| Name | `string` |  |  |

#### UserGroupExtendedDto : UserGroupDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Description | `string` |  |  |
| MembersCount | `int` |  |  |
| Identifier | `string` | geerbt:UserGroupDto |  |
| Name | `string` | geerbt:UserGroupDto |  |

#### UserGroupMapEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### UserGroupMemberDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |

#### UserIdentifiersDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| UserIdentifiers | `IList<string>` |  |  |

#### UserInfoDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| UserName | `string` |  |  |
| FirstName | `string` |  |  |
| LastName | `string` |  |  |
| EmailAddress | `string` |  |  |
| AvatarUrl | `string` |  |  |
| Status | `EntityStatusDto` |  |  |
| Company | `string` |  |  |

#### UserNotificationSettingsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DisableNotifications | `bool` |  |  |

#### UserProfileDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Roles | `IList<TenantRoleExtendedDto>` |  |  |
| Settings | `UserSettingsDto` |  |  |

#### UserRegistrationDto : UserDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| TenantIdentifier | `string` |  |  |
| ApprovedAt | `DateTime?` |  |  |
| Identifier | `string` | geerbt:UserDto |  |
| UserName | `string` | geerbt:UserDto |  |
| FirstName | `string` | geerbt:UserDto |  |
| LastName | `string` | geerbt:UserDto |  |
| EmailAddress | `string` | geerbt:UserDto |  |
| UserSettings | `UserSettingsDto` | geerbt:UserDto |  |
| Status | `EntityStatusDto` | geerbt:UserDto |  |
| AccountStatus | `AccountStatusDto` | geerbt:UserDto |  |
| UserOrigin | `UserOriginDto` | geerbt:UserDto |  |
| LastLoginDate | `DateTime?` | geerbt:UserDto |  |
| CreatedAt | `DateTime` | geerbt:UserDto |  |
| ContactPerson | `string` | geerbt:UserDto |  |
| Company | `string` | geerbt:UserDto |  |
| PhoneNumber | `string` | geerbt:UserDto |  |
| AccessType | `AccessTypeDto` | geerbt:UserDto |  |

#### UserSettingsDto : CreateOrUpdateUserSettingsDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SearchTermOperatorDefault | `MultiTermOperatorDto?` |  |  |
| DisplayLanguage | `string` | geerbt:CreateOrUpdateUserSettingsDto |  |
| Locale | `string` | geerbt:CreateOrUpdateUserSettingsDto |  |
| DataLanguageIdentifier | `string` | geerbt:CreateOrUpdateUserSettingsDto |  |
| UiSettings | `string` | geerbt:CreateOrUpdateUserSettingsDto |  |
| UserContextIdentifier | `string` | geerbt:CreateOrUpdateUserSettingsDto |  |
| AvatarUrl | `string` | geerbt:CreateOrUpdateUserSettingsDto |  |
| SearchTermOperator | `MultiTermOperatorDto?` | geerbt:CreateOrUpdateUserSettingsDto |  |

#### ValidateAssetSetPropertiesResultDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AssetSetIdentifier | `string` |  |  |
| FileName | `string` |  |  |
| IsValid | `bool` |  |  |
| AssetSetPropertyValidationResults | `IDictionary<string, IList<AssetSetPropertyValidationResultDto>>` |  |  |

#### ValidateFileNamingDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Format | `string` | required |  |
| CharacterCasing | `CharacterCasingDto` |  |  |
| ReplaceIllegalCharacters | `bool` |  |  |
| AbsentPropertyStrategy | `AbsentPropertyStrategyDto` | required |  |

#### ValidationErrorDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| ParameterName | `string` |  |  |
| Errors | `IList<string>` |  |  |
| FieldErrors | `IDictionary<string, IList<FieldErrorDto>>` |  |  |

#### VideoDerivationJobDefinitionDto  <sub>(MessageQueue/V1/Processing/Derivation/Video)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| DerivatorName | `string` |  |  |

#### VideoDerivationStartMessage : JobStartMessage  <sub>(MessageQueue/V1/Processing/Derivation/Video)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| SourceDataId | `string` |  | The source data, the derivation is based on |
| OriginalFileName | `string` |  | The original file name |
| OutputFileExtension | `string` |  | The file extension of the generated output |
| DerivationTypeIdentifier | `string` |  | Identifier of the derivation type |
| JobDefinitionDto | `VideoDerivationJobDefinitionDto` |  | Defines what happens during derivation |
| ProcessingId | `string` | geerbt:JobStartMessage | Used to identify status updates for the process that was started |
| JobInitiatorProvidedMetaData | `IDictionary<string, string>` | geerbt:JobStartMessage | Data the job initiator can add to later identify the context of status messages |

#### WatermarkDefinitionDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| DisplayInUI | `bool` |  |  |
| PropertyDefinitionIdentifier | `string` |  |  |
| DerivationJobDefinitionIdentifier | `string` |  |  |
| WatermarkTriggers | `IList<WatermarkTriggerDto>` |  |  |
| RowVersion | `byte[]` |  |  |

#### WatermarkTriggerDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| Identifier | `string` |  |  |
| WatermarkDefinitionIdentifier | `string` |  |  |
| PropertyListValueIdentifier | `string` |  |  |
| ValueBool | `bool` |  |  |
| FileReferenceIdentifier | `string` |  |  |
| RowVersion | `byte[]` |  |  |

#### WorkspaceDto : CreateWorkspaceDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| TotalAssets | `int` |  |  |
| TotalTasks | `int` |  |  |
| TotalComments | `int` |  |  |
| TotalUsers | `int` |  |  |
| ApplicablePermissions | `IList<string>` |  |  |
| PropertyDefinitionRestrictions | `ISet<PropertyDefinitionRestrictionDto>` |  |  |
| RootCategoryIdentifier | `string` |  |  |
| Properties | `IList<PropertyDto>` |  |  |
| StateIdentifier | `string` |  |  |
| SequenceNr | `int` |  |  |
| CreatedAt | `DateTime` |  |  |
| ThumbnailUris | `IList<string>` |  |  |
| AvatarUrl | `string` |  |  |
| Identifier | `string` | geerbt:CreateWorkspaceDto |  |
| IsTemplate | `bool` | geerbt:CreateWorkspaceDto |  |
| WorkspaceType | `WorkspaceTypeDto` | geerbt:CreateWorkspaceDto |  |
| StateIdentifier | `string` | geerbt:CreateWorkspaceDto |  |
| Properties | `IList<SetPropertyDto>` | geerbt:CreateWorkspaceDto | Optionally create properties on the workspace. |
| Name | `IList<TranslatedValueDto>` | geerbt:UpdateWorkspaceDto |  |
| Status | `EntityStatusDto` | required, geerbt:UpdateWorkspaceDto |  |
| PropertySetIdentifier | `string` | geerbt:UpdateWorkspaceDto |  |
| FinalizationCategoryIdentifier | `string` | geerbt:UpdateWorkspaceDto |  |
| CopyPropertiesToAssets | `bool` | geerbt:UpdateWorkspaceDto |  |

#### WorkspaceEvent : DataEventBase  <sub>(MessageQueue/V1/EventStream)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| AffectedData | `IList<AffectedData>` | geerbt:DataEventBase |  |
| UtcTimeStamp | `DateTime` | geerbt:DataEventBase |  |
| EventCategory | `EventCategory` | geerbt:DataEventBase |  |
| AdditionalData | `IDictionary<string, string>` | geerbt:DataEventBase |  |
| User | `IDictionary<string, string>` | geerbt:DataEventBase |  |

#### WorkspaceSearchQueryDto : SearchQueryBaseDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| IsTemplate | `bool` |  |  |
| SearchTerm | `string` | json:q, geerbt:SearchQueryBaseDto | The full text search parameter (optional) |
| LanguageIdentifiers | `IList<string>` | json:languages, geerbt:SearchQueryBaseDto | Text/String properties only: The languages to consider (optional). E.g. {"en","de"} If not given, all languages are considered. Languages are not validated. |
| ExactSearch | `bool` | geerbt:SearchQueryBaseDto | Whether or not the search should be exact. Unexact search is (e.g.): "houses" also matches "house" (stemming). "the car" also matches "car" (ignore common stop words). |
| Operator | `MultiTermOperatorDto?` | geerbt:SearchQueryBaseDto | Define how multiple search terms in fulltext search should be combined (AND or OR) If not set, the default value from SearchServiceConfiguration is used |
| AdvancedSearchFilters | `IList<FilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties e.g for advanced search (optional). If not given, no filter is applied. Filters are in AND conjunction. |
| Sort | `IList<SortDto>` | geerbt:SearchQueryBaseDto | Sort by one or more properties (the earlier in the list the more important). If omitted, it uses the default sort order "relevance" (the more important first) then "update date" (the most recent updated first). If only categories or advanced search filter applied "relevance" is not taken into account since all hits are equally relevant. |
| IncludePropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | Enrich the result with properties (optional). |
| FacetPropertyDefinitionIdentifiers | `IList<string>` | geerbt:SearchQueryBaseDto | To include the "counts" for each item of a list item identifier or true/false counts for boolean property identifiers. If the property identifier is invalid, no result will be returned (not validated). |
| FacetFilters | `IList<FacetFilterDto>` | geerbt:SearchQueryBaseDto | Filter in properties (currently list items and booleans are supported) e.g for drilling down searches in an even more simple way than <see cref="FacetFilters"/> (optional). If not given, no filter is applied. Filters are in AND conjunction. |

#### ZipCategoryDownloadRequestDto  <sub>(Webservice)</sub>

| Feld | Typ | Flags | Beschreibung |
|---|---|---|---|
| FileNamingDefinitionIdentifier | `string` |  |  |
| DataLanguageIdentifier | `string` |  |  |
| IncludeSubCategories | `bool` |  |  |
| AssetDerivationTypes | `IList<AssetTypeDerivationTypesDto>` | required |  |

#### AbsentPropertyStrategyDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Unknown | 0 | Not specified |
| PropertyDefinitionIdentifier | 1 | Fall back to the PropertyDefinition identifier. |
| Skip | 2 | Ignore the absent property. |
| Abort | 3 | Prevent usage of the file naming definition. |

#### ActivityLogChangeTypeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| AssigneeSet | 0 |  |
| AssigneeUpdated | 1 |  |
| AssigneeCleared | 2 |  |
| CommentCreated | 3 |  |
| CommentUpdated | 4 |  |
| CommentDeleted | 5 |  |
| PropertyValueCreated | 6 |  |
| PropertyValueUpdated | 7 |  |
| PropertyValueDeleted | 8 |  |
| RelationCreated | 9 |  |
| RelationDeleted | 10 |  |
| ResourcePermissionAdded | 11 |  |
| ResourcePermissionUpdated | 12 |  |
| ResourcePermissionRemoved | 13 |  |
| StateUpdated | 14 |  |
| ShareCreated | 15 |  |
| ShareUpdated | 16 |  |
| ShareDeleted | 17 |  |
| ConfigurationValueCreated | 18 |  |
| ConfigurationValueUpdated | 19 |  |
| ConfigurationValueDeleted | 20 |  |

#### ActivityLogTypeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| AssetCreated | 0 |  |
| AssetUpdated | 1 |  |
| AssetDeleted | 2 |  |
| CategoryCreated | 3 |  |
| CategoryUpdated | 4 |  |
| CategoryDeleted | 5 |  |
| TaskCreated | 6 |  |
| TaskUpdated | 7 |  |
| TaskDeleted | 8 |  |
| WorkspaceCreated | 9 |  |
| WorkspaceUpdated | 10 |  |
| WorkspaceDeleted | 11 |  |
| JobUpdated | 12 |  |
| ApplicationSettingsUpdated | 13 |  |

#### ArchiveExtractionModeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| ExtractWithFullPath | 0 | Extracts files from an archive with their full paths in the output category. This is the default. |
| ExtractFilesOnly | 1 | Extracts all files from an archive to one output directory. |

#### AssetDerivationStatusDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Pending | 0 |  |
| Processing | 1 |  |
| Finished | 2 |  |
| Failed | 3 |  |

#### AssetUploadTypeDto (enum)  <sub>(Webservice/Upload)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Asset | 0 | Upload a new asset |
| AssetDerivation | 1 | Upload of a derivation for an existing asset |
| AssetVersion | 2 | Upload of a version for an existing asset |

#### BackgroundJobTriggerTypeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Unknown | 0 |  |
| EntityCreated | 1 |  |
| EntityDeleted | 2 |  |
| FullPermissionUpdate | 3 |  |
| PermissionCreated | 4 |  |

#### CommentSortByDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| CreatedAt | 0 |  |

#### ConfigurationProviderDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Unknown | 0 |  |
| AppSettingsYaml | 1 |  |
| AppSettingsOverridesYaml | 2 |  |
| Yaml | 3 |  |
| EnvironmentVariables | 4 |  |
| CommandLine | 5 |  |
| Database | 6 |  |

#### ContextGroupTypeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Group | 0 |  |
| UserContext | 1 |  |

#### CreateRelationResultReasonDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Created | 0 | The relation has been created. |
| Error | 1 | Failed to create the relation. |
| AlreadyExists | 2 | The relation already exists. |
| MutuallyExclusive | 3 | A mutually exclusive relation already exists. |
| PermissionDenied | 4 | Permission denied on the relation type or any of the involved entities. |
| Invalid | 5 | The involved entities do not match the requirements of the relation type. |

#### DeletePropertyResultReasonDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Success | 0 |  |
| NonExistingPropertyDefinition | 1 |  |
| DeleteSystemPropertyNotAllowed | 2 |  |
| NotEditable | 3 |  |
| NonExistingProperty | 4 |  |
| NotAllowed | 5 |  |

#### EventCategory (enum)  <sub>(MessageQueue/V1/EventStream/Base)</sub>

Describes what domain caused the event

| Wert | Nr. | Beschreibung |
|---|---|---|
| SystemHealth | 1 |  |
| DataEntity | 2 |  |

#### ExternalGroupSortByDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| ExternalGroupName | 0 |  |
| ExternalGroupDisplayName | 1 |  |

#### FacetOperatorDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Equals | 2 | Exact match |
| IdentifierEquals | 11 | To exactly search for properties that are stored with an identifier such as list items |

#### FileNamingTokenTypeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Unknown | 0 |  |
| Property | 1 |  |
| Variable | 2 |  |

#### JobStatus (enum)  <sub>(MessageQueue/V1/Processing)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Started | 0 |  |
| Processing | 1 |  |
| Finished | 2 |  |
| Failed | 3 |  |

#### JobStatusDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Unknown | 0 |  |
| EnqueuedForExecution | 1 |  |
| Executing | 2 |  |
| Executed | 3 |  |
| Failed | 4 |  |

#### PermissionDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Unknown | 0 |  |
| AdminView | 1 |  |
| AssetBrowserView | 2 |  |
| AssetDerivationCreate | 3 |  |
| AssetDerivationDelete | 4 |  |
| AssetDerivationRead | 5 |  |
| AssetDerivationUpdate | 6 |  |
| AssetDerivationView | 7 |  |
| AssetDerivationJobCreate | 8 |  |
| AssetDerivationJobDelete | 9 |  |
| AssetDerivationJobRead | 10 |  |
| AssetDerivationJobUpdate | 11 |  |
| AssetDerivationJobView | 12 |  |
| AssetDetailView | 13 |  |
| AssetDownloadRead | 14 |  |
| AssetPropertyCreate | 15 |  |
| AssetPropertyDelete | 16 |  |
| AssetPropertyRead | 17 |  |
| AssetPropertyUpdate | 18 |  |
| AssetPropertyView | 19 |  |
| AssetPropertyMassEditCreate | 20 |  |
| AssetPropertyMassEditDelete | 21 |  |
| AssetPropertyMassEditRead | 22 |  |
| AssetPropertyMassEditUpdate | 23 |  |
| AssetPropertyMassEditView | 24 |  |
| AssetPropertySetCreate | 25 |  |
| AssetPropertySetDelete | 26 |  |
| AssetPropertySetGlobalCreate | 27 |  |
| AssetPropertySetGlobalDelete | 28 |  |
| AssetPropertySetGlobalRead | 29 |  |
| AssetPropertySetGlobalUpdate | 30 |  |
| AssetPropertySetGlobalView | 31 |  |
| AssetPropertySetRead | 32 |  |
| AssetPropertySetUpdate | 33 |  |
| AssetPropertySetView | 34 |  |
| RelationCreate | 35 |  |
| RelationDelete | 36 |  |
| RelationRead | 37 |  |
| RelationUpdate | 38 |  |
| RelationView | 39 |  |
| AssetSetCreate | 40 |  |
| AssetSetDelete | 41 |  |
| AssetSetRead | 42 |  |
| AssetSetUpdate | 43 |  |
| AssetSetView | 44 |  |
| AssetSetDuplicateRead | 45 |  |
| CategoryCreate | 46 |  |
| CategoryDelete | 47 |  |
| CategoryRead | 48 |  |
| CategoryUpdate | 49 |  |
| CategoryView | 50 |  |
| CategoryPropertyCreate | 51 |  |
| CategoryPropertyDelete | 52 |  |
| CategoryPropertyRead | 53 |  |
| CategoryPropertyUpdate | 54 |  |
| CategoryPropertyView | 55 |  |
| CategoryPropertyInheritanceCreate | 56 |  |
| CategoryPropertyInheritanceDelete | 57 |  |
| CategoryPropertyInheritanceRead | 58 |  |
| CategoryPropertyInheritanceUpdate | 59 |  |
| CategoryPropertySetCreate | 60 |  |
| CategoryPropertySetDelete | 61 |  |
| CategoryPropertySetGlobalCreate | 62 |  |
| CategoryPropertySetGlobalDelete | 63 |  |
| CategoryPropertySetGlobalRead | 64 |  |
| CategoryPropertySetGlobalUpdate | 65 |  |
| CategoryPropertySetGlobalView | 66 |  |
| CategoryPropertySetRead | 67 |  |
| CategoryPropertySetUpdate | 68 |  |
| CategoryPropertySetView | 69 |  |
| CommentCreate | 70 |  |
| CommentDelete | 71 |  |
| CommentRead | 72 |  |
| CommentUpdate | 73 |  |
| CommentView | 74 |  |
| DashboardView | 75 |  |
| DataLanguageCreate | 76 |  |
| DataLanguageDelete | 77 |  |
| DataLanguageRead | 78 |  |
| DataLanguageUpdate | 79 |  |
| DataLanguageView | 80 |  |
| DerivationJobDefinitionCreate | 81 |  |
| DerivationJobDefinitionDelete | 82 |  |
| DerivationJobDefinitionRead | 83 |  |
| DerivationJobDefinitionUpdate | 84 |  |
| DerivationJobDefinitionView | 85 |  |
| DerivationTypeCreate | 86 |  |
| DerivationTypeDelete | 87 |  |
| DerivationTypeRead | 88 |  |
| DerivationTypeUpdate | 89 |  |
| DownloadBasketView | 90 |  |
| EdgeTypeCreate | 91 |  |
| EdgeTypeDelete | 92 |  |
| EdgeTypeRead | 93 |  |
| EdgeTypeUpdate | 94 |  |
| EdgeTypeView | 95 |  |
| FileNamingDefinitionCreate | 96 |  |
| FileNamingDefinitionDelete | 97 |  |
| FileNamingDefinitionRead | 98 |  |
| FileNamingDefinitionUpdate | 99 |  |
| FileNamingDefinitionView | 100 |  |
| GlobalAdmin | 101 |  |
| NodePermissionCreate | 102 |  |
| NodePermissionDelete | 103 |  |
| NodePermissionRead | 104 |  |
| NodePermissionUpdate | 105 |  |
| NodePermissionView | 106 |  |
| PropertyDefinitionCreate | 107 |  |
| PropertyDefinitionDelete | 108 |  |
| PropertyDefinitionRead | 109 |  |
| PropertyDefinitionUpdate | 110 |  |
| PropertyDefinitionView | 111 |  |
| PropertyGroupCreate | 112 |  |
| PropertyGroupDelete | 113 |  |
| PropertyGroupRead | 114 |  |
| PropertyGroupUpdate | 115 |  |
| PropertyGroupView | 116 |  |
| PropertyImportCreate | 117 |  |
| PropertyImportDelete | 118 |  |
| PropertyImportRead | 119 |  |
| PropertyImportUpdate | 120 |  |
| PropertyImportView | 121 |  |
| RecycleBinDelete | 122 |  |
| RecycleBinRead | 123 |  |
| RecycleBinView | 124 |  |
| ReIndexingJobCreate | 125 |  |
| ReIndexingJobRead | 126 |  |
| ReIndexingJobView | 127 |  |
| RoleCreate | 128 |  |
| RoleDelete | 129 |  |
| RoleRead | 130 |  |
| RoleUpdate | 131 |  |
| RoleView | 132 |  |
| SavedSearchCreate | 133 |  |
| SavedSearchDelete | 134 |  |
| SavedSearchRead | 135 |  |
| SavedSearchUpdate | 136 |  |
| SavedSearchView | 137 |  |
| ShareCreate | 138 |  |
| ShareDelete | 139 |  |
| ShareRead | 140 |  |
| ShareUpdate | 141 |  |
| ShareView | 142 |  |
| ShareFileNamingDefinitionCreate | 143 |  |
| ShareFileNamingDefinitionDelete | 144 |  |
| ShareFileNamingDefinitionRead | 145 |  |
| ShareFileNamingDefinitionUpdate | 146 |  |
| ShareFileNamingDefinitionView | 147 |  |
| TaskCreate | 148 |  |
| TaskDelete | 149 |  |
| TaskRead | 150 |  |
| TaskUpdate | 151 |  |
| TaskView | 152 |  |
| TaskPropertyCreate | 153 |  |
| TaskPropertyDelete | 154 |  |
| TaskPropertyRead | 155 |  |
| TaskPropertyUpdate | 156 |  |
| TaskPropertyView | 157 |  |
| TaskPropertySetCreate | 158 |  |
| TaskPropertySetDelete | 159 |  |
| TaskPropertySetGlobalCreate | 160 |  |
| TaskPropertySetGlobalDelete | 161 |  |
| TaskPropertySetGlobalRead | 162 |  |
| TaskPropertySetGlobalUpdate | 163 |  |
| TaskPropertySetGlobalView | 164 |  |
| TaskPropertySetRead | 165 |  |
| TaskPropertySetUpdate | 166 |  |
| TaskPropertySetView | 167 |  |
| TenantCreate | 168 |  |
| TenantDelete | 169 |  |
| TenantRead | 170 |  |
| TenantUpdate | 171 |  |
| TenantView | 172 |  |
| TenantFileNamingDefinitionCreate | 173 |  |
| TenantFileNamingDefinitionDelete | 174 |  |
| TenantFileNamingDefinitionRead | 175 |  |
| TenantFileNamingDefinitionUpdate | 176 |  |
| TenantFileNamingDefinitionView | 177 |  |
| UploadView | 178 |  |
| UserCreate | 179 |  |
| UserDelete | 180 |  |
| UserRead | 181 |  |
| UserUpdate | 182 |  |
| UserView | 183 |  |
| UserContextCreate | 184 |  |
| UserContextDelete | 185 |  |
| UserContextUpdate | 186 |  |
| UserGroupCreate | 187 |  |
| UserGroupDelete | 188 |  |
| UserGroupRead | 189 |  |
| UserGroupUpdate | 190 |  |
| UserGroupView | 191 |  |
| VersionCreate | 192 |  |
| VersionDelete | 193 |  |
| VersionRead | 194 |  |
| VersionUpdate | 195 |  |
| VersionView | 196 |  |
| VersionPropertySetCreate | 197 |  |
| VersionPropertySetDelete | 198 |  |
| VersionPropertySetGlobalCreate | 199 |  |
| VersionPropertySetGlobalDelete | 200 |  |
| VersionPropertySetGlobalRead | 201 |  |
| VersionPropertySetGlobalUpdate | 202 |  |
| VersionPropertySetGlobalView | 203 |  |
| VersionPropertySetRead | 204 |  |
| VersionPropertySetUpdate | 205 |  |
| VersionPropertySetView | 206 |  |
| WatermarkCreate | 207 |  |
| WatermarkDelete | 208 |  |
| WatermarkRead | 209 |  |
| WatermarkUpdate | 210 |  |
| WatermarkView | 211 |  |
| WorkspaceCreate | 212 |  |
| WorkspaceDelete | 213 |  |
| WorkspaceRead | 214 |  |
| WorkspaceUpdate | 215 |  |
| WorkspaceView | 216 |  |
| WorkspacePropertyCreate | 217 |  |
| WorkspacePropertyDelete | 218 |  |
| WorkspacePropertyRead | 219 |  |
| WorkspacePropertyUpdate | 220 |  |
| WorkspacePropertyView | 221 |  |
| WorkspacePropertySetCreate | 222 |  |
| WorkspacePropertySetDelete | 223 |  |
| WorkspacePropertySetGlobalCreate | 224 |  |
| WorkspacePropertySetGlobalDelete | 225 |  |
| WorkspacePropertySetGlobalRead | 226 |  |
| WorkspacePropertySetGlobalUpdate | 227 |  |
| WorkspacePropertySetGlobalView | 228 |  |
| WorkspacePropertySetRead | 229 |  |
| WorkspacePropertySetUpdate | 230 |  |
| WorkspacePropertySetView | 231 |  |
| WorkspaceTemplateCreate | 232 |  |
| WorkspaceTemplateDelete | 233 |  |
| WorkspaceTemplateRead | 234 |  |
| WorkspaceTemplateUpdate | 235 |  |
| WorkspaceTemplateView | 236 |  |
| CropDerivationCreate | 237 |  |
| EntityCreate | 238 |  |
| EntityDelete | 239 |  |
| EntityRead | 240 |  |
| EntityUpdate | 241 |  |
| EntityView | 242 |  |
| CustomEntityCreate | 243 |  |
| CustomEntityDelete | 244 |  |
| CustomEntityRead | 245 |  |
| CustomEntityUpdate | 246 |  |
| CustomEntityView | 247 |  |
| CustomEntityPropertyCreate | 248 |  |
| CustomEntityPropertyDelete | 249 |  |
| CustomEntityPropertyRead | 250 |  |
| CustomEntityPropertyUpdate | 251 |  |
| CustomEntityPropertyView | 252 |  |
| ApplicationSettingsCreate | 253 |  |
| ApplicationSettingsDelete | 254 |  |
| ApplicationSettingsRead | 255 |  |
| ApplicationSettingsUpdate | 256 |  |
| ApplicationSettingsView | 257 |  |
| RuleCreate | 258 |  |
| RuleDelete | 259 |  |
| RuleRead | 260 |  |
| RuleUpdate | 261 |  |
| RuleView | 262 |  |
| RuleReApplyJobCreate | 263 |  |
| CustomEntityBrowserView | 264 |  |
| CustomEntityPropertySetCreate | 265 |  |
| CustomEntityPropertySetDelete | 266 |  |
| CustomEntityPropertySetRead | 267 |  |
| CustomEntityPropertySetUpdate | 268 |  |
| CustomEntityPropertySetView | 269 |  |
| CustomEntityPropertySetGlobalCreate | 270 |  |
| CustomEntityPropertySetGlobalDelete | 271 |  |
| CustomEntityPropertySetGlobalRead | 272 |  |
| CustomEntityPropertySetGlobalUpdate | 273 |  |
| CustomEntityPropertySetGlobalView | 274 |  |
| ArchiveExtractionJob | 275 |  |
| ReIndexingJobDelete | 276 |  |
| StateCreate | 277 |  |
| StateDelete | 278 |  |
| StateRead | 279 |  |
| StateUpdate | 280 |  |
| StateView | 281 |  |
| CollectionView | 282 |  |
| WorkspaceFinalize | 283 |  |
| RuleJobReportRead | 284 |  |
| LicenseView | 285 |  |
| LicenseRead | 286 |  |
| LicenseCreate | 287 |  |
| LicenseUpdate | 288 |  |
| LicenseDelete | 289 |  |
| WorkspaceActivityLogRead | 290 |  |
| WorkspaceActivityLogView | 291 |  |
| TaskActivityLogRead | 292 |  |
| TaskActivityLogView | 293 |  |
| AssetActivityLogRead | 294 |  |
| AssetActivityLogView | 295 |  |
| LicenseOfferView | 296 |  |
| LicenseOfferRead | 297 |  |
| PropertyExtractionDefinitionCreate | 298 |  |
| PropertyExtractionDefinitionDelete | 299 |  |
| PropertyExtractionDefinitionRead | 300 |  |
| PropertyExtractionDefinitionUpdate | 301 |  |
| PropertyExtractionDefinitionView | 302 |  |
| LicensePurchaseCreate | 303 |  |
| LicensePurchaseRead | 304 |  |
| PropertyExtractionJob | 305 |  |
| PermissionMaterializationJobRead | 306 |  |
| PermissionMaterializationJobCreate | 307 |  |
| PermissionMaterializationJobView | 308 |  |
| CategoryDownloadRead | 309 |  |
| ApplicationSettingsActivityLogRead | 310 |  |
| ApplicationSettingsActivityLogView | 311 |  |

#### PriceListSortByDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Identifier | 0 |  |
| Name | 1 |  |
| Description | 2 |  |
| Status | 3 |  |
| CreatedAt | 4 |  |
| UpdatedAt | 5 |  |

#### PropertyActionStrategyDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Unknown | 0 | Unknown |
| KeepExisting | 1 | Create if not exists, otherwise keep existing |
| OverwriteExisting | 2 | Create if not exists, otherwise replace existing |
| Abort | 3 | Create if not exists, otherwise abort |
| DeleteExisting | 4 | Delete if exists, otherwise ignore it |
| Merge | 5 | Merge values for string, text and multiselect listitems |

#### PropertyDefinitionFieldsDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Name | 0 |  |
| Description |  |  |
| EntityTypes |  |  |
| PropertyGroup |  |  |
| SortKey |  |  |
| DefinitionType |  |  |
| IsSearchableFacet |  |  |
| Editable |  |  |
| SearchOption |  |  |
| IgnoreCopying |  |  |
| Status |  |  |
| IsFileNamingUnlocked |  |  |
| HasUserContext |  |  |
| AllowMultipleValues |  |  |
| AllowCustomListValue |  |  |
| ListItems |  |  |

#### PropertyDefinitionListTypeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| NoList | 0 |  |
| List | 1 |  |
| Multiselect | 2 |  |
| Singleselect | 3 |  |

#### PropertyDefinitionSortByDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Identifier | 0 |  |
| PropertyGroupIdentifier | 1 |  |
| ValueType | 2 |  |
| Editable | 3 |  |
| IsSystem | 4 |  |
| IsSearchableFacet | 5 |  |
| Active | 6 |  |
| SortKey | 7 |  |

#### PropertyDefinitionTypeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Standard | 0 |  |
| Url | 1 |  |
| Html | 2 |  |

#### PropertyExportFileFormatDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Unknown | 0 |  |
| Xls | 1 |  |
| Xlsx | 2 |  |
| Csv | 3 |  |

#### PropertyExtractionJobAssetResultStatusDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Pending | 0 |  |
| Extracted | 1 |  |
| Imported | 2 |  |
| Error | 3 |  |

#### PropertyExtractionProviderDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| AzureCognitiveServices | 0 |  |
| Imagga | 1 |  |
| Clarifai | 2 |  |

#### PropertyExtractionResultStatusDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Extracted | 0 |  |
| Imported | 1 |  |
| Skipped | 2 |  |
| Error | 3 |  |

#### PropertyExtractionSourceStatusDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Extracted | 0 |  |
| Imported | 1 |  |
| Error | 2 |  |

#### PropertyExtractionTypeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| DetectTags | 0 |  |

#### PropertyGroupSortByDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| PropertyDefinitionsCount | 0 |  |
| SortKey | 1 |  |
| Active | 2 |  |

#### PropertyImportMessageSeverityDto (enum)  <sub>(Webservice)</sub>

Describes the severity of a PropertyImportValidation message

| Wert | Nr. | Beschreibung |
|---|---|---|
| Info | 1 |  |
| Warning | 2 |  |
| Error | 3 |  |

#### PropertyImportMessageTypeDto (enum)  <sub>(Webservice)</sub>

Parsing message type

| Wert | Nr. | Beschreibung |
|---|---|---|
| Unknown | 0 |  |
| FileFormatError | 1 |  |
| MetadataTabNotFound | 2 |  |
| EntityIdentifierNotFound | 3 |  |
| EnttiyTypeNotFound | 4 |  |
| PropertyDefinitionIdentifierOrLanguageParseError | 5 |  |

#### PropertyValueTypeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| String | 0 |  |
| Text | 1 |  |
| Date | 2 |  |
| DateTime | 3 |  |
| Time | 4 |  |
| Integer | 5 |  |
| Float | 6 |  |
| Decimal | 7 |  |
| Boolean | 8 |  |
| TimeSpan | 9 |  |

#### SearchOptionDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| NotSearchable | 0 |  |
| AdvancedSearchable | 1 |  |
| FulltextSearchable | 2 |  |
| AdvancedAndFulltextSearchable | 3 |  |

#### SetPropertyResultReasonDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Success | 0 |  |
| CreateOrUpdateRestricted | 1 |  |
| InheritancePermissionDenied | 2 |  |
| PropertyDefinitionNotExists | 3 |  |
| NotEditable | 4 |  |
| ChangeSystemPropertyNotAllowed | 5 |  |
| Invalid | 6 |  |
| CreateOrUpdatePropertyValuesFailed | 7 |  |
| CreateInEditNotAllowed | 8 |  |

#### SortDirectionDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Asc | 0 |  |
| Desc | 1 |  |

#### TaskStateDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Open | 0 |  |
| Closed | 1 |  |

#### UserGroupSortByDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Name | 0 |  |
| Created | 1 |  |

#### UserOriginDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Unknown | 0 |  |
| Sharedien | 1 |  |
| ExternalProvider | 2 |  |

#### UserSortByDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| FirstName | 0 |  |
| LastName | 1 |  |
| Email | 2 |  |
| UserName | 3 |  |

#### WorkspaceTypeDto (enum)  <sub>(Webservice)</sub>

| Wert | Nr. | Beschreibung |
|---|---|---|
| Upload | 0 |  |
| Collection | 1 |  |
| Collaboration | 2 |  |

### C.2 Alle DTOs und Enums kompakt nach Ordner

#### Ordner `MessageQueue/V1/EventStream` (16 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **AssetEvent** | DataEventBase | Event that is sent when Asset-Data is changed | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **CategoryEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **CustomEntityEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **DerivationEvent** | DataEventBase | Event that is created when derivation-Data is changed | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **DerivationTypeEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **EntityTypeEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **PropertyDefinitionEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **PropertyEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **RoleEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **ShareSearchEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **StatisticsEvent** | DataEventBase | An event triggered by statistics measurement | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **TaskEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **TenantEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **UserEvent** | DataEventBase | An event triggered by an operation that affects a user | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **UserGroupMapEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **WorkspaceEvent** | DataEventBase |  | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |

#### Ordner `MessageQueue/V1/EventStream/Base` (2 Klassen, 3 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **AffectedData** |  | Describes the data that was affected by the event | ObjectType: ObjectType; AffectedDataIdentifier: string; EventAction: EventAction; EventActionDetails: string; AdditionalData: IDictionary<string,string> |
| **DataEventBase (abstract)** |  | Base class for the EventStream produced by the Sharedien API for external consumers | AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string> |

| Enum | Beschreibung | Werte |
|---|---|---|
| **EventAction** (string) | Describes what action caused the event | Read=1, Create=2, Update=3, Delete=4 |
| **EventCategory** (string) | Describes what domain caused the event | SystemHealth=1, DataEntity=2 |
| **ObjectType** (string) | Describes object types in the system | User=0, UserGroupMap=1, Role=2, Tenant=3, AssetSet=4, AssetVersion=5, Derivation=6, DerivationType=7, Property=8, PropertyDefinition=9, Category=10, Task=11, Relation=12, SharedSearch=13, ObjectTypePermission=14, NodePermission=15, PropertyDefinitionPermission=16, DerivationTypeRestriction=17, Workspace=18, CustomNode=19, ArchiveExtractionJob=20, PropertyValue=21, Statistics=22, WorkspaceTemplate=23, NodeType=24, PropertyMassEditJob=25, AssetReDerivationJob=26, PropertyImportJob=27, BillingContact=28, RightsOwner=29, License=30, Purchase=31 |

#### Ordner `MessageQueue/V1/Processing` (2 Klassen, 1 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **JobStartMessage (abstract)** |  | Message to start a processing job | ProcessingId: string; JobInitiatorProvidedMetaData: IDictionary<string, string> |
| **JobStatusMessage (abstract)** |  | Message that is created to pass status updates of a scheduled, running or finished job | ProcessingIdentifier: string; JobStatus: JobStatus; Message: string; JobInitiatorProvidedMetaData: IDictionary<string, string>; JobMetaData: IDictionary<string, string> |

| Enum | Beschreibung | Werte |
|---|---|---|
| **JobStatus** |  | Started=0, Processing=1, Finished=2, Failed=3 |

#### Ordner `MessageQueue/V1/Processing/Derivation` (1 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **DerivationStatusMessage** | JobStatusMessage | Status message for a derivation | DataIdentifier: string; FileSize: long; FileHash: string **[geerbt: ProcessingIdentifier: string; JobStatus: JobStatus; Message: string; JobInitiatorProvidedMetaData: IDictionary<string, string>; JobMetaData: IDictionary<string, string>]** |

#### Ordner `MessageQueue/V1/Processing/Derivation/General` (5 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **CustomDerivationJobDefinitionDto** |  | Custom Job definition | Steps: IList<CustomDerivationStepDto> |
| **CustomDerivationOptionalFileDto** |  |  | DataId: string; FileName: string |
| **CustomDerivationStartMessage** | JobStartMessage | Message used to trigger processing a custom job | SourceDataId: string; OriginalFileName: string; OriginalFileExtension: string; OriginalFileSize: long; AssetType: string; OptionalFiles: IList<CustomDerivationOptionalFileDto>; OutputFileExtension: string; OutputMimeType: string; DerivationTypeIdentifier: string; JobDefinitionDto: CustomDerivationJobDefinitionDto **[geerbt: ProcessingId: string; JobInitiatorProvidedMetaData: IDictionary<string, string>]** |
| **CustomDerivationStepDto** |  | Contains parameters for one step of a custom job | Variables: IList<CustomDerivationVariableDto>; CustomStepType: string; StepOutputFile: string; StepInputFile: string; ArgumentString: string; TargetProfile: string; IntentString: string; UseColorProfileOverrides: bool |
| **CustomDerivationVariableDto** |  | Definition for a custom variable | Name: string; Expression: string; Evaluate: bool |

#### Ordner `MessageQueue/V1/Processing/Derivation/Image` (21 Klassen, 5 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **AbsoluteScaleDto** |  |  | AbsolutePixelWidth: int; AbsolutePixelHeight: int; NaturalWidth: int; NaturalHeight: int |
| **BlurDefinitionDto** |  |  | BlurRadiusInPixels: double; StandardLaplaceDeviationInPixels: double |
| **BlurStepDto** | TransformationStepDto |  | BlurDefinition: BlurDefinitionDto **[geerbt: Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int]** |
| **ColorDefinitionDto** |  |  | ColorDefinitionType: ColorDefinitionTypeDto; Alpha: double; CmykCyan: double; CmykMagenta: double; CmykYellow: double; CmykBlack: double; RgbRed: double; RgbGreen: double; RgbBlue: double |
| **CropDefinitionDto** |  |  | SizeX: int; SizeY: int; XAxisAnchor: XAxisAnchorDto; YAxisAnchor: YAxisAnchorDto; XAxisAnchorOffset: int; YAxisAnchorOffset: int; NaturalWidth: int; NaturalHeight: int |
| **CropStepDto** | TransformationStepDto |  | CropDefinition: CropDefinitionDto **[geerbt: Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int]** |
| **ImageDerivationJobDefinitionDto** |  |  | SourceImageDefinition: InputImageDefinitionDto; SourceImageTransformation: ImageTransformationDefinitionDto; OverlayDefinitions: IList<OverlayDefinitionDto>; OutputImageDefinition: OutputImageDefinitionDto |
| **ImageDerivationStartMessage** | JobStartMessage |  | SourceDataId: string; OriginalFileName: string; OutputFileExtension: string; OutputMimeType: string; DerivationTypeIdentifier: string; JobDefinitionDto: ImageDerivationJobDefinitionDto **[geerbt: ProcessingId: string; JobInitiatorProvidedMetaData: IDictionary<string, string>]** |
| **ImageQualityDefinitionDto** |  |  | ColorSpace: string; ColorType: string; ColorProfileName: string; Dpi: double?; CompressionMethod: string |
| **ImageTransformationDefinitionDto** |  |  | TransformationSteps: IList<TransformationStepDto>; SetOpacity: bool; OpacityValue: double; SetBackgroundColor: bool; BackgroundColorDefinition: ColorDefinitionDto |
| **InputImageDefinitionDto** | ImageQualityDefinitionDto |  | FrameIndex: int? **[geerbt: ColorSpace: string; ColorType: string; ColorProfileName: string; Dpi: double?; CompressionMethod: string]** |
| **OutputImageDefinitionDto** | ImageQualityDefinitionDto |  | ImageType: string; ImageQuality: int? **[geerbt: ColorSpace: string; ColorType: string; ColorProfileName: string; Dpi: double?; CompressionMethod: string]** |
| **OverlayDefinitionDto** |  |  | OverlayDataId: string; ImageDefinition: InputImageDefinitionDto; ImageTransformationDefinition: ImageTransformationDefinitionDto; AnchorPixelOffsetX: int; AnchorPixelOffsetY: int; SourceImageXAxisAnchor: XAxisAnchorDto; SourceImageYAxisAnchor: YAxisAnchorDto; OverlayImageXAxisAnchor: XAxisAnchorDto; OverlayImageYAxisAnchor: YAxisAnchorDto |
| **RelativeScaleDto** |  |  | RelativeWidthPercent: double; RelativeHeightPercent: double |
| **RotateDefinitionDto** |  |  | RotationDirection: RotateDirectionDto; RotationDegrees: double |
| **RotateStepDto** | TransformationStepDto |  | RotateDefinition: RotateDefinitionDto **[geerbt: Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int]** |
| **ScaleDefinitionDto** |  |  | ScaleType: ScaleTypeDto; IgnoreAspectRatio: bool; AbsoluteScale: AbsoluteScaleDto; RelativeScale: RelativeScaleDto; ScaleToFillArea: bool; PreventDownscaling: bool; PreventUpscaling: bool |
| **ScaleStepDto** | TransformationStepDto |  | ScaleDefinition: ScaleDefinitionDto **[geerbt: Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int]** |
| **SharpenDefinitionDto** |  |  | SharpeningRadiusInPixels: double; StandardLaplaceDeviationInPixels: double |
| **SharpenStepDto** | TransformationStepDto |  | SharpenDefinition: SharpenDefinitionDto **[geerbt: Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int]** |
| **TransformationStepDto (abstract)** |  |  | Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int |

| Enum | Beschreibung | Werte |
|---|---|---|
| **ColorDefinitionTypeDto** |  | RgbA=0, CmykA=1 |
| **RotateDirectionDto** |  | CounterClockWise, ClockWise |
| **ScaleTypeDto** |  | Absolute, Relative |
| **XAxisAnchorDto** |  | Left, Center, Right |
| **YAxisAnchorDto** |  | Top, Center, Bottom |

#### Ordner `MessageQueue/V1/Processing/Derivation/Office` (2 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **OfficeDerivationJobDefinitionDto** |  |  | DerivatorName: string |
| **OfficeDerivationStartMessage** | JobStartMessage |  | SourceDataId: string; OriginalFileName: string; OutputFileExtension: string; DerivationTypeIdentifier: string; JobDefinitionDto: OfficeDerivationJobDefinitionDto **[geerbt: ProcessingId: string; JobInitiatorProvidedMetaData: IDictionary<string, string>]** |

#### Ordner `MessageQueue/V1/Processing/Derivation/Print` (2 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **PrintDerivationJobDefinitionDto** |  |  | DerivatorName: string |
| **PrintDerivationStartMessage** | JobStartMessage |  | SourceDataId: string; OriginalFileName: string; OutputFileExtension: string; DerivationTypeIdentifier: string; JobDefinitionDto: PrintDerivationJobDefinitionDto **[geerbt: ProcessingId: string; JobInitiatorProvidedMetaData: IDictionary<string, string>]** |

#### Ordner `MessageQueue/V1/Processing/Derivation/Video` (2 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **VideoDerivationJobDefinitionDto** |  |  | DerivatorName: string |
| **VideoDerivationStartMessage** | JobStartMessage |  | SourceDataId: string; OriginalFileName: string; OutputFileExtension: string; DerivationTypeIdentifier: string; JobDefinitionDto: VideoDerivationJobDefinitionDto **[geerbt: ProcessingId: string; JobInitiatorProvidedMetaData: IDictionary<string, string>]** |

#### Ordner `MessageQueue/V1/Processing/MetadataExtraction` (4 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **MetadataExtractionProperty** |  | Single metadata information tag | Name: string; Value: string; Tag: string |
| **MetadataExtractionRestartMessage** | MetadataExtractionStartMessage | Message to re-start metadata extraction | **[geerbt: SourceDataId: string; OriginalFileName: string; OriginalFileExtension: string; OriginalFileSize: long; OutputFileExtension: string; OutputMimeType: string; AssetSetIdentifier: string; AssetVersionIdentifier: string; AssetType: string; ProcessingId: string; JobInitiatorProvidedMetaData: IDictionary<string, string>]** |
| **MetadataExtractionStartMessage** | JobStartMessage | Message to start metadata extraction | SourceDataId: string; OriginalFileName: string; OriginalFileExtension: string; OriginalFileSize: long; OutputFileExtension: string; OutputMimeType: string; AssetSetIdentifier: string; AssetVersionIdentifier: string; AssetType: string **[geerbt: ProcessingId: string; JobInitiatorProvidedMetaData: IDictionary<string, string>]** |
| **MetadataExtractionStatusMessage** | JobStatusMessage | Message containing information about the status of the metadata extraction | Properties: IList<MetadataExtractionProperty>; AssetSetIdentifier: string; AssetVersionIdentifier: string **[geerbt: ProcessingIdentifier: string; JobStatus: JobStatus; Message: string; JobInitiatorProvidedMetaData: IDictionary<string, string>; JobMetaData: IDictionary<string, string>]** |

#### Ordner `MessageQueue/V2/EventStream` (5 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **ArchiveExtractionEvent** | DataEventBase | Event that is sent when an archive extraction job is changed | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **AssetReDerivationEvent** | DataEventBase | Event that is sent when an asset re-derivation job is changed | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **PropertyImportEvent** | DataEventBase | Event that is sent when a property import job is changed | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **PropertyMassEditEvent** | DataEventBase | Event that is sent when a property mass edit job is changed | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |
| **RelationEvent** | DataEventBase | Event that is sent when relation data is changed | **[geerbt: AffectedData: IList<AffectedData>; UtcTimeStamp: DateTime; EventCategory: EventCategory; AdditionalData: IDictionary<string, string>; User: IDictionary<string, string>]** |

#### Ordner `SignalR` (4 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **ActivityLogUpdateAffectedDataDto** |  |  | Identifier: string; EntityTypeIdentifier: string; ObjectType: Webservice.ObjectTypeDto |
| **ActivityLogUpdateDto** |  |  | Identifier: string; Type: Webservice.ActivityLogTypeDto; TriggeredByUserIdentifier: string; AffectedData: ActivityLogUpdateAffectedDataDto; CreatedAt: DateTime |
| **INotificationHubClient** |  |  |  |
| **Message** |  |  | ActivityLogUpdate: ActivityLogUpdateDto; UtcTimeStamp: DateTime |

#### Ordner `Webservice` (329 Klassen, 55 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **ActivityLogAffectedDataDto** |  |  | Identifier: string; EntityTypeIdentifier: string; ObjectType: ObjectTypeDto; Name: IList<TranslatedValueDto>; AdditionalData: IDictionary<string, string>; AdditionalAffectedData: IList<ActivityLogAffectedDataDto> |
| **ActivityLogChangeDto** |  |  | Type: ActivityLogChangeTypeDto; AffectedData: ActivityLogAffectedDataDto |
| **ActivityLogDto** |  |  | Identifier: string; Type: ActivityLogTypeDto; TriggeredBy: UserInfoDto; AffectedData: ActivityLogAffectedDataDto; CreatedAt: DateTime; Changes: IList<ActivityLogChangeDto> |
| **ActivityLogIdentifiersDto** |  |  | ActivityLogIdentifiers: IList<string> |
| **ActivityLogNotificationDto** |  |  | Identifier: string; ReadAt: DateTime?; Type: ActivityLogTypeDto; TriggeredBy: UserInfoDto; AffectedData: ActivityLogAffectedDataDto; CreatedAt: DateTime; Changes: IList<ActivityLogChangeDto> |
| **ArchiveExtractionEntryDto** |  | Represents an entry in an archive. | EntryType: ArchiveEntryTypeDto; Status: ArchiveEntryStatusDto; Path: string; Name: string; CreatedEntity: EntityDto |
| **ArchiveExtractionJobDto** |  | An archive extraction job. | Identifier: string; Status: JobStatusDto; CreatedBy: UserInfoDto; CreatedAt: DateTime; InitiatedAt: DateTime?; FinishedAt: DateTime?; ArchiveAssetSetIdentifier: string; OutputCategoryIdentifier: string; ArchiveExtractionMode: ArchiveExtractionModeDto; DeleteAssetAfterExtraction: bool; TotalEntryCount: int; FailedEntryCount: int; ExtractedEntryCount: int; FilteredEntryCount: int; ApplicablePermissions: IList<string> |
| **ArchiveExtractionJobReportDto** | ArchiveExtractionJobDto | The report to an archive extraction job. | Entries: IList<ArchiveExtractionEntryDto> **[geerbt: Identifier: string; Status: JobStatusDto; CreatedBy: UserInfoDto; CreatedAt: DateTime; InitiatedAt: DateTime?; FinishedAt: DateTime?; ArchiveAssetSetIdentifier: string; OutputCategoryIdentifier: string; ArchiveExtractionMode: ArchiveExtractionModeDto; DeleteAssetAfterExtraction: bool; TotalEntryCount: int; FailedEntryCount: int; ExtractedEntryCount: int; FilteredEntryCount: int; ApplicablePermissions: IList<string>]** |
| **AssetDerivationDto** |  |  | AssetTypeIdentifier: string; FileTypeIdentifier: string; FileMimeType: string; DerivationTypeIdentifier: string; FileName: string; FileEnding: string; FileSize: long; Uri: string; DownloadUri: string; FileHash: string; Status: AssetDerivationStatusDto |
| **AssetDerivationJobAssetDerivationDto** |  |  | AssetSetIdentifier: string; AssetVersionIdentifier: string; DerivationTypeIdentifier: string; AssetTypeIdentifier: string; FileTypeIdentifier: string; FileMimeType: string; FileName: string; FileEnding: string; FileSize: long; Status: AssetDerivationStatusDto; AssetDerivation: AssetDerivationDto~ |
| **AssetDerivationJobDto** |  |  | Identifier: string; Status: JobStatusDto; CreatedBy: UserInfoDto; CreatedAt: DateTime; InitiatedAt: DateTime?; FinishedAt: DateTime?; TotalAssetDerivationCount: int; FailedAssetDerivationCount: int; SucceededAssetDerivationCount: int; ApplicablePermissions: IList<string> |
| **AssetDerivationJobReportDto** | AssetDerivationJobDto |  | AssetDerivations: IList<AssetDerivationJobAssetDerivationDto> **[geerbt: Identifier: string; Status: JobStatusDto; CreatedBy: UserInfoDto; CreatedAt: DateTime; InitiatedAt: DateTime?; FinishedAt: DateTime?; TotalAssetDerivationCount: int; FailedAssetDerivationCount: int; SucceededAssetDerivationCount: int; ApplicablePermissions: IList<string>]** |
| **AssetDerivationTypeDto** |  |  | AssetSetIdentifier: string*; AssetVersionIdentifier: string; DerivationTypeIdentifier: string |
| **AssetDerivationsRequestDto** |  |  | AssetSetIdentifiers: IList<string>*; AssetTypeDerivationTypes: IDictionary<string, IList<string>> |
| **AssetDerivationsResponseDto** |  |  | DerivationsByAsset: IDictionary<string, IList<AssetDerivationDto>> |
| **AssetDto** |  |  | Identifier: string; Status: string; ThumbnailUri: string; AlternativeThumbnailUri: string; WatermarkThumbnailUri: string; FileName: string; AssetType: string; Created: DateTime; Version: long; DisplayProperties: IDictionary<string, string>; Properties: IList<PropertyDto>; CategoryProperties: IList<CategoryPropertiesDto>; ApplicablePermissions: IList<string>; PropertyDefinitionRestrictions: ISet<PropertyDefinitionRestrictionDto>; ParentCategoryIdentifier: string; LinkedCategoryIdentifiers: IList<string> |
| **AssetDuplicateDto** |  |  | AssetSetIdentifier: string; FileName: string; ImportedBy: UserInfoDto; CreatedAt: DateTime; DuplicateAssetSetIdentifier: string; DuplicateFileName: string; DuplicateImportedBy: UserInfoDto; DuplicateCreatedAt: DateTime |
| **AssetPropertyMutationDto** |  |  | AssetSetIdentifier: string; AssetVersionIdentifier: string; AssetVersionFileName: string; AssetVersionNumber: int; PropertyMutations: IList<PropertyMutationResultDto> |
| **AssetSearchQueryDto** | SearchQueryBaseDto |  | SearchInDocumentExtraction: bool; Categories: IList<string>; RecursivelyFilterCategories: bool; Context: string; Filename: string; FileHash: string **[geerbt: SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **AssetSetDerivationFileNameDto** |  |  | AssetSetIdentifier: string; DerivationTypeIdentifier: string; FileName: string |
| **AssetSetDerivationTypeDto** |  |  | Identifier: string*; DerivationTypeIdentifier: string*; OverrideFileNameFormat: string; Path: string |
| **AssetSetFinalizationResultDto** |  |  | FinalizedAssetSetIdentifiers: IList<string>; ValidateAssetSetPropertiesResult: IList<ValidateAssetSetPropertiesResultDto> |
| **AssetSetPropertyValidationResultDto** |  |  | PropertyDefinitionIdentifier: string; IsValid: bool |
| **AssetSetRelationsDto** |  |  | AssetSetIdentifier: string; Relations: IList<RelationDto> |
| **AssetSetRelationsQueryDto** | RelationsSearchQueryDto |  | AssetSetIdentifiers: IList<string>* **[geerbt: IncludePropertyDefinitionIdentifiers: IList<string>; IncludeRelationTypeIdentifiers: IList<string>; IncludeEntityTypes: IList<string>; IncludePaths: bool?]** |
| **AssetTasksDto** |  |  | TasksByAssetSetIdentifier: IDictionary<string, IList<TaskDto>> |
| **AssetTypeDerivationTypesDto** |  |  | AssetTypeIdentifier: string*; DerivationTypeIdentifiers: IList<string>* |
| **AssetTypeDto** |  |  | Identifier: string; DerivationTypeSettings: IList<DerivationTypeSettingsDto> |
| **AssetTypeSettingsDto** |  |  | AssetTypeIdentifier: string; IsOnDemand: bool; IsDefaultDerivation: bool; AllowManualUpload: bool; ShowInDownloads: bool; UseAsThumbnail: bool; UseAsPreview: bool |
| **AssetVersionDto** |  |  | Identifier: string; AssetSetIdentifier: string; Created: DateTime; FileName: string; Version: long; ThumbnailUri: string; AssetType: string |
| **BillingContactDto** |  |  | Identifier: string; Name: string; EmailAddress: string; Status: EntityStatusDto; CreatedAt: DateTime; UpdatedAt: DateTime; CreatedBy: string; UpdatedBy: string; RightsOwners: IList<RightsOwnerDto>; Properties: IList<PropertyDto>; ApplicablePermissions: IList<string> |
| **BillingContactSearchQueryDto** | SearchQueryBaseDto |  | **[geerbt: SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **BuyoutDto** |  |  | Identifier: string; Description: string; Basis: double; Currency: CurrencyDto; AdditionalFeePercentage: double; FromDate: DateTime; ToDate: DateTime; RightsOwner: RightsOwnerDto; PriceList: PriceListDto; CreatedAt: DateTime; CreatedBy: string; UpdatedAt: DateTime; UpdatedBy: string; RowVersion: byte[] |
| **CategoryDto** |  |  | Identifier: string*; Name: IList<TranslatedValueDto>*; TenantIdentifier: string; HasSubCategories: bool*; SubCategories: IList<CategoryDto>; ApplicablePermissions: IList<string>; PropertyDefinitionRestrictions: ISet<PropertyDefinitionRestrictionDto>; ParentCategoryIdentifier: string |
| **CategoryPropertiesDto** |  |  | CategoryIdentifier: string; Name: IList<TranslatedValueDto>; Properties: IList<PropertyDto> |
| **CategorySearchQueryDto** | SearchQueryBaseDto |  | **[geerbt: SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **CategorySortOrderDto** |  |  | BeforeCategoryIdentifier: string; AfterCategoryIdentifier: string |
| **ChangePasswordDto** |  |  | OldPassword: string; Password: string |
| **CommentDto** | CreateOrUpdateCommentDto |  | Identifier: string; RowVersion: byte[]; CreatedBy: UserInfoDto; CreatedAt: string; ApplicablePermissions: IList<string> **[geerbt: Text: string]** |
| **ConfigurationValueDto** |  |  | Path: string; Value: string |
| **ConfigurationValueExtendedDto** |  |  | Path: string; Value: string; ConfigurationProvider: ConfigurationProviderDto |
| **ContactPrintDto** |  |  | AssetSetIdentifiers: IList<string>; Format: ContactPrintFormatDto; DataLanguages: IList<string>; LangFallback: bool? |
| **ContextGroupIdentifierDto** |  |  | Identifier: string; Type: ContextGroupTypeDto |
| **ContextPercentageDto** |  |  | UserContextGroup: UserContextGroupDto; Percentage: double |
| **CopyAllPropertiesDto** |  |  | AssetSetIdentifiers: IList<string>; PropertyActionStrategy: PropertyActionStrategyDto |
| **CopyCategoryDto** | CreateCategoryDto |  | DestinationParentCategoryIdentifier: string; SortOrder: CategorySortOrderDto **[geerbt: Identifier: string; Name: IList<TranslatedValueDto>; Properties: IList<SetPropertyDto>]** |
| **CopyPropertiesDto** |  |  | AssetSetIdentifiers: IList<string>; CopyPropertiesStrategy: IList<CopyPropertiesStrategyDto> |
| **CopyPropertiesFromEntityDto** |  |  | EntityIdentifier: string; EntityTypeIdentifier: string; PropertyDefinitionIdentifiers: IList<string>; CopyAllProperties: bool |
| **CopyPropertiesStrategyDto** |  |  | PropertyActionStrategy: PropertyActionStrategyDto; PropertyDefinitionIdentifier: string |
| **CreateAssetDerivationDto** |  |  | DerivationTypeIdentifier: string*; FileName: string* |
| **CreateAssetDerivationResponseDto (obsolete)** |  |  | DerivationIdentifier: string* |
| **CreateAssetDto (obsolete)** |  |  | FileName: string*; ParentCategoryIdentifier: string*; ExtractCreatedArchiveAsset: ExtractCreatedArchiveAssetDto |
| **CreateAssetResponseDto (obsolete)** |  |  | AssetIdentifier: string* |
| **CreateAssetVersionDto (obsolete)** |  |  | FileName: string* |
| **CreateAssetVersionResponseDto (obsolete)** |  |  | AssetVersionIdentifier: string* |
| **CreateBillingContactDto** |  |  | Identifier: string; Name: string; EmailAddress: string; Status: EntityStatusDto; Properties: IList<SetPropertyDto> |
| **CreateBuyoutDto** |  |  | Identifier: string; Description: string; Basis: double; CurrencyIdentifier: string; AdditionalFeePercentage: double; FromDate: DateTime; ToDate: DateTime; RightsOwnerIdentifier: string; PriceListIdentifier: string |
| **CreateCategoryDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; Properties: IList<SetPropertyDto> |
| **CreateCustomEntityDto** |  |  | Identifier: string; EntityTypeIdentifier: string; Name: IList<TranslatedValueDto>; Properties: IList<SetPropertyDto> |
| **CreateDerivationDto** |  |  | Asset: AssetDerivationTypeDto*; CategoryIdentifier: string*; CropData: CropDataDto |
| **CreateDerivationJobDefinitionDto** |  |  | Identifier: string; DerivationTypeIdentifier: string; OutputFileTypeIdentifier: string; InputFileTypeIdentifiers: IList<string>; JobDefinition: string |
| **CreateDerivationTypeDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>*; AssetTypeSettings: IList<AssetTypeSettingsDto>* |
| **CreateEntityTypeDto** |  |  | Identifier: string; TenantIdentifier: string; Name: IList<TranslatedValueDto>; Description: IList<TranslatedValueDto>; Status: EntityStatusDto |
| **CreateFileDto** |  |  | Identifier: string; FileUsageType: FileUsageTypeDto |
| **CreateFileNamingDefinitionDto** | UpdateFileNamingDefinitionDto |  | Identifier: string **[geerbt: Name: IList<TranslatedValueDto>; Tenants: IList<FileNamingDefinitionTenantDto>; IsGlobalDefault: bool; Format: string; CharacterCasing: CharacterCasingDto; ReplaceIllegalCharacters: bool; AbsentPropertyStrategy: AbsentPropertyStrategyDto]** |
| **CreateFileResponseDto** |  |  | FileIdentifier: string |
| **CreateLicenseDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; Description: IList<TranslatedValueDto>; Status: EntityStatusDto; PeriodStart: DateTime; PeriodEnd: DateTime; Properties: IList<SetPropertyDto> |
| **CreateOrReplacePropertyDto** |  |  | PropertyActionStrategy: PropertyActionStrategyDto?; PropertyDefinitionIdentifier: string; PropertyValues: IList<MassEditPropertyValueDto> |
| **CreateOrUpdateCommentDto** |  |  | Text: string |
| **CreateOrUpdateExternalGroupDto** |  |  | ExternalGroupName: string; ExternalGroupDisplayName: string; UserGroupIdentifiers: IList<string> |
| **CreateOrUpdatePropertyListItemDto** |  |  | Identifier: string; SortKey: int?; Value: IList<TranslatedValueDto> |
| **CreateOrUpdateUserSettingsDto** |  |  | DisplayLanguage: string; Locale: string; DataLanguageIdentifier: string; UiSettings: string; UserContextIdentifier: string; AvatarUrl: string; SearchTermOperator: MultiTermOperatorDto? |
| **CreatePriceListDto** |  |  | Identifier: string; ContextPercentages: IList<SetContextPercentageDto>; Name: string; Description: string; Status: EntityStatusDto |
| **CreatePropertyDefinitionDto** | UpdatePropertyDefinitionDto |  | Identifier: string; IsList: bool; ValueType: PropertyValueTypeDto; Translatable: bool **[geerbt: PropertyGroupIdentifier: string; AllowMultipleValues: bool; AllowCustomListValue: bool; Active: bool; Editable: bool; IgnoreCopying: bool; IsSearchableFacet: bool; SearchOption: SearchOptionDto; Name: IList<TranslatedValueDto>; Description: IList<TranslatedValueDto>; ListItems: IList<CreateOrUpdatePropertyListItemDto>; EntityTypeIdentifiers: IList<string>; IsFileNamingUnlocked: bool; HasUserContext: bool; DefinitionType: PropertyDefinitionTypeDto; SortKey: int?]** |
| **CreatePropertyDto** |  |  | PropertyDefinitionIdentifier: string; PropertyValues: IList<PropertyValueDto>; InheritToNodeTypeIdentifiers: IList<string> |
| **CreatePropertyExtractionDefinitionDto** |  |  | Identifier: string; ExtractionType: PropertyExtractionTypeDto; InputDerivationTypeIdentifier: string; InputAssetTypeIdentifiers: ISet<string>; ExtractionSources: IList<PropertyExtractionSourceDto>; Status: EntityStatusDto? |
| **CreatePropertyGroupDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; Active: bool; SortKey: int? |
| **CreatePropertyMassEditDto** |  |  | AssetSetIdentifiers: IList<string>; Properties: IList<CreateOrReplacePropertyDto> |
| **CreatePropertyMassEditSearchQueryDto** | AssetSearchQueryDto |  | Properties: IList<CreateOrReplacePropertyDto> **[geerbt: SearchInDocumentExtraction: bool; Categories: IList<string>; RecursivelyFilterCategories: bool; Context: string; Filename: string; FileHash: string; SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **CreatePropertySetDto** | UpdatePropertySetDto |  | EntityTypeIdentifier: string **[geerbt: Name: string; IsGlobal: bool; IsDefault: bool; PropertySetConditions: IList<PropertySetConditionDto>; PropertySetPropertyDefinitions: IList<PropertySetPropertyDefinitionDto>]** |
| **CreatePropertyValueRestrictionDto** |  |  | Actions: IList<AuthorizableActionDto>; Property: CreatePropertyDto |
| **CreatePurchaseDto** |  |  | StartDate: DateTime; ContextGroupIdentifier: ContextGroupIdentifierDto; LicenseIdentifier: string |
| **CreateRelationDto** |  |  | ToEntityIdentifier: string; ToEntityTypeIdentifier: string; RelationTypeIdentifier: string |
| **CreateRelationResultDto** |  |  | Identifier: string; RowVersion: byte[]; Reason: CreateRelationResultReasonDto; FromEntityIdentifier: string; FromEntityTypeIdentifier: string; ToEntityIdentifier: string; ToEntityTypeIdentifier: string; RelationTypeIdentifier: string |
| **CreateRelationTypeDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>*; Description: IList<TranslatedValueDto>; Status: EntityStatusDto*; IsDirected: bool; IsUsedForAssetSearch: bool |
| **CreateResourcePermissionDto** | UpdateResourcePermissionDto |  | Identifier: string **[geerbt: UserGroupIdentifier: string; UserIdentifier: string; RoleIdentifier: string]** |
| **CreateRightsOwnerDto** |  |  | Identifier: string; Name: string; Status: EntityStatusDto; BillingContactIdentifier: string; Properties: IList<SetPropertyDto> |
| **CreateRoleDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>*; Status: EntityStatusDto*; ObjectTypes: IList<ObjectTypeDto>; PropertyEditEntityLimit: int?; PropertyExportEntityLimit: int? |
| **CreateRuleActionDto** |  |  | Property: CreatePropertyDto; PropertyExtractionDefinitionIdentifier: string; AdditionalData: string; Template: string; OnTriggerMatched: bool* |
| **CreateRuleDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>*; NodeTypeIdentifiers: IList<string>; Filters: IList<CreateRuleTriggerDto>; Triggers: IList<CreateRuleTriggerDto>*; Actions: IList<CreateRuleActionDto>* |
| **CreateRuleTriggerDto** |  |  | TriggerOperator: TriggerOperatorDto*; Property: CreatePropertyDto; AdditionalData: string |
| **CreateSavedSearchDto** | UpdateSavedSearchDto |  | Identifier: string **[geerbt: Name: string; SearchQuery: string]** |
| **CreateShareDto** |  |  | Name: string*; AssetTypeDerivations: IDictionary<string, IList<string>>*; ValidUntil: DateTime?*; EmailRecipients: IList<string> |
| **CreateShareRecipientDto** |  |  | EmailAddress: string* |
| **CreateStateDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>*; PreviousStateIdentifiers: IList<string>*; NextStateIdentifiers: IList<string>*; SortOrder: EntitySortOrderDto |
| **CreateTaskDto** |  |  | Name: string*; Description: string; DueDate: DateTime?; Identifier: string; TaskType: string; AssignedUserIdentifiers: IList<string>; StateIdentifier: string; Properties: IList<SetPropertyDto> |
| **CreateUserContextDto** | UpdateUserContextDto |  | Identifier: string **[geerbt: Name: IList<TranslatedValueDto>; SequenceNr: int; IsDefault: bool]** |
| **CreateUserContextGroupDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; UserContextIdentifiers: IList<string> |
| **CreateUserDto** |  |  | UserName: string; FirstName: string; LastName: string; EmailAddress: string; Password: string; UserSettings: CreateOrUpdateUserSettingsDto; Status: EntityStatusDto; ContactPerson: string; Company: string; PhoneNumber: string; AccessType: AccessTypeDto |
| **CreateUserGroupDto** |  |  | Name: string; Description: string |
| **CreateUserGroupRoleMapDto** |  |  | UserGroupIdentifier: string; RoleIdentifier: string |
| **CreateWatermarkDefinitionDto** | UpdateWatermarkDefinitionDto |  | Identifier: string **[geerbt: DisplayInUI: bool; DerivationJobDefinitionIdentifier: string; PropertyDefinitionIdentifier: string]** |
| **CreateWatermarkTriggerDto** | UpdateWatermarkTriggerDto |  | **[geerbt: PropertyListValueIdentifier: string; ValueBool: bool; FileReferenceIdentifier: string]** |
| **CreateWorkspaceAssetDto (obsolete)** | CreateAssetDto |  | TaskIdentifier: string **[geerbt: FileName: string; ParentCategoryIdentifier: string; ExtractCreatedArchiveAsset: ExtractCreatedArchiveAssetDto]** |
| **CreateWorkspaceDto** | UpdateWorkspaceDto |  | Identifier: string; IsTemplate: bool; WorkspaceType: WorkspaceTypeDto; StateIdentifier: string; Properties: IList<SetPropertyDto> **[geerbt: Name: IList<TranslatedValueDto>; Status: EntityStatusDto; PropertySetIdentifier: string; FinalizationCategoryIdentifier: string; CopyPropertiesToAssets: bool]** |
| **CurrencyDto** |  |  | Identifier: string; ShortName: string |
| **CustomEntityDto** |  |  | Identifier: string; EntityTypeIdentifier: string; CreatedAt: DateTime; Name: IList<TranslatedValueDto>; ApplicablePermissions: IList<string>; ThumbnailUris: IList<string>; Properties: IList<PropertyDto> |
| **CustomEntitySearchQueryDto** | SearchQueryBaseDto |  | EntityTypeIdentifier: string **[geerbt: SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **DataLanguageDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; Default: bool; Status: EntityStatusDto |
| **DeleteAssetsDto** |  |  | AssetSetIdentifiers: IList<string>* |
| **DeleteConfigurationValuesDto** |  |  | Paths: IList<string> |
| **DeletePropertiesDto** |  |  | PropertyDefinitionIdentifiers: IList<string> |
| **DeletePropertyResultDto** |  |  | PropertyDefinitionIdentifier: string; IsSuccess: bool; Reason: DeletePropertyResultReasonDto |
| **DeleteRelationsDto** |  |  | RelationIdentifiers: IList<string>*; RecycleBin: bool |
| **DeleteResourcePermissionsDto** |  |  | ResourcePermissionIdentifiers: IList<string> |
| **DerivationJobDefinitionDto** |  |  | Identifier: string; DerivationTypeIdentifier: string; InputFileTypeIdentifiers: IList<string>; OutputFileTypeIdentifier: string; JobDefinition: string; IsSystem: bool |
| **DerivationRestartDto** |  |  | AssetSetIdentifiers: IList<string>*; AssetTypeDerivationTypes: IDictionary<string, IList<string>>; RestartFailedDerivations: bool |
| **DerivationRestartSearchQueryDto** | AssetSearchQueryDto |  | AssetTypeDerivationTypes: IDictionary<string, IList<string>>; RestartFailedDerivations: bool **[geerbt: SearchInDocumentExtraction: bool; Categories: IList<string>; RecursivelyFilterCategories: bool; Context: string; Filename: string; FileHash: string; SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **DerivationStatusDetailsDto** |  |  | AssetSetIdentifier: string; AssetTypeIdentifier: string; DerivationTypeIdentifier: string; FileName: string; Status: AssetDerivationStatusDto; RetryCount: int; CreatedAt: DateTime |
| **DerivationStatusOverviewDto** |  |  | PendingCount: int; FailedCount: int |
| **DerivationStatusTypesDto** |  |  | AssetTypeIdentifier: string; DerivationTypeIdentifier: string; Status: AssetDerivationStatusDto; Count: int |
| **DerivationTypeDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; IsSystem: bool; AssetTypeSettings: IList<AssetTypeSettingsDto> |
| **DerivationTypeRestrictionDto** |  |  | Action: AuthorizableActionDto; DerivationTypeIdentifier: string; AssetTypeIdentifier: string |
| **DerivationTypeSettingsDto** |  |  | DerivationTypeIdentifier: string; DerivationName: IList<TranslatedValueDto>; IsOnDemand: bool; IsDefaultDerivation: bool; AllowManualUpload: bool; ShowInDownloads: bool; UseAsThumbnail: bool; UseAsPreview: bool |
| **EntityDto** |  |  | Identifier: string; ObjectType: ObjectTypeDto |
| **EntityExtendedDto** |  |  | Identifier: string; EntityTypeIdentifier: string; ThumbnailUri: string; Properties: IList<PropertyDto> |
| **EntityIdentifierDto** |  |  | Identifier: string; EntityTypeIdentifier: string |
| **EntityIdentifiersDto** |  |  | EntityIdentifiers: IList<EntityIdentifierDto> |
| **EntitySortOrderDto** |  |  | BeforeEntity: EntityDto; AfterEntity: EntityDto |
| **EntityTypeDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; Description: IList<TranslatedValueDto>; Status: EntityStatusDto; IsSystem: bool |
| **EntityTypeMappingDto** |  |  | FromEntityTypeIdentifier: string; ToEntityTypeIdentifier: string |
| **ExecuteRulesDto** |  |  | EntityTypeIdentifiers: IList<string>; EntityIdentifiers: IList<string>; RuleIdentifiers: IList<string> |
| **ExecuteTaskStatusTransitionDto** |  |  | TransitionIdentifier: string |
| **ExternalGroupDto** |  |  | Identifier: string; ExternalGroupName: string; ExternalGroupDisplayName: string; UserGroupIdentifiers: IList<string> |
| **ExtractCreatedArchiveAssetDto** |  | Used to configure whether an archive asset should be extracted and optionally be deleted after the extraction | ArchiveExtractionMode: ArchiveExtractionModeDto*; DeleteAssetAfterExtraction: bool* |
| **ExtractFromAssetSearchQueryDto** | AssetSearchQueryDto |  | PropertyExtractionDefinitionIdentifier: string; Import: bool **[geerbt: SearchInDocumentExtraction: bool; Categories: IList<string>; RecursivelyFilterCategories: bool; Context: string; Filename: string; FileHash: string; SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **FacetDto** |  |  | PropertyDefinitionIdentifier: string; Items: IList<FacetItemDto> |
| **FacetFilterDto** |  |  | PropertyDefinitionIdentifier: string; Terms: string[]; Operator: FacetOperatorDto? |
| **FacetItemDto** |  |  | Term: string; Count: int |
| **FieldErrorDto** |  |  | Field: string; Message: string |
| **FileDto** |  |  | FileIdentifier: string; FileName: string; FileUsageType: FileUsageTypeDto; Uri: string; DownloadUri: string |
| **FileNamingDefinitionDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; Format: string; CharacterCasing: CharacterCasingDto; ReplaceIllegalCharacters: bool; AbsentPropertyStrategy: AbsentPropertyStrategyDto; Tenants: IList<FileNamingDefinitionTenantDto>; IsGlobalDefault: bool; CreatedBy: UserInfoDto |
| **FileNamingDefinitionTenantDto** |  |  | TenantIdentifier: string; IsDefault: bool |
| **FileNamingTokenDto** |  |  | FileNamingTokenType: FileNamingTokenTypeDto; Identifier: string; Description: IList<TranslatedValueDto> |
| **FileTypeDto** |  |  | Identifier: string; AssetTypeIdentifier: string; FileEnding: string; MimeType: string |
| **FilterDto** |  | Filter <see cref="Terms"/> on property with identifier <see cref="PropertyDefinitionIdentifier"/> with <see cref="OperatorDto"/>. | PropertyDefinitionIdentifier: string; EntityTypeIdentifier: string; Terms: IList<string>; Operator: OperatorDto?; LanguageIdentifiers: IList<string> |
| **FinalizeAssetSetsDto** |  |  | AssetSetIdentifiers: IList<string>; TargetCategoryIdentifier: string |
| **FinalizeUserRegistrationDto** |  |  | Approve: bool; UserGroupIdentifiers: IList<string> |
| **IdentifierRowVersionDto** | RowVersionDto |  | Identifier: string **[geerbt: RowVersion: byte[]]** |
| **LicenseDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; Description: IList<TranslatedValueDto>; Status: EntityStatusDto; PeriodStart: DateTime; PeriodEnd: DateTime; CreatedAt: DateTime; UpdatedAt: DateTime; CreatedBy: string; UpdatedBy: string; Properties: IList<PropertyDto>; ApplicablePermissions: IList<string>; PurchaseDurationInDays: int |
| **LicenseOfferDto** |  |  | ThumbnailUri: string; License: LicenseDto; RightsOwners: IList<RightsOwnerDto>; ContextGroup: UserContextGroupDto; AlternativeGroups: IList<UserContextGroupDto>; ValidFrom: DateTime; ValidUntil: DateTime; Prices: IList<LicensePriceDto>; IncludedAssetsCount: int |
| **LicenseOfferSearchQueryDto** |  |  | SearchTerm: string; ContextIdentifiers: IList<ContextGroupIdentifierDto>; ValidFrom: DateTime?; ValidUntil: DateTime?; RightsIdentifiers: IList<string>; PurchasedForContext: string; LicenseIdentifiers: IList<string>; EntityIdentifiers: IList<EntityIdentifierDto> |
| **LicensePriceDto** |  |  | Price: double; Currency: CurrencyDto |
| **LicensePurchaseDto** |  |  | Identifier: string; OrderId: long; PurchaseDate: DateTime; StartDate: DateTime; ExpiryDate: DateTime; Prices: IList<LicensePriceDto>; ContextGroup: UserContextGroupDto; PurchasedBy: UserInfoDto; License: LicenseDto; IncludedEntities: IList<EntityIdentifierDto>; IncludedAssetsCount: int; Properties: IList<PropertyDto> |
| **LicensePurchaseSearchQueryDto** | SearchQueryBaseDto |  | IncludedEntities: IList<EntityIdentifierDto> **[geerbt: SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **LicenseSearchQueryDto** | SearchQueryBaseDto |  | **[geerbt: SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **MassEditPropertyValueDto** |  |  | PropertyActionStrategy: PropertyActionStrategyDto?; PropertyListItemIdentifier: string; PropertyValue: IList<MassEditTranslatedValueDto>; UserContextIdentifier: string |
| **MassEditTranslatedValueDto** | TranslatedValueDto |  | PropertyActionStrategy: PropertyActionStrategyDto? **[geerbt: DataLanguageIdentifier: string; Value: string]** |
| **MetadataExtractionDto** |  |  | AssetSetIdentifiers: IList<string> |
| **MetadataExtractionV6Dto** |  |  | AssetSetIdentifiers: IList<string>; PropertyExtractionDefinitionIdentifier: string; Import: bool |
| **MoveAssetsToCategoryDto** |  |  | AssetSetIdentifiers: IList<string>; FromParentCategoryIdentifier: string |
| **MoveCategoryDto** |  |  | SourceParentCategoryIdentifier: string; DestinationParentCategoryIdentifier: string; SortOrder: CategorySortOrderDto |
| **MoveEntityDto** |  |  | SourceParentEntity: EntityDto*; DestinationParentEntity: EntityDto*; SortOrder: EntitySortOrderDto |
| **ObjectTypePermissionDto** |  |  | Identifier: string*; Permission: PermissionDto; Description: string |
| **PathSegmentDto** |  |  | Entity: EntityDto; Name: IList<TranslatedValueDto> |
| **PermissionMaterializationDto** |  |  | Identifier: string; InitiatedBy: UserInfoDto; CreatedAt: DateTime; FinishedAt: DateTime?; Status: JobStatusDto; Operations: IList<PermissionMaterializationOperationDto> |
| **PermissionMaterializationOperationDto** |  |  | JobType: BackgroundJobTriggerTypeDto; AffectedEntityIdentifier: EntityIdentifierDto |
| **PriceListDto** |  |  | Identifier: string; Name: string; Description: string; ContextPercentages: IList<ContextPercentageDto>; Status: EntityStatusDto; CreatedAt: DateTime; UpdatedAt: DateTime; CreatedBy: string; UpdatedBy: string; ApplicablePermissions: IList<string> |
| **PropertyDefinitionDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; Description: IList<TranslatedValueDto>; PropertyGroupIdentifier: string; ValueType: PropertyValueTypeDto; AllowMultipleValues: bool; AllowCustomListValue: bool; Active: bool; Editable: bool; Translatable: bool; IgnoreCopying: bool; IsSystem: bool; IsSearchableFacet: bool; SearchOption: SearchOptionDto; ListItems: IList<PropertyListItemDto>; EntityTypeIdentifiers: IList<string>; IsFileNamingUnlocked: bool; HasUserContext: bool; IsUrl: bool~; DefinitionType: PropertyDefinitionTypeDto; SortKey: int?; IsList: bool; BlacklistedFields: IList<PropertyDefinitionFieldsDto> |
| **PropertyDefinitionRestrictionDto** |  |  | Action: AuthorizableActionDto; PropertyDefinitionIdentifier: string |
| **PropertyDto** |  |  | Identifier: string; PropertyDefinitionIdentifier: string; PropertyValues: IList<PropertyValueDto>; InheritToNodeTypeIdentifiers: IList<string>; RowVersion: byte[] |
| **PropertyEntityCreationResultDto** | IdentifierRowVersionDto |  | PropertyResults: IList<SetPropertyResultDto> **[geerbt: Identifier: string; RowVersion: byte[]]** |
| **PropertyExportDto** |  |  | PropertyExportIdentifiers: IList<PropertyExportIdentifierDto>*; Options: PropertyExportOptionsDto*; PropertyDefinitionIdentifiers: IList<string>* |
| **PropertyExportIdentifierDto** |  |  | EntityIdentifier: string*; EntityVersion: string |
| **PropertyExportOptionsDto** |  |  | FileFormat: PropertyExportFileFormatDto*; LabelDataLanguageIdentifier: string*; LocaleIdentifier: string; PropertyDataLanguageIdentifiers: IList<string>*; PropertyUserContextIdentifiers: IList<string> |
| **PropertyExtractionDefinitionDto** |  |  | Identifier: string; RowVersion: byte[]; IsSystem: bool; ExtractionType: PropertyExtractionTypeDto; InputDerivationTypeIdentifier: string; InputAssetTypeIdentifiers: ISet<string>; ExtractionSources: IList<PropertyExtractionSourceDto>; CreatedAt: DateTime; Status: EntityStatusDto |
| **PropertyExtractionJobAssetResultDto** |  |  | AssetDerivationIdentifier: string; AssetSetIdentifier: string; AssetVersionIdentifier: string; AssetVersionNumber: long; FileName: string; Status: PropertyExtractionJobAssetResultStatusDto; Extracted: IList<PropertyExtractionSourceResultDto> |
| **PropertyExtractionJobDto** |  |  | Identifier: string; RowVersion: byte[]; PropertyExtractionDefinitionIdentifier: string; ImportAfterExtraction: bool; Status: JobStatusDto; CreatedBy: UserInfoDto; CreatedAt: DateTime; InitiatedAt: DateTime?; FinishedAt: DateTime?; TotalAssetCount: int; FailedAssetCount: int; ExtractedAssetCount: int; ImportedAssetCount: int; ApplicablePermissions: IList<string> |
| **PropertyExtractionMappingDto** |  |  | FromValue: string; ToPropertyDefinitionIdentifier: string; ToPropertyListValueIdentifier: string |
| **PropertyExtractionResultDto** |  |  | ExtractedValue: string; Confidence: double; ToPropertyDefinitionIdentifier: string; ToPropertyListValueIdentifier: string; Status: PropertyExtractionResultStatusDto |
| **PropertyExtractionSourceDto** |  |  | Provider: PropertyExtractionProviderDto; CustomModelId: string; Language: string; ConfidenceThreshold: double?; Mappings: IList<PropertyExtractionMappingDto> |
| **PropertyExtractionSourceResultDto** |  |  | PropertyExtractionProvider: PropertyExtractionProviderDto; ConfidenceThreshold: double?; Status: PropertyExtractionSourceStatusDto; StatusMessage: string; Properties: IList<PropertyExtractionResultDto> |
| **PropertyGroupDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; Active: bool; SortKey: int; PropertyDefinitionsCount: int |
| **PropertyImportDto** |  |  | PropertyImportIdentifier: string; FileName: string; DataId: string; Status: JobStatusDto; CreatedBy: string; CreatedAt: DateTime; InitiatedAt: DateTime?; FinishedAt: DateTime?; MimeType: string; FileUrl: string; TotalPropertyMutationCount: int; FailedPropertyMutationCount: int; SkippedPropertyMutationCount: int; SucceededPropertyMutationCount: int; Messages: IList<PropertyImportMessageDto>; ImportEntities: IList<PropertyImportEntityDto> |
| **PropertyImportEntityDto** |  |  | EntityIdentifier: string; EntityTypeIdentifier: string; VersionIdentifier: string; VersionFileName: string; VersionNumber: long; PropertyMutations: IList<PropertyImportMutationDto> |
| **PropertyImportMessageDto** |  | PropertyImport source file validation / parsing error messages | MessageType: PropertyImportMessageTypeDto; Severity: PropertyImportMessageSeverityDto; Result: string; RowNumber: long?; ColumnNumber: long? |
| **PropertyImportMutationDto** |  |  | PropertyDefinitionIdentifier: string; Result: string; Success: bool |
| **PropertyListItemDto** |  |  | Identifier: string; SortKey: int?; Value: IList<TranslatedValueDto>; Status: EntityStatusDto; IsSystemListValue: bool |
| **PropertyMassEditDto** |  |  | Identifier: string; Status: JobStatusDto; AssetSetIdentifiers: IList<string>; Properties: IList<CreateOrReplacePropertyDto>; CreatedAt: DateTime; InitiatedAt: DateTime?; FinishedAt: DateTime?; TotalPropertyMutationCount: int; FailedPropertyMutationCount: int; SucceededPropertyMutationCount: int; TotalAssetCount: long; ApplicableAssetCountLimit: int? |
| **PropertyMassEditReportDto** |  |  | PropertyMassEditIdentifier: string; Status: JobStatusDto; AssetPropertyMutations: IList<AssetPropertyMutationDto>; TotalPropertyMutationCount: int; FailedPropertyMutationCount: int; SucceededPropertyMutationCount: int; CreatedAt: DateTime; InitiatedAt: DateTime?; FinishedAt: DateTime?; TotalAssetCount: long; ApplicableAssetCountLimit: int? |
| **PropertyMassEditReportV4Dto** |  |  | Identifier: string; Status: JobStatusDto; Properties: IList<CreateOrReplacePropertyDto>; CreatedAt: DateTime; InitiatedAt: DateTime?; FinishedAt: DateTime?; TotalPropertyMutationCount: int; FailedPropertyMutationCount: int; SucceededPropertyMutationCount: int; TotalAssetCount: long; ApplicableAssetCountLimit: int? |
| **PropertyMassEditV4Dto** |  |  | Identifier: string; Status: JobStatusDto; CreatedAt: DateTime; InitiatedAt: DateTime?; FinishedAt: DateTime?; TotalPropertyMutationCount: int; FailedPropertyMutationCount: int; SucceededPropertyMutationCount: int; TotalAssetCount: long; ApplicableAssetCountLimit: int? |
| **PropertyMutationResultDto** |  |  | PropertyDefinitionIdentifier: string; PropertyIdentifier: string; Result: string; Success: bool; ValueMutations: IList<PropertyValueMutationDto> |
| **PropertySetConditionDto** |  |  | PropertyDefinitionIdentifier: string; PropertyListValueIdentifier: string |
| **PropertySetDto** |  |  | Identifier: string; Name: string; CreatedBy: UserInfoDto; IsGlobal: bool; IsDefault: bool; EntityTypeIdentifier: string; PropertySetPropertyDefinitions: IList<PropertySetPropertyDefinitionDto>; PropertySetConditions: IList<PropertySetConditionDto>; ApplicablePermissions: IList<string> |
| **PropertySetPropertyDefinitionDto** |  |  | PropertyDefinitionIdentifier: string; FillMode: FillModeDto |
| **PropertyValueDto** |  |  | Identifier: string; PropertyListItemIdentifier: string; PropertyValue: IList<TranslatedValueDto>; UserContextIdentifier: string |
| **PropertyValueMutationDto** |  |  | UserContextIdentifier: string; LanguageIdentifier: string; Result: string; Success: bool |
| **PropertyValueRestrictionDto** |  |  | Property: PropertyDto; Actions: IList<AuthorizableActionDto> |
| **PublicShareDto** |  |  | Name: string; Derivations: IList<ShareDerivationDto> |
| **QuickSearchResultDto<T> (abstract)** |  | For quick search retrieval of any entity such as Assets, Categories, etc.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation. | Items: IList<T>; AutoCompleteSuggestions: IList<string> |
| **QuickSearchResultOfAssetDto** | QuickSearchResultDto<AssetDto> | For quick search retrieval of Asset entities.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation. | **[geerbt: Items: IList<AssetDto>; AutoCompleteSuggestions: IList<string>]** |
| **QuickSearchResultOfBillingContactDto** | QuickSearchResultDto<BillingContactDto> |  | **[geerbt: Items: IList<BillingContactDto>; AutoCompleteSuggestions: IList<string>]** |
| **QuickSearchResultOfCategoryDto** | QuickSearchResultDto<CategoryDto> | For quick search retrieval of Category entities.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation. | **[geerbt: Items: IList<CategoryDto>; AutoCompleteSuggestions: IList<string>]** |
| **QuickSearchResultOfCustomEntityDto** | QuickSearchResultDto<CustomEntityDto> | For quick search retrieval of Custom entities.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation. | **[geerbt: Items: IList<CustomEntityDto>; AutoCompleteSuggestions: IList<string>]** |
| **QuickSearchResultOfLicenseDto** | QuickSearchResultDto<LicenseDto> |  | **[geerbt: Items: IList<LicenseDto>; AutoCompleteSuggestions: IList<string>]** |
| **QuickSearchResultOfLicensePurchaseDto** | QuickSearchResultDto<LicensePurchaseDto> |  | **[geerbt: Items: IList<LicensePurchaseDto>; AutoCompleteSuggestions: IList<string>]** |
| **QuickSearchResultOfRightsOwnerDto** | QuickSearchResultDto<RightsOwnerDto> |  | **[geerbt: Items: IList<RightsOwnerDto>; AutoCompleteSuggestions: IList<string>]** |
| **QuickSearchResultOfTaskDto** | QuickSearchResultDto<TaskDto> | For quick search retrieval of Task entities.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation. | **[geerbt: Items: IList<TaskDto>; AutoCompleteSuggestions: IList<string>]** |
| **QuickSearchResultOfWorkspaceDto** | QuickSearchResultDto<WorkspaceDto> | For quick search retrieval of Workspace entities.  Due to performance reasons not all of the fields will be filled. It shall be decided by the endpoint implementation. | **[geerbt: Items: IList<WorkspaceDto>; AutoCompleteSuggestions: IList<string>]** |
| **ReIndexEntityTypeDto** |  |  | EntityTypeIdentifiers: IList<string> |
| **ReIndexingJobDto** |  |  | Identifier: string; InitiatedBy: string; InitiatedAt: DateTime; FinishedAt: DateTime?; EntityTypes: IList<string>; Status: JobStatusDto; FailureReason: string; TotalEntitiesCount: int; SuccessfulEntitiesCount: int; FailedEntitiesCount: int; IsCancelled: bool |
| **ReIndexingJobReportDto** | ReIndexingJobDto |  | FailedEntities: IList<ReportEntityDto> **[geerbt: Identifier: string; InitiatedBy: string; InitiatedAt: DateTime; FinishedAt: DateTime?; EntityTypes: IList<string>; Status: JobStatusDto; FailureReason: string; TotalEntitiesCount: int; SuccessfulEntitiesCount: int; FailedEntitiesCount: int; IsCancelled: bool]** |
| **RelationDto** |  | Represents a directed Relationship between two Nodes | Identifier: string; FromEntity: EntityExtendedDto; ToEntity: EntityExtendedDto; RelationTypeIdentifier: string; PathSegments: IList<PathSegmentDto>; CanDelete: bool |
| **RelationTypeDto** | CreateRelationTypeDto |  | IsSystem: bool; AllowedEntityRelations: IList<EntityTypeMappingDto> **[geerbt: Identifier: string; Name: IList<TranslatedValueDto>; Description: IList<TranslatedValueDto>; Status: EntityStatusDto; IsDirected: bool; IsUsedForAssetSearch: bool]** |
| **RelationsSearchQueryDto** |  |  | IncludePropertyDefinitionIdentifiers: IList<string>; IncludeRelationTypeIdentifiers: IList<string>; IncludeEntityTypes: IList<string>; IncludePaths: bool? |
| **ReportEntityDto** |  |  | Identifier: EntityIdentifierDto; DisplayName: string; BatchNumber: int |
| **ResourcePermissionDto** |  |  | Identifier: string*; UserGroup: UserGroupDto; User: UserInfoDto; RoleIdentifier: string*; IsInherited: bool |
| **ResourcePermissionSettingsDto** |  |  | InterruptInheritance: bool |
| **RightsOwnerDto** |  |  | Identifier: string; Name: string; Status: EntityStatusDto; AvatarUrl: string; BillingContact: BillingContactDto; CreatedAt: DateTime; UpdatedAt: DateTime; CreatedBy: string; UpdatedBy: string; Properties: IList<PropertyDto>; ApplicablePermissions: IList<string> |
| **RightsOwnerSearchQueryDto** | SearchQueryBaseDto |  | **[geerbt: SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **RoleDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>*; Status: EntityStatusDto*; ObjectTypes: IList<ObjectTypeDto>; IsSystem: bool; PropertyEditEntityLimit: int?; PropertyExportEntityLimit: int?; RowVersion: byte[] |
| **RoleSummaryDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>*; Status: EntityStatusDto*; ObjectTypes: IList<ObjectTypeDto>; Permissions: IList<PermissionDto>; DerivationTypeRestrictions: IList<DerivationTypeRestrictionDto>; PropertyDefinitionRestrictions: IList<PropertyDefinitionRestrictionDto>; PropertyValueRestrictions: IList<PropertyValueRestrictionDto>; IsSystem: bool; PropertyEditEntityLimit: int?; PropertyExportEntityLimit: int?; RowVersion: byte[] |
| **RowVersionDto** |  |  | RowVersion: byte[] |
| **RuleActionDto** |  |  | Property: PropertyDto; PropertyExtractionDefinitionIdentifier: string; AdditionalData: string; Template: string; OnTriggerMatched: bool* |
| **RuleDto** | RowVersionDto |  | Identifier: string; Name: IList<TranslatedValueDto>; NodeTypeIdentifiers: IList<string>; Filters: IList<RuleTriggerDto>; Triggers: IList<RuleTriggerDto>; Actions: IList<RuleActionDto> **[geerbt: RowVersion: byte[]]** |
| **RuleExecutionDetailReportDto** |  |  | EntityIdentifier: EntityIdentifierDto; EntityName: string; IsMatchedTrigger: bool; IsMatchedActionExecuted: bool; IsNotMatchedActionExecuted: bool; IsError: bool; ErrorReason: string; MatchedValuesExecuted: IDictionary<string, string>; NotMatchedValuesExecuted: IDictionary<string, string> |
| **RuleExecutionReportDto** |  |  | JobIdentifier: string; Rule: RuleDto; Status: JobStatusDto; CreatedAt: DateTime; InitiatedBy: string; InitiatedAt: DateTime?; FinishedAt: DateTime?; TotalCount: int?; CurrentCount: int?; MatchedTriggerCount: int?; NotMatchedTriggerCount: int?; MatchedActionExecutedCount: int?; NotMatchedActionExecutedCount: int?; ActionSkippedCount: int?; ErrorCount: int? |
| **RuleTriggerDto** |  |  | TriggerOperator: TriggerOperatorDto*; Property: PropertyDto; AdditionalData: string |
| **SavedSearchDto** | CreateSavedSearchDto |  | ApplicablePermissions: IList<string> **[geerbt: Identifier: string; Name: string; SearchQuery: string]** |
| **SearchQueryBaseDto** |  |  | SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto> |
| **SearchResultDto<T> (abstract)** |  | For generic search retrieval of any entity such as Assets, Categories, etc. | Items: IList<T>; Facets: IList<FacetDto> |
| **SearchResultOfAssetDto** | SearchResultDto<AssetDto> |  | **[geerbt: Items: IList<AssetDto>; Facets: IList<FacetDto>]** |
| **SearchResultOfAssetV6Dto** | SearchResultV6Dto<AssetDto> |  | **[geerbt: Cursor: string; Items: IList<T>; Facets: IList<FacetDto>]** |
| **SearchResultOfBillingContactDto** | SearchResultDto<BillingContactDto> |  | **[geerbt: Items: IList<BillingContactDto>; Facets: IList<FacetDto>]** |
| **SearchResultOfBillingContactV6Dto** | SearchResultV6Dto<BillingContactDto> |  | **[geerbt: Cursor: string; Items: IList<T>; Facets: IList<FacetDto>]** |
| **SearchResultOfCustomEntityDto** | SearchResultDto<CustomEntityDto> |  | **[geerbt: Items: IList<CustomEntityDto>; Facets: IList<FacetDto>]** |
| **SearchResultOfCustomEntityV6Dto** | SearchResultV6Dto<CustomEntityDto> |  | **[geerbt: Cursor: string; Items: IList<T>; Facets: IList<FacetDto>]** |
| **SearchResultOfLicenseDto** | SearchResultDto<LicenseDto> |  | **[geerbt: Items: IList<LicenseDto>; Facets: IList<FacetDto>]** |
| **SearchResultOfLicenseV6Dto** | SearchResultV6Dto<LicenseDto> |  | **[geerbt: Cursor: string; Items: IList<T>; Facets: IList<FacetDto>]** |
| **SearchResultOfPurchaseDto** | SearchResultV6Dto<LicensePurchaseDto> |  | **[geerbt: Cursor: string; Items: IList<T>; Facets: IList<FacetDto>]** |
| **SearchResultOfRightsOwnerDto** | SearchResultDto<RightsOwnerDto> |  | **[geerbt: Items: IList<RightsOwnerDto>; Facets: IList<FacetDto>]** |
| **SearchResultOfRightsOwnerV6Dto** | SearchResultV6Dto<RightsOwnerDto> |  | **[geerbt: Cursor: string; Items: IList<T>; Facets: IList<FacetDto>]** |
| **SearchResultOfTaskDto** | SearchResultDto<TaskDto> |  | **[geerbt: Items: IList<TaskDto>; Facets: IList<FacetDto>]** |
| **SearchResultOfTaskV6Dto** | SearchResultV6Dto<TaskDto> |  | **[geerbt: Cursor: string; Items: IList<T>; Facets: IList<FacetDto>]** |
| **SearchResultOfWorkspaceDto** | SearchResultDto<WorkspaceDto> |  | **[geerbt: Items: IList<WorkspaceDto>; Facets: IList<FacetDto>]** |
| **SearchResultOfWorkspaceV6Dto** | SearchResultV6Dto<WorkspaceDto> |  | **[geerbt: Cursor: string; Items: IList<T>; Facets: IList<FacetDto>]** |
| **SearchResultV6Dto<T> (abstract)** | SearchResultDto<T> | For generic search retrieval of any entity such as Assets, Categories, etc. | Cursor: string **[geerbt: Items: IList<T>; Facets: IList<FacetDto>]** |
| **SetConfigurationValuesDto** |  |  | ConfigurationValues: IList<ConfigurationValueDto> |
| **SetContextPercentageDto** |  |  | ContextGroupIdentifier: ContextGroupIdentifierDto; Percentage: double |
| **SetPasswordDto** |  |  | Password: string |
| **SetPropertiesDto** |  |  | Properties: IList<SetPropertyDto> |
| **SetPropertyDto** | RowVersionDto |  | PropertyDefinitionIdentifier: string; PropertyValues: IList<SetPropertyValueDto>; InheritToNodeTypeIdentifiers: IList<string> **[geerbt: RowVersion: byte[]]** |
| **SetPropertyResultDto** | IdentifierRowVersionDto |  | PropertyDefinitionIdentifier: string; IsSuccess: bool; Reason: SetPropertyResultReasonDto; Messages: IList<string> **[geerbt: Identifier: string; RowVersion: byte[]]** |
| **SetPropertyValueDto** |  |  | PropertyListItemIdentifier: string; PropertyValue: IList<TranslatedValueDto>; UserContextIdentifier: string |
| **ShareDerivationDto** |  |  | Derivation: AssetDerivationDto; ThumbnailUri: string; AssetSetIdentifier: string |
| **ShareDto** |  |  | Identifier: string*; Name: string*; AssetTypeDerivations: IDictionary<string, ISet<string>>*; ValidUntil: DateTime?*; EmailRecipients: IList<ShareRecipientDto>* |
| **ShareRecipientDto** |  |  | EmailAddress: string; Token: string |
| **SortDto** |  |  | PropertyDefinitionIdentifier: string; Direction: SortDirectionDto; LanguageIdentifier: string |
| **StartUploadResponseDto (obsolete)** |  |  | UploadSessionId: string* |
| **StateDto** |  |  | Identifier: string*; Name: IList<TranslatedValueDto>*; ApplicablePermissions: IList<string>; SequenceNr: int; NextStateIdentifiers: IList<string>; PreviousStateIdentifiers: IList<string>; IsDefault: bool; RowVersion: byte[] |
| **StateTransitionsDto** |  |  | NextStateIdentifiers: IList<string>* |
| **TaskAssigneeDto** |  |  | UserIdentifier: string |
| **TaskDto** |  |  | Name: string*; Description: string; DueDate: DateTime?; Identifier: string; CreatedAt: DateTime?; CreatedBy: string; UpdatedAt: DateTime?; Status: TaskStatusDto; TaskStatus: TaskStateDto; TaskType: string; AssignedUsers: IList<UserInfoDto>; ApplicablePermissions: IList<string>; SequenceNr: int; StateIdentifier: string; WorkspaceIdentifier: string; WorkspaceName: IList<TranslatedValueDto>; Properties: IList<PropertyDto>; TotalComments: int |
| **TaskSearchQueryDto** | SearchQueryBaseDto |  | ParentEntities: IList<EntityDto> **[geerbt: SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **TaskStatusDto** |  |  | Identifier: string; Name: string; IsDefault: bool; IsEndStatus: bool; Transitions: IList<TaskStatusTransitionDto> |
| **TaskStatusTransitionDto** |  |  | Identifier: string; Name: string |
| **TenantDto** |  |  | Identifier: string; Name: IList<TranslatedValueDto>; Default: bool; Status: string; AcceptsRegistrations: bool |
| **TenantExtendedDto** | TenantDto |  | UserGroupRoleMaps: IList<TenantUserGroupRoleMapDto> **[geerbt: Identifier: string; Name: IList<TranslatedValueDto>; Default: bool; Status: string; AcceptsRegistrations: bool]** |
| **TenantPermissionsDto** |  |  | TenantIdentifier: string; ApplicablePermissions: IList<string> |
| **TenantRoleExtendedDto** |  |  | Identifier: string; TenantIdentifiers: IList<string>; Name: IList<TranslatedValueDto>; ObjectTypePermissions: IList<ObjectTypePermissionDto>; PropertyEditEntityLimit: int?; PropertyExportEntityLimit: int? |
| **TenantUserGroupRoleMapDto** |  |  | UserGroup: UserGroupDto; TenantIdentifier: string; RoleIdentifier: string; RowVersion: byte[] |
| **TranslatedValueDto** |  |  | DataLanguageIdentifier: string; Value: string |
| **UpdateBillingContactDto** |  |  | Name: string; EmailAddress: string; Status: EntityStatusDto |
| **UpdateBuyoutDto** |  |  | Description: string; Basis: double; CurrencyIdentifier: string; AdditionalFeePercentage: double; FromDate: DateTime; ToDate: DateTime; RightsOwnerIdentifier: string; PriceListIdentifier: string |
| **UpdateCategoryDto** |  |  | Name: IList<TranslatedValueDto> |
| **UpdateCustomEntityDto** |  |  | Name: IList<TranslatedValueDto> |
| **UpdateDataLanguageDto** |  |  | Name: IList<TranslatedValueDto>; Status: EntityStatusDto; IsDefault: bool |
| **UpdateDerivationJobDefinitionDto** |  |  | DerivationTypeIdentifier: string; OutputFileTypeIdentifier: string; InputFileTypeIdentifiers: IList<string>; JobDefinition: string |
| **UpdateDerivationTypeDto** |  |  | Name: IList<TranslatedValueDto>; AssetTypeSettings: IList<AssetTypeSettingsDto> |
| **UpdateEntityTypeDto** |  |  | Name: IList<TranslatedValueDto>; Description: IList<TranslatedValueDto>; Status: EntityStatusDto |
| **UpdateFileNamingDefinitionDto** | ValidateFileNamingDefinitionDto |  | Name: IList<TranslatedValueDto>*; Tenants: IList<FileNamingDefinitionTenantDto>; IsGlobalDefault: bool **[geerbt: Format: string; CharacterCasing: CharacterCasingDto; ReplaceIllegalCharacters: bool; AbsentPropertyStrategy: AbsentPropertyStrategyDto]** |
| **UpdateLicenseDto** |  |  | Name: IList<TranslatedValueDto>; Description: IList<TranslatedValueDto>; PeriodStart: DateTime; PeriodEnd: DateTime; Status: EntityStatusDto |
| **UpdatePriceListDto** |  |  | ContextPercentages: IList<SetContextPercentageDto>; Name: string; Description: string; Status: EntityStatusDto |
| **UpdatePropertyDefinitionDto** |  |  | PropertyGroupIdentifier: string; AllowMultipleValues: bool; AllowCustomListValue: bool; Active: bool; Editable: bool; IgnoreCopying: bool; IsSearchableFacet: bool; SearchOption: SearchOptionDto; Name: IList<TranslatedValueDto>; Description: IList<TranslatedValueDto>; ListItems: IList<CreateOrUpdatePropertyListItemDto>; EntityTypeIdentifiers: IList<string>; IsFileNamingUnlocked: bool; HasUserContext: bool; DefinitionType: PropertyDefinitionTypeDto; SortKey: int? |
| **UpdatePropertyDto** | RowVersionDto |  | PropertyValues: IList<PropertyValueDto> **[geerbt: RowVersion: byte[]]** |
| **UpdatePropertyExtractionDefinitionDto** |  |  | ExtractionType: PropertyExtractionTypeDto; InputDerivationTypeIdentifier: string; InputAssetTypeIdentifiers: ISet<string>; ExtractionSources: IList<PropertyExtractionSourceDto>; Status: EntityStatusDto? |
| **UpdatePropertyGroupDto** |  |  | Name: IList<TranslatedValueDto>; Active: bool; SortKey: int? |
| **UpdatePropertyInheritanceDto** |  |  | InheritToNodeTypeIdentifiers: IList<string> |
| **UpdatePropertySetDto** |  |  | Name: string; IsGlobal: bool; IsDefault: bool; PropertySetConditions: IList<PropertySetConditionDto>; PropertySetPropertyDefinitions: IList<PropertySetPropertyDefinitionDto> |
| **UpdateRelationTypeDto** | RowVersionDto |  | Name: IList<TranslatedValueDto>*; Description: IList<TranslatedValueDto>; Status: EntityStatusDto*; IsDirected: bool; IsUsedForAssetSearch: bool **[geerbt: RowVersion: byte[]]** |
| **UpdateResourcePermissionDto** |  |  | UserGroupIdentifier: string; UserIdentifier: string; RoleIdentifier: string* |
| **UpdateRightsOwnerDto** |  |  | Name: string; BillingContactIdentifier: string; Status: EntityStatusDto |
| **UpdateRuleDto** |  |  | Name: IList<TranslatedValueDto>; NodeTypeIdentifiers: IList<string>; Filters: IList<CreateRuleTriggerDto>; Triggers: IList<CreateRuleTriggerDto>*; Actions: IList<CreateRuleActionDto>* |
| **UpdateSavedSearchDto** |  |  | Name: string; SearchQuery: string |
| **UpdateShareDto** |  |  | Name: string*; ValidUntil: DateTime?*; AssetTypeDerivations: IDictionary<string, ISet<string>>* |
| **UpdateStateDto** |  |  | Name: IList<TranslatedValueDto>* |
| **UpdateStateTransitionsDto** |  |  | PreviousStateIdentifiers: IList<string>*; NextStateIdentifiers: IList<string>* |
| **UpdateTaskDto** |  |  | Name: string*; Description: string; DueDate: DateTime?; AssignedUserIdentifier: string; TaskStatus: TaskStateDto* |
| **UpdateTransitionsDto** |  |  | Transitions: IDictionary<string, StateTransitionsDto>* |
| **UpdateUserContextDto** |  |  | Name: IList<TranslatedValueDto>; SequenceNr: int; IsDefault: bool |
| **UpdateUserContextGroupDto** |  |  | Name: IList<TranslatedValueDto>; UserContextIdentifiers: IList<string> |
| **UpdateUserDto** |  |  | UserName: string; FirstName: string; LastName: string; EmailAddress: string; UserSettings: CreateOrUpdateUserSettingsDto; Status: EntityStatusDto?; ContactPerson: string; Company: string; PhoneNumber: string; AccessType: AccessTypeDto |
| **UpdateUserGroupDto** | CreateUserGroupDto |  | **[geerbt: Name: string; Description: string]** |
| **UpdateWatermarkDefinitionDto** |  |  | DisplayInUI: bool; DerivationJobDefinitionIdentifier: string; PropertyDefinitionIdentifier: string |
| **UpdateWatermarkTriggerDto** |  |  | PropertyListValueIdentifier: string; ValueBool: bool; FileReferenceIdentifier: string |
| **UpdateWorkspaceDto** |  |  | Name: IList<TranslatedValueDto>; Status: EntityStatusDto*; PropertySetIdentifier: string; FinalizationCategoryIdentifier: string; CopyPropertiesToAssets: bool |
| **UserAccountInfoDto** | UserInfoDto |  | AccessType: AccessTypeDto; AccountStatus: AccountStatusDto **[geerbt: Identifier: string; UserName: string; FirstName: string; LastName: string; EmailAddress: string; AvatarUrl: string; Status: EntityStatusDto; Company: string]** |
| **UserContextDto** | CreateUserContextDto |  | RowVersion: byte[] **[geerbt: Identifier: string; Name: IList<TranslatedValueDto>; SequenceNr: int; IsDefault: bool]** |
| **UserContextGroupDto** |  |  | Identifier: ContextGroupIdentifierDto; Name: IList<TranslatedValueDto>; IncludedContexts: IList<UserContextGroupDto> |
| **UserDto** |  |  | Identifier: string; UserName: string; FirstName: string; LastName: string; EmailAddress: string; UserSettings: UserSettingsDto; Status: EntityStatusDto; AccountStatus: AccountStatusDto; UserOrigin: UserOriginDto; LastLoginDate: DateTime?; CreatedAt: DateTime; ContactPerson: string; Company: string; PhoneNumber: string; AccessType: AccessTypeDto |
| **UserGroupDto** |  |  | Identifier: string; Name: string |
| **UserGroupExtendedDto** | UserGroupDto |  | Description: string; MembersCount: int **[geerbt: Identifier: string; Name: string]** |
| **UserGroupMemberDto** |  |  | Identifier: string |
| **UserIdentifiersDto** |  |  | UserIdentifiers: IList<string> |
| **UserInfoDto** |  |  | Identifier: string; UserName: string; FirstName: string; LastName: string; EmailAddress: string; AvatarUrl: string; Status: EntityStatusDto; Company: string |
| **UserNotificationSettingsDto** |  |  | DisableNotifications: bool |
| **UserProfileDto** |  |  | Roles: IList<TenantRoleExtendedDto>; Settings: UserSettingsDto |
| **UserRegistrationDto** | UserDto |  | TenantIdentifier: string; ApprovedAt: DateTime? **[geerbt: Identifier: string; UserName: string; FirstName: string; LastName: string; EmailAddress: string; UserSettings: UserSettingsDto; Status: EntityStatusDto; AccountStatus: AccountStatusDto; UserOrigin: UserOriginDto; LastLoginDate: DateTime?; CreatedAt: DateTime; ContactPerson: string; Company: string; PhoneNumber: string; AccessType: AccessTypeDto]** |
| **UserSettingsDto** | CreateOrUpdateUserSettingsDto |  | SearchTermOperatorDefault: MultiTermOperatorDto? **[geerbt: DisplayLanguage: string; Locale: string; DataLanguageIdentifier: string; UiSettings: string; UserContextIdentifier: string; AvatarUrl: string; SearchTermOperator: MultiTermOperatorDto?]** |
| **ValidateAssetSetPropertiesResultDto** |  |  | AssetSetIdentifier: string; FileName: string; IsValid: bool; AssetSetPropertyValidationResults: IDictionary<string, IList<AssetSetPropertyValidationResultDto>> |
| **ValidateFileNamingDefinitionDto** |  |  | Format: string*; CharacterCasing: CharacterCasingDto; ReplaceIllegalCharacters: bool; AbsentPropertyStrategy: AbsentPropertyStrategyDto* |
| **ValidationErrorDto** |  |  | ParameterName: string; Errors: IList<string>; FieldErrors: IDictionary<string, IList<FieldErrorDto>> |
| **WatermarkDefinitionDto** |  |  | Identifier: string; DisplayInUI: bool; PropertyDefinitionIdentifier: string; DerivationJobDefinitionIdentifier: string; WatermarkTriggers: IList<WatermarkTriggerDto>; RowVersion: byte[] |
| **WatermarkTriggerDto** |  |  | Identifier: string; WatermarkDefinitionIdentifier: string; PropertyListValueIdentifier: string; ValueBool: bool; FileReferenceIdentifier: string; RowVersion: byte[] |
| **WorkspaceDto** | CreateWorkspaceDto |  | TotalAssets: int; TotalTasks: int; TotalComments: int; TotalUsers: int; ApplicablePermissions: IList<string>; PropertyDefinitionRestrictions: ISet<PropertyDefinitionRestrictionDto>; RootCategoryIdentifier: string; Properties: IList<PropertyDto>; StateIdentifier: string; SequenceNr: int; CreatedAt: DateTime; ThumbnailUris: IList<string>; AvatarUrl: string **[geerbt: Identifier: string; IsTemplate: bool; WorkspaceType: WorkspaceTypeDto; StateIdentifier: string; Properties: IList<SetPropertyDto>; Name: IList<TranslatedValueDto>; Status: EntityStatusDto; PropertySetIdentifier: string; FinalizationCategoryIdentifier: string; CopyPropertiesToAssets: bool]** |
| **WorkspaceSearchQueryDto** | SearchQueryBaseDto |  | IsTemplate: bool **[geerbt: SearchTerm: string; LanguageIdentifiers: IList<string>; ExactSearch: bool; Operator: MultiTermOperatorDto?; AdvancedSearchFilters: IList<FilterDto>; Sort: IList<SortDto>; IncludePropertyDefinitionIdentifiers: IList<string>; FacetPropertyDefinitionIdentifiers: IList<string>; FacetFilters: IList<FacetFilterDto>]** |
| **ZipCategoryDownloadRequestDto** |  |  | FileNamingDefinitionIdentifier: string; DataLanguageIdentifier: string; IncludeSubCategories: bool; AssetDerivationTypes: IList<AssetTypeDerivationTypesDto>* |
| **ZipDownloadRequestDto** |  |  | Assets: IList<AssetSetDerivationTypeDto>*; FileNamingDefinitionIdentifier: string |

| Enum | Beschreibung | Werte |
|---|---|---|
| **AbsentPropertyStrategyDto** (string) |  | Unknown=0, PropertyDefinitionIdentifier=1, Skip=2, Abort=3 |
| **AccessTypeDto** (string) |  | Permanent=0, Expiring=1 |
| **AccountStatusDto** (string) |  | Unknown=0, Active=1, Inactive=2, EmailVerificationPending=10, EmailVerificationExpired=11, ApprovalPending=20, ApprovalDenied=21, Locked=30, AccountRenewalPending=31, AccountRenewalExpired=32 |
| **ActivityLogChangeTypeDto** (string) |  | AssigneeSet=0, AssigneeUpdated=1, AssigneeCleared=2, CommentCreated=3, CommentUpdated=4, CommentDeleted=5, PropertyValueCreated=6, PropertyValueUpdated=7, PropertyValueDeleted=8, RelationCreated=9, RelationDeleted=10, ResourcePermissionAdded=11, ResourcePermissionUpdated=12, ResourcePermissionRemoved=13, StateUpdated=14, ShareCreated=15, ShareUpdated=16, ShareDeleted=17, ConfigurationValueCreated=18, ConfigurationValueUpdated=19, ConfigurationValueDeleted=20 |
| **ActivityLogTypeDto** (string) |  | AssetCreated=0, AssetUpdated=1, AssetDeleted=2, CategoryCreated=3, CategoryUpdated=4, CategoryDeleted=5, TaskCreated=6, TaskUpdated=7, TaskDeleted=8, WorkspaceCreated=9, WorkspaceUpdated=10, WorkspaceDeleted=11, JobUpdated=12, ApplicationSettingsUpdated=13 |
| **ArchiveEntryStatusDto** (string) |  | Unknown=0, Extracted=1, Filtered=2, Error=3 |
| **ArchiveEntryTypeDto** (string) |  | Unknown=0, Folder=1, File=2 |
| **ArchiveExtractionModeDto** (string) |  | ExtractWithFullPath=0, ExtractFilesOnly=1 |
| **AssetDerivationStatusDto** (string) |  | Pending=0, Processing=1, Finished=2, Failed=3 |
| **AuthorizableActionDto** (string) |  | Read=0, Create=1, Update=2, Delete=3, View=4, Any=5 |
| **BackgroundJobTriggerTypeDto** (string) |  | Unknown=0, EntityCreated=1, EntityDeleted=2, FullPermissionUpdate=3, PermissionCreated=4 |
| **CharacterCasingDto** (string) |  | Normal=0, Upper=1, Lower=2 |
| **CommentSortByDto** (string) |  | CreatedAt=0 |
| **ConfigurationProviderDto** (string) |  | Unknown=0, AppSettingsYaml=1, AppSettingsOverridesYaml=2, Yaml=3, EnvironmentVariables=4, CommandLine=5, Database=6 |
| **ContactPrintFormatDto** (string) |  | Portrait1x1=1, Portrait1x2=2, Portrait2x3=3, Portrait3x5=4, Landscape1x1=5, Landscape2x1=6, Landscape3x2=7, Landscape5x3=8 |
| **ContextGroupTypeDto** (string) |  | Group=0, UserContext=1 |
| **CreateRelationResultReasonDto** (string) |  | Created=0, Error=1, AlreadyExists=2, MutuallyExclusive=3, PermissionDenied=4, Invalid=5 |
| **DeletePropertyResultReasonDto** (string) |  | Success=0, NonExistingPropertyDefinition=1, DeleteSystemPropertyNotAllowed=2, NotEditable=3, NonExistingProperty=4, NotAllowed=5 |
| **EntityStatusDto** (string) |  | Inactive=0, Active=1 |
| **ExternalGroupSortByDto** (string) |  | ExternalGroupName=0, ExternalGroupDisplayName=1 |
| **FacetOperatorDto** (string) |  | Equals=2, IdentifierEquals=11 |
| **FileNamingTokenTypeDto** (string) |  | Unknown=0, Property=1, Variable=2 |
| **FileUsageTypeDto** (string) |  | Undefined=0, Watermark=1, Avatar=2 |
| **FillModeDto** (string) |  | Optional=0, Recommended=1, Mandatory=2 |
| **JobStatusDto** (string) |  | Unknown=0, EnqueuedForExecution=1, Executing=2, Executed=3, Failed=4 |
| **MultiTermOperatorDto** (string) |  | Or=0, And=1 |
| **ObjectTypeDto** (string) |  | AssetSet=0, Version=1, AssetDerivation=2, Category=3, DerivationJobDefinition=4, DerivationType=5, Language=7, PropertyDefinition=8, AssetProperty=9, PropertyValue=10, PropertyListEntry=11, Task=12, PropertyGroup=13, Tenant=14, Workspace=15, RelationType=16, Comment=17, AssetRelation=18, SavedSearch=19, AssetPropertySet=20, AssetPropertySetGlobal=21, WorkspaceTemplate=23, CategoryProperty=24, CategoryPropertySet=25, CategoryPropertySetGlobal=26, WorkspaceProperty=27, WorkspacePropertySet=28, WorkspacePropertySetGlobal=29, TaskProperty=30, TaskPropertySet=31, TaskPropertySetGlobal=32, CropDerivation=33, AssetPropertyMassEdit=34, AssetDerivationJob=35, Share=36, RecycleBin=37, AssetSetDuplicate=38, Watermark=39, CategoryPropertyInheritance=40, ReIndexingJob=41, PropertyImport=42, VersionPropertySet=43, VersionPropertySetGlobal=44, UserContext=45, FileNamingDefinition=46, TenantFileNamingDefinition=47, ShareFileNamingDefinition=48, AssetDownload=49, NodeType=50, CustomNode=51, ApplicationSettings=52, Rule=53, ArchiveExtractionJob=54, State=55, Collection=56, BillingContact=57, RightsOwner=58, License=59, Relation=60, PriceList=61, Buyout=62, UserContextGroup=63, LicensePurchase=64, PropertyExtractionJob=65, Role=1000, UserGroupToRoleMap=1020, ObjectTypePermission=1030, NodePermission=1040, DerivationTypeRestriction=1050, PropertyDefinitionRestriction=1060, User=1070, UserGroup=1080, Dashboard=2000, AssetBrowser=2010, DownloadBasket=2020, Uploads=2030, Admin=2040, AssetDetail=2050, Any=2060 |
| **OperatorDto** (string) |  | HasValue=0, HasNoValue=1, Equals=2, NotEquals=3, Contains=4, DoesNotContain=5, FuzzyEquals=6, FuzzyContains=7, GreaterThanOrEquals=8, LessThanOrEquals=9, Between=10, IdentifierEquals=11, IdentifierNotEquals=12 |
| **PermissionDto** (string) |  | Unknown=0, AdminView=1, AssetBrowserView=2, AssetDerivationCreate=3, AssetDerivationDelete=4, AssetDerivationRead=5, AssetDerivationUpdate=6, AssetDerivationView=7, AssetDerivationJobCreate=8, AssetDerivationJobDelete=9, AssetDerivationJobRead=10, AssetDerivationJobUpdate=11, AssetDerivationJobView=12, AssetDetailView=13, AssetDownloadRead=14, AssetPropertyCreate=15, AssetPropertyDelete=16, AssetPropertyRead=17, AssetPropertyUpdate=18, AssetPropertyView=19, AssetPropertyMassEditCreate=20, AssetPropertyMassEditDelete=21, AssetPropertyMassEditRead=22, AssetPropertyMassEditUpdate=23, AssetPropertyMassEditView=24, AssetPropertySetCreate=25, AssetPropertySetDelete=26, AssetPropertySetGlobalCreate=27, AssetPropertySetGlobalDelete=28, AssetPropertySetGlobalRead=29, AssetPropertySetGlobalUpdate=30, AssetPropertySetGlobalView=31, AssetPropertySetRead=32, AssetPropertySetUpdate=33, AssetPropertySetView=34, RelationCreate=35, RelationDelete=36, RelationRead=37, RelationUpdate=38, RelationView=39, AssetSetCreate=40, AssetSetDelete=41, AssetSetRead=42, AssetSetUpdate=43, AssetSetView=44, AssetSetDuplicateRead=45, CategoryCreate=46, CategoryDelete=47, CategoryRead=48, CategoryUpdate=49, CategoryView=50, CategoryPropertyCreate=51, CategoryPropertyDelete=52, CategoryPropertyRead=53, CategoryPropertyUpdate=54, CategoryPropertyView=55, CategoryPropertyInheritanceCreate=56, CategoryPropertyInheritanceDelete=57, CategoryPropertyInheritanceRead=58, CategoryPropertyInheritanceUpdate=59, CategoryPropertySetCreate=60, CategoryPropertySetDelete=61, CategoryPropertySetGlobalCreate=62, CategoryPropertySetGlobalDelete=63, CategoryPropertySetGlobalRead=64, CategoryPropertySetGlobalUpdate=65, CategoryPropertySetGlobalView=66, CategoryPropertySetRead=67, CategoryPropertySetUpdate=68, CategoryPropertySetView=69, CommentCreate=70, CommentDelete=71, CommentRead=72, CommentUpdate=73, CommentView=74, DashboardView=75, DataLanguageCreate=76, DataLanguageDelete=77, DataLanguageRead=78, DataLanguageUpdate=79, DataLanguageView=80, DerivationJobDefinitionCreate=81, DerivationJobDefinitionDelete=82, DerivationJobDefinitionRead=83, DerivationJobDefinitionUpdate=84, DerivationJobDefinitionView=85, DerivationTypeCreate=86, DerivationTypeDelete=87, DerivationTypeRead=88, DerivationTypeUpdate=89, DownloadBasketView=90, EdgeTypeCreate=91, EdgeTypeDelete=92, EdgeTypeRead=93, EdgeTypeUpdate=94, EdgeTypeView=95, FileNamingDefinitionCreate=96, FileNamingDefinitionDelete=97, FileNamingDefinitionRead=98, FileNamingDefinitionUpdate=99, FileNamingDefinitionView=100, GlobalAdmin=101, NodePermissionCreate=102, NodePermissionDelete=103, NodePermissionRead=104, NodePermissionUpdate=105, NodePermissionView=106, PropertyDefinitionCreate=107, PropertyDefinitionDelete=108, PropertyDefinitionRead=109, PropertyDefinitionUpdate=110, PropertyDefinitionView=111, PropertyGroupCreate=112, PropertyGroupDelete=113, PropertyGroupRead=114, PropertyGroupUpdate=115, PropertyGroupView=116, PropertyImportCreate=117, PropertyImportDelete=118, PropertyImportRead=119, PropertyImportUpdate=120, PropertyImportView=121, RecycleBinDelete=122, RecycleBinRead=123, RecycleBinView=124, ReIndexingJobCreate=125, ReIndexingJobRead=126, ReIndexingJobView=127, RoleCreate=128, RoleDelete=129, RoleRead=130, RoleUpdate=131, RoleView=132, SavedSearchCreate=133, SavedSearchDelete=134, SavedSearchRead=135, SavedSearchUpdate=136, SavedSearchView=137, ShareCreate=138, ShareDelete=139, ShareRead=140, ShareUpdate=141, ShareView=142, ShareFileNamingDefinitionCreate=143, ShareFileNamingDefinitionDelete=144, ShareFileNamingDefinitionRead=145, ShareFileNamingDefinitionUpdate=146, ShareFileNamingDefinitionView=147, TaskCreate=148, TaskDelete=149, TaskRead=150, TaskUpdate=151, TaskView=152, TaskPropertyCreate=153, TaskPropertyDelete=154, TaskPropertyRead=155, TaskPropertyUpdate=156, TaskPropertyView=157, TaskPropertySetCreate=158, TaskPropertySetDelete=159, TaskPropertySetGlobalCreate=160, TaskPropertySetGlobalDelete=161, TaskPropertySetGlobalRead=162, TaskPropertySetGlobalUpdate=163, TaskPropertySetGlobalView=164, TaskPropertySetRead=165, TaskPropertySetUpdate=166, TaskPropertySetView=167, TenantCreate=168, TenantDelete=169, TenantRead=170, TenantUpdate=171, TenantView=172, TenantFileNamingDefinitionCreate=173, TenantFileNamingDefinitionDelete=174, TenantFileNamingDefinitionRead=175, TenantFileNamingDefinitionUpdate=176, TenantFileNamingDefinitionView=177, UploadView=178, UserCreate=179, UserDelete=180, UserRead=181, UserUpdate=182, UserView=183, UserContextCreate=184, UserContextDelete=185, UserContextUpdate=186, UserGroupCreate=187, UserGroupDelete=188, UserGroupRead=189, UserGroupUpdate=190, UserGroupView=191, VersionCreate=192, VersionDelete=193, VersionRead=194, VersionUpdate=195, VersionView=196, VersionPropertySetCreate=197, VersionPropertySetDelete=198, VersionPropertySetGlobalCreate=199, VersionPropertySetGlobalDelete=200, VersionPropertySetGlobalRead=201, VersionPropertySetGlobalUpdate=202, VersionPropertySetGlobalView=203, VersionPropertySetRead=204, VersionPropertySetUpdate=205, VersionPropertySetView=206, WatermarkCreate=207, WatermarkDelete=208, WatermarkRead=209, WatermarkUpdate=210, WatermarkView=211, WorkspaceCreate=212, WorkspaceDelete=213, WorkspaceRead=214, WorkspaceUpdate=215, WorkspaceView=216, WorkspacePropertyCreate=217, WorkspacePropertyDelete=218, WorkspacePropertyRead=219, WorkspacePropertyUpdate=220, WorkspacePropertyView=221, WorkspacePropertySetCreate=222, WorkspacePropertySetDelete=223, WorkspacePropertySetGlobalCreate=224, WorkspacePropertySetGlobalDelete=225, WorkspacePropertySetGlobalRead=226, WorkspacePropertySetGlobalUpdate=227, WorkspacePropertySetGlobalView=228, WorkspacePropertySetRead=229, WorkspacePropertySetUpdate=230, WorkspacePropertySetView=231, WorkspaceTemplateCreate=232, WorkspaceTemplateDelete=233, WorkspaceTemplateRead=234, WorkspaceTemplateUpdate=235, WorkspaceTemplateView=236, CropDerivationCreate=237, EntityCreate=238, EntityDelete=239, EntityRead=240, EntityUpdate=241, EntityView=242, CustomEntityCreate=243, CustomEntityDelete=244, CustomEntityRead=245, CustomEntityUpdate=246, CustomEntityView=247, CustomEntityPropertyCreate=248, CustomEntityPropertyDelete=249, CustomEntityPropertyRead=250, CustomEntityPropertyUpdate=251, CustomEntityPropertyView=252, ApplicationSettingsCreate=253, ApplicationSettingsDelete=254, ApplicationSettingsRead=255, ApplicationSettingsUpdate=256, ApplicationSettingsView=257, RuleCreate=258, RuleDelete=259, RuleRead=260, RuleUpdate=261, RuleView=262, RuleReApplyJobCreate=263, CustomEntityBrowserView=264, CustomEntityPropertySetCreate=265, CustomEntityPropertySetDelete=266, CustomEntityPropertySetRead=267, CustomEntityPropertySetUpdate=268, CustomEntityPropertySetView=269, CustomEntityPropertySetGlobalCreate=270, CustomEntityPropertySetGlobalDelete=271, CustomEntityPropertySetGlobalRead=272, CustomEntityPropertySetGlobalUpdate=273, CustomEntityPropertySetGlobalView=274, ArchiveExtractionJob=275, ReIndexingJobDelete=276, StateCreate=277, StateDelete=278, StateRead=279, StateUpdate=280, StateView=281, CollectionView=282, WorkspaceFinalize=283, RuleJobReportRead=284, LicenseView=285, LicenseRead=286, LicenseCreate=287, LicenseUpdate=288, LicenseDelete=289, WorkspaceActivityLogRead=290, WorkspaceActivityLogView=291, TaskActivityLogRead=292, TaskActivityLogView=293, AssetActivityLogRead=294, AssetActivityLogView=295, LicenseOfferView=296, LicenseOfferRead=297, PropertyExtractionDefinitionCreate=298, PropertyExtractionDefinitionDelete=299, PropertyExtractionDefinitionRead=300, PropertyExtractionDefinitionUpdate=301, PropertyExtractionDefinitionView=302, LicensePurchaseCreate=303, LicensePurchaseRead=304, PropertyExtractionJob=305, PermissionMaterializationJobRead=306, PermissionMaterializationJobCreate=307, PermissionMaterializationJobView=308, CategoryDownloadRead=309, ApplicationSettingsActivityLogRead=310, ApplicationSettingsActivityLogView=311 |
| **PriceListSortByDto** (string) |  | Identifier=0, Name=1, Description=2, Status=3, CreatedAt=4, UpdatedAt=5 |
| **ProcessingStatusDto** |  | Waiting, Started, Finished, Canceled |
| **PropertyActionStrategyDto** (string) |  | Unknown=0, KeepExisting=1, OverwriteExisting=2, Abort=3, DeleteExisting=4, Merge=5 |
| **PropertyDefinitionFieldsDto** (string) |  | Name=0, Description, EntityTypes, PropertyGroup, SortKey, DefinitionType, IsSearchableFacet, Editable, SearchOption, IgnoreCopying, Status, IsFileNamingUnlocked, HasUserContext, AllowMultipleValues, AllowCustomListValue, ListItems |
| **PropertyDefinitionListTypeDto** (string) |  | NoList=0, List=1, Multiselect=2, Singleselect=3 |
| **PropertyDefinitionSortByDto** (string) |  | Identifier=0, PropertyGroupIdentifier=1, ValueType=2, Editable=3, IsSystem=4, IsSearchableFacet=5, Active=6, SortKey=7 |
| **PropertyDefinitionTypeDto** (string) |  | Standard=0, Url=1, Html=2 |
| **PropertyExportFileFormatDto** (string) |  | Unknown=0, Xls=1, Xlsx=2, Csv=3 |
| **PropertyExtractionJobAssetResultStatusDto** (string) |  | Pending=0, Extracted=1, Imported=2, Error=3 |
| **PropertyExtractionProviderDto** (string) |  | AzureCognitiveServices=0, Imagga=1, Clarifai=2 |
| **PropertyExtractionResultStatusDto** (string) |  | Extracted=0, Imported=1, Skipped=2, Error=3 |
| **PropertyExtractionSourceStatusDto** (string) |  | Extracted=0, Imported=1, Error=2 |
| **PropertyExtractionTypeDto** (string) |  | DetectTags=0 |
| **PropertyGroupSortByDto** (string) |  | PropertyDefinitionsCount=0, SortKey=1, Active=2 |
| **PropertyImportMessageSeverityDto** (string) | Describes the severity of a PropertyImportValidation message | Info=1, Warning=2, Error=3 |
| **PropertyImportMessageTypeDto** (string) | Parsing message type | Unknown=0, FileFormatError=1, MetadataTabNotFound=2, EntityIdentifierNotFound=3, EnttiyTypeNotFound=4, PropertyDefinitionIdentifierOrLanguageParseError=5 |
| **PropertyValueTypeDto** (string) |  | String=0, Text=1, Date=2, DateTime=3, Time=4, Integer=5, Float=6, Decimal=7, Boolean=8, TimeSpan=9 |
| **SearchOptionDto** (string) |  | NotSearchable=0, AdvancedSearchable=1, FulltextSearchable=2, AdvancedAndFulltextSearchable=3 |
| **SetPropertyResultReasonDto** (string) |  | Success=0, CreateOrUpdateRestricted=1, InheritancePermissionDenied=2, PropertyDefinitionNotExists=3, NotEditable=4, ChangeSystemPropertyNotAllowed=5, Invalid=6, CreateOrUpdatePropertyValuesFailed=7, CreateInEditNotAllowed=8 |
| **SortDirectionDto** (string) |  | Asc=0, Desc=1 |
| **TaskStateDto** (string) |  | Open=0, Closed=1 |
| **TriggerOperatorDto** (string) |  | Equals=0, NotEquals=1, GreaterThan=2, LessThan=3, GreaterThanOrEquals=4, LessThanOrEquals=5, Contains=6, HasRelationType=7, IsNewVersion=8, ValueChanged=9, IsNewCategory=10, RegexMatch=11 |
| **UserGroupSortByDto** (string) |  | Name=0, Created=1 |
| **UserOriginDto** (string) |  | Unknown=0, Sharedien=1, ExternalProvider=2 |
| **UserSortByDto** (string) |  | FirstName=0, LastName=1, Email=2, UserName=3 |
| **WorkspaceTypeDto** (string) |  | Upload=0, Collection=1, Collaboration=2 |

#### Ordner `Webservice/ImageDerivation` (20 Klassen, 5 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **AbsoluteScaleDto** |  |  | AbsolutePixelWidth: int; AbsolutePixelHeight: int |
| **BlurDefinitionDto** |  |  | BlurRadiusInPixels: double; StandardLaplaceDeviationInPixels: double |
| **BlurStepDto** | TransformationStepDto |  | BlurDefinition: BlurDefinitionDto **[geerbt: Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int]** |
| **ColorDefinitionDto** |  |  | ColorDefinitionType: ColorDefinitionTypeDto; Alpha: double; CmykCyan: double; CmykMagenta: double; CmykYellow: double; CmykBlack: double; RgbRed: double; RgbGreen: double; RgbBlue: double |
| **CropDataDto** |  |  | X: double*; Y: double*; Width: double*; Height: double*; NaturalWidth: double; NaturalHeight: double; IgnoreAspectRatio: bool; ScaleWidth: double; ScaleHeight: double; Rotate: double |
| **CropDefinitionDto** |  |  | SizeX: int; SizeY: int; XAxisAnchor: XAxisAnchorDto; YAxisAnchor: YAxisAnchorDto; XAxisAnchorOffset: int; YAxisAnchorOffset: int |
| **CropStepDto** | TransformationStepDto |  | CropDefinition: CropDefinitionDto **[geerbt: Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int]** |
| **ImageQualityDefinitionDto** |  |  | ColorSpace: string; ColorType: string; ColorProfileName: string; Dpi: double?; CompressionMethod: string |
| **ImageTransformationDefinitionDto** |  |  | TransformationSteps: IList<TransformationStepDto>; SetOpacity: bool; OpacityValue: double; SetBackgroundColor: bool; BackgroundColorDefinition: ColorDefinitionDto |
| **InputImageDefinitionDto** | ImageQualityDefinitionDto |  | FrameIndex: int? **[geerbt: ColorSpace: string; ColorType: string; ColorProfileName: string; Dpi: double?; CompressionMethod: string]** |
| **OutputImageDefinitionDto** | ImageQualityDefinitionDto |  | ImageType: string; ImageQuality: int? **[geerbt: ColorSpace: string; ColorType: string; ColorProfileName: string; Dpi: double?; CompressionMethod: string]** |
| **OverlayDefinitionDto** |  |  | OverlayDataId: string; ImageDefinition: InputImageDefinitionDto; ImageTransformationDefinition: ImageTransformationDefinitionDto; AnchorPixelOffsetX: int; AnchorPixelOffsetY: int; SourceImageXAxisAnchor: XAxisAnchorDto; SourceImageYAxisAnchor: YAxisAnchorDto; OverlayImageXAxisAnchor: XAxisAnchorDto; OverlayImageYAxisAnchor: YAxisAnchorDto |
| **RelativeScaleDto** |  |  | RelativeWidthPercent: double; RelativeHeightPercent: double |
| **RotateDefinitionDto** |  |  | RotationDirection: RotateDirectionDto; RotationDegrees: double |
| **RotateStepDto** | TransformationStepDto |  | RotateDefinition: RotateDefinitionDto **[geerbt: Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int]** |
| **ScaleDefinitionDto** |  |  | ScaleType: ScaleTypeDto; IgnoreAspectRatio: bool; AbsoluteScale: AbsoluteScaleDto; RelativeScale: RelativeScaleDto; ScaleToFillArea: bool; PreventDownscaling: bool; PreventUpscaling: bool |
| **ScaleStepDto** | TransformationStepDto |  | ScaleDefinition: ScaleDefinitionDto **[geerbt: Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int]** |
| **SharpenDefinitionDto** |  |  | SharpeningRadiusInPixels: double; StandardLaplaceDeviationInPixels: double |
| **SharpenStepDto** | TransformationStepDto |  | SharpenDefinition: SharpenDefinitionDto **[geerbt: Note: string; StepType: string; OriginalWidth: int; OriginalHeight: int]** |
| **TransformationStepDto (abstract)** |  |  | Note: string; StepType: string |

| Enum | Beschreibung | Werte |
|---|---|---|
| **ColorDefinitionTypeDto** |  | RgbA=0, CmykA=1 |
| **RotateDirectionDto** |  | CounterClockWise, ClockWise |
| **ScaleTypeDto** |  | Absolute, Relative |
| **XAxisAnchorDto** |  | Left, Center, Right |
| **YAxisAnchorDto** |  | Top, Center, Bottom |

#### Ordner `Webservice/Upload` (6 Klassen, 1 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **ChunkUploadResponseDto** |  | Returned if the chunk upload request was successful | ChunkIdentifier: string*; UploadUrl: string*; NextChunkUploadRequestUrl: string*; FinishUploadRequestUrl: string* |
| **UploadFinishCreateRelationDto** |  |  | EntityIdentifier: string; EntityTypeIdentifier: string; RelationTypeIdentifier: string |
| **UploadFinishRequestDto** |  | Used to finish an upload | ChunkIdentifiers: IList<string>*; CopyMetadataFrom: IList<CopyPropertiesFromEntityDto>; CreateRelations: IList<UploadFinishCreateRelationDto>; Properties: IList<SetPropertyDto> |
| **UploadFinishResponseDto** |  | Returned if the request to finish the asset upload was successful | AssetSetIdentifier: string*; AssetVersionIdentifier: string*; AssetStatus: string*; ExistingAssetSetIdentifier: string; CopyMetadataResults: IList<SetPropertyResultDto>; CreateRelationResults: IList<CreateRelationResultDto>; PropertyResults: IList<SetPropertyResultDto> |
| **UploadStartRequestDto** |  | Used to request an upload of an asset, asset derivation or asset version | AssetUploadType: AssetUploadTypeDto*; FileName: string*; Asset: AssetUploadDto; AssetDerivation: AssetDerivationUploadDto; AssetVersion: AssetVersionUploadDto |
| **UploadStartResponseDto** |  | Returned if the upload start request was successful | ChunkUploadRequestUrl: string*; MaxChunkSizeBytes: long* |

| Enum | Beschreibung | Werte |
|---|---|---|
| **AssetUploadTypeDto** (string) |  | Asset=0, AssetDerivation=1, AssetVersion=2 |

#### Ordner `Webservice/Upload/Asset` (1 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **AssetUploadDto** |  | Information required to upload an asset | ParentCategoryIdentifier: string*; WorkspaceIdentifier: string; TaskIdentifier: string; ExtractCreatedArchiveAsset: ExtractCreatedArchiveAssetDto |

#### Ordner `Webservice/Upload/AssetDerivation` (1 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **AssetDerivationUploadDto** |  | Information required to upload an asset derivation | AssetSetIdentifier: string*; DerivationTypeIdentifier: string*; AssetVersionIdentifier: string |

#### Ordner `Webservice/Upload/AssetVersion` (1 Klassen, 0 Enums)

| DTO | Basis | Beschreibung | Felder (Name: Typ; * = required, ~ = obsolete) |
|---|---|---|---|
| **AssetVersionUploadDto** |  | Information required to upload an asset version | AssetSetIdentifier: string* |

### C.3 EventStream-Events (Message Queue Contracts)

**MessageQueue/V1/EventStream**

| Event-Klasse | Basis | Beschreibung | ActionEventDetails (x_sharedien_event_action_details) | DataKeys (AdditionalData) |
|---|---|---|---|---|
| **AssetEvent** | DataEventBase | Event that is sent when Asset-Data is changed | NewAssetSetCreated, NewAssetVersionCreated, AssetVersionDeactivated, AssetVersionRestored, DerivationFinished, NewDerivationProcessed, PropertyEdited, ChildAssetSetUnlinked, LinkToParentCategory, UnlinkFromParentCategory, MoveToNewParentCategory, LinkToParentWorkspace, UnlinkFromParentWorkspace, LinkToParentTask, UnlinkFromParentTask, AssetSetDeleted, ChildAssetSetLinked | NewParentCategoryIdentifier, RemovedParentCategoryIdentifier, AssetVersionIdentifier, AssetTypeIdentifier, Version, Filename, FileExtension, DerivationTypeIdentifier, NewParentWorkspaceIdentifier, RemovedParentWorkspaceIdentifier, NewParentTaskIdentifier, RemovedParentTaskIdentifier, EntityIdentifier, EntityTypeIdentifier, ChildAssetSetIdentifier, Status |
| **CategoryEvent** | DataEventBase |  | UpdateCategory, NewCategoryCreated, NewChildAssetCreated, ChildAssetSetLinked, ChildAssetSetUnlinked, DeleteCategory, LinkToParentCategory, UnlinkFromParentCategory, MoveToNewParentCategory | NewParentCategoryIdentifier, RemovedParentCategoryIdentifier, ChildAssetSetIdentifier |
| **CustomEntityEvent** | DataEventBase |  | NewCustomEntityCreated, CustomEntityDeleted | EntityTypeIdentifier, EntityIdentifier |
| **DerivationEvent** | DataEventBase | Event that is created when derivation-Data is changed | CreateDerivation, UpdateDerivation, DeleteDerivation | AssetVersionIdentifier, DerivationTypeIdentifier, FileSize, FileHash |
| **DerivationTypeEvent** | DataEventBase |  | CreateDerivationType, UpdateDerivationType, DeleteDerivationType |  |
| **EntityTypeEvent** | DataEventBase |  | NewEntityTypeCreated, EntityTypeStatusChanged, EntityTypeDeleted | RootEntityIdentifier, Status |
| **PropertyDefinitionEvent** | DataEventBase |  |  |  |
| **PropertyEvent** | DataEventBase |  | CreateProperty, UpdateProperty, DeleteProperty, PropertyValueCreated, PropertyValueUpdated, PropertyValueDeleted | PropertyDefinitionIdentifier, PropertyValue, LanguageIdentifier, UserContextIdentifier |
| **RoleEvent** | DataEventBase |  | CreateRole, UpdateRole, DeleteRole |  |
| **ShareSearchEvent** | DataEventBase |  | CreateShareSearch, UpdateShareSearch, DeleteShareSearch |  |
| **StatisticsEvent** | DataEventBase | An event triggered by statistics measurement |  |  |
| **TaskEvent** | DataEventBase |  | ChildAssetSetLinked, ChildAssetSetUnlinked, LinkToParentTask, UnlinkFromParentTask, NewTaskCreated, TaskDeleted, StateChanged, AssignedUserChanged | ChildAssetSetIdentifier, StateIdentifier, FormerStateIdentifier, AssignedUserIdentifier, UnassignedUserIdentifier, WorkspaceIdentifier |
| **TenantEvent** | DataEventBase |  | CreateTenant, UpdateTenant, DeleteTenant |  |
| **UserEvent** | DataEventBase | An event triggered by an operation that affects a user | SuccessfulLogin, FailedLogin, Logout | LastLoginAttempt, LastLoginDate, IpAddress, UserAgent, Referrer, Identifier, UserName, FirstName, LastName, Email, UserOrigin |
| **UserGroupMapEvent** | DataEventBase |  | CreateUserGroupMap, UpdateUserGroupMap, DeleteUserGroupMap | UserGroupIdentifier, RoleIdentifier, TenantIdentifier |
| **WorkspaceEvent** | DataEventBase |  | NewWorkspaceCreated, NewWorkspaceTemplateCreated, StateChanged, WorkspaceTypeChanged, WorkspaceDeleted, WorkspaceTemplateDeleted, WorkspaceUpdated, WorkspaceTemplateUpdated, ChildAssetSetLinked, ChildAssetSetUnlinked, LinkToParentWorkspace, UnlinkFromParentWorkspace, TaskCreated | ChildAssetSetIdentifier, WorkspaceType, FormerWorkspaceType, StateIdentifier, FormerStateIdentifier, SourceTemplateIdentifier, TaskIdentifier, PropertySetIdentifier, FinalizationCategoryIdentifier, CopyPropertiesToAssets |

**MessageQueue/V2/EventStream**

| Event-Klasse | Basis | Beschreibung | ActionEventDetails (x_sharedien_event_action_details) | DataKeys (AdditionalData) |
|---|---|---|---|---|
| **ArchiveExtractionEvent** | DataEventBase | Event that is sent when an archive extraction job is changed |  |  |
| **AssetReDerivationEvent** | DataEventBase | Event that is sent when an asset re-derivation job is changed |  |  |
| **PropertyImportEvent** | DataEventBase | Event that is sent when a property import job is changed |  |  |
| **PropertyMassEditEvent** | DataEventBase | Event that is sent when a property mass edit job is changed |  |  |
| **RelationEvent** | DataEventBase | Event that is sent when relation data is changed |  |  |

## Teil D: Feature-Ableitungen aus Doku und API (Workflows, Best Practices, Grenzen)

### D.1 Funktionsbereiche laut API (Inventar für Shareme)

| # | Bereich | Was Sharedien kann (aus Endpoints + DTOs abgeleitet) | Zentrale Endpoints/DTOs |
|---|---|---|---|
| 1 | **Objektmodell / Node-Graph** | Alle Inhalte sind Nodes eines `EntityType` (System-Typen: AssetSet, Category, Workspace, Task, WorkspaceTemplate, CustomNode …; kundendefinierte Entity Types anlegbar). Jeder Node hat `Identifier`, mehrsprachigen `Name` (`TranslatedValueDto[]`), `Properties`, `ApplicablePermissions` (Liste erlaubter `PermissionDto`-Werte für den aktuellen Benutzer, auf **jedem** DTO), `RowVersion` (Optimistic Concurrency), Status (`EntityStatusDto` Active/Inactive). Mandantenfähigkeit über `Tenant` (Entity Types, Rollenzuordnung, Registrierung je Tenant). | EntityTypes (6), Tenants (7), `EntityDto`, `EntityExtendedDto`, `ObjectTypeDto` (84 Objekttypen) |
| 2 | **Metadaten-Modell** | `PropertyDefinition` mit `ValueType` (String, Text, Date, DateTime, Time, Integer, Float, Decimal, Boolean, TimeSpan), Listen (`NoList/List/Multiselect/Singleselect`, `ListItems`, `AllowCustomListValue`), `AllowMultipleValues`, `Translatable` (Werte je `DataLanguage`), `HasUserContext` (Werte je `UserContext`, z. B. Markt/Marke), `Editable`, `IsSystem`, `IsSearchableFacet`, `SearchOption` (NotSearchable/Advanced/Fulltext/beides), `IgnoreCopying` (nicht auf neue Version/Kopie übernehmen), `DefinitionType` (Standard/Url/Html), `EntityTypeIdentifiers` (an welchen Node-Typen verfügbar), `IsFileNamingUnlocked`, `SortKey`, `BlacklistedFields` (UI-Felder sperren). Gruppierung in `PropertyGroup`. `PropertySet` (pro Entity Type, global/default, `PropertySetConditions` = bedingte Pflichtfelder über Listenwerte, `FillMode` Optional/Recommended/Mandatory) → Validierung (`assets/validate`, `workspaces/{id}/assets/validate`). Property-Werte: `PropertyDto {PropertyDefinitionIdentifier, PropertyValues[] {PropertyListItemIdentifier, PropertyValue[] (je Sprache), UserContextIdentifier}, InheritToNodeTypeIdentifiers}`. Kategorie-Properties können auf Kind-Node-Typen **vererbt** werden (`inheritance-node-types`, `CategoryProperties` auf Asset). | PropertyDefinitions (8), PropertyGroups (6), PropertySets (5), DataLanguages (5), UserContexts (5); je Entität GET/POST/PUT/PATCH/DELETE `…/properties` und `bulk-delete-properties`; Ergebnisse `SetPropertyResultDto` (Reason: Success, CreateOrUpdateRestricted, InheritancePermissionDenied, PropertyDefinitionNotExists, NotEditable, ChangeSystemPropertyNotAllowed, Invalid, …) |
| 3 | **Assets (AssetSet / Version / Derivation)** | Asset = `AssetSet` mit Versionen (`AssetVersionDto`, `GetVersions`, Versions-Upload; Events `AssetVersionDeactivated/Restored`) und Derivaten je `DerivationType` (`AssetDerivationDto`: FileType, MimeType, Size, Hash, Uri/DownloadUri, Status Pending/Processing/Finished/Failed). `AssetDto`: Thumbnail-/Alternative-/Watermark-Thumbnail-URI, FileName, AssetType, Version, `DisplayProperties`, `Properties`, `CategoryProperties`, `PropertyDefinitionRestrictions`, `ParentCategoryIdentifier` + `LinkedCategoryIdentifiers` (**Multi-Kategorisierung: ein Asset hat eine Heimatkategorie und beliebige Links**). Operationen: Get (mit Version, Restrictions, Category-Properties), Delete (optional Recycle Bin), Bulk Delete, Duplikate (per MD5-`FileHash`), Activity Log, Tasks eines Assets, Relationen (typisiert, gerichtet, mit Pfadsegmenten; Suche über Relationen; Bulk-Delete), Kontaktabzug-PDF (8 Layouts, Metadaten je Layout konfigurierbar), Zip-Download ausgewählter Derivate (mit File Naming Definition; `stream`/`stream-formdata` für Browser-Download), On-Demand-Derivate starten, Derivat-Upload (manueller Ersatz eines Derivats). | Assets (47), AssetTypes (1), AssetUpload (3), Derivations (6), DerivationTypes (6), DerivationJobDefinitions (5), RecycleBin (2), Relations (1), RelationTypes (6) |
| 4 | **Upload** | Aktueller Flow (v6): `assets/upload/start` → `assets/upload/chunk` → `PUT` Chunk direkt an Storage (Shared-Access-URL, `maxChunkSizeBytes`) → `assets/upload/finish` mit `chunkIdentifiers`, optional `properties[]`, `copyMetadataFrom[]` (Properties von anderen Entitäten kopieren, alle oder ausgewählte), `createRelations[]`; Response mit `assetSetIdentifier`, `assetVersionIdentifier`, `assetStatus`, `existingAssetSetIdentifier` (Duplikat), Ergebnislisten. Upload-Typen: Asset (in Kategorie, optional Workspace/Task), AssetVersion, AssetDerivation. Legacy-Flow (v1–v5, obsolet): Sessions mit `sequencenumber`-Chunks über die API (`assets/upload/chunks/*`, `workspaces/…/upload/chunks/*`, Derivations). Blacklist verbotener Endungen (`file-types/forbidden`), File Recognition (A.8). | AssetUpload (3), `UploadStartRequestDto`, `ChunkUploadResponseDto`, `UploadFinishRequestDto/ResponseDto`, `AssetUploadDto`, `AssetVersionUploadDto`, `AssetDerivationUploadDto` |
| 5 | **Renditions / Processing** | `DerivationType` (systemeigen oder custom) mit `AssetTypeSettings` je Asset-Typ: `IsOnDemand`, `IsDefaultDerivation`, `AllowManualUpload`, `ShowInDownloads`, `UseAsThumbnail`, `UseAsPreview`. `DerivationJobDefinition` = JSON-`JobDefinition` je Input-Filetypen → Output-Filetyp; Bildpipeline (`ImageDerivationJobDefinitionDto`): Input (Frame, Farbraum, Profil, DPI, Kompression), Transformationsschritte (Scale absolut/relativ, Crop, Rotate, Blur, Sharpen), Opacity, Hintergrundfarbe (RGBA/CMYKA), Overlays (Wasserzeichen-Bilder mit Anker/Offsets), Output (Typ, Qualität, Farbraum/Profil/DPI). Video/Office/Print-Derivatoren per Name; Custom-Derivation-Steps mit Variablen und optionalen Dateien. Re-Derivation per Ids oder Suchabfrage, Status-Dashboards (Overview/Types/Details), Derivation-Jobs. Standard-Renditions siehe A.8. Message-Queue-Contracts: `JobStartMessage`, `JobStatusMessage`, `*DerivationStartMessage`, `DerivationStatusMessage`, `MetadataExtractionStart/Status/RestartMessage`. | Derivations, DerivationTypes, DerivationJobDefinitions, Jobs/derivations, Ordner `Webservice/ImageDerivation` und `MessageQueue/V1/Processing` |
| 6 | **Kategorien** | Baum (`GetCategoryTree`, Einzelknoten mit direkten Kindern), CRUD, Move, Copy (inkl. Assets), Assets verschieben/linken/unlinken, Quick Search, Zip-Download (rekursiv), eigene Properties mit Vererbung, Resource Permissions + `ResourcePermissionSettings` (Vererbung), Sortierung (`CategorySortOrderDto`). | Categories (30) |
| 7 | **Suche** | Elasticsearch-basiert. `SearchQueryBaseDto`: `SearchTerm`, `LanguageIdentifiers`, `ExactSearch`, `Operator` (And/Or), `AdvancedSearchFilters[]` (`FilterDto`: PropertyDefinition, EntityType, Terms, Operator aus 13: HasValue, HasNoValue, Equals, NotEquals, Contains, DoesNotContain, FuzzyEquals, FuzzyContains, GreaterThanOrEquals, LessThanOrEquals, Between, IdentifierEquals, IdentifierNotEquals; Sprachen), `Sort[]` (Property, Richtung, Sprache), `IncludePropertyDefinitionIdentifiers` (Projektion), `FacetPropertyDefinitionIdentifiers` + `FacetFilters[]` (Equals/IdentifierEquals) → `FacetDto {Items[] {Term, Count}}`. Asset-Suche zusätzlich: `SearchInDocumentExtraction` (Volltext in Dokumentinhalten), `Categories` + `RecursivelyFilterCategories`, `Context`, `Filename`, `FileHash`. Ergebnis v5: Offset/Limit; **v6: Cursor-Paging** (`SearchResultV6Dto`). Quick Search mit Auto-Complete-Vorschlägen und "did you mean". Suchbare Entitäten: Assets, Categories (quick), Custom Entities, Tasks, Workspaces, Licenses, Rights Owners, Billing Contacts, Purchases, License Offers, Relations. Saved Searches (Query als String, teilbar per Resource Permissions mit Benutzern/Gruppen). Re-Index je Entity Type als Job (Report, Abbruch). Header `X-Data-Languages`, `X-User-Contexts`, Query `langFallback`, `allDataLanguages`. | Search (4), SavedSearches (11), `*/search`, `*/quicksearch` |
| 8 | **Workspaces & Tasks (Kollaboration)** | `WorkspaceType` Upload / Collection / Collaboration; Templates (`IsTemplate`, `SourceTemplate`, "templated resource permissions" werden auf Instanzen übertragen); Zustände (`StateDto`: Sequenz, Vorgänger/Nachfolger-Transitionen, Default, Permissions je State) für Workspaces und Tasks (Kanban); Tasks mit Assignees, Watchers, DueDate, TaskType, Properties, Comments, Status-Transitionen, Move (State/Position), verlinkte Assets; Workspace-Properties, Property Set, `FinalizationCategoryIdentifier` + `CopyPropertiesToAssets`, Assets **validieren und finalisieren** (nach erfolgreicher Property-Set-Validierung in Zielkategorie verschieben), Properties auf Assets kopieren (alle/ausgewählt, Strategie), Avatar, Comments, Watchers, Activity Log, Benachrichtigungseinstellungen je Benutzer, berechtigte Benutzer/Gruppen abfragen, externe Shares. Root-Kategorie je Workspace. | Workspaces (65), Tasks (28), Comments (3) |
| 9 | **Shares (externe Weitergabe)** | Share auf Workspace: Name, `AssetTypeDerivations` (welche Derivate je Asset-Typ), `ValidUntil`, E-Mail-Empfänger mit Token; Empfänger hinzufügen/entfernen. Öffentlicher Zugriff ohne Login über `public/shares?token` und Zip-Download. Datei-Benennung per `ShareFileNamingDefinition`. | Shares (4), Public (3), `ShareDto`, `PublicShareDto`, `ShareDerivationDto` |
| 10 | **Custom Entities** | Frei definierbare Entitätstypen (z. B. Produkte, Personen) mit Properties, Relationen zu Assets/anderen Entitäten, Resource Permissions, Suche/Quick Search, Thumbnails (über Relationen), UI-Pinning. | CustomEntities (26), EntityTypes (6) |
| 11 | **Berechtigungen** | Drei Ebenen: (a) **Rollen** mit `ObjectTypePermissions` (331 feingranulare `PermissionDto`-Werte nach Muster `<Objekt><Create|Read|Update|Delete|View>`, plus Sonderrechte wie `GlobalAdmin`, `AdminView`, `WorkspaceFinalize`, `RuleReApplyJobCreate`, Limits `PropertyEditEntityLimit`/`PropertyExportEntityLimit`), (b) **Restriktionen je Rolle**: Property-Definition-Restrictions (Aktion je Property), Derivation-Type-Restrictions (je Asset-Typ), Property-Value-Restrictions (Aktionen nur bei bestimmtem Property-Wert – wertbasierte Zugriffssteuerung), (c) **Resource Permissions** (Benutzer oder Gruppe + Rolle) auf Kategorie, Workspace, State, Custom Entity, Saved Search, DRM-Bereich, mit Vererbung (`IsInherited`, `ResourcePermissionSettings`). Zuordnung Gruppe→Rolle je Tenant (`usergroup-permissions`). **Permission Materialization**: vorberechneter Berechtigungsbaum, Jobs mit Trigger (EntityCreated, EntityDeleted, FullPermissionUpdate, PermissionCreated), manuelles Re-Materialisieren eines Teilbaums. Externe Gruppen (IdP-Gruppen → interne Gruppen). | Roles (9), ObjectTypePermissions (1), PermissionMaterializations (2), ExternalGroups (6), Tenants, `*/resource-permissions` |
| 12 | **Benutzer & Gruppen** | Users CRUD, Suche/Sortierung, Passwort setzen, Entsperren, Avatar, Gruppenmitgliedschaften; Registrierungen (Liste, Detail, finalisieren = genehmigen/ablehnen), `AccessType` Permanent/Expiring, `AccountStatus`, `UserOrigin` (Sharedien/ExternalProvider), Kontaktdaten. User Groups CRUD + Mitglieder. `My`: Profil (Rollen je Tenant, Settings: Anzeigesprache, Locale, Datensprache, `UiSettings` (JSON), User Context, Such-Operator), Passwort ändern, Benachrichtigungen (Activity-Log-Notifications lesen/als gelesen markieren/löschen). | Users (13), UserGroups (7), My (7) |
| 13 | **Massenoperationen auf Metadaten** | Mass-Edit-Jobs (per Ids oder Suchabfrage; `PropertyActionStrategy` KeepExisting/OverwriteExisting/Abort/DeleteExisting/Merge je Property; Report mit Zählern, Mutationen je Asset/Version; `ApplicableAssetCountLimit` aus Rolle), Property-Import (Excel/CSV-Datei; Meldungen mit Severity/Typ; Entity-/Mutationsreport), Property-Export (xls/xlsx/csv; Label-Sprache, Locale, Datensprachen, User Contexts; Browser-Variante `export-formdata`), Bulk-Delete Properties. | Properties (14), Jobs (14) |
| 14 | **KI-/Metadaten-Extraktion** | `PropertyExtractionDefinition`: Typ `DetectTags`, Provider AzureCognitiveServices / Imagga / Clarifai, Input-Derivat + Asset-Typen, Quellen mit Mappings auf Property Definitions; Extraktion für Asset-Ids oder Suchabfrage, optional direkter Import; Jobs mit Ergebnissen je Asset/Quelle (Extracted/Imported/Skipped/Error). Technische Metadaten (EXIF/IPTC/XMP/Audio/Video) werden beim Import über konfigurierbare Mappings extrahiert (A.3.5). | Properties/extract*, property-extraction-definitions, Jobs/property-extractions |
| 15 | **Rule Engine** | Regeln je Node-Typ mit `Filters[]`, `Triggers[]` (`TriggerOperator`: Equals, NotEquals, GreaterThan, LessThan, GreaterThanOrEquals, LessThanOrEquals, Contains, HasRelationType, IsNewVersion, ValueChanged, IsNewCategory, RegexMatch; Property + AdditionalData) und `Actions[]` (Property setzen, Extraktionsdefinition ausführen, `Template` (Werte-Template), `OnTriggerMatched` true/false = Aktion bei Treffer oder Nicht-Treffer). Ausführung automatisch (Hintergrund, `QueryBatchSize`) oder manuell (`rules/execute` für Entitäten/Regeln); Reports mit Zählern (matched/not matched/executed/skipped/error) und Detailseiten. | Rules (9) |
| 16 | **Wasserzeichen & Dateinamen** | `WatermarkDefinition` (verknüpft Derivation Job Definition + Property Definition; `DisplayInUI`) mit `WatermarkTriggers` (Listenwert oder Bool-Wert einer Property → Wasserzeichen-Bilddatei als `FileReference`); Wasserzeichen-Thumbnail auf Asset. `FileNamingDefinition`: Format mit Tokens (Property/Variable), `CharacterCasing`, `ReplaceIllegalCharacters`, `AbsentPropertyStrategy` (PropertyDefinitionIdentifier/Skip/Abort), je Tenant, globaler Default, Vorschau auf Assets; genutzt für Downloads und Shares. `FileReferences`: Upload/Liste/Löschen von Hilfsdateien (Watermark, Avatar). | WatermarkDefinitions (9), FileNamingDefinitions (7), FileReferences (3) |
| 17 | **Archive** | Zip-Asset entpacken als Job (`ArchiveExtractionMode` ExtractWithFullPath/ExtractFilesOnly, Zielkategorie, Asset nach Extraktion löschen, Filter aus Konfiguration), Report je Eintrag (Extracted/Filtered/Error). | Extractions (2), Jobs/extractions |
| 18 | **DRM / Lizenzmanagement** | Rights Owners (Avatar, Billing Contact, Properties), Billing Contacts, Licenses (Laufzeit, Properties, zugeordnete Entitäten/Assets, `PurchaseDurationInDays`), Buyouts je Lizenz (Basispreis, Währung, Zusatzgebühr %, Zeitraum, Rights Owner, Preisliste), Price Lists mit `ContextPercentages` je User-Context-Group, User Context Groups (hierarchisch, Typ Group/UserContext), License Offers (Suche: Preise je Kontextgruppe, Alternativgruppen, enthaltene Assets), Purchases (Kauf für Kontextgruppe ab Startdatum; OrderId, Ablauf, Preise, Käufer, enthaltene Entitäten), Suche/Quick Search für alle DRM-Entitäten, Resource Permissions für den DRM-Bereich. UI: Kaufdialog mit Hilfe-Link, Ablauf-Banner auf Karten. | DrmLicenses (58) |
| 19 | **Jobs, Activity Log, Benachrichtigungen** | Einheitlicher `JobStatusDto` (EnqueuedForExecution/Executing/Executed/Failed); Job-Listen für Mass Edits, Derivations, Imports, Extractions, Property Extractions, Re-Index, Permission Materialization, Rule Executions. Activity Logs je Asset/Task/Workspace/Application Settings (`ActivityLogTypeDto` 14 Typen, `ActivityLogChangeTypeDto` 21 Änderungsarten inkl. Assignee, Comment, PropertyValue, Relation, ResourcePermission, State, Share, ConfigurationValue). Benachrichtigungen an Benutzer (`ActivityLogNotificationDto`, ReadAt) + Echtzeit-Push per SignalR (`Message {ActivityLogUpdate}`); Watcher-Konzept auf Workspaces/Tasks; Abschaltbar je Workspace. | Jobs (14), My (7), `*/activity-logs`, SignalR-Contracts |
| 20 | **System / Admin** | Application Settings zur Laufzeit (DB-Provider, Pfadfilter, Overrides, Bulk-Delete, Activity Log), `info` (API-Infos), File Types (inkl. verbotene), Object-Type-Permissions-Katalog, Value-Types-Katalog, Data Languages, Re-Index, Hangfire-Dashboard, Recycle Bin (Baum, leeren). | ApplicationSettings (6), Info (1), FileTypes (2), Search (4), RecycleBin (2) |

### D.2 Workflows, die nur die Doku beschreibt

1. **Erstinbetriebnahme** (A.2): Bootstrap-Admin → Admin-Gruppe → Rollenzuweisung auf Tenant, Kategorie-Root und Recycle Bin → Reindex. Berechtigungen sind nicht global, sondern pro Wurzelknoten zu vergeben.
2. **Integrations-Upload** (A.6): Token holen (ROPC) → Start → Chunk-Schleife direkt gegen Storage → Finish; Berechtigungen des Benutzers gelten. Der SDK-Client kapselt dies inkl. `ArrayPool`-basiertem Chunking (min. halbe Blockgröße) und Metadaten-Kopie/Relationen beim Finish.
3. **Eventstream-Integration** (A.6, C.3): eigene durable Subscription je Integration, immer mindestens ein Filter (`x_sharedien_*`), Sample-Consumer zum Test; Event-Payload mit `AffectedData[]` (ObjectType, Id, EventAction, EventActionDetails, AdditionalData) und `User`-Dictionary.
4. **Account-Lifecycle** (A.3.2): Registrierung (E-Mail-Verifikation, Genehmigung) → Expiring-Account → Renewal-Einladungen/Reminder → Expired → optionale automatische Löschung; Lockout nach Fehlversuchen; Admin-Entsperrung (`users/{id}/unlock`).
5. **Release-Update-Prozedur** (A.10): Update Notes in Reihenfolge abarbeiten; typische Schritte: Konfigurationsschlüssel migrieren, Processing-Abhängigkeiten (ImageMagick, ExifTool, Blender) austauschen, Service-Bus-Topics/Subscriptions anlegen, **Re-Index bestimmter Entity Types** nach Index-Mapping-Änderungen, Frontend-Branding migrieren.
6. **SAML-Zertifikatswechsel** und **Azure-AD-App-Registrierung** (A.4) als wiederkehrende Betriebsaufgaben.
7. **Log-Korrelation** über `X-Operation-ID` (A.5) – Support-Prozess: Benutzer meldet Operation-Id aus der UI.
8. **Kontaktabzug**, **Zip-Download mit Dateinamensregeln**, **Archiv-Extraktion**, **Workspace-Finalisierung** und **Mass Edit per Suchabfrage** als Bulk-Workflows der UI (D.1).

### D.3 Best Practices und Empfehlungen aus der Doku
* Basiskonfiguration nie ändern, nur Overrides; Secrets (Signing Keys, Client Secrets, Access Keys) müssen dienstübergreifend übereinstimmen (`ApiClientSecret`, `SharedAccessSigningKey`, `UploadSigningKey`).
* Hinter Reverse Proxy immer `KnownProxies`/`KnownNetworks` setzen; ein SAN-Zertifikat je Umgebung; HTTPS auch in Testumgebungen.
* Token-Lifetime, Validierungs-Cache (`ValidationCache.CacheExpirationInSeconds`) und `LastLoginDateThresholdInSeconds` aufeinander abstimmen; Public Clients rotieren Refresh Tokens.
* E-Mail-Whitelist (`AllowedRecipients`) in Test-/Staging-Systemen; Mail-Queue mit Retention.
* Application Insights pro Umgebung; Log Sanitation für PII; `HttpApiRequestResponseLoggingEnabled`/`SelfLogEnabled` nur temporär.
* Eventstream: immer filtern, Subscriptions mit kurzer TTL, keine Durability bei Service Bus anfordern.
* Azure-AD-Gruppen per Graph API (nicht im Token) und über GUIDs mappen; ADFS-Claims mit Regex-Filter einschränken.
* Elasticsearch: Mapping-Änderungen erfordern Reindex (Alias-Swap ohne Downtime); Reindex nach Updates gemäß Notes.
* File Recognition nur bei Bedarf aktivieren (Overhead je Import); `Strict` nur zur Verifikation.
* Rule Engine `QueryBatchSize` als Performance-Stellschraube; Rollen-Limits für Mass Edit/Export nutzen.

### D.4 Grenzen und Constraints (dokumentiert oder aus DTOs ersichtlich)
* Max. 4 parallele API-Versionen; nur die in A.6 gelisteten Operationsgruppen sind versioniert/stabil. Aktuelle Version v6; v1/v2 abgekündigt, Reste im Code (`ExternalGroups v1`, `Jobs/mass-edits v1`, `PropertyDefinitions v1`, Legacy-Upload v2/v3, `assets/relations v4`).
* String/Text bis 2^30-1 Bytes; Integer int64; Decimal 18/10; MD5-Hash (nicht kryptografisch sicher, nur Duplikaterkennung).
* Azure-AD-Token: max. 200 (JWT) / 150 (SAML) Gruppen → Graph-Abfrage; Direct vs. Transitive Membership konfigurierbar.
* Lockout-Defaults 10 Versuche / 15 min / Reset 10 min; Access Token 60 min; Refresh 30 d absolut / 15 d sliding.
* Standard-Renditions fest (Thumbnail 240², Screen 1600×1200, Print 3508² CMYK); INDD ohne Rendition; CAD/RAW-Formate nur als Typ erkannt, keine Renditions dokumentiert.
* Windows Auth nur mit IIS; Identity Service als einziger Dienst IIS-fähig dokumentiert.
* Themes sind "PREVIEW" (Properties können sich ändern); IE11 nicht unterstützt.
* Frontend-URL-Länge (`urlMaxLength`), Seitengrößen konfigurierbar; Avatar-Größe begrenzt.
* Mass Edit: `ApplicableAssetCountLimit` je Rolle; Export-Limit je Rolle; Property-Import nur Excel/CSV mit definiertem Format (Fehlertypen: FileFormatError, MetadataTabNotFound, EntityIdentifierNotFound, EntityTypeNotFound, ParseError).
* Workspace-Update/-Delete nur für eigene Workspaces (laut Summary), sonst Berechtigung nötig.
* Löschen einer Parent-Child-Relation löscht standardmäßig das Asset-Set (`ParentChildRelationDeleteBehavior`).
* Docker-Compose-Setups ausdrücklich nicht produktionsreif; SDK nur auf Anfrage und lizenzpflichtig.

### D.5 Abweichungen Doku ↔ Code (für die Übernahme relevant)
* Upload-Doku nennt `POST /api/v3/assets/upload/start` und `PUT …/finish`; der generierte Client nutzt `/api/v6/…` und `POST …/finish` (Doku veraltet in Version/Verb).
* Die Doku-Beispielfilter `x_sharedien_event_action = 'AssetSet'` ist ein Tippfehler (gemeint `x_sharedien_object_type`).
* `api-diff/specs` sind keine echten Specs (nur `application-settings`); vollständige Specs sind zur Laufzeit unter `<API>/swagger/v{1..6}/swagger.json` abrufbar.
* Konfigurationsdoku beschreibt teils Stand ≤3.5 (`Sharedien.StorageHandler.*`, `StorageConfigurationType`), die Update Notes 3.6+ ersetzen diese Schlüssel; beide Generationen sind in A.3 aufgeführt.
* Nicht in der Doku, aber in der API vorhanden (nur über Code/UI erschließbar): Custom Entities, Rule Engine (nur Setting), DRM/Lizenzen (nur UI-Setting), Watermarks, File Naming Definitions, Property Extraction (KI-Tagging), Permission Materialization, Property Value Restrictions, States/Transitions, Shares, Saved Searches, Contact Prints, Property Import/Export, User Contexts, Activity Logs/Notifications.

### D.6 Lückenliste / offene Punkte für Shareme
* Kein Benutzer-/Admin-Handbuch im Repo → UI-Verhalten (Dashboard, Asset Browser, Download Basket, Admin-Masken) muss aus dem Frontend-Code (`src/Frontend`) abgeleitet werden.
* Message-Queue-Contracts für interne Jobs (`Advellence.Sharedien.Internal`) sind nur teilweise öffentlich (Processing-Messages ja, interne API-Jobs nein).
* Datenbankschema (DbUp-Migrationen in `Sharedien.Api.DbMigration`) und Elasticsearch-Mappings sind nicht dokumentiert; für Migration bestehender Daten erforderlich.
* Identity-Service-API (Registrierung, Passwort-Reset, Renew-Account-Endpunkte `Home/RenewAccount`, `VerifyEmailEndpoint`, `ApprovalEndpoint`) hat keine OpenAPI-Beschreibung im Repo.
* Storage-Handler- und Processing-APIs haben eigene Swagger-UIs (Docker-Ports 5050/5052), deren Specs hier nicht enthalten sind (`StorageHandler.Api.Contracts` existiert als Projekt).
