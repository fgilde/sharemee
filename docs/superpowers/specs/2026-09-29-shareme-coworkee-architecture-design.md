# Shareme & Coworkee – Architektur-Design (Master-Spec)

Stand: 2026-09-29 · Status: **Freigegeben 2026-10-01 (alle Empfehlungen aus Kap. 8 angenommen, Lizenz Coworkee: MIT)** · Autor: Claude (mit Analyse-Grundlage in `docs/analysis/`)

Dieses Dokument ist die Master-Spec. Es legt Zielbild, Architektur, Technologie-Entscheidungen, die Coworkee-Lib-Familie, das Shareme-Domänenmodell und die Zerlegung in Teilprojekte fest. Jedes Teilprojekt bekommt danach eine eigene Detail-Spec und einen Implementierungsplan.

---

## 0. Verständnis der Aufgabe (Brief)

**Was gesagt wurde**

- Neues Produkt **Shareme**: Asset Management + Dokumentenmanagement + Knowledge Base + Intranet.
- Muss von Anfang an alles können, was **Sharedien** kann (DAM, Angular 9 + .NET, Stand 2021). Inventar: `docs/analysis/`.
- Versionierung und Audit/Change-Log für **alles**. User/Rollen/Rechte detailliert, inkl. Zuweisen/Entziehen auf **einzelnen Assets/Dateien**.
- Datei-Anzeige mit `MudExFileDisplay` (Blazor) bzw. `<mudex-file-display>` Webcomponent.
- Modern; Themes administrativ konfigurierbar; Setup-Wizard für nicht eingerichtete Systeme.
- Saubere API mit Swagger für alles; direkte **MCP**-Schnittstelle; administrative Config für interne AI, die per MCP das Tool bedient.
- Bei getrennten Systemen (Auth, API, Frontend, Redis …) von Anfang an **.NET Aspire**.
- **Realtime für alles** mit SignalR, generisch und dynamisch.
- Template `CleanArchitectureBaseBlazor` als Basis, aber **kein MediatR** mehr.
- Template zu **Coworkee.*-NuGet-Libs** umbauen (eigener Ordner), ABP-ähnlich (Module, Mail-Template-Verwaltung, Rechteverwaltung mit UI und Contributor). Zuerst lokaler NuGet-Feed, später echtes NuGet. Coworkee-Icon existiert.
- Clean Code, wenig Kommentare, CCD/KISS/YAGNI/SoC, Trennung Integration/Aggregation/Logik, Unit-, Integrations- und UI-Tests.

**Annahmen (bitte korrigieren, wenn falsch)**

| # | Annahme | Warum |
|---|---|---|
| A1 | Coworkee wird ein eigenes Repo `C:\dev\privat\github\coworkee` (Sibling zu `shareme`), nicht Unterordner von shareme. | Libs sollen unabhängig versioniert und publiziert werden. |
| A2 | Shareme ist mandantenfähig (wie Sharedien), auch wenn die erste Installation nur einen Mandanten hat. | Sharedien-Parität; Tenant-Spalte nachträglich einzuführen ist teuer. |
| A3 | Ein relationales DB-System für alle Dienste (mehrere Schemata), Dienste sind Deployment-Einheiten, keine Datengrenzen. | Sharedien machte es genauso; ein Team, ein Produkt. Echte Microservice-Datentrennung wäre YAGNI. |
| A4 | PostgreSQL ist Default-DB; SQL Server bleibt als zweiter Provider unterstützt (provider-neutrale EF-Konfiguration), Migrationen dafür erst bei Bedarf. | JSONB, Volltext, Aspire-Container, kostenfrei. Template unterstützte beide. |
| A5 | Sharedien-Frontend-Verhalten wird funktional nachgebaut, nicht pixelgenau. UI-Framework ist MudBlazor + MudBlazor.Extensions. | „modern aussehen" schlägt Kopie. |
| A6 | „Knowledge Base" = versionierte Seiten (Wiki) im selben Knotenbaum; „Intranet" = Startseite mit Widgets, News, Personen-Verzeichnis, Favoriten. Beides erweiterbar. | Nicht in Sharedien vorhanden; minimaler sinnvoller Umfang, nutzt das Graph-Modell. |
| A7 | Sharedien-Datenmigration (Importer aus Sharedien-DB + Storage) ist gewünscht, aber letzte Phase. | Bestandsdaten werden vermutlich übernommen; blockiert nichts vorher. |
| A8 | Lizenz für Coworkee: wie Nextended/MudEx (MIT oder GPL – Package.props im Prototyp sagt GPL-3.0). Muss entschieden werden. | Publizierung. |

---

## 1. Zielbild in einem Absatz

Shareme ist eine modulare .NET-10-Plattform: ein typisierter **Knotengraph** (Ordner, Assets, Seiten, Workspaces, Tasks, beliebige Custom-Typen) mit **dynamischen Metadaten**, **Versionen** und **Derivaten**, **knotenbezogenen Rechten** mit Vererbung, **lückenlosem Audit**, **Volltextsuche mit Facetten**, **Kollaboration** (Workspaces, Kanban, Tasks, Kommentare, Freigaben), **Automatisierung** (Regeln, Massenbearbeitung, KI-Extraktion), **DRM** und einer **KI-Assistenz per MCP**. Alles läuft als Aspire-Anwendung aus getrennten Diensten (Auth, API, Web, Processing) auf einer gemeinsamen Datenbank. Die generischen Teile (Module, Identity, Rechte, Settings, Audit, Mail, Realtime, Storage, Suche, Jobs, Admin-UI, Theming, Setup-Wizard, MCP/AI) bilden das wiederverwendbare Framework **Coworkee**.

---

## 2. Architektur-Ansätze (Abwägung)

### 2.1 Grundschnitt

| Ansatz | Beschreibung | Bewertung |
|---|---|---|
| **A – Modularer Monolith auf Aspire (empfohlen)** | Ein Domänenmodell, eine DB, mehrere Prozesse (Auth, API, Web, Processing, Migrations) als Aspire-Ressourcen. Module = Coworkee-Module in Prozess. | Erfüllt „getrennte Systeme + Aspire", minimale Verteilungskomplexität, gemeinsame Libs, einfache Tests. Skaliert horizontal über Redis-Backplane und Hangfire-Queues. |
| B – Microservices je Bounded Context (wie Sharedien 2021: 7 Dienste + Broker) | API, Identity, Storage, Processing, Search, EventHub, Web mit eigenen DBs/Contracts. | Sharedien litt darunter (Doku: Contracts-Drift, Broker-Konfiguration, 260 Settings). Für ein Team zu teuer. Nicht empfohlen. |
| C – Reiner Monolith (ein Prozess) | Alles in einem ASP.NET-Prozess inkl. Blazor Server. | Widerspricht der Vorgabe getrennter Systeme; Processing (ImageMagick/FFmpeg) gehört nicht in den Web-Prozess. |

**Entscheidung: A.**

### 2.2 Frontend-Modell

| Ansatz | Bewertung |
|---|---|
| **Blazor Web App, Render-Modus InteractiveWebAssembly mit Prerendering, Web-Host als BFF (Cookie-Login gegen Auth-Server, Token-Weiterleitung an API) – empfohlen** | Microsoft-Standardmuster (`BlazorWebAppOidcBff`). Schneller Erststart, sichere Token-Haltung (kein Token im Browser), MudEx voll nutzbar, API bleibt reines Bearer-API für SDK/MCP/Integrationen. |
| Standalone WASM (statisch) + OIDC im Browser | Token im Browser, keine Prerender, komplexere Auth. |
| Blazor Server | Kein Offline/Skalierungsvorteil, Server-Session pro Client; MudExFileDisplay-Streams laufen über Circuit. |

**Entscheidung: Blazor Web App + BFF.** `<mudex-file-display>` Webcomponent bleibt als Option für Einbettungen außerhalb Blazor (z. B. Shares-Seite ohne Login), Standard ist `MudExFileDisplay`.

### 2.3 Use-Case-Verarbeitung ohne MediatR

| Ansatz | Bewertung |
|---|---|
| **Eigener minimaler Dispatcher (Commands/Queries/Handler + Middleware-Pipeline) – empfohlen** | ~150 Zeilen, keine Fremdabhängigkeit, ein Pipeline-Pfad für REST, MCP, SignalR und Jobs (Validation, Authorization, UnitOfWork, Audit-Korrelation, Logging). Handler sind isoliert testbar. |
| Application Services (ABP-Stil) | Weniger Klassen, aber Cross-Cutting nur per Interceptor/Decorator; MCP-Tools und Jobs müssten Services direkt kennen. |
| Wolverine / andere Bibliothek | Mächtig, aber neue große Abhängigkeit, Konventionsmagie. YAGNI. |

**Entscheidung: eigener Dispatcher** (`ICommand<TResult>`, `IQuery<TResult>`, `IHandler<TRequest,TResult>`, `IRequestMiddleware`). Domain-Events aus Aggregaten werden nach Commit über die Outbox verteilt.

### 2.4 Metadaten-Speicherung

| Ansatz | Bewertung |
|---|---|
| EAV (Sharedien: Property/PropertyValue, 190 000 Zeilen für 24 000 Knoten) | Bewährt, aber schwer zu lesen, viele Joins, Massenänderung teuer. |
| **JSON-Spalte je Metadaten-Träger (EF Core 10 `ToJson`, Postgres jsonb / SQL Server json) – empfohlen** | Ein Read für alle Werte, typisiertes Wertemodell im Code, Audit-Diff auf Feldebene im Domain-Code, Filter über JSON-Pfade, Suche ohnehin in Elasticsearch. Provider-neutral über EF-JSON-Mapping. |

**Entscheidung: JSON-Spalte**. Definitionen (PropertyDefinition, Gruppen, Listenwerte, Sets) bleiben relational.

### 2.5 Suche

| Ansatz | Bewertung |
|---|---|
| DB-Volltext (Postgres tsvector/trigram) | Kein weiterer Dienst, aber Facetten, Fuzzy, Did-you-mean, Dokumentinhalte und Rechtefilter sind mühsam. |
| **Elasticsearch (Aspire-Integration, `Elastic.Clients.Elasticsearch`) hinter `ISearchIndex` – empfohlen** | Sharedien-Parität (13 Operatoren, Facetten, Cursor-Paging, Suggest, Dokument-Volltext, Rechtefilter über Principal-IDs im Index). Abstraktion erlaubt später DB-Provider für Kleininstallationen. |

**Entscheidung: Elasticsearch.**

### 2.6 Events, Jobs, Realtime

- **Transactional Outbox** in der DB (ein `OutboxMessage` je Domain-/Integrationsevent, geschrieben im selben `SaveChanges`). Dispatcher nach Commit → in-process Handler, SignalR-Publish, Job-Enqueue, Webhooks.
- **Hangfire** (Postgres-Storage) für Hintergrundjobs; Queues `default`, `processing`, `search`, `permissions`, `mail`. Worker: `Shareme.Processing` (schwer) und API (leicht).
- **SignalR** mit **Redis-Backplane** für Multi-Instanz. Kein Message-Broker in Phase 1; `IEventBus`-Abstraktion mit Redis-Pub/Sub für Dienst-übergreifende Events, Broker-Adapter später möglich.
- Integrations-Eventstream (Sharedien: AMQP/Service Bus) wird zunächst über **Webhooks** + SignalR abgebildet; Broker-Adapter ist Erweiterung.

### 2.7 Authentifizierung

| Ansatz | Bewertung |
|---|---|
| **OpenIddict 7 als eigener Auth-Dienst `Shareme.Auth` mit ASP.NET Core Identity (User-Store in der gemeinsamen DB) – empfohlen** | Volle Kontrolle über User/Rollen/Mandanten, externe Provider als ASP.NET-Handler (Entra ID/OIDC, Google, SAML via Sustainsys, LDAP), Registrierung/2FA/Passkeys, Client-Credentials für Integrationen, Device/MCP-Flows. Template hatte JWT+Refresh selbstgebaut plus optional Keycloak. |
| Keycloak-Container | Schnell, aber Java, User-Modell außerhalb unserer DB, Mandanten/Gruppen-Mapping doppelt. |
| Selbstgebautes JWT (Template) | Kein Standard-OIDC, keine externen Clients sauber. |

**Entscheidung: OpenIddict.** Alle Clients (Web-BFF, SDK, MCP-Clients, Processing) nutzen OIDC/OAuth2.

---

## 3. Systemtopologie (Aspire)

```
Shareme.AppHost
├── postgres        (Aspire.Hosting.PostgreSQL)      DB "shareme" – Schemas: cw (Coworkee), sm (Shareme), hangfire
├── redis           (Aspire.Hosting.Redis)           Cache, SignalR-Backplane, Pub/Sub, Locks
├── elasticsearch   (Aspire.Hosting.Elasticsearch)   Suchindex
├── minio           (Container, S3-kompatibel)       Blob-Storage in Dev; Prod: FileSystem/Azure/S3
├── mailpit         (Container)                      Mail in Dev
├── ollama          (optional, Aspire Community)     lokales LLM für Assistent
├── shareme-migrations   Projekt, WaitForCompletion  EF-Migrationen + Seeding
├── shareme-auth         Projekt  OpenIddict + Identity-UI (Login, Registrierung, 2FA, Consent)
├── shareme-api          Projekt  REST (OpenAPI/Swagger) + SignalR + MCP + Hangfire (light) + Webhooks
├── shareme-processing   Projekt  Hangfire-Worker: Derivate, Metadaten-Extraktion, Archive, Import, Rules, Reindex
└── shareme-web          Projekt  Blazor Web App (BFF) + WASM-Client
```

Alle Projekte referenzieren `Shareme.ServiceDefaults` (OpenTelemetry, Health, Resilience, Service Discovery). Processing-Tools (ImageMagick, FFmpeg, Ghostscript, LibreOffice, ExifTool, Blender) kommen als Container-Image `shareme-processing` mit vorinstallierten Binaries; lokal alternativ Pfade in Settings.

Request-Fluss: Browser → `shareme-web` (Cookie) → BFF-Proxy (YARP) → `shareme-api` (Bearer) → DB/Redis/ES/Storage. SignalR: Browser ↔ `shareme-api` direkt (Token via BFF-Endpoint kurzlebig) oder über den BFF-Proxy (WebSockets-Forwarding). Entscheidung: **über den BFF proxied** (ein Origin, keine Token im Browser).

---

## 4. Coworkee – die Lib-Familie

### 4.1 Grundsätze

1. **Module** sind die Einheit der Erweiterung. Jede Lib und jede App-Funktion ist ein `CoworkeeModule` mit `[DependsOn]`, `ConfigureServices`, `Configure` (Pipeline), `SeedAsync`. Apps registrieren genau ein Root-Modul.
2. **Contributors** statt Konfigurations-Magie: Module tragen Permissions, Settings, Mail-Templates, Menüeinträge, EF-Modell, MCP-Tools, Realtime-Events, Seed-Daten bei – jeweils über ein kleines Interface, das der Kern einsammelt.
3. **Contracts getrennt**: DTOs, Request-Modelle, Konstanten und Event-Verträge liegen in `*.Contracts` ohne Server-Abhängigkeiten, damit der WASM-Client keine Application/EF-Assemblies zieht (Fehler des Templates).
4. **Ein DbContext pro App**, zusammengesetzt aus Modul-Beiträgen (`IModelContributor`). Migrationen gehören der App, nicht der Lib.
5. **Keine Fremd-Bibliothek für Cross-Cutting**: Dispatcher, Outbox, Audit, Permissions, Settings sind eigene, kleine Implementierungen. Fremdpakete nur für echte Infrastruktur (EF, OpenIddict, Hangfire, MudBlazor, MudEx, Nextended, Elastic, MailKit, FluentValidation, ModelContextProtocol, Microsoft.Extensions.AI).
6. **Nullable enabled, ImplicitUsings, .NET 10, C# 14**, `TreatWarningsAsErrors` in Libs.

### 4.2 Pakete

| Paket | Inhalt | Abhängig von | Phase |
|---|---|---|---|
| `Coworkee.Core` | Modulsystem (`CoworkeeModule`, `DependsOnAttribute`, Modul-Loader mit topologischer Sortierung), Contributor-Interfaces, `Result`/`Error`, `PagedResult`, `LocalizedString`, `ICurrentUser`, `ITenantContext`, `IClock`, Ausnahmen, Guard-Helper | Nextended.Core | 0 |
| `Coworkee.Domain` | `Entity<TId>`, `AggregateRoot<TId>` (Domain-Events), `AuditableEntity`, `ISoftDelete`, `IMultiTenant`, `IHasRowVersion`, `IHierarchical` (ParentId + Path), `ValueObject`, `IRepository<T>`, `IUnitOfWork`, Specifications, Domain-Event-Basis | Core | 0 |
| `Coworkee.Contracts` | DTO-Basis, `QueryOptions` (Filter/Sort/Page/Search), Permission-Namen der Kernmodule, Settings-Schlüssel, Realtime-Event-Verträge (`EntityChanged`, `Notification`, `JobProgress`), API-Client-Interfaces der Kernmodule, MCP-Tool-Verträge | Core | 0 |
| `Coworkee.Application` | Dispatcher + Middlewares (Validation via FluentValidation, Authorization, UnitOfWork, Logging, Correlation), Feature-Handler der Kernmodule (Identity, Rollen, Permissions, Settings, Mail-Templates, Audit-Abfragen, Tenants, Themes, Localization, Notifications), Abstraktionen: `IPermissionChecker`, `IPermissionStore`, `ISettingProvider`, `IMailTemplateRenderer`, `IMailSender`, `IRealtimePublisher`, `IBlobStorage`, `IBackgroundJobs`, `ISearchIndex`, `IOutbox`, `IEventBus`, `IAuditStore` | Domain, Contracts | 0 |
| `Coworkee.Infrastructure` | `CoworkeeDbContext`-Basis + `IModelContributor`, Interceptors (Audit, SoftDelete, Tenant-Filter, Outbox, RowVersion), ASP.NET Core Identity Entities (User, Role, Group, Claims), Stores für Permissions/Settings/MailTemplates/Audit/Themes/Localization, Outbox-Dispatcher, Hangfire-Adapter, Redis-Adapter (Cache, EventBus, Locks), Storage-Provider FileSystem, Mail via MailKit (SMTP) + Sendgrid, Data Protection für Secrets | Application, EF Core 10, Npgsql, SqlServer, Hangfire, StackExchange.Redis, MailKit | 0 |
| `Coworkee.AspNetCore` | `AddCoworkee<TRootModule>()`, Controller-Basis, ProblemDetails-Mapping, API-Versionierung, OpenAPI (built-in) + Swagger UI, Permission-Policy-Provider (`[Authorize(Permission = …)]`, `[ResourcePermission]`), Bearer-Validierung gegen OpenIddict, SignalR `RealtimeHub` + Redis-Backplane + Gruppen-Abo-Protokoll, Webhook-Dispatcher, Health-Checks, Setup-Wizard-Gate-Middleware, Hangfire-Dashboard mit Permission | Infrastructure | 0 |
| `Coworkee.Client.Blazor` | Shell/Layout (MudBlazor 9 + MudEx 9), Theming (`ThemeDefinition` laden/anwenden, `MudExThemeEdit` im Admin), Auth-State über BFF, typisierte API-Clients (aus `Coworkee.Sdk`), Realtime-Client (`IRealtimeClient` mit `Subscribe<TEntity>`), `AuthorizeView`-Erweiterungen (`Permission`, `Resource`), generische Admin-Seiten (User, Gruppen, Rollen, Permission-Matrix, Settings, Mail-Templates, Audit-Log, Tenants, Themes, Jobs), Setup-Wizard, Notification-Center, generische Tabellen/Formulare (`EditableDataTable`, `MudExObjectEdit`), File-Display-Wrapper, Fehler-/Snackbar-Handling | Contracts, Sdk, MudBlazor, MudBlazor.Extensions | 0 |
| `Coworkee.Sdk` | NSwag-generierte typisierte Clients (C#) für die Coworkee-API-Oberfläche; Basis für App-SDKs | Contracts | 0 |
| `Coworkee.Testing` | Test-Basisklassen, Testcontainers-Fixtures (Postgres, Redis, Elasticsearch, MinIO), Respawn, `FakeClock`, `FakeCurrentUser`, `InMemoryRealtimePublisher`, bUnit-Helfer, Playwright-Basis für Aspire-Hosting-Tests | Infrastructure | 0 |
| `Coworkee.Aspire` | AppHost-Erweiterungen: `AddCoworkeeInfrastructure()` (Postgres/Redis/ES/MinIO/Mailpit mit Standardnamen), Endpoint-Env-Helfer | Aspire.Hosting | 0 |
| `Coworkee.Search.Elasticsearch` | `ISearchIndex`-Provider: Mapping-Registry je Dokumenttyp, Bulk-Indexer, Query-Builder (Filter-Operatoren, Facetten, Sort, Cursor), Suggest, Rechtefilter, Alias-Reindex | Application, Elastic.Clients.Elasticsearch | 2 |
| `Coworkee.Storage.Azure`, `Coworkee.Storage.S3` | `IBlobStorage`-Provider inkl. signierter Upload-/Download-URLs | Application | 1 |
| `Coworkee.Ai` | MCP-Server-Hosting (`ModelContextProtocol.AspNetCore`), `IMcpToolContributor`, Tool-Ausführung über den Dispatcher im Kontext des aufrufenden Users, `IChatClient`-Factory aus Settings (OpenAI, Azure OpenAI, Anthropic, Ollama) via `Microsoft.Extensions.AI`, Assistent-Runner (Chat + MCP-Tools), Secret-Verschlüsselung | AspNetCore | 8 |

Versionierung: `Version.props` (SemVer, `0.x` bis Shareme P1 stabil), `GeneratePackageOnBuild` in Release, Output nach `coworkee/artifacts/nuget`. Shareme `nuget.config` mit lokalem Feed; `Directory.Build.props`-Schalter `CoworkeeReferenceMode=Package|Project` für schnelle Iteration (Project referenziert die Sibling-Projekte direkt).

### 4.3 Kern-Mechanismen (Design)

**Modulsystem**
```csharp
[DependsOn(typeof(CoworkeeIdentityModule), typeof(CoworkeePermissionsModule))]
public sealed class ShareMeAssetsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleContext ctx) { ctx.Services.AddScoped<IAssetStorage, AssetStorage>(); }
    public override Task SeedAsync(SeedContext ctx) => ctx.Seed<AssetTypeSeeder>();
}
```
Loader: alle `[DependsOn]` transitiv auflösen, topologisch sortieren, `ConfigureServices` in Reihenfolge, Contributors per DI registrieren (`services.AddContributor<IPermissionDefinitionContributor, AssetPermissions>()`).

**Contributors** (alle gleich gebaut: Interface + Kontext-Objekt)

| Contributor | Trägt bei | Konsument |
|---|---|---|
| `IPermissionDefinitionContributor` | Gruppen, Permissions (Name, Anzeige, Parent/impliziert, `AssignableScopes`: Global, Tenant, Resource-Typ) | Permission-Registry, Admin-Matrix, Policy-Provider, MCP-Tool-Beschreibung |
| `ISettingDefinitionContributor` | Settings (Key, Typ, Default, Scope Global/Tenant/User, Verschlüsselt, UI-Metadaten) | Settings-Store, Admin-Settings-UI, Setup-Wizard |
| `IMailTemplateContributor` | Template-Definitionen (Name, Default-Inhalt als Razor/Scriban je Kultur, Modell-Typ) | Mail-Template-Store (DB-Override je Tenant/Kultur), Admin-Editor, Renderer |
| `IMenuContributor` | Navigationseinträge mit Permission-Bedingung | Blazor-Shell |
| `IModelContributor` | `OnModelCreating`-Beiträge, Schema-Name | App-DbContext |
| `IDataSeedContributor` | Seed-Schritte (idempotent) | Migrations-Projekt, Setup-Wizard |
| `IRealtimeEventContributor` | Registrierung publizierbarer Ereignistypen + Gruppenregeln | RealtimeHub |
| `IMcpToolContributor` | MCP-Tools/Resources/Prompts | MCP-Server |
| `ISearchDocumentContributor` | Indexierbare Typen + Mapping + Projektion | Search-Indexer |

**Dispatcher**
```csharp
public interface ICommand<TResult> : IRequest<TResult>;  public interface IQuery<TResult> : IRequest<TResult>;
public interface IHandler<in TRequest, TResult> where TRequest : IRequest<TResult> { Task<TResult> HandleAsync(TRequest request, CancellationToken ct); }
public interface IRequestMiddleware { Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestDelegate<TResult> next, CancellationToken ct); }
public interface IDispatcher { Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken ct = default); }
```
Middlewares in fester Reihenfolge: Correlation → Logging → Authorization (`[RequiresPermission("…")]` am Request oder `IResourceRequest` für Ressourcen-Checks) → Validation → UnitOfWork (nur Commands; Commit + Outbox-Flush) → Handler. Handler-Registrierung per Assembly-Scan im Modul (`ctx.AddHandlersFromAssembly()`).

**Audit & Versionierung**
- `AuditInterceptor` (EF `SaveChangesInterceptor`) schreibt je geänderter Entität einen `AuditEntry { EntityType, EntityId, Action, TenantId, ActorId, CorrelationId, OccurredAt, Changes: [{Property, Old, New}] }`. Für JSON-Metadaten liefert die Entität ihren Diff selbst (`IAuditDiffProvider`).
- `IVersioned` Aggregate (Seiten, Property-Sets, Themes, Mail-Templates, Settings-Änderungen) erhalten Snapshots in `EntitySnapshot { EntityType, EntityId, Version, Payload(json), CreatedBy }` → „Version wiederherstellen" generisch.
- Datei-Versionen sind fachlich (AssetVersion), keine Snapshots.
- `RowVersion`/`xmin` für Optimistic Concurrency an jedem Aggregat.
- Audit-UI generisch: Timeline je Entität, Diff-Ansicht, Filter, Export.

**Rechte** (siehe 5.4 für Shareme-Spezifika)
- `PermissionDefinition` (aus Contributors) → `PermissionGrant { Name, ProviderName: Role|User|Group, ProviderKey, TenantId, IsGranted }` (global/tenantweit).
- `Role { Name, TenantId?, IsSystem, AssignableTo: ResourceTypes[] }` mit Permissions.
- `ResourcePermission { ResourceType, ResourceId, PrincipalType: User|Group, PrincipalId, RoleId, TenantId }` → **`EffectiveResourcePermission`** materialisiert für hierarchische Ressourcen (`IHierarchical.Path`), Job-getrieben (bei Create/Move/Delete/Grant/Revoke/InterruptInheritance), Lookup per Index.
- `IPermissionChecker.IsGrantedAsync(user, permission)` und `IsGrantedAsync(user, permission, resourceType, resourceId)`; Ergebnisse je Request gecacht; Claims im Token enthalten nur Rollen, Permissions werden serverseitig aufgelöst (Token bleibt klein).
- UI: Permission-Matrix (Rolle × Permission, Gruppen aufklappbar), Direktrechte für User/Gruppe, **`ResourcePermissionsPanel`** (wiederverwendbar an jedem Knoten: Einträge, Vererbung an/aus, geerbte vs. direkte, Zuweisen/Entziehen), „Effektive Rechte eines Users an Ressource X" als Diagnose.

**Settings**: Definitionen aus Contributors, Werte in `SettingValue { Name, Scope, ScopeKey, Value }`, `ISettingProvider.GetAsync<T>(key)` mit Auflösung User → Tenant → Global → Default, verschlüsselte Werte via Data Protection, Änderung = Audit-Eintrag, Realtime-Event `SettingsChanged` invalidiert Caches.

**Mail**: `MailTemplate` (Definition aus Contributor) + `MailTemplateOverride { Name, TenantId, Culture, Subject, Body, Version }` (versioniert, Snapshot), Renderer Scriban (leichter als Razor im Worker), Admin-Editor mit Vorschau und Testversand, `OutgoingMail`-Queue über Hangfire mit Retry und Whitelist für Testsysteme.

**Realtime**: `IRealtimePublisher.PublishAsync(RealtimeEvent)` schreibt in die Outbox; nach Commit → Redis → `RealtimeHub`. Gruppen: `tenant:{id}`, `user:{id}`, `type:{entityType}`, `entity:{type}:{id}`, `job:{id}`. Regel: **breite Gruppen erhalten nur Kennungen** (`EntityChanged {Type, Id, Action, ChangedProperties[]}`), der Client lädt über die API nach (Rechte werden dort geprüft); **enge Gruppen** (`entity:…`, `user:…`) dürfen Payloads tragen, Abo wird beim Join per Permission geprüft. Client: `IRealtimeClient.Subscribe<TDto>(id, onChanged)`, Komponenten-Helfer `UpdateOnChange`. Jede Entität ist damit ohne Zusatzcode realtime-fähig.

**Storage**: `IBlobStorage { Put, Get, Delete, Exists, GetSignedUploadUrl?, GetSignedDownloadUrl? }` + `StorageAccount`-Konfiguration je Tenant/Zweck. Upload-Protokoll in `Coworkee.Application`: `UploadSession` (Chunks, Hash, Größe, Ziel), Provider mit signierten URLs (S3/Azure) laden direkt, FileSystem über die API. SHA-256 als Hash, Dedupe je Tenant.

**Setup-Wizard**: `SystemState.IsInitialized`; Middleware leitet alles nach `/setup`, solange false. Schritte: Willkommen → DB-Verbindung prüfen → Admin-User + Tenant → Storage → Mail → Branding/Theme → Optionen (Demo-Daten, Sprachen) → Zusammenfassung. Schreibt Settings, seedet, markiert initialisiert.

**Theming**: `ThemeDefinition { Name, TenantId?, Palette light/dark, Typography, Layout, LogoSvg, Favicon, CustomCss, IsDefault }` (versioniert). Admin-Editor mit `MudExThemeEdit` + Live-Vorschau. Client lädt Default-Theme des Tenants, User-Override (hell/dunkel/Theme-Wahl) in User-Settings.

**MCP/AI**: MCP-Endpoint `/mcp` (Streamable HTTP) im API-Dienst, Auth per Bearer (OAuth Protected Resource Metadata), Tools aus Contributors; jeder Tool-Aufruf geht durch den Dispatcher → Rechte des aufrufenden Users gelten. Interner Assistent: Settings `Ai:Provider`, `Ai:Model`, `Ai:ApiKey` (verschlüsselt), `Ai:Endpoint`; `IChatClient` + `McpClient` gegen den eigenen Server im Kontext des Chat-Users; Chat-UI in Blazor mit Tool-Call-Anzeige; Audit jedes Tool-Aufrufs.

---

## 5. Shareme – Domänenmodell

### 5.1 Graph

```
Node (abstract)  Id, TenantId, NodeTypeId, Name(LocalizedString), Slug, ParentId?, Path(ltree/varchar), Depth, SortIndex,
                 Status, Metadata(json PropertyValueSet), InterruptInheritance, Owner, Auditable, SoftDelete, RowVersion
 ├─ Folder       (Sharedien: Category)  IsRoot, Icon, Color, Inheritable metadata rules
 ├─ Asset        (Sharedien: AssetSet)  AssetTypeId, CurrentVersionId, Versions[], Thumbnail, Preview, DuplicateOf?
 ├─ Page         (neu, Knowledge Base)  CurrentVersionId, PageVersions[] (Content markdown/html, Metadata snapshot)
 ├─ Workspace    Type (Upload/Collection/Collaboration), IsTemplate, SourceTemplateId, PropertySetId, FinalizationFolderId, CopyMetadataToAssets, Avatar
 ├─ State        (Kanban-Spalte)  SortIndex, IsDefault, Transitions[] (→ State)
 ├─ Task         Assignee, DueDate, TaskTypeId, Status, Watchers
 ├─ News         (neu, Intranet)  PublishAt, ExpiresAt, Pinned, Audience
 ├─ License / RightsOwner / BillingContact / Purchase (DRM)
 └─ CustomNode   NodeType = admin-definiert (Product, Campaign, Person, …)

Edge  Id, TenantId, EdgeTypeId, FromNodeId, ToNodeId, Metadata(json), Auditable
EdgeType  Key, Semantic (Link, Variant, Translation, Crop, Duplicate, Transition, LicenseFor, Custom), IsDirected, AllowedPairs[(FromType,ToType)], UsedInSearch
NodeType  Key, Semantic (Folder, Asset, Page, Workspace, State, Task, News, Drm*, Custom), IsSystem, Icon, PropertyDefinitions[], DefaultPropertySet
```
Unterschied zu Sharedien: Hierarchie ist `ParentId + Path` (schnelle Teilbaum-Abfragen, Materialisierung), nicht Edge. Alle übrigen Beziehungen bleiben typisierte Edges. Mehrfachverortung (Sharedien „Link") ist Edge `Link` Folder→Asset.

### 5.2 Metadaten

- `PropertyDefinition { Key, Group, ValueType (String, Text, RichText, Date, DateTime, Time, Integer, Decimal, Boolean, Duration, Url, Reference(NodeType)), List (None/Single/Multi, AllowCustom), MultiValue, Localizable, HasContext, Editable, IsSystem, Searchable (None/Filter/Fulltext/Both), Facet, IgnoreOnNewVersion, UsableInFileNaming, SortKey, Validation (Regex, Min/Max, Required) }`
- `PropertyGroup`, `PropertyListValue` (lokalisiert, sortiert, Status), `PropertySet { NodeType, Name, IsGlobal, IsDefault, Items[{Definition, FillMode Optional/Recommended/Mandatory}], Conditions[{Definition, ListValue}] }`, `DataLanguage`, `UserContext` + `UserContextGroup`.
- `PropertyValueSet` (JSON): `{ "<definitionKey>": { "values": [ { "lang": "de", "ctx": "DE", "v": …, "listId": … } ] } }`; typisiertes Modell im Code, Validierung gegen Definition, Diff auf Feldebene für Audit.
- Vererbung Folder→Kinder pro Definition (`InheritToNodeTypes`), aufgelöst beim Lesen (Effective Metadata) und indiziert.
- System-Properties werden aus Entitätsfeldern gespiegelt (Dateiname, Größe, Hash, Version, Import-Datum, technische Metadaten aus Extraktion: EXIF/IPTC/XMP/Video/Audio/Dokument).
- Restriktionen je Rolle: Definition (View/Edit/Delete), Wert-basiert (Aktion nur bei Listenwert X), Derivat-Typ je Asset-Typ. Realisiert als Policy-Provider im Permission-Check (`IFieldRestriction`).

### 5.3 Assets, Versionen, Derivate, Verarbeitung

- `AssetType { Key, FileTypes[], DerivationSettings[{DerivationType, IsDefault, OnDemand, AllowManualUpload, ShowInDownloads, UseAsThumbnail, UseAsPreview}] }`, `FileType { Extension, MimeType, AssetType, ForbiddenForUpload, ExtractDocumentText, MagicNumbers }`.
- `AssetVersion { AssetId, Number, FileName, Size, Hash, MimeType, OriginalBlobKey, Status (Active/Deactivated), ProcessingStatus, MetadataSnapshot(json), CreatedBy, CreatedAt }`.
- `Derivation { VersionId, DerivationTypeId, BlobKey, Size, Hash, MimeType, Status (Pending/Processing/Ready/Failed), Error, IsManual }`.
- `DerivationType { Key, IsSystem (Original, Thumbnail, Preview, Print, AnimatedThumbnail), Localized name }` + `DerivationJobDefinition { DerivationType, InputFileTypes[], OutputFileType, Pipeline(json: Steps[{Tool: ImageMagick|FFmpeg|Ghostscript|LibreOffice|ExifTool|Blender|Custom, Args, Variables, Profiles}]) }` (Sharedien-Format wird übernommen, siehe `sharedien-db-insights.md` 3.3), `WatermarkDefinition` + Trigger.
- Upload: `POST /uploads` (Session: Ziel, Dateiname, Größe, Hash-Ankündigung) → Chunks direkt zum Storage (signierte URL) oder `PUT /uploads/{id}/chunks/{n}` → `POST /uploads/{id}/complete` (Duplikatprüfung, Version anlegen, Metadaten kopieren, Relationen, Default-Derivate enqueuen, Rules) – ein Protokoll für Asset, neue Version, manuelles Derivat, Workspace/Task-Ziel, Archiv (mit Extraktionsmodus).
- Verarbeitung im `Shareme.Processing`: Job `RenderDerivation(versionId, derivationTypeId)`, `ExtractMetadata(versionId)`, `ExtractDocumentText(versionId)`, `ExtractArchive(assetId, mode, targetFolder)`, `Crop(versionId, definition)`. Fortschritt per Realtime `job:{id}`.
- Download: Einzel (Derivat-Auswahl, Dateinamensregel `FileNamingDefinition`), Zip mehrerer Knoten (Job + Download-Link), Kontaktabzug-PDF (Job).
- Papierkorb: SoftDelete → `RecycleBin`-Ansicht → Wiederherstellen/Endgültig löschen (Job mit Retention).
- Duplikate: Hash-Index je Tenant; beim Complete Duplikat melden und Edge `Duplicate` setzen.

### 5.4 Rechte in Shareme (auf Coworkee aufgesetzt)

- Ressourcentypen mit `ResourcePermission`: `Node` (alle Knotentypen inkl. Ordner, Workspace, State, Custom), `SavedSearch`, `PropertySet`, `Job`, `FileNamingDefinition`, `DrmScope`.
- Rollen-Kategorien: global (Admin, Editor, Viewer …), knotenbezogen (Owner, Collaborator, Reader, Uploader – `AssignableTo: Node`), Workspace (`Owner/Collaborator/Reader`), Collection, License.
- Permission-Katalog nach Muster `<Modul>.<Objekt>.<View|Read|Create|Update|Delete|…>` (Sharedien: 331 Werte → wird in Detail-Spec P1 konsolidiert, Ziel < 200 durch Implikationen).
- Materialisierung über `Node.Path`; `InterruptInheritance` am Knoten; Suchindex trägt `AllowedPrincipals[]` je Dokument für serverseitigen Rechtefilter.
- Öffentliche Freigaben (`Share`) laufen an Rechten vorbei über Token mit Ablauf, eingeschränkt auf Derivat-Auswahl.

### 5.5 Kollaboration, Automatisierung, DRM, KB/Intranet

- **Workspaces**: Typen Upload/Collection/Collaboration, Templates (Rechte werden auf Instanzen übertragen), Zustände mit Transitions und Rechten je State, Tasks (Assignee, Watcher, Fälligkeit, Typ, Assets), Kommentare (auf jedem Knoten, mit Erwähnungen), Validieren/Finalisieren (PropertySet-Pflicht → Zielordner), Shares (extern, Token, Ablauf, Derivate), automatische Löschung mit Erinnerung.
- **Benachrichtigungen**: `Notification { UserId, Type, Payload, ReadAt }` aus ActivityLog + Abos (`NodeSubscription { NodeId, UserId, Inbox, Email }`), Digest-Mails, Realtime `user:{id}`.
- **Rules**: `Rule { NodeTypes[], Filters[], Triggers[] (Equals, NotEquals, Gt, Lt, Gte, Lte, Contains, HasRelationType, IsNewVersion, ValueChanged, IsNewFolder, Regex), Actions[] (SetProperty mit Template `{fnc.find(...)}`, RunExtraction, Notify) }`, live über Domain-Events + Job für Nachbearbeitung mit Report.
- **Massenoperationen**: Mass-Edit (Strategien Keep/Overwrite/Merge/Delete), Import (xlsx/csv mit Meldungen), Export (xlsx/csv), Bulk-Delete – alle als Jobs mit Report und Rollen-Limits.
- **Extraktion/KI-Tagging**: `ExtractionDefinition { Provider (Azure Vision, Imagga, Clarifai, OpenAI-Vision via Microsoft.Extensions.AI), InputDerivation, Mappings→PropertyDefinitions }`, Jobs mit Ergebnissen; technische Metadaten (ExifTool, ffprobe) immer.
- **DRM**: License, RightsOwner, BillingContact, PriceList (+ Kontext-Prozente), Buyout, LicenseOffer (berechnet), Purchase; Rechte über `DrmScope`.
- **Knowledge Base**: `Page` mit `PageVersion` (Markdown + eingebettete Assets, Rich-Text-Editor MudExRichTextEditor), Diff zwischen Versionen, Kommentare, Tags (Property), Verlinkung Page↔Asset/Node, Volltext im Index.
- **Intranet**: Startseite mit Widget-Layout je User/Tenant (Widgets: News, Zuletzt, Favoriten, Meine Tasks, Suche, Kalender-Fälligkeiten), `News`-Knoten, Personenverzeichnis (User-Profile mit Avatar, Abteilung, Kontakt), Favoriten.

### 5.6 UI-Parität (aus `sharedien-frontend.md`)

- Asset-Browser als **eine** generische, einbettbare Komponente (Sharedien: `AssetsComponent`, 3 000 Zeilen, 45 Flags) – in Shareme zerlegt in `NodeBrowser` (Datenquelle, Auswahl, Suchzustand in URL) + austauschbare Ansichten Galerie / Spreadsheet (inline editierbar) / Formular.
- Suche: Type-ahead, exakt, in Dokumenten, AND/OR, Datensprache, Facetten, 13 Filter-Operatoren, gespeicherte Suchen, PropertySet als Spaltenauswahl.
- Ordnerbaum mit Favoriten, Drag&Drop mit Link/Move-Wahl, Massenaktionen, Papierkorb.
- Asset-Detail-Tabs: Metadaten, geerbte Ordner-Metadaten, Versionen (mit Vergleich), Derivate (On-Demand, manueller Upload), Relationen (Graph-Ansicht), Lizenzen, Rechte, Aktivität, Kommentare. Vorschau über MudExFileDisplay inkl. Lightbox mit Zoom/Vergleich; 3D-Viewer ist Mehrwert gegenüber Sharedien.
- Upload-Wizard (Ziel, Template/Session, Properties, Relationen, Archiv-Extraktion), globale Upload-Queue mit Chunk-Retry; Ordner-Upload als Mehrwert.
- Download-Warenkorb mit mehreren Bundles, Derivat-Auswahl je Asset-Typ, Dateinamensmuster.
- Dashboard mit Job-Kacheln und Job-Reports; Inbox/Watcher; @-Mentions in Kommentaren.
- Realtime ersetzt überall das Polling von Sharedien (Job-Status, Derivate, Listen).
- UI-Sprachen en/de mit Tenant-Override; Datensprachen und User-Contexts getrennt.

### 5.7 API-Oberfläche (Kurz)

Versionierte REST-API `/api/v1/…` je Modul (Nodes, Folders, Assets, Versions, Derivations, Uploads, Metadata, PropertyDefinitions, PropertySets, Search, SavedSearches, Workspaces, States, Tasks, Comments, Shares, Public, Rules, Jobs, Extractions, Drm, Pages, News, Users, Groups, Roles, Permissions, ResourcePermissions, Settings, MailTemplates, Themes, Audit, Notifications, Tenants, Setup, Info), einheitliches `QueryOptions` für Listen, ProblemDetails, Idempotency-Key für Uploads/Commands, OpenAPI je Version mit Swagger UI, SDK-Generierung (NSwag C# + TypeScript). SignalR `/hubs/realtime`. MCP `/mcp`. Webhooks konfigurierbar je Tenant (Event-Filter, Secret, Retry).

---

## 6. Qualität, Tests, Betrieb

- **Tests**: xUnit v3 + Shouldly + NSubstitute. Unit (Domain, Handler mit Fakes), Integration (`WebApplicationFactory` + Testcontainers + Respawn, echte DB/Redis/ES), UI (bUnit für Komponenten, Playwright gegen `Aspire.Hosting.Testing` für E2E-Flows: Setup-Wizard, Login, Upload, Rechte), Architektur-Tests (NetArchTest: Layer-Abhängigkeiten, keine EF im Domain, keine Application im Client).
- **Code-Regeln**: `Directory.Build.props` mit Analyzern (Microsoft.CodeAnalysis.NetAnalyzers, Meziantou), `EnforceCodeStyleInBuild`, `.editorconfig`, keine XML-Kommentare außer public Lib-API, keine Regionen.
- **Observability**: OpenTelemetry (Traces/Metrics/Logs) via ServiceDefaults, Correlation-Id in Audit und Logs, Health-Endpoints, Hangfire-Dashboard hinter Permission.
- **Lehren aus `sharedien-services.md` (Kap. 14)**: signierte URLs per HMAC (nicht SHA-256 über Key+Pfad), TLS-Zertifikatsprüfung nie abschalten, Bearer lokal validieren (nicht nur Introspection), OpenIddict-Tokens/Grants persistent, SHA-256 statt MD5, 2FA/Passkeys als Mehrwert, Realtime auch für Jobs/Derivate statt Polling, ExifTool als dauerhafter Prozess (`-stay_open`), Suchindex-Rechte bei Rechte-/Ordneränderung inkrementell nachziehen.
- **Sicherheit**: Secrets nur via Data Protection/Aspire-Parameter, Rate-Limiting, Antiforgery im BFF, CSP, Upload-Blacklist + Magic-Number-Prüfung, signierte URLs kurzlebig, Audit auch für Logins/Admin-Aktionen.
- **CI**: GitHub Actions – Build, Tests (Testcontainers), Pack Coworkee → lokaler Feed-Artefakt/NuGet, Container-Images, `azd`-kompatibles Deployment (Aspire Manifest).

---

## 7. Zerlegung in Teilprojekte (jedes: Spec → Plan → Umsetzung)

| Phase | Teilprojekt | Ergebnis (Definition of Done) |
|---|---|---|
| **P0** | **Fundament** – Repos `coworkee` + `shareme`, Solutions, Aspire-Topologie, Coworkee-Pakete Core/Domain/Contracts/Application/Infrastructure/AspNetCore/Client.Blazor/Sdk/Testing/Aspire, Auth-Dienst (OpenIddict), API, Web (BFF+WASM), Migrations, Setup-Wizard, Identity/Gruppen/Rollen/Permissions (global + Resource-Panel), Settings, Mail-Templates, Audit, Themes, Realtime, Notifications, Jobs-Basis, Test-Infrastruktur, CI | Login über Auth-Dienst; Admin verwaltet User/Gruppen/Rollen/Permission-Matrix/Settings/Mail-Templates/Themes; Audit-Log sichtbar; Realtime-Demo (Änderung in Tab A erscheint in Tab B); Setup-Wizard auf leerer DB; Coworkee-Pakete im lokalen Feed, Shareme baut damit |
| **P1** | **Graph, Metadaten, Assets** – Node/Folder/Custom-Typen, PropertyDefinitions/Gruppen/Sets/Listen/Sprachen/Kontexte, Metadaten-Editor, Upload-Protokoll, Storage-Provider (FS, S3/MinIO, Azure), Versionen, Basis-Derivate (Thumbnail/Preview via ImageMagick im Processing), Datei-Anzeige (MudExFileDisplay), Download, Papierkorb, Duplikate, Node-Rechte mit Materialisierung, Activity-Log/Abos | Ordnerbaum, Assets hochladen/anzeigen/versionieren/herunterladen, Metadaten pflegen, Rechte je Ordner/Asset vergeben und entziehen, alles realtime und auditiert |
| **P2** | **Suche** – Elasticsearch-Provider, Indexierung aller Knoten, Filter-Operatoren, Facetten, Sortierung, Cursor-Paging, Quick Search/Suggest, Dokument-Volltext, Saved Searches (teilbar), Reindex-Jobs | Suchseite mit Facetten und Rechtefilter, gespeicherte Suchen |
| **P3** | **Processing komplett** – Video/Office/PDF/EPS/3D-Pipelines, Wasserzeichen, Crop-Editor, On-Demand/manuelle Derivate, technische Metadaten-Extraktion, Archiv-Extraktion, Dateinamensregeln, Zip-Download, Kontaktabzug, Processing-Container-Image | Alle Sharedien-Renditions inkl. Wasserzeichen und 3D |
| **P4** | **Kollaboration** – Workspaces (3 Typen, Templates), States/Transitions, Tasks, Kommentare, Watcher, Shares + Public-Seite, Finalisierung, Benachrichtigungs-Center, Digest-Mails | Kanban-Workspace mit Freigabe an Externe |
| **P5** | **Automatisierung & Datenoperationen** – Rules-Engine, Mass-Edit, Import/Export, KI-Tagging/Extraktion, Jobs-Dashboard, Restriktionen (Feld/Wert/Derivat) | Regeln laufen live und als Job mit Report |
| **P6** | **DRM** – Licenses, RightsOwners, BillingContacts, PriceLists, Buyouts, Offers, Purchases, DRM-Rechte | Lizenzkauf-Flow mit Mails |
| **P7** | **Knowledge Base & Intranet** – Pages mit Versionen/Diff/Editor, News, Widgets-Startseite, Personenverzeichnis, Favoriten, globale Suche über alles | Wiki + Intranet-Startseite |
| **P8** | **AI & MCP** – MCP-Server mit Tools/Resources/Prompts aus allen Modulen, Admin-AI-Konfiguration, Assistent-Chat mit Tool-Calls, Audit der Tool-Aufrufe, `Coworkee.Ai`-Paket | Assistent legt per Chat Ordner an, sucht Assets, setzt Metadaten – nur im Rahmen der User-Rechte |
| **P9** | **Migration & Publishing** – Sharedien-Importer (DB + Storage → Shareme), Coworkee auf NuGet.org, SDK-Pakete (C#/TS), Betriebsdoku | Bestandsdaten übernommen, Pakete öffentlich |

Reihenfolge ist fix (jede Phase baut auf der vorigen). P0 und P1 sind die kritischen Fundamente; die Detail-Spec für P0 folgt nach Freigabe dieses Dokuments.

---

## 8. Entscheidungen, die deine Bestätigung brauchen

1. **PostgreSQL als Default-DB** (SQL Server als zweiter Provider später) – oder SQL Server als Default?
2. **OpenIddict als Auth-Server** (statt Keycloak/Entra-only).
3. **JSON-Spalte für Metadaten** (statt EAV).
4. **Elasticsearch von Anfang an** (P2) statt DB-Volltext.
5. **Hangfire + Outbox + Redis** statt Message-Broker in Phase 1.
6. **Blazor Web App mit BFF** (WASM interaktiv, Cookie-Login, Proxy) statt Standalone-WASM.
7. **Eigener Dispatcher** (CQRS-light) statt reiner Application-Services.
8. **Repo-Struktur**: `coworkee` als eigenes Repo neben `shareme`, lokaler Feed unter `coworkee/artifacts/nuget`, Schalter Package/Project-Referenz.
9. **Test-Stack**: xUnit v3 + Shouldly + NSubstitute + Testcontainers + bUnit + Playwright (statt NUnit/FluentAssertions/Moq des Templates; FluentAssertions ist ab v8 kommerziell).
10. **Lizenz für Coworkee** (MIT vs. GPL-3.0).
11. **Scope KB/Intranet** wie in A6 (minimal, erweiterbar) – oder mehr (z. B. Blog, Umfragen, Kalender)?
12. **Sharedien-Migration** als P9 – oder früher nötig?

---

## 9. Referenzen

- Feature-Inventar Sharedien: `docs/analysis/sharedien-docs-and-api.md` (Teil D.1: 20 Funktionsbereiche, 463 Endpoints, 424 DTOs), `sharedien-backend.md`, `sharedien-frontend.md`, `sharedien-services.md`, `sharedien-db-schema.md`, `sharedien-db-insights.md`.
- Template-Bewertung: `docs/analysis/coworkee-template.md` (Working Tree ist Branch `wayes-edith`; generischer Stand `master`; Prototyp-Branches `app-framework-lib-2`, `app-framework-3`).
- Coworkee-Icon: `docs/assets/coworkee-icon.svg`, `docs/assets/coworkee-favicon.ico` (aus Git-History wiederhergestellt).
- Versionen (NuGet, 2026-09-29): .NET SDK 10.0.300; MudBlazor 9.11.0; MudBlazor.Extensions 9.10.1; Nextended.Core/Aspire 10.1.38; Aspire 13.5.4; EF Core 10.0.12; OpenIddict 7.7.1; ModelContextProtocol 2.2.0; Microsoft.Extensions.AI 10.10.0.
