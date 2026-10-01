# Sharedien Frontend – vollständige Feature-Analyse (Angular)

Quelle: `C:\dev\privat\github\sharedien\src\Frontend` (Stand der Sourcen: April 2021, Version `sharedien.web` 3.0.0).
Zweck: lückenloses Inventar aller UI-Features aus Nutzersicht als Grundlage für den Blazor-Nachbau **Shareme**.

Konventionen in diesem Dokument:
- Pfade sind relativ zu `src/Frontend/src` (z. B. `app/assets/assets.component.ts`).
- Berechtigungen werden als `ObjectTypePermissions.X` (Enum in `app/models/object-type-permission.ts`, 316 Werte) angegeben.
- "unklar" = im Frontend-Code nicht eindeutig feststellbar (z. B. serverseitiges Verhalten).

---

## Inhaltsverzeichnis

1. Tech-Stack und Build
2. Modulstruktur und Bootstrapping (Startup-Sequenz, Auth)
3. Laufzeit-Konfiguration (`configuration.js`)
4. Routing – alle Routen
5. Berechtigungsprüfung im Frontend (Guards, Direktiven, Services)
6. App-Shell (Topbar, Sidebar, Bottombar, Overlays)
7. Asset-Browser, Asset-Detail, Upload, Download, Vorschau, Suche, Kategorien, Sammlungen, Warenkorb
8. Workspaces, Tasks/Kanban, Custom Entities, Sharing, Kommentare, Aktivität, Metadaten-Editor, Relationen, Entity-Permissions, Dashboard/Tiles/Reports, Benutzerprofil/Einstellungen
9. Admin-Bereich und Lizenz-/DRM-Modul
10. Komponenten-Inventar (Controls, Dialoge, Popups, Direktiven, Pipes, Services, State, Models, Styles)
11. Realtime (SignalR)
12. State-Management und API-Client (Überblick)
13. Theming, Branding, Styling
14. Internationalisierung (UI-Sprache vs. Datensprache)
15. Tests (e2e, Unit)
16. Hosting/Deployment
17. Kennzahlen und Komplexitätsbewertung

---

## 1. Tech-Stack und Build

| Bereich | Technologie / Version | Anmerkung |
|---|---|---|
| Framework | Angular 9.1 (`@angular/core ^9.1.6`), TypeScript 3.8, RxJS 6.5 | AOT-Build, `es5`-Konfiguration für IE11 (`tsconfig-es5.app.json`), `core-js`, `web-animations-js`, `css-vars-ponyfill` |
| State-Management | NGXS 3.6 (`@ngxs/store`, `@ngxs/router-plugin`, `@ngxs/devtools-plugin`) | Redux-Pattern; sehr viele States (siehe Kap. 10/12) |
| UI-Bibliothek | Kendo UI for Angular (Buttons, Charts, DateInputs, Dropdowns, Excel-Export, Grid, Inputs, Intl, L10n, Layout, PDF-Export, Popup, ProgressBar, Data-Query, Drawing) + `@progress/kendo-theme-material` | Grid/Listen-Ansichten, Dropdowns, Popups, Datumseingaben, Excel-Export von Grids |
| Icons | Font Awesome Pro 5.3 (fontawesome, brands, light, regular, solid) | privates npm-Registry-Token in `.npmrc` |
| Auth | `angular-oauth2-oidc` 9.2 (OIDC Authorization Code Flow + PKCE unklar, Silent Refresh via `auth-silent-token-refresh.html`) | IdP = "Sharedien UAA" (`services/identity-provider/sharedien-uaa-api.service.ts`) |
| Realtime | `@microsoft/signalr` 3.1 | Ein Hub-Event `Publish` (siehe Kap. 11) |
| Baum | `angular-tree-component` 8.5.6 | Kategoriebaum, Property-Gruppen etc. |
| Toasts | `angular2-notifications` 9 | Position top/center |
| Bildbearbeitung | `cropperjs` 1.5 | Asset-Crop-Dialog |
| Video/Audio | `video.js` 7.7 | `controls/audio-video-element` |
| PDF | `ngx-extended-pdf-viewer` 4.1 (pdf.js) | `controls/pdf-element`; Assets aus `node_modules/ngx-extended-pdf-viewer/assets` werden nach `/assets/` kopiert |
| Rich-Text | `roosterjs` 8 | `rich-text-editor/` für RichText-Properties |
| Graph | `d3` 5.16 | `controls/relation-graph` (Relationsgraph) |
| Diff | `scripts/diff_match_patch.js` (Google diff-match-patch, global) | Versions-/Property-Vergleich (`entity-properties-compare`, `asset-version-compare-dialog`) |
| i18n | `i18next` 11 + `i18next-xhr-backend` + `i18next-browser-languagedetector` | Eigene `I18NextModule` mit Pipe `t` und Direktive `[t]` (siehe Kap. 14) |
| Sonstiges | `hammerjs`, `popper.js`, `uuid`, `normalize.css`, `faker` (dev) | |
| Tests | Karma/Jasmine (44 Spec-Dateien), Protractor (1 Platzhalter-Spec) | siehe Kap. 15 |
| Lint | TSLint + codelyzer | |
| Doku-Widget | Document360 Web Assistant (`scripts/document360_web_assistant_integration.js`) | wird nur bei konfiguriertem API-Key geladen; Button wird bei offenem Dialog/Lightbox ausgeblendet |

**Build-Konfigurationen (angular.json):** `local`, `development`, `production` (optimization, outputHashing, extractCss, buildOptimizer), `es5`. Assets: `src/assets`, `src/locales`, `src/fonts`, `favicon.png`, `version.json` (von CI erzeugt), `Web.config`, `rewrite.rules`, `auth-silent-token-refresh.html`, `configuration/configuration.js`, `configuration/configuration.overrides.js`, `configuration/themes/*`.
Globale Styles: `normalize.css`, `angular-tree-component.css`, `styles/styles.scss`, FA-Pro-CSS, `video-js.min.css`, `cropper.min.css`. Globale Scripts: `video.min.js`, `diff_match_patch.js`, Document360-Script.
npm-Scripts: `start` (Dev-API, es5), `start:local`, `build:development`, `build:production(-with-stats)`, `test`, `test-ci`, `lint`, `e2e`, `show-bundle-report`.

**Kennzahlen Quellcode:** 2 118 `.ts`-Dateien, 329 `*.component.ts`, 331 HTML-Templates, ~3 950 Zeilen Übersetzungen pro Sprache (155 Top-Level-Namespaces), API-Client 4 452 Zeilen (`services/api/sharedien-api.service.ts`) + 2 413 Zeilen abstrakte Basis (`services/api/api.service.ts`).

---

## 2. Modulstruktur und Bootstrapping

### 2.1 Module

| Modul | Datei | Inhalt |
|---|---|---|
| `AppModule` | `app/app.module.ts` | Root; importiert `OAuthModule.forRoot()`, `NgxsModule.forRoot(states)`, `NgxsRouterPluginModule`, `NgxsReduxDevtoolsPluginModule`, `I18NextModule.forRoot()`, `SimpleNotificationsModule.forRoot()`, `TreeModule.forRoot()`; Provider: `GlobalErrorHandler` (ErrorHandler), `OAuthStorage`, `CustomRouteReuseStrategy`, `CustomRouterStateSerializer`, HTTP-Interceptors `ApiAuthInterceptor` + `NoCacheApiInterceptor`, `ApiService → SharedienApiService`, `ApiDownloadService → SharedienApiDownloadV1Service`, `IdentityProviderApiService → SharedienUaaApiService`, `StorageService → LocalStorageService`, `UploadService → SharedienUploadService`, `UserApiService → UserManagementService`, `LOCALE_ID`, zwei `APP_INITIALIZER` (i18next, App-Initialisierung) |
| `AppRoutingModule` | `app/routing/app-routing.module.ts` | Alle Hauptrouten (eager), Admin lazy (`loadChildren`) |
| `AdminModule` + `AdminRoutingModule` | `app/admin/admin.module.ts`, `admin-routing.module.ts` | Lazy-Loaded Admin-Bereich mit eigenem `feature.state.ts` |
| `SharedModule` | `app/shared/shared.module.ts` | Alle wiederverwendbaren Controls, Dialoge, Pipes, Direktiven, Kendo-Module |
| `I18NextModule` | `i18next/i18next-module.ts` | Pipe `t`, Direktive `[t]`, `I18NextService` |

Ordnerstruktur `src/`: `app/` (Features), `services/` (API, Auth, Dialog, Popup, Lightbox, Upload, DragDrop, Storage, Theme, SignalR, Logger), `providers/` (APP_INITIALIZER-Factories), `i18next/`, `locales/{de,en}/translation.json`, `configuration/` (Runtime-Config + Themes), `styles/` (SCSS), `models/` (globale Typen), `typings_custom/` (Ambient-Typings für Config, Theme, video.js, diff-match-patch), `polyfills/`, `fonts/roboto`, `assets/` (Logos, Icons).

### 2.2 Startup-Sequenz (`providers/app-initialization-service.provider.ts`)

1. `configuration.js` + `configuration.overrides.js` werden in `index.html` als globale Variable `sharedienAppConfig` geladen (kein Rebuild nötig; zur Laufzeit in der Konsole änderbar).
2. `APP_INITIALIZER` i18next: `fallbackLng: 'en'`, `whitelist: ['en','de']`, `load: 'languageOnly'`, Namespaces `translation` + `override` (Override-Bundle wird per Deep-Merge in `translation` gemischt – kundenspezifische Textanpassungen ohne Rebuild; bei Sprachwechsel erneut gemerged).
3. `APP_INITIALIZER` App-Init:
   - Wenn URL mit dem Public-Share-Pfad beginnt (`/shares/:token`): **keine Authentifizierung**, nur `ThemeService.initialize()`.
   - Sonst: `AuthConfig` aus `sharedienAppConfig.authentication` (Issuer, ClientId `sharedien-web`, Scope `openid email profile profile.extended sharedien-api offline_access`, `responseType: 'code'`, Silent-Refresh-URI), Discovery-Document laden, `tryLoginCodeFlow()`, bei fehlendem Token `initCodeFlow()` (Redirect zum IdP). Query-Param `external=true` erzwingt manuellen Login (Logout + Redirect), `defaultExternalIdentityProvider` setzt `acr_values=idp:<name>` (Auto-Weiterleitung an externen IdP). Token-Validierung clientseitig deaktiviert (`DummyTokenValidationHandler`). Danach `loadUserProfile()` → `SessionUser` (Claim aus `userIdClaim`, Erkennung anonymer Benutzer über `anonymousAccess.userName`) → `userService.login()`.
   - `ThemeService.initialize()` (siehe Kap. 13).
   - `SessionState.GetInitialData`: lädt parallel Data-Languages, Forbidden-File-Types, danach `getMyProfile`, `getUserContexts`, `getEntityTypes` (aktive), `getAllPropertyDefinitions`, `getAllPropertyGroups`. User-Settings werden in den State übernommen (bevorzugte Datensprache, UI-Sprache, Locale, User-Context, Theme, Favoriten-Kategorien, gewählte Property-Sets).
   - Theme aus Benutzerprofil anwenden (nur wenn `userCanSwitchThemes`).
   - Navigation: Custom-Entity-Typen werden als Sub-Navigation unter "Data" eingehängt; in `uiSpecification.sidebar.pinnedCustomEntities` konfigurierte Typen erscheinen als eigene Hauptmenüpunkte (mit konfigurierbarem FA-Icon).
4. `AppComponent.ngOnInit`: Favicon setzen, Anker für Dialog/Popup/Lightbox setzen, **SignalR-Verbindung starten**, Seitentitel aus Route (`displayNameTranslationKey`), Loading-Overlay während Navigation.
5. `RootAuthGuard` auf `''`: leitet zur ersten Hauptnavigation, für die der Benutzer Berechtigung hat (Dashboard wird übersprungen, wenn es weitere gibt); wirft `ClientError` (`ApiNotAvailable` / `NoPermissions`) → Error-Page.

### 2.3 Fehlerbehandlung

- `GlobalErrorHandler` (ErrorHandler-Provider) + `error-page/` (Route `/error`); `ClientErrorType` u. a. `MissingPermissions`, `NoPermissions`, `ApiNotAvailable`; Fehler wird mit `ErrorSourceUrl` angereichert.
- `ApiResponse<T>` mit `success`/`value`; UI-Toasts über `UiNotificationsState.lastMessage` → `ToastNotificationService` (Success/Error/Warning).
- `LoggerService` (`ConsoleLoggerService`) mit konfigurierbarem `logLevel` (0–4).

---

## 3. Laufzeit-Konfiguration (`configuration/configuration.js`)

Alle Werte sind ohne Rebuild änderbar (globales Objekt `sharedienAppConfig`, Typdefinition in `typings_custom/configuration`). Für Shareme entspricht das einer `appsettings`/Tenant-Konfiguration.

| Schlüssel | Bedeutung |
|---|---|
| `logLevel` | 0 SILENT … 4 DEBUG |
| `apiUrl`, `hubUrl`, `prependBearer` | API-Basis-URL, SignalR-Hub-URL (leer = Realtime deaktiviert), Bearer-Prefix im `access_token`-Query-Param |
| `enforceNoCachingOnApiCalls` | `NoCacheApiInterceptor` (IE11) |
| `authentication.identityProviderUrl`, `identityProviderLogoutEndpoint` (`connect/endsession`), `issuerEndpoint`, `discoveryDocumentEndpoint`, `requireHttps`, `skipIssuerCheck`, `clientId`, `userIdClaim` (`sub`), `scope`, `defaultExternalIdentityProvider`, `userCanChangePassword`, `userCanLogout`, `anonymousAccess.{isEnabled,userName}` | OIDC-Setup; Passwort-Änderung und Logout-Button abschaltbar; anonymer Zugang über technischen Benutzer |
| `branding.theming.{themes[], defaultTheme, userCanSwitchThemes}` | Custom-Themes (`{id,name,extends}`), Default `sharedien_light_theme`, Theme-Wahl in User-Settings freischaltbar |
| `branding.sidebar.appLogo.url`, `branding.topbar.{brandLogoUrl, headerText, appLogo.url}`, `branding.bottombar.links[]`, `branding.faviconUrl` | Logos, Kopfzeilentext ("Digital Asset Management"), Footer-Links, Favicon |
| `uiSpecification.entityCardInfoIconEnabled`, `entityCardExpiryBannerThresholdDays` (90) | Info-Icon auf Karten; Banner "läuft ab" auf Asset-Karten ab X Tagen vor Ablauf |
| `uiSpecification.avatar.maxFileSizeBytes` (256 000) | Profilbild-Upload |
| `uiSpecification.parentChildRelationDeleteBehavior` (`DeleteAssetSet`) | Verhalten beim Löschen von Parent-Child-Relationen |
| `uiSpecification.archiveExtractionSettings.{archiveExtractionMode: 'ExtractWithFullPath', deleteAssetAfterExtraction: true}` | Defaults für ZIP-Extraktion |
| `uiSpecification.paging.pageSizes` `[50,100,200,500]` | Page-Sizes aller Listen |
| `uiSpecification.urlMaxLength` (2000) | Schutz gegen zu lange Such-URLs |
| `uiSpecification.sidebar.pinnedCustomEntities[]` `{entityTypeIdentifier, icon}` | Custom-Entity-Typen als Hauptmenü |
| `integration.smintIo.smintIoSearchUrl` | Button "Stock-Content bei Smint.io suchen" im Asset-Browser (öffnet URL mit `?query=<Suchbegriff>` in neuem Tab; nur mit Berechtigung `canSearchInExternalProviders`) |
| `integration.document360.apiKey` | Hilfe-Widget |
| `drm.purchaseHelpLink.{link,text}` | Hilfe-Link im Lizenz-Kaufdialog |
| `support.emailAddress` | Support-Kontakt (Error-Page / About; unklar wo genau) |

---

## 4. Routing – alle Routen

Routen-Definition: `app/routing/routes.ts` (Konstanten), `app/routing/app-routing.module.ts` (Root), `app/admin/admin-routes.ts` + `admin-routing.module.ts` (Admin, lazy). Jede Route hat `displayNameTranslationKey` (Seitentitel), `shouldReuse` (überall `false` → `CustomRouteReuseStrategy` cached nichts), optional `permissions`.

### 4.1 Hauptrouten

| Route | Komponente | Guards / Resolver | Zweck |
|---|---|---|---|
| `/` | `AppComponent` | `RootAuthGuard` | Weiterleitung auf erste erlaubte Hauptseite |
| `/error` | `ErrorPageComponent` | – | Fehlerseite (fehlende Rechte, API nicht erreichbar, allgemeine Fehler) |
| `/assets` | `AssetBrowserComponent` | `AuthGuard`, `AssetBrowserDeactivateGuard`, `AssetBrowserResolver` | Zentraler Asset-Browser (Suche, Kategorien, Grid/Liste, Massenaktionen) |
| `/custom-entities` | `CustomEntityBrowserComponent` | `AuthGuard` | Browser für generische Datenobjekte (alle Typen) |
| `/custom-entities/:id` | `CustomEntityBrowserComponent` | `AuthGuard`, `CustomEntityBrowserResolver` | Browser für einen Custom-Entity-Typ |
| `/collections` | `CollectionBrowserComponent` | `AuthGuard`, `CollectionBrowserResolver` | Sammlungen (Workspace-Typ "Collection") |
| `/workspaces` | `WorkspaceBrowserComponent` | `AuthGuard`, `WorkspaceBrowserResolver` | Workspace-Übersicht |
| `/workspaces/create`, `/workspaces/create/:templateId` | (Dialog-Route, siehe Workspace-Kapitel) | – | Workspace anlegen (aus Template) |
| `/license-purchases` | `LicensePurchaseBrowserComponent` | `AuthGuard`, Resolver | Eigene Lizenzkäufe |
| `/tasks` | `TaskBrowserComponent` | `AuthGuard` | Aufgaben-Übersicht (alle Workspaces) |
| `/basket` | `BasketComponent` | `AuthGuard` | Download-Warenkorb |
| `/upload-management` → Kinder `sessions`, `history` | `UploadManagementComponent` → `UploadSessionsComponent`, `UploadHistoryComponent` | `AuthGuard`, `UploadManagementResolver`, `UploadSessionsResolver` | Upload-Sessions (Workspaces vom Typ Upload) und Upload-Historie |
| `/dashboard` | `DashboardComponent` | `AuthGuard` | Kachel-Dashboard (Jobs, Duplikate) |
| `/asset-derivation-job-report/:id` | `AssetDerivationJobReportComponent` | Resolver | Report eines Derivations-Jobs (pro Asset) |
| `/mass-edit-job-report/:id` | `MassEditJobReportComponent` | Resolver | Report Massenbearbeitung |
| `/mass-validation-job-report/:id` | `MassValidationJobReportComponent` | Resolver | Report Massenvalidierung |
| `/archive-extraction-job-report/:id` | `ArchiveExtractionJobReportComponent` | Resolver | Report ZIP-Extraktion |
| `/property-import-job-report/:id` | `PropertyImportJobReportComponent` | Resolver | Report Metadaten-Import |
| `/property-extraction-job-report/:id` | `PropertyExtractionJobReportComponent` | Resolver | Report Metadaten-Extraktion |
| `/derivation-job-report/:assetTypeId/:derivationTypeId` | `DerivationJobReportComponent` | Resolver | Report Derivate je Asset-Typ/Derivations-Typ (mit Selektion, Regenerieren) |
| `(print-outlet:print/metadata-export)` | `PrintLayoutComponent` → `EntityPropertiesPrintComponent` | `AuthGuard` | Named Outlet für Druck-Layout (Metadaten-Export/Contact-Print) |
| `/user-settings` | `UserSettingsComponent` | `AuthGuard`, `UserSettingsDeactivateGuard` | Benutzereinstellungen |
| `/user-profile` | `UserProfileComponent` | `AuthGuard` | Profil (Avatar, Passwort) |
| `/asset/:id` → Kinder `metadata` (Default), `category-properties`, `versions`, `formats`, `relations`, `license-purchases` | `AssetComponent` → `AssetPropertiesEditComponent`, `AssetCategoryPropertiesComponent`, `AssetVersionsComponent`, `AssetDerivationsComponent`, `AssetRelationsComponent`, `AssetLicensePurchaseBrowserComponent` | `AuthGuard`, `AssetResolver`, `AssetPropertiesEditDeactivateGuard`, Resolver je Tab | Asset-Detailseite mit Tabs |
| `/custom-entity/:entityTypeId/:entityId` → Kinder `assets`, `metadata`, `relations`, `licenses` | `CustomEntityComponent` → `CustomEntityAssetsComponent`, `CustomEntityPropertiesEditComponent`, `EntityRelationsComponent`, `CustomEntityLicenseOffersComponent` | `AuthGuard`, Resolver, `CustomEntityPropertiesEditDeactivateGuard` | Detailseite Custom Entity |
| `/workspace/:id` → Kinder `assets` (Default), `tasks`, `template-permissions`, `shares`, `metadata` | `WorkspaceComponent` → `WorkspaceAssetsComponent`, `WorkspaceTasksComponent`, `WorkspaceTemplatePermissionsComponent`, `WorkspaceSharesComponent`, `WorkspacePropertiesEditComponent` | `AuthGuard`, `WorkspaceResolver`, `WorkspaceAssetsResolver`, `TaskEditDeactivateGuard`, `WorkspaceTemplatePermissionsDeactivateGuard`, `WorkspacePropertiesEditDeactivateGuard` | Workspace-Detail (Assets, Kanban, Rechte, Shares, Metadaten) |
| `/workspace/upload/:id` | `WorkspaceUploadComponent` | `AuthGuard`, `WorkspaceUploadResolver` | Upload-Wizard einer Upload-Session |
| `/shares/:token` | `PublicShareComponent` | `PublicShareResolver` (Tenant) – **ohne AuthGuard** | Öffentliche Freigabe-Seite |
| `/metadata-export` | (Kind von `print-outlet`) | | Druckansicht Metadaten |
| `/saved-search-management` | – (nur Route-Konstante, keine Registrierung gefunden) | | unklar / nicht verdrahtet |
| `/pg` | `PlaygroundComponent` | nur `!production` | Entwickler-Spielwiese (Relation-Graph, Controls) |

**Frontend-Query-Parameter** (`app/routing/frontend-query-params.ts`):
`c` (Kategorie, deprecated → `sq_c`), `t` (Task), `ss` (Saved Search), `external` (manueller Login), `r` (Rolle), `ds` (Derivation-Status), `vm` (View-Mode), `ro` (Rights-Owner), `pl` (Price-List), `li` (License).
Such-Parameter: `sq_q` (Suchbegriff), `sq_af` (Advanced Filter), `sq_ff` (Facet Filter), `sq_so` (Sort), `sqo_op` (Operator), `sqo_ln` (Sprache), `sqo_es` (Exakte Suche), `sq_c` (Kategorie), `sqo_rfc` (rekursiv in Unterkategorien), `sqo_sd` (Suche in Dokumentinhalten), `sq_et` (Entity-Typ), `sq_st` (Templates suchen). Suchzustand ist damit vollständig per URL teilbar/bookmarkbar; `urlMaxLength` begrenzt.

### 4.2 Admin-Routen (lazy `AdminModule`, Basis `/admin`)

Alle Admin-Routen nutzen `CheckRoutePermissionsAuthGuard` (Ausnahme: `about`, `plugins` nur `AuthGuard`). Notation: `AND[...]`/`OR[...]` = `PermissionsAuthorizationFactory.multipleAnd/multipleOr`.

| Route | Komponente | Berechtigung |
|---|---|---|
| `/admin` | `AdminComponent` (Admin-Navigation/Sub-Header) | `AdminView` (Hauptmenü) |
| `/admin/category-management` | `CategoryManagementComponent` | `CategoryView` |
| `/admin/user-management` → `users` (Default), `user-groups`, `registrations` | `UsersAndGroupsComponent` → `UserManagementComponent`, `UserGroupManagementComponent`, `RegistrationManagementComponent` | OR[`UserView`,`UserGroupView`]; Kinder AND[`AdminView`, `UserView`/`UserGroupView`] |
| `/admin/user-management/users/create`, `/users/:id`, `/registrations/:id` | `UserEditComponent` | `UserCreate` / `UserUpdate` |
| `/admin/user-management/user-groups/create`, `/user-groups/:id` | `UserGroupEditComponent` | `UserGroupCreate` / `UserGroupUpdate` |
| `/admin/context-management`, `/create`, `/:id` | `UserContextManagementComponent`, `UserContextEditComponent` | `AdminView`; `UserContextCreate` / `UserContextUpdate` |
| `/admin/security` → `role-management` (Default), `permission-management` | `SecurityComponent` → `RoleManagementComponent`, `PermissionManagementComponent` | OR[`RoleView`,`NodePermissionView`]; AND[`AdminView`,`RoleView`] / AND[`AdminView`,`NodePermissionView`] |
| `/admin/security/role-management/create`, `/copy`, `/:id` | `RoleEditComponent` | (Create/Copy/Update; Permission im Code unklar/`RoleCreate`/`RoleUpdate`) |
| `/admin/tenant-management`, `/create`, `/:id` | `TenantManagementComponent`, `TenantEditComponent` | AND[`AdminView`,`TenantView`]; `TenantCreate` / `TenantUpdate` |
| `/admin/app-settings` → `external-groups` (+`/create`, `/:id`) | `AppSettingsComponent` (Tabs: Assets, Contact-Print, E-Mail, File) → `ExternalGroupManagementComponent`, `ExternalGroupEditComponent` | `ApplicationSettingsView`; `ApplicationSettingsCreate`/`Update`. Die Tab-Routen `contact-print-metadata-configuration`, `file-configuration`, `email-configuration`, `assets-configuration` sind im Router auskommentiert (Tabs innerhalb der Komponente) |
| `/admin/configuration` → `data-languages`, `relation-types`, `search` (+`search/:id` Re-Indexing-Report), `materialization`, `watermarks`, `file-naming-definitions`, `entity-types` (je + `/create`, `/:id`) | `ConfigurationComponent` → `DataLanguageManagement`, `RelationTypeManagement`, `SearchManagement`, `ReIndexingJobReport`, `PermissionMaterialization`, `WatermarksManagement`, `FilenamePatternManagement`, `EntityTypesManagement` + Edit-Komponenten | OR[`DataLanguageView`,`RelationTypeView`,`ReIndexingJobCreate`,`WatermarkView`,`FileNamingDefinitionView`,`EntityView`]; je AND[`AdminView`, `XView`]; Create/Update: `DataLanguageCreate/Update`, `RelationTypeCreate/Update`, `FileNamingDefinitionCreate/Update`, `EntityCreate/Update`; `PermissionMaterializationJobView` |
| `/admin/property-management` → `definitions` (Default), `property-groups`, `property-extraction-definitions` (je + `/create`, `/:id`) | `PropertiesComponent` → `PropertyDefinitionManagement`, `PropertyGroupManagement`, `PropertyExtractionDefinitionManagement` + Edit | OR[`PropertyDefinitionView`,`PropertyGroupView`]; `PropertyDefinitionCreate/Update`, `PropertyGroupCreate/Update`, `PropertyExtractionDefinitionView/Create/Update` |
| `/admin/derivation-management` → `derivation-types`, `derivation-job-definitions`, `watermark-definitions` (je + `/create`, `/:id`), `file-types` | `DerivationManagementComponent` → `DerivationTypeManagement`, `DerivationJobDefinitionManagement`, `WatermarkDefinitionManagement`, `FileTypeManagement` + Edit | OR[`DerivationTypeRead`,`DerivationJobDefinitionView`]; `DerivationTypeCreate/Update`, `DerivationJobDefinitionCreate/Update` |
| `/admin/rule-management`, `/create`, `/:id` | `RuleManagementComponent`, `RuleEditComponent` | `RuleView`; `RuleCreate`; AND[`RuleView`,`RuleUpdate`] |
| `/admin/license-management` → `licenses` (Default), `price-lists`, `rights-owners`, `billing-contacts`, `permissions`, `user-context-groups` | `LicensesComponent` (Resolver `AdminLicensesResolver`) → `LicenseManagement`, `PriceListManagement`, `RightsOwnerManagement`, `BillingContactManagement`, `LicensePermissions`, `UserContextGroupManagement` | `AdminView`; je AND[`AdminView`,`LicenseView`]; Deactivate-Guards für ungespeicherte Änderungen (Licenses, PriceLists, RightsOwners, BillingContacts) |
| `/admin/plugins` | `PluginsComponent` | `AuthGuard` (Menüpunkt auskommentiert) |
| `/admin/about` | `AboutComponent` | `AuthGuard` |

**Hauptnavigation** (`app/state/navigation/navigation.state.ts`, Reihenfolge nach `order`):
Dashboard (`DashboardView`), Assets (`AssetBrowserView`), Collections (`CollectionView`), Basket (`DownloadBasketView`, mit Item-Count-Badge und **Drop-Ziel für Assets** → öffnet Derivat-Auswahl-Dialog), Uploads (`UploadView`), Tasks (`TaskView`), Workspaces (`WorkspaceView`), License Purchases (`LicensePurchaseRead`), Data/Custom Entities (`CustomEntityBrowserView`, Sub-Menü je Entity-Typ), gepinnte Custom Entities (`CustomEntityView`), Admin (`AdminView`, Sub-Menü: Tenant, Security, Users, Contexts, Categories, Properties, Derivations, Configuration, App-Settings, Rules, Licenses, About). Plugins ist auskommentiert.

---

## 5. Berechtigungsprüfung im Frontend

| Mechanismus | Datei | Verhalten |
|---|---|---|
| `BaseAuthGuard` | `app/routing/base-auth.guard.ts` | Prüft gültiges Access-Token (sonst `initCodeFlow` → IdP-Redirect); schließt offene Lightbox bei Navigation statt zu navigieren (Browser-Back schließt Lightbox). **Routen-Permission-Check ist hier auskommentiert** (TODO im Code: Kollision `assets` vs. `workspace/x/assets`). |
| `AuthGuard` | `app/routing/auth.guard.ts` | nur Token-Check (alle Hauptrouten) |
| `CheckRoutePermissionsAuthGuard` | `app/routing/check-route-permissions-auth.guard.ts` | Admin-Routen: prüft `RouteConfiguration.permissions` via `UserService.hasPermissions`; wirft `ClientError(MissingPermissions/NoPermissions)` → Error-Page |
| `RootAuthGuard` | `app/root-auth.guard.ts` | wählt Startseite nach Berechtigung |
| `BaseUnsavedChangesConfirmationGuard` | `app/routing/base-unsaved-changes-confirmation.guard.ts` | `CanDeactivate`: Bestätigungsdialog "ungespeicherte Änderungen verwerfen?" (umgehbar via `router.navigate(..., {state:{bypassGuards:true}})`). Ableitungen: AssetBrowser, AssetPropertiesEdit, CustomEntityPropertiesEdit, WorkspacePropertiesEdit, WorkspaceTemplatePermissions, TaskEdit, UserSettings, Licenses, PriceLists, RightsOwners, BillingContacts |
| `PermissionsAuthorization` + `PermissionsAuthorizationFactory.single/multipleAnd/multipleOr` | `app/models/permissions-authorization.ts`, `models/factory/permissions-authorization.factory.ts` | deklaratives Rechte-Objekt für Routen und Navigation |
| `*ifPermission="'role_create'"` | `app/directives/if-permission.directive.ts` | Strukturdirektive, `UserService.hasPermission(string)` |
| `*ifPermissions` | `app/directives/if-permissions.directive.ts` | Strukturdirektive mit `PermissionsAuthorization` (AND/OR) |
| `SessionState.currentUserPermissions` | `app/state/session-state` | abgeleitete Flags wie `canViewBasket`, `canSearchInExternalProviders` etc. (aus Profil-Permissions) |
| Entitätsbezogene Rechte | `entity-resource-permissions/`, `controls/entity-permission-table` | Node-Permissions pro Kategorie/Workspace/SavedSearch/Lizenz (Benutzer/Gruppen × Rechte), siehe Kap. 8 |
| Kategorie-Rechte | `admin/category-management/category-permission-management` | Rechte je Kategorie (Vererbung unklar/serverseitig), Materialisierung über `permission-materialization` |

Berechtigungs-Enum `ObjectTypePermissions` (316 Werte) folgt dem Muster `<ObjectType><Verb>` (View/Read/Create/Update/Delete/…); UI-spezifische Objekt-Typen: `Dashboard`, `AssetBrowser`, `DownloadBasket`, `Uploads`, `Admin` (siehe `ObjectType`-Enum in Kap. 10). Vollständige Liste siehe Kap. 9.

---

## 6. App-Shell

`app/app.component.html`:

```
root-container [.is-printing]
 ├─ app-topbar [showUserAccountMenu]      (Brand-Logo, Header-Text, Benutzer-Menü, Verbindungsstatus)
 ├─ main-container
 │   ├─ app-sidebar-nav                   (Hauptnavigation, Mini-Mode, Notifications-Glocke mit Unread-Badge, Sub-Menüs)
 │   └─ main > router-outlet
 ├─ app-bottombar                         (konfigurierbare Links, Version)
 ├─ app-upload-progress                   (globaler Upload-Fortschritt, Klick → /upload-management)
 ├─ dynamicComponentAnchor="FullScreenAnchor" (Vollbild-Komponenten)
 ├─ #dialogAnchor / #lightboxAnchor / #popupAnchor (dynamische Dialoge/Lightboxen/Popups)
 ├─ app-drag-hint                         (Drag-Vorschau beim Drag&Drop)
 ├─ app-loading-overlay (während Routing)
 └─ simple-notifications (Toasts top/center)
router-outlet name="print-outlet"        (Druck-Layout)
```

Besonderheit **Selection-Action-Bar**: Sobald in irgendeinem Browser Elemente selektiert sind oder ungespeicherte Änderungen existieren (Assets, Tasks, Workspaces, Collections, Upload-Sessions, Rollen, Benutzer, Gruppen, Shares, Permissions, Lizenzen, Preislisten, Rights-Owner, Billing-Contacts, Custom Entities, Derivation-Report …), werden Sidebar und Benutzer-Menü ausgeblendet und eine kontextuelle Aktionsleiste (`controls/focused-action-bar`, `controls/asset-selection-summary`, `controls/item-selection-summary`, `controls/property-editing-summary`) angezeigt. Beim Abschluss des Upload-Dialogs werden Assets in die Upload-Queue gestellt (`UploadManagementState.EnqueueAssetsForUpload`) und bei Upload in eine Session zum Wizard `/workspace/upload/:id` navigiert.

**Notifications-Popup** (`sidebar-nav/sidebar-nav-notifications`): Glocke mit Unread-Badge, Kendo-Popup mit Kopf (Gesamt/Ungelesen), Inhalt `app-activity-logs-inbox` (Klick navigiert zum betroffenen Objekt), Footer "Alle löschen" + Verbindungsstatus-Indikator (Connected/Connecting/Disconnected).


---

## 7. Asset-Browser, Asset-Detail, Upload, Download, Vorschau, Suche, Kategorien, Sammlungen, Warenkorb

### 7.1 Asset-Browser (`/assets`, `app/asset-browser`, `app/assets`)

`AssetBrowserComponent` ist eine dünne Hülle um die wiederverwendbare `AssetsComponent` (`app/assets/assets.component.ts`, 2 075 Zeilen TS + 999 Zeilen HTML). Dieselbe `AssetsComponent` wird mit anderen Flags in Workspace-Assets, Custom-Entity-Assets, Asset-Auswahl-Dialog, Upload-Wizard und Public Share eingebettet (Instanzierung über `stateInstanceKey`, NGXS-State `AssetsState` mit Instanzen je Browser).

**Konfigurations-Inputs der `AssetsComponent`** (bestimmen, was in der jeweiligen Einbettung erlaubt ist): `allowSearchWithoutSearchCriterias`, `loadLinkedTasks`, `canAddToWorkspace`, `canAddToCollection`, `canDelete`, `deleteAssetsConfiguration`, `uploadAssetsConfiguration`, `canDownload`, `canUnlink`, `canAddToTask`, `canFinalize`, `canUpload`, `canDownloadCategories`, `showUploadButton`, `canEdit`, `canEditCategories`, `canMoveAssets`, `canAddToBasket`, `canNavigate`, `canCrop`, `canViewRelations`, `canQuickSearch`, `loadDataOnInit`, `resetStateInstanceOnDestroy`, `includeRootInTree`, `rootCategoryNameOverride`, `rootCategoryIdentifierOverride`, `parentEntity`, `requiresAtLeastOneSelectedCategory`, `canCreateTask`, `canCreateWorkspace`, `canCreateCollection`, `canRegenerateDerivations`, `canExtractProperties`, `canAccessRoute`, `canManageCategoryFavorites`, `canManageCategorySettings`, `showSavedSearches`, `showTopActions`, `showCategories`, `showRecycleBin`, `canSearchInExternalProviders`, `initialSelectedFilters`, `hasFocusedActionBarMenu`. Outputs: `addToTask`, `taskActivated`, `finalize`, `createTask`, `navigationRequested`, `selection`.

#### Layout und Bereiche

| Bereich | Inhalt |
|---|---|
| Kopfzeile | Quick-Search-Box (Type-ahead ab 2 Zeichen, Gruppen: Asset-Vorschläge, Kategorien, Custom Entities; "mehr Treffer"-Hinweis), Smint.io-Button (optional), Button "Erweiterte Suche" (Vollbild-Overlay), Saved-Searches-Dropdown, "Clear", Upload-Button (Multi-File), Aktionsmenü (Bearbeiten per Suchabfrage = Massenbearbeitung, Excel-Import `.xls/.xlsx`, Derivate regenerieren per Suchabfrage, Properties extrahieren per Suchabfrage) |
| Filterzeile | Aktive Metadaten-Filter als Chips (`app-metadata-filter`, Klick = toggeln, X = entfernen), Facetten (`app-item-filter` je Facette, Multi-Select je nach Facette), `app-view-configuration` (Gesamtanzahl, Page-Size, Paging), User-Context-Auswahl, Data-Language-Auswahl (mit "Anwenden"-Bestätigung), View-Switch, Spaltenauswahl (nur Spreadsheet), Sortierung (nur Galerie) |
| Linke Spalte (resizable, Breite persistiert) | Auswahl-Zusammenfassung / Bearbeitungs-Zusammenfassung, Basket-Drop-Area (bei Selektion), Kategorie-Baum (`app-category-filter`), Papierkorb-Baum (`app-recycle-bin-filter`) |
| Hauptbereich | Galerie (Karten) oder Spreadsheet (Kendo-Grid, virtuelles Scrolling, Inline-Editing) oder Formular-Modus; "keine Treffer"-Hinweis; Loading-Overlay |
| Rechte Kachel | `app-entity-relations-tile` (Relationen des fokussierten Assets, inkl. Upload verwandter Assets) |
| Drag&Drop | Ganze Seite ist Drop-Ziel für Datei-Upload (`app-file-drop-handler`, nur mit `canUploadAssetSet`) |

#### Ansichtsmodi (`AssetsState.viewModeIdentifiers`)

| Modus | Query-Param `vm` | Beschreibung |
|---|---|---|
| Gallery | `gallery` | `app-entity-card`-Kacheln: Thumbnail (Watermark-Thumbnail, alternatives Hover-Thumbnail), Placeholder-Icon nach Asset-Typ, Primär-Text (konfigurierte Display-Property), Asset-Typ, Status beim Hover, Datum, Fokus/Selektion, Duplikat-Markierung, Gültigkeits-Banner (`toBeLaunched`/`willExpire`/`isExpired` + User-Context), Pflichtfeld-Füllstand (`propertyFillRate`), Task-Verknüpfungen (Kommentar-Anzahl, Task-Popup, Avatare) |
| Spreadsheet | `spreadsheet` | Kendo-Grid, virtuelles Scrolling (Zeilenhöhe 42), Checkbox-Spalte mit "alle auf Seite", Thumbnail-Spalte (draggable, Klick = Detail), dynamische Spalten aus Property-Set, sortierbar je Spalte, resizable, Inline-Editing (Edit-Mode) mit typabhängigen Editoren: Text, User (readonly), Boolean-Checkbox, Integer (NumericTextBox), Bytes (readonly, `fileSize`-Pipe), Decimal/Float, Date, Time, DateTime, Timespan, Liste (DropDownList mit Filter, "Create"-Button bei `allowCustomListValue`), Multi-Liste (MultiSelect mit Tag-Erstellung). Header zeigt Label, Kontext `[CTX]`, Sprache `[DE]`, Tooltip mit Beschreibung und Suchbarkeit |
| Form | `form` | öffnet `EntityPropertiesEditDialog` für die selektierten (sonst alle geladenen) Assets – Formular-Bearbeitung mehrerer Assets nacheinander |

#### Aktionen auf Asset-Karte (`controls/entity-card`)

Select/Deselect, Retry/Cancel (bei Upload), Download (`canDownloadAssets`), In Warenkorb, Details (Info-Icon, konfigurierbar), Relationen anzeigen (`canViewRelation`), Archiv extrahieren (`isExtractableArchive` + `canAddAssetSet`), Zuschneiden (`isImage` + `canCreateCropDerivation`), Löschen/Entfernen (`entityPermissions.canDeleteAssetSet`), Relation löschen (in Relations-Kontext), Task erstellen / zu Task hinzufügen; Klick = Aktivierung (Navigation zur Detailseite); Drag (Multi-Drag der Selektion, Drag-Hint "n Assets").

#### Selektion und Selection-Action-Bar (mehrere Assets)

| Aktion | Bedingung / Berechtigung |
|---|---|
| Alle auf Seite auswählen | – |
| In Warenkorb | `canDownloadAssets`, `canAddToBasket` → Derivat-Auswahl-Dialog |
| Download | `canDownloadAssets`, `canDownload` → Derivat-Auswahl-Dialog mit sofortigem ZIP-Download |
| Teilen (Share) | `canCreateWorkspace` → erstellt Workspace/Share (`onCreateShare`) |
| Finalisieren | `canFinalize` (Upload-Session) → Finalisierungs-Wizard |
| Bearbeiten (Metadaten) | alle selektierten Assets `canUpdate` + `canEdit` → Mehrfach-Bearbeitung |
| Zu Workspace / zu Collection hinzufügen | `canEditWorkspace` + `canViewWorkspace`/`canViewCollection` → Workspace-Auswahl-Dialog (Typ Collaboration/Collection) |
| Zu Task hinzufügen / Task erstellen | `canAddToTask` / `canCreateTask` |
| Vergleichen | genau 2 Assets selektiert → Versions-/Asset-Vergleichsdialog |
| Metadaten exportieren | alle lesbar → Export-Dialog (Excel/CSV, s. u.) |
| Kontaktabzug exportieren | → Contact-Print-Dialog |
| Lizenzrechte prüfen | `canReadLicensePurchase` → Rights-Check-Dialog |
| Derivate regenerieren / Properties extrahieren | Wartungsaktionen (`canRegenerateDerivations`, `canExtractProperties`) → Auswahl-Dialoge |
| Entfernen | `canDelete` → Asset-Removal-Dialog |

Edit-Mode (Spreadsheet-Inline-Editing) hat eigene Action-Bar mit "Speichern" (nur bei Änderungen) und Abbrechen; Deactivate-Guard fragt bei ungespeicherten Änderungen nach.

#### Drag&Drop-Operationen (`services/dragdrop`, `DragOperationType`)

`LinkAssetToCategory`, `MoveAssetToCategory` (nur mit `canDeleteAssetSet`), `CreateAssetToAssetRelation` (Asset auf Asset/Relations-Kachel), `MoveCategoryToCategory`, `CopyCategoryToCategory` (Kategorie im Baum verschieben/kopieren), `AddAssetToBasket` (Drop auf Basket-Navigation oder Drop-Area), `AddTaskToStateLane`, `ChangePositionOfStateLane`, `AddStateAsNextStateTransition` (Kanban). Konzept: `DragInitiator` (canInitiate, getOperations, getDragHintConfiguration, getParams, completed) und `DragReceiver` (canReceive, getOperations, receive) verhandeln über `DragNegotiationService`; bei mehreren möglichen Operationen erscheint ein Kontextmenü (Link vs. Move). Drag-Hint: Text oder Klon des Elements (`DragHintType`).

#### Suche

| Feature | Details |
|---|---|
| Volltext | Quick-Search-Box, Type-ahead-Vorschläge (Assets, Kategorien, Custom Entities), Enter = Suche; Mindestlänge 2 |
| Erweiterte Suchoptionen (`filter/advanced-search-configuration-options`) | Exakte Suche (Checkbox), Suche in Dokumentinhalten (Checkbox, nur Assets), Operator zwischen Suchbegriffen (`MultiTermOperator`: `Or`/`And`), Sprachen (Multi-Select der Data-Languages) |
| Metadaten-Filter (`filter/advanced-property-filters`) | Liste von Filtern: optional Checkbox zum Aktivieren, Entity-Typ-Auswahl (Asset/Version/Category/Custom Entity), Property-Definition-Dropdown, Sprach-Multi-Select (bei mehrsprachigen Properties), Operator-Dropdown (je Datentyp anwendbar), Werteingabe über `app-form-element-editable` (typgerecht), `Between` mit zwei Werten ("und"), Filter entfernen/hinzufügen, Reset. Operatoren (`Operator`): `HasValue`, `HasNoValue`, `Equals`, `IdentifierEquals`, `IdentifierNotEquals`, `NotEquals`, `Contains`, `DoesNotContain`, `FuzzyEquals`, `FuzzyContains`, `GreaterThanOrEquals`, `LessThanOrEquals`, `Between` |
| Filter-Chips (`filter/metadata-filter`) | Zeigen Entity-Typ, Property, Sprachen (Tooltip), Operator, Werte (typgerecht formatiert: Liste, Datum, Zeit, User), Verknüpfung "und"/"oder"; Klick toggelt, X löscht |
| Facetten (`filter/item-filter`) | Vom Server gelieferte Facetten (`searchResultFacets`) als Dropdown-Filter mit Mehrfachauswahl, Anzeige der gewählten Werte, Tooltip bei mehreren; Sonderfälle: User-Facette (Avatar/Name), Workspace-Facette; `item-filter-datetime` (Kendo DatePicker, optional Apply-Bestätigung), `item-filter-toggle` (Boolean-Toggle, z. B. "ist Template"), `generic-filters` (Checkbox-Liste mit Suchfeld, Treffer-Anzahl, "Filter löschen") |
| Kategorie-Einschränkung | Ausgewählte Kategorien im Baum (Multi-Select konfigurierbar), Option "rekursiv in Unterkategorien" (`sqo_rfc`) |
| Papierkorb | Eigener Baum (gelöschte Assets), Kontextmenü: Konfiguration, Aktualisieren, Bearbeiten, Unterkategorie, Einstellungen, **Papierkorb leeren** (`canDeleteRecycleBin`), Löschen, "Verschieben nach" (Wiederherstellen per Drag/Menü; genaues Restore-Verhalten unklar) |
| User-Context-Filter | Auswahl des Kontexts (Mandant/Markt), beeinflusst Property-Werte und Gültigkeit |
| Data-Language-Filter | Auswahl der Datensprache(n) für Anzeige/Suche |
| Sortierung (`popup/sort-configuration-dialog`) | Popup mit sortierbaren Property-Definitionen + Auf/Absteigend; Default "Relevanz"; Sprach-Suffix; im Spreadsheet Sortierung per Spaltenkopf |
| Paging (`controls/view-configuration`) | Gesamtanzahl vorne, Page-Size-Popup (`[50,100,200,500]` aus Config), Erste/Vorherige/Seitenzahlen/Nächste/Letzte |
| Spaltenauswahl (`asset-browser/column-selection-dialog`) | Dialog mit `app-property-definition-selection` (Property-Definitionen wählen/ordnen); aktuelle Auswahl als **Property-Set** speichern (`canCreateVersionPropertySet[Global]`), Property-Sets (`controls/property-sets`): Liste privat/global, Suche ab 10 Einträgen, Kontextmenü Create/Edit/Delete/Deselect (`dialogs/property-set-edit-dialog`: Name, isGlobal) |
| Gespeicherte Suchen (`saved-searches/`) | Dropdown mit Liste (Suche ab 10), Zustand "ungespeicherte Suche", Speichern (neu/bestehend aktualisieren), Clear, Kontextmenü je Suche: Details, Bearbeiten, Suche aktualisieren, Löschen (Bestätigung). `dialogs/saved-search-edit-dialog`: Formularfelder (Name etc. aus State) + **Freigabe** über `app-entity-resource-permissions` (Benutzer/Gruppen) – geteilte Suchen; Aufruf per URL `?ss=<id>` |
| URL-Zustand | Vollständiger Suchzustand in Query-Params (siehe Kap. 4.1); `AssetBrowserResolver` baut `AssetSearchQuery.fromQueryParams`, entfernt ungültige Params, Saved-Search-Param hat Vorrang |
| Externe Suche | Smint.io (Stock Content) via konfigurierter URL |
| Keine Treffer | `app-no-search-results-info-large` mit Hinweistext (ohne Kriterien: "Suchhinweis") |

#### Kategoriebaum (`filter/category-filter`, `controls/tree`, `state/category-tree`, angular-tree-component)

- Lazy geladener Baum mit Checkboxen (Einzel-/Multi-Selektion je Konfiguration), Ordner-Icons, Asset-Anzahl je Knoten (optional), Ladeindikator je Knoten, Favoriten-Bereich oben (Stern-Toggle, in User-Settings persistiert), Schnellzugriff "ausgewählte oder Favoriten", Root-Override (z. B. Workspace-Root), Data-Language-Override für Namen (kursiv, wenn nicht übersetzt).
- **Kontextmenü Root** (Filter-Menü): Kategorie erstellen (`entityPermissions.canCreateCategory`), Bearbeiten, Konfiguration (Dialog `category-tree-configuration-dialog`: Mehrfachauswahl, Asset-Anzahl anzeigen, alle Kategorien anzeigen, rekursive Suche), Alle einklappen, Aktualisieren.
- **Kontextmenü Knoten**: Zu/Von Favoriten, Upload in Kategorie (`canCreateAssetSet` + `canUploadAssetSet`), Kategorie herunterladen (`canDownloadCategories` + `canDownloadAssets` → ZIP inkl. Unterkategorien-Option), Bearbeiten (`canUpdateCategory`), Unterkategorie erstellen, Einstellungen (Rechte/Properties; `canManageSettings`), Einklappen, Löschen (`canDeleteCategory`, Bestätigung mit Checkbox), Verschieben nach / Kopieren nach / Verknüpfen mit (`MoveTo`, `CopyTo`, `LinkTo`).
- Drag&Drop: Kategorien verschieben/kopieren; Assets auf Kategorie ziehen → Link oder Move.
- `dialogs/category-edit-dialog`: Felder Identifier, Name (mehrsprachig, Data-Language-Filter links), weitere Properties in Gruppe "Allgemein" (Property-Definitionen des Entity-Typs Category).
- Kategorie-Properties eines Assets (`asset/asset-category-properties`): Read-only-Anzeige der Properties aller Kategorien, in denen das Asset liegt, gruppiert je Kategorie mit Pfad (`app-entity-path`).

### 7.2 Asset-Detailseite (`/asset/:id`, `app/asset`)

| Element | Beschreibung | Berechtigung |
|---|---|---|
| Kopf | Titel "Asset" + Dateiname; Versions-Selector (`asset-version-selector`: Dropdown mit Thumbnails, "neuere/ältere laden", "alle anzeigen", Upload neuer Version) | `canViewAssetVersion`, `canCreateAssetVersion` |
| Drop-Zone | Datei auf Seite ziehen = neue Version hochladen | `canCreateAssetVersion` |
| Buttons | Zurück (Routing-History), Compare (wenn Vergleich möglich) | |
| Aktionsmenü | Derivate regenerieren (nur neueste Version), Properties drucken (Print-Outlet), Properties exportieren, verwandte Assets hochladen (Upload-Wizard mit Relations-Schritt) | `canCreateAssetDerivationJob`, `canReadAssetProperty`, `canUploadAssetSet` + `canCreateRelation` |
| Sub-Navigation (Tabs) | `metadata` (`canViewAssetProperty`), `category-properties` (`canViewCategoryProperty`), `versions` (`canViewAssetVersion`), `formats` (`canViewAssetDerivation`), `relations` (`canViewRelation`), `license-purchases` (`canViewLicenseOffer`) | |

| Tab | Inhalt |
|---|---|
| Metadaten (`asset-properties-edit` → `entity-properties-edit`) | Vollständiger Property-Editor (siehe Kap. 8.6), Deactivate-Guard |
| Kategorie-Properties | s. o. |
| Versionen (`asset-versions`) | Grid: Version, Thumbnail, Dateiname, Asset-Typ, Erstellt am; Paging |
| Formate (`asset-derivations`) | Grid der Derivate: Derivations-Typ (Link "in neuem Fenster öffnen" wenn `showInDownloads`), Dateityp, Größe, "manueller Upload erlaubt", Status (On-Demand / Job-Status-Icon), Aktionen: Generieren (On-Demand, nur neueste Version), Download, In neuem Fenster, In Warenkorb, Derivat manuell hochladen (`allowManualUpload`); **Polling alle 5 s** für Job-Status |
| Relationen (`asset-relations` → `entity-relations`) | Relationen bearbeiten nur bei neuester Version (siehe Kap. 8.7) |
| Lizenzen (`asset-purchase-browser` → `entity-license-purchases`) | Lizenzkäufe zu diesem Asset (siehe Kap. 9) |

### 7.3 Vorschau / Viewer

| Komponente | Unterstützt | Bibliothek |
|---|---|---|
| `controls/entity-preview` | Thumbnail-Fallback, Zoom-Button (öffnet Lightbox), Media-Source je Typ: `isMovie` → Video, `isAudio` → Audio, `isPDF` → PDF-Viewer; sonst Bild | – |
| `controls/audio-video-element` | `<video>`/`<audio>` mit video.js (Preview-Derivat) | video.js 7 |
| `controls/pdf-element` | PDF mit Zoom-Toolbar (`page-width`), "in neuem Fenster öffnen", Ladeanzeige | ngx-extended-pdf-viewer |
| `lightboxes/asset-image-lightbox` | Vollbild-Bild mit Zoom (`imageZoom`-Direktive: Mausrad/Drag, Reset), Vor/Zurück durch Trefferliste, Schließen (Esc / Browser-Back) | eigene |
| `lightboxes/asset-image-compare-lightbox` + `controls/image-comparer` | Zwei Bilder (Versionen/Assets) mit Schieberegler-Vergleich, Labels | eigene |
| `controls/cropper` + `dialogs/asset-crop-dialog` | Wizard: 1) Ausschnitt wählen (cropperjs) 2) Zielkategorie → erzeugt Crop-Derivat/neues Asset (`canCreateCropDerivation`) | cropperjs |
| 3D / CAD | kein Viewer; `AssetTypes.CAD` nur als Typ mit Placeholder-Icon | – |
| Office (Word/Excel/PowerPoint/InDesign/Illustrator) | nur Thumbnail-Derivate (`assetTypesWithThumbnailDerivation`), keine Live-Vorschau | – |

Asset-Typen (`AssetTypes`): Image, Movie, MS PowerPoint, MS Word, MS Excel, PDF, Adobe Illustrator, Audio, Adobe InDesign, CAD, Archive, Unknown. Asset-Status (`AssetStatus`): Importing, Imported, Duplicate, Unknown. Icons via `assetTypeIcon`-Pipe.

### 7.4 Upload

| Aspekt | Verhalten |
|---|---|
| Einstieg | Upload-Button (Multi-File), Drag&Drop auf Asset-Browser/Kategorie/Workspace/Asset-Detail (neue Version)/Relations-Kachel; Kontextmenü "Upload in Kategorie"; Aktionsmenü "verwandte Assets hochladen"; Derivat manuell hochladen |
| Dialog `dialogs/asset-upload-dialog` (Wizard) | Schritt 1 **Assets**: Grid der Dateien (Name, Größe, Entfernen), Kennzeichnung nicht erlaubter Dateitypen (`forbiddenFileTypes` aus Session, Tooltip), Archive: "alle extrahieren"-Checkbox + je Archiv `app-archive-extraction` (Modus `ExtractWithFullPath`/…, Asset nach Extraktion löschen), Datei-Drop in den Dialog fügt hinzu. Schritt 2 **Zielkategorie**: Kategorie-Baum. Schritt 3 **Upload-Workspace** (nur wenn `mustUploadThroughUploadWorkspace`): neue Upload-Session (Standard oder aus Template) oder bestehende Session wählen (`app-template-card`, `app-entity-selection`). Schritt 4 **Workspace-Properties** (wenn Template Properties verlangt): `app-entity-properties-edit`. Schritt 5 **Relation** (wenn von einem Asset aus gestartet, `canCreateRelation`, optional/pflicht je Konfiguration): Quell-Asset-Karte, Checkbox "Metadaten kopieren", Relationstypen wählen, je Typ Grid der Dateien mit Checkboxen (Archive ausgeschlossen) |
| Übertragung (`services/upload/sharedien-upload.service.ts`) | **Chunked Upload**: API liefert `maxChunkSizeBytes` + Chunk-Request-URL; Datei wird per `File.slice` in Chunks geteilt; je Chunk `requestAssetChunkUpload` → Upload direkt zum Storage-Provider (`uploadUrl`), **3 Retries pro Chunk**, sequenziell; abschließend `finishUpload` mit Chunk-IDs, Relations-Infos, Extraktions-Settings. Fortschritt aus hochgeladenen Bytes |
| Queue (`upload-management/state`) | Globale Upload-Queue, **ein Upload gleichzeitig** (nächster nach Abschluss), Re-Enqueue bei Fehler, Status `Queued/Uploading/Finished/Failed`; Abbrechen des aktiven Uploads |
| Fortschritt (`upload-progress/`) | Globales Widget unten: aktueller Upload (Thumbnail, n von m), Zusammenfassung nach Batch (erfolgreich/fehlgeschlagen, Thumbnails, Link "zu Uploads") |
| Upload-Management (`/upload-management`) | Sub-Navigation: **Sessions** (= `app-workspaces` vom Typ Upload-Session) und **History** (Karten aller eigenen Uploads mit Upload-Status/Asset-Status, Filter nach Status, Retry/Cancel, Mehrfachauswahl + Löschen, Paging; **Polling mit Backoff** 3 s bis 10 Intervalle) |
| Ordner-Upload | nicht vorhanden (kein `webkitdirectory`) |
| Duplikate | Server markiert `AssetStatus.Duplicate`; Karte zeigt Duplikat-Icon; Dashboard-Kachel "Asset-Duplikate" |
| Archiv-Extraktion (`dialogs/asset-extraction-dialog`) | Wizard: Extraktions-Settings, Zielkategorie → Job (Report `/archive-extraction-job-report/:id`) |
| Finalisierung (`dialogs/asset-finalization-dialog`) | Für Upload-Sessions: Validierungsschritt (Grid je Asset gültig/ungültig, Property-Fehler-Grid, "Assets bearbeiten"), dann Finalisieren (Assets werden in Ziel übernommen; Details serverseitig unklar) |

### 7.5 Download / Warenkorb

| Aspekt | Verhalten |
|---|---|
| Einzel-Download | Karte/Menü "Download": `DownloadPreferredDerivationOfAsset` bzw. `DownloadDerivationOfAsset` → `window.open(uri)` (Download-URL, ggf. mit Token) |
| Derivat-Auswahl (`dialogs/asset-derivations-selection-dialog`) | Gruppiert nach Asset-Typ (`app-collapsible-container`), je Derivations-Typ Checkbox mit Dateiendung/Größe, Kennzeichnung "On-Demand" (Tooltip: wird erst generiert), "Alle auswählen", bei Kategorie-Download Checkbox "Unterkategorien einschließen", Auswahl **Filename-Pattern** (`controls/file-naming-definition-selection`, Dropdown der `FileNamingDefinition`s mit Tooltip), Button "In Warenkorb" oder "Download" (ZIP) |
| ZIP-Download (`services/api/sharedien-api-download.service.ts`) | Formular-POST (verstecktes `<form>` mit `zipDownloadRequest` JSON: `assets[{identifier, derivationTypeIdentifier, overrideFileNameFormat}]`, `fileNamingDefinitionIdentifier`) bzw. Kategorie-ZIP (`assetDerivationTypes`, `dataLanguageIdentifier`, `includeSubCategories`, `fileNamingDefinitionIdentifier`); Public-Share-Variante mit Token |
| Warenkorb (`/basket`, `basket/`) | **Bundles** (mehrere benannte Pakete): je Bundle Titel + Gesamtgröße, Filename-Pattern-Auswahl, Download (ZIP), Entfernen, Alle auf-/zuklappen; Grid der Assets (Thumbnail → Detail, Name, Anzahl Formate, Größe) mit Untergrid je Format (Derivations-Typ, Größe, Status On-Demand/laufend); Aktionen je Asset/Format: in anderes Bundle verschieben, in neues Bundle, Format hinzufügen, Entfernen; "Alle entfernen"; Hinweis + Button "On-Demand-Formate generieren"; Item-Count-Badge in Navigation; Drop-Ziel in Navigation und Drop-Area im Browser |
| Wasserzeichen | serverseitig per Derivations-Typ/Watermark-Definition (Admin); Karte zeigt `watermarkThumbnailUri` |
| Lizenz-/Rechte-Check | Aktion "Lizenzrechte prüfen" (Kap. 9); Download-Sperre bei fehlender Lizenz unklar (serverseitig) |

### 7.6 Sammlungen (`/collections`, `collection-browser/`, `workspaces/`)

`CollectionBrowserComponent` bettet `app-workspaces` mit Workspace-Typ *Collection* ein (gleiche Komponente wie Workspaces/Upload-Sessions). Features der `WorkspacesComponent`: Quick-Search mit Vorschlägen, erweiterte Suche (Optionen + Metadaten-Filter), Filter-Chips, Facetten, Toggle "nur Templates" (`sq_st`), View-Switch Galerie/Spreadsheet, Sortierung, Paging, Button "Erstellen" (`canCreateWorkspace`), Selektion + "Alle auswählen"/"Löschen"; Karte (`controls/workspace-card`): Titel, Avatar oder Thumbnail-Collage der Assets, Erstellt am, Task-/Benutzer-/Kommentar-Anzahl, Select/Delete. Assets werden per Aktionsmenü "zu Collection hinzufügen" (Workspace-Auswahl-Dialog) aufgenommen.

### 7.7 Weitere Asset-Dialoge

| Dialog | Zweck |
|---|---|
| `asset-move-dialog` | Assets in andere Kategorie verschieben; zeigt je Asset bestehende Kategorie-Relationen (Original vs. Link, Pfad) mit Checkboxen, wenn Auswahl nötig |
| `asset-removal-dialog` | Entfernen aus Kategorie / Löschen: unterscheidet Originale vs. Links, Modus "Kategorie-Entfernung" vs. "Relation-Entfernung", `parentChildRelationDeleteBehavior` (regulär = AssetSet löschen), Auswahl der zu entfernenden Relationen je Asset |
| `asset-selection-dialog` | Eingebetteter `app-assets`-Browser zur Auswahl von Assets (z. B. für Relationen/Tasks) |
| `asset-properties-mass-edit-dialog` | Massenbearbeitung per Suchabfrage: Property-Gruppen-Sprungliste, Suche nach Properties, Filter (Gruppen, Pflicht …), Property-Set-Auswahl, Formular je Property mit User-Context/Sprache; Hinweis auf Limit (`property-edit-limit-info`); erzeugt Job → Report `/mass-edit-job-report/:id` (Ergebnis je Property: Created, AppliedKeepExisting, AppliedOverwriteExisting, AppliedDeleteExisting, IgnoredDelete, MissingAsset, MissingPropertyDefinition, Failed) |
| `export-asset-metadata-dialog` (`entity-properties-export-dialog`) | Felder: Dateiformat, Label-Datensprache, Label-Locale, Property-Sprachen, Property-Kontexte; Modus: alle Properties / aktuelle Auswahl (Spalten) / Property-Set; Warnung bei Überschreiten des Export-Limits |
| `export-contact-print-dialog` | Kontaktabzug (Druck via Print-Outlet) in Formaten `Portrait1x1/1x2/2x3/3x5`, `Landscape1x1/2x1/3x2/5x3` (Vorschau-Raster), Metadaten-Felder aus Admin-Einstellung "Contact Print" |
| `asset-version-compare-dialog` | Zwei Versions-Selectoren (links/rechts), Bildvergleich (`image-comparer`, Zoom → Compare-Lightbox), Property-Vergleich (`entity-properties-compare`, diff-match-patch), Sprungliste, Filter |
| `regenerate-derivation-selection-dialog` | Auswahl der Derivations-Typen für Regenerierung (Selektion oder Suchabfrage) |
| `property-extraction-definition-selection-dialog` | Auswahl der Extraktions-Definitionen (Metadaten aus Datei extrahieren) |
| `entities-license-rights-check-dialog` | siehe Kap. 9 |

---

## 8. Workspaces, Tasks, Custom Entities, Sharing, Kommentare/Aktivität, Metadaten-Editor, Relationen, Entity-Permissions, Dashboard/Reports, Benutzer

### 8.1 Workspaces (`/workspaces`, `/workspace/:id`)

Workspace-Typen (`WorkspaceType`): **Collaboration** (Projekt-Arbeitsbereich mit Assets, Tasks, Kollaborateuren), **Collection** (Sammlung, Kap. 7.6), **Upload** (Upload-Session mit Finalisierung). Workspaces können **Templates** sein (`isTemplate`, Filter "nur Templates").

| Bereich | Features | Berechtigung |
|---|---|---|
| Browser (`workspaces/`, `workspace-browser/`) | Galerie (`workspace-card`) / Spreadsheet (Thumbnail, dynamische Property-Spalten, `totalAssets`, `totalTasks`, `totalUsers`, `totalComments`, Zeilenmenü), Quick-Search, erweiterte Suche, Facetten, Sortierung, Paging, Mehrfachauswahl + Löschen, "Erstellen" | `WorkspaceView`, `canCreateWorkspace` |
| Erstellen (`workspace-create-dialog`, Route `/workspaces/create[/:templateId]`) | Wizard: 1) **Einstellungen** (Data-Language-Filter, Felder `identifier`, `title` (mehrsprachig), `workspace_type`, `template`, `copy_properties`, Property-Set; Avatar-Upload `app-avatar-selection` mit Thumbnail-Auswahl) 2) **Template-Auswahl** (nicht bei Collection; System-Templates als Karten + Benutzer-Templates mit Suche/Paging) 3) **Kollaborateure** (`entity-resource-permissions-display`: Benutzer/Gruppen suchen, Rolle zuweisen) | `canCreateWorkspace` |
| Bearbeiten (`workspace-edit-dialog`, "Einstellungen") | Gleiche Felder + **Finalisierungs-Kategorie** (Kategorie-Auswahl-Dialog, Clear; nicht bei Collection), Avatar, `workspace-settings` (Benachrichtigungen deaktivieren `disable_notifications`) | `canEditWorkspace` |
| Detailseite (`workspace.component`) | Kopf: Titel, Kollaborateure-Popup (`entity-resource-permissions-popup`, Avatare), Aktivitäts-Popup (`entity-activity-popup`: Activity-Log + Kommentare + Watcher), Zurück, Upload-Button, "Finalisieren" (Upload-Typ, wenn Assets vorhanden), Aktionsmenü: Assets hinzufügen (Asset-Auswahl-Dialog), Share erstellen, Properties drucken, Properties auf Assets kopieren (`canCopyWorkspacePropertiesToAssets`), Einstellungen, Workspace/Collection löschen. Sub-Navigation: `assets`, `tasks`, `template-permissions` (Template-Rechte bzw. Kollaborateure), `shares`, `metadata` | `workspaceFeatures.*` (canViewWorkspaceCollaborators, canReadWorkspaceActivityLogs, canUploadAsset, canFinalize, canAddAsset, canCreateShare, canViewWorkspaceProperties, canEditWorkspace, canDeleteWorkspace) |
| Assets-Tab (`workspace-assets`) | `app-assets` mit Workspace-Root als Kategorie-Wurzel; Crop/Upload/Kategorie-Download nur bei Collaboration; kein Basket; Task-Erstellung/-Zuordnung (`canCreateTask`, `canUpdateTask`); Finalisieren (`canFinalizeWorkspace`) | |
| Tasks-Tab (`workspace-tasks`) | `app-tasks` (Kanban/Liste) für diesen Workspace, Task-Edit-Panel mit Deactivate-Guard | `TaskView` |
| Rechte-Tab (`workspace-template-permissions`) | `app-entity-resource-permissions` (Benutzer/Gruppen × Rolle), Hinweistext je nach Template/Workspace, Speichern/Entfernen in Action-Bar, Deactivate-Guard | |
| Shares-Tab (`workspace-shares` → `state/shares/shares.component`) | Grid: Name (Klick = Share-Dialog), Empfänger, Gültig bis, Status (Active/Expired); Auswahl + Löschen, Paging | `canDeleteShare` |
| Metadaten-Tab (`workspace-properties-edit`) | `entity-properties-edit` für Workspace-Properties | `canEditWorkspaceProperty` |
| Upload-Wizard (`/workspace/upload/:id`, `workspace-upload/`) | Wizard-Style "Dialog" mit Schritten 1) **Assets**: Drop-Zone, Upload-Button, Übersicht (Assets, ausstehende Uploads, ausstehende Extraktionen), Karten der laufenden Uploads (Retry) und der hochgeladenen Assets (Select, Download, Remove, Extract), Refresh (Polling 5 s + Backoff) 2) **Klassifizierung**: Hinweis auf unausgefüllte Pflichtfelder, Property-Set-Filter (abgeleitete/eigene Sets, "nicht klassifizierbar"), Füllstand-Icons je Asset, Auswahl + "Bearbeiten" (Mehrfach-Editor) 3) **Finalisieren**: Zielkategorie (vorbelegt aus Workspace-Einstellung) → Finalisierungs-Dialog | |

Rollen für Workspace-Kollaborateure (`RoleIdentifiers`): `workspaceReader`, `workspaceCollaborator`, `workspaceOwner`, `collectionReader`, `collectionOwner`, `entityReader`, `entityOwner`, `licenseAdmin` – als Toggle-Buttons in der Rechte-Tabelle; Hinweis, wenn erforderliche Rollen nicht erfüllt (`requiredRoles`).

### 8.2 Tasks / Kanban (`/tasks`, `tasks/`, `task-edit/`, `entity-board/`, `state-lanes/`)

| Feature | Details |
|---|---|
| Übersicht (`task-browser` → `tasks`) | Quick-Search + erweiterte Suche + Facetten (wie Assets), Toggle "Templates", View-Switch **Board** (Kanban) / **Spreadsheet**, Paging, Mehrfachauswahl + Löschen, "Erstellen" (`canCreateTask`) |
| Spreadsheet-Spalten | Checkbox, Name (Link zu Details, Kommentar-Anzahl), Workspace (außerhalb Workspace-Kontext), Assignee (Avatare / "nicht zugewiesen"), Status (`task-status`: Open/Closed), Fälligkeit (`task-due-date`, rot bei überfällig), Zeilenmenü (Details, Löschen) |
| Kanban-Board (`entity-board`) | Spalten = **State Lanes** (Workspace-spezifische Zustände), je Spalte Menü (Edit/Delete), Karten mit Name, Assignees, Fälligkeit, Status, Kommentar-Anzahl; **Drag&Drop** Task → Lane (`AddTaskToStateLane`); Klick = Task-Detail; Button "View-Settings" öffnet `state-lanes-dialog` |
| State Lanes (`state-lanes`, `state-lanes-dialog`, `state-lane-edit-dialog`) | Liste der Lanes mit Drag&Drop-Reihenfolge (`ChangePositionOfStateLane`), **Transitions** (erlaubte Folgezustände per Drag `AddStateAsNextStateTransition`, Entfernen), Lane erstellen/bearbeiten/löschen (`canCreateState`, `canUpdateState`, `canDeleteState`); Edit-Dialog: Name (mehrsprachig) + `entity-resource-permissions` je Lane (wer darf in diesen Zustand verschieben) |
| Task-Detail (`task-edit`, Slide-in/Vollbild-Panel) | Sprungliste (Assets, Aktivität), Menü Löschen (`canDeleteTask`), Vollbild-Toggle, Schließen, Speichern; **Properties** via `entity-properties-edit` (Task-Felder sind Property-Definitionen des Entity-Typs Task: Name, Beschreibung, Assignees, Fälligkeit, Status …); **Assets**-Abschnitt: Karten, Hinzufügen (Asset-Auswahl-Dialog), Upload, Auswahl → Entfernen/Download; **Aktivität**: `entity-activity-logs` (Kommentare + Änderungen); **Watcher**: `entity-watchers` (Avatare, Watch/Unwatch) |
| Task aus Asset-Browser | Aktionen "Task erstellen" / "zu Task hinzufügen" (`task-selection-dialog`, `task-template-selection-dialog`) |
| Model | `TaskState` Open/Closed, `dueDate`, `isOverdue`, `assignedUsers`, `totalComments`, `entityPermissions.canReadTask/canUpdateTask/canDeleteTask/canReadTaskActivityLogs/canReadComment` |

### 8.3 Custom Entities (`/custom-entities[/:id]`, `/custom-entity/:entityTypeId/:entityId`)

Generische, im Admin definierte Entity-Typen (z. B. Produkte, Personen, Kampagnen) mit eigenen Property-Definitionen, Relationen zu Assets und Lizenzangeboten.

| Feature | Details | Berechtigung |
|---|---|---|
| Browser (`custom-entity-browser` → `custom-entities`) | Pro Entity-Typ (Sub-Navigation "Data" bzw. gepinnt): Quick-Search, erweiterte Suche, Filter-Chips, Facetten, Sortierung, Paging, Galerie aus `entity-card` (draggable), Auswahl + Löschen, "Erstellen", Relations-Kachel für fokussierte Entity | `CustomEntityBrowserView`, `canCreateCustomEntity` |
| Erstellen/Bearbeiten (`custom-entity-create-dialog` = `custom-entity-edit-dialog`) | Data-Language-Filter, Gruppe "Allgemein" mit dynamischen Property-Feldern | `CustomEntityCreate/Update` |
| Detail (`custom-entity`) | Kopf: Entity-Typ + Name, Zurück, Menü "Properties drucken"; Tabs `assets` (`app-assets` mit Relationen: Upload = Asset + Relation, Delete = Relation löschen, zu Workspace/Collection, Regenerate, Extract), `metadata` (`custom-entity-properties-edit` → `entity-properties-edit`, Deactivate-Guard), `relations` (`entity-relations`), `licenses` (`license-offers`) | `canReadCustomEntityProperty`, `canViewCustomEntityProperty`, `canViewCustomEntity`, `canViewLicenseOffer` |
| Auswahl-Dialog (`custom-entity-selection-dialog`) | Auswahl von Custom Entities (z. B. für Relationen/Properties vom Typ "searchable entity") | |

### 8.4 Sharing / öffentliche Links

| Element | Details |
|---|---|
| Share-Modell (`models/share.ts`) | `name`, `assetTypeDerivations` (je Asset-Typ erlaubte Derivations-Typen), `validUntil` (Ablaufdatum), `emailRecipients[]` (je Empfänger eigener Token), Status `Active`/`Expired` (clientseitig aus `validUntil`) |
| Erstellen (`share-edit-dialog`) | Aus Workspace ("Share erstellen") oder Asset-Selektion ("Teilen" → erzeugt Workspace + Share). Panels: Allgemein (Name, Gültig bis), Formate (Checkboxen je Asset-Typ/Derivations-Typ), Empfänger (E-Mail-Liste: hinzufügen/entfernen, je Empfänger **Link kopieren** `/shares/<token>`) |
| Public-Share-Seite (`/shares/:token`, `public-share/`) | **Ohne Login** (App-Init überspringt Auth, Tenant per Resolver, Theme wird geladen): Bundle-Titel, Anzahl, Grid (Thumbnail, Dateiname, Derivations-Typ, Größe, Einzel-Download), Mehrfachauswahl → "Ausgewählte herunterladen" / "Alle herunterladen" (ZIP via Public-Endpoint mit Token) |
| Passwortschutz | nicht vorhanden (unklar serverseitig; UI hat kein Feld) |
| Share-Item-Dialog (`share-item-dialog`) | Interner Deep-Link eines Objekts (z. B. Asset/Workspace) anzeigen und in Zwischenablage kopieren |
| Verwaltung | Workspace-Tab "Shares" (Liste, Bearbeiten, Löschen) |

### 8.5 Kommentare, Aktivitätsprotokoll, Benachrichtigungen, Watcher

| Komponente | Details |
|---|---|
| `comments/`, `comments-popup/`, `controls/comment-box` | Kommentar-Editor (roosterjs Rich-Text, **@-Mentions** von Benutzern über Kendo-Popup, `canMentionUsers`), "Posten", Liste mit Paging, Inline-Edit/Delete je Kommentar (`canUpdateComment`, `canDeleteComment`), Popup-Variante mit Badge |
| `entity-activity-logs/` | Kommentar-Editor + Umschalter "nur Kommentare / alle Aktivitäten", gruppierte Einträge (nach Datum), Kommentare editierbar; verwendet in Task-Detail und Workspace-Aktivitäts-Popup (`entity-activity-popup` inkl. Watcher) |
| `controls/activity-log/**` | Ein Eintrag: Avatar, Text nach `ActivityLogType` (TaskCreated/Updated/Deleted, WorkspaceCreated/Updated/Deleted, AssetCreated/Updated/Deleted, CategoryCreated/Updated/Deleted, RelationCreated/Deleted, JobUpdated), Link zum betroffenen Objekt, Änderungsdetails (`activity-log-change` Standard, `activity-log-property-changes` alt/neu je Property/Sprache/Kontext, `activity-log-comment-changes`), Menü "als gelesen markieren"/"löschen" |
| `activity-logs-inbox/` (Glocke) | Persönliche Inbox (ungelesen-Zähler), "mehr laden", "alle löschen", Navigation zum Objekt; **Reload bei SignalR-Event** |
| `entity-watchers/` | Watcher-Avatare eines Objekts, "Beobachten/Nicht mehr beobachten" für sich selbst → Benachrichtigungen bei Änderungen |
| Toasts | `angular2-notifications` Success/Error/Warning aus `UiNotificationsState` |
| Workspace-Einstellung | "Benachrichtigungen deaktivieren" je Workspace |

### 8.6 Metadaten-Editor (`entity-properties-edit/`, `entity-properties-edit-multiple/`, `controls/form-element*`)

Generischer Property-Editor für alle Entity-Typen (Asset/Version, Category, Workspace, Task, Custom Entity, License, RightsOwner, BillingContact, SavedSearch …). Formulare werden aus Property-Definitionen (Session-State) + Property-Gruppen gebaut (`ReactiveFormElementGroup`).

| Feature | Details |
|---|---|
| Layout | Links: Entity-Vorschau (`entity-preview`, 260px), **Füllstand-Status** (Pflicht/Empfohlen: Anzahl unausgefüllt, Prozent, Filter-Checkboxen "recommended"/"mandatory", "Anzeigen"), allgemeine Filter (Gruppen), Property-Gruppen-Sprungliste; oben: Property-Suche (Textfilter über Labels), Item-Filter (Gruppen, Sprachen, Kontexte), **Property-Set-Auswahl** (privat/global, `canViewPropertySet(entityType)`), Hinweis "n ausgeblendete Treffer". Konfigurierbar über `PropertiesEditViewConfiguration` (Preview, FillRate, Gruppenübersicht, Suche, Property-Sets sichtbar) |
| Gruppen | `collapsible-container` je Property-Gruppe (ein-/ausklappbar, selektierbar im Auswahlmodus) |
| Form-Element (`controls/form-element`) | Label (übersetzt), Tooltip (Beschreibung + Suchbarkeit), Validitäts-/Pristine-Markierung, Pflicht-/Empfohlen-Kennzeichnung, Kontext-Label (User-Context), Sprach-Label, Lock-Icon mit Grund (`isLocked`, z. B. Rule/Systemfeld), Auswahl-Checkbox (Selektionsmodus für Druck/Export) |
| Editor-Controls (`form-element-editable`) je `ValueType`/Flag | `String` → Textbox; `String`+`isUrl` → Textbox mit Link; `String`+User → `user-selection-dropdown` (Benutzersuche); `String`+`isSearchableEntityElement` → `searchable-entity-selection-dropdown` (Entity-Referenz, z. B. Custom Entity); `Text` → Textarea (auto-expand); `Text`+`isHtml` → Rich-Text (roosterjs) mit Vorschau; `Boolean` → Checkbox; `Integer` → NumericTextBox (Bytes readonly `fileSize`); `Decimal`/`Float` → NumericTextBox; `Date` → DatePicker; `Time` → TimePicker; `DateTime` → DatePicker (G); `TimeSpan` → TimePicker; Status → `status-selection-form-element`; Liste single → DropDownList (Filter, "Create" bei `allowCustomListValue`); Liste multi → MultiSelect (Tags, Erstellen); Radio-Liste → Radio-Buttons. Read-only-Darstellung (`form-element-readonly`) für nicht editierbare/gesperrte Felder |
| Mehrsprachigkeit / Kontexte | `isTranslatable` → ein Feld je aktiver Data-Language (Sprach-Filter); `hasUserContext` → ein Feld je User-Context (Kontext-Filter, Standardkontext) |
| Vererbung | "Pass down" Checkbox je Property (`canViewPropertyInheritance`, Weitergabe an Kind-Entitäten, z. B. Kategorie → Assets) |
| Speichern | Focused-Action-Bar "Speichern" nur bei ungespeicherten Änderungen; Deactivate-Guard; Validierung (Pflicht, Format) mit `form-validation-errors` |
| Selektionsmodus | "Alle auswählen" + "Drucken"/"Exportieren" der ausgewählten Properties (Print-Outlet `entity-properties-print`) |
| Mehrfach-Editor (`entity-properties-edit-multiple`) | Für n Entities gleichzeitig: gleiche Struktur, Felder zeigen gemischte Werte, geänderte Felder werden auf alle angewendet (Modus Überschreiben; Ergebnisse `AppliedKeepExisting/OverwriteExisting/DeleteExisting`), Limit-Hinweis (`property-edit-limit-info`), Dialog-Variante `entity-properties-edit-multiple-dialog` |
| Vergleich (`entity-properties-compare`) | Zwei Entities/Versionen nebeneinander, Diff-Hervorhebung (diff-match-patch), `property-value-comparer` |
| Export/Druck (`entity-properties-export/`, `print-layout/`) | Druck-Layout im Named Outlet, `SessionState.isPrinting` blendet Shell aus, Excel/CSV-Export über Dialog (Kap. 7.7) |
| Property-Definition-Auswahl (`property-definition-selection`, `-dialog`) | Baum/Liste der Definitionen nach Gruppe mit Suche, Reihenfolge; für Spalten, Property-Sets, Extraktion |
| Gruppen-Manager (`form-element-group-manager-dialog`, `multiple-form-element-group-managers-dialog`) | Generischer Formular-Dialog (Felder aus Konfiguration, Kontext-/Sprach-Filter) für Admin-Edits |
| Rich-Text (`rich-text-editor/`) | roosterjs-Editor (Toolbar unklar/Standard) |

### 8.7 Relationen (`entity-relations/`, `entity-relations-graph/`, `tiles/entity-relations-tile`)

| Feature | Details |
|---|---|
| Relationstypen | Admin-definiert (`relation-type-management`): Name, erlaubte From/To-Entity-Typen; Sonderfälle Parent-Child (Kategorie ↔ Asset, Original), Link |
| Relations-Tab (`entity-relations`) | Filter (Relationstyp, Entity-Typ), Paging, View **Spreadsheet** (Thumbnail, Relation mit Pfad `entity-path`, Typ, Löschen `canDelete`) / **Graph** (`entity-relations-graph` auf d3-Force-Layout `controls/relation-graph`: Knoten Rect/Circle/Image in Größen S/M/L, Drag mit Fixieren, Click/DblClick auf Knoten und Kanten, Klick ins Leere) |
| Relations-Kachel (`entity-relations-tile`) | Rechte Seitenkachel im Browser: fokussierte Entity-Karte, Filter, Karten der verwandten Entities (mit Relationstyp-Name, Relation löschen), Paging, Auswahl + Download, **Drop-Zone**: Assets auf Kachel ziehen → `relation-type-selection-dialog` → Relation erstellen; Upload-Button "verwandte Assets hochladen" |
| Erstellen | Drag&Drop Asset→Asset/Kachel, Upload-Wizard-Relations-Schritt, `allowed-entity-relation-selection-dialog` (From-/To-Typ wählen) |

### 8.8 Entity-Permissions (`entity-resource-permissions/`, `controls/entity-permission-table`)

- `entity-resource-permissions-display`: Suchbox (Benutzer und Gruppen mit Vorschlägen, Links zu erweiterter Benutzer-/Gruppensuche → `user-selection-dialog`, `user-group-selection-dialog`), Grid (Checkbox, Name/Avatar bzw. Gruppe, Rolle als Toggle-Buttons mit Tooltip oder Custom-Rollenname, geerbt `isInherited`), Entfernen.
- Verwendet für: Workspace-Kollaborateure/Template-Rechte, State Lanes, Saved Searches, Lizenz-Rechte (`license-permissions`), Kategorie-Rechte (Admin).
- `entity-permission-table`: einfachere Tabelle (User/Group, Rolle, "Add Role" → `role-selection-dialog`, Hinzufügen → Auswahl-Dialog).

### 8.9 Dashboard, Kacheln, Job-Reports

Dashboard (`/dashboard`) zeigt Kacheln nach Berechtigung: Mass-Edit-Jobs (`canViewAssetPropertyMassEdit`), Asset-Derivation-Jobs (`canViewAssetDerivationJob`), Property-Import-Jobs (`canViewPropertyImport`), Asset-Duplikate, Archiv-Extraktions-Jobs, Derivation-Jobs (pending/failed je Asset-Typ/Derivations-Typ), Mass-Validation-Jobs (`canReadRuleJobReport`), Property-Extraction-Jobs (`canReadPropertyExtractionJobReport`). Jede Kachel: Titel, Refresh, Grid (Status-Icon `job-status-icon`, Name/Link zum Report, gestartet von/am, Erfolg-/Fehler-/gefiltert-Zähler). Re-Indexing-Kachel im Admin (Search-Management).

| Report (Route) | Inhalt |
|---|---|
| Archive-Extraction (`/archive-extraction-job-report/:id`) | Allgemein: Status, gestartet von/am, beendet, Dauer, "nach Extraktion löschen", Modus, Ausgabe-Kategorie (Link); Report-Grid je Archiv-Eintrag (Status, Name, Typ), Refresh |
| Asset-Derivation (`/asset-derivation-job-report/:id`) | Status/Zeiten; Grid je Asset (Dateiname → Asset, Identifier, Status, Derivations-Typ), Refresh |
| Derivation (`/derivation-job-report/:assetTypeId/:derivationTypeId?ds=`) | Liste der Derivate eines Typs nach Status (Query `ds`), Spalten Identifier (→ Asset), Dateiname, Retry-Count, Erstellt; Auswahl + **Restart** |
| Mass-Edit (`/mass-edit-job-report/:id`) | Status/Zeiten, Asset-Limit, Gesamtanzahl; Grid je Asset (Dateiname, Version, Identifier, Ergebnis je Property `PropertyMutationResult`) |
| Mass-Validation (`/mass-validation-job-report/:id`) | Regel-Link, Gesamtanzahl; Grid je Entity (Name, Typ, Ergebnis) |
| Property-Extraction (`/property-extraction-job-report/:id`) | wie Mass-Edit, je Asset extrahierte Properties |
| Property-Import (`/property-import-job-report/:id`) | Excel-Import: Status/Zeiten; Grid je Zeile (Dateiname → Asset, Version, Identifier, Property-Definition, Ergebnis) |
| Re-Indexing (`/admin/configuration/search/:id`) | Status, gestartet von, Zeiten, Fehlergrund, Entity-Typen, Fortschritt (verarbeitet/gesamt), geschätztes Ende, **Abbrechen**; Grid fehlgeschlagener Entities |

Status-Übersetzungen: `tStatus`, `tBackgroundJobTriggerType` (manuell/Regel/…), `tArchiveExtractionMode`.

### 8.10 Benutzerprofil, Einstellungen, Shell-Elemente

| Seite | Inhalt |
|---|---|
| Profil (`/user-profile`) | Profilbild (Upload mit `maxFileSizeBytes`, Zurücksetzen; nicht für anonyme Benutzer), Benutzername, Vorname, Nachname, E-Mail (readonly aus IdP), Rollen, Gruppen; **Passwort ändern** (aktuell/neu/bestätigen, Validierung Match/alt≠neu, Hinweis auf Logout; nur wenn `userCanChangePassword`) über UAA-API |
| Einstellungen (`/user-settings`) | Lokalisierung: UI-Sprache (`en`/`de`), Locale (Zahlen/Datumsformat, Kendo Intl), Datensprache (Default für Inhalte), User-Context (Default); Suchoptionen: Standard-Operator (And/Or); UI-Konfiguration: **Theme** (nur wenn `userCanSwitchThemes`); Speichern-Button bei Änderungen, Deactivate-Guard; weitere in `uiSettings` persistierte Werte: Favoriten-Kategorien, gewählte Property-Sets je Entity-Typ |
| Topbar | App-Logo (Link `/`), Brand-Logo, Header-Text, Notifications-Glocke (nur mit SignalR + Rollen + nicht anonym), Benutzer-Dropdown |
| Sidebar (`sidebar-nav`) | Logo, Benutzer-Dropdown (Avatar/Name; Profil, Einstellungen, Logout bzw. "Login" für Gast), Glocke, Hauptnavigation mit Item-Count-Badges, **Mini-Mode** (einklappen), Sub-Navigation (Admin, Data) |
| Bottombar | konfigurierte Links (`branding.bottombar.links`, öffnen in neuem Tab) |
| Error-Page | Illustration je Fehlertyp (Client-Error, API 400/403/404, unbekannter API-Fehler, unbekannter Fehler), Stacktrace (`<pre>`) |
| Anonymer Zugang | technischer Benutzer (`anonymousAccess`) → eingeschränkte UI (kein Profil, "Login" statt Logout, keine Notifications) |

---

## 9. Admin-Bereich und Lizenz-/DRM-Modul

Allgemeines Muster aller Admin-Listen: Suchbox, linke Filterspalte (`generic-filters`, z. B. aktiv/inaktiv, System), Kendo-Grid mit Checkbox-Selektion ("alle auf Seite"), sortierbare Spalten, Paging, Zeilenmenü (Edit/Delete/…), "Erstellen"-Button, Focused-Action-Bar für Mehrfach-Löschen. Edit-Seiten: eigene Route (`/create`, `/:id`), Kopf mit Zurück/Speichern (nur bei Änderungen)/Menü Löschen, Data-Language-Filter für mehrsprachige Felder, Panels (`collapsible-container`), Formular über `form-element-editable`, Deactivate-Guard. Systemobjekte (`isSystem`) sind nicht löschbar.

### 9.1 Admin-Seiten

| Seite | Liste (Spalten) | Edit-Felder / Panels | Besonderheiten |
|---|---|---|---|
| Kategorie-Management (`category-management`) | Quick-Search über Kategorien, Data-Language-Filter, Kategorie-Baum + Papierkorb-Baum (resizable) | Tabs für aktive Kategorie: **Metadaten** (`entity-properties-edit`, `canViewCategoryProperty`), **Rechte** (`category-permission-management`, `canViewNodePermission`) | Rechte: zugewiesene Permissions (`entity-permission-table`: Gruppe + Rolle), **geerbte Permissions** (readonly Grid), Checkbox "Vererbung unterbrechen" (Validierung: mind. eine eigene Permission) |
| Benutzer (`users-and-groups/user-mgmt`, `users/`) | Avatar, Benutzername, Vorname, Nachname, E-Mail, Firma, Zugangstyp, Status; Filter; Mehrfach-Löschen, **Entsperren** | Allgemein: userName, password/confirm (bei Anlage), email, lastName, firstName, UI-Sprache, Locale, Datensprache, User-Context, account_status, last_login_date, user_origin, access_type, active, tenant, contact_person, number, company, registration_date, approval_date; Profilbild Upload/Reset; **zugewiesene Gruppen** (hinzufügen/entfernen, Gruppe bearbeiten); **Passwort setzen** (nur Sharedien-eigene Benutzer, nicht extern) | Menü: Entsperren, Benutzer löschen; bei Registrierung "Registrierung abschließen" → Akzeptieren/Ablehnen |
| Benutzergruppen (`user-group-mgmt`, `user-groups/`) | Name, Beschreibung, Mitgliederzahl | name, description; **Mitglieder** (Suche, hinzufügen/entfernen, Grid Benutzername/Vor-/Nachname/E-Mail/aktiv) | |
| Registrierungen (`registration-mgmt`) | Name, E-Mail, Firma, Tenant, Ansprechpartner, registriert am, genehmigt am, Status, Zugangstyp; Aktionen Accept/Decline (einzeln + Mehrfach) | Detail = User-Edit | Selbstregistrierung (Tenant-Flag `accepts_registrations`) |
| User-Contexts (`user-context-management`) | Identifier (Link), Name, Default | identifier, name (mehrsprachig), default, sort_key | Default nicht löschbar; Kontexte = Märkte/Regionen für kontextabhängige Property-Werte und Lizenzen |
| Rollen (`security/role-mgmt`, `roles/`) | Identifier, Name, …, System, Aktiv; Menü Edit/**Copy**/Delete | Panels: **Allgemein** (identifier, name, object_types, mass_edit_entity_limit, export_entity_limit, active, is_system), **zugewiesene Permissions** (`role-permission-assignment-dialog`: Suche, Paging, Checkbox-Liste ObjectType-Permissions mit Beschreibung), **Metadaten-Restriktionen** (Property-Definitionen, die Rolle nicht sehen/bearbeiten darf), **Derivations-Restriktionen** (Asset-Typ × Derivations-Typ, nicht downloadbar), **Property-Value-Restriktionen** | Rolle kopieren (`/copy`) |
| Permission-Management (`security/permission-mgmt`) | Readonly-Liste aller Permissions (Name, Beschreibung), Suche, Paging | – | |
| Tenants (`tenant-management`) | Identifier (Link), Name, Default, Aktiv | identifier, name, active, accepts_registrations, default; **autorisierte Benutzergruppen** (Gruppe↔Rolle-Mappings hinzufügen/entfernen) | Default nicht löschbar; Multi-Tenant |
| Data-Languages (`configuration/data-language-management`) | Identifier, Name, Default, Aktiv | identifier, name, active, default | Steuert Datensprachen aller mehrsprachigen Properties |
| Entity-Types (`configuration/entity-types-management`) | Identifier, Name, System, Aktiv | identifier, name, description, active; **Permissions** (Gruppen × Rolle, nur Nicht-System) | Definiert Custom-Entity-Typen |
| Relation-Types (`relation-type-management`) | Identifier, Name, Beschreibung, System, gerichtet, Aktiv | identifier, name, description, directed, used_for_asset_search, active, system; **erlaubte Relationen** (From-Typ → To-Typ via `allowed-entity-relation-selection-dialog`) | |
| Search-Management (`search-management`) | Re-Indexing starten (Checkbox-Auswahl der Entity-Typen), Liste der Re-Indexing-Jobs (Kachel) → Report | – | `ReIndexingJobCreate` |
| Permission-Materialization (`permission-materialization`) | Jobs-Grid (Status, Identifier, Zeiten …), Filter, Refresh, **Rematerialisierung starten** je Eintrag | – | Rechte-Neuberechnung |
| Watermarks (`watermarks-management`) | Wasserzeichen-Dateien (Dateiname als Link), Upload, Ersetzen, Löschen | – | `canCreateWatermark` |
| File-Naming-Definitions (`filename-pattern-management`) | Identifier, Name, Format, erstellt von, globaler Default | identifier, name, **format** (Pattern mit Property-Platzhaltern), character_casings, absent_property_strategy, replace_illegal_characters, global_default | Für Download-Umbenennung (Kap. 7.5); Pattern-Syntax serverseitig (unklar) |
| Property-Definitions (`properties/property-definition-mgmt`) | Identifier, Name, Gruppe, Sortkey, ValueType, editierbar, System, Facette, Aktiv | identifier, name, description (mehrsprachig), **entity_type(s)**, group, sort_key, **value_type** (`String, Text, Boolean, Integer, Decimal, Float, Date, Time, DateTime, TimeSpan` + Listenvarianten `X_list`), **definition_type** (`Standard, Url, Html`), searchable (`NotSearchable, AdvancedSearchable, FulltextSearchable, AdvancedAndFulltextSearchable`), searchable_facet, editable, ignore_copying (bei neuer Version nicht kopieren), active, translatable, is_filenaming_unlocked (in Filename-Patterns nutzbar), has_user_context, is_system, allow_multiple_values, allow_custom_list_value; **Listenwerte** (Grid: identifier, value mehrsprachig, sort_key, status; Anlegen/Bearbeiten via `list-item-edit-dialog`, Aktivieren/Deaktivieren statt Löschen) | Kernkonfiguration des Metadatenmodells |
| Property-Groups (`property-groups-mgmt`) | Identifier, Name, Anzahl Definitionen, Sortkey, Aktiv | identifier, name, sort_key, active | |
| Property-Extraction-Definitions | Identifier, Extraktionstyp, Asset-Typen, Provider, erstellt, aktiv | identifier, extraction_type, asset_types, active, derivation_type; **Extraktionsquellen** (Grid; `property-extraction-source-edit-dialog`: provider, confidence_threshold, model_id, language) + Mapping auf Property-Definitionen | KI/Metadaten-Extraktion (z. B. Bilderkennung; Provider serverseitig) |
| Derivation-Types (`derivation-management/derivation-types-mgmt`) | Identifier, Name, System | identifier, name, is_system; **Default-Konfiguration** + je Asset-Typ (Sprungliste) Überschreibung: is_default_derivation, is_on_demand, allow_manual_upload, show_in_downloads, use_as_thumbnail, use_as_preview | = Formate/Renditions |
| Derivation-Job-Definitions | Identifier, Derivations-Typ, System | identifier, derivation_type, job_definition (Konverter-Konfiguration, Text/JSON unklar), input_file_types, output_file_type, is_system | |
| Watermark-Definitions | Identifier, Job-Definition, Property-Definition, Trigger, in UI anzeigen | identifier, derivation_job_definition, property_definition, display_in_ui; **Trigger** (`watermark-trigger-edit-dialog`: Listenwert oder Boolean-Wert → Wasserzeichen-Datei) | Wasserzeichen abhängig von Metadatenwert |
| File-Types (`file-types-mgmt`) | Readonly: Identifier, Asset-Typ, Dateiendung, MIME-Type; Suche | – | |
| Rules (`rule-management`) | Identifier, Name | Allgemein: identifier, name (mehrsprachig), rule_type (`Standard`, `MetadataExtraction`), entity_types; Panels **Filter**, **Trigger**, **Aktionen** (je Grid mit Hinzufügen/Bearbeiten/Entfernen über Dialoge); Button **Ausführen** (Mass-Validation-Job → Report) | Filter/Trigger-Operatoren `FilterAndTriggerOperator`: Equals, NotEquals, Contains, GreaterThan(OrEquals), LessThan(OrEquals), HasRelationType, IsNewVersion, ValueChanged, IsNewCategory, RegexMatch; Scopes: Property, Relation, EventTrigger (AdditionalData); Aktionen `RuleTypeAction`: SetProperty, ExecutePropertyExtraction; Property-Aktionen: SetPredefinedValue, SetValueFromTemplate, SetRegexValueFromTemplate |
| App-Settings (`app-settings`) | Tabs: **Assets** (forbidden_file_extensions, max_file_size_zip, zip_file_name_prefix, zip_name_date_format, zip_name_fallback, animated_filename_types, disable_duplicate_recognition(_check), filenaming_definition_fallback), **Contact-Print** (print_property_name, Properties je Layout 1/2/6/15 Bilder pro Seite), **E-Mail** (is_enabled, base_url, smtp_server, smtp_port, username, password, sender_name, sender_address, use_ssl), **File** (File-Usage-Types: name, max_file_size, allowed_file_extensions), **External Groups** (Liste: externer Gruppenname, Anzeigename, Anzahl Gruppen; Edit: identifier, display_name, zugeordnete Benutzergruppen – Mapping von IdP-Gruppen/Claims auf Sharedien-Gruppen) | | `ApplicationSettingsView/Create/Update` |
| Plugins (`plugins`) | leer / Platzhalter (Navigation auskommentiert) | | unklar |
| About (`about`) | Produktversion und Informational Version der API | | |

### 9.2 Lizenz-/DRM-Modul

Domänenmodell (aus UI abgeleitet):

| Begriff | Bedeutung / UI |
|---|---|
| **License** | Lizenz mit Name, Beschreibung, Laufzeit (period_start/end), aktiv; enthält **Buyouts** und zugeordnete Entities (Assets, Custom Entities je Entity-Typ) |
| **Buyout** | Konditionen einer Lizenz: Rights-Owner, Price-List, Dauer (from/to), Basis, Währung, Zusatzgebühr, Beschreibung; kopierbar |
| **Rights Owner** | Rechteinhaber (Name, Bild/Avatar, Billing-Contact, Properties, Historie) |
| **Billing Contact** | Rechnungskontakt (Name, E-Mail, Properties, zugeordnete Rights-Owner, Historie) |
| **Price List** | Preisliste (Name, Beschreibung, Status, Historie) mit **Country Percentage Split**: Baum der User-Contexts/Länder mit Prozentanteil, Suche, "leere ausblenden" |
| **User Context Group** | Gruppe von User-Contexts (Region), mehrsprachiger Name + zugeordnete Kontexte |
| **License Offer** | Angebot für Nutzer (aus Lizenz + Context-Group abgeleitet): Thumbnail, Lizenzname, Rechte, Region, Preise, Buyout-Zeitraum, enthaltene Assets (einblendbar), alternative Regionen, Hinweise "startet in Zukunft"/"läuft bald ab", Rights-Owner-Popup, Button **Bestellen** |
| **License Purchase** | Erfolgter Kauf: Lizenz, Region, Zeitraum, Rechte, enthaltene Assets, Status valid / about_to_expire / not_yet_valid / expired |

| Seite/Komponente | Funktion | Berechtigung |
|---|---|---|
| Admin `/admin/license-management` → `licenses` | Liste (Name → Detail-Panel, Beschreibung, Start, Ende, aktualisiert, aktiv), Quick-Search, erweiterte Suche, Facetten, Mehrfach-Löschen, "Lizenz erstellen"; Detail-Panel (`license-edit`, Vollbild-Toggle): Properties, Buyouts-Grid (Rights-Owner, Price-List, Basis, Zeitraum; Edit/Copy/Remove; `buyout-edit-dialog` mit Auswahl-Dialogen `rights-owner-selection-dialog`, `price-list-selection-dialog`), zugeordnete Entities je Typ (hinzufügen/entfernen, "Assets hinzufügen") | AND[`AdminView`,`LicenseView`], `canUpdateLicense` |
| `price-lists` | Liste + Filter, Detail-Panel (Allgemein, Country-Split-Baum, Historie `entity-history-summary`) | |
| `rights-owners` | Liste (Avatar, Name, Billing-Contact, aktualisiert, aktiv), Detail (Properties, Billing-Contact wählen `billing-contact-selection-dialog`, Bild, Historie) | |
| `billing-contacts` | Liste (Name, E-Mail, aktualisiert, Rights-Owner, aktiv), Detail (Properties, Rights-Owner-Liste, Historie) | |
| `permissions` (`license-permissions`) | `entity-resource-permissions` – wer darf Lizenzen verwalten (Rolle `licenseAdmin`) | |
| `user-context-groups` | Liste (Name, Kontexte), Detail (Name mehrsprachig, Kontexte hinzufügen/entfernen via `user-context-selection-dialog`) | |
| Custom-Entity-Tab "Lizenzen" (`license-offers`) | Angebote für diese Entity: Suche, Filter "nur gekaufte", "gültig ab" (Datum), User-Context, Rechte; Karten `license-offer-card` | `canViewLicenseOffer` |
| Kaufprozess (`license-purchase-dialog`) | Wizard: 1) **Optionen** (Zeitraum von/bis mit Enddatum, Region inkl. Alternativen via `license-offer-context-dialog`, Hilfe-Link aus Config `drm.purchaseHelpLink`) 2) **Zusammenfassung** (Lizenz, Beschreibung, Preise) → Kauf | `LicensePurchaseCreate` |
| Asset-Lizenz kaufen (`purchase-asset-license-dialog`) | Ausgewählte Assets als Karten + `license-offers` zur Auswahl | |
| Asset-Tab "Lizenzen" / `/license-purchases` (`entity-license-purchases`, `license-purchases`) | Liste der Käufe (Karten mit Status, Region, Zeitraum, Rechte, Assets einblendbar), Suche/Filter/Facetten, Button "Lizenz kaufen", Mehrfach-Löschen | `LicensePurchaseRead` |
| Rights-Check (`entities-license-rights-check`, Dialog) | Für ausgewählte Assets: Grid (Thumbnail/Name, Context-Group, Rechte, Start, Ablauf, Status), Filter (gültig ab, User-Context, Rechte), Hinweis "Lizenzen müssen gekauft werden" + Button "Lizenzen kaufen" (→ Offers), Hinweis "nicht eindeutig" | `canReadLicensePurchase` |
| Asset-Karte | Gültigkeits-Banner (`entityValidity`: to be launched / will expire (Schwelle `entityCardExpiryBannerThresholdDays`) / expired, je User-Context) | |

### 9.3 Berechtigungs-Katalog (`ObjectTypePermissions`, gruppiert)

Format `ObjectType(Verben)`; ~316 Einzelwerte.

- **UI-Bereiche**: Dashboard(View), AssetBrowser(View), Upload(View), DownloadBasket(View), Admin(View), GlobalAdmin, AssetDetail(View), Collection(View), CustomEntityBrowser(View)
- **Assets**: AssetSet, Version, AssetDerivation, AssetProperty (je CRUD+View), Asset(Download), CropDerivation(Create), AssetActivityLog(Read/View), AssetDerivationJob(CRUD+View), AssetPropertyMassEdit(CRUD+View), PropertyImport(Create/Read/View), PropertyExtractionJob, RecycleBin(View/Read/Delete)
- **Kategorien**: Category(CRUD+View+Download), CategoryProperty(CRUD+View), CategoryPropertyInheritance(Create/Update/Delete)
- **Property-Sets** (je CRUD+View, privat und `…Global`): PropertySet, AssetPropertySet, VersionPropertySet, CategoryPropertySet, WorkspacePropertySet, TaskPropertySet, CustomEntityPropertySet
- **Kollaboration**: Workspace(CRUD+View+Finalize), WorkspaceTemplate, WorkspaceProperty, WorkspaceActivityLog, Task, TaskProperty, TaskActivityLog, State, Comment, Share, SavedSearch
- **Custom Entities/Relationen**: Entity, CustomEntity, CustomEntityProperty, Relation, RelationType, EdgeType
- **Konfiguration**: PropertyDefinition, PropertyGroup, Property, PropertyExtractionDefinition, DataLanguage, FileNamingDefinition, Watermark, DerivationType(CRUD), DerivationJobDefinition, Rule, RuleJobReport(Read), RuleReApplyJob(Create), ReIndexingJob(Create/Read/View), PermissionMaterializationJob(View/Create/Read), ApplicationSettings
- **Sicherheit**: User, UserGroup, Role, NodePermission, Tenant, UserContext(Create/Update/Delete)
- **Lizenzen**: License(CRUD+View), LicenseOffer(Read/View), LicensePurchase(Read/Create)

Im UI werden zusätzlich **Entity-Permissions** pro Objekt ausgewertet (`entity.entityPermissions.canXxx`, vom Server geliefert, z. B. `canDeleteAssetSet`, `canCreateCategory`, `canUpdateSavedSearch`, `canDeleteShare`) sowie abgeleitete Flags in `SessionState.currentUserPermissions` (z. B. `canViewPropertySet(entityType, global)`).

---

## 10. Komponenten-Inventar

Kennzahlen: 329 Komponenten gesamt, davon ~60 generische Controls (`app/controls`), 48 Dialog-Komponenten (`app/dialogs`, zzgl. Dialoge in Feature-Ordnern), 26 Direktiven, 41 Pipes, 178 NGXS-States.

### 10.1 Controls (`app/controls`)

| Control (Selector) | Zweck | Wichtige Inputs | Lib |
|---|---|---|---|
| `app-actions-menu` (+ `-item`, `-divider`, `-header`, `-upload-item`) | Dropdown-Aktionsmenü ("…"), Einträge mit Icon/Text, Untermenüs, Upload-Eintrag mit Dateiauswahl | classes, text, icon, isDisabled; item: itemTextTranslationKey, icon, isDisabled; upload-item: allowMultipleFiles, customFileTypes | Kendo Popup |
| `app-activity-log` (+ changes/*) | Einzelner Aktivitäts-/Benachrichtigungseintrag mit Änderungen | activityLog, propertyDefinitions, isUnread, canMarkAsRead, canDelete | |
| `app-archive-extraction` | Einstellungen für ZIP-Extraktion (Modus, nach Extraktion löschen) | archiveExtractionSettings, showDeleteAfterExtractionOption | |
| `app-asset-selection-summary` / `app-item-selection-summary` | Zusammenfassung der Auswahl (Anzahl, Thumbnails) | selectedAssets / itemName, selectedItemsCount | |
| `app-audio-video-element` | Video-/Audio-Player | playerId, isVideo, isAudio, width, height, showPlayerControls | video.js |
| `app-avatar-selection` | Avatar hochladen/zurücksetzen, optional Thumbnail-Auswahl | avatarUrl, canChangeAvatar, canResetAvatar, isThumbnailSelection | |
| `app-avatars` | Avatar-Stapel (Benutzer/Gruppen) mit Overlap, "+n" | maxNumberOfAvatars, avatarSize, overlap, canAdd, canRemoveAll | |
| `app-basket-drop-area` | Drop-Zone "in Warenkorb" | – | DragDrop-Service |
| `app-collapsible-container` | Auf-/zuklappbares Panel mit Titel, optional selektierbar | identifier, isOpen, canCollapse, title, isSelectable, isSelected | |
| `app-comment-box` | Rich-Text-Kommentar-Editor mit @-Mentions | isReadOnly, canMentionUsers, userSourceForMentions | roosterjs, Kendo Popup |
| `app-content-popup` / `app-popup` | Verankertes Popup mit beliebigem Inhalt | anchor, anchorAlign, popupAlign, collision | Kendo Popup |
| `app-cropper` | Bildzuschnitt | width, height, imageUri | cropperjs |
| `app-derivation-status-icon` / `app-job-status-icon` | Status-Icon für Derivat/Job | derivationStatus / jobStatus | |
| `app-dialog` | Modaler Dialog-Rahmen (Header/Content/Footer, Vollbild, Fokus-Falle) | isOpen, isFullScreen, width, height, modalContentScrollable | |
| `app-drag-hint` | Drag-Vorschau (Text/Klon) | – | |
| `app-entity-card` | Universelle Kachel (Asset, Upload, Relation, Custom Entity) mit Hover-Aktionen, Status, Banner, Fortschritt | isMini, thumbnailUri, watermarkThumbnailUri, alternativeThumbnailUri, placeholderIcon, primary/secondaryTextProperty, can* (Download, Basket, Crop, Remove, Delete, Extract, Retry, Cancel, ViewRelations, ViewDetails, CreateTask, AddToTask), isDuplicate, entityValidity, linkedTasksInfo, progress | |
| `app-entity-carousel` | Horizontaler Karussell-Durchlauf von Entities (z. B. im Mehrfach-Editor) | activeEntityIdentifier, useMiniMode | |
| `app-entity-history-summary` | "Erstellt/geändert von/am" | entity | |
| `app-entity-path` | Breadcrumb-Pfad (Kategoriepfad), klickbar | pathSegments, excludeRoot, pathSegmentsClickable | |
| `app-entity-permission-table` | Tabelle User/Gruppe × Rolle | resourcePermissions, resourcePermissionType, canCreate/DeletePermission | Kendo Grid |
| `app-entity-preview` | Vorschau Bild/Video/Audio/PDF + Zoom | entityPreviewMediaSources, width, height, canZoom | |
| `app-entity-selection` | Paginierte, durchsuchbare Auswahl-Liste mit Item-Template | itemTemplate, canSearch, canPage, paginationStyle, pageSize | |
| `app-file-drop-handler` | Seitenweite Datei-Drop-Zone mit Hinweis-Overlay | isEnabled, disabledIfDialogIsOpen, dropHintText | |
| `app-file-naming-definition-selection` | Dropdown Filename-Pattern | fileNamingDefinitions, selectedIdentifier | Kendo DropDown |
| `app-focused-action-bar` | Kontext-Aktionsleiste oben (ersetzt Topbar bei Auswahl), Esc schließt | canClose | |
| `app-form-element` / `-editable` / `-readonly` / `app-form-validation-errors` | Formular-Zeile, typabhängiger Editor, Read-only-Anzeige, Fehlerliste | siehe Kap. 8.6 | Kendo Inputs/DateInputs/Dropdowns |
| `app-form-element-group-manager-dialog` | Generischer Formular-Dialog | – | |
| `app-horizontal-resizer` / `app-vertical-resizer` | Größenänderung von Panels, Breite persistiert | resizeTarget, minWidth/minHeight, usePersistedWidth | |
| `app-image-comparer` | Vorher/Nachher-Schieberegler | imageUris, width, height | |
| `app-info-container` | Info-/Warn-/Fehler-/Erfolgsbox | isInline, isLoading, isSuccess, isWarning, isError, text(TranslationKey) | |
| `app-item-count-badge` | Zähler-Badge | count, maxCount, isError | |
| `app-lightbox` | Lightbox-Rahmen | – | |
| `app-loading-overlay` / `app-loading-spinner` / `app-progress-circle` | Ladeanzeigen, Fortschrittskreis | size, thickness / percent | |
| `app-no-search-results-info-large` | Großer Leer-/Hinweis-Zustand | icon, headerText, infoText | |
| `app-pdf-element` | PDF-Viewer | pdfUri, width, height | ngx-extended-pdf-viewer |
| `app-property-definition-selection` | Auswahl/Anordnung von Property-Definitionen (für Spalten/Sets) | propertyDefinitionSet, entityTypeIdentifiers, canSelectMandatoryProperties | |
| `app-property-edit-limit-info` / `app-property-editing-summary` | Hinweis auf Mass-Edit-Limit / Anzahl ungespeicherter Änderungen + Fehler | entityCount, usesSearchQuery / unsavedChangesCount, validationErrorsCount | |
| `app-property-sets` | Liste/Auswahl von Property-Sets mit Kontextmenü | entityType, selectedPropertySetIdentifier, loadDefaultSet, hasContextMenu | |
| `app-property-value-comparer` | Diff-Darstellung eines Property-Werts | propertyValueCompare | diff-match-patch |
| `app-relation-graph` (Klassen in `controls/relation-graph`) | Force-Directed-Graph (Knoten/Kanten, Formen, Drag, Klick) | GraphOptions, NodeData, LinkData | d3 |
| `app-search-bar` | Einfaches Suchfeld | placeholder | |
| `app-search-box` (+ result-group/-item/more/no-results) | Suchfeld mit Type-ahead-Dropdown, Debounce, Esc | isTypeAheadSearchEnabled, minSearchValueLength, searchValue, isSearching, isDisabled | |
| `app-searchable-entity-selection-dropdown` | Dropdown mit Serversuche für Entity-Referenzen | pageSize, group, formElementName, configuration | Kendo ComboBox |
| `app-status-selection-form-element` | Statusauswahl (Task-/Entity-Status) | items, isTaskStatusElement, isEntityStatusElement | |
| `app-sub-header` / `app-sub-navigation` | Abschnittsüberschrift / Tab-Navigation aus Routen | value(TranslationKey) / routes | |
| `app-task-due-date` / `app-task-status` | Fälligkeit (überfällig rot) / Status-Anzeige | task | |
| `app-template-card` / `app-workspace-card` | Template-Kachel / Workspace-Kachel | title, thumbnailUri / workspace, can* | |
| `app-toggle-button` | Umschaltbutton (z. B. Rollenwahl) | displayText, checked, disabled | |
| `app-tooltip` | Tooltip (Hover/anchor, Warn-/Fehlerstil) | alignment, anchor, showOnHover, maxWidth | |
| `controls/tree` | Baum-Basis (Kategorien), NGXS `TreeState` | – | angular-tree-component |
| `app-upload-button` | Dateiauswahl-Button | allowMultipleFiles, customFileTypes, icon, classes | |
| `app-user-selection-dropdown` / `app-user-selection-inline` | Benutzersuche (Dropdown bzw. inline Liste) | pageSize, userSource, searchDebounceTimeMs | Kendo |
| `app-view-configuration` (+ `-action`, `-popup-action`) | Paging-/Anzeige-Leiste mit Popup-Aktionen (View-Switch, Sortierung, Spalten) | pagingState, showPages, canNavigateToFirst/LastPage | |
| `app-view-filter` | Filter-Panel in linker Spalte mit Menü | title, isOpen, isCollapsible, hasMenu | |
| `app-wizard` / `app-wizard-step` | Mehrstufiger Assistent (Schrittanzeige, Weiter/Zurück/Fertig, Validierung je Schritt) | wizardStyle, wizardControlsHidden/Disabled, finishButtonTranslationKey | NGXS `WizardStepState` |

### 10.2 Dialoge (`app/dialogs` und Feature-Dialoge)

| Dialog | Zweck |
|---|---|
| `confirmation-dialog`, `transient-confirmation-dialog` | Bestätigung (Header/Text/Button, optionale Checkbox "ich bin sicher") |
| `add-comment-dialog` | Kommentar hinzufügen |
| `allowed-entity-relation-selection-dialog` | From/To-Entity-Typ für Relationstyp |
| `asset-crop-dialog`, `asset-derivations-selection-dialog`, `asset-extraction-dialog`, `asset-finalization-dialog`, `asset-move-dialog`, `asset-properties-mass-edit-dialog`, `asset-removal-dialog`, `asset-selection-dialog`, `asset-upload-dialog`, `asset-version-compare-dialog` | siehe Kap. 7 |
| `billing-contact-selection-dialog`, `price-list-selection-dialog`, `rights-owner-selection-dialog`, `user-context-selection-dialog` | Auswahl im Lizenzmodul |
| `category-edit-dialog`, `category-selection-dialog`, `category-tree-configuration-dialog` | Kategorie anlegen/bearbeiten, auswählen (Baum), Baum-Optionen |
| `custom-entity-create-dialog` (Edit), `custom-entity-selection-dialog` | Custom Entities |
| `data-language-selection-dialog` | Datensprachen auswählen |
| `entities-license-rights-check-dialog` | Lizenz-Check |
| `entity-preview-dialog` | Große Vorschau einer Entity |
| `entity-properties-edit-dialog`, `entity-properties-edit-multiple-dialog`, `multiple-form-element-group-managers-dialog` | Property-Editor als Dialog (einzeln/mehrfach) |
| `export-asset-metadata-dialog`, `export-contact-print-dialog` | Export |
| `list-item-edit-dialog` | Listenwert einer Property-Definition |
| `property-definition-selection-dialog`, `property-extraction-definition-selection-dialog`, `property-extraction-source-edit-dialog`, `property-set-edit-dialog` | Property-Konfiguration |
| `regenerate-derivation-selection-dialog` | Derivate regenerieren |
| `role-selection-dialog`, `user-selection-dialog`, `user-group-selection-dialog` | Rollen/Benutzer/Gruppen wählen (mit Suche, Paging) |
| `saved-search-edit-dialog`, `share-edit-dialog`, `share-item-dialog` | Suchen/Shares |
| `state-lane-edit-dialog`, `state-lanes-dialog`, `task-selection-dialog`, `task-template-selection-dialog`, `template-selection-dialog`, `workspace-selection-dialog` | Kollaboration |
| Feature-Dialoge | `column-selection-dialog`, `workspace-create-dialog`, `workspace-edit-dialog`, `relation-type-selection-dialog`, `role-permission-assignment-dialog`, `file-usage-type-edit-dialog`, `watermark-trigger-edit-dialog`, `buyout-edit-dialog`, `license-purchase-dialog`, `license-offer-context-dialog`, `purchase-asset-license-dialog`, `sort-configuration-dialog` (Popup), `form-element-group-manager-dialog` |

**Dialog-Mechanik** (`services/dialog/dialog.service.ts`, `dialog-state-listener.service.ts`): Dialoge werden **state-getrieben** geöffnet – eine NGXS-Action `XxxDialogActions.InitiateDialog(...)` lädt Daten und setzt den Dialog-State; der `DialogStateListenerService` erzeugt daraufhin die Komponente dynamisch im `#dialogAnchor` (`ComponentFactoryResolver`). Ergebnis über Actions (`FinishDialog`/`CancelDialog`). Daneben "transiente" Dialoge (`openTransientDialog<TParam,TResult>` → `Observable<TransientDialogResult>` mit `canceled`). Popups (`services/popup`, Kontextmenüs `popup/context-menu` mit `ContextMenuConfiguration` → `contextMenuActionId`) und Lightboxen (`services/lightbox`) analog mit eigenen Ankern.

### 10.3 Popups (`app/popup`)

`configuration-menu` (Konfigurationsmenü), `context-menu` (generisches Kontextmenü mit Action-IDs, Divider, Untermenüs), `sort-configuration-dialog` (Sortier-Popup: Property + Richtung).

### 10.4 Direktiven (26)

| Direktive | Zweck |
|---|---|
| `*ifPermission`, `*ifPermissions` | Rechtebasiertes Rendern (Kap. 5) |
| `[avatar]`, `[userGroupAvatar]` (`base-avatar`) | Avatar-Bild oder Initialen-Fallback für User/Gruppe |
| `[dragInitiator]`, `[dragReceiver]` | Drag&Drop-Verhandlung (Kap. 7.1) |
| `elementDragStarted/DragOver/DragEnd/Drop`, `fileDragEvents` | DOM-Drag-Events, Datei-Drag |
| `clickStopPropagation`, `escapeKeyUp`, `focus`, `scrollIntoView`, `scrollTopOnItemsChange`, `horizontalScroll`, `textareaExpand`, `elementInitialized`, `elementResized`, `elementScrolled` | UI-Helfer |
| `disableControl`, `formGroupObserver` | Reactive-Forms-Helfer |
| `imageZoom` | Zoom/Pan für Lightbox |
| `dynamicComponentAnchor` | Anker für dynamische Komponenten (`FullScreenAnchor`) |
| `[t]` (i18next) | Übersetzung als Attribut, `t-options` für Parameter |

### 10.5 Pipes (41)

Übersetzung: `t` (i18next), `tTranslatedValue` (mehrsprachiger Wert in aktueller Datensprache mit Fallback), `tHasTranslatedValue`, `tAssetType`, `tAssetStatus`, `tEntityType`, `tStatus`, `tTaskStatus`, `tTaskType`, `tTaskTransition`, `tUploadStatus`, `tOperator`, `tMultiTermOperator`, `tSortOrder`, `tAccountStatus`, `tContactPrint`, `tCharacterCasings`, `tPropertyStrategy`, `tPropertyDefinitionSearchableValue`, `tPropertyMutationResult`, `tSetPropertyResultReason`, `tValidationError`, `tArchiveExtractionMode`, `tTranslateArchiveEntryStatus/Type`, `tTranslateBackgroundJobTriggerType`. Formatierung: `fileSize`, `dateLabel`, `daysToString`, `brackets`, `propertyValue`, `arrayToString`. Lookup: `entityFromArray`, `entityFromDictionary`, `arrayContains`, `allExcept`, `elementSelectionStateToBoolean`, `user` (async Benutzername aus Cache), `workspace` (async). Icons: `assetTypeIcon`, `entityTypeIcon`. Zusätzlich Kendo-Pipes `kendoDate`, `kendoNumber`.

### 10.6 Services (`src/services`)

| Service | Zweck |
|---|---|
| `ApiService` → `SharedienApiService` | **Handgeschriebener** REST-Client (4 452 Zeilen, ~208 Methoden, Basis `${apiUrl}/api/v6`), DTO↔Model-Mapping über `models/mapper`, `models/factory` und Decorators `@DtoProperty/@CreateProperty/@UpdateProperty`; Ergebnis `ApiResponse<T>` |
| `ApiDownloadService` → `SharedienApiDownloadV1Service` | Downloads (Form-POST für ZIP, `window.open` für Einzeldateien, Public-Share) |
| `UploadService` → `SharedienUploadService` | Chunked Upload (Kap. 7.4) |
| `IdentityProviderApiService` → `SharedienUaaApiService` | IdP-Aufrufe (Passwort ändern/setzen) |
| `UserApiService` → `UserManagementService`, `UserService`, `UserCacheService` | Benutzer, Session-User, `hasPermission(s)`, Benutzer-Cache für `user`-Pipe |
| `ApiAuthInterceptor`, `NoCacheApiInterceptor` | Bearer-Token an API-Requests; No-Cache-Header |
| `SharedienApiMockInterceptor`, `BaseApiMockService`, `I18NextMockService`, `IntlMockService` | Mocks für Unit-Tests |
| `DialogService`, `DialogStateListenerService`, `PopupService`, `LigthboxService`, `DynamicComponentService` | Dynamische Overlays |
| `DragNegotiationService`, `DragHintService` | Drag&Drop |
| `SignalRService`, `ToastNotificationService` | Realtime, Toasts |
| `ThemeService`, `FaviconService` | Theming, Favicon aus Config |
| `KeyUpListenerService` | Globaler Keyup-Stack (Esc schließt obersten Popup/Action-Bar/Suche) |
| `RoutingHistoryService`, `RoutingParamService` | Zurück-Navigation, Query-Param-Handling |
| `StorageService` → `LocalStorageService` | Persistenz (Panelbreiten, ausgewählte Filter, Mini-Mode …) |
| `LoggerService` → `ConsoleLoggerService` | Logging mit Level |

**API-Ressourcen** (aus URL-Segmenten): assets, versions, derivations, derivation-types, derivation-job-definitions, file-types, asset-types, asset-search-types, categories, recycle-bin, relations, relation-types, allowed-entity-relations, properties, property-definitions, property-groups, propertysets, value-types, custom-entities, entity-types, entities, workspaces, states, transitions, tasks, task-types, assignees, comments, activity-logs, watchers, shares, recipients, public, saved-searches, search, quicksearch, jobs, report, extractions, duplicates, users, user-groups, roles, object-type-permissions, resource-permission-settings, usergroup-permissions, property-definition-restrictions, property-value-restrictions, derivation-type-restrictions, tenants, user-context, data-languages, file-naming-definitions, tokens, watermark-definitions, triggers, rules, execute, application-settings, email, file, contact-print-metadata, permission-materializations, drm, my/profile/settings, avatar, info, forbidden; Aktionen: move, link, unlink, copy, copy-properties, copy-all-properties, bulk-delete, restart, validate, mutations, upload.

### 10.7 NGXS-State-Architektur

- 178 States. Generische Basis: `EntityManagementState` (Listen: Items via `EntityAdapter`, Selektion, Paging, Sorting, Filter, Suche, Facetten, View-Mode) und **`EntityManagementInstanceState`/`InstancedState`** – ein State kann mehrere Instanzen haben (`stateInstanceKey`), damit dieselbe Komponente (z. B. `AssetsComponent`) parallel in Browser, Dialog und Workspace läuft.
- Querschnitt-States (`app/state`): `session-state` (Benutzer, Permissions, Data-Languages, User-Contexts, Entity-Types, Property-Definitionen/-Gruppen, UI-Settings, Forbidden-File-Types, Druckmodus, Admin-Navbar), `navigation` (Navigationseinträge + Badges + Drop-Receiver), `category-tree`, `tree`, `filters`, `paging`, `sorting`, `entity-browser`, `entity-relations`, `asset-versions`, `properties-edit`, `property-definitions`, `permissions`, `shares`, `download` (Action-Handler), `dialog`, `wizard-step`, `ui-notifications`, `event-notifications`, `router` (NGXS Router-Plugin + Custom Serializer).
- Pro Feature eigener `state/`-Ordner (Model, Actions, State, teils Action-Handler); Dialoge haben je eigenen State (Init → Load → Finish/Cancel).
- Redux-DevTools-Plugin aktiv.

### 10.8 Models (`app/models`)

Gruppen: Asset/Version/Derivation/AssetType/AssetStatus/Validity, Category, PropertyDefinition/-Group/-ListItem/-Set/-Value/Mutation/MassEdit, CustomEntity/EntityType/GenericEntity, Relation/RelationType, Workspace/Template/State/Task, Comment/ActivityLog/EventNotification, Share, SavedSearch/SearchQuery (`AssetSearchQuery.fromQueryParams`), Download (ZipDownloadRequest …), Upload (UploadableAsset, UploadChunk, UploadStatus), Jobs/Reports, Rules, DRM (License, Buyout, PriceList, RightsOwner, BillingContact, LicenseOffer, LicensePurchase, UserContextGroup), User/UserGroup/Role/Tenant/UserContext/ResourcePermission/ObjectTypePermission, UI (ViewMode, NavigationItem, UiSettings, UiLanguage, Locale), Errors (ClientError). DTOs in `models/dto`, Mapper in `models/mapper`, Factories in `models/factory`.

---

## 11. Realtime (SignalR)

| Aspekt | Implementierung (`services/notification/signalr.service.ts`) |
|---|---|
| Aktivierung | Nur wenn `sharedienAppConfig.hubUrl` gesetzt; sonst `EnableEventNotifications(false)` → Glocke ausgeblendet |
| Verbindung | `HubConnectionBuilder().withUrl(hubUrl, { accessTokenFactory })` (Token aus OAuth-Storage, optional "Bearer "-Prefix), `withAutomaticReconnect` mit festen 15 s Retry, Status `Connecting/Connected/Disconnected` im `EventNotificationsState` → Indikator im Notifications-Popup |
| Konsumierte Hub-Methoden | **nur eine**: `Publish` (Payload `EventNotification` → als `ActivityLogUpdate` interpretiert: `type: ActivityLogType`, `triggeredByUserIdentifier`, `affectedData {identifier, entityTypeIdentifier, objectType, name, additionalData, additionalAffectedData}`, `createdAt`) |
| Reaktion | `eventReceived`-Subject (im Code aktuell ohne weitere Abonnenten) + `ActivityLogsInboxActions.ReloadActivityLogs` → Inbox/Unread-Badge neu laden |
| Event-Typen (`ActivityLogType`) | TaskCreated, TaskUpdated, TaskDeleted, WorkspaceCreated, WorkspaceUpdated, WorkspaceDeleted, AssetCreated, AssetUpdated, AssetDeleted, CategoryCreated, CategoryUpdated, CategoryDeleted, RelationCreated, RelationDeleted, JobUpdated |
| AdditionalData-Schlüssel | OldValue, NewValue, PropertyDefinitionIdentifier, LanguageIdentifier, UserContextIdentifier, ValidUntil, Status, RelationTypeIdentifier |
| Kein Realtime für | Upload-Fortschritt anderer, Job-Status, Derivate → stattdessen **Polling**: Formate-Tab 5 s, Workspace-Upload-Wizard 5 s + Backoff, Upload-History Backoff (3 s, max. 10 Intervalle), Refresh-Buttons in Kacheln/Reports |

Konsequenz für Shareme: Mindestens Activity-Log-Push + Inbox-Reload; Live-Updates von Jobs/Derivaten wären ein Mehrwert, sind aber kein Paritäts-Erfordernis.

---

## 12. State-Management und API-Client (Zusammenfassung)

- **NGXS** (Redux-Pattern) mit 178 States, generischen Entity-Management-Basisklassen und Instanz-Keys; Router-Plugin; DevTools. Dialoge, Wizards und Listen sind vollständig state-getrieben (Action → Effekt im `@Action`-Handler → API → patchState).
- **API-Client** handgeschrieben (nicht generiert), abstrakte Basis `ApiService` + Implementierung `SharedienApiService`, REST `/api/v6`, ~208 Methoden, DTO-Mapping mit eigenen Decorators; alle Antworten als `ApiResponse<T>` (success, value, Fehler).
- **Mehrsprachige Werte** überall als `TranslatedValue[]` (`{ languageIdentifier, value }`), Anzeige über `tTranslatedValue`-Pipe mit Fallback auf Default-Sprache.
- **Persistenz im Browser**: LocalStorage (Panelbreiten, Filterauswahl `selected_filters_storage`, Mini-Mode, OAuth-Tokens); User-Settings serverseitig (`updateMySettings`).
- **Suchzustand in der URL** (Query-Params) → Deep-Links/Bookmarks.

---

## 13. Theming, Branding, Styling

| Aspekt | Details |
|---|---|
| Mechanik | `ThemeService` lädt CSS-Datei `configuration/themes/<themeId>.css` als `<link sh-theme>`; Themes können andere erweitern (`extends`, rekursiv zuerst geladen); Laufzeit-Umschaltung; `css-vars-ponyfill` für IE11 |
| Konfiguration | `branding.theming.themes[] {id,name,extends}`, `defaultTheme`, `userCanSwitchThemes` (dann Auswahl in User-Settings, gespeichert in `uiSettings.theme`) |
| Standard-Theme | `sharedien_light_theme.css` mit **~490 CSS-Custom-Properties**: Farbpalette (Material: Blue/Green/Orange/Red/Grey 50–900 + A100–A700, Primary/Success/Error/Neutral/Warning), Gruppen *Main* (General, Page, Top bar, Bottom bar, Side bar, Side bar items, Side bar drop down, Loading indicators, Drag hint, Drop area, Action bar), *Buttons*, *Links*, *Modals*, *Popups*, *Components* (Activity logs, Asset formats, Asset format selection, Asset removal dialog, Asset search hint, Asset versions, Avatar, Basket, Calendar, Category filter, … weitere Komponenten) |
| Branding | Sidebar-/Topbar-App-Logo, Brand-Logo, Header-Text, Footer-Links (mit Übersetzungsschlüsseln), Favicon, übersetzbare Texte via `override`-Namespace |
| SCSS (`styles/`) | `styles.scss` importiert: fontface (Roboto), variables, kendo_variables_overrides, mixins (dialog, drop-area, lightbox, tiles), button, link, loader, toast, textbox, textarea, rich-text-editor, forms, input, icons, dynamic_components, **Kendo Material Theme** (`kendo-theme-material/scss/all`) + Overrides (grid, dropdownlist, checkbox, popup), angular-tree-overrides, video-js-overrides, **print**, graph, diff-match-patch |
| Druck | `_print.scss` + `print-outlet` + `SessionState.isPrinting` (Shell ausgeblendet) |
| Icons | Font Awesome Pro (light/regular/solid/brands) |
| Responsiveness | Desktop-orientiert (Mini-Mode der Sidebar, resizable Panels); explizites Mobile-Layout nicht erkennbar (unklar) |

---

## 14. Internationalisierung

| Aspekt | Details |
|---|---|
| UI-Sprachen | **Englisch (`en`) und Deutsch (`de`)**, `locales/{en,de}/translation.json` (~3 950 Zeilen je Sprache, 155 Top-Level-Namespaces), Fallback `en`, Whitelist `['en','de']`, `load: 'languageOnly'` |
| Bibliothek | i18next 11 + XHR-Backend (lädt JSON zur Laufzeit) + Browser-Language-Detector; eigene Angular-Integration (`i18next/`: Pipe `t`, Direktive `[t]` mit `t-options`, `I18NextService`) |
| Kunden-Overrides | Namespace `override` (`locales/<lng>/override.json`, optional) wird per Deep-Merge über `translation` gelegt – Textanpassungen ohne Rebuild |
| Locale | Separate Einstellung (Datums-/Zahlenformat) über Kendo Intl (`LOCALE_ID`), wählbar in User-Settings |
| Datensprachen | Unabhängig von der UI-Sprache: Admin-definierte **Data-Languages** für mehrsprachige Inhalte (`TranslatedValue[]`), Default-Datensprache je Benutzer, Filter "Datensprache" in Browsern/Editoren, Suchoption "Sprachen" |
| User-Contexts | Zusätzliche Dimension für kontextabhängige Property-Werte (Markt/Region) |
| Seitentitel | Aus `displayNameTranslationKey` der Route |

---

## 15. Tests

| Art | Umfang |
|---|---|
| e2e (Protractor, `e2e/`) | **Nur Angular-CLI-Platzhalter**: `app.e2e-spec.ts` prüft "Welcome to app!" im `h1` – faktisch keine e2e-Abdeckung (Test wäre gegen die echte App fehlschlagend) |
| Unit (Karma/Jasmine) | 44 Spec-Dateien, fast ausschließlich **NGXS-State-Tests**: Admin-States (Category-, DataLanguage-, EntityType-, RelationType-, PropertyDefinition-, PropertyGroup-, Permission-, Role-, RolePermissionAssignment-, Tenant-, UserContext-Management inkl. Edit-States), Dialog-States (ColumnSelection, PropertyDefinitionSelection, PropertySets, CategoryEdit, CategorySelection, DataLanguageSelection, EntityPropertiesExport, ListItemEdit, PropertySetEdit, ShareEdit, ShareItem, WorkspaceSelection, SortConfiguration), Querschnitt (Roles, CategoryTree, EntityAdapter, EntityRelationsHelpers, Navigation, Session, Shares, UploadManagement), Utils (`compare`, `date-util`, `url-util`), Pipe `file-size` |
| Mocks | `SharedienApiMockInterceptor`, `BaseApiMockService`, `I18NextMockService`, `IntlMockService`, `faker` |

---

## 16. Hosting / Deployment

- Statische SPA; `Web.config` (IIS/Azure: HTTPS-Redirect, SPA-Rewrite, kein Caching für `configuration/` und `locales/`), `rewrite.rules` (Apache mod_rewrite für On-Premise/Http.Sys), Docker-Image `sharedien/sharedien-web`.
- `version.json` vom CI geschrieben; Laufzeit-Konfiguration per `configuration.overrides.js` je Umgebung.
- Silent-Refresh-Seite `auth-silent-token-refresh.html`.

---

## 17. Kennzahlen und Komplexitätsbewertung

### 17.1 Kennzahlen

| Metrik | Wert |
|---|---|
| TypeScript-Dateien | 2 118 |
| Komponenten (`*.component.ts`) | 329 |
| davon Controls / Dialoge | ~60 / 48 (+ ~12 Feature-Dialoge) |
| Direktiven / Pipes | 26 / 41 |
| NGXS-States | 178 |
| Routen Hauptmodul | 31 Einträge (inkl. 17 Kind-Routen: Asset 7, Custom Entity 4, Workspace 6 inkl. Redirects; Upload-Management 2; Print 1) |
| Routen Admin-Modul | ~70 (Listen, Create, Update, Copy, Tab-Kinder) |
| Seiten/Views (fachlich) | ~75 (≈ 30 Nutzerseiten/Tabs + ≈ 45 Admin-Seiten/Edit-Seiten) |
| Berechtigungen | ~316 `ObjectTypePermissions` |
| Übersetzungsschlüssel | ~3 950 Zeilen je Sprache (en, de) |
| API-Methoden | ~208 |

### 17.2 Besonders komplex (Aufwandstreiber für Shareme)

1. **`AssetsComponent`** (≈ 3 000 Zeilen, ~45 Konfigurations-Inputs): Galerie + virtualisiertes, inline-editierbares Grid mit typabhängigen Editoren, Facetten, erweiterte Property-Filter mit 13 Operatoren, Saved Searches mit Freigabe, Property-Sets, Kategorie-/Papierkorb-Bäume, Drag&Drop-Verhandlung, Selection-Action-Bar, Mehrfach-Einbettung über State-Instanzen.
2. **Generischer Metadaten-Editor** (einzeln, mehrfach, Vergleich, Druck) auf Basis dynamischer Property-Definitionen: 10 Value-Types × Liste/Multi/Custom-Value × mehrsprachig × User-Context × Vererbung × Füllstand (Pflicht/Empfohlen) × Sperren.
3. **Chunked Upload mit Queue** und mehrstufigem Upload-Wizard (Kategorie, Upload-Session/Template, Workspace-Properties, Relationen, Archiv-Extraktion) + Workspace-Upload-Wizard (Klassifizierung, Finalisierung).
4. **Download-Pipeline**: Derivat-Auswahl je Asset-Typ, On-Demand-Generierung, Filename-Patterns, Warenkorb mit mehreren Bundles, ZIP per Form-POST, Public Shares mit Token.
5. **Rechtemodell**: 316 Objekttyp-Permissions + objektbezogene Entity-Permissions + Rollen mit Metadaten-/Derivat-/Wert-Restriktionen + Kategorie-Rechte mit Vererbung/Unterbrechung + Materialisierung; Workspace-Rollen (Reader/Collaborator/Owner).
6. **Lizenz-/DRM-Modul**: License, Buyout, PriceList (Country-Split-Baum), RightsOwner, BillingContact, UserContextGroups, Offers, Kauf-Wizard, Rights-Check.
7. **Regel-Engine-UI** (Filter/Trigger/Aktionen mit Operatoren und Templates) und Property-Extraction (Provider, Confidence).
8. **Kanban** mit konfigurierbaren State Lanes, Transitions und Lane-Rechten.
9. **d3-Relationsgraph**.
10. **Konfigurationsgetriebenes Branding** (490 CSS-Variablen, Theme-Vererbung, Text-Overrides, Laufzeit-Config).

### 17.3 Offene Punkte (unklar im Frontend)

- Routen-Rechteprüfung für Nicht-Admin-Routen ist im Guard auskommentiert – Schutz erfolgt nur über Navigation/UI-Flags und API.
- `/saved-search-management` und `/admin/plugins` sind definiert, aber ohne Funktion.
- Share-Passwortschutz, Download-Sperre bei fehlender Lizenz, Filename-Pattern-Syntax, Job-Definition-Format, Papierkorb-Restore-Semantik: serverseitig, im Frontend nicht ersichtlich.
- Rich-Text-Toolbar-Umfang (roosterjs) nicht explizit konfiguriert.
