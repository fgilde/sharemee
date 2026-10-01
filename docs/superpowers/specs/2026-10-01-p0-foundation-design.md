# P0 – Fundament (Detail-Spec)

Stand: 2026-10-01 · Basis: [Master-Spec](2026-09-29-shareme-coworkee-architecture-design.md) (freigegeben) · Status: Entwurf

P0 liefert das lauffähige Gerüst: Coworkee-Pakete, Shareme-Dienste unter Aspire, Login, Admin für Identity/Rechte/Settings/Mail/Themes/Audit, Realtime, Setup-Wizard, Tests und CI. Keine Shareme-Fachlichkeit (Knoten, Assets) – die kommt in P1.

P0 wird in drei Pläne geschnitten, jeweils lauffähig und getestet:

| Plan | Inhalt |
|---|---|
| **P0.1 Gerüst & Kern** | Repos, Build-Infrastruktur, lokaler Feed, `Coworkee.Core/Domain/Contracts/Application/Infrastructure/AspNetCore/Testing/Aspire`, Modulsystem, Dispatcher, EF-Basis mit Interceptors (Audit, SoftDelete, Tenant, RowVersion, Outbox), Shareme AppHost/ServiceDefaults/Migrations/Api, Health, OpenAPI+Swagger, erster Integrationstest gegen Postgres |
| **P0.2 Identity & Rechte** | `Shareme.Auth` (OpenIddict + Identity), Tenants, User, Gruppen, Rollen, Permission-Definitionen/-Grants, Resource-Permission-Basis (nicht hierarchisch), Permission-Checker + Policies, Setup-Wizard (API + Gate), BFF-Login in `Shareme.Web`, Shell mit MudBlazor/MudEx, Admin-Seiten Users/Gruppen/Rollen/Permission-Matrix |
| **P0.3 Plattform-Dienste** | Settings, Mail-Templates + Versand, Audit-Abfrage + UI, Themes + Editor, Realtime (Hub, Redis-Backplane, Client-Abo), Notifications, Jobs (Hangfire) + Dashboard, Playwright-E2E, CI |

---

## 1. Repositories und Build

```
C:\dev\privat\github\coworkee\
  global.json                 SDK 10.0.300, rollForward latestFeature
  Directory.Build.props       net10.0, Nullable, ImplicitUsings, LangVersion latest, TreatWarningsAsErrors (src), Analyzer, Paket-Metadaten (Authors fgilde, MIT, Icon, Repo-URL)
  Directory.Packages.props    Central Package Management, alle Versionen
  Version.props               VersionPrefix 0.1.0, VersionSuffix aus CI (dev-<timestamp> lokal)
  nuget.config                nuget.org
  .editorconfig
  Coworkee.slnx
  assets/icon.png, icon.svg   aus docs/assets/coworkee-icon.svg
  src/Coworkee.{Core,Domain,Contracts,Application,Infrastructure,AspNetCore,Client.Blazor,Testing,Aspire}
  tests/Coworkee.{Core,Application,Infrastructure,AspNetCore,Client.Blazor}.Tests, tests/Coworkee.ArchitectureTests
  build/pack-local.ps1        dotnet pack -c Release -o artifacts/nuget mit Suffix dev-<yyyyMMddHHmm>
  artifacts/nuget/            (gitignored) lokaler Feed

C:\dev\privat\github\shareme\
  global.json, Directory.Build.props, Directory.Packages.props, .editorconfig, Shareme.slnx
  nuget.config                Quellen: nuget.org + ../coworkee/artifacts/nuget (Key "coworkee-local")
  Directory.Build.targets     CoworkeeReferenceMode=Package (Default) | Project → ersetzt PackageReference Coworkee.* durch ProjectReference auf ../coworkee/src
  src/Shareme.AppHost, Shareme.ServiceDefaults
  src/Shareme.Contracts, Shareme.Domain, Shareme.Application, Shareme.Infrastructure   (P0: nur Root-Modul + DbContext, Fachliches ab P1)
  src/Shareme.Migrations      Worker: Migrate + Seed, endet danach
  src/Shareme.Auth            OpenIddict-Server + Razor-Seiten Login/Logout/Consent/Registrierung
  src/Shareme.Api             REST + SignalR + Hangfire-Server (P0.3)
  src/Shareme.Web             Blazor Web App Host (BFF, YARP-Forwarder)
  src/Shareme.Web.Client      WASM-Client
  tests/Shareme.{Application,Api,Web.Client}.Tests, tests/Shareme.E2E
```

`CoworkeeReferenceMode=Project` ist der Entwicklungsmodus (Breakpoints, sofortige Änderungen); CI und Release bauen `Package`.

## 2. Coworkee-Kern (P0.1)

### 2.1 Core
- `CoworkeeModule` (abstrakt): `ConfigureServices(ModuleServiceContext)`, `ConfigureApplication(ModuleApplicationContext)` (Middleware/Endpoints, nur im Web-Host), `SeedAsync(SeedContext, CancellationToken)`.
- `[DependsOn(params Type[])]`; `ModuleLoader.Load(rootType)` → topologisch sortierte, deduplizierte Liste; Zyklus = Exception mit Pfad.
- `services.AddCoworkee<TRoot>(configuration)` registriert Module als Singleton `IReadOnlyList<CoworkeeModule>`; `app.UseCoworkee()` ruft `ConfigureApplication` in Reihenfolge.
- `Result`, `Result<T>`, `Error(Code, Message, ErrorKind: Validation|NotFound|Forbidden|Conflict|Unexpected, Details)`.
- `PagedResult<T>(Items, TotalCount, Page, PageSize)`, `ICurrentUser { UserId?, TenantId?, IsAuthenticated, Roles, IsInRole }`, `IClock`.

### 2.2 Domain
- `Entity<TId>`, `AggregateRoot<TId>` mit `DomainEvents` (Add/Clear), `IDomainEvent`.
- Marker: `IAuditable` (CreatedAt/By, ModifiedAt/By), `ISoftDelete` (IsDeleted, DeletedAt/By), `IMultiTenant` (TenantId), `IHasConcurrencyToken` (Version uint → Postgres xmin / SQL rowversion via Konvention).
- IDs: `Guid` v7 (`Guid.CreateVersion7()`), stark typisiert erst bei Bedarf (YAGNI).

### 2.3 Contracts
- `QueryOptions { Search, Filter (OData-ähnlicher String, P1), Sort, Page, PageSize }`, DTO-Konventionen, `RealtimeEvent`-Verträge, Permission-/Setting-Namen der Kernmodule als `const string`.
- Keine Abhängigkeit außer Core.

### 2.4 Application – Dispatcher
```csharp
public interface IRequest<TResult>;
public interface ICommand<TResult> : IRequest<TResult>;
public interface IQuery<TResult> : IRequest<TResult>;
public interface IHandler<in TRequest, TResult> where TRequest : IRequest<TResult>
{ Task<TResult> HandleAsync(TRequest request, CancellationToken ct); }
public delegate Task<TResult> RequestHandlerDelegate<TResult>();
public interface IRequestMiddleware
{ Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct) where TRequest : IRequest<TResult>; }
public interface IDispatcher { Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken ct = default); }
```
- Auflösung per `HandlerInvoker<TRequest,TResult>` (generisch gecacht je Request-Typ, keine Reflection pro Aufruf).
- Registrierung: `ctx.AddHandlers(typeof(Module).Assembly)` (Scan nach `IHandler<,>`), Middlewares geordnet über `ctx.AddRequestMiddleware<T>(order)`.
- Standard-Middlewares P0.1: `LoggingMiddleware`, `ValidationMiddleware` (FluentValidation, Fehler → `Result.Validation` oder `ValidationException` wenn TResult kein Result), `UnitOfWorkMiddleware` (nur `ICommand`: `IUnitOfWork.SaveChangesAsync`). P0.2 ergänzt `AuthorizationMiddleware`.

### 2.5 Infrastructure – EF-Basis
- `CoworkeeDbContext` (abstrakt) sammelt `IModelContributor` (`Apply(ModelBuilder)`), wendet Konventionen an (Soft-Delete- und Tenant-Query-Filter, Concurrency-Token, Guid v7 Default, snake_case nicht – PascalCase bleibt).
- Interceptors: `AuditableInterceptor` (Felder setzen), `SoftDeleteInterceptor` (Delete → Update), `AuditTrailInterceptor` (schreibt `AuditEntry` + `AuditChange` in denselben SaveChanges), `OutboxInterceptor` (sammelt DomainEvents der Aggregate → `OutboxMessage`).
- `OutboxMessage { Id, Type, Payload(json), OccurredAt, ProcessedAt?, Error?, Attempts }`; `OutboxProcessor` (BackgroundService, Polling 1 s + Signal nach Commit, `FOR UPDATE SKIP LOCKED` auf Postgres) ruft `IDomainEventHandler<T>` in neuem Scope.
- `AuditEntry { Id, TenantId?, EntityType, EntityId, Action (Created|Updated|Deleted|Restored), ActorId?, CorrelationId, OccurredAt, Changes[] {Property, OldValue(json), NewValue(json)} }` – `[NotAudited]` an Property/Entity schließt aus, Secrets werden maskiert (`[Sensitive]`).
- Provider: Npgsql (Default). SQL-Server-Kompatibilität wird durch provider-neutrale Konfiguration gewahrt, aber in P0 nicht getestet.

### 2.6 AspNetCore
- `builder.AddCoworkee<TRoot>()` (Services + ServiceDefaults-Hooks), `app.UseCoworkee()`.
- Endpunkte als Minimal APIs gruppiert je Modul (`MapGroup("/api/v1/identity")`), Helper `group.MapCommand<TRequest,TResult>(...)`/`MapQuery` → Dispatcher, `Result` → HTTP (200/201/204, ProblemDetails 400/403/404/409/500).
- OpenAPI: `Microsoft.AspNetCore.OpenApi` (eingebaut) + Swagger UI (`Swashbuckle.AspNetCore.SwaggerUI`) unter `/swagger`; Dokument je API-Version.
- `ICurrentUser` aus `HttpContext.User`, Correlation-Id-Middleware (`X-Correlation-Id`, in Logs + Audit).

### 2.7 Testing
- `PostgresFixture` (Testcontainers.PostgreSql, ein Container pro Test-Collection, Respawn-Reset), `ApiFactory<TProgram>` (`WebApplicationFactory` + Fixture-Connection), `TestCurrentUser`, `FakeClock`.

### 2.8 Aspire
- `builder.AddCoworkeeInfrastructure(options)` → Postgres (+ DB), Redis, Mailpit; liefert Ressourcen-Handles. Elasticsearch/MinIO kommen in P1/P2 hinzu.

## 3. Identity & Rechte (P0.2)

### 3.1 Datenmodell (Schema `cw`)
- `Tenant { Id, Name, Identifier(unique), IsActive, IsDefault, AcceptsRegistrations }`.
- Identity (ASP.NET Core Identity, Guid-Keys): `User : IdentityUser<Guid>` + `TenantId, FirstName, LastName, Department, AvatarBlobKey?, IsActive, AccessType (Permanent|Expiring), ExpiresAt?, LastLoginAt?, Culture, TimeZone`; `Role : IdentityRole<Guid>` + `TenantId?, IsSystem, Description, AssignableResourceTypes[] (json)`; `UserGroup { Id, TenantId, Name, Description, ExternalKey? }`, `UserGroupMember { GroupId, UserId, IsProvisioned }`, `GroupRole { GroupId, RoleId }` (global je Tenant).
- `PermissionGrant { Id, TenantId?, PermissionName, ProviderType (Role|User|Group), ProviderKey, IsGranted }` (unique Tenant+Name+Provider).
- `ResourcePermission { Id, TenantId, ResourceType, ResourceId, PrincipalType (User|Group), PrincipalId, RoleId }` (unique). Hierarchie/Materialisierung: P1.

### 3.2 Permission-Definitionen
```csharp
public interface IPermissionDefinitionContributor { void Define(PermissionDefinitionContext ctx); }
ctx.AddGroup("Identity", L("Identität"))
   .Add("Identity.Users.View").Add("Identity.Users.Manage", implies: "Identity.Users.View") ...;
```
- `PermissionDefinition { Name, DisplayName (LocalizedString-Key), Group, Implies[], Scopes (Global|Tenant|Resource:<type>) }`.
- Kern-Permissions P0: `Identity.Users.{View,Manage}`, `Identity.Groups.{View,Manage}`, `Identity.Roles.{View,Manage}`, `Identity.Permissions.Manage`, `Tenants.{View,Manage}`, `Settings.{View,Manage}`, `MailTemplates.{View,Manage}`, `Audit.View`, `Themes.Manage`, `Jobs.View`, `System.Setup`.
- System-Rolle `admin` (alle Permissions, nicht löschbar), `user` (keine Admin-Permissions).

### 3.3 Prüfung
- `IPermissionChecker.IsGrantedAsync(string permission, CancellationToken)` und `IsGrantedAsync(string permission, string resourceType, Guid resourceId, CancellationToken)`.
- Auflösung: effektive Permissions des Users = Grants (User) ∪ Grants (Rollen des Users) ∪ Grants (Gruppen) ∪ Grants (Rollen der Gruppen), inkl. `Implies`; Cache in `IDistributedCache` (Redis) je User+Tenant, Invalidierung per Domain-Event (`PermissionGrantsChanged`, `UserRolesChanged`, `GroupMembershipChanged`).
- Resource-Check: Rollen des Users/seiner Gruppen an der Ressource → deren Grants.
- Request-Attribut `[RequiresPermission("…")]`; `IResourceRequest { ResourceType, ResourceId, RequiredPermission }` → `AuthorizationMiddleware`.
- ASP.NET-Policies: `PermissionPolicyProvider` (`"perm:<name>"`) für Endpunkte und Blazor `AuthorizeView Policy`.
- Endpunkte: `GET /api/v1/permissions/definitions`, `GET/PUT /api/v1/permissions/grants?providerType&providerKey`, `GET /api/v1/permissions/effective?userId&resourceType&resourceId`, `GET/POST/DELETE /api/v1/resource-permissions`.

### 3.4 Auth-Dienst
- OpenIddict 7 Server mit EF-Stores (gleiche DB, Schema `auth`), Flows: Authorization Code + PKCE (Web-BFF, öffentliche Clients), Refresh Token (rotierend), Client Credentials (Integrationen), Device Code (später MCP/CLI – Client-Registrierung vorbereitet).
- Scopes: `openid profile email roles offline_access shareme_api`. Access-Token als JWT (signiert, lokal validierbar), Laufzeit 15 min; Claims: `sub`, `tenant`, `role[]`, `name`, `email`.
- UI (Razor Pages, MudBlazor-SSR-Stil, gebrandet über Theme-Endpunkt): Login, Logout, Passwort vergessen/zurücksetzen, E-Mail bestätigen, Registrierung (wenn Tenant `AcceptsRegistrations`; Status ausstehend bis Freigabe), 2FA (TOTP) einrichten/abfragen. Passkeys: P1+.
- Externe Provider: Schnittstelle vorbereitet (`IExternalLoginProviderSource` aus Settings); Entra ID/OIDC/SAML/LDAP: P1+ (eigener Plan).
- Lockout: 10 Versuche / 15 min (Settings).

### 3.5 Web (BFF)
- `Shareme.Web`: OIDC-Client (Cookie + OpenIdConnect, `SaveTokens`), Token-Refresh-Handler, `MapForwarder("/api/{**catch-all}", "https+http://shareme-api")` + `/hubs/{**}` mit Bearer-Transform (`Microsoft.Extensions.ServiceDiscovery.Yarp`), Antiforgery, `PersistentAuthenticationStateProvider`-Muster für WASM.
- `Shareme.Web.Client`: `AddCoworkeeClient()` → MudBlazor + MudEx, `ApiClient` (HttpClient gegen gleiche Origin), Auth-State, Permission-Policies (Permissions des Users per `GET /api/v1/permissions/me`, gecacht, Realtime-invalidiert ab P0.3).
- Layout: AppBar (Logo, Suche-Platzhalter, Benachrichtigungen ab P0.3, User-Menü mit Theme-Umschalter), NavMenu aus `IMenuContributor`, Content. Startseite P0: Platzhalter-Dashboard.

### 3.6 Admin-UI (Coworkee.Client.Blazor, Bereich `/admin`)
- Users: Liste (Server-Paging, Suche), Anlegen/Bearbeiten (MudExObjectEdit), Aktiv/Inaktiv, Passwort zurücksetzen, Rollen, Gruppen, effektive Rechte.
- Gruppen: Liste, Mitglieder, Rollen.
- Rollen: Liste, Bearbeiten, Permission-Matrix (Gruppen aufklappbar, Implikationen sichtbar, Speichern als Diff).
- `ResourcePermissionsPanel` (Komponente, wiederverwendbar ab P1): Einträge listen, Principal-Picker (User/Gruppe), Rollen-Picker (gefiltert nach `AssignableResourceTypes`), Zuweisen/Entziehen.
- Tenants (wenn mehr als einer erlaubt).

### 3.7 Setup-Wizard
- `SystemState { IsInitialized, InitializedAt, Version }` (Tabelle `cw.SystemState`, eine Zeile).
- Gate-Middleware in Api + Web: solange nicht initialisiert, liefert Api `503 + setup_required` außer `/api/v1/setup/*` und Health; Web leitet auf `/setup` um.
- Wizard (Web, anonym, nur solange nicht initialisiert; Schutz über einmaligen Setup-Token aus Log/Env `SHAREME_SETUP_TOKEN`): 1 Willkommen + Token, 2 System-Check (DB, Redis, Mail erreichbar), 3 Organisation (Tenant-Name, Sprache, Zeitzone), 4 Administrator (E-Mail, Name, Passwort), 5 Mail (SMTP, Testversand – überspringbar), 6 Branding (Theme-Auswahl, Logo – ab P0.3, vorher übersprungen), 7 Zusammenfassung → `POST /api/v1/setup/complete` (Tenant, Admin-User mit Rolle admin, Settings, `IsInitialized=true`, Audit).

## 4. Plattform-Dienste (P0.3)

- **Settings**: `ISettingDefinitionContributor` (`SettingDefinition { Name, Type, Default, Scopes (Global|Tenant|User), IsEncrypted, IsVisibleToClient, Group, DisplayName, Description }`), `SettingValue { Name, Scope, ScopeKey, Value }`, `ISettingProvider` (User→Tenant→Global→Default, Redis-Cache, Event-Invalidierung), verschlüsselte Werte via Data Protection (Keys in DB), Admin-Seite generiert Formulare aus Definitionen (gruppiert), User-Einstellungen-Seite für User-Scope.
- **Mail**: `IMailTemplateContributor` (`MailTemplateDefinition { Name, ModelType, DefaultSubject/Body je Kultur (eingebettete Ressource .scriban) }`), `MailTemplateOverride { Name, TenantId?, Culture, Subject, Body, Version }` + Snapshot-Historie, Renderer Scriban mit Layout-Template, `IMailSender` → `OutgoingMail`-Tabelle → Hangfire-Job (MailKit SMTP, 3 Retries, Whitelist-Setting), Admin: Liste, Editor (MudEx Code-Editor/Textarea + Vorschau mit Beispielmodell), Testversand, Versandprotokoll. Kern-Templates: `Identity.ConfirmEmail`, `Identity.ResetPassword`, `Identity.RegistrationPending`, `Identity.RegistrationApproved`, `Identity.Welcome`, `Notifications.Digest`.
- **Audit-UI**: `GET /api/v1/audit?entityType&entityId&actorId&from&to&page` (Permission `Audit.View`), Seite mit Filter + Timeline + Diff-Dialog; Komponente `AuditTimeline(EntityType, EntityId)` zum Einbetten.
- **Snapshots/Versionierung**: `IVersioned` + `EntitySnapshot { EntityType, EntityId, Version, Payload, CreatedAt, CreatedBy }` (Interceptor), Endpunkte `GET …/versions`, `POST …/versions/{n}/restore` generisch für versionierte Typen (MailTemplateOverride, ThemeDefinition in P0).
- **Themes**: `ThemeDefinition : IVersioned { Name, TenantId?, IsDefault, PaletteLight, PaletteDark (json, MudBlazor-Palette), Typography, LayoutProperties, LogoSvg?, FaviconBlobKey?, CustomCss? }`; Seeds: „Coworkee" (aus Icon-Farben), „Classic", „High Contrast"; Admin-Editor mit `MudExThemeEdit` + Live-Vorschau; Client lädt Tenant-Default (`GET /api/v1/themes/current`, anonym für Login-Branding), User wählt hell/dunkel/System + Theme (User-Setting).
- **Realtime**: `RealtimeHub` (`/hubs/realtime`, authentifiziert), Methoden `Subscribe(topic)`/`Unsubscribe(topic)` mit Topic-Autorisierung über `IRealtimeTopicAuthorizer` (Contributor je Topic-Präfix), Server→Client `OnEvent(RealtimeEnvelope { Topic, Type, Payload })`. Publisher schreibt in Outbox → nach Commit Redis-Backplane. Automatisch: `EntityChanged { EntityType, EntityId, Action, ChangedProperties }` für Aggregate mit `[Realtime]`-Attribut an Topics `type:{EntityType}` und `entity:{EntityType}:{Id}`; `user:{id}` für Notifications, `job:{id}` für Fortschritt. Client: `IRealtimeClient` (Reconnect, Re-Subscribe), `Subscribe<T>(topic, handler)` → `IAsyncDisposable`; Komponente `<RealtimeSubscription Topic=… OnEvent=…/>`; Admin-Listen aktualisieren sich live.
- **Notifications**: `Notification { Id, UserId, Type, Title, Body, Link?, CreatedAt, ReadAt? }`, `INotifier.NotifyAsync(userIds, …)` (Outbox → Realtime `user:{id}` + optional Mail-Digest), Glocke in AppBar mit Zähler, Liste, als gelesen markieren.
- **Jobs**: Hangfire mit Postgres-Storage, Server im Api-Prozess (Queues `default`, `mail`), `IBackgroundJobs.Enqueue<TJob>(args)`, Dashboard `/admin/jobs` hinter `Jobs.View` (über Api, im Web per Forwarder).

## 5. Tests und Qualität

| Ebene | Werkzeug | P0-Abdeckung |
|---|---|---|
| Unit | xUnit v3, Shouldly, NSubstitute | ModuleLoader (Sortierung, Zyklen), Dispatcher (Pipeline-Reihenfolge, Validation, UoW nur Commands), Permission-Auflösung (Implies, Gruppen, Ressource), Setting-Auflösung, Scriban-Renderer, Audit-Diff |
| Integration | Testcontainers Postgres/Redis, Respawn, WebApplicationFactory | Interceptors (Audit, SoftDelete, Tenant-Filter, Outbox), Setup-Flow, Identity-Endpunkte, Grants + Checker, Settings, Mail-Queue (Mailpit-Container), Realtime (SignalR-Testclient) |
| Komponenten | bUnit | Permission-Matrix, ResourcePermissionsPanel, Settings-Formular, Setup-Wizard-Schritte |
| E2E | Playwright + Aspire.Hosting.Testing | Setup-Wizard auf leerer DB → Login → User anlegen → Rolle zuweisen → zweiter Browser sieht Änderung live |
| Architektur | NetArchTest.eNhancedEdition | Domain ohne EF/AspNetCore, Contracts ohne Application, Web.Client ohne Infrastructure/EF, Handler internal sealed |

CI (GitHub Actions, beide Repos): `dotnet build -c Release`, `dotnet test` (Docker verfügbar), Coworkee: `pack` → Artefakt; Shareme bezieht in CI die Coworkee-Pakete per Checkout + Pack (bis NuGet-Publish in P9).

## 6. Definition of Done P0

1. `aspire run` (bzw. AppHost F5) startet Postgres, Redis, Mailpit, Migrations, Auth, Api, Web ohne Fehler; Aspire-Dashboard zeigt alle grün.
2. Leere DB → Setup-Wizard → Admin angemeldet.
3. Admin verwaltet User/Gruppen/Rollen/Permission-Matrix, Settings, Mail-Templates (mit Vorschau/Testmail in Mailpit), Themes (Live-Wechsel); jede Änderung erscheint im Audit-Log mit Alt/Neu.
4. Zwei Browser: Änderung in A ist ohne Reload in B sichtbar.
5. Swagger unter Api `/swagger` dokumentiert alle Endpunkte; alle Endpunkte außer Setup/Health/Theme-current verlangen Auth + Permission.
6. Alle Testebenen grün; Coworkee-Pakete im lokalen Feed; Shareme baut im Package-Modus.

## 7. Bewusst nicht in P0

Knoten/Assets/Metadaten/Suche (P1/P2), externe Login-Provider und Passkeys (eigener Plan nach P1), Webhooks (P4), MCP/AI (P8), SDK-Generierung (P9), SQL-Server-Tests, UI-Übersetzungen über Englisch/Deutsch hinaus (resx en/de in P0).
