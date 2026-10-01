# Deferred minors

Review findings that were graded Minor and not fixed in their phase. Later plans pick them up; tick when done.

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

## From P0.2a and P0.1

See the review notes in the P0.2a and P0.1 ledgers: tenant claim handling, 201 on create, unique NormalizedEmail, tenant delete cascade, Redis backed HybridCache, setup token logging, setup rate limit, authorization fail-open without attributes, tenant isolation tests, JwtBearer authority fail-fast, system role update 404 vs 409, duplicate email leak across tenants, Permissions.Manage admin equivalence, Swagger auth in production, outbox cleanup and dead letters.
