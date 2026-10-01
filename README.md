# Shareme

Asset Management, Dokumentenmanagement, Knowledge Base und Intranet auf Basis von Coworkee.

## Voraussetzungen
- .NET SDK 10.0.300, Docker
- Coworkee-Repo daneben: `../coworkee`

## Starten
1. `pwsh ../coworkee/build/pack-local.ps1` (Coworkee-Pakete in den lokalen Feed)
2. `dotnet run --project src/Shareme.AppHost` → Aspire-Dashboard

Entwicklung gegen Coworkee-Quellen statt Pakete: `-p:CoworkeeReferenceMode=Project` an `dotnet build/run/test` anhängen.

## Tests
`dotnet test --solution Shareme.slnx` (Docker erforderlich)

## Dokumentation
- Architektur: `docs/superpowers/specs/`
- Pläne: `docs/superpowers/plans/`
- Sharedien-Analyse: `docs/analysis/`
