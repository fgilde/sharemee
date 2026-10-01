# Deferred minors

Review findings that were graded Minor and not fixed in their phase. Later plans pick them up; tick when done.

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
