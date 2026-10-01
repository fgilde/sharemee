# P0.3b Audit UI, Versioning, Themes – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (native). Tests per case are written first; implementation files are listed with their contract.

**Goal:** Audit-Log durchsuchbar (API + Seite + einbettbare Timeline), generische Versionierung mit Wiederherstellen für versionierte Typen und admin-konfigurierbare Themes mit Live-Vorschau, Tenant-Default und User-Wahl.

**Architecture:** `Coworkee.Infrastructure` bekommt `IVersioned` und einen `SnapshotInterceptor`, der bei jeder Änderung eines versionierten Aggregats einen `EntitySnapshot` (JSON der skalaren Properties, Version fortlaufend) schreibt. Neues Paket `Coworkee.Auditing` (IWebModule) liefert Audit-Abfrage, Versionsliste und Restore über registrierte `VersionedType`-Einträge (Typ, Name, View-/Restore-Permission). Neues Paket `Coworkee.Theming` mit `ThemeDefinition : IVersioned`, Seeds und Endpunkten. Der Client bekommt `ThemeService` (lädt `GET /api/v1/themes/current`, User-Setting `Theme.Mode` und `Theme.Id`), `AuditTimeline`, `VersionHistory` und Admin-Seiten; der Theme-Editor nutzt `MudExThemeEdit` aus MudBlazor.Extensions.

**Tech Stack:** wie P0.3a, plus MudBlazor.Extensions `MudExThemeEdit`.

**Spec:** `docs/superpowers/specs/2026-10-01-p0-foundation-design.md` Abschnitt 4 (Audit-UI, Snapshots/Versionierung, Themes).

## Global Constraints

- Permissions: `Audit.View`, `Themes.Manage`; Versionen eines Typs sehen/wiederherstellen verlangt die beim Typ registrierte Permission.
- Audit-API filtert immer auf den aktuellen Tenant (Einträge mit `TenantId == null` nur für Admins der System-Rolle).
- Snapshots enthalten keine als `IsSensitive`/`[Sensitive]` markierten Properties.
- Restore erzeugt eine neue Version (keine Historie wird überschrieben) und einen Audit-Eintrag mit Aktion `Restored`.
- `GET /api/v1/themes/current` ist anonym (Login-Branding) und liefert nur das Tenant-Default-Theme bzw. den globalen Default.
- `MailTemplateOverride` und `ThemeDefinition` sind versioniert.

## Review Focus

1. Audit-Abfrage eines fremden Tenants (Query-Parameter manipuliert) liefert nichts. Test in Task 2.
2. Restore einer Version eines Typs ohne Permission ⇒ 403; Restore eines gelöschten Aggregats ⇒ 404 bzw. definierte Wiederanlage. Test in Task 2.
3. Snapshot enthält kein `PasswordHash`/Secret-Feld. Test in Task 1.
4. Theme mit kaputtem JSON / unbekannten Farben wird mit 400 abgelehnt statt den Client zu zerstören. Test in Task 3.
5. Anonymer Zugriff auf `/themes/current` liefert kein CustomCss mit `</style>`-Ausbruch (Sanitisierung). Test in Task 3.

---

### Task 1: Versioning in Coworkee.Infrastructure

**Files:** Modify `coworkee/src/Coworkee.Domain/Markers.cs` (`IVersioned { int Version { get; set; } }` – Name kollidiert nicht mit `IHasConcurrencyToken.Version`? Falls doch: `int RevisionNumber`), Create `coworkee/src/Coworkee.Infrastructure/Versioning/{EntitySnapshot,SnapshotInterceptor}.cs`, Modify `CoworkeeModelContributor` (Tabelle `cw.EntitySnapshots`, Index `(EntityType, EntityId, Revision)` unique), `DbContextServiceCollectionExtensions` (Interceptor nach AuditTrail); Test `coworkee/tests/Coworkee.Infrastructure.Tests/SnapshotTests.cs`.

**Interfaces:** `IVersioned { int Revision { get; set; } }`; `EntitySnapshot { Id, TenantId?, EntityType, EntityId, Revision, Payload (jsonb), CreatedAt, CreatedBy }` (`[NotAudited]`).

- [ ] Tests: `Creating_versioned_entity_writes_revision_1`, `Each_update_increments_revision_and_snapshots`, `Unchanged_save_writes_no_snapshot`, `Sensitive_properties_are_not_in_payload`, `Delete_writes_final_snapshot_marked_deleted`.
- [ ] rot → Implementierung → grün → Commit `feat(infrastructure): entity snapshots for versioned aggregates`.

### Task 2: Coworkee.Auditing

**Files:** Create `coworkee/src/Coworkee.Auditing/**`, `coworkee/src/Coworkee.Contracts/Auditing/*.cs`; Test `coworkee/tests/Coworkee.Auditing.Tests/**`; Modify `Coworkee.Mailing` (Registrierung `MailTemplateOverride` als versionierter Typ, Permission `Mail.Templates.Manage`).

**Interfaces:**
- Contracts: `AuditPermissions.View`, `AuditEntryDto(Id, EntityType, EntityId, Action, ActorId, ActorName?, OccurredAt, CorrelationId, Changes: AuditChangeDto(Property, OldValue, NewValue)[])`, `AuditQuery(EntityType?, EntityId?, ActorId?, From?, To?, Page, PageSize)`, `EntityVersionDto(Revision, CreatedAt, CreatedBy, CreatedByName?, IsDeleted)`, `EntityVersionDetailDto(Revision, Payload: JsonElement)`.
- `IVersionedTypeRegistry.Register<T>(string name, string permission)`; Contributor-Interface `IVersionedTypeContributor`.
- Endpunkte: `GET /api/v1/audit` (Audit.View), `GET /api/v1/versions/{type}/{id}`, `GET /api/v1/versions/{type}/{id}/{revision}`, `POST /api/v1/versions/{type}/{id}/{revision}/restore` (Typ-Permission).
- Akteur-Namen über `IUserDirectory.GetDisplayNamesAsync(ids)` (Erweiterung des Interfaces aus P0.3a).

- [ ] Tests: `Audit_lists_changes_of_current_tenant_only`, `Audit_filters_by_entity_actor_and_time`, `Audit_requires_permission`, `Versions_list_revisions_newest_first`, `Restore_applies_payload_as_new_revision_and_audits_restored`, `Restore_without_type_permission_is_forbidden`, `Unknown_type_is_not_found`.
- [ ] rot → grün → Commit `feat(auditing): audit query, version history and restore`.

### Task 3: Coworkee.Theming

**Files:** Create `coworkee/src/Coworkee.Theming/**`, `coworkee/src/Coworkee.Contracts/Theming/*.cs`; Test `coworkee/tests/Coworkee.Theming.Tests/**`.

**Interfaces:**
- `ThemeDefinition : AuditedAggregateRoot, IVersioned { Name, TenantId?, IsDefault, PaletteLight (json), PaletteDark (json), Typography (json?), LayoutProperties (json?), LogoSvg?, CustomCss? }`.
- Seeds beim ersten Start (global): `Coworkee` (Paper/Ink/Signal aus dem Icon), `Classic` (MudBlazor-Standard), `High Contrast`; genau ein globaler Default.
- Validierung: JSON parsebar, Farbwerte `#rgb|#rrggbb|#rrggbbaa|rgb(a)()`, `LogoSvg` ohne `<script`/`on*=`-Attribute, `CustomCss` ohne `</style`, `@import`, `expression(`; Größenlimits.
- Endpunkte `/api/v1/themes`: `GET /` (angemeldet: globale + Tenant-Themes), `GET /current` (anonym), `POST /`, `PUT /{id}`, `DELETE /{id}`, `POST /{id}/default` (Themes.Manage; Default je Tenant).
- Settings (User-Scope, client-sichtbar): `Theme.Mode` (Choice `system|light|dark`), `Theme.Id` (String).
- Versionierter Typ `Theme` mit Permission `Themes.Manage`.

- [ ] Tests: `Seeds_three_global_themes_with_one_default`, `Current_is_anonymous_and_prefers_tenant_default`, `Invalid_palette_json_is_rejected`, `Unsafe_css_and_svg_are_rejected`, `Setting_default_unsets_previous_default`, `Theme_changes_are_versioned`, `Manage_requires_permission`.
- [ ] rot → grün → Commit `feat(theming): theme definitions with seeds, validation and api`.

### Task 4: Client

**Files:** Modify `coworkee/src/Coworkee.Client.Blazor/**`: API-Methoden, `ThemeService` (MudTheme aus DTO, Modus aus User-Setting oder `prefers-color-scheme`, Event `Changed`), `CoworkeeLayout` nutzt `ThemeService` statt `CoworkeeTheme.Default`, Komponenten `AuditTimeline(EntityType, EntityId)`, `VersionHistory(Type, Id, OnRestored)`, Seiten `/admin/audit` (Filter, Tabelle, Diff-Dialog), `/admin/themes` (Liste, Default setzen), `/admin/themes/{id}` (MudExThemeEdit + Live-Vorschau + VersionHistory), Mail-Editor bekommt `VersionHistory`; Tests `ThemeServiceTests`, `AuditTimelineTests`, `VersionHistoryTests`.

- [ ] Tests: `Applies_current_theme_and_user_mode`, `Falls_back_to_default_when_api_fails`, `Timeline_shows_changes_with_old_and_new_values`, `Restore_calls_api_and_raises_event`.
- [ ] rot → grün → Commit `feat(client): audit log, version history and theme editor`.

### Task 5: Shareme-Integration

**Files:** `ShareMeDatabaseModule` DependsOn Auditing + Theming, Migration `AuditVersionsThemes`, Auth-Login-Seite nutzt Theme-Farben aus `/api/v1/themes/current` (Auth host liest Theme direkt aus der DB über `CoworkeeThemingModule` ohne Web-Endpunkte), E2E: nach Login `GET /api/v1/themes/current` über BFF liefert `Coworkee`; Admin ändert Theme ⇒ Audit-Eintrag und Version 2 sichtbar.

- [ ] rot → grün → Commit `feat: shareme audit log, versions and themes`.
