# Sharemee – Betriebshandbuch

Stand: Oktober 2026 (Branch `feat/p9a-importer`). Alles hier ist gegen den Code von Sharemee und Coworkee geprüft; Abschnitte mit **Offen** beschreiben Lücken, die vor einem Produktivbetrieb geschlossen werden müssen.

## 1. Überblick und Architektur

| Dienst (Aspire-Name) | Projekt | Aufgabe | Braucht |
|---|---|---|---|
| `shareme-migrations` | `src/Shareme.Migrations` | Wendet die EF-Core-Migrationen an und beendet sich (Exit-Code 0 = ok, 1 = Fehler) | Postgres |
| `shareme-auth` | `src/Shareme.Auth` | OpenID-Connect-Server (OpenIddict), Login-Seiten, Kontoverwaltung | Postgres |
| `shareme-api` | `src/Shareme.Api` | REST-API (`/api/v1/...`), SignalR-Hub, MCP (`/mcp`), Hangfire-Dashboard, Hangfire-Worker für `default`, `mail`, `search` | Postgres, Redis, Elasticsearch, SMTP, Blob-Storage |
| `shareme-processing` | `src/Shareme.Processing.Host` | Hangfire-Worker nur für die Queue `processing` (Renditions, Vorschauen, technische Metadaten); keine Endpunkte außer Health | Postgres, Redis (Pflicht), Blob-Storage (derselbe wie die API), Render-Tools |
| `shareme-web` | `src/Shareme.Web` | Blazor-WebAssembly-Host und BFF: Cookie-Login per OIDC, leitet `/api`, `/admin/jobs` und `/hubs` mit Access-Token an die API weiter | Auth, API |

Infrastruktur (aus `AddCoworkeeApp` in `Coworkee.Aspire`; jeder Dienst bekommt, was seine Coworkee-Pakete brauchen):

- **PostgreSQL** – eine Datenbank (`shareme`) für alles: Fachdaten, Identity, Einstellungen, Audit, Data-Protection-Keys (Schema `cw`), Hangfire (Schema `hangfire`). Migrationshistorie in `cw."__EFMigrationsHistory"`.
- **Redis** – nur SignalR-Backplane (Channel-Präfix `coworkee`). Der Processing-Host meldet fertige Renditions darüber an die Browser; ohne `redis`-Connection-String startet er nicht.
- **Elasticsearch** – Volltextsuche, Alias `shareme-nodes` (konfigurierbar), physische Indizes werden beim Reindex getauscht.
- **SMTP** – lokal Mailpit, produktiv ein beliebiger SMTP-Server (konfiguriert über Admin-Einstellungen).
- **Blob-Storage** – FileSystem, S3 (auch MinIO) oder Azure Blob. API und Processing-Host müssen denselben Speicher sehen.
- **Keycloak** – lokal als externer Login am Auth-Dienst (Realm `shareme`, Client `shareme-auth`, Benutzer `demo@sharemee.local`). Passwort und Client-Secret erzeugt der AppHost als Parameter (`shareme-keycloak-user-password`, `shareme-keycloak-client-secret`, in den User-Secrets und im Dashboard). Neue Keycloak-Benutzer werden beim ersten Login angelegt (ohne Rollen); bestehende werden über die bestätigte E-Mail-Adresse verknüpft.

## 2. Lokal starten (Aspire)

Voraussetzungen: .NET-10-SDK (`global.json`: 10.0.300, `rollForward: latestFeature`), Docker, Coworkee-Repository als `../coworkee`.

```bash
pwsh ../coworkee/build/pack-local.ps1          # Coworkee-Pakete lokal bauen
dotnet run --project src/Shareme.AppHost       # startet alles, Dashboard-URL steht in der Konsole
dotnet test --solution Shareme.slnx
```

Mit `-p:CoworkeeReferenceMode=Project` wird gegen die Coworkee-Quellen statt gegen die lokalen Pakete gebaut.

Was der AppHost (`src/Shareme.AppHost/AppHost.cs`) tut:

```csharp
var app = builder.AddCoworkeeApp("shareme", options =>
{
    options.DisplayName = "Sharemee";
    options.UseKeycloak(keycloak => keycloak.Users.Add(new KeycloakUser("demo@sharemee.local", "Demo", "User")));
});

app.AddMigrations<Projects.Shareme_Migrations>();
app.AddAuthServer<Projects.Shareme_Auth>();
app.AddApi<Projects.Shareme_Api>();
app.AddWorker<Projects.Shareme_Processing_Host>("processing");
app.AddWeb<Projects.Shareme_Web>();
```

- Welche Infrastruktur ein Dienst bekommt, liest `AddCoworkeeApp` aus dessen `obj/project.assets.json`: `Coworkee.Infrastructure` → Datenbank und Warten auf die Migrationen, `Coworkee.Realtime` → Redis, `Coworkee.Mailing` → Mailpit als Einstellungs-Default, `Coworkee.Storage` → `.data/blobs`, `Coworkee.Search.Elasticsearch` → Elasticsearch.
- Postgres, Redis, Elasticsearch und Keycloak laufen mit Daten-Volume. Mit `Coworkee:EphemeralInfrastructure=true` in der AppHost-Konfiguration werden sie flüchtig.
- Elasticsearch läuft lokal mit `-Xms512m -Xmx512m -XX:TieredStopAtLevel=1` und `cluster.routing.allocation.disk.threshold_enabled=false` (volle Entwicklerplatten blockieren keine Shards). Produktiv gilt das nicht.
- Der OIDC-Client `shareme-web` (öffentlicher Client, PKCE) und der API-Scope `shareme_api` werden am Auth-Dienst angelegt; Auth läuft ohne Hangfire-Server.
- Optionales festes Setup-Token: `dotnet user-secrets set Coworkee:SetupToken <wert> --project src/Shareme.AppHost`.

## 3. Produktion betreiben

### 3.1 Container und Ressourcen

- Nur der Processing-Host hat ein Dockerfile (`src/Shareme.Processing.Host/Dockerfile`, Basis `mcr.microsoft.com/dotnet/aspnet:10.0`, mit FFmpeg, Ghostscript, ExifTool, LibreOffice Writer/Calc/Impress, Schriften DejaVu/Liberation; **ohne Blender**):
  ```bash
  dotnet publish src/Shareme.Processing.Host -c Release -o publish/processing
  docker build -f src/Shareme.Processing.Host/Dockerfile -t shareme-processing publish/processing
  ```
- Für Migrations, Auth, API und Web gibt es noch keine Deployment-Artefakte im Repository (**Offen**).
- Processing braucht Platz im Temp-Verzeichnis (`Shareme:Processing:TempRoot`): Originale werden pro Worker dorthin kopiert. Worker-Anzahl = `max(1, Prozessorkerne / 2)`, jede Rendition nutzt bis zu zwei Threads.
- ImageMagick (Magick.NET, eingebettet) ist begrenzt auf 16 000 × 16 000 px, 200 Megapixel Fläche, 1 GB Speicher, 120 s pro Bild. Größere Bilder schlagen fehl statt den Host zu blockieren.
- Reverse-Proxy vor `shareme-web`: Request-Bodies bis 101 MB zulassen (Kestrel-Limit des BFF; Rendition-Uploads sind auf 100 MB begrenzt, normale Uploads laufen in 8-MB-Chunks bis 20 GB pro Datei).

### 3.2 Reihenfolge beim Start

1. Postgres, Redis, Elasticsearch, SMTP, Blob-Storage bereitstellen.
2. `Shareme.Migrations` einmal laufen lassen; nur bei Exit-Code 0 weiter.
3. Auth, API, Processing, Web starten. Elasticsearch darf beim Start fehlen: der Index wird bei der ersten Nutzung angelegt, Index-Jobs wiederholen sich.

### 3.3 Konfiguration

Konfiguration kommt aus den üblichen ASP.NET-Quellen (Umgebungsvariablen mit `__` statt `:`). Es gibt im Repository keine `appsettings*.json`; alles Nötige muss gesetzt werden.

**Connection-Strings**

| Key | Zweck | Default |
|---|---|---|
| `ConnectionStrings:shareme` | Postgres für alle Dienste, auch Hangfire-Storage | – (Pflicht) |
| `ConnectionStrings:redis` | SignalR-Backplane; Pflicht für Processing, für die API nötig, sobald Processing oder mehrere API-Instanzen laufen | – |
| `ConnectionStrings:elasticsearch` | Elasticsearch-URL; `http://user:pass@host:9200` wird als Basic Auth genutzt | `http://localhost:9200` |

**Coworkee**

| Key | Zweck | Default |
|---|---|---|
| `Auth:Authority` (API) | URL des Auth-Dienstes für JWT-Validierung; `http://` schaltet `RequireHttpsMetadata` ab | – (Pflicht) |
| `Coworkee:SetupToken` (API) | Token für die Ersteinrichtung | zufällig je Prozess, im Log |
| `Coworkee:Account:PublicAuthUrl` | Basis für Links in Konto-Mails (Passwort-Reset, Bestätigung) | leer |
| `Coworkee:Notifications:PublicAppUrl` | Basis für Links in Digest- und Freigabe-Mails (Web-App-URL) | leer (relative Links) |
| `Coworkee:Notifications:DigestCron` | Zeitpunkt des Tages-Digests (Cron, UTC) | `0 6 * * *` |
| `Coworkee:Auth:DisplayName` | Anzeigename des Auth-Servers | `Coworkee` |
| `Coworkee:Auth:AllowHttp` | OpenIddict ohne HTTPS-Pflicht (nur hinter TLS-terminierendem Proxy) | `false` |
| `Coworkee:Auth:AccessTokenLifetime` | Lebensdauer der Access-Tokens | `00:15:00` |
| `Coworkee:Auth:ApiScopes:shareme_api` | API-Scope; muss `shareme_api` sein (Audience der API) | – |
| `Coworkee:Auth:Clients:N:*` | OIDC-Clients (`ClientId`, `DisplayName`, `ClientSecret`, `RedirectUris`, `PostLogoutRedirectUris`, `Scopes`); werden beim Start angelegt/aktualisiert. Für Sharemee: `shareme-web`, Redirect `<web>/signin-oidc`, Logout `<web>/signout-callback-oidc`, Scope `shareme_api` | – |
| `Coworkee:Bff:Authority` / `ClientId` / `ClientSecret` | OIDC-Anbindung des Web-Hosts | – / – / leer (öffentlicher Client) |
| `Coworkee:Bff:ApiAddress` | Ziel der Weiterleitung (API-URL) | – |
| `Coworkee:Bff:Scopes:N` | zusätzliche Scopes, `shareme_api` | leer |
| `Coworkee:Bff:ForwardedPrefixes:N` | weitere weitergeleitete Pfade neben `/api`; für Sharemee `/admin/jobs` und `/hubs` | leer |
| `Coworkee:Bff:AuthorizationEndpoint` | abweichender öffentlicher Authorize-Endpunkt (Token-/Logout-Endpunkt werden dann aus `Authority` gebildet) | leer |
| `Coworkee:Jobs:RunServer` | Hangfire-Server in diesem Prozess | `true` (Auth: `false` setzen) |
| `Coworkee:Jobs:Queues:N` | bediente Queues | API: `default`, `mail`, `search` (nur wenn nicht gesetzt); Processing fest `processing` |
| `Coworkee:Jobs:WorkerCount` | Hangfire-Worker | `5`; Processing fest `Kerne / 2` |
| `Coworkee:Jobs:Attempts` | Versuche je Job | `3` |
| `Coworkee:Jobs:RetryDelaysInSeconds:N` | Wartezeiten zwischen Versuchen | `10, 60, 300` |
| `Coworkee:Jobs:PollingInterval` | Queue- und Schedule-Polling | `00:00:15` |
| `Coworkee:Jobs:DashboardPath` | Pfad des Hangfire-Dashboards | `/admin/jobs` |
| `Coworkee:Storage:Provider` | `FileSystem`, `S3` oder `AzureBlob` | `FileSystem` |
| `Coworkee:Storage:FileSystem:Root` | Ablageverzeichnis | `App_Data/blobs` (relativ zum Prozess!) |
| `Coworkee:Storage:S3:ServiceUrl` / `Region` / `Bucket` / `AccessKey` / `SecretKey` / `CreateBucket` | S3/MinIO | leer / `us-east-1` / `coworkee` / – / – / `true` |
| `Coworkee:Storage:AzureBlob:ConnectionString` / `Container` | Azure Blob | `UseDevelopmentStorage=true` / `coworkee` |
| `Coworkee:Search:Elasticsearch:Url` / `Username` / `Password` | Alternative zum Connection-String | – |
| `Coworkee:Settings:Defaults:<Name>` | Default für eine Admin-Einstellung, z. B. `Coworkee:Settings:Defaults:Mail.Smtp.Host` | – |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | aktiviert den OTLP-Export von Logs, Metriken, Traces | leer |

Die Processing-Werte für `Queues` und `WorkerCount` werden im Code nach den Umgebungsvariablen gesetzt und lassen sich nicht überschreiben. `Coworkee:Jobs:ConnectionStringName` wird von Sharemee immer auf `shareme` gesetzt.

**Sharemee**

| Key | Zweck | Default |
|---|---|---|
| `Shareme:Search:Alias` | Elasticsearch-Alias | `shareme-nodes` |
| `Shareme:Uploads:TempRoot` | Temp-Ordner für Uploads und Archive | System-Temp |
| `Shareme:Processing:TempRoot` | Arbeitsordner der Renditions (Unterordner `shareme-render`) | System-Temp |
| `Shareme:Archives:MaxUnpackedBytes` | maximale entpackte Größe beim Archiv-Entpacken | 20 GiB (max. 10 000 Einträge, Tiefe 32 fest) |
| `Shareme:MassEdit:MaxNodes` | Obergrenze für Massenbearbeitung | `1000` (1–10 000) |
| `Shareme:Shares:RequestsPerMinute` | Rate-Limit je öffentlichem Freigabe-Link | `600` |
| `Shareme:Drm:PurchaseEmails` | zusätzliche Empfänger der DRM-Kauf-Mail (`;` oder `,` getrennt) | leer |
| `Shareme:Ai:AllowPrivateNetworks` | KI-Extraktions-Provider in privaten Netzen erlauben | `false` |
| `Processing:Tools:<Tool>` | Pfad zu `FFmpeg`, `FFprobe`, `Ghostscript`, `LibreOffice`, `ExifTool`, `Blender` (ohne Präfix `Shareme:`) | Suche im `PATH` |

## 4. Ersteinrichtung

Bis zur Einrichtung beantwortet die API alle `/api`-Aufrufe außer `/api/v1/setup/*` und `/api/v1/system/*` mit `503` und `code: setup_required`.

1. Setup-Token beschaffen: entweder `Coworkee:SetupToken` an der API setzen (empfohlen, bei mehreren Instanzen Pflicht) oder im Log der API nach `System is not set up yet. Setup token: ...` suchen. Jeder Prozess erzeugt ohne Konfiguration sein eigenes Token; auch Auth und Processing loggen eines, gültig ist nur das des Prozesses, der den Request bekommt.
2. Im Browser `/setup` der Web-App öffnen (oder direkt die API):
   - `GET /api/v1/setup/status` – eingerichtet ja/nein
   - `GET /api/v1/setup/checks` – Vorabprüfungen (u. a. Datenbank)
   - `POST /api/v1/setup/complete` mit `setupToken`, `tenantName`, `adminEmail`, `adminPassword` (mind. 8 Zeichen), optional `adminFirstName`, `adminLastName`, `settings` (globale Einstellungen als Name/Wert).
3. Ergebnis: Default-Mandant, Rollen `Admin` und `User`, Admin-Benutzer (E-Mail bestätigt). Die Antwort enthält `tenantId` und `adminUserId`; die Mandanten-Id wird z. B. für den Importer gebraucht (`cw."Tenants"`).
4. Ein zweiter Aufruf liefert `409 setup.completed`, ein falsches Token `403 setup.token_invalid`.

## 5. Admin-Einstellungen

Verwaltung unter `/admin/settings` (Coworkee-Settings-Modul). Werte gelten global, je Mandant oder je Benutzer, je nach Definition. Typ `Secret` wird mit ASP.NET Data Protection verschlüsselt gespeichert. Defaults lassen sich per `Coworkee:Settings:Defaults:<Name>` setzen.

| Gruppe | Einstellung | Bereiche | Default |
|---|---|---|---|
| Mail | `Mail.Smtp.Host`, `Mail.Smtp.Port`, `Mail.Smtp.UseSsl`, `Mail.Smtp.UserName`, `Mail.Smtp.Password` (Secret), `Mail.From` | global, Mandant | –, `25`, `false`, –, –, `noreply@localhost` |
| Mail | `Mail.Whitelist` – leer = alle Empfänger erlaubt, sonst eine Adresse oder `@domain` pro Zeile | global, Mandant | leer |
| Ai | `Ai.Enabled`, `Ai.Model`, `Ai.ApiKey` (Secret, Anthropic), `Ai.MaxToolRounds` | **nur global** | `false`, `claude-opus-5-5`, –, `10` |
| Account | `Account.AllowRegistration` | global | `false` |
| Notifications | `Notifications.Digest` (Tages-Mail mit ungelesenen Benachrichtigungen) | global, Benutzer | `true` |
| Appearance | `Theme.Mode` (`system`, `light`, `dark`), `Theme.Id` | Benutzer | `system`, – |

Weitere Admin-Seiten: `/admin/users`, `/admin/groups`, `/admin/roles`, `/admin/audit`, `/admin/mail/templates`, `/admin/mail/log`, `/admin/themes` sowie Sharemee-spezifisch `/admin/node-types`, `/admin/metadata`, `/admin/renditions`, `/admin/restrictions`, `/admin/rules`, `/admin/drm`, `/admin/ai`, `/admin/ai-tool-calls`.

## 6. Hintergrundjobs

Hangfire mit Postgres-Storage (Schema `hangfire`).

| Queue | Bedient von | Inhalt |
|---|---|---|
| `default` | API | allgemeine Jobs, alle wiederkehrenden Jobs |
| `mail` | API | Mailversand (`SendMailJob`) |
| `search` | API | Indexierung, Reindex |
| `processing` | Processing-Host | Renditions, Zuschnitte, technische Metadaten |

Wiederkehrende Jobs (werden beim Start registriert, bei nicht erreichbarer DB alle 5 s erneut):

| Id | Zeitplan | Zweck |
|---|---|---|
| `shareme-rendition-sweep` | `*/10 * * * *` | verlorene Renditions nach 2 h neu einreihen, nach 1 Tag als fehlgeschlagen markieren, alte Arbeitsordner löschen |
| `shareme-storage-cleanup` | `17 * * * *` | Dateien gelöschter Assets und abgelaufener Uploads entfernen, abgelaufene Download-Pakete löschen, hängende Archiv-Extraktionen beenden |
| `coworkee-notification-digest` | `Coworkee:Notifications:DigestCron` | Tages-Digest |

Dashboard: `/admin/jobs` über die Web-App (BFF leitet weiter), Recht `Jobs.View`. Fehlgeschlagene Jobs werden nach `Coworkee:Jobs:Attempts` Versuchen dort als *Failed* angezeigt und können neu gestartet werden. Eine Übersicht der Sharemee-Läufe (Massenbearbeitung, Importe, KI, Regeln, Archive, Reindex) zeigt die Jobs-Seite der App.

## 7. Suche

- Schreibende Änderungen indexieren über die Queue `search`. Fehlt Elasticsearch, wiederholen sich die Jobs (3 Versuche) und landen dann als *Failed* im Dashboard.
- **Reindex** (je Mandant): Admin-Seite `/admin/node-types`, Schaltfläche *Rebuild index*, oder `POST /api/v1/search/reindex` (Recht `Nodes.Admin`); Verlauf `GET /api/v1/search/reindex`. Es läuft höchstens ein Reindex gleichzeitig (`409 search.reindex_running`). Änderungen an Restriktionen stoßen automatisch einen Reindex an.
- Der Index ist vollständig aus Postgres rekonstruierbar und muss nicht gesichert werden.

## 8. Dateien und Renditions

- Upload → Asset-Version im Blob-Storage → Rendition-Job auf `processing`. Fertige Renditions erreichen den Browser über SignalR/Redis.
- Werkzeuge (je über `Processing:Tools:<Tool>` oder `PATH` gefunden; fehlt eines, werden betroffene Formate nicht gerendert, ein falsch konfigurierter Pfad erzeugt eine Warnung im Log):

| Werkzeug | Verwendung | Kandidaten im `PATH` |
|---|---|---|
| Magick.NET (eingebettet) | Bilder (JPEG, PNG, GIF, WebP, TIFF, BMP, PSD, HEIC), Zuschnitte, Kontaktbögen (PDF) | – |
| FFmpeg | Video-Standbilder und abspielbare Kopien, Audio-Wellenform und MP3 | `ffmpeg` |
| FFprobe | technische Metadaten von Audio/Video, wenn ExifTool fehlt | `ffprobe` |
| Ghostscript (safe mode) | PDF, PostScript, EPS: erste Seite als PNG | `gs`, `gswin64c`, `gswin32c` |
| LibreOffice | Office- und Textdokumente → PDF → erste Seite | `soffice`, `libreoffice`, Windows-Standardpfad |
| ExifTool | technische Metadaten (Maße, Dauer, Codecs, Kameradaten) | `exiftool` |
| Blender | 3D-Modelle (headless); FBX wird bewusst nicht gerendert | `blender`, Windows-Standardpfad |

- Neu rendern: `POST /api/v1/assets/{id}/derivations/rerender` (Recht Update auf dem Knoten). Manuell hochgeladene Renditions bleiben erhalten.

## 9. KI und MCP

- **Assistent** (`/api/v1/ai/chat`, Chat-Seite in der App): nur wenn `Ai.Enabled = true`, `Ai.ApiKey` gesetzt und der Benutzer das Recht `Ai.Chat` hat. Alle Werkzeugaufrufe werden protokolliert (`/admin/ai-tool-calls`, Recht `Ai.Audit`).
- **MCP-Endpunkt** `/mcp` auf der API (Streamable HTTP, zustandslos). Authentifizierung mit einem Bearer-Token des Auth-Dienstes (Audience `shareme_api`). `tools/list` liefert nur Werkzeuge, wenn `Ai.Enabled` an ist, der Benutzer `Ai.Chat` hat und zusätzlich die Rechte des jeweiligen Werkzeugs. POST-Requests müssen `Content-Type: application/json` haben, ein `Origin`-Header muss zum Host passen, sonst `403`.
- **KI-Extraktion** (Tagging/Beschreibungen, `/admin/ai`): Provider `openai` (auch kompatible Server), `anthropic`, `azureVision`. Endpunkte müssen `https` sein, API-Keys werden mit Data Protection verschlüsselt. Verbindungen folgen keinen Redirects und gehen nur an öffentliche Adressen (SSRF-Schutz); interne Provider erfordern `Shareme:Ai:AllowPrivateNetworks=true`.

## 10. Backup und Restore

| Was | Sichern? | Hinweis |
|---|---|---|
| Postgres (`shareme`) | **ja** | enthält auch Data-Protection-Keys; ohne sie sind Secrets (SMTP-Passwort, API-Keys) und Download-Links nicht mehr lesbar |
| Blob-Storage | **ja** | FileSystem-Root, S3-Bucket bzw. Azure-Container |
| Elasticsearch | nein | per Reindex wiederherstellbar |
| Redis | nein | nur Backplane, kein Zustand |

Empfehlung: zuerst Postgres, danach Blobs sichern – Dateien, die neuer sind als der Datenbankstand, sind harmlos; fehlende Dateien zu vorhandenen Versionen nicht. Nach einem Restore für jeden Mandanten einen Reindex starten.

## 11. Updates und Migrationen

1. Neue Version bereitstellen, Dienste stoppen (oder rollierend, wenn die Migration kompatibel ist).
2. `Shareme.Migrations` laufen lassen (`Database.MigrateAsync()`); bei Exit-Code 1 steht `Database migration failed` im Log, nicht weitermachen.
3. Dienste starten. Hangfire legt sein Schema selbst an, wiederkehrende Jobs werden beim Start aktualisiert, OIDC-Clients aus `Coworkee:Auth:Clients` beim Start des Auth-Dienstes angelegt oder aktualisiert.
4. Nach Renderer-Updates bei Bedarf einzelne Assets neu rendern; nach Änderungen an der Suchabbildung einen Reindex starten.

Vorher immer ein Backup (Abschnitt 10).

## 12. Monitoring und Health

- Auth, API, Processing und Web haben `/health` (alle Checks; Auth/API/Processing prüfen zusätzlich die Datenbank, Check `database`) und `/alive` (nur Prozess, Tag `live`). Beide sind ohne Anmeldung erreichbar – nicht nach außen veröffentlichen.
- OpenTelemetry über `Shareme.ServiceDefaults`: Logs (mit Scopes), Metriken (ASP.NET Core, HttpClient, Runtime), Traces (ASP.NET Core ohne Health-Pfade, HttpClient). Export per OTLP, sobald `OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist (Aspire setzt ihn lokal fürs Dashboard).
- HTTP-Clients haben den Standard-Resilience-Handler (Retry, Circuit Breaker, Timeouts).
- Log-Level über die üblichen `Logging:LogLevel:*`-Keys.
- Fachliche Kontrolle: Hangfire-Dashboard (*Failed*), Mail-Log (`/admin/mail/log`), Reindex-Verlauf.

## 13. Sicherheit

- **Data Protection**: Keys liegen in `cw."DataProtectionKeys"`, Anwendungsname `Coworkee`, damit teilen alle Dienste die Keys. Die Keys sind **unverschlüsselt** gespeichert (**Offen**: `ProtectKeysWithCertificate`). Datenbank-Backups entsprechend schützen.
- **Secrets in Einstellungen** (`Secret`-Typ) und KI-Provider-Keys sind mit Data Protection verschlüsselt.
- **Auth-Server-Zertifikate**: außerhalb von Development startet der Auth-Server nur mit `Coworkee:Auth:SigningCertificate:Path`/`:Password` und `Coworkee:Auth:EncryptionCertificate:Path`/`:Password` (PKCS#12; Signatur-Zertifikat mit Key Usage DigitalSignature, Verschlüsselung mit KeyEncipherment), gleiche Dateien für alle Instanzen. `Coworkee:Auth:Issuer` setzt die öffentliche Adresse fest (hinter Proxy oder bei mehreren Instanzen). `Coworkee:Auth:DevelopmentCertificates=true` erlaubt die Entwicklungszertifikate für Tests und Demos; der AppHost setzt es nur im Development-Modus.
- **Content-Security-Policy fehlt** noch in Sharemee und Coworkee (**Offen**, siehe `docs/superpowers/deferred-minors.md`).
- Swagger UI (`/swagger`) und `/openapi/v1.json` sind in allen Umgebungen gemappt (**Offen**).
- BFF-Cookie: `HttpOnly`, `SameSite=Strict`, `Secure` wie der Request – TLS am Proxy erzwingen.
- MCP lehnt Formular-/`text/plain`-Posts und fremde Origins ab (kein CSRF über das Cookie).
- SSRF-Schutz für KI-Extraktion siehe Abschnitt 9.
- Setup-Token wird verglichen in konstanter Zeit; nach der Einrichtung ist der Setup-Endpunkt wirkungslos.
- Render-Tools laufen ohne Shell mit Zeitlimit; Blender läuft ohne Sandbox und ohne Speicherlimit (**Offen**), deshalb ist es nicht im Image.
- Mail-Whitelist in Test-/Staging-Umgebungen setzen, damit keine echten Empfänger angeschrieben werden.

## 14. Datenübernahme aus Sharedien

Werkzeug: `src/Shareme.Importer` (Assembly `shareme-import`). Konzept: `docs/superpowers/specs/2026-10-04-p9-migration-design.md`.

```bash
export SHAREME_IMPORT_SOURCE='<SQL-Server-Connection-String, nur lesend>'
export SHAREME_IMPORT_TARGET='<Sharemee-Postgres-Connection-String>'
dotnet run --project src/Shareme.Importer -- plan --source-tenant <id> --tenant <guid> --report import-report.json
dotnet run --project src/Shareme.Importer -- run  --source-tenant <id> --tenant <guid> --report import-report.json --max-problems 50
```

- Verbindungen nur über die Umgebungsvariablen, nie über die Kommandozeile.
- `plan` liest, mappt und prüft, schreibt nichts. `run` schreibt in **einer Transaktion**.
- `--source-tenant`: numerische Id des Sharedien-Mandanten. `--tenant`: GUID eines in Sharemee bereits angelegten Mandanten (z. B. `tenantId` aus der Ersteinrichtung), sonst Abbruch.
- Exit-Codes: `0` ok, `1` Fehler oder mehr Probleme als `--max-problems`, `2` ungültige Argumente oder fehlende Umgebungsvariablen. Der Bericht enthält Quell-Primärschlüssel und Problemcodes, keine Inhalte.
- **Nur Einfügen**: bis ein Upsert existiert, nur gegen ein frisches oder Wegwerf-Ziel laufen lassen. Ein zweiter Lauf schreibt nichts Neues; eine Quell-GUID, die schon in einem anderen Mandanten existiert, bricht ab.
- Übernommen werden derzeit: Benutzer (ohne Passwort – Anmeldung über *Passwort vergessen*), Profile, Gruppen, Rollen mit gemappten Rechten, Knotenrechte, Sprachen, Knoten-/Kantentypen, Metadaten-Katalog, Baum (Assets noch ohne Dateien), Kanten, Metadatenwerte. Noch nicht: Dateien, Workspaces/Tasks, DRM, Regeln, Kontextwerte.
- Danach für den Zielmandanten einen **Reindex** starten (Abschnitt 7).

## 15. Fehlerbehebung

| Symptom | Ursache / Abhilfe |
|---|---|
| API antwortet `503 setup_required` | Ersteinrichtung fehlt (Abschnitt 4). |
| `setup.token_invalid` | Token eines anderen Prozesses verwendet; Token aus dem API-Log nehmen oder `Coworkee:SetupToken` setzen. |
| Processing startet nicht: `needs the 'redis' connection string` | `ConnectionStrings:redis` setzen. |
| Renditions bleiben *pending* | Processing-Host läuft nicht, sieht einen anderen Blob-Speicher (FileSystem-Default `App_Data/blobs` ist relativ zum Prozess) oder das Werkzeug fehlt. Der Sweep reiht nach 2 h neu ein und markiert nach 1 Tag als fehlgeschlagen; danach neu rendern. |
| Vorschaubilder erscheinen erst nach Reload | Redis fehlt an der API oder am Processing-Host. |
| Rendition-Fehler bei sehr großen Bildern | ImageMagick-Limits (Abschnitt 3.1). |
| Renditions langsam / Rückstau in `processing` | Worker = Kerne / 2; mehr CPU oder weitere Processing-Instanzen. |
| Rückstau in `default`/`mail`/`search` | `Coworkee:Jobs:WorkerCount` an der API erhöhen (Default 5). |
| Suche findet neue Inhalte nicht, Index-Jobs *Failed* | Elasticsearch nicht erreichbar oder Index schreibgeschützt nach Überschreiten des Disk-Watermarks (flood stage). Platz schaffen, Schreibsperre im Cluster aufheben, Jobs neu starten oder Reindex. |
| Keine Mails | SMTP-Einstellungen, `Mail.Whitelist`, Mail-Log und `mail`-Queue prüfen. |
| Links in Mails relativ oder falsch | `Coworkee:Notifications:PublicAppUrl` bzw. `Coworkee:Account:PublicAuthUrl` setzen. |
| MCP listet keine Werkzeuge | `Ai.Enabled`, Recht `Ai.Chat` und Werkzeugrechte prüfen. |
| MCP antwortet `403` | `Content-Type` nicht JSON oder fremder `Origin`. |
| KI-Extraktion: `... is no public address` | Provider im privaten Netz; `Shareme:Ai:AllowPrivateNetworks=true`. |
| Upload bricht mit `413` ab | Body-Limit am Reverse-Proxy (≥ 101 MB). |
| Öffentlicher Freigabe-Link liefert `429` | `Shareme:Shares:RequestsPerMinute` erhöhen. |
| Nach Neustart des Auth-Containers ungültige Tokens | Entwicklungszertifikate erlaubt (`Coworkee:Auth:DevelopmentCertificates`) statt konfigurierter Zertifikate (Abschnitt Sicherheit). |
| Hangfire-Dashboard `401/403` | Anmeldung über die Web-App und Recht `Jobs.View`. |

## App-Konfiguration

- `src/Shareme.Contracts/Configuration/appconfig.json` ist der Abschnitt `Shareme` mit seinen Standardwerten; Nextended.CodeGen erzeugt daraus `SharemeConfiguration` (Server und Client teilen die Klasse). Neue Werte: in die JSON-Datei eintragen, die Klasse entsteht beim Build.
- Ebenen (unten nach oben): appconfig.json, appsettings, Umgebung (Aspire), in der App geänderte Werte (`cw.ConfigurationEntries`). API, Auth und Processing lesen die Tabelle beim Start und alle 15 Sekunden; der Dienst, der speichert, sofort.
- Bearbeiten: Admin › Configuration, nur aus der System-Organisation (Recht `Settings.Manage`). Werte, die beim Start gelesen werden (z. B. `Shares.RequestsPerMinute`), wirken erst nach einem Neustart.
- **Offen**: Geheimnisse liegen dort im Klartext (im Audit maskiert) – Schlüssel und Passwörter weiter über Umgebung/Key Vault setzen.
