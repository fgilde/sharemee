# Sharemee

Digital asset management, document management, knowledge base and intranet on one .NET 10 platform: versioned, audited, permissioned per item, realtime, with a REST API and an MCP endpoint.

> Early development. The foundation is in place; features land phase by phase.

**Website & docs:** https://fgilde.github.io/sharemee/

## Quick start

Requires the .NET 10 SDK, Docker and the Coworkee repository checked out next to this one as `../coworkee`.

```bash
pwsh ../coworkee/build/pack-local.ps1
dotnet run --project src/Shareme.AppHost
dotnet test --solution Shareme.slnx
```

Add `-p:CoworkeeReferenceMode=Project` to build against the Coworkee sources instead of the local packages.

Built by [gilde.org](https://gilde.org)
