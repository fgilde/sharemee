# Deferred minors

Review findings that were graded Minor and not fixed in their phase. Later plans pick them up; tick when done.

## From P3d (packages, archives)

- [ ] ImageMagick has no restrictive policy (no '@file' reads, no URL coders): MagickNET.Initialize with a custom policy crashed the test host; user text going into Magick is escaped (MagickText.Literal) instead.
- [ ] Zip download keeps AllowSynchronousIO for the small headers the writer emits on a non-seekable body; a header-buffering stream would remove it.
- [ ] Entries over 4 GB in streamed zips are not tested with Explorer, macOS Archive Utility and 7-Zip.
- [ ] The extraction reads the whole central directory before the entry count check (bounded by the archive size); an EOCD pre-check would stop huge directories earlier.
- [ ] Unpacking again after a partial failure duplicates the files already imported (folders are reused); no duplicate detection or Duplicate status for extracted files.
- [ ] Package links are bearer links for an hour and are not revoked when rights change or assets are deleted.
- [ ] A blob that breaks while being copied into a zip ends the response with an incomplete zip.

## From P3c (crop, variants)

- [ ] A failed variant keeps no error text (only status failed); store it where the asset page can show it.
- [ ] The crop of a rendition is not in DerivationDto and cannot be removed (cropping the full area resets it); the editor cannot show the current crop.
- [ ] While a cropped rendition renders the old one is not served; after a failed render its old file stays on the failed row until the next rerender.
- [ ] Assets with their own permissions get no variant; copying the source's grants onto the variant would allow it.
- [ ] MudExImageViewer sends the selected area as PNG bytes to .NET on every selection although the editor only needs the rectangle.

## From P3b (derivation types, watermarks)

- [ ] Renditions are not claimed per row: a render request while a job runs is refused (409) and metadata triggered re-renders wait for the sweep; a Pending to Processing claim would allow both at once.
- [ ] Deleting a rendition type keeps its renditions (served by key; a new type with the same key adopts them); changing size, format or watermark of a type does not re-render existing ones.
- [ ] Two concurrent creates of the same type key end in a 500 from the unique index instead of 409.
- [ ] Asset tab shows the upload button to users without the update right (API refuses); renditions of deleted types are not listed.
- [ ] No confirmation when deleting rendition types or watermarks in the admin page.
- [ ] Opening /view of a PDF directly in Chrome shows it blocked (CSP sandbox disables the built-in viewer); the app renders PDFs with pdf.js through MudExFileDisplay, so only direct links are affected.
- [ ] Hand made renditions carry no pixel size (the API host has no image library).

## From P3a (renderers)

- [ ] Technical metadata of every version is sent with `GetAsset`; send the current version's only. The per-render update of `AssetVersion` may write the whole dictionary into the audit trail.
- [ ] Range requests on media need seekable storage streams (S3/Azure providers return non-seekable ones, so `<video>` cannot seek there).
- [ ] The `{}` default of `TechnicalMetadata` is hand edited in the migration; the model snapshot has no default.
- [ ] The audio waveform is not shown on the detail page next to the player.
- [ ] No CI step builds the processing image (package typos surface at deploy time); no memory cap per tool beyond the container limit.
- [ ] No failure path test (truncated video ⇒ `failed` with a message).
- [ ] P3b: move planning (`DerivationKinds.For`) into processing once derivation types are a catalog; keep the intermediate still (frame, page, office PDF) so on-demand types, crops and watermarks do not rerun the heavy tool; decide whether on-demand and uploaded derivations survive a rerender; map derivation formats to extensions instead of the three case switch.
- [ ] Technical metadata is not mirrored into `sys.*` catalog properties yet (search facets on duration or dimensions need it).

## From P2 (search)

- [ ] Bulks use `refresh=wait_for` everywhere, also during tenant rebuilds; batch by bytes rather than count once documents carry content (P3).
- [ ] `EnsureAsync` on an existing alias cannot change analyzers or settings; a cluster wide rebuild with alias swap (`CreateIndexAsync`/`SwapAsync`) needs a platform level trigger.
- [ ] RichText HTML goes into `fulltext` untagged; booleans and dates add noise to it.
- [ ] Date definitions used as facets give per-value buckets (use a date histogram or disallow).
- [ ] The metadata catalog is loaded several times per search and per suggest keystroke (cache per tenant).
- [ ] Inherited metadata (`InheritToChildren`) is not indexed on descendants; decide before P3.
- [ ] No highlights; `names.{lang}` not indexed; definition changes need the manual rebuild (spec says automatic).
- [ ] A grant on another tenant's node id removes that node's document (grant handler does not validate the resource).
- [ ] Changing a definition's `ListMode` moves its field prefix; old documents keep the old field until a rebuild.
- [ ] Saved searches are shared with everyone or no one (no per user or group sharing yet); applying one replays filters as equality and ignores its folder.
- [ ] Suggestion list has no arrow-key navigation; `Total` can exceed the shown hits after the read-time access check.
- [ ] Overriding `Coworkee:Jobs:Queues` for the api must keep `search`.

## From P1d (processing, asset UI)

- [ ] No ImageMagick security policy; coders are pinned from the checked MIME type, but a policy disabling MSVG/MVG/PS/PDF/URL/TEXT coders is defence in depth.
- [ ] `[Realtime]` on `Derivation` adds type/entity publishes nobody needs (the `children:{assetId}` extra topic is what the UI uses).
- [ ] Shared blob storage between api and processing host is assumed, never probed at startup (FileSystem root defaults per process).
- [ ] Uploads cannot be cancelled or removed from the queue; the queue list is app wide while the panel text speaks of this folder.
- [ ] AssetDetail reloads several times per upload (derivation created/updated events); debounce.
- [ ] Admin pages (metadata, node types) are only `[Authorize]`; non-admins see raw error codes.
- [ ] The processing module (Magick.NET) is loaded by every host through `ShareMeDatabaseModule`; only the processing host and the test fixture need it.
- [ ] Renditions of superseded versions are rendered and stored but not exposed; share links cannot serve renditions.

## From P1c (storage, assets)

- [ ] Upload assembly goes through a temp file (`Shareme:Uploads:TempRoot`), so it needs free disk of the file size; stream into S3 multipart / Azure blocks while hashing instead.
- [ ] Download link tokens carry only the blob key (resolver looks up the version across tenants); fixed 15 minute lifetime, no revocation; P1d derivations need their own lookup.
- [ ] FileSystem provider leaves `*.tmp` files after a crash during a write (sweep in the cleanup job).
- [ ] Two parallel PUTs of the same chunk index can hit the `UploadChunks` primary key (500); treat the unique violation as success.
- [ ] `AssetVersionCreated` is raised for duplicates too; P1d derivations should key on the SHA-256 to skip identical content.
- [ ] Spec puts processing status and file type id on `AssetVersion`; P1d adds the derivation table and must include derivation keys in the purge listener and the link resolver.
- [ ] The current version has no metadata snapshot; "metadata as of version N" must fall back to the live asset for the current one.

## From P1b (metadata)

- [ ] Deleting and recreating a definition with the same key revives stored values under the new type (strip `Metadata - 'key'` on delete).
- [ ] Deleting a language or context leaves values that the editor never shows and the next save drops silently.
- [ ] Check-then-insert on group, definition and fallback language/context keys gives 500 on concurrent creation (map unique violations to 409).
- [ ] Set `NodeTypeKey` and definition `NodeTypeKeys` are not validated against existing node types.
- [ ] Inherited values expose `InheritedFrom` ids of ancestors above the caller's access.
- [ ] The reference check is an existence oracle for node ids; dangling references after purge stay.
- [ ] Read-only equality compares serialized JSON (`12` vs `12.0` gives a false "cannot be edited").
- [ ] Min/Max are ignored for date, boolean and reference types; RichText is stored unsanitized (sanitize before rendering it as HTML).
- [ ] Integer editor truncates decimals and can overflow; only one custom value can be entered next to list values.
- [ ] The editor refetches the catalog on every node switch (cache it, invalidate on realtime).
- [ ] Admin deletes have no confirmation; non-managers see raw error codes; no Sets UI; only folder/asset types are offered.
- [ ] The `{}` default in the Metadata migration is hand edited; the model snapshot has no default.
- [ ] `DataLanguage` and `UserContext` are not audited entities.

## From P1a (graph)

- [ ] `GET /nodes/root` creates the root lazily without a permission and two first calls race on `IX_Nodes_Root` (create in setup or catch the unique violation).
- [ ] Children search does not escape `%`/`_`; name sort has no tiebreaker for paging.
- [ ] `CreateNodeType` does not validate `Name`; `Names` dictionaries are unbounded; subscribing to a missing id by a global reader hits the FK (500).
- [ ] One resource check per interrupted child in `GetChildren`; `GetGrantedResourcesAsync` loads grants per role; resource checks are uncached; entry points build `IN` over all grants.
- [ ] Delete still loads the subtree into memory and emits realtime and audit per descendant.
- [ ] Node resource permission panel sits behind the global `Identity.ResourcePermissions.Manage`; `Nodes.ManagePermissions` does not imply it.
- [ ] Interrupting inheritance with only an inherited `ManagePermissions` locks the user out of the node.
- [ ] Restore checks no right on the destination; delete has no confirmation dialog.
- [ ] DTOs from create carry `CreatedAt` = default (stamped after the handler).
- [ ] Undirected `duplicate` edges can be stored in both directions.
- [ ] Purge bypasses realtime and audit.
- [ ] Concurrent create under a folder that is being moved can persist the old path prefix.

## From P0.2b (auth, BFF, admin UI)

- [ ] `prompt=login` loops on `/connect/authorize` (exclude `prompt`/`max_age` when rebuilding the challenge query).
- [ ] Inactive user with live IdP cookie gets a raw 500 from the BFF callback (`OnRemoteFailure` redirect).
- [ ] id_token claims not gated by granted scopes; refresh ignores narrowed `scope`.
- [ ] `role` claim lacks group-derived roles (union or drop roles from `BffUserDto`).
- [ ] Password change does not invalidate refresh tokens (security stamp claim, check at refresh). Do with the password-reset work.
- [ ] TokenRefresher signs out on transient IdP errors; null refresh token path leaves stale cookie; concurrent refresh.
- [ ] Forwarder passes the session cookie and browser `Authorization` header to the API (remove `Cookie`, always set/clear `Authorization`).
- [ ] Client seeding failure only logged as warning (rethrow or LogError).
- [ ] Auth host maps Identity admin/setup API and logs its own setup token (skip Identity web endpoints on auth hosts, or share the token).
- [ ] Auth host dead ends: `/connect/endsession` without client and login without ReturnUrl redirect to `/` (404).
- [ ] Blazor: catch `HttpRequestException`, redirect to login on 401 and when unauthenticated, reset `PermissionStore`.
- [ ] Groups page edit controls not behind `Groups.Manage`; pickers capped at 200.
- [ ] WASM payload about 10 MB br; MudBlazor.Extensions pulls SQLite, ImageSharp, SharpCompress and more while no MudEx component is used yet (fix upstream or reference only when needed; consider InvariantGlobalization).
- [ ] Issuer not pinned (`SetIssuer` from options for publish mode). With production certificates.
- [ ] Cookie hardening: `SecurePolicy.Always`, consider `SameSite=Lax`.
- [ ] `UseAntiforgery` before authentication in Shareme.Web (remove or move).

## From P0.3a (settings, jobs, mail)

- [ ] Settings page Organisation tab throws for users without tenant claim (catch in `LoadAsync` or hide tab).
- [ ] Audit masks every setting value, not only secrets (mask only Secret definitions).
- [ ] `Jobs.View` allows dashboard mutations (`IsReadOnlyFunc = _ => true` or `Jobs.Manage`).
- [ ] Hangfire global filter mutation not idempotent across hosts in one process (move Remove + UseFilter into the lambda); third retry delay unused; docs say 3 retries vs 3 attempts.
- [ ] `Mail.Whitelist` tenant-overridable (make Global only); maybe also `Mail.Smtp.*` Global only for multi tenant.
- [ ] `SmtpSettings` record prints the password in `ToString`.
- [ ] Opportunistic STARTTLS with credentials (require StartTls when a user name is set, or a TLS mode setting).
- [ ] `OutgoingMails` keeps HTML bodies with live reset links forever (null body after send or retention job).
- [ ] Saved template override only syntax checked; runtime errors break that tenant's mails (render with sample model on save; no test send for Layout).
- [ ] Culture case handling mixed (normalize to lower case, validate in get/preview).
- [ ] `ToScript` has no recursion depth guard (EF entities as model would overflow the stack).
- [ ] UI cannot clear a stored secret (Clear button sending null).
- [ ] Hangfire schema created by the API process instead of migrations (DDL rights).
- [ ] Data protection keys stored unencrypted next to ciphertexts (`ProtectKeysWithCertificate` in production).
- [ ] Mail stuck in `Sending` after a crash between SMTP and status write (reconciler).

## From P0.3b (audit, versions, themes)

- [ ] Concurrent edits of versioned entities end in 500 (mark `Revision` as concurrency token for `IVersioned`).
- [ ] Restore with incompatible payload types throws 500 (catch, return conflict).
- [ ] `VersionHistory` reloads on every parent render; timeline not refreshed after restore.
- [ ] Theme name uniqueness: trim before check, check on update; returned DTO revision is stale.
- [ ] Validator accepts `hsl()`/`transparent` which `MudColor` cannot parse (tighten or parse with MudColor on the server).
- [ ] Dark mode: toggle not persisted, mode read once, system observation overrides explicit choice; fire-and-forget reload in `ThemeDetail.Dispose`.
- [ ] Snapshot payload ignores the `[NotAudited]` property attribute (share one helper with the audit trail).
- [ ] Audit records `Revision: 0` on create; `Restored` entry has no correlation id; no-op restore still audited.
- [ ] CustomCss allows `url(` (external loads, attribute selector exfiltration within the tenant).
- [ ] Mail template editor has no version history; reset deletes the override and orphans its history.
- [ ] Auth host login page does not use the tenant theme yet.
- [ ] Typography is stored but not applied and wiped by the editor.
- [ ] Missing index `(TenantId, OccurredAt)` on audit entries.
- [ ] Audit filter needs CLR type names (`ThemeDefinition`); unknown theme id renders an empty page; duplicate names between global and tenant themes.
- [ ] Snapshot and audit growth without retention (logo and CSS per save).

## From P0.3c (realtime, notifications)

- [ ] `/hubs` negotiate outside the X-CSRF rule; no Origin check on WebSocket upgrades (SameSite=Strict is the only guard).
- [ ] NotificationBell: list not refreshed on push or after mark read; read-all floods N events and N count requests (debounce).
- [ ] NotificationBell: mark read calls unguarded against ApiException; link check admits `//host`.
- [ ] Realtime handler exceptions are silently dropped (log them).
- [ ] Entities with null TenantId (global roles, global themes) publish only to the actor's tenant.
- [ ] Redis channel prefix fixed to `coworkee` (derive from application name or configuration).
- [ ] Two sequential sends per change in the request path (dedupe `type:` per save, send in parallel).
- [ ] `EntityTopicAuthorizer` scans the model on every subscribe (cache the name map).
- [ ] Soft deleted realtime entities report `Updated` instead of `Deleted`.
- [ ] `INotifier` only stages entities; document or add a saving overload for jobs.
- [ ] E2E uses long polling only; WebSocket path through YARP untested.
- [ ] `ChangedProperties` exposes names of sensitive properties (`PasswordHash`).
- [ ] Deactivated users keep receiving events until their token expires (at most the access token lifetime).
- [ ] Spec says realtime goes through the outbox; implementation is best effort after commit (update the spec before `job:` progress or other loss intolerant consumers).

## From P0.3d (account flows)

- [ ] Register reveals existing addresses (show the success text and mail the owner instead).
- [ ] Turning 2FA off needs no password or code and sends no notification.
- [ ] Security stamp changes on the 2FA page without `RefreshSignInAsync` sign the user out of the auth server within 30 minutes.
- [ ] ConfirmEmail answers "confirmed" for already confirmed users without validating the token.
- [ ] Reset and confirmation tokens live 24 hours (shorten `DataProtectionTokenProviderOptions.TokenLifespan`).
- [ ] No rate limit on ForgotPassword and Register (both send mail to third parties).
- [ ] ForgotPassword timing differs for known and unknown addresses.
- [ ] Anonymous setup checks disclose the SMTP host and exception messages and open outbound connections.
- [ ] Effective permissions hard cast `IPermissionChecker` to `PermissionChecker`.
- [ ] `PublicAuthUrl` not validated on start.
- [ ] Admins get no notification about pending registrations; no approval mail on activation.

## From P0.2a and P0.1

See the review notes in the P0.2a and P0.1 ledgers: tenant claim handling, 201 on create, unique NormalizedEmail, tenant delete cascade, Redis backed HybridCache, setup token logging, setup rate limit, authorization fail-open without attributes, tenant isolation tests, JwtBearer authority fail-fast, system role update 404 vs 409, duplicate email leak across tenants, Permissions.Manage admin equivalence, Swagger auth in production, outbox cleanup and dead letters.
