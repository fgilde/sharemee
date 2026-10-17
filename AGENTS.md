# AGENTS.md

Rules for coding agents in Sharemee: digital asset management, documents, knowledge base and intranet on Coworkee (.NET 10, Aspire, Blazor WebAssembly, EF Core with PostgreSQL). Coworkee docs: https://fgilde.github.io/CoworkeeLib/

## Layout

| Path | What goes there |
|---|---|
| `src/Shareme.Contracts/<Area>` | DTOs, requests and permission names (`<Area>Permissions`, e.g. `NodePermissions`, `AssetPermissions`) |
| `src/Shareme.Graph` | the node tree every item lives in (folders, assets, pages, workspaces), edges, per-node rights (`NodeAccess`), restrictions |
| `src/Shareme.Assets`, `Metadata`, `Search`, `Knowledge`, `Workspaces`, `Drm`, `Automation`, `Processing` | feature modules: entities, model contributor, handlers, endpoints and the `ShareMe<Area>Module` that wires them, in a few files per module |
| `src/Shareme.Application` | requests that belong to no module |
| `src/Shareme.Infrastructure` | DbContext, migrations, module composition (`ShareMeDatabaseModule`), texts (`Localization/<culture>.json`) |
| `src/Shareme.Migrations` | applies migrations and the seed (`DemoSeed.cs`: roles, `DemoUsers.cs`: the users of `LOGIN.md`) |
| `src/Shareme.Web.Client` | Blazor pages (`Pages/`), components and API clients per area (`Graph/SharemeApi.cs`, ...), navigation (`Graph/SharemeNavigation.cs`) |
| `src/Shareme.Api`, `src/Shareme.Web`, `src/Shareme.Auth`, `src/Shareme.Processing.Host` | hosts: API, web app with backend for frontend, auth server, rendition worker; rarely touched |
| `src/Shareme.AppHost` | Aspire topology |
| `src/Shareme.Client`, `sdk/` | typed .NET client, OpenAPI snapshot (`sdk/openapi.json`) and generated TypeScript client (`sdk/typescript`) |
| `src/Shareme.Importer` | `shareme-import`: migration from the old Sharedien database |
| `tests/` | API tests against a real database (`Shareme.Api.Tests`), page tests (bUnit, `Shareme.Web.Client.Tests`), migration tests, app host tests, browser journey (`Shareme.E2E.Tests`, Playwright) |

## Required patterns

- Everything a user can see is a `Node` (or hangs off one) with `IMultiTenant`; rights are checked per node through `NodeAccess`. Requests on a node use `[RequiresPermission(NodePermissions.X, ResourceType = NodePermissions.ResourceType)]`, other requests `[RequiresPermission(<Area>Permissions.X)]`.
- Every use case is a request (`ICommand<Result<T>>` / `IQuery<Result<T>>`) with a handler (`IHandler<,>`) sent through `IDispatcher`. Handlers return `Result`/`Error` (`Error.Validation`, `Error.NotFound`, `Error.Forbidden`, `Error.Conflict`), never throw for expected failures, and never call `SaveChangesAsync`: the pipeline saves and commits.
- Permission names are constants in `Shareme.Contracts/<Area>`; define each in the module's `IPermissionDefinitionContributor` and give the demo roles in `DemoSeed.cs` what they need.
- Tables: `IModelContributor` with schema `sm`, maximum lengths and precisions; register it in the module.
- Realtime: `[Realtime(...)]` with `IHasRealtimeTopics`, so only viewers of the node (`GraphTopics`) are told about a change.
- Pages: code in a `.razor.cs` partial next to the `.razor` file, no `@code` blocks. Texts through `L["English text"]`, translations in `src/Shareme.Infrastructure/Localization/<culture>.json`.
- Show files with MudBlazor.Extensions' `MudExFileDisplay` (absolute URL); do not build own previews or viewers.

## Pitfalls

- A request without `[RequiresPermission]` (and without a check in its handler) is not checked: any signed-in user may send it, also through MCP and the assistant.
- A node permission without `ResourceType = NodePermissions.ResourceType` checks the role only, not the rights on that node.
- `ExecuteUpdate` and `ExecuteDelete` skip the change tracker: no audit entry, no realtime message, no history. Use them only for bulk work and publish yourself (`IRealtimePublisher`).
- The web host sends the library's Content-Security-Policy (`app.UseCoworkeeSecurityHeaders()`); `Shareme.E2E.Tests` fails on a policy violation. Extra sources go to `Coworkee:Bff:SecurityHeaders`, not into a hand-written header.

## Migrations

After every model change (entity, property, table mapping, new module, a Coworkee update that asks for one):

```bash
dotnet ef migrations add <Name> --project src/Shareme.Infrastructure --startup-project src/Shareme.Infrastructure
```

Never edit an applied migration or the model snapshot by hand. The migrations project applies them at the next start of the app host. `tests/Shareme.Migrations.Tests` fails while the model has changes without a migration.

## OpenAPI and SDKs

`sdk/openapi.json` is a snapshot of the API's OpenAPI document; `tests/Shareme.Api.Tests/OpenApiTests.cs` fails when the API no longer matches it. After changing endpoints or DTOs, refresh it and regenerate the TypeScript client in the same change (see `sdk/README.md`):

```bash
SHAREME_UPDATE_OPENAPI=1 dotnet test --project tests/Shareme.Api.Tests --filter-method "*OpenApi*"
cd sdk/typescript && npm run generate && npm run build
```

## Tests

```bash
dotnet test --solution Shareme.slnx                     # all; needs Docker for the database
dotnet test --project tests/Shareme.Api.Tests           # one project
```

API tests use `ApiFixture` (a real database per run, `ResetAllAsync()` between tests, `As(userId, tenantId)` to act as a user). Prove permission checks (403 without the permission), node rights and tenant separation for new features. Run focused projects; the app host tests start the whole topology one test at a time (collection `AppHost`), keep it that way.

## Running and Coworkee

- `dotnet run --project src/Shareme.AppHost`; sign in with the users of `LOGIN.md`. The Coworkee packages come from `../coworkee/artifacts/nuget` (`pwsh ../coworkee/build/pack-local.ps1`).
- `coworkee update [--dry-run]` lists newer Coworkee packages with the upgrade rules and manual steps; the changelog is on the Coworkee docs site.
- The running app is an MCP server under `/mcp`, authenticated with a personal API key.
