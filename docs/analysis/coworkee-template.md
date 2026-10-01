# Tiefenanalyse: Clean-Architecture-Template "Coworkee"

Quelle: `C:\dev\privat\github\CleanArchitectureBaseBlazor` (Branch im Working Tree: **`wayes-edith`**, letzter Commit 216ca23 vom 2025-12-06, 360 Commits gesamt).
Stand der Analyse: 2026-09-29. Analysiert wurde der Working Tree; `bin/`, `obj/`, `.vs/`, `.idea/`, `node_modules/` wurden ignoriert.

> **Wichtig vorab:** Der ausgecheckte Branch `wayes-edith` ist eine **Spezialisierung des Templates für eine Kunden-App "WayesEdith"** (Insolvenz-/Lohndaten-Import aus PDF/Excel). Das ursprüngliche Beispiel-Domain (`Brand`/`Product`) wurde dort zu `Mandate`/`SocialData` umgebaut, `ApplicationName` ist `"WayesEdith"`, das Coworkee-Logo wurde durch ein "WAYES"-Text-SVG ersetzt. Der generische Template-Stand liegt auf **`master`** (b4eecad, 2025-10-28; 18 Commits hinter `wayes-edith`, 3 Commits voraus: "odata step 1", "Add delta", "add new data grid"). Wo relevant, wird unten auf `master` verwiesen. Zwei ältere Branches (`app-framework-lib-2`, `origin/app-framework-3`) enthalten bereits **Vorarbeiten für die Lib-Familie** (siehe Abschnitt 11.6).

---

## Inhalt

1. Solution-Struktur
2. Domain
3. Application
4. Infrastructure
5. Server
6. Client (Blazor WASM)
7. Shared
8. SDK
9. Aspire
10. Tests
11. Bewertung / Empfehlungen für die Lib-Familie
12. Coworkee-Icon / Logo
13. Anhang: parserMain, Branches, Secrets

---

## 1. Solution-Struktur

### 1.1 Projekte (`Coworkee.sln`)

| Projekt (sln-Name) | Pfad | SDK / Typ | AssemblyName | Referenzen (Projekt) | Zweck |
|---|---|---|---|---|---|
| Domain | `src/Domain/Domain.csproj` | Microsoft.NET.Sdk | Coworkee.Domain | – | Entities, Base-Klassen, Enums |
| Shared | `src/Shared/Shared.csproj` | Microsoft.NET.Sdk | Coworkee.Shared | – | Wrapper, Konstanten, Permissions, YAML-Lokalisierung, Mail-Templates |
| Application | `src/Application/Application.csproj` | Microsoft.NET.Sdk | Coworkee.Application | Domain, Shared, **parserMain/WayesPDFParser** | CQRS-Features (MediatR), Contracts, Hubs, Validators |
| Infrastructure | `src/Infrastructure/Infrastructure.csproj` | Microsoft.NET.Sdk (+FrameworkReference AspNetCore.App) | Coworkee.Infrastructure | Application, Shared | EF Core DbContext, Identity, Repositories, Services |
| SDK | `src/SDK/SDK.csproj` | Microsoft.NET.Sdk, `IsPackable`, `GeneratePackageOnBuild` | Coworkee.SDK (PackageId `Coworkee.SDK`, Version `0.0.0-dev`) | Application | NSwag-generierter C#-Client + Angular-Client |
| Client | `src/Client/Client.csproj` | Microsoft.NET.Sdk.BlazorWebAssembly | Coworkee.Client | SDK, Shared; Protobuf `..\Server\gRPC\Main.proto` | Blazor WASM UI (MudBlazor + MudBlazor.Extensions) |
| Server | `src/Server/Server.csproj` | Microsoft.NET.Sdk.Web | Coworkee.Server | Application, Infrastructure, Shared, ServiceDefaults, Client (nur wenn `HostClientInServer`) | ASP.NET Core API + Blazor-Host |
| Coworkee.AppHost | `src/Aspire/Coworkee.AppHost/Coworkee.AppHost.csproj` | Aspire.AppHost.Sdk 9.0.0 | Coworkee.AppHost | Client, Server (+ Linked-Compile-Files aus Application/Infrastructure/Shared) | Aspire-Orchestrierung |
| Coworkee.ServiceDefaults | `src/Aspire/Coworkee.ServiceDefaults/Coworkee.ServiceDefaults.csproj` | Microsoft.NET.Sdk | Coworkee.ServiceDefaults | – | OpenTelemetry, HealthChecks, Resilience |
| Application.IntegrationTests | `tests/Application.IntegrationTests/...csproj` | xUnit | Coworkee.Application.IntegrationTests | SDK, Server | WebApplicationFactory + Testcontainers MsSql |
| Application.UnitTests | `tests/Application.UnitTests/...csproj` | NUnit | Coworkee.Application.UnitTests | Application, Infrastructure | Unit-Tests |
| Domain.UnitTests | `tests/Domain.UnitTests/...csproj` | NUnit | Coworkee.Domain.UnitTests | Domain | Unit-Tests |
| Application.UITests | `tests/Application.UITests/...csproj` | xUnit + Playwright + Aspire.Hosting.Testing | (default) | Coworkee.AppHost | E2E Login-Test |
| docker-compose | `docker-compose.dcproj` | Docker Compose | – | – | VS Docker-Integration |
| WayesPDFParser | `parserMain/WayesPDFParser/WayesPDFParser.csproj` | Microsoft.NET.Sdk (net9.0) | WayesPDFParser | – | **Fremdprojekt** (siehe 13.1) |
| WayesPDFParser.UI | `parserMain/WayesPDFParser.UI/WayesPDFParser.UI.csproj` | WPF (net9.0-windows) | WayesPDFParser.UI | WayesPDFParser | **Fremdprojekt** (siehe 13.1) |

Leere Verzeichnisse ohne csproj (nur `bin/`/`obj/`-Reste älterer Branches): `src/Core`, `src/Client.Blazor`, `src/Example.Application`, sowie alle sechs `lib/Coworkee.*`-Ordner (271 Dateien, ausschließlich `bin/`/`obj/`; enthält u. a. `lib/Coworkee.Application/bin/Debug/net8.0/Coworkee.Core.dll` als Artefakt eines früheren Versuchs).

### 1.2 Zentrale Build-Konfiguration

- `Shared.props` (Root, von allen Projekten importiert): `TargetFramework=net9.0`, `LangVersion=latest`, **`Nullable=disable`** (Kommentar: "breaks db operations but we want to enable it"), `MicrosoftPackagesVersion=9.0.10`, `HostClientInServer=true` → `DefineConstants=HostClient`.
- Kein `Directory.Build.props` / `Directory.Packages.props` (kein Central Package Management). Versionen sind je csproj gepinnt; MS-Pakete über `$(MicrosoftPackagesVersion)`.
- `version.json` (Nerdbank.GitVersioning-Schema): `version: 2.2`, cloudBuild buildNumber enabled, publicRelease nur auf `refs/heads/main`. Das NB.GV-Paket ist in keinem csproj referenziert – das File ist damit **ohne Wirkung** (unklar, ob extern per CI verwendet).
- `rename.ps1`: Template-Umbenennung (installiert `vsrenamer` global, ersetzt Namen in Dateien/Dateinamen/Ordnern nach Dateimaske, aktualisiert `RootNamespace`/`AssemblyName` in csproj). Kommentare teilweise deutsch.
- `update-packages.ps1`: `dotnet add package` für jede PackageReference in allen `*proj` (Brute-Force-Update auf latest).
- `docker-compose.yml`: Service `Coworkee` (Dockerfile `src/Server/Dockerfile`, Ports 5000/5001, Volumes für HTTPS-Zertifikat, DataProtection-Keys, `./src/Server/Files:/app/Files`) + `db` (`mcr.microsoft.com/mssql/server`, SA-Passwort im Klartext). `docker-compose.client.yml`: separater nginx-Client (`src/Client/Dockerfile`, Port 5002, Build-Arg `BACKEND_ORIGIN`).
- `azure.yaml` (Root): azd-Projekt `clean-architecture-base-blazor`, Service `app` = AppHost, Host `containerapp`. Zweites `azure.yaml` in `src/Aspire/Coworkee.AppHost/` (Name `coworkee-apphost`). `.azure/COWORKEE/config.json` enthält Infra-Parameter (AdminUserName/Password "admin", SQL/PG-Passwörter als Vault-Referenzen).
- `.github/workflows/master_coworkee.yml`: Build+Deploy auf Azure Web App "Coworkee" (Trigger `master`), **veraltet** (`dotnet-version: 7.0.100`, actions v1/v2). `delete_artifacts.yml`: nächtlicher Artefakt-Cleanup.
- `launchSettings.json` (Root): Docker-Compose-Profil.

### 1.3 Packages je Projekt

**Domain**: keine (Nextended.Core auskommentiert).

**Shared**: AKSoftware.Localization.MultiLanguages.Blazor 1.5.0, Microsoft.Extensions.Identity.Stores, Microsoft.Extensions.Localization.Abstractions, System.IdentityModel.Tokens.Jwt 8.14.0, **Nextended.Core 9.0.27**, Microsoft.Rest.ClientRuntime.Azure 3.3.19 (nur für `FilterString`/OData in `TransferableExpression`). Build-Target `Translate` (Debug): installiert `ResourceTranslator.CLI` global und übersetzt `Resources/en-US.yml` in 16 Zielsprachen (Azure Translator, Key in `resourceTranslationOptions.json` – **Secret im Repo**).

**Application**: FluentValidation 11.11 (+DI), **MediatR 12.5.0**, LazyCache 2.4, Hangfire.Core 1.8.18, Microsoft.EntityFrameworkCore, SignalR.Common + SignalR.Core 1.2.0 (alt), Newtonsoft.Json, System.Linq.Dynamic.Core, StringToExpression 2.2 (OData-`$filter` → Expression), Hashids.net, SendGrid (nur für `MailRequest`-Typ? unklar – Paket ist referenziert), **GptInvoke 1.2** (AI-Action-Invoker), **OllamaSharp 5.1**.

**Infrastructure**: CsvHelper 27.2 + CsvHelper.Excel.Core, Blazored.LocalStorage (fremd hier – nur für `IStorageProvider`-Interface), Hangfire.Core, **RazorEngineCore 2024.4.1** (Mail-Templates), Microsoft.AspNetCore.Components.Authorization, Microsoft.AspNetCore.Identity.EntityFrameworkCore, **Microsoft.EntityFrameworkCore.SqlServer**, **Aspire.Npgsql.EntityFrameworkCore.PostgreSQL 9.2**, PostgreSQLCopyHelper, EF Tools, MailKit/MimeKit 4.11.

**Server**: FluentValidation.AspNetCore, Microsoft.Azure.SignalR 1.30, Nextended.Imaging, Hangfire 1.8.18 + Hangfire.PostgreSql, LazyCache.AspNetCore, MediatR, Mvc.Versioning 5.1 (+ApiExplorer), JwtBearer, WebAssembly.Server, EF Proxies, **Aspire.Keycloak.Authentication 9.0.0-preview.5**, Mime 3.7, NSwag.AspNetCore + NSwag.MSBuild 14.3, **Nextended.CodeGen 9.0.17** (generiert `ServerConfiguration` aus appsettings.json), Grpc.AspNetCore + Web, SkiaSharp + Svg.Skia (Logo→PNG), **Microsoft.AspNetCore.OData 9.4**, **TranslatorService 3.0.11** (Azure Translator), nksoft.MakeGenericAgain (Post-Processing des NSwag-Clients).

**Client**: MudBlazor 8.9.0, **MudBlazor.Extensions 8.9.0-prev-2507101529-main**, MudExRichTextEditor 8.5.2 (nicht aktiv genutzt), BlazorJS 2.2 (nicht genutzt), Blazored.LocalStorage 4.5, Blazored.FluentValidation 2.2, Toolbelt.Blazor.HttpClientInterceptor 10.2, SignalR.Client, Components.WebAssembly(.Authentication/.DevServer), Extensions.Http(.Polly), Identity.Core, Localization, ServiceDiscovery 9.2, Microsoft.TypeScript.MSBuild 5.8, Google.Protobuf + Grpc.Net.Client(.Web) + Grpc.Tools.

**AppHost**: Nextended.Aspire 9.0.25, Aspire.Hosting.AppHost 9.5.1, Aspire.Hosting.SqlServer/PostgreSQL/Azure.Storage/Azure.SignalR 9.5.1, Aspire.Hosting.Keycloak 9.0.0-preview.5, Aspire.Azure.Messaging.ServiceBus, Aspire.Microsoft.Azure.Cosmos, CommunityToolkit.Aspire.Hosting.Ollama 9.8, Aspire4Wasm 5.0, System.IO.Hashing.

**ServiceDefaults**: Microsoft.Extensions.Http.Resilience, ServiceDiscovery, OpenTelemetry (OTLP, Prometheus, AspNetCore, Http, Runtime, GrpcNetClient), Azure.Monitor.OpenTelemetry.AspNetCore.

**Tests**: xUnit 2.9.3 / NUnit 4.3.2, FluentAssertions 8.2, Moq 4.20, Testcontainers(.MsSql) 4.3, Mvc.Testing, EF InMemory, Aspire.Hosting.Testing 9.2, Microsoft.Playwright 1.51.

### 1.4 Abhängigkeitsgraph (Ist)

```
Domain  <-- Application <-- Infrastructure <-- Server
Shared  <-- Application, Infrastructure, Client, Server, SDK(transitiv)
Application <-- SDK <-- Client
WayesPDFParser <-- Application   (!! Fremdprojekt in der Application-Schicht)
Server --> Client (nur bei HostClientInServer)
AppHost --> Server, Client
```

Verstöße gegen Clean Architecture: `Application` referenziert `Microsoft.EntityFrameworkCore` (Includes in Query-Handlern), `Microsoft.AspNetCore.SignalR.Core` (Hub-Klassen), `Hangfire.Core` (`BackgroundJob.Enqueue` in Handlern), `Microsoft.AspNetCore.Identity` (`IdentityResult` in `Errors.cs`) und das Fremdprojekt `WayesPDFParser`. `SDK` referenziert die komplette `Application` (weil NSwag `generateDtoTypes=false` setzt und die DTOs wiederverwendet) – dadurch zieht der **Blazor-Client transitiv MediatR, Hangfire, EF Core, OllamaSharp** etc. in die WASM-Payload.

---

## 2. Domain (`src/Domain`)

### 2.1 Base-Klassen und Contracts (`src/Domain/Contracts/`)

| Datei | Inhalt |
|---|---|
| `IEntity.cs` | `IEntity` (Marker), `IEntity<TId> { TId Id }` |
| `IAuditableEntity.cs` | `IAuditableEntity : IEntity { CreatedBy, CreatedOn, LastModifiedBy, DateTime? LastModifiedOn }`, `IAuditableEntity<TId>` |
| `AuditableEntity.cs` | `abstract class AuditableEntity<TId> : IAuditableEntity<TId>` – die Basisklasse aller Entities. **Kein Soft-Delete**, kein `IsDeleted`, kein Query-Filter (nur `ApplicationUser` in Infrastructure hat `IsDeleted`/`DeletedOn`, ohne Filter). Kein DomainEvent-Mechanismus, keine Value Objects, kein Aggregate-Root-Konzept. |
| `IEntityExtendedAttribute.cs`, `IEntityAuditableExtendedAttribute.cs` | EAV-Muster ("Extended Attributes"): `Type (EntityExtendedAttributeType)`, `Key`, `Decimal?`, `Text?`, `DateTime?`, `Json?`, `ExternalId?`, `Group?`, `Description?`, `IsActive`, `EntityId` |
| `AuditableEntityExtendedAttribute.cs` | `abstract AuditableEntityExtendedAttribute<TId, TEntityId, TEntity>` mit Navigation `Entity` |
| `IEntityWithExtendedAttributes.cs`, `AuditableEntityWithExtendedAttributes.cs` | `ICollection<TExtendedAttribute> ExtendedAttributes` (HashSet) |

### 2.2 Enums

- `src/Domain/Enums/EntityExtendedAttributeType.cs`: `Decimal=1, Text=2, DateTime=3, Json=4` (byte).

### 2.3 Entities

| Entity | Datei | Basis | Felder | Generisch / Beispiel-Domain |
|---|---|---|---|---|
| `Document` | `Entities/Misc/Document.cs` | `AuditableEntityWithExtendedAttributes<int,int,Document,DocumentExtendedAttribute>` | Title, Description, IsPublic, URL, DocumentTypeId, DocumentType (nav), `DocumentTypeName` (computed) | **generisch** (für Shareme direkt relevant) |
| `DocumentType` | `Entities/Misc/DocumentType.cs` | `AuditableEntity<int>` | Name, Description | generisch |
| `DocumentExtendedAttribute` | `Entities/ExtendedAttributes/DocumentExtendedAttribute.cs` | `AuditableEntityExtendedAttribute<int,int,Document>` | – | generisch (EAV) |
| `Notification` | `Entities/Notifications/Notification.cs` | `AuditableEntity<int>` | UserId, NotificationTypeId, Subject, Excerpt, Content, Url, IsRead, SentAsMail | generisch |
| `Language` | `Entities/Localization/Language.cs` | `AuditableEntity<int>` | IsRTL, IsActive, CultureCode, DisplayName | generisch |
| `Translation` | `Entities/Localization/Translation.cs` | `AuditableEntity<int>` | Key, Value, CultureCode | generisch |
| `UserInformations` | `Entities/Identity/UserInformations.cs` | `AuditableEntity<int>` | Addresses (nav), LastLoginDate, Language, IsOnline | generisch |
| `Address` | `Entities/Identity/Address.cs` | `AuditableEntity<int>` | Name, City, Street, Country, PostalCode, HouseNumber, UserInformationsId | generisch |
| `Mandate` | `Entities/Catalog/Mandate.cs` | `AuditableEntity<int>` | Name, Description, ReferenceNumber, `ICollection<SocialData> Entries` | **Beispiel-Domain (WayesEdith)**; auf `master`: `Brand` |
| `SocialData` | `Entities/Catalog/SocialData.cs` | `AuditableEntity<int>` | Person, Employment, Insurance, Payroll, Banking (je Owned/Nav), MandateId | **Beispiel-Domain**; auf `master`: `Product` |
| `PersonData`, `EmploymentData` (Datei `EmployeeData.cs`), `InsuranceData`, `PayrollData`, `BankingData` | `Entities/Catalog/*.cs` | `AuditableEntity<int>` | Dutzende deutsch dokumentierte Lohn-/SV-Felder (überwiegend `string`, viele `TODO: enum/bool`) | **Beispiel-Domain**, komplett entfernbar |

Sonstiges: `src/Domain/Coworkee.Domain.xml` (XML-Doc-Output, eingecheckt). Das Domain-Projekt ist frei von Paketabhängigkeiten – gut wiederverwendbar als `Coworkee.Domain` (Base-Klassen + generische Entities Document/DocumentType/Notification/Language/Translation/UserInformations/Address).

---

## 3. Application (`src/Application`)

### 3.1 Registrierung (`src/Application/DependencyInjection.cs`)

```csharp
services.AddIdHashing(config);                       // Hashids-Singleton + globale Nextended-ClassMapping-Converter (int <-> string)
services.TryAddScoped<ISessionProvider, SimpleSessionProvider>();
services.AddValidatorsFromAssembly(...);             // FluentValidation
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...));
services.AddAllWithRegisterAttribute(assembly);     // Nextended: [RegisterAs(...)]-Attribut-Scan
```

Zusätzlich `AddExtendedAttributesHandlers()` (aus `Server/Startup.cs` aufgerufen): reflektiert alle Subklassen von `AuditableEntityExtendedAttribute<,,>` im Domain-Assembly und registriert pro Typ sechs geschlossene generische `IRequestHandler<,>` (AddEdit, Delete, GetAllByEntityId, GetById, GetAll, Export) – ca. 90 Zeilen `MakeGenericType`-Boilerplate.

Registrierungsmechanismus im ganzen Projekt: **Nextended.Core `[RegisterAs(typeof(IFoo), order, ServiceLifetime=..., RegisterAsImplementation=..., Enabled=...)]`** plus eigene Ableitungen in `src/Application/Contracts/Attributes/`: `RegisterIfConfigAttribute` (abstract) → `RegisterAsIfConfigValueEqualsAttribute` / `...NotEquals` / `...IsEmpty` / `...IsNotEmpty`. Diese lesen `ServerConfiguration.Instance` (statisch, `JObject.ToFlatDictionary`) und registrieren eine Implementierung nur, wenn ein Config-Pfad einen Wert hat (z. B. `SMTPMailService` nur wenn `MailConfiguration.SendGridApiKey` leer). Brauchbares, aber statisch gekoppeltes Muster (statische Instanz muss vor dem DI-Scan gebunden sein).

### 3.2 MediatR-Verdrahtung (Tiefe)

Zahlen (ohne bin/obj/generierte Dateien, unter `src/`):

| Symbol | Dateien |
|---|---|
| `IMediator` | 32 |
| `Mediator.Send(...)` | 15 |
| `IRequest<>` / `IRequest` | 24 |
| `IRequestHandler<>` | 19 |
| `INotification` / `INotificationHandler<>` | 2 (`ClientEventNotification`, `ClientEventDispatcher`) |
| `IPipelineBehavior<,>` | 5 |
| `IRequestPreProcessor<>` | 1 (`LoggingBehaviour`) |
| `PublishClientEvent(s)` (Extension auf `IMediator`) | 8 |

**Wo MediatR außerhalb von `Application` verwendet wird:** `src/Infrastructure/Services/Identity/AccountService.cs`, `IdentityService.cs`, `UserService.cs`, `src/Infrastructure/Services/ServerNotificationService.cs` (alle nur für `PublishClientEvent`), `src/Server/Controllers/BaseApiController.cs` (`protected IMediator Mediator => _mediatorInstance ??= Get<IMediator>()`), 11 Controller (`Mediator.Send`), `src/Server/gRPC/MainDataService.cs`, `src/Server/BackgroundServices/ProductFeedSyncService.cs`. **Der Client referenziert MediatR nicht direkt** – aber transitiv über SDK→Application, weil Command-Klassen wie `AddEditMandateCommand` als API-Payload-Typen dienen und `IRequest<>` implementieren (die WASM-Payload enthält MediatR.dll).

**Pipeline Behaviors** (`src/Application/Common/Behaviours/`, alle via `[RegisterAs(typeof(IPipelineBehavior<,>), Transient)]`):

| Behaviour | Datei | Funktion |
|---|---|---|
| `AuthorizationBehaviour` | `AuthorizationBehaviour.cs` | Liest `[CustomAuthorize]`-Attribute vom Request-Typ, `ICustomAuthorizeAttributeHandler.EnsureIsAuthorizedForAllAsync` → `IPermissionService` (DB-basiert, nicht JWT-Claims) |
| `ClientEventBehaviour` | `ClientEventBehaviour.cs` | Publiziert `BeforeRequest<TRequest>` und `AfterRequest<TRequest,TResponse>` als SignalR-Client-Events **für jeden Request** (Target Current) |
| `LoggingBehaviour` | `LoggingBehaviour.cs` | `IRequestPreProcessor`: LogInformation mit User-Name-Lookup (`IAccountService.GetUserNameAsync`) pro Request (DB-Roundtrip pro Request) |
| `PerformanceBehaviour` | `PerformanceBehaviour.cs` | Stopwatch, Warnung > 500 ms |
| `UnhandledExceptionBehaviour` | `UnhandledExceptionBehaviour.cs` | LogError + rethrow |
| `ValidationBehaviour` | `ValidationBehaviour.cs` | Alle `IValidator<TRequest>` ausführen, `ValidationException` bei Fehlern |

Kein Caching-Behaviour (Caching passiert in `GetAllQueryHandlerBase` via LazyCache `IAppCache`), kein Transaction-Behaviour (Commit im Handler via `IUnitOfWork.Commit`).

### 3.3 Feature-Basisklassen (`src/Application/Features/Base/`) – das eigentliche Herzstück

| Basis | Datei | Request-Typ | Handler-Basis | Verhalten |
|---|---|---|---|---|
| `AddEditCommandBase<TDto>` | `Commands/AddEditCommandBase.cs` | `IRequest<AddUpdateResult<TDto>>`, `Items[]`, `CreateNewIfToUpdateNotExists` | `AddEditCommandHandlerBase<TCommand,TEntityId,TDto,TEntity>` (internal) | Split in Create/Update (per `dto.IsNew`), `PermissionService.EnsurePolicyAsync(Create/EditPermission)`, Mapping über **Nextended `MapTo<T>()`** + `CopyChangedValuesTo` (Reflection), `UnitOfWork.CommitAndRemoveCache`, publiziert `EntitiesCreated/Changed/Updated<TDto>` (typisiert + untypisiert mit Ids) per SignalR |
| `DeleteCommandBase<TId>` | `Commands/DeleteCommandBase.cs` | `IRequest`, `Ids[]` | `DeleteCommandHandlerBase<...>` | `GetByIdsAsync`, `DeleteManyAsync`, Commit + Cache-Remove, `EntitiesDeleted`-Event |
| `GetAllPagedQueryBase<TDto>` | `Queries/GetAllPagedQueryBase.cs` | `IRequest<PaginatedResult<TDto>>`, PageNumber/PageSize/SearchString/OrderBy[]/`TransferableExpression<TDto> OdataFilterQuery` | `GetAllPagedQueryHandlerBase<...>` | `ISpecification` + OData-Filter (StringToExpression) + `System.Linq.Dynamic.Core` OrderBy + `e => e.MapTo<TDto>()` als Select-Expression (nicht SQL-übersetzbar → Client-Evaluation) |
| `GetAllQueryBase<TDto>` | `Queries/GetAllQueryBase.cs` | `IRequest<IReadOnlyCollection<TDto>>`, `Force` | `GetAllQueryHandlerBase<...>` | LazyCache `GetOrAddAsync(cacheKey)` – Key `ApplicationConstants.Cache.CacheKeyFor(typeof(TEntity), filter)` |
| `GetByIdQueryBase<TId,TDto>` | `Queries/GetByIdQueryBase.cs` | `IRequest<TDto>` | `GetByIdQueryHandlerBase<...>` | optionale `Includes` |
| `ExportQueryBase` / `ExportQueryBase<TId>` / `ExportQueryHashed` | `Export/ExportQueryBase.cs` | `IExportQuery<TId> : IRequest<byte[]>`, `ExportServiceType` (Excel/Csv/Json) | `ExportQueryHandlerBase<...>` | wählt `IExportService` nach Typ, Specification-Filter |
| `ImportQueryBase<TDto>` | `Import/ImportQueryBase.cs` | `IRequest<AddUpdateResult<TDto>>`, ContentType + Data | `ImportQueryHandlerBase<...>` | wählt `IImportService` nach ContentType, delegiert an `AddEditCommand` via `Mediator.Send` |

Die Handler-Basisklassen sind `internal` – konkrete Handler in derselben Assembly erben sie mit 5–15 Zeilen (Beispiel `Features/Documents/Queries/GetById/GetDocumentByIdQuery.cs`). **MediatR ist hier nur der Dispatcher**; die Logik steckt in den Basisklassen und ist leicht auf ein anderes Dispatch-Modell übertragbar (siehe 11.4).

### 3.4 Konkrete Features (`src/Application/Features/`)

| Feature-Ordner | Requests | Generisch? |
|---|---|---|
| `Documents` | `AddEditDocumentsCommand` (Upload via `IUploadService`, Default-DocumentType "Unassigned"), `DeleteDocumentsCommand` (`DeleteFiles`, Cache-Invalidierung EAV), `GetAllDocumentsQuery` (Spec: Public oder eigene, Admin alles; MimeType-Ermittlung), `GetDocumentByIdQuery` | ja |
| `DocumentTypes` | AddEdit, Delete, Export, GetAll, GetById | ja |
| `ExtendedAttributes` | generische `AddEditExtendedAttributeCommand<TId,TEntityId,TEntity,TExtendedAttribute>`, Delete, Export, GetAll, GetAllByEntityId, GetById (+Response-Typen) – eigene UoW `IExtendedAttributeUnitOfWork` | ja (EAV) |
| `Notifications` | AddEdit, Delete, `MarkAllNotificationsCommand`, GetAllPaged, GetById, `GetUnreadCount` (nested `Request`/`Handler`-Stil) | ja |
| `Translations` | `AddEditLanguagesCommand`, `AddEditTranslationsCommand` (Cache-Keys pro Culture), Delete, Export, Import, GetAll(Languages/Translations), GetAllPaged, GetById | ja |
| `Dashboards` | `GetDashboardDataQuery` → `Result<DashboardDataResponse>` mit 12-Monats-Zählungen (13 Queries pro Entity) | Beispiel-Domain (zählt SocialData/Mandates) |
| `System` | `GetVersion`, `SendToServiceBus` (`IServiceBus` – **keine Implementierung im Repo gefunden**) | ja |
| `Mandates` | AddEdit, Delete, Export, GetAll, GetById | Beispiel-Domain |
| `SocialData` | AddEdit, Delete, Export (Excel via `WayesPDFParser.Exporter.ExcelExporter`), Import (PDF → Hangfire `BackgroundJob.Enqueue(ImportPdf)` via Stirling-PDF, Excel → `SocialDataExcelImporter`), GetAllPaged, GetById | Beispiel-Domain |

`Requests/` (kein MediatR): Identity-Requests (`TokenRequest`, `RefreshTokenRequest`, `RegisterRequest`, `ForgotPasswordRequest`, `ResetPasswordRequest`, `ChangePasswordRequest`, `UpdateProfilePictureRequest`, `UpdateUserRolesRequest`, `ToggleUserStatusRequest`, `RoleClaimRequest`, `PermissionRequest`), `MailRequest`, `NotificationRequest`, `UploadRequest` (`IUploadableFile`, `FromUrlAsync`).

### 3.5 Contracts (Interfaces für Infrastructure) – `src/Application/Contracts/`

| Interface | Datei | Implementierung (Infrastructure/Server) |
|---|---|---|
| `IRepositoryAsync<T,TId>` | `Repositories/IRepositoryAsync.cs` | `RepositoryAsync<T,TId>` – generisches EF-Repository (`Entities` IQueryable, Add/Update/Delete/GetById(s)/Paged) |
| `IUnitOfWork<TId>` | `Repositories/IUnitOfWork.cs` | `UnitOfWork<TId>` – `Repository<T>()`-Factory (Hashtable-Cache), `Commit`, `CommitAndRemoveCache(cacheKeys)`, `Rollback` |
| `IExtendedAttributeUnitOfWork<TId,TEntityId,TEntity>` | `Repositories/IExtendedAttributeUnitOfWork.cs` | `ExtendedAttributeUnitOfWork` |
| `IDocumentRepository`, `ISocialDataRepository`, `IBrandRepository` (leer), `IDocumentTypeRepository` (leer) | `Repositories/*.cs` | Spezial-Repos (`IsDocumentTypeUsed`, `IsMandateUsed`) |
| `ICurrentUserService` | `Services/ICurrentUserService.cs` | Server `CurrentUserService` (UserId, RoleIds aus Header `x-role-id`, `AsSystemUser()`, `AsUser(id)` als Scope) |
| `IPermissionService` | `Services/IPermissionService.cs` | `PermissionService` (Has/Ensure Policy/Role, Any/All-Match, `IsAdministratorAsync`) |
| `IAccountService`, `IUserService`, `IRoleService`, `IRoleClaimService`, `ITokenService`, `IDictionaryService`, `IUserCultureScopeService` | `Services/Account/`, `Services/Identity/` | Identity-Services (siehe 4.4) |
| `IMailService`, `IEmailTemplateService` | `Services/` | `SMTPMailService` / `SendGridMailService`, `EmailTemplateService` (RazorEngineCore) |
| `INotificationService` | `Services/INotificationService.cs` | `ServerNotificationService` |
| `IExportService`, `IImportService` | `Services/ExportImport/IExportService.cs` | `CsvImportExport`, `ExcelImportExport`, `JsonImportExport` |
| `IUploadService`, `IFileAccess` | `Services/` | `UploadService`, `ServerFileAccess` (Dateisystem `wwwroot/Files`) |
| `IServerStorageService`, `ISyncServerStorageService`, `IServerStorageProvider` | `Services/Storage/` | `ServerStorageService` + **Placeholder** `ServerStorageProvider` (In-Memory, `NotImplementedException`) |
| `IAuditService`, `IDBBackupService`, `IDatabaseSeeder`, `IDateTimeService`, `ITranslationService`, `IZipService`, `IChatService` | `Services/` | siehe 4.4 |
| `IServiceBus` | `IServiceBus.cs` | **keine Implementierung gefunden** (unklar, ob je vorhanden) |
| `ISessionProvider` | `ISessionProvider.cs` | `SimpleSessionProvider` (Application) / `SessionProvider` (Server, HttpContext.Session) |
| `IClientEventHub` | `Hubs/IClientEventHub.cs` | typisierter SignalR-Hub-Vertrag (ConnectUser, DisconnectUser, UserRolesChanged, LogoutUserById, ReceiveMessage, UpdateDashboard, RegenerateTokens) |
| `IJsonSerializer`, `IJsonSerializerOptions`, `IJsonSerializerSettings` | `Serialization/` | `SystemTextJsonSerializer`, `NewtonSoftJsonSerializer` |
| `IChatUser`, `IChatHistory<TUser>` | `Chat/` | `ApplicationUser`, `ChatHistory<TUser>` (Modell in Application `Common/Models/Chat/`) |

### 3.6 Hubs / SignalR-Abstraktion (`src/Application/Hubs/`)

- `HubBase<T> : Hub<T>` – bei Connect/Disconnect werden SignalR-Gruppen beigetreten: SessionId, UserId, **jede Permission** (Claim-Value), jeder Rollen-Name, jede Rollen-Id (via `IUserService`/`IRoleClaimService`, synchron `.Result` in LINQ). Damit kann `EventTarget.WithPermission("...")`/`WithRole`/`User(...)` gezielt adressiert werden. Das ist ein gutes, wiederverwendbares Konzept.
- `ClientEventHub : HubBase<IClientEventHub>` – Hub-Methoden `OnConnectAsync(userId)`, `OnDisconnectAsync`, `UserRolesChanged`, `LogoutUserById`, `UpdateDashboardAsync`, `RegenerateTokensAsync` (alle broadcasten an `Clients.All`).
- `ClientEventNotification : INotification` + `ClientEventDispatcher : INotificationHandler` – sendet jedes Event doppelt: als generisches `"EventRecieved"` (sic) mit Payload und unter `EventName` (= `GetType().FullName`).
- `Events/`: `ClientEventBase` (Target default `Current`), `EventTarget` (All, Current, WithRole, WithPermission, User), `BeforeRequest<T>`, `AfterRequest<T,R>`, `EntitiesCreated/Changed/Updated/Deleted` (jeweils generisch `<TDto>` und untypisiert mit `IdsAsString`), `UserProfileChanged`, `UserOnlineStatusChanged`.
- `MediatorExtensions.PublishClientEvent(s)` – die einzige Kopplung von SignalR an MediatR (`mediator.Publish(new ClientEventNotification(e))`). Durch ein eigenes `IClientEventPublisher` ersetzbar.

### 3.7 Specifications (`src/Application/Specifications/`)

`ISpecification<T> { Criteria; Includes; IncludeStrings }`, `SpecificationBase<T>` – minimalistisch (keine Ardalis.Specification), angewandt über `QueryableExtensions.Specify()`. Konkrete: `DocumentFilterSpecification` (Search + Public/Owner/Admin), `DocumentTypeFilterSpecification`, `ExtendedAttributeFilterSpecification`, `NotificationFilterSpecification`, `TranslationFilterSpecification`, `MandateFilterSpecification`, `SocialDataFilterSpecification`. In Infrastructure zusätzlich `AuditFilterSpecification`, `UserFilterSpecification`.

### 3.8 Validators (`src/Application/Validators/`)

FluentValidation `AbstractValidator<T>` mit `IStringLocalizer`-Messages; für jedes AddEdit-Command ein Command-Validator, der `RuleForEach(x => x.Items).SetValidator(new XyzDtoValidator(...))` setzt. Identity-Request-Validators (`RegisterRequestValidator`, `TokenRequestValidator`, `ChangePassword`, `ForgotPassword`, `ResetPassword`, `RoleRequest`, `UpdateProfile`, `AddressValidator`), `JsonValidator`, `ValidatorExtensions.WithNotEmptyMessage`. Ordnernamen noch aus dem alten Beispiel (`Validators/Features/Brands/…/AddEditMandateCommandValidator.cs`, `Validators/Features/Products/…/AddEditSocialDataCommandValidator.cs`).

### 3.9 AssistantFeatures / AI

- `src/Application/AssistantFeatures/AddProduct.cs`: `IAIInvokableService` (GptInvoke) mit Parameter-Metadaten "Add Product" – `ExecuteAsync` gibt **`return false`** zurück, Body auskommentiert (toter Code, verweist auf Brand/Product).
- `src/Application/Common/OllamaAIHandler.cs`: `IAIHandler` (GptInvoke) über `OllamaSharp.OllamaApiClient` (Chat + Streaming).
- Server: `AddAIAssistant()` wählt OpenAI (`CognitiveServices.OpenAi.ApiKey`) oder Ollama (Aspire-Endpoint `Ollama`), sonst `PublicSettings.AssistantAvailable=false`. `AssistantController` streamt Antworten (`Ask`, `AskWithHistory` [Authorize], `AskOllama` **anonym**).
- Client: `Assistant.razor` + `AssistantDialog` (siehe 6.10). Der SDK-Client hat handgeschriebenes `AskAssistantAsync` mit Chunk-Streaming.

### 3.10 Serialization, Configurations, Sonstiges

- `Serialization/`: `SystemTextJsonSerializer`, `NewtonSoftJsonSerializer`, `SystemTextJsonOptions`, `NewtonsoftJsonSettings`, `TimespanJsonConverter`.
- `Configurations/ServerConfiguration.cs` (handgeschriebener partial: `static Instance`, `PublicSettings.KeycloakEnabled/Endpoints`, `LoginMode` enum) + `Configurations/Generated/appsettings.g.cs` (145 Zeilen, **generiert von Nextended.CodeGen** aus `src/Server/appsettings.json` per `src/Server/CodeGen.config.json`: Klassen `ServerConfiguration`, `ConnectionStrings`, `PublicSettings`, `UserRegistration`, `LoginSettings`, `AppConfiguration`, `IdHashing`, `CognitiveServices`, `OpenAi`, `Translation`, `ApiDocumentation`, `Contact`, `License`, `MailConfiguration`, `BackupOptions`). `ClassMappingConfiguration` registriert globale Nextended-Converter (HashedInt ↔ int/string, `UploadRequest` ↔ `DocumentDto`).
- `Common/Models/`: `DtoBase<TId>`, `HashableDtoBase` (Id als Hashids-String, `IDtoBase<int>` explizit), `IDtoBase`, `HashedInt` (Hashids-Wrapper mit impliziten Konvertierungen), `AddUpdateResult<TDto>` (Added/Updated/Skipped), DTOs (`DocumentDto` mit `UploadRequest`, `DocumentTypeDto`, `NotificationDto`, `LanguageDto`, `TranslationDto`, `AuditDto`, `DatabaseBackupDto`, `VersionInfoModel`, `AssistantCommandDto`, Identity-Responses `UserResponse`, `RoleDto`, `RoleClaimResponse`, `PermissionResponse`, `TokenResponse`, `UserRolesResponse`, `ChatUserResponse`, `ChatHistoryResponse`, `UserInformationsDto`, `AddressDto`, `ExternalLoginOptions`; Beispiel-Domain: `MandateDto`, `SocialDataDto`, `PersonDataDto`, `EmployeeDataDto`, `InsuranceDataDto`, `PayrollDataDto`, `BankingDataDto`).
- `Common/Security/`: `CustomAuthorizeAttribute` (Roles/Policies + `RoleMatch`/`PolicyMatch` Any/All) für Request-Klassen, `CustomAuthorizeAttributeHandler`.
- `Common/Exceptions/`: `ApiException(message, inner, HttpStatusCode, args)`, `ForbiddenAccessException`, `NotFoundException`, `ValidationException` (Errors-Dictionary). `Errors.cs`: statische Factory (`Errors.Create`, `NotFound`, `Forbidden`, `ValidationFailed`, `IdentityFail`, `EnsureSuccess(IdentityResult)`).
- `Common/Scopes/CultureScope`, `UserCultureScope` (setzt `CultureInfo` + AKSoftware `ILanguageContainerService.SetLanguage`).
- `Common/Extensions/`: `ODataQueryOptionsExtensions.ParseExpression<T>(string)` (StringToExpression `ODataFilterLanguage`), `QueryableExtensions.ToPaginatedListAsync/Specify`, `ServiceScopeExtensions.AsSystemUserAsync/WithPermissions/WithRoles`, `ClaimsPrincipalExtensions`, `UriExtensions`.
- `Common/Services/ZipService.cs` (`IZipService`).
- `AssemblyInfo.cs`: `InternalsVisibleTo` für UnitTests/IntegrationTests.

---

## 4. Infrastructure (`src/Infrastructure`)

### 4.1 DbContexts (`src/Infrastructure/Contexts/`)

- `AuditableContext : IdentityDbContext<ApplicationUser, ApplicationRole, string, IdentityUserClaim<string>, IdentityUserRole<string>, IdentityUserLogin<string>, ApplicationRoleClaim, IdentityUserToken<string>>` – `DbSet<Audit> AuditTrails`; `SaveChangesAsync(userId, ct)` erzeugt pro geändertem Entry ein `AuditEntry` (Create/Update/Delete, Old/NewValues als JSON, ChangedColumns, PrimaryKey) und schreibt sie in `AuditTrails` (Temporary-Keys nach dem Save).
- `ApplicationDbContext : AuditableContext` – **ein einziger DbContext** mit DbSets: `ChatHistories` (`ChatHistory<ApplicationUser>`), `UserInformations`, `Addresses`, `Languages`, `Translations`, `Mandates`, `SocialData`, `Notifications`, `Documents`, `DocumentTypes`, `DocumentExtendedAttributes`. `SaveChangesAsync` setzt `CreatedOn/By` bzw. `LastModifiedOn/By` auf `IAuditableEntity` (via `IDateTimeService.NowUtc`, `ICurrentUserService.UserId`) und ruft die Audit-Variante nur, wenn ein UserId vorhanden ist. `OnModelCreating`: alle `decimal` → `decimal(18,2)`, `ApplyConfigurationsFromAssembly`.
- Kein Multi-Tenancy, kein Soft-Delete-Filter, kein DbContext-Splitting (Identity + Domain + Audit + Chat in einem Context).

### 4.2 Identity (`src/Infrastructure/Models/Identity/`)

- ASP.NET Core Identity mit **string-Keys** und EF-Stores (`AddIdentity<ApplicationUser, ApplicationRole>().AddEntityFrameworkStores<ApplicationDbContext>()`, Passwort-Policy: nur `RequiredLength=6`; `RequireUniqueEmail`).
- `ApplicationUser : IdentityUser<string>, IChatUser, IAuditableEntity<string>` – FirstName, LastName, `ProfilePictureDataUrl` (`text`), IsDeleted/DeletedOn (ohne Filter), IsActive, **`RefreshToken` + `RefreshTokenExpiryTime` in der User-Tabelle**, ChatHistoryFrom/ToUsers, `UserInfo` (nav → `UserInformations`), `IsOnline` (computed), `IsSystemUser()`.
- `ApplicationRole : IdentityRole, IAuditableEntity<string>` – Description, `RoleClaims`, **`IsSelectableByUser`** (Rolle bei Registrierung wählbar).
- `ApplicationRoleClaim : IdentityRoleClaim<string>, IAuditableEntity<int>` – Description, **Group** (= Permission-Gruppe).
- Tabellen im Schema `Identity` (`Users`, `Roles`, `RoleClaims`, `UserClaims`, `UserLogins`, `UserRoles`, `UserTokens`) über `Configurations/*Configuration.cs`.
- Externes Login: Keycloak OIDC (`Aspire.Keycloak.Authentication`), User wird per Email/`preferred_username` gesucht bzw. automatisch mit Rolle `Basic` angelegt (`LoginExternalAsync`). `KeycloakSeeder.cs` (HTTP-Admin-API: Client/User/Role anlegen) wird vom AppHost als Aspire-Command "Seed Client" genutzt.

### 4.3 Repositories (`src/Infrastructure/Repositories/`)

| Klasse | Funktion |
|---|---|
| `RepositoryAsync<T,TId>` | Generic Repository über `ApplicationDbContext.Set<T>()`; `Entities` (tracked IQueryable), Add/AddMany, Update (`Set.Update`) / `UpdateAsync(id, values)` (`CurrentValues.SetValues`), Delete/DeleteMany/DeleteAll, GetById(s), GetAll, GetPagedResponse (AsNoTracking) |
| `UnitOfWork<TId>` | Repository-Factory (Hashtable nach Typname – Kollision bei gleichnamigen Typen in verschiedenen Namespaces möglich), `Commit`, `CommitAndRemoveCache` (LazyCache), `Rollback` (Reload aller Entries), `Dispose` → **disposed den DbContext** (obwohl der scoped von DI verwaltet wird) |
| `ExtendedAttributeUnitOfWork<TId,TEntityId,TEntity>` | Kopie von UnitOfWork mit `Activator.CreateInstance` für EAV-Repos |
| `DocumentRepository`, `DocumentTypeRepository`, `MandateRepository : IBrandRepository`, `SocialDataRepository` | Spezial-Abfragen (IsUsed) |

Registrierung: `src/Infrastructure/DependencyInjection.cs` → `AddAllWithRegisterAttribute` + `AddRepositories()` (transient), `AddServerStorage()`, `AddExtendedAttributesUnitOfWork()`.

### 4.4 Services (`src/Infrastructure/Services/`)

| Service | Interface | Kern |
|---|---|---|
| `Identity/IdentityService.cs` | `ITokenService` (+ als Implementierung selbst injiziert) | JWT HS256 mit `AppConfiguration.Secret`, **2 Tage** Gültigkeit, kein Issuer/Audience; Claims: NameIdentifier, Email, Name, Surname, MobilePhone, Adresse, User-Claims, `ClaimTypes.Role` je Rolle + **alle Role-Claims (Type "Permission") ins JWT**. Refresh-Token: 32 Random-Bytes Base64, 7 Tage, in `ApplicationUser.RefreshToken`; `GetRefreshTokenAsync` validiert abgelaufenes JWT (ohne Lifetime), rotiert Refresh-Token. `LoginExternalAsync` (Keycloak), `RegenerateTokenAsync(specificRoles)` (Rollen-Umschaltung per Header `x-role-id`), `SetUserOnlineStatusAsync`. |
| `Identity/PermissionService.cs` | `IPermissionService` | Rollen via `IAccountService.IsInRoleAsync` (UserManager), Policies via `IAuthorizationService.AuthorizeAsync` mit frisch gebautem Principal (DB, nicht JWT); wirft `UnauthorizedAccessException` (kein User) / `ForbiddenAccessException` |
| `Identity/RoleService.cs` | `IRoleService` | Rollen-CRUD, `GetAllPermissionsAsync(roleId)` (Reflection über `Permissions`-Klasse, `Group` = Nested-Class-Name), `UpdatePermissionsAsync` (löscht alle Claims und setzt neu; Guards für Administrator/Basic; ein Guard wirkt invertiert – siehe Agent-Hinweis) |
| `Identity/RoleClaimService.cs` | `IRoleClaimService` | CRUD auf `ApplicationRoleClaim` |
| `Identity/UserService.cs` | `IUserService` | Registrierung (E-Mail-Bestätigung, Admin-Aktivierung, Dokument-Upload), Update (Username/Email-Regeln aus `PublicSettings.UserRegistration`), Rollen, Aktivierung, Kultur, `GetOrAddUserAsync` (Seeding; Superuser bekommen alle Permissions als Role-Claims), `GetAllForTargetAsync(EventTarget)`, Excel-Export; **Mails via `BackgroundJob.Enqueue(() => _mailService.SendAsync(...))`** |
| `Identity/AccountService.cs` | `IAccountService` | ChangePassword, Profilbild (Upload + `UserProfileChanged`-Event + neues JWT), `AuthorizeAsync(userId, policy)`, `WithRoles`/`WithPermissions` (temporäre Overrides), Logout |
| `Identity/DictionaryService.cs` | `IDictionaryService` | In-Memory-Singleton-Dictionary (Keycloak-id_token-Ablage), nicht threadsafe |
| `Identity/UserCultureScopeService.cs` | `IUserCultureScopeService` | erzeugt `UserCultureScope` |
| `EmailTemplateService.cs` + `Models/RazorEngineEmailTemplate.cs` + `Models/EmailTemplateCompiled.cs` | `IEmailTemplateService` | **RazorEngineCore**: Template + Parts (`Layout`, `Footer`) werden **bei jedem Aufruf neu kompiliert** (kein Cache); Template-Basisklasse bietet `Localize()`, `Table<T>()`, `Url()`, `ServerUrl()`, `Include()`, `RenderBody()`, `Layout` |
| `SMTPMailService.cs` | `IMailService` (`[RegisterAsIfConfigValueIsEmpty(SendGridApiKey)]`) | MailKit, StartTls |
| `SendGridMailService.cs` | `IMailService` (`[RegisterAsIfConfigValueIsNotEmpty(SendGridApiKey)]`) | SendGrid; **aktiv**, da Key in appsettings.json gesetzt |
| `ServerNotificationService.cs` | `INotificationService` | Zielgruppe aus `EventTarget`, `NotificationAsMail` Never/Always/WhenTargetOffline (Hangfire), `PersistInDb` (Notification-Entity), `_mediator.PublishClientEvent` |
| `AuditService.cs` | `IAuditService` | `GetTrailsAsync(limit 1000, userIds)`, `ExportAsync` (Specification + `IExportService`) |
| `ExportImport/CsvImportExport.cs`, `ExcelImportExport.cs`, `JsonImportExport.cs` | `IExportService` + `IImportService` | CsvHelper / CsvHelper.Excel / Newtonsoft; `ExportServiceType` + `SupportedContentTypes` |
| `Storage/ServerStorageService.cs`, `Storage/Provider/ServerStorageProvider.cs` | `IServerStorageService`, `IStorageProvider` | Key-Value-Fassade; Provider ist **Platzhalter** (scoped Dictionary, meiste Methoden `NotImplementedException`, TODO im Code) → `ServerPreferenceManager` persistiert nichts |
| `UploadService.cs`, `ServerFileAccess.cs` | `IUploadService`, `IFileAccess` | Dateisystem unter `wwwroot/Files/{UploadType-Description}/`, `EnsureFileNotExists` (Nummern-Suffix), relative URL `/Files/...`; **kein Blob-Storage-Provider** trotz Aspire Azure Storage im AppHost |
| `ChatService.cs` | `IChatService` | Chat-History in DB, Kontaktliste |
| `LocalDbBackupService.cs`, `AwsDbBackupService.cs` | `IDBBackupService` (config-bedingt) | `BACKUP DATABASE ... TO DISK` (nur SQL Server, String-Interpolation), AWS RDS `rds_backup_database` |
| `SystemDateTimeService.cs` | `IDateTimeService` | `DateTime.UtcNow` |

Lokalisierung der Services über `.resx` in `src/Infrastructure/Resources/Services/**` (en/it/km/ru; `RoleClaimService` zusätzlich de/es/fr/id/sv) – parallel zum YAML-System in Shared, d. h. **zwei Lokalisierungssysteme** (resx + YAML + DB-Translations).

### 4.5 Migrations (`src/Infrastructure/Migrations/`)

- **Nur eine Migration**: `20221101130227_Initial.cs` (+ Designer, Snapshot). Sie erzeugt Tabellen `Brands`, `Products`, `Documents`, `DocumentTypes`, `DocumentExtendedAttributes`, `Languages`, `Translations`, `Notifications`, `AuditTrails`, `ChatHistory`, `UserInformations`, `Addresses` + Identity-Schema – sie entspricht dem **`master`**-Modell, nicht dem aktuellen (`Mandate`/`SocialData` fehlen; der Snapshot referenziert `Coworkee.Domain.Entities.Catalog.Brand/Product`). SQL-Server-spezifisch (`bit`, `nvarchar(max)`).
- Zur Laufzeit: `src/Server/Extensions/ServiceCollectionExtensions.cs` (`AddDatabase`) ruft `Database.EnsureCreated()` ("required for hangfire"), `DatabaseSeeder.Initialize()` ruft ebenfalls `EnsureCreated()`, `src/Server/Program.cs` (`TryMigrateDbAsync`) ruft `MigrateAsync()` nur bei pending Migrations/Model-Changes (Fehler werden nur geloggt). Effektiv wird das Schema per **EnsureCreated** aus dem Modell erzeugt; die Migration ist faktisch tot. Für Postgres existieren keine Migrations.
- Provider-Wahl: `DatabaseProviderDetector.DetectProvider(connectionString)` (`Host=` → Postgres, `Server=`/`Data Source=` → SqlServer) in `src/Shared/Helper/DatabaseProviderDetector.cs`. Postgres über `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` + Hangfire.PostgreSql.

### 4.6 Seeding

`src/Infrastructure/DatabaseSeeder.cs` (`[RegisterAs(IDatabaseSeeder)]`): `EnsureCreated`, Rollen aus `ApplicationConstants.Defaults.Roles` (mit Permissions als Claims, `IsSelectableByUser`), User aus `Defaults.Users` (System, Administrators [admin, fgilde – Klartext-Passwörter im Code], Basic johndoe). `Administrator`/`Basic`-Rollen entstehen implizit über `GetOrAddUserAsync`.

---

## 5. Server (`src/Server`)

### 5.1 Startup-Pipeline (`src/Server/Program.cs`, `src/Server/Startup.cs`)

`Program.Main`: setzt `ApplicationConstants.IsNswagGeneration` (wenn `--applicationName` in args), läuft in einer `do { StartServer } while (cancelled)`-Schleife → **Self-Restart** (`SystemController.RestartServer` stoppt nur den Host). `StartServer`: `CreateBuilder` → `AddServiceDefaults()` (Aspire) → `Configuration.AddConfigurations()` (Env + optionales `appsettings.Override.json`) → `UseStaticWebAssets` → `Startup.ConfigureServices` → `AddRequestTimeouts/AddOutputCache` → Build → `TryMigrateDbAsync` → `Startup.Configure` → `MapDefaultEndpoints()` → `RunAsync`.

`Startup.ConfigureServices` (Reihenfolge): `AddApplicationSettings` (bindet `ServerConfiguration.Instance` + transientes Re-Binding pro Resolve) → `HangfireAuthorizationFilter` → `AddCors` (AllowAll) → `AddSignalRServices` (Azure SignalR wenn ConnectionString `signalr`, sonst lokal) → `AddCurrentUserServiceAndSession` (Session, HttpContextAccessor, `ISessionProvider`, `ICurrentUserService`) → `AddAllWithRegisterAttribute(Server-Assembly)` → `AddLocalization` → `AddHealthChecks` → `AddSerialization` → `ServerPreferenceManager` → `AddServerLocalization` (`IStringLocalizer<>` → DB-`ServerLocalizer<>`) → `AddYamlLocalizationWithFallback` → `AddIdentity` (PermissionPolicyProvider, PermissionAuthorizationHandler, Identity mit EF-Stores) → `AddJwtAuthentication` (+Keycloak, +Policies) → `AddApplication` → `AddInfrastructure` → `AddApiVersions` → `AddOpenApiDocumentation` (NSwag Swagger+OpenApi je Version, JWT-ApiKey-Scheme) → `AddDatabase` (SqlServer/Npgsql per Detector, `EnsureCreated`) → `AddHangfire` (SqlServer/Postgres-Storage) → `AddHangfireServer` → `AddGrpc` → `AddControllersWithViews` (`ApiExceptionFilterAttribute`, `TransferableExpressionModelBinderProvider`, FluentValidation, XML-Formatter, **OData** `EnableQueryFeatures().SetMaxTop(1000)`) → `AddExtendedAttributesValidators` → `AddExtendedAttributesHandlers` → `AddRazorPages` → `AddLazyCache` → `AddAIAssistant` → `AddOptions<BackupOptions>`.

`Startup.Configure`: `UseSessionId` → `UseCors` → `UseExceptionHandling` (Dev: DeveloperExceptionPage + `UseWebAssemblyDebugging`) → `UseHttpsRedirection` → **`UseBlazorFrameworkFiles`** → `UseStaticFiles` → `UseRequestLocalizationByCulture` (+`RequestCultureMiddleware`) → `UseRouting` → `UseGrpcWeb` → `UseAuthenticationFromQuery` (nur wenn `!HostClientInServer`) → `UseAuthentication` → `UseAuthorization` → `UseHangfireDashboard("/jobs")` → `UseApplicationEndpoints` (`MapRazorPages`, `MapControllers`, **`MapFallbackToFile("index.html")`**, `MapHub<ClientEventHub>("/eventHub")`, `MapGrpcService<MainDataService>().EnableGrpcWeb()`) → `UseSwaggerAuthorized` → `UseSwagger` (SwaggerUi + OpenApi mit Custom-Header/JS/CSS) → `Initialize` (alle `IDatabaseSeeder`).

**Client-Hosting**: `Shared.props` `HostClientInServer=true` → Server referenziert `Client.csproj`, entfernt sein eigenes `wwwroot/index.html`, `UseBlazorFrameworkFiles` + `MapFallbackToFile` liefern die WASM-App. Bei `false`: Client als eigener nginx-Container (`src/Client/Dockerfile`), Server-`index.html` = Platzhalter "Coworkee Server is Running", Client bekommt `BackendOrigin` per `sed` im Publish-Target.

### 5.2 Auth (JWT, Keycloak, Cookies, Refresh)

- **JWT Bearer** (Default-Scheme): `SymmetricSecurityKey(AppConfiguration.Secret)`, kein Issuer/Audience-Check, `ClockSkew=0`, `SaveToken`. Events: `OnTokenValidated` → UserId in Session; `OnAuthenticationFailed` → bei `SecurityTokenExpiredException` 401-JSON + `IAccountService.LogoutAsync`, sonst 500-JSON; `OnChallenge` → 401-JSON; `OnForbidden` → 403-JSON.
- **Token-Ausgabe**: `TokenController` (`POST api/v1/identity/Token` Login, `POST .../refresh`, `POST .../RegenerateNew` mit Header `x-role-id`). Details in 4.4 (`IdentityService`).
- **Keycloak** (nur wenn `PublicSettings.Endpoints["keycloak"]` gesetzt): Cookie-Scheme + `AddKeycloakOpenIdConnect("keycloak")`, Authorization-Code + PKCE, `OnTokenValidated` → `ITokenService.LoginExternalAsync` → JWT in Session → `AccountController.login-callback` hängt das JWT als `auth_token`-Query-Parameter an die Client-URL. Logout ruft Keycloak-End-Session.
- **Cookies**: nur für den Keycloak-Flow; die API ist ansonsten stateless per Bearer. Session (`AddSession`) wird trotzdem genutzt (SessionId für SignalR-Gruppen, UserId-Ablage).
- Refresh-Token-Rotation im Client per Interceptor (siehe 6.4).

### 5.3 Permission-System (Modell)

| Ebene | Ort | Mechanismus |
|---|---|---|
| Definition | `src/Shared/Constants/Permission/Permissions.cs` | **String-Konstanten in Nested Static Classes** (`Permissions.Documents.View = "Permissions.Documents.View"` …). Gruppen: SocialData (Werte noch `Permissions.Products.*`!), Mandates (Werte `Permissions.Brands.*`!), Documents, DocumentTypes, Backups, Translations, DocumentExtendedAttributes, Users, Roles, RoleClaims, Communication.Chat, Preferences.ChangeLanguage, Dashboards, Hangfire, PgAdmin, Grafana, OllamaUI, Prometheus, Swagger, AuditTrails. `[RequiresPermissions(...)]`-Attribut auf Konstanten deklariert **Abhängigkeiten** (z. B. Create braucht View). `GetRegisteredPermissions()`/`GetRequiredDependencyPermissionsFor()` per Reflection. |
| Claim-Typ | `src/Shared/Constants/Permission/ApplicationClaimType.cs` | `ApplicationClaimTypes.Permission = "Permission"` |
| Persistenz | `Identity.RoleClaims` (`ApplicationRoleClaim` mit `Group`, `Description`) | Permissions sind **Role-Claims**; User → Rollen → Claims. Keine User-direkten Permissions. |
| Token | `IdentityService.GetClaimsAsync` | alle Role-Claims wandern **ins JWT** |
| Policies | `AddJwtAuthentication` → `AddAuthorization`: pro Konstante `RequireClaim("Permission", value)` | plus `src/Server/Permission/PermissionPolicyProvider.cs` (baut für Policy-Namen mit Prefix "Permission" dynamisch `PermissionRequirement`) und `PermissionAuthorizationHandler.cs` (Claim `Permission == value` **und `Issuer == "LOCAL AUTHORITY"`**). Der Provider schattet die statischen Policies. `GetFallbackPolicyAsync` = null → Actions ohne Attribut sind anonym. |
| Controller | `[Authorize(Policy = Permissions.X.Y)]` (JWT-Claims) **oder** `[Filters.CustomAuthorize(Policies=..., PolicyMatch=Any)]` (`src/Server/Filters/CustomAuthorizeAttribute.cs` → DB-Check via `IPermissionService`) | zwei parallel verwendete Mechanismen |
| Application | `[CustomAuthorize]` auf Request-Klassen + `AuthorizationBehaviour` (DB-Check) + `EnsurePolicyAsync` in Handler-Basisklassen | dritte Prüfebene |
| Client | `AddAuthorizationCore(RegisterPermissionClaims)` – dieselbe Reflection über `Permissions` erzeugt clientseitige Policies; `AuthorizeView Policy=...`, `NavigationEntry.WithPolicies` | JWT-Claims im Browser |
| UI | `Pages/Identity/RolePermissions.razor` (Tabs pro Gruppe, Toggle, Abhängigkeits-Dialog `PermissionsRequired`), `Roles.razor`, `UserRoles.razor` | vorhanden, aber statisch an die Konstanten-Klasse gebunden |

Konsequenz: Permissions sind **compile-time-statisch**; eine App (Shareme) kann keine eigenen Permissions ergänzen, ohne `Permissions.cs` (in Shared) zu ändern – ein Provider-/Contributor-Modell fehlt (siehe 11.2; Branch `app-framework-3` hat einen Prototyp `IPermissionProvider`).

### 5.4 Controller (alle unter `api/v{version}/…`, `ApiVersions.V1="1"`)

Basis: `src/Server/Controllers/BaseApiController.cs` – `BaseApiController<T> : ControllerBase` mit `[ApiController] [ApiVersion("1")] [Route("api/v{version:apiVersion}/[controller]")]`, lazy `IMediator Mediator`, `Get<TService>()`, `UnhashId(s)`, `Filter<T>(…, TransferableExpression<T>)`, `ClientEventHub`-Zugriff. Legende Auth: `P:` = `[Authorize(Policy=…)]`, `CA:` = `[CustomAuthorize]` (DB), `A` = `[Authorize]`, `Anon` = `[AllowAnonymous]`, **NONE** = kein Attribut (= anonym!).

| Controller | Datei | Route | Actions (Verb Route → Auth → Dispatch) |
|---|---|---|---|
| Assistant | `Controllers/AssistantController.cs` | `Assistant` | POST AskOllama → **NONE** → Service; POST Ask → A → `IAIActionInvoker` (Streaming); POST AskWithHistory → A → Service |
| Mandates | `Controllers/Catalog/MandatesController.cs` | `Mandates` | GET → P Mandates.View → MediatR; GET {id}; POST → Create; DELETE → Delete; GET Export → Export (`[AllowSynchronousIo]`) – alle MediatR |
| SocialData | `Controllers/Catalog/SocialDataController.cs` | `SocialData` | GET → CA View; GET {id}; POST → CA Create/Edit Any; DELETE → P Delete; GET Export → P Export; POST ImportFile → **NONE**; POST Import → **NONE** – alle MediatR |
| Chats | `Controllers/Communication/ChatsController.cs` | `Chats` | Klasse P Communication.Chat; DELETE {messageId}, GET {contactId}, GET users, POST (Save + Mail-Template + SignalR `ReceiveMessage` + `INotificationService`) – Service |
| Dashboard | `Controllers/DashboardController.cs` | `Dashboard` | GET → P Dashboards.View → MediatR |
| Database | `Controllers/DatabaseController.cs` | `Database` | alle `[Authorize(Roles=Administrator)]`: POST (Backup), PUT (Rename), POST Download (Zip), DELETE, GET (paged), POST Restore – Service/File-IO |
| Resource | `Controllers/ResourceController.cs` (+ `SvgHelper.cs`) | absolute Routen | GET `/favicon.ico` (SVG→PNG via Svg.Skia), GET `/Logo.svg`, GET `/Logo.png?height&width`, GET `/package/download/nuget` (SDK-nupkg aus `wwwroot/sdk`), GET `/{objectName}/resources.js` (JS-Konstanten aus `ApplicationConstants`+`CustomIcons`, MemoryCache) – alle **NONE** |
| System | `Controllers/SystemController.cs` | `System` | GET AuthorizeServerUrl → A; GET UnhashHashedId → A; GET Version → Anon → MediatR; GET AvailableApiVersions → Anon; GET Configuration → Anon (PublicSettings); POST {queue} → **NONE** → MediatR `SendToServiceBus`; GET SystemConfiguration → Roles Admin; POST WriteSystemConfiguration → Admin (schreibt `appsettings.Override.json`); POST RestoreSystemConfiguration → Admin; POST RestartServer → Admin |
| Account | `Controllers/Identity/AccountController.cs` | `identity/Account` | Klasse A: PUT ChangePassword, GET/POST profile-picture/{userId}, POST SetPreferredLanguage; Anon: GET login-callback, GET login (Keycloak-Challenge), POST Logout – Service |
| RoleClaim | `Controllers/Identity/RoleClaimController.cs` | `identity/RoleClaim` | GET ($filter) → P RoleClaims.View; GET {roleId}; POST → Create; DELETE {id} → Delete – Service |
| Role | `Controllers/Identity/RoleController.cs` | `identity/Role` | GET GetPublicRoles → Anon; GET → P Roles.View; POST → Create; DELETE {id}; GET permissions/{roleId} → P RoleClaims.View; PUT permissions/update → P RoleClaims.Edit – Service |
| Token | `Controllers/Identity/TokenController.cs` (erbt `BaseApiController<RoleController>` – Copy/Paste) | `identity/Token` | POST RegenerateNew → A; POST (Login) → NONE; POST refresh → NONE – Service |
| User | `Controllers/Identity/UserController.cs` | `identity/User` | Klasse A: POST UpdateProfile; DELETE → P Users.Delete; GET ($filter) → P Users.View; GET {id}; GET roles/{id} → View; GET roles/my; PUT roles/{id} → Edit; POST (Register) → Anon (erzwingt `UserRegistration`-Settings); GET confirm-email → Anon; POST toggle-status → A; POST forgot-password → Anon; POST reset-password → Anon; GET export → P Users.Export – Service |
| Notifications | `Controllers/Notifications/NotificationsController.cs` | `Notifications` | alle A: GET (paged), GET Unread, DELETE, DELETE DeleteAll, GET {id}, POST MarkAllRead, POST {id} – MediatR |
| Product (OData) | `Controllers/OData/ProductController.cs` | – | **komplett auskommentiert** (geplant `GenericODataController<Product>`) |
| Language | `Controllers/Translations/LanguageController.cs` | `Language` | POST → CA Translations.Create/Edit; GET ($filter) → Anon – MediatR |
| Translations | `Controllers/Translations/TranslationsController.cs` | `Translations` | GET GetAllPaged → CA View; GET GetAll → Anon; GET {id}; POST → CA Create/Edit; DELETE → P Delete; POST Export → P Export; POST ImportFile → **NONE**; POST Import → **NONE** – MediatR |
| Audits | `Controllers/Utilities/AuditsController.cs` | `Audits` | GET → P AuditTrails.View (Fremd-User nur Admin); GET Export → P Export – Service |
| Preferences | `Controllers/Utilities/PreferencesController.cs` | `Preferences` | POST changeLanguage → P Preferences.ChangeLanguage → `ServerPreferenceManager` (persistiert nichts) |
| DocumentTypes | `Controllers/Utilities/Misc/DocumentTypesController.cs` | `DocumentTypes` | GET, GET {id}, POST, DELETE, GET Export → P DocumentTypes.* – MediatR |
| Documents | `Controllers/Utilities/Misc/DocumentsController.cs` | `Documents` | GET (paged), GET {id} (Hash-Id), POST, DELETE ?deleteFile, GET GetMimeType?url → P Documents.* / A – MediatR |
| ExtendedAttributes (abstract) | `Controllers/Utilities/ExtendedAttributes/Base/ExtendedAttributesController.cs` | `{Entity}ExtendedAttributes` | generisch `<TId,TEntityId,TEntity,TExtendedAttribute>`: GET, GET by-entity/{entityId}, GET {id}, POST, DELETE {id}, GET export – MediatR (virtuelle Actions ohne Auth) |
| DocumentExtendedAttributes | `Controllers/Utilities/ExtendedAttributes/Misc/DocumentExtendedAttributesController.cs` | `DocumentExtendedAttributes` | Overrides mit P DocumentExtendedAttributes.* |

### 5.5 Middlewares, Filters, Managers, Services

| Typ | Datei | Funktion |
|---|---|---|
| Middleware | `Middlewares/AuthenticationFromQueryMiddleware.cs` | `?auth_token=` → `Authorization: Bearer`, Redirect ohne Parameter (nur Standalone-Client) |
| Middleware | `Middlewares/RequestCultureMiddleware.cs` | Culture aus `?culture=` oder `Accept-Language`, setzt `CultureInfo` + AKSoftware `SetLanguage` |
| Middleware | `Middlewares/SwaggerBasicAuthMiddleware.cs` (Klasse `SwaggerAuthorizedMiddleware`) | `/swagger` nur mit Login (`ApiDocumentation.RequireLogin`) bzw. `Permissions.Swagger.View` (kein Basic-Auth trotz Dateiname) |
| ModelBinder | `Middlewares/TransferableExpressionModelBinder.cs` | bindet `TransferableExpression<T>` aus `$filter`/Feldname; `FromOdataFilterAttribute` |
| Filter | `Filters/ApiExceptionFilterAttribute.cs` (+ `Extensions/ExceptionConvertExtensions.cs`) | Exception → ProblemDetails: `ApiException`→StatusCode, `ValidationException`→400 `ValidationProblemDetails`, `NotFoundException`→404, `UnauthorizedAccessException`→401, `ForbiddenAccessException`→403, ModelState→400, sonst 500 (nicht als handled markiert) |
| Filter | `Filters/CustomAuthorizeAttribute.cs`, `CustomAuthorizeAttributeFilter.cs` | `TypeFilterAttribute` → `IAsyncAuthorizationFilter` → `ICustomAuthorizeAttributeHandler` (DB-basiert) |
| Filter | `Filters/HangfireAuthorizationFilter.cs` | Dashboard nur mit `Permissions.Hangfire.View`, sonst Login-Redirect |
| Filter | `Filters/AllowSynchronousIoAttribute.cs` | `AllowSynchronousIO=true` für Datei-Exporte |
| Permission | `Permission/PermissionRequirement.cs`, `PermissionPolicyProvider.cs`, `PermissionAuthorizationHandler.cs` | siehe 5.3 |
| BackgroundService | `BackgroundServices/ProductFeedSyncService.cs` | `[RegisterAs(IHostedService, Enabled=false)]` – **deaktiviert**; täglicher CSV-Feed-Import (Hangfire), `CreateProduct` gibt `null` (Restcode aus Product-Beispiel) |
| gRPC | `gRPC/Main.proto`, `gRPC/MainDataService.cs` | Service `CoworkeeData` (`GetDashboardData` → MediatR, `GetProducts` → nur Count; Messages noch Product/Brand-lastig), `[Authorize]`, gRPC-Web aktiviert; Client nutzt es nur im Dashboard-"Test"-Button |
| Manager | `Managers/Preferences/IServerPreferenceManager.cs`, `ServerPreferenceManager.cs`, `Settings/ServerPreference.cs` | Server-Preference (nur `LanguageCode`) über `IServerStorageService` → wegen Placeholder-Provider **nicht persistent** |
| Service | `Services/CurrentUserService.cs` | `ICurrentUserService` aus HttpContext; `AsUser`/`AsSystemUser` als `ActionScope` (Claims via `IdentityService.GetClaimsAsync`) |
| Service | `Services/TranslationService.cs` | `ITranslationService` via NuGet `TranslatorService` (Azure Translator, Key `CognitiveServices.Translation`), Batches à 25 |
| Service | `SessionProvider.cs` | `ISessionProvider` aus `HttpContext.Session` |
| Localization | `Localization/ServerLocalizer.cs`, `ServerCommonResources.cs` | `IStringLocalizer<T>` aus **DB-Tabelle `Translations`** (eine Query pro String, kein Cache) mit Key-Fallback; resx en/ru unter `Resources/` |
| Extensions | `Extensions/ServiceCollectionExtensions.cs` (AddAIAssistant, AddSignalRServices, AddCurrentUserServiceAndSession, AddServerLocalization, AddApplicationSettings, AddApiVersions, AddOpenApiDocumentation, AddSerialization, AddDatabase, AddIdentity, AddJwtAuthentication), `ApplicationBuilderExtensions.cs`, `MvcBuilderExtensions.cs` (AddValidators, AddExtendedAttributesValidators), `ConfigBuilderExtensions.cs`, `HttpContextExtensions.cs`, `HttpRequestExtensions.cs`, `FormFileExtensions.cs`, `ODataQueryOptionsExtensions.cs` | Server-Verdrahtung |
| Codegen | `ApiVersions.cs`, `CustomSchemaNameGenerator.cs` (nicht verdrahtet), `nswag.json`, `CodeGen.config.json` | siehe 8 |
| Files | `wwwroot/Files/` (Uploads/Backups: enthält eingecheckt `DBBackups/DB_Backup_2025-07-12_20-51-36.bak` und `Documents/Pitch deck English 112024.pdf`), `wwwroot/api/specification.json` (NSwag-Output), `wwwroot/swagger-ui/{header.html,scripts.js,styles.css}` (Custom Swagger-UI: Dark-Mode, Auto-Login aus `localStorage.authToken`, Logo `/Logo.svg`, nupkg-Download-Link), `wwwroot/clientEventTest.html` + `js/clientEvent.js` (veraltete Demo, referenziert `api/v2/myentity`), `wwwroot/js/signalr/*` (libman), `Output.zip`, `Server.csproj.Backup*.tmp` (Müll) | |
| Config | `appsettings.json` | Sections: `ClientUrl`, `ConnectionStrings{DefaultConnection (LocalDB), Ollama}`, `PublicSettings{AssistantAvailable, ContactAddress, HostClientInServer, UserRegistration{…}, LoginSettings{LoginMode, AllowLoginWithUsername, AllowedEmails}, Endpoints{Dashboard}}`, `AllowedHosts`, `AppConfiguration{IdHashing{Enabled,MinLength,AllowAccessWithNotHashedId,Salt}, Secret}`, `CognitiveServices{OpenAi{ApiKey,Model}, Translation{Key,…,Region}}`, `ApiDocumentation{RequireLogin,RequirePermission,Title,Description,Contact,License}`, `MailConfiguration{SendGridApiKey,From,Host,Port,UserName,Password,DisplayName}`, `BackupOptions{BucketName}`. **Enthält echte Secrets** (SendGrid-Key, Azure-Translator-Key, SMTP-Passwort, JWT-Secret, Hash-Salt). |
| Docker | `Dockerfile` | Multi-Stage aspnet/sdk 9.0 + nodejs; kopiert **nicht** `src/Aspire/Coworkee.ServiceDefaults` obwohl referenziert (Build-Risiko, nicht verifiziert) |
| Tools | `.config/dotnet-tools.json` (dotnet-ef 5.0.11 – veraltet), `libman.json` (SignalR-JS) | |

---

## 6. Client – Blazor WASM (`src/Client`)

Grundlagen: `_Imports.razor` setzt global `@attribute [Authorize]` (jede Seite/Komponente ist auth-pflichtig, außer sie überschreibt), plus globale `@inject`s (`_api` = `IApplicationClient` aus dem SDK, `_gRpc`, `_interceptor`, `_clientPreferenceManager`, `_localStorage`, `_errorService`, `_config`, `_healthCheckService`, `_themeManager` …). `Program.cs` setzt Default-Culture aus der Preference und ruft `WebAssemblyHostBuilderExtensions` (gesamte DI).

### 6.1 Pages (`src/Client/Pages/`)

| Datei | Route | Auth | Zweck | Haupt-Komponenten |
|---|---|---|---|---|
| `Administration/Database.razor(.cs)` | `/admin/database-backups` | Roles=Administrator | DB-Backups listen/anlegen/umbenennen/downloaden/restoren; Menüeintrag auskommentiert | EditableDataTable, MudEx-Confirmation |
| `Administration/Settings.razor(.cs)` | `/admin/site-settings` | Roles=Administrator | `ServerConfiguration` + `ClientApplicationConfiguration` editieren, Server-Restart, Defaults | MudTabs, **MudExObjectEditForm** ×2 |
| `Authentication/Login.razor(.cs)` | `Login`, `Login/{Message}` | AllowAnonymous, `LoginLayout` | E-Mail/Passwort-Login, Keycloak-Redirect (`LoginMode.External`), Dev-Buttons für Admin/Basic | EditForm, FluentValidationValidator, ApplicationLogo |
| `Authentication/Register.razor(.cs)` | `Register` | AllowAnonymous, `LoginLayout` | Mehrstufige Registrierung (Carousel: Adresse, Rollen, Dokumente) | MudCarousel, AddressEdit, **MudExUploadEdit** |
| `Catalog/Mandates.razor(.cs)` | `/catalog/mandates/{Action?}/{Id?}` | P Mandates.View | CRUD MandateDto (Beispiel-Domain) | EditableDataTable, `EditOrCreate` (MudExObjectEditDialog) |
| `Catalog/SocialData.razor(.cs)` | `/catalog/data/{MandateId}/{Action?}/{Id?}` | P SocialData.View | CRUD SocialData je Mandate, Import PDF/Excel (Beispiel-Domain) | EditableDataTable, `ShowComponentInDialogAsync<MudExUploadEdit<UploadableFile>>` |
| `Communication/Chat.razor(.cs)` | `/chat`, `/chat/{CId}` | P Communication.Chat | 1:1-Chat, Online-Status, Löschen mit Countdown (Menüeintrag auskommentiert) | UserAvatar, UserCard, MudExIconButton |
| `Content/Dashboard.razor(.cs)` | `/dashboard` | P Dashboards.View | Zähler-Karten + Balkendiagramm, SignalR `UpdateDashboard`, gRPC-Test-Button | MudChart |
| `Content/Home.razor(.cs)` | `/`, `/home` | impl. Authorize | Logo + App-Name | ApplicationLogo |
| `ForbiddenPage.razor` | `Forbidden/{ReturnUrl?}` | impl. | 403-Seite | Forbidden |
| `Identity/Account.razor` | `/account`, `/account/{Action?}/{Id?}` | impl. | Tabs Profile/Security/Notifications/Documents | MudTabs, Profile, Security, NotificationsList, DocumentList |
| `Identity/Forgot.razor(.cs)`, `Reset.razor(.cs)` | `/account/forgot-password`, `/account/reset-password/` | AllowAnonymous | Passwort vergessen / zurücksetzen | EditForm |
| `Identity/Profile.razor(.cs)`, `Security.razor(.cs)` | (Komponenten) | impl. | Profil + Bild-Upload (dropZone.js), Passwort ändern | MudCard, UserAvatar, AddressEdit |
| `Identity/UserProfile.razor(.cs)` | `/user-profile/{Id}` | P Users.View | Admin-Sicht auf User, Force-Logout | MudTabs, MudExMessageDialog |
| `Identity/Users.razor(.cs)`, `RegisterUserModal.razor(.cs)` | `/identity/users` | P Users.View | Userliste, Export, Anlegen, Löschen | MudTable, Dialog |
| `Identity/Roles.razor(.cs)`, `RoleModal.razor(.cs)` | `/identity/roles` | P Roles.View | Rollenliste, CRUD | MudTable, Dialog |
| `Identity/RolePermissions.razor(.cs)` | `/identity/role-permissions/{Id}` | P RoleClaims.View | **Permissions je Rolle** (Tabs pro Gruppe, Toggle, Abhängigkeits-Dialog `PermissionsRequired`) | MudTabs, MudTable |
| `Identity/UserRoles.razor(.cs)` | `/identity/user-roles/{Id}` | P Roles.View | Rollen einem User zuweisen | MudTable, MudCheckBox |
| `Localization/Languages.razor` | `/localization/languages` | P Translations.Edit | Sprachen aktivieren (AutoCreateTranslations) | MudExpansionPanels, MudSwitch |
| `Localization/Translations.razor(.cs)` | `/localization/translations/{Action?}/{Id?}` | P Translations.View | Übersetzungen Inline-Bulk-Edit, Import/Export | EditableDataTable (`EditMode.InlineBulk`) |
| `Misc/DocumentStore.razor(.cs)` | `/document-store` | P Documents.View | Dokumentenliste | DocumentList |
| `Misc/DocumentTypes.razor(.cs)`, `AddEditDocumentTypeModal.razor(.cs)` | `/document-types/{Action?}/{Id?}` | P DocumentTypes.View | CRUD Dokumenttypen | EditableDataTable |
| `Misc/AddEditDocumentModal.razor(.cs)` | (Dialog) | impl. | Dokument anlegen/bearbeiten mit Upload und **Vorschau via `MudExFileDisplay`** | MudDialog, InputFile, **MudExFileDisplay**, MudAutocomplete |
| `Misc/ExtendedAttribute/DocumentExtendedAttributes.razor` | `/extended-attributes/{EntityName}/{EntityIdString}` | P DocumentExtendedAttributes.View | konkrete EAV-Seite für Document | ExtendedAttributesBase |
| `Redirect.razor` | `Redirect` | impl. | Redirect mit optionaler Bestätigung (externe URLs) | – |
| `Utilities/AuditTrails.razor(.cs)` | `/audit-trails` | P AuditTrails.View | Audit-Liste, User-Filter, Datumsbereich, Suche in Old/New, Excel-Export | MudTable, MudDateRangePicker, UserFilter |

### 6.2 Shared Components, Dialogs, Layouts (`src/Client/Shared/`)

| Datei | Zweck |
|---|---|
| `App.razor(.cs)` | Root: `Error`, `CascadingValue<HubConnection>`, `CascadingAuthenticationState`, Router mit `AuthorizeRouteView` (NotAuthorized → Forbidden/Login, NotFound → NotFoundComponent); registriert HTTP-Interceptor, startet Hub, lädt Translations |
| `MainLayout.razor(.cs)` | MudThemeProvider/RTL/Dialog/Snackbar/Popover, AppBar, Drawer; hält `CurrentTheme`, `IsDarkMode`, `RightToLeft`; registriert Hub-Handler; statisches `Instance` |
| `LoginLayout.razor` | Layout für Login/Register (LanguageSelector, ThemeSelector ToggleOnly) |
| `NotFoundLayout.razor`, `NotFoundComponent.razor` | 404 mit Inline-SVG (isometrische Würfel) |
| `MainAppBar.razor(.cs)` | AppBar: AppBarHeader, Sign-In/Register, Assistant-Button, RoleSelector, LanguageSelector, RTL, ThemeSelector, NotificationToggle, User-Menü |
| `MainNavMenuDrawer.razor(.cs)` | `MudDrawer`-Ableitung mit Pin/Single-Expand, Variant/ClipMode aus Theme |
| `NavMenu.razor(.cs)` | Menübaum aus `Navigations.Default(_config)` mit `MudExTreeView`; Filter nach Policies/Roles/Auth/Condition |
| `Error.razor`, `Forbidden.razor` | Error-Wrapper (keine echte `ErrorBoundary`), 403-View mit RoleSelector |
| `Components/EditableDataTable.razor(+.cs/.classes.cs/.mode.cs/.css)` | **generische CRUD-Tabelle** (MudTable): Paging/Suche, Export/Import, Multi-Select, Inline-Edit, URL-Sync `Action`/`Id`, Permissions pro Aktion, `UpdateOnChange`-Wrapper, `CustomActions` – zentrales wiederverwendbares Stück |
| `Components/EditableDataGrid.razor(.cs/.css)` | MudDataGrid-Variante, **ungenutzt** |
| `Components/UpdateOnChange.razor(.cs)` | Live-Update-Wrapper: bei `EntitiesUpdated<TDto>` anderer User → Alert "Update now"/Auto-Update (LocalStorage `ConfirmUpdate`) |
| `Components/ExtendedAttributesBase.razor(.cs)`, `ExtendedAttributes.razor(.cs)`, `AddEditExtendedAttributeModal.razor(.cs)` | generische EAV-UI (Tabs pro Gruppe) |
| `Components/DocumentList.razor(.cs)` | paginierte Dokumenttabelle mit Permission-Aktionen, Download-Link |
| `Components/ApplicationLogo.razor` | MudIcon mit `CustomIcons.ApplicationMainIcon` (siehe 12) |
| `Components/AppBarHeader.razor(.cs)` | Menü-Toggle/Logo/Titel nach `AppBarTitleBehaviour`/`MenuTogglePosition` |
| `Components/ThemeSelector.razor(.cs)` | Drawer mit Dark/Light, `MudExThemeSelect`, "Customize" → `MudExThemeEdit` |
| `Components/LanguageSelector.razor`, `RoleSelector.razor`, `UserFilter.razor`, `MandateSelect.razor` | Selects (Sprache, aktive Rollen, User-Multi-Select, Mandate via `MudExSelect`) |
| `Components/Notification/NotificationToggle.razor`, `NotificationView.razor`, `NotificationsList.razor(.cs)`, `AnimationBehavior.cs` | Glocke + Popover, Einzelansicht, Liste |
| `Components/UserAvatar.razor(.cs)`, `UserCard.razor(.cs)` | Avatar (Bild/Gravatar/Initialen, Online-Badge), Karte |
| `Components/AddressEdit.razor(.cs/.css)`, `SimplePropertyEdit.razor(.cs)`, `TablePager.razor`, `PageHeaderTitle.razor`, `ReactOnPreferenceChanged.razor`, `TranslationInlineEdit.razor` | Formular/Utility-Komponenten; `ReactOnPreferenceChanged` rendert Komponente bei Sprachwechsel neu (Reflection auf `StateHasChanged`) |
| `Components/Assistant.razor(.cs)` | KI-Chat (siehe 6.10) |
| `Dialogs/About.razor(.cs/.css)`, `AssistantDialog.razor(.cs)`, `DeleteConfirmation.razor`, `Logout.razor`, `PermissionsRequired.razor`, `SimplePromptDialog.razor` | Dialoge |

### 6.3 Theming

- `src/Client/Theming/ClientTheme.cs`: `ClientTheme : MudTheme, ICloneable` mit Zusatz-Properties (`ShowFilterInDrawer`, `CanPinDrawer`, `Dense`, `RowsStriped`, `BorderedTables`, `DrawerClipMode`, `DrawerVariant`, `NavMenuExpandMode`, `AppBarTitleBehaviour`, `MenuTogglePosition`, `ShowUserCardInNavigation`, `ShowLogoInAppBar`, `ShowLogoInNavMenu`, `PreferDarkMode`, **`Icon`**, **`Logo`**, `StyleAppBar`, `DefaultCardHoverMode`).
- `src/Client/Theming/ClientThemes.cs`: drei statische Themes `DefaultTheme` (Primary #1E88E5), `LuckyGreen` (#199b90), `CodeBlue`; Montserrat-Typography; Dark-Palette via `PaletteLight.ToPaletteDark()` (MudEx). `LastUsedTheme` statisch.
- `src/Client/Theming/ThemeManager.cs` (`IThemeManager`): liefert `ClientThemes.All`; "TODO: Add from server". `BrowserPrefersDarkMode()` via JS.
- **Dark Mode**: `ClientPreference.DarkMode (bool?)` → `MainLayout.IsDarkMode = pref.DarkMode ?? BrowserPrefersDarkMode()`; Theme mit `PreferDarkMode` überschreibt.
- **Persistenz**: nur Browser-LocalStorage (`clientPreference`: `ThemeName`, `ThemeJson` [Custom-Theme aus `MudExThemeEdit`], `IsRTL`, `IsDrawerOpen/Pinned`, `DrawerSingleExpand`, `DarkMode`, `LanguageCode`, `ActiveSelectedRoles`) über `Managers/Preferences/ClientPreferenceManager.cs`. **Keine Server-Preference** (`StorageConstants.Server` = TODO).
- `Enums/AppBarTitleBehaviour.cs` (TitleOnly/AppNameOnly/AppNameAndTitle/Hidden), `Enums/MenuTogglePosition.cs` (Start/End/Hidden) werden von `MainAppBar` → `AppBarHeader` durchgereicht.
- `Configuration/MudExObjectEdit/RenderDataManager.cs`: `AddMudExtensions(WithoutAutomaticCssLoading)` + `RenderDataDefaults` (MandateDto → `MandateSelect`, `UploadRequest` → `MudExUploadEdit`); `MetaConfigurations/` (`BaseDtoMetaConfiguration<T,TId>`, `MandateDtoMetaConfiguration`, `SocialDataDtoMetaConfiguration`, `ClientThemeMetaConfiguration`).

### 6.4 Authentication (Client)

- Token-Speicher: Blazored LocalStorage `authToken`, `refreshToken`, `userImageURL` (`src/Shared/Constants/Storage/StorageConstants.cs`).
- `src/Client/Authentication/ApplicationStateProvider.cs`: `AuthenticationStateProvider` + `ICurrentUserService`; liest `?auth_token=` (Keycloak-Callback) in LocalStorage; Claims aus JWT via `ClaimReader.ReadClaimsFromJwt` (`src/Shared/ClaimReader.cs`); Guest-State bei `AllowAnonymousPageAccess`.
- `src/Client/Managers/Identity/Authentication/ClientAuthenticationManager.cs`: `Login` → `_api.Token_GetAsync`, `TryRefreshToken` (bei `exp` ≤ 1 min → `Token_RefreshAsync`), `RegenerateAndUpdateTokenAsync` (`Token_RegenerateNewAsync`, bei Rollenwechsel/Hub-Events), `Logout`.
- `src/Client/Handler/AuthenticationHeaderHandler.cs`: DelegatingHandler, setzt Bearer aus LocalStorage (auch für gRPC). `Handler/RedirectToBackendHandler.cs` ist leer/ungenutzt.
- HttpClient: benannter Client `ApplicationConstants.ApplicationClientName` mit `BaseAddress = BackendOrigin` + `AddTypedClient<IApplicationClient>(new ApplicationClient(base + "api/v1/", http))`; der injizierte `HttpClient` = `CreateClient(name).EnableIntercept()` (Toolbelt).
- `src/Client/Managers/Interceptors/HttpInterceptorManager.cs`: `BeforeSend` → Token-Refresh (außer token/accounts-Routen), bei Fehler Logout; `AfterSend` → jeder Nicht-2xx → `IErrorHandler.HandleAsync(response)`; **kein expliziter 401-Zweig**.
- BackendOrigin-Ermittlung: Env `BACKEND_ORIGIN`/`DockerBackendOrigin` → bei `HostClientInServer` eigene BaseAddress → sonst Aspire-Service-Discovery (`Services:<name>:https[0]`).
- SignalR-Hub wird **ohne AccessTokenProvider** verbunden (kein JWT am Hub).

### 6.5 Managers, Routes, Extensions, Handler

- `Managers/IManager.cs` (Marker; `AddManagers()` registriert per Reflection `I<Name>`→`Name` transient), `Managers/ExtendedAttribute/ExtendedAttributeManager.cs` (einziger **handgeschriebener HttpClient-Wrapper** mit `Routes/ExtendedAttributesEndpoints.cs`; nur für Document registriert), `Managers/Preferences/ClientPreferenceManager.cs`, `Managers/Interceptors/HttpInterceptorManager.cs`, `Managers/Identity/Authentication/ClientAuthenticationManager.cs`, `Managers/Theme/IThemeManager.cs`.
- `Routes/BaseEndpoints.cs` (`Api="api/v1"`), `Routes/ExtendedAttributesEndpoints.cs`.
- `Extensions/`: `ApiClientExtensions.cs` (leer/auskommentiert), `AuthorizationServiceExtensions.cs` (`HasPoliciesAsync`), `DateTimeExtensions.cs`, `DialogServiceExtensions.cs` (`EditOrCreate<TModel>` → MudExObjectEditDialog, `DefaultDialogOptionsEx` mit Mausposition), `FileResponseExtensions.cs` (`ForceDownloadAsync`), `HttpClientExtensions.cs` (Accept-Language/Authorization/`x-role-id`), `HubExtensions.cs` (`BuildHubConnection`, `EnsureStartedAsync`, typisiertes `On<TClientEvent>`), `NavigationManagerExtensions.cs`, `ResultExtensions.cs`, `ServiceAccessor.cs` (statischer ServiceProvider), `WebAssemblyHostBuilderExtensions.cs` (gesamte DI inkl. `AddAuthorizationCore(RegisterPermissionClaims)`).
- **API-Zugriff: fast ausschließlich über das generierte SDK** (`_api.Controller_ActionAsync`, ~100 Aufrufe); Filter als `TransferableExpression<T>` (OData-String aus Lambda).

### 6.6 Localization (Client)

`AddLocalization` + `IStringLocalizer<>` → `Localization/ApiLocalizer.cs` (statisches `ConcurrentDictionary<CultureInfo, IList<TranslationDto>>`, gefüllt per `_api.Translations_GetAllAsync`; Key-Fallback) + `AddYamlLocalizationWithFallback()` (Shared: `YamlLocalizer<>` + `AggregatedLocalizer<>`). Sprachwechsel in `LanguageSelector.razor`: `ClientPreferenceManager.ChangeLanguageAsync` (Preference, Accept-Language-Header, AKSoftware `SetLanguage`), RTL-Umschaltung, `Reload()`, `Account_SetPreferredLanguageAsync`. Hub-Event `EntitiesUpdated<TranslationDto>` lädt Translations neu. Jede Seite hat `<ReactOnPreferenceChanged Component="this" />`.

### 6.7 JsInterop

- `JsInterop/JsNamespace.cs` (Pfade wie `WayesEdith.BrowserHelper.download` aus `ClientApplicationConfiguration.JsMainNamespace` – Wert in `wwwroot/appsettings.json` ist "WayesEdith"), `JsRuntimeExtensions.cs` (`ObserveMudTabsForStickMerge`), `CustomEventInterop.cs` (Custom-DOM-Events → `[JSInvokable]`), Models `JsAppData`, `BrowserDimension`.
- TypeScript in `wwwroot/js/` (ES2022, kompiliert per Microsoft.TypeScript.MSBuild): `index.ts` (`___initialLoad` führt per `eval` das vom Server generierte Konstanten-JS aus, entfernt Loading-Klassen, enthält ein `debugger;`), `helper/browserHelper.ts` (download, setTitle, isDarkMode, scrollToBottom, playAudio, changeUrl, removeUrlParams, navigateToExternalUrl …), `eventHelper.ts`, `cssHelper.ts`, `colorHelper.ts`, `dropZone.ts` (Drag&Drop/Paste/Ordner-Upload, nur in Profile genutzt), `helper/index.ts`.

### 6.8 ErrorHandling

`ErrorHandling/ErrorHandler.cs` (`IErrorHandler`): parst `ClientProblemDetails` (RFC 7807) → Snackbar (10 s) mit "Show Details" → `MudExMessageDialog` (Debug: "Ask Google"); `IsSuccessFull(IResult)` zeigt `Messages` als Snackbars; `HandleAsync(Exception)`. `ErrorHandling/HealthChecker.cs` pollt `{BackendOrigin}/health` (Intervall `BackendHealthCheckIntervalInSeconds`, default 0 = aus) und zeigt modalen "Not connected"-Dialog.

### 6.9 SignalR (Client)

`HubConnection` ist kein DI-Service; `App.razor.cs` erzeugt sie (`WithUrl(origin + "/eventHub").WithAutomaticReconnect()`) und reicht sie als `CascadingValue` durch. Gesendete Hub-Methoden: `OnConnectAsync`, `OnDisconnectAsync`, `UpdateDashboardAsync`. Abonnierte Events: `EntitiesUpdated<TranslationDto|LanguageDto|DocumentDto|TDto>`, `EntitiesUpdated` (AuditTrails), `ReceiveMessage`, `RegenerateTokens`, `UserRolesChanged`, `LogoutUserById`, `ConnectUser`/`DisconnectUser`, `UpdateDashboard`, `NotificationRequest`, `UserOnlineStatusChanged`, `UserProfileChanged`. `EntitiesCreated/Deleted/Changed` werden **nicht** abonniert.

### 6.10 Assistant und Chat

- Assistant: `Shared/Components/Assistant.razor(.cs)` (MudExSplitPanel, Timeline), `Models/AssistantCommandClientModel.cs`, `Dialogs/AssistantDialog` (Glass-Look, Pin-Button ohne Wirkung). Ruft `_api.Assistant_AskWithHistoryAsync(...)`, streamt Chunks (`ReadAsStringStreamAsync`). Sichtbar nur bei `ServerConfiguration.AssistantAvailable`.
- Chat: `Pages/Communication/Chat.razor(.cs)`; Senden per REST `Chats_SaveMessageAsync`, Empfang per Hub `ReceiveMessage`; `Models/ChatMessage.cs`/`ChatUser.cs` sind **ungenutzt**.

### 6.11 MudBlazor.Extensions-Nutzung

| Komponente/Methode | Dateien | Verwendung |
|---|---|---|
| **`MudExFileDisplay`** | `Pages/Misc/AddEditDocumentModal.razor` (Zeile ~96) | Dateivorschau: `ContentStream` (lokaler Upload) oder `Url` (Server), `FileName`, `ContentType`; nur wenn `Model.URL` gesetzt |
| `MudExFileDisplayDialog` / `ShowFileDisplayDialog` | – | **nicht verwendet** (DocumentList bietet nur Download-Link) |
| `MudExObjectEditDialog<T>` via `DialogService.EditObject` (Wrapper `EditOrCreate`) | `Extensions/DialogServiceExtensions.cs`, `Pages/Catalog/Mandates.razor.cs`, `SocialData.razor.cs` | Create/Edit-Dialog für `MandateDto`, `SocialDataDto` |
| `MudExObjectEditForm` | `Pages/Administration/Settings.razor` | `ServerConfiguration` + `ClientApplicationConfiguration` (Import/Export, StickyToolbar) |
| `MudExPropertyEdit` | `Shared/Components/EditableDataTable.razor`, `EditableDataGrid.razor` | Inline-Zellen-Editor |
| `MudExUploadEdit<T>` | `Pages/Authentication/Register.razor`, `Pages/Catalog/SocialData.razor`, `RenderDataManager.cs` | Datei-Upload (auch als Default-Editor für `UploadRequest`) |
| `MudExSelect<T>` | `MandateSelect.razor`, `UserFilter.razor` | Basis eigener Selects |
| `MudExThemeSelect`, `MudExThemeEdit`, `MudExThemeHelper` | `ThemeSelector.razor(.cs)`, `ClientPreferenceManager.cs`, `ClientTheme(s).cs` | Theme-Auswahl/-Editor/-JSON |
| `MudExTreeView(List)` | `Shared/NavMenu.razor` | Navigationsbaum |
| `MudExPopover`, `MudExSplitPanel`, `MudExGravatar`, `MudExIconButton`, `MudExMessageDialog`, `MudExAppearance`, `MudExStyleBuilder`, `MudExCss`, `MudExSvg`, `MudExColor` | diverse | Layout/Style-Helper |
| `DialogService.ShowEx`, `DialogOptionsEx`, `ShowConfirmationDialogAsync`, `ShowInformationAsync`, `ShowComponentInDialogAsync` | viele Pages/Dialoge | erweiterte Dialoge (Animationen, Resizeable, DragMode) |
| `mud-ex-app-loader` (JS) | `wwwroot/index.html` | Ladeanimation mit Inline-SVG |
| `MudExRichTextEdit`, `BlazorJS` | – | referenziert, **nicht genutzt** |
| nicht genutzt | – | `MudExColorEdit`, `MudExCardList`, `MudExSplitter`, `MudExChipSelect`, `MudExEnumSelect`, `MudExIconPicker` |

### 6.12 Navigation, wwwroot, Docker

- `Models/Navigation/Navigations.cs` + `NavigationEntry.cs` (`Hierarchical<NavigationEntry>`; `Text`, `Icon`, `Href`, `Target`, `Policies`/`PolicyMatch`, `Roles`/`RoleMatch`, `IsAuthenticationRequired`, `Condition`): **statische Liste**, nicht vom Server. Einträge: Home; Swagger (extern); Personal → Dashboard, Account, Audit Trails; Document Management → Document Store, Document Types; Administrator → Site Settings, Users, Roles, Localization → Languages, Translations; Mandates → Mandates; auskommentiert: Database Backups, Chat.
- `wwwroot/index.html`: Fonts Montserrat/Roboto, `js/index.js`, `MudExAppLoader.min.js`, MudBlazor CSS, `css/site.min.css`, CSS-Isolation, `mudBlazorExtensions.min.css`; `<mud-ex-app-loader AppName="" AccentColor="#00324b">` mit Inline-SVG (Text "Y"). Auf `master`: `AppName="Coworkee" AccentColor="#93eb8f"` mit dem vollständigen Coworkee-Logo-SVG inline.
- `wwwroot/appsettings.json`: `BackendOrigin`, `DockerBackendOrigin` ("__BACKEND_ORIGIN__"), `JsMainNamespace` ("WayesEdith"), `AllowAnonymousPageAccess`, `BackendHealthCheckIntervalInSeconds`, Logging. `appsettings.Development.json`: Aspire-Service-Discovery `Services:Coworkee-Server-Api`.
- `wwwroot/css/site.scss` (+`_animations.scss`, `_app_load.scss`, kompiliert via `compilerconfig.json` zu `site.css`/`site.min.css`), `wwwroot/media/background-1.jpg`, `notification.mp3`.
- `Dockerfile`: sdk:9.0 + nodejs → `dotnet publish /p:BackendOrigin=$BACKEND_ORIGIN` → nginx:alpine (ohne sichtbare SPA-Fallback-Config: unklar).

---

## 7. Shared (`src/Shared`)

| Bereich | Dateien | Inhalt |
|---|---|---|
| Wrapper (Result-Typen) | `Wrapper/IResult.cs`, `Result.cs`, `PaginatedResult.cs` | `IResult { List<string> Messages; bool Succeeded }`, `IResult<T> { T Data }`, `Result`/`Result<T>` mit statischen `Success/Fail(Async)`-Factories; `PaginatedResult<T> : Result` (Data, CurrentPage, TotalPages, TotalCount, PageSize, HasPrevious/NextPage). Kein Error-Code, keine typisierten Fehler – nur Strings. Wird gemischt mit Exceptions (ProblemDetails) verwendet. |
| Permissions | `Constants/Permission/Permissions.cs`, `RequiresPermissionsAttribute.cs`, `ApplicationClaimType.cs` | siehe 5.3 |
| Rollen | `Constants/Role/RoleConstants.cs` | `AdministratorRole="Administrator"`, `BasicRole="Basic"` |
| App-Konstanten | `Constants/Application/ApplicationConstants.cs` | `ApplicationName="WayesEdith"` (master: "Coworkee"), `DatabaseName`, `LargeLanguageModel="deepseek-r1"`, Keycloak-Realm/Client/Secret, `HostClientInServer` (`#if HostClient`), `ServiceNames` (Ollama, Keycloak, Grafana, Prometheus, PgAdmin, Stirling, Postgress, SqlServer, Storage, Blobs), `Defaults.Users` (System/Admin/Basic **mit Klartext-Passwörtern**), `Defaults.Roles` ("Product Manager", "Brand Manager" mit Permission-Listen), `Routes` (Login, Register, Forbidden, Redirect, Dashboard="/jobs", ApiDocumentation), `FileAccess` (`Files`, `appsettings.Override.json`), `ParameterNames` (`auth_token`, `id_token`), `HeaderNames.RoleIdHeader="x-role-id"`, `Session` (Refresh 7 d, Token 2 d), `ServiceBusQueues`, `SignalR` (`/eventHub`, `EventRecieved`), `Cache.CacheKeyFor(...)` |
| Storage-Keys | `Constants/Storage/StorageConstants.cs` | Local: `clientPreference`, `authToken`, `refreshToken`, `userImageURL`; Server: `serverPreference` (TODO) |
| Icons | `Constants/CustomIcons.cs` | `ApplicationMainIcon` (siehe 12) |
| Localization | `Constants/Localization/LocalizationConstants.cs`, `LanguageCode.cs` | `DefaultLanguageCode="en-US"`, `DefaultUILanguageCodes` (en, de, it, es, fr), `SystemCultures`, `ExistingTranslations` (aus eingebetteten YAML-Namen), `GetDefaultLanguageResources()` (YamlDotNet) |
| Localizers | `Localizers/YamlLocalizer.cs`, `AggregatedLocalizer.cs`, `Resource.cs` | `YamlLocalizer<T>` wrappt AKSoftware `ILanguageContainerService` (per Reflection auf private `keyValues`/`GetValue`!), `AggregatedLocalizer<T>` fragt alle registrierten `IStringLocalizer<T>` (alphabetisch nach Typname sortiert – Reihenfolge `ApiLocalizer` < `ServerLocalizer` < `YamlLocalizer`) und nimmt den ersten Treffer. `Resource.AddYamlLocalizationWithFallback()` registriert Yaml an Position 0 + Aggregated + `AddLanguageContainer`. |
| Resources | `Resources/*.yml` (17 Kulturen: ar-SA, bg-BG, cs-CZ, da-DK, de-DE, el-CY, en-US, es-ES, fr-FR, hr-HR, it-IT, ja-JP, ko-KR, nl-NL, pl-PL, sv-SE, tr-TR), `resourceTranslationOptions.json` | **Automatische Übersetzung**: MSBuild-Target `Translate` in `Shared.csproj` (Debug) installiert `ResourceTranslator.CLI` global und übersetzt `en-US.yml` (1139 Zeilen, Key = englischer Text, flache Key:Value-YAML) per Azure Translator in 16 Zielsprachen. Key in `resourceTranslationOptions.json` (**Secret im Repo**). YAML als `EmbeddedResource`. |
| Mail-Templates | `EmailTemplate.cs`, `EmailTemplateNames.tt` → `EmailTemplateNames.cs`, `Resources/Emails/{de-DE,en-US}/{Activated,Footer,Forgot-Password,NewChatMessage,Register}.cshtml`, `Resources/Emails/Shared/Layout.cshtml` | Templates = Razor-`.cshtml` als **EmbeddedResource**; `EmailTemplate.Get(name, culture)` sucht `{Assembly}.Resources.Emails.{culture_}.{name}.cshtml` mit Fallback en_US → beliebig. **T4-Template** `EmailTemplateNames.tt` generiert typisierte Zugriffe (`EmailTemplate.Register(culture)`, `EmailTemplate.DE_DE.Register`, `Names.*`). Layout hat hart codiert "Coworkee" im Header, `@RenderBody()`, `@Include("Footer", Model)`. Rendering in Infrastructure (RazorEngineCore, ohne Cache). **Keine DB-/Admin-Verwaltung der Templates**, kein Template-Editor, keine Versionierung – Änderung = Rebuild. |
| Preferences | `Settings/IPreference.cs`, `Managers/IPreferenceManager.cs` | `IPreference { LanguageCode }`, `IPreferenceManager { Set/GetPreference, ChangeLanguageAsync }` – gemeinsame Basis für Client-/Server-Preference |
| Helper | `Helper/DatabaseProviderDetector.cs`, `ClaimReader.cs`, `TransferableExpression.cs`, `Extensions/ObjectExtensions.cs` (`CopyChangedValuesTo` – Reflection, TODO "Mapster/AutoMapper"), `Extensions/CultureExtensions.cs` | `TransferableExpression<T>`: wandelt `Expression<Func<T,bool>>` clientseitig in einen OData-`$filter`-String (Nextended `FilterString.Generate`) und serverseitig zurück (StringToExpression) – zentrales Muster für typisierte Filter über die API |
| Models | `Models/CreateUser.cs` | Seed-User-Modell |

Fazit Shared: enthält bereits das, was ein `Coworkee.Shared`-NuGet braucht (Result, Permissions-Infrastruktur, Localization, TransferableExpression), aber gemischt mit **App-spezifischen Konstanten** (`ApplicationName`, Default-User/Passwörter, Rollen mit Beispiel-Permissions, Keycloak-Secret), die in die App bzw. in Optionen gehören.

---

## 8. SDK (`src/SDK`)

| Datei | Inhalt |
|---|---|
| `Net/v1/ClientGenerated.cs` | **NSwag 14.3** `openApiToCSharpClient`: eine Klasse `GeneratedClient : ClientBase` (`SingleClientFromOperationId`, Methoden `Controller_ActionAsync`), Interface `IGeneratedClient`, Namespace `SDK`, **`generateDtoTypes=false`** – DTOs kommen aus `Coworkee.Application` (daher ProjectReference auf Application). 11 283 Zeilen. Nach der Generierung läuft das Tool **`makeGenericAgain`** (nksoft.MakeGenericAgain) über die Datei, um von NSwag "entgenerifizierte" Typen (z. B. `ResultOfString`) wieder generisch zu machen. `parameterDateTimeFormat "s"`, `System.DateTimeOffset`. |
| `Net/v1/ClientGenerated.partial.cs` | `SwaggerUrl`, `PrepareRequest`/`ProcessResponse`-Delegates, `GetHttpClient()` |
| `Net/v1/FileResponse.partial.cs` | `FileResponse`-Erweiterungen (`IsSuccessStatusCode`, `MimeType`, `FileName`, `Base64String`, `JsDownloadObject`, `ReadAsStringStreamAsync` chunked) |
| `Net/Extensions/StreamExtensions.cs` | `AsChunkedUtf8Strings` |
| `ApplicationClient.cs` | `ApplicationClient : GeneratedClient, IApplicationClient` + handgeschriebenes `AskAssistantAsync` (Streaming `Assistant/Ask`) |
| `ClientBase.cs` | `BaseUrl` |
| `Angular/v1/web-api-client.ts` | NSwag `openApiToTypeScriptClient` (Template Angular, HttpClient, `{controller}Client`, DTO-Klassen generiert, 12 877 Zeilen): `AccountClient, AssistantClient, AuditsClient, ChatsClient, DashboardClient, DatabaseClient, DocumentExtendedAttributesClient, DocumentTypesClient, DocumentsClient, LanguageClient, MandatesClient, NotificationsClient, PreferencesClient, ResourceClient, RoleClaimClient, RoleClient, SocialDataClient, SystemClient, TokenClient, TranslationsClient, UserClient` |
| `SDK.csproj` | `IsPackable`, `GeneratePackageOnBuild`, PackageId `Coworkee.SDK`, Version `0.0.0-dev`, Target `CopyPackage` kopiert die `.nupkg` nach `Server/wwwroot/sdk/` (Download über `/package/download/nuget`, Link im Swagger-Header) |

Generierung: `src/Server/nswag.json` (Document `aspNetCoreToOpenApi` → `wwwroot/api/specification.json`, OpenApi3, Enums als Integer, Titel "Coworkee API") + MSBuild-Target `NSwag` in `Server.csproj` (AfterBuild, nur Debug: `$(NSwagExe_Net90) run /v1/swagger.json`) + Target `MakeGenericAgain`. Kein Kiota. Für die Lib-Familie problematisch: Der SDK-Client ist an die konkreten Controller/DTOs der Beispiel-App gebunden; ein generisches `Coworkee.Client.Blazor` müsste die Basis-Endpunkte (Identity, Translations, Notifications, Documents …) über ein eigenes, stabiles Interface kapseln.

---

## 9. Aspire (`src/Aspire`)

### 9.1 AppHost (`Coworkee.AppHost/Program.cs`)

Konfiguration per `CoworkeeAppHostSettings` (`Types/CoworkeeAppHostSettings.cs`): aktuell `DatabaseToUse=SqlServer, AddOllama=false, AddKeycloak=false, AddGrafana=false, AddStirling=true, AddAzureStorage=false`. `AspireMainAppHelper.AddDependencyServices` (`ApplicationServiceHelper/AspireMainAppHelper.cs`) baut die Ressourcen:

| Ressource | Helper | Details |
|---|---|---|
| Azure SignalR | `SignalRHelper.cs` | nur im Publish-Mode (`AddAzureSignalR("signalr")`) |
| SQL Server **oder** PostgreSQL | `DbHelper.cs` | `AddSqlServer("SqlServer")` bzw. `AddPostgres("Postgress").WithPgAdmin(...)`; DataVolume nur außerhalb Run-Mode; `ContainerLifetime.Persistent`; DB `DefaultConnection` |
| Keycloak | `KeycloakExtensions.cs` | `AddKeycloak` (quay.io keycloak 26.0, `--features=preview`, DataVolume, Aspire-Command "Seed Client" → `KeycloakSeeder`), zusätzlich Dev-Cert-Helfer (`RunKeycloakWithHttpsDevCertificate`) |
| Azure Storage (Blobs) | `StorageHelper.cs` | `AddAzureStorage("storage")` + Azurite-Emulator (Ports 27000–27002) + `AddBlobs("blobs")` – **im Server nicht konsumiert** |
| Ollama + Open WebUI + Modell | `OllamaHelper.cs` | CommunityToolkit `AddOllama` mit `AddModel(deepseek-r1)`, OpenWebUI |
| Grafana + Prometheus + OTel-Collector | `GrafanaHelper.cs`, `OpenTelemetryCollector/*` (eigene Aspire-Resource nach practical-otel), `grafana/`, `prometheus/`, `otelcollector/config.yaml` | Dashboards `aspnetcore.json`, `aspnetcore-endpoint.json` |
| Stirling-PDF | `StirlingHelper.cs` | Container `stirlingtools/stirling-pdf` (für den WayesPDFParser-Import) |
| Server-Projekt | `Program.cs` | `AddProject<Projects.Server>` + `WithEndpointsAsEnvironmentIf` (Nextended.Aspire: schreibt alle Service-Endpunkte als `PublicSettings__Endpoints__{Name}` in die Server-Env → daher `PublicSettings.Endpoints`-Dictionary), `WaitForIf`, `WithReferencesIf` (ConnectionStrings + ServiceDiscovery), `WithExternalHttpEndpoints` |
| WASM-Client | `Program.cs` | nur wenn `!HostClientInServer`: `api.AddWebAssemblyClient<Projects.Client>` (Aspire4Wasm) |

Kein Redis, kein Mail-Container (z. B. Mailpit/smtp4dev), kein ServiceBus-Emulator (Paket referenziert, ungenutzt), kein Cosmos (Paket referenziert, ungenutzt). `Settings.job` ist ein leeres WebJob-Schedule-Template. `.azure/Coworkeedev/` enthält azd-Environment.

Linked Files: Der AppHost kompiliert `appsettings.g.cs`, `ServerConfiguration.cs`, `KeycloakSeeder.cs`, `ApplicationConstants.cs`, `Permissions.cs`, `RequiresPermissionsAttribute.cs`, `RoleConstants.cs`, `CreateUser.cs` per `<Compile Include=... Link=...>` mit – ein Workaround, um Shared-Konstanten ohne Projektreferenz zu nutzen (fragil).

### 9.2 ServiceDefaults (`Coworkee.ServiceDefaults/Extensions.cs`)

Standard-Aspire-ServiceDefaults: `AddServiceDiscovery`, `ConfigureHttpClientDefaults` (StandardResilience), OpenTelemetry (Logging, Metrics inkl. Prometheus-Exporter, Tracing inkl. gRPC-Client), OTLP-Exporter bei `OTEL_EXPORTER_OTLP_ENDPOINT`, Azure Monitor bei `APPLICATIONINSIGHTS_CONNECTION_STRING`, HealthChecks `/health` + `/alive` (mit RequestTimeout/OutputCache-Policies), `MapPrometheusScrapingEndpoint`. Wiederverwendbar 1:1.

---

## 10. Tests (`tests/`)

| Projekt | Framework | Inhalt | Bewertung |
|---|---|---|---|
| `Application.IntegrationTests` | xUnit, `WebApplicationFactory<Program>`, **Testcontainers MsSql** (`mcr.microsoft.com/mssql/server:2019-latest`, Random-Port), Environment `Testing` | `Base/ServerApplicationFactory.cs` (Container pro Test-Klasse, ersetzt `ApplicationDbContext`), `Base/TestFixture.cs` (`CreateAuthorizedClient(CreateUser)` holt JWT direkt über `ITokenService.LoginAsync`), `Base/BaseTest.cs` (Admin/Basic/Unauthorized `HttpClient` + `IApplicationClient`, `IsHealthy`), `Tests/SystemControllerTests.cs` (4 Tests: Version, AuthorizeServerUrl, 401, 403) | Solide Basis (echte DB, SDK-Client als Testtreiber), aber **nur 5 Tests**. **Respawn wird nicht verwendet** (README nennt es; kein Paket, keine Nutzung). `Microsoft.EntityFrameworkCore.InMemory` referenziert, ungenutzt. |
| `Application.UnitTests` | NUnit, FluentAssertions, Moq | `Common/Exceptions/ValidationExceptionTests.cs` (3 Tests), `Common/Models/HashedIntTests.cs` (7 Tests, Hashids/ClassMapping), `Common/Behaviours/RequestLoggerTests.cs` **komplett auskommentiert** (referenziert `CreateTodoItemCommand` aus dem Ur-Template) | minimal; keine Handler-Tests, keine Behaviour-Tests |
| `Domain.UnitTests` | NUnit | `ValueObjects/EntityExtendedAttributeTypeTests.cs` (1 Test: Enum hat 5 Felder) | trivial |
| `Application.UITests` | xUnit, **Playwright** 1.51, `Aspire.Hosting.Testing` (startet `Coworkee.AppHost`, wartet auf Resource Running, optional `UITEST_FRONTEND_URL`) | `LoginTests.Test_LoginFlow` (Login als Admin, prüft `h2` enthält `ApplicationName`) | 1 E2E-Test; Fixture ist wiederverwendbar. Kein bUnit. |

Kein CI-Workflow führt Tests aus (`master_coworkee.yml` baut nur). Test-Gesamtstand: ~16 Tests, Abdeckung der Kernlogik (Handler-Basisklassen, Permission-System, Mail, Import/Export) = 0.

---

## 11. Bewertung für die Lib-Familie "Coworkee.*"

### 11.1 Gut / direkt wiederverwendbar

| Baustein | Ort | Ziel-Lib |
|---|---|---|
| `AuditableEntity<TId>`, `IEntity`, EAV-Basis (`AuditableEntityExtendedAttribute`, `IEntityWithExtendedAttributes`) | `src/Domain/Contracts/` | `Coworkee.Domain` |
| Generische Entities `Document`, `DocumentType`, `Notification`, `Language`, `Translation`, `UserInformations`, `Address` | `src/Domain/Entities/` | `Coworkee.Domain` (Document/DocumentType ggf. als optionales Modul, da Shareme ein eigenes Asset-Modell braucht) |
| `Result`/`Result<T>`/`PaginatedResult<T>` | `src/Shared/Wrapper/` | `Coworkee.Shared` |
| `TransferableExpression<T>` + `ODataQueryOptionsExtensions` | `src/Shared/TransferableExpression.cs`, `src/Application/Common/Extensions/` | `Coworkee.Shared` / `Coworkee.Application` |
| YAML-Localization (`YamlLocalizer`, `AggregatedLocalizer`, `LocalizationConstants`) + Auto-Translate-Target | `src/Shared/Localizers/`, `Shared.csproj` | `Coworkee.Shared` (Translate-Target muss in der App bleiben; YAML per App) |
| DB-Translations (`Translation`-Entity, `ServerLocalizer`, `ApiLocalizer`, Pages Languages/Translations, `TranslationInlineEdit`) | Server/Client/Application | `Coworkee.Infrastructure` + `Coworkee.Client.Blazor` |
| Feature-Basisklassen (AddEdit/Delete/GetAll/GetAllPaged/GetById/Export/Import) | `src/Application/Features/Base/` | `Coworkee.Application` – nach MediatR-Ablösung (11.4) |
| Specification-Minimalismus, `QueryableExtensions.Specify/ToPaginatedListAsync` | `src/Application/Specifications/Base/`, `Common/Extensions/` | `Coworkee.Application` |
| Generic Repository + UnitOfWork (+ LazyCache-Invalidierung) | `src/Infrastructure/Repositories/` | `Coworkee.Infrastructure` (Dispose-Bug fixen) |
| `AuditableContext` (Audit-Trail via ChangeTracker) | `src/Infrastructure/Contexts/AuditableContext.cs` | `Coworkee.Infrastructure` |
| Identity-Erweiterungen (`ApplicationUser/Role/RoleClaim`, JWT+Refresh, Keycloak-Login, `IsSelectableByUser`, Registrierung mit Admin-Aktivierung/E-Mail-Bestätigung) | `src/Infrastructure/Models/Identity/`, `Services/Identity/` | `Coworkee.Infrastructure` |
| SignalR-Client-Events (`EventTarget` mit Permission/Role/User-Gruppen, `EntitiesUpdated<TDto>`, `HubBase`) | `src/Application/Hubs/` | `Coworkee.Application` (ohne MediatR-`INotification`) |
| Client: `EditableDataTable`, `UpdateOnChange`, `ClientTheme`/`ThemeSelector`, `ClientPreferenceManager`, `ErrorHandler`, `HttpInterceptorManager`, `ApplicationStateProvider`, Identity-Pages (Users/Roles/RolePermissions/UserRoles/Profile), Notification-Komponenten, `NavMenu`+`NavigationEntry`, `LanguageSelector`, `ExtendedAttributesBase` | `src/Client/Shared/`, `Pages/Identity/`, `Theming/`, `Managers/` | `Coworkee.Client.Blazor` (als Razor Class Library) |
| Export/Import-Services (Csv/Excel/Json), Upload/FileAccess, Notification-Service, Mail-Services (SMTP/SendGrid), RazorEngine-Mail-Rendering | `src/Infrastructure/Services/` | `Coworkee.Infrastructure` |
| Aspire ServiceDefaults, AppHost-Helper (Db/Keycloak/Ollama/Grafana/Storage) | `src/Aspire/` | eigenes `Coworkee.Aspire`-Paket oder Template |
| Config-Codegen (`Nextended.CodeGen` appsettings → Klassen), `RegisterAs`-Attribut-Registrierung, `RegisterIfConfig*` | Server/Application | `Coworkee.Core` (Registrierungs-Infrastruktur) |
| Swagger/NSwag-Setup mit Auto-Login + nupkg-Download, API-Versioning | `src/Server/Extensions/` | `Coworkee.Server`-Host-Paket (fehlt in der geplanten Liste – empfehlenswert: `Coworkee.AspNetCore` mit Startup-Extensions) |

### 11.2 Schwach / fehlt (inkl. der vom Besitzer genannten Punkte)

1. **Module-System fehlt.** Es gibt keine Modul-Abstraktion; alles wird in `Startup.cs` per Extension-Methoden bzw. Attribut-Scan verdrahtet. Entities/DbSets, Permissions, Navigation, Seed-Daten, Validators, Handler, SignalR-Events und Client-Pages einer "Feature-Domäne" sind über 5 Projekte verteilt. Eine App kann weder DbSets zum Context beitragen (ein fester `ApplicationDbContext` mit hart codierten DbSets) noch Menüeinträge, Permissions oder Seeds ohne Änderung der Lib. Der Branch `origin/app-framework-3` enthält einen Copilot-generierten Prototyp (`Coworkee.Core/ModuleLayer.cs`: `IModuleLayerEntrance { ConfigureServices; Configure }`, `AddCoworkeeModule<T>()`, `IPermissionProvider`, `IRoleProvider`, Beispiel `Example.Application/ExampleApplicationModule.cs`) – nur Skelett, keine DbContext-/Navigation-/UI-Contributions.
2. **Mail-Template-Verwaltung fehlt.** Templates sind eingebettete `.cshtml` (Rebuild bei Änderung), T4-generierte Namen, kein DB-Store, keine Admin-UI, keine Versionierung, kein Template-Test/Preview, Layout mit hart codiertem "Coworkee". Rendering kompiliert bei jedem Aufruf neu (Performance). Vorschlag: `IEmailTemplateStore` (Embedded-Default → DB-Override), Template-Entity (Name, Culture, Subject, Body, Layout, Version), Admin-Page mit `MudExRichTextEdit`/Code-Editor + Preview mit Beispiel-Modell, kompilierter Cache mit Invalidierung.
3. **Rechteverwaltung: statische Konstanten, kein Contributor.** Permissions sind Compile-Time-Konstanten in `Coworkee.Shared` (siehe 5.3) mit Namensresten (`Permissions.Products.*`, `Permissions.Brands.*`). Ein `IPermissionDefinitionProvider`/`IPermissionContributor` (pro Modul: Gruppe, Name, DisplayName-Key, Abhängigkeiten, Default-Rollen) plus Registry fehlt; die Policy-Registrierung (Server `AddAuthorization`, Client `AddAuthorizationCore`) und die UI (`RolePermissions.razor`) müssten aus dieser Registry gespeist werden statt per Reflection über eine Klasse. Drei parallele Prüfmechanismen (`[Authorize(Policy)]` JWT, `[CustomAuthorize]` DB, `AuthorizationBehaviour` DB) sollten auf einen reduziert werden. Mehrere Controller-Actions sind versehentlich anonym (Import-Endpunkte, `AskOllama`, `SendToServiceBus`, `Token`-Login natürlich gewollt).
4. **Persistenz-Lücken:** `ServerStorageProvider` ist ein Placeholder (Server-Preferences gehen verloren), `IServiceBus` hat keine Implementierung, `IDictionaryService` ist ein nicht threadsicheres Singleton-Dictionary, EF-Migrations sind tot (EnsureCreated), keine Postgres-Migrations, nur ein DbContext für Identity+Domain+Audit+Chat.
5. **Blob-Storage fehlt:** Uploads liegen im `wwwroot/Files`-Dateisystem; Aspire provisioniert Azure Storage, aber kein `IFileStorage`-Provider (Local/Azure Blob/S3) existiert. Für Shareme (Asset-Management) zentral.
6. **Nullable disabled**, 47 TODOs, `.Result`/`.Wait()`-Blocking (HubBase, DatabaseSeeder), Dispose des DbContext im UnitOfWork, `MapTo<TDto>()` als EF-Select-Expression (Client-Evaluation), N+1 im Dashboard, Logging-Behaviour mit DB-Lookup pro Request, RazorEngine ohne Cache.
7. **Sicherheit:** Secrets in `appsettings.json`, `resourceTranslationOptions.json`, `.azure/COWORKEE/config.json`; Klartext-Default-Passwörter in `ApplicationConstants`; JWT ohne Issuer/Audience; SignalR-Hub ohne Token; `System/{queue}` und Import-Endpunkte anonym; DB-Backup-SQL per String-Interpolation; `ResourceController` liefert nupkg ohne Auth.
8. **Lokalisierung dreifach:** resx (Infrastructure/Server), YAML (Shared), DB-Translations (Server/Client) mit `AggregatedLocalizer`-Reihenfolge nach Typnamen – sollte auf YAML-Default + DB-Override konsolidiert werden.
9. **Tests:** ~16 Tests, kein Respawn trotz README, keine Handler-/Permission-Tests, CI führt keine Tests aus.
10. **Repo-Hygiene:** `Server.csproj.Backup*.tmp`, `Output.zip`, eingecheckte `.bak`/PDF unter `wwwroot/Files`, `clientEventTest.html` (veraltet), auskommentierte Klassen (`ProductController`, `RequestLoggerTests`, `AddProduct.ExecuteAsync`), leere Ordner `src/Core`, `src/Client.Blazor`, `src/Example.Application`, `lib/*` (nur bin/obj), veralteter GitHub-Workflow (.NET 7), `dotnet-tools.json` mit EF 5.0.11, ungenutzte Pakete (BlazorJS, MudExRichTextEditor, EF InMemory, Aspire ServiceBus/Cosmos, Blazored.LocalStorage in Infrastructure).

### 11.3 Eng mit dem Beispiel-Domain verwoben (muss generisch werden)

| Stelle | Kopplung |
|---|---|
| `src/Domain/Entities/Catalog/*` (Mandate, SocialData, PersonData, EmploymentData, InsuranceData, PayrollData, BankingData) | komplett Beispiel/Kunde |
| `src/Application/Features/{Mandates,SocialData}/`, `Specifications/Catalog/`, `Validators/Features/{Brands,Products}/`, DTOs `MandateDto`, `SocialDataDto`, `*DataDto`, `AssistantFeatures/AddProduct.cs` | Beispiel |
| `src/Application/Application.csproj` → `parserMain/WayesPDFParser` ProjectReference; `Features/SocialData/Export|Import` nutzen `WayesPDFParser.*` direkt | **harte Fremdabhängigkeit in der Application-Schicht** |
| `src/Application/Features/Dashboards/GetDashboardDataQuery.cs`, `DashboardDataResponse`, `Server/gRPC/Main.proto` (Product/Brand-Messages), `Client/Pages/Content/Dashboard.razor` | zählt Beispiel-Entities |
| `src/Shared/Constants/Permission/Permissions.cs` (`SocialData`, `Mandates` mit `Products`/`Brands`-Strings), `ApplicationConstants.Defaults.Roles` ("Product Manager", "Brand Manager"), `ApplicationName="WayesEdith"`, `DatabaseName`, `LargeLanguageModel`, Keycloak-Client-Secret, Default-User | App-Konfiguration in der Shared-Lib |
| `src/Infrastructure/Contexts/ApplicationDbContext.cs` (DbSets `Mandates`, `SocialData`), `Repositories/MandateRepository.cs`, `SocialDataRepository.cs`, `Configurations/SocialDataConfiguration.cs` | Beispiel |
| `src/Server/Controllers/Catalog/*`, `BackgroundServices/ProductFeedSyncService.cs`, `Controllers/OData/ProductController.cs`, `SystemController.{queue}` (Body `SocialDataDto`) | Beispiel |
| `src/Client/Pages/Catalog/*`, `Shared/Components/MandateSelect.razor`, `Configuration/MudExObjectEdit/MetaConfigurations/{Mandate,SocialData}DtoMetaConfiguration.cs`, `RenderDataManager.cs` (MandateDto-Default), `Models/Navigation/Navigations.cs` (Mandates-Menü), `wwwroot/appsettings.json` (`JsMainNamespace="WayesEdith"`), `wwwroot/index.html` (Loader-SVG "Y", AccentColor) | Beispiel |
| `src/Shared/Constants/CustomIcons.cs` ("WAYES"-Text-SVG) | Branding |
| `src/Shared/Resources/Emails/Shared/Layout.cshtml` ("Coworkee" hart codiert) | Branding |
| `Aspire/Coworkee.AppHost` (`AddStirling=true` für PDF-Import) | Beispiel |
| SDK (`ClientGenerated.cs`, `web-api-client.ts`) | enthält Mandates/SocialData-Clients; muss pro App generiert werden |

Sauberer Ausgangspunkt für die Generalisierung ist **`master`** (Brand/Product-Beispiel, Coworkee-Branding), nicht `wayes-edith`.

### 11.4 MediatR ablösen – wo und wie

**Ist:** MediatR wird für (a) Request-Dispatch aus Controllern/gRPC, (b) Pipeline-Behaviours (Validation, Authorization, Logging, Performance, Exception, ClientEvent), (c) `INotification` für SignalR-Client-Events, (d) Handler-in-Handler-Aufrufe (`ImportQueryHandlerBase` → `AddEditCommand`, `ProductFeedSyncService`) verwendet. Die fachliche Logik liegt in den Basisklassen und hängt nur über `IRequest<T>`/`IRequestHandler<,>`-Signaturen und `IMediator`-Konstruktor-Parameter an MediatR.

**Empfehlung (in dieser Reihenfolge, kleinster Eingriff zuerst):**

1. **SignalR-Events entkoppeln:** `MediatorExtensions.PublishClientEvent(s)` → eigenes `IClientEventPublisher { Task PublishAsync(ClientEventBase e, EventTarget? target, CancellationToken) }` mit Implementierung = heutiger `ClientEventDispatcher` (`IHubContext<ClientEventHub>`). Betrifft 8 Dateien; `ClientEventNotification`/`INotification` entfallen.
2. **Eigenes Dispatch-Interface statt MediatR:** `ICommand<TResult>`/`IQuery<TResult>` (Marker) + `ICommandHandler<TCommand,TResult>` / `IQueryHandler<TQuery,TResult>` + ein ~40-Zeilen-`IDispatcher` (`Send<TResult>(IRequest<TResult>)` → `GetRequiredService(typeof(IHandler<,>).MakeGenericType(...))`, Cache per `ConcurrentDictionary<Type, Func<...>>`). Registrierung per Assembly-Scan (offene Generics werden von MS.DI unterstützt; die EAV-Handler-Registrierung in `AddExtendedAttributesHandlers` bleibt gleich, nur mit dem neuen Interface). Controller behalten `Dispatcher.Send(...)` – Änderung ist ein Find/Replace von `Mediator`→`Dispatcher` und `IRequest`→`ICommand/IQuery`.
3. **Behaviours als Decorator-Kette im Dispatcher:** Validation, Authorization, Logging/Performance, Exception als `IPipelineStep` in fester Reihenfolge im Dispatcher ausführen (statt generischer `IPipelineBehavior<,>`-Registrierung). Das `ClientEventBehaviour` (Before/AfterRequest für jeden Request) ist kaum genutzt und kann entfallen.
4. **Alternative "direkte Services":** Für die generischen CRUD-Basen ist ein `ICrudService<TEntity,TDto,TId>` (GetAll/GetPaged/GetById/AddEdit/Delete/Export/Import) mit Permission-Metadaten oft einfacher als Command-Klassen pro Feature; der Vorteil der Command-Klassen (Serialisierung als API-Payload, NSwag-DTO-Typen, `[CustomAuthorize]` am Request) spricht aber dafür, die Request-Klassen als reine POCOs zu behalten und nur den Dispatcher zu tauschen. Wolverine wäre möglich, bringt aber eine neue, große Abhängigkeit (Messaging, Codegen) – für eine Lib-Familie unnötig; ein eigener Dispatcher hält die Libs abhängigkeitsarm und ist trivial testbar.
5. Die transitive MediatR-Abhängigkeit im Client verschwindet, sobald `IRequest<>` nicht mehr auf den API-Payload-Klassen liegt (eigene Marker-Interfaces in `Coworkee.Shared` oder `Coworkee.Application.Abstractions`).

### 11.5 Vorschlag Schnitt der Lib-Familie (abgeleitet aus dem Ist)

| NuGet | Inhalt aus dem Template | Abhängigkeiten |
|---|---|---|
| `Coworkee.Core` | Modul-Abstraktion (`ICoworkeeModule`: ConfigureServices/Configure/ConfigureDbModel/Permissions/Navigation/Seeds), `RegisterAs`/`RegisterIfConfig`-Registrierung, Dispatcher + Pipeline, `Result`-Typen?, Options-Basis | MS.Extensions.DI/Configuration |
| `Coworkee.Shared` | Result/PaginatedResult, `TransferableExpression`, Permission-Definition-Typen (`PermissionDefinition`, `PermissionGroup`, `[RequiresPermissions]`), Claim-Typen, Localization-Konstanten, `IPreference`, `StorageConstants`, DTO-Basen (`DtoBase`, `HashableDtoBase`), Client-Event-Typen | Nextended.Core |
| `Coworkee.Domain` | `IEntity`, `AuditableEntity`, EAV-Basis, generische Entities (Notification, Language, Translation, UserInformations, Address, optional Document/DocumentType) | – |
| `Coworkee.Application` | Feature-Basisklassen, Specifications, Contracts (Repos/Services), Validators-Basis, Hub-Events/`EventTarget`, Serialization, `HashedInt` | Domain, Shared, FluentValidation, LazyCache |
| `Coworkee.Infrastructure` | `CoworkeeDbContext` mit Modul-Hooks (`IDbModelContributor`), Identity-Modelle/-Services, Repos/UoW, Audit, Mail (SMTP/SendGrid + Template-Store), Notifications, Export/Import, File-Storage (Local + Blob), Translations-Service, Seeder-Infrastruktur | Application, EF Core (SqlServer + Npgsql als separate Provider-Pakete), Identity, Hangfire |
| `Coworkee.AspNetCore` (neu) | Startup-Extensions (JWT/Keycloak, Permission-Policies aus Registry, NSwag, Versioning, OData, SignalR-Hub, Exception-Filter, Middlewares), Basis-Controller (Identity, Translations, Notifications, Audits, System, Documents), gRPC optional | Infrastructure |
| `Coworkee.Client.Blazor` | Razor Class Library: Layouts, Theming, Auth, Interceptor, ErrorHandler, `EditableDataTable`, Identity-/Localization-/Notification-Pages, `NavigationEntry`-Registry (aus Modulen), SDK-Basis-Interface für Kern-Endpunkte | MudBlazor, MudBlazor.Extensions, Shared |
| `Coworkee.Aspire` (optional) | ServiceDefaults + AppHost-Helper | Aspire |

### 11.6 Vorarbeiten in Git-Branches (relevant für den Umbau)

| Branch | Stand | Inhalt |
|---|---|---|
| `app-framework-lib-2` (lokal + origin) | 2025-09-04 | `lib/Coworkee.{Core,Domain,Application,Infrastructure,Shared,Client.Blazor}` mit csproj (272 Dateien), `props/{Shared,Package,Version,Output}.props` (NuGet-Packaging, GPL-3.0-Lizenz-Text aus Nextended kopiert), `src/Application` referenziert `lib/Coworkee.Application` + `lib/Coworkee.Core`. `Coworkee.Core` = nur `IModuleLayerEntrance` + `ModuleLayer`; `Coworkee.Client.Blazor` = leerer RCL-Stub. Application-Lib enthält noch Brand/Product-DTOs. |
| `origin/app-framework-3` | 2025-09-04 (Copilot-Commits "Complete modular refactoring …") | `src/Coworkee.{Core,Domain,Application,Infrastructure,Shared,Client.Blazor}` + `src/Example.Application` (Konsolen-Demo mit `ExampleApplicationModule : ApplicationModuleLayer`, `BusinessPermissionProvider : PermissionProviderBase`, `BusinessRoleProvider`). `Coworkee.Core`: `ModuleLayer.cs`, `Providers/IPermissionProvider.cs`, `IRoleProvider.cs`, `Extensions/ServiceCollectionExtensions.cs` (`AddCoworkeeModule<T>`, `AddPermissionProvider<T>`, `GetAllPermissions()`). Baut laut Commit; keine echte Integration in Server/Client. |
| `feat/net10` | 2025-11-12 | .NET-10-Update (nicht analysiert) |
| `features/gRPC_and_NewAuth`, `features/EntityTranslation`, `features/WebDeploy`, `origin/mud-8-and-aspire` | älter | Feature-Branches |
| `master` | 2025-10-28 | generischer Template-Stand (Brand/Product, Coworkee-Branding, "odata step 1", "Add delta", "add new data grid" – 3 Commits, die `wayes-edith` nicht hat) |

Die leeren Ordner `src/Core`, `src/Client.Blazor`, `src/Example.Application`, `lib/*` im Working Tree sind Reste dieser Branches (Commit `a152c3a "Create Core framework foundation and rename Client to Client.Blazor"`).

---

## 12. Coworkee-Icon / Logo – exakte Fundstellen

**Es gibt im Repository keine Bild-Datei des Coworkee-Logos** (kein `favicon.ico`, kein `icon-*.png`, kein `logo.svg`, kein `manifest.json`). Das Logo existiert ausschließlich als **Inline-SVG-String in C#** und wird zur Laufzeit gerendert.

| Was | Pfad | Inhalt |
|---|---|---|
| **Hauptquelle (Working Tree, Branch `wayes-edith`)** | `C:\dev\privat\github\CleanArchitectureBaseBlazor\src\Shared\Constants\CustomIcons.cs` | `public static string CustomIcons.ApplicationMainIcon` – aktuell ein **Platzhalter**: `<svg><text ... fill="#00324b">WAYES</text></svg>` (Kunden-Branding) |
| **Original-Coworkee-Logo** | gleiche Datei auf **`master`**: `git show master:src/Shared/Constants/CustomIcons.cs` | vollständiges SVG, `viewBox="0 0 108.11 108.11"`, ~6 KB: mehrfarbiges "C"-Emblem aus Kreissegmenten mit 14 `linearGradient`s (Farben #c835f8→#0276db, #ff0→#07aba0, #ed376a→#ff0, #ed376a→#c835f8, #5e17a9→#c40015, Flächen #f9d92e, #fef163, #f98afe, #300c50, #005b50, #fef7a2, #f6dcfd, #01052d), Masken/ClipPaths, Layer-Namen `Ebene_2`/`Layer_1` (Illustrator-Export). Historie: Commits `5cba760 FavIcon from SVG` (löschte `src/Client/wwwroot/favicon.ico`), `6418dc7 Change Icon`. |
| Inline im Loader (master) | `git show master:src/Client/wwwroot/index.html` | dasselbe SVG in `<mud-ex-app-loader AppName="Coworkee" AccentColor="#93eb8f">` (Working Tree: Text-SVG "Y", `AccentColor="#00324b"`) |
| Verwendung Client | `C:\dev\privat\github\CleanArchitectureBaseBlazor\src\Client\Shared\Components\ApplicationLogo.razor` | `MudIcon Icon="@CustomIcons.ApplicationMainIcon"` mit `FillColor`/`StrokeColor` (via `MudExSvg.RemoveFillColors`), Default `ViewBox="0 0 500 500"`; genutzt in `Pages/Authentication/Login.razor`, `Register.razor`, `Pages/Identity/Forgot.razor`, `Reset.razor`, `Pages/Content/Home.razor` (Theme-`Logo`-Override), `Shared/Components/AppBarHeader.razor` (Theme-`Icon`-Override), `Shared/Dialogs/About.razor` |
| Verwendung Server (Laufzeit-Rendering) | `C:\dev\privat\github\CleanArchitectureBaseBlazor\src\Server\Controllers\ResourceController.cs` + `SvgHelper.cs` | `GET /favicon.ico` (SVG→PNG via Svg.Skia/SkiaSharp), `GET /Logo.svg`, `GET /Logo.png?height&width`; Swagger-UI-Logo über `wwwroot/swagger-ui/styles.css` → `/Logo.svg` |
| Theme-Overrides | `C:\dev\privat\github\CleanArchitectureBaseBlazor\src\Client\Theming\ClientTheme.cs` | Properties `Icon` und `Logo` (string SVG) erlauben Theme-spezifische Logos (in `ClientThemes.cs` nicht gesetzt) |
| Weitere SVGs (kein Logo) | `src/Client/Shared/NotFoundComponent.razor` (404-Illustration, `viewBox="0 0 569 384.7"`), `src/Client/wwwroot/css/open-iconic/font/fonts/open-iconic.svg` (Icon-Font) | – |
| Sonstige Bilder | `src/Client/wwwroot/media/background-1.jpg`, `_background-1.jpg` (Login-Hintergrund) | – |
| Fremdprojekt | `parserMain/WayesPDFParser.UI/Icon.png`, `Logo.png` (Wayes-Branding, nicht Coworkee) | – |

Zum Extrahieren des Original-Logos als Datei: `git show master:src/Shared/Constants/CustomIcons.cs` und den String zwischen `$"` und `";` nehmen (`{{`/`}}` → `{`/`}`, `\"` → `"`).

---

## 13. Anhang

### 13.1 parserMain (Fremdprojekt, nur kurz)

`parserMain/WayesPDFParser` (net9.0-Klassenbibliothek) und `parserMain/WayesPDFParser.UI` (WPF, MahApps/MaterialDesign, `win-x64` Single-File) sind ein **eigenständiges Kunden-Tool "Wayes PDF Parser"**: Import von Lohn-/Sozialversicherungsdaten aus PDFs (Stirling-PDF-API `IStirlingApi`, OpenAI-Vision `OpenAIVisionClientService`/`Azure.AI.OpenAI`, `PdfImporter` mit `PdfImportMode.Html`) und Excel (`SocialDataExcelImporter`, EPPlus/OpenXml) in `Types/SocialData` (Person/Employment/Insurance/Payroll/Banking – Spiegelbild der Domain-Entities) und Export in ein Excel-Template (`ExcelExporter`, `ExcelWriteOptions.Wayes`, Template im eingebetteten `Resources/Output.zip`). Wird von `Coworkee.Application` direkt referenziert (SocialData Import/Export) – gehört nicht ins Template.

### 13.2 Secrets/Passwörter im Repo (zu bereinigen)

`src/Server/appsettings.json` (JWT-Secret, Hash-Salt, SendGrid-Key, Azure-Translator-Key, SMTP-Credentials), `src/Shared/resourceTranslationOptions.json` (Azure-Translator-Key), `src/Shared/Constants/Application/ApplicationConstants.cs` (Default-User-Passwörter, Keycloak-Client-Secret), `docker-compose.yml` (SA-Passwort), `.azure/COWORKEE/config.json` (Admin-Passwort "admin"), `.azure/COWORKEE/.env`, `src/Aspire/Coworkee.AppHost/.azure/`.

### 13.3 Kennzahlen

- Commits: 360, letzter 2025-12-06 (Florian Gilde). `master`↔`wayes-edith`: 196 Dateien, +12 642/−3 457 Zeilen.
- Controller: 22 (davon 1 auskommentiert); Client-Pages: 30 Routen-Seiten + 6 Dialog-Pages; Shared-Komponenten: ~35; TODO-Kommentare in `src/`: 47.
- Generierter Code: `ClientGenerated.cs` 11 283 Zeilen, `web-api-client.ts` 12 877 Zeilen, `appsettings.g.cs` 145 Zeilen, `en-US.yml` 1 139 Keys.
