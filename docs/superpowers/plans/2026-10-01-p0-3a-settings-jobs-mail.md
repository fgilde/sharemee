# P0.3a Settings, Background Jobs, Mail – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (native). Tests are specified per case and written first; implementation files are listed with their contract and the non-obvious parts. The executor writes the implementation against the tests.

**Goal:** Konfigurierbare Einstellungen (Global → Tenant → User, verschlüsselt, admin-editierbar), Hintergrundjobs über Hangfire und ein Mail-System mit Templates (Scriban), Overrides, Vorschau, Testversand, Queue und Versandprotokoll.

**Architecture:** Drei neue Coworkee-Module als eigene Pakete: `Coworkee.Settings`, `Coworkee.BackgroundJobs`, `Coworkee.Mailing` (hängt von beiden ab). Daten im Schema `cw` der gemeinsamen DB. Mails werden beim Einreihen gerendert und als `OutgoingMail`-Aggregat gespeichert; das Domain-Event `MailQueued` läuft über die Outbox (nach Commit) und reiht einen Hangfire-Job in Queue `mail` ein, der per MailKit versendet. Die Admin-Oberflächen liegen in `Coworkee.Client.Blazor`.

**Tech Stack:** Hangfire.Core/AspNetCore 1.8.25, Hangfire.PostgreSql 1.21.1, Scriban 7.5.0, MailKit 4.18.1, ASP.NET Data Protection (eigenes `IXmlRepository` auf der DB), Testcontainers (Postgres, generischer Mailpit-Container).

**Spec:** `docs/superpowers/specs/2026-10-01-p0-foundation-design.md` Abschnitt 4 (Settings, Mail, Jobs).

## Global Constraints

- Alle Constraints aus P0.1/P0.2.
- Settings-Auflösung: User → Tenant → Global → Default. Verschlüsselte Werte verlassen den Server nie im Klartext; GET liefert `HasValue` statt Wert.
- Permissions: `Settings.Manage` (Global + Tenant), `Mail.Templates.Manage`, `Mail.Log.View`, `Jobs.View`. User-Scope-Settings darf jeder angemeldete User für sich selbst setzen, aber nur Definitionen mit Scope `User`.
- Mail: 3 Versuche (Hangfire `AutomaticRetry`), Whitelist-Setting `Mail.Whitelist` (leer = alle; sonst Adressen oder `@domain`, getrennt durch Zeilenumbruch/Komma), nicht erlaubte Empfänger → Status `Skipped`.
- Scriban-Modelle mit Standard-Member-Renamer (snake_case: `{{ user.first_name }}`), Layout-Template `Layout` mit `{{ content }}`.
- Hangfire-Server läuft im Api-Prozess (Queues `default`, `mail`), Schema `hangfire`. Dashboard unter `/admin/jobs` hinter `Jobs.View`, im Web per Forwarder.

## Review Focus

1. Verschlüsselter Wert (`Mail.Smtp.Password`) taucht in keiner API-Antwort, keinem Audit-Eintrag und keinem Log im Klartext auf. Test in Task 1.
2. User setzt per API ein Setting mit Scope nur `Global` im User-Scope → 400; fremder Tenant sieht Tenant-Werte nicht. Test in Task 1.
3. Mail-Job läuft erst nach Commit; Rollback ⇒ kein Versand. Test in Task 3.
4. Whitelist: Empfänger außerhalb → `Skipped`, nie an SMTP übergeben. Test in Task 3.
5. Template-Override mit Scriban-Syntaxfehler wird beim Speichern mit 400 und Fehlerposition abgelehnt, nicht erst beim Versand. Test in Task 3.

---

### Task 1: Coworkee.Settings

**Files:** Create `coworkee/src/Coworkee.Settings/**`, `coworkee/src/Coworkee.Contracts/Settings/*.cs`; Test `coworkee/tests/Coworkee.Settings.Tests/**`

**Interfaces (Produces):**
- Contracts: `enum SettingScope { Global, Tenant, User }` (string enum), `enum SettingType { String, Text, Bool, Int, Secret, Choice }`, `SettingDefinitionDto(Name, Group, DisplayName, Description, Type, DefaultValue, IReadOnlyList<SettingScope> Scopes, IReadOnlyList<string>? Choices)`, `SettingGroupDto(Name, DisplayName, IReadOnlyList<SettingDefinitionDto> Settings)`, `SettingValueDto(Name, string? Value, bool HasValue)`, `SetSettingsRequest(IReadOnlyDictionary<string, string?> Values)` (null = Wert entfernen), `SettingsPermissions.Manage = "Settings.Manage"`.
- `ISettingDefinitionContributor { void Define(SettingDefinitionContext context); }`, `SettingDefinitionContext.Group(name, displayName).Add(name, displayName, SettingType type, SettingScope[] scopes, string? defaultValue = null, bool visibleToClient = false, string? description = null, string[]? choices = null)`; `Secret` impliziert verschlüsselt und nie client-sichtbar.
- `ISettingProvider`: `Task<string?> GetAsync(string name, CancellationToken)`, `Task<T> GetAsync<T>(string name, CancellationToken)` (invariant parse), `Task<IReadOnlyDictionary<string,string?>> GetClientValuesAsync(CancellationToken)`.
- Entity `SettingValue : AuditedEntity { Name, Scope, ScopeKey (Guid?), Value }`, unique `(Name, Scope, ScopeKey)` mit `AreNullsDistinct(false)`; `Value` ist `IsSensitive()` → im Audit maskiert.
- Data Protection: `DataProtectionKey { Id, FriendlyName, Xml }` (`[NotAudited]`) + `DbXmlRepository : IXmlRepository`; `services.AddDataProtection().SetApplicationName("Coworkee")` + `KeyManagementOptions.XmlRepository`.
- Cache: HybridCache-Tag `settings`, Invalidation per `SaveChangesInterceptor` nach Commit (Muster aus `PermissionCacheInterceptor`).
- Defaults aus Konfiguration: `Coworkee:Settings:Defaults:<Name>` überschreibt den Definitions-Default.
- Endpunkte (`/api/v1/settings`): `GET /definitions` (Manage), `GET|PUT /global`, `GET|PUT /tenant` (Manage, aktueller Tenant), `GET|PUT /user` (angemeldet; `GET /definitions/user` liefert nur User-Scope-Definitionen ohne Manage), `GET /client` (angemeldet: aufgelöste client-sichtbare Werte).
- Modul `CoworkeeSettingsModule` (DependsOn `CoworkeeApplicationModule`, IWebModule).

- [ ] **Step 1: Failing tests** (`SettingsApp` analog `IdentityApp`: Postgres + TestServer + Test-Auth; Testmodul mit Definitionen `Test.Text` String Default "fallback" Scopes Global/Tenant/User client-sichtbar, `Test.GlobalOnly` Int Default "5" Scope Global, `Test.Secret` Secret Scopes Global/Tenant)
  - `Default_applies_without_values` – `/client` liefert `Test.Text = "fallback"`.
  - `Tenant_overrides_global_and_user_overrides_tenant` – global "g", tenant "t", user "u" ⇒ "u"; User-Wert entfernen ⇒ "t".
  - `Other_tenant_does_not_see_tenant_value` – zweiter Tenant sieht "g".
  - `User_cannot_set_global_only_setting` – `PUT /user {Test.GlobalOnly}` ⇒ 400.
  - `Unknown_setting_is_rejected` – ⇒ 400.
  - `Int_setting_rejects_non_numbers` – ⇒ 400.
  - `Without_manage_permission_global_is_forbidden` – ⇒ 403.
  - `Configured_default_overrides_definition_default` – `Coworkee:Settings:Defaults:Test.GlobalOnly = 7` ⇒ Provider liefert 7.
  - `Secret_is_never_returned_in_plain_text` – `GET /global`: HasValue true, Value null, JSON ohne Klartext.
  - `Secret_is_stored_encrypted` – DB-Wert ≠ Klartext.
  - `Secret_is_masked_in_audit` – Audit-Changes ohne Klartext.
  - `Provider_decrypts_secret` – Provider liefert Klartext.
  - `Secret_is_not_exposed_to_client` – `/client` ohne Key.
- [ ] **Step 2: Run, expect FAIL** (Projekt fehlt).
- [ ] **Step 3: Implementation** – Validierung über die Definition (Typ, Scope, Choices); Verschlüsselung mit `IDataProtector("Coworkee.Settings")`; Provider lädt alle Werte der drei Scopes in einem Query, cached pro `(tenant,user)` mit Tag `settings`.
- [ ] **Step 4: Run, expect PASS** – 13 passed; Suite grün.
- [ ] **Step 5: Commit** – `feat(settings): scoped settings with encryption, cache and api`

---

### Task 2: Coworkee.BackgroundJobs

**Files:** Create `coworkee/src/Coworkee.BackgroundJobs/**`; Test `coworkee/tests/Coworkee.BackgroundJobs.Tests/**`

**Interfaces (Produces):**
- `IBackgroundJob<TArgs> { Task ExecuteAsync(TArgs args, CancellationToken cancellationToken); }`
- `IBackgroundJobs { string Enqueue<TJob, TArgs>(TArgs args, string queue = "default") where TJob : IBackgroundJob<TArgs>; }` – fängt UserId/TenantId aus `ICurrentUser` ein; `JobRunner<TJob,TArgs>.RunAsync(JobEnvelope<TArgs>, CancellationToken)` stellt den Actor per `CurrentUserScope.Begin(new ImpersonatedUser(...))` wieder her und löst `TJob` aus einem neuen Scope.
- `BackgroundJobOptions` (Section `Coworkee:Jobs`): `string ConnectionStringName`, `bool RunServer = true`, `string[] Queues = ["default", "mail"]`, `int WorkerCount = 5`, `int[] RetryDelaysInSeconds = [10, 60, 300]`, `int Attempts = 3`.
- `CoworkeeBackgroundJobsModule` (IWebModule): Hangfire mit PostgreSql-Storage (Schema `hangfire`), Server wenn `RunServer`; Dashboard `/admin/jobs` mit `IDashboardAsyncAuthorizationFilter` → `IPermissionChecker.IsGrantedAsync("Jobs.View")`; Permission-Contributor `Jobs.View`.
- `JobsPermissions.View = "Jobs.View"` in Contracts.

- [ ] **Step 1: Failing tests**
  - `Enqueued_job_runs_with_captured_actor` – Enqueue unter Actor (u, t) ⇒ Job sieht (u, t).
  - `Job_runs_in_requested_queue` – Queue `mail` laut Monitoring-API.
  - `Failing_job_is_retried` – erster Lauf wirft ⇒ zweiter Lauf erfolgt.
  - `Dashboard_requires_jobs_view` – anonym ⇒ 401, ohne Recht ⇒ 403, mit Recht ⇒ 200.
- [ ] **Step 2: Run, expect FAIL**.
- [ ] **Step 3: Implementation** – laut Interfaces.
- [ ] **Step 4: Run, expect PASS** – 4 passed.
- [ ] **Step 5: Commit** – `feat(jobs): hangfire background jobs with actor context and dashboard`

---

### Task 3: Coworkee.Mailing

**Files:** Create `coworkee/src/Coworkee.Mailing/**` (Templates als EmbeddedResource), `coworkee/src/Coworkee.Contracts/Mailing/*.cs`; Test `coworkee/tests/Coworkee.Mailing.Tests/**`

**Interfaces (Produces):**
- `IMailTemplateContributor { void Define(MailTemplateContext context); }`; `MailTemplateDefinition(string Name, string DisplayName, object SampleModel, IReadOnlyDictionary<string, MailTemplateContent> Defaults)` (Key = Kultur, `"en"` Pflicht); `MailTemplateContent(string Subject, string Body)`; `EmbeddedTemplates.Load(Assembly, string name)` liest `<name>.<culture>.subject.scriban` / `.body.scriban`.
- Kern-Templates in Coworkee.Mailing: `Layout`, `Identity.ConfirmEmail`, `Identity.ResetPassword`, `Identity.RegistrationPending`, `Identity.RegistrationApproved`, `Identity.Welcome`, `Notifications.Digest` (en + de).
- Entity `MailTemplateOverride : AuditedAggregateRoot { Name, TenantId?, Culture, Subject, Body }` unique `(Name, TenantId, Culture)` mit `AreNullsDistinct(false)`.
- `IMailTemplateRenderer.RenderAsync(name, model, culture, overrideContent?) → RenderedMail(Subject, HtmlBody)`: Override (Tenant → Global) vor Default, Kultur-Fallback `de-AT → de → en`, Body in `Layout`. `MailTemplateValidation.Validate(subject, body) → IReadOnlyList<string>` (Scriban-Parse-Messages).
- `OutgoingMail : AggregateRoot, IMultiTenant { To, Subject, HtmlBody, TemplateName, Status (Queued|Sent|Failed|Skipped), Attempts, LastError, QueuedAt, SentAt }` raist `MailQueued(Guid MailId)`.
- `IMailSender.QueueAsync(string to, string template, object model, string? culture, CancellationToken)` – rendert, fügt hinzu (kein `SaveChanges`), Event → Outbox → `MailQueuedHandler` → `IBackgroundJobs.Enqueue<SendMailJob, Guid>(id, "mail")`.
- `SendMailJob`: Whitelist-Prüfung, Versand über `ISmtpTransport` (Default MailKit; Settings `Mail.Smtp.Host`, `Mail.Smtp.Port` (25), `Mail.Smtp.UseSsl`, `Mail.Smtp.UserName`, `Mail.Smtp.Password` (Secret), `Mail.From`, `Mail.Whitelist` (Text)); Fehler ⇒ `Attempts++`, `LastError`, rethrow; letzter Versuch ⇒ `Failed`.
- Endpunkte `/api/v1/mail`: `GET /templates`, `GET|PUT|DELETE /templates/{name}/{culture}`, `POST /templates/{name}/{culture}/preview`, `POST /templates/{name}/{culture}/test`, `GET /outgoing?page&pageSize&status`.
- Contracts: `MailPermissions.Templates.Manage`, `MailPermissions.Log.View`, `MailTemplateSummaryDto`, `MailTemplateDto`, `SaveMailTemplateRequest(Subject, Body)`, `RenderedMailDto(Subject, HtmlBody)`, `OutgoingMailDto`.

- [ ] **Step 1: Failing tests**
  - Renderer: `Default_template_renders_with_model_in_layout`, `Culture_falls_back_to_language_then_english`, `Tenant_override_wins_over_global_override_and_default`, `Model_values_are_html_escaped`, `Syntax_error_is_reported_with_position`.
  - Queue (Postgres + Mailpit-Container): `Queued_mail_is_sent_after_commit`, `Rolled_back_mail_is_never_sent`, `Whitelist_skips_other_recipients`, `Smtp_failure_marks_failed_after_retries`.
  - Endpunkte: `Override_with_syntax_error_is_rejected`, `Override_is_used_and_can_be_removed`, `Preview_renders_sample_model`, `Test_mail_goes_to_current_user`, `Log_lists_outgoing_mails`, `Templates_require_manage_permission`.
- [ ] **Step 2: Run, expect FAIL**.
- [ ] **Step 3: Implementation** – Scriban `Template.Parse`, `HasErrors`/`Messages`; Rendern über `TemplateContext` + `ScriptObject.Import(model)`; Model-Strings werden vor dem Import HTML-escaped (eigener `ScriptObject`-Import mit `WebUtility.HtmlEncode` für Strings), damit Admin-Templates nicht versehentlich XSS in Mails erzeugen.
- [ ] **Step 4: Run, expect PASS** – 15 passed; Suite grün.
- [ ] **Step 5: Commit** – `feat(mail): templates with overrides, queue via outbox and hangfire, smtp delivery`

---

### Task 4: Admin-UI für Settings, Mail, Jobs

**Files:** Modify `coworkee/src/Coworkee.Client.Blazor/**` (API-Client, Seiten `/admin/settings`, `/settings`, `/admin/mail/templates`, `/admin/mail/templates/{name}/{culture}`, `/admin/mail/log`, Nav inkl. `/admin/jobs` mit `ForceLoad`), `CoworkeeNavItem` + `bool ForceLoad`; Komponente `SettingsForm`; Tests `SettingsFormTests`, `MailTemplateEditorTests`.

- [ ] **Step 1: Failing tests**
  - `Renders_inputs_by_type` (Text, Checkbox, Zahl, Passwort; `data-setting`).
  - `Saves_only_changed_values`.
  - `Secret_shows_placeholder_and_is_not_sent_unless_changed`.
  - `Preview_renders_html_from_api`.
  - `Save_shows_syntax_errors_from_api`.
  - `Reset_removes_override`.
- [ ] **Step 2–4:** rot → Implementierung → grün.
- [ ] **Step 5: Commit** – `feat(client): settings, mail template and mail log admin pages`

---

### Task 5: Shareme-Integration

**Files:** Modify `shareme/src/Shareme.Infrastructure/ShareMeDatabaseModule.cs` (DependsOn Settings, Mailing, BackgroundJobs), `shareme/src/Shareme.Api` (Jobs-Server), `shareme/src/Shareme.Auth` (`Coworkee:Jobs:RunServer=false`), `shareme/src/Shareme.Web/Program.cs` (Forwarder `/admin/jobs/{**catch-all}` über `BffOptions.ForwardedPrefixes`), `shareme/src/Shareme.AppHost/AppHost.cs` (Mailpit-Host/Port als `Coworkee__Settings__Defaults__Mail.Smtp.*`), Migration `PlatformServices`; Test E2E erweitern.

- [ ] **Step 1: Failing test** – E2E nach Login: `POST /api/v1/mail/templates/Identity.Welcome/en/test` über den BFF ⇒ Mailpit zeigt binnen 60 s Mail an `admin@acme.test`; `GET /admin/jobs` über den BFF ⇒ 200.
- [ ] **Step 2–4:** rot → Implementierung → grün; `ModelTests` grün.
- [ ] **Step 5: Commit** – `feat: shareme settings, jobs and mail`
