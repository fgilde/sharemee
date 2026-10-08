# Sharemee SDKs

Both clients are built from `openapi.json`, the API description the Sharemee API serves at `/openapi/v1.json`.
The test `OpenApiTests` fails when the API changes and the file is out of date.

## Update after API changes

```bash
SHAREME_UPDATE_OPENAPI=1 dotnet test tests/Shareme.Api.Tests --filter "FullyQualifiedName~OpenApiTests"
cd sdk/typescript && npm install && npm run generate && npm run build
```

## .NET: `Sharemee.Client`

`src/Shareme.Client` – typed methods over the DTOs of `Sharemee.Contracts` (nodes, metadata, search, chunked uploads, download links, pages, news, people) plus `GetAsync`/`SendAsync` for any other endpoint. Errors raise `CoworkeeApiException` (package `Coworkee.Client`) with status, problem code and validation messages.

```csharp
var http = new HttpClient(new BearerTokenHandler(ct => tokens.GetAsync(ct))) { BaseAddress = new Uri("https://sharemee.example/") };
var client = new SharemeClient(http);
var folder = await client.CreateFolderAsync((await client.GetRootAsync()).Id, "Campaign 2026");
await using var file = File.OpenRead("poster.pdf");
var uploaded = await client.UploadAsync(file, "poster.pdf", folder.Id);
```

## TypeScript: `@sharemee/client`

`sdk/typescript` – types generated with `openapi-typescript`, requests through `openapi-fetch`; paths, parameters, bodies and responses are checked by the compiler.

```ts
import { createSharemeClient } from "@sharemee/client";

const api = createSharemeClient("https://sharemee.example", () => getToken());
const { data: root } = await api.GET("/api/v1/nodes/root");
const { data: children } = await api.GET("/api/v1/nodes/{id}/children", { params: { path: { id: root!.id } } });
```

Authentication: a bearer token of the Sharemee API (the same as for MCP clients), or the session cookie when the code runs inside the Sharemee web app.
