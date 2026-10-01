# P0.1 Gerüst & Kern – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lauffähiges Gerüst aus Coworkee-Kernpaketen (Module, Dispatcher, EF-Basis mit Audit/SoftDelete/Tenant/Concurrency/Outbox, ASP.NET-Integration, Testing, Aspire) und einer Shareme-Aspire-App (Postgres, Redis, Mailpit, Migrations, Api), die über den lokalen NuGet-Feed gebaut wird.

**Architecture:** Zwei Repos nebeneinander: `C:\dev\privat\github\coworkee` (Libs, gepackt nach `artifacts/nuget`) und `C:\dev\privat\github\shareme` (App, referenziert Coworkee per Paket oder – Schalter – per Projekt). Use-Cases laufen über einen eigenen Dispatcher mit geordneter Middleware-Pipeline; EF-Interceptors erledigen Querschnitt beim Speichern.

**Tech Stack:** .NET SDK 10.0.300, EF Core 10.0.12 + Npgsql 10.0.3, FluentValidation 12.1.1, Aspire 13.6.0, xUnit v3 4.0.1, Shouldly 4.3.0, NSubstitute 6.2.0, Testcontainers 4.15.0, Respawn 7.0.0, NetArchTest.eNhancedEdition 1.4.5.

**Spec:** `docs/superpowers/specs/2026-10-01-p0-foundation-design.md` (Abschnitte 1, 2, 5) und Master-Spec `docs/superpowers/specs/2026-09-29-shareme-coworkee-architecture-design.md`.

## Global Constraints

- TargetFramework `net10.0`, `Nullable` enable, `ImplicitUsings` enable, `LangVersion` latest, `TreatWarningsAsErrors` true in allen `src`-Projekten.
- Central Package Management in beiden Repos (`Directory.Packages.props`), keine `Version`-Attribute in `PackageReference`.
- Kein MediatR, kein AutoMapper, keine weitere Bibliothek außer den im Tech Stack und in Task-Dateien genannten.
- Code ohne Kommentare, außer `ponytail:`-Markierungen für bewusste Vereinfachungen. Keine XML-Doc-Kommentare.
- IDs sind `Guid` v7 (`Guid.CreateVersion7()`).
- Coworkee-Tabellen liegen im Postgres-Schema `cw`.
- Paket-Metadaten: Authors `fgilde`, Lizenz `MIT`, Icon `assets/icon.png`, VersionPrefix `0.1.0`.
- Git-Commits ohne Co-Authored-By- oder "Generated with"-Zeilen.
- Docker muss laufen (Testcontainers, Aspire).

## Review Focus

1. Neue mandantenfähige Entität ohne Tenant (User ohne Tenant-Claim) → `SaveChanges` wirft `InvalidOperationException`, speichert nie `Guid.Empty`. Test in Task 6.
2. Abfrage durch User ohne Tenant oder mit fremdem Tenant → sieht keine fremden Zeilen (strikter Filter, kein „null = alles“). Test in Task 6.
3. Outbox-Nachricht mit unbekanntem Typ oder fehlerhaftem Handler → wird als Fehlversuch markiert, blockiert den Rest des Batches nicht. Test in Task 8.
4. Validierungsfehler bei Request ohne `Result`-Rückgabe → HTTP 400 ProblemDetails statt 500. Test in Task 9.
5. Gleichzeitige Änderung derselben Entität → HTTP 409 ProblemDetails statt 500. Tests in Task 6 (Exception-Übersetzung) und Task 9 (HTTP-Mapping).

---

## Dateistruktur

```
coworkee/
  global.json, Directory.Build.props, Directory.Packages.props, Version.props, nuget.config, .editorconfig, .gitignore, Coworkee.slnx, README.md
  assets/icon.png, assets/icon.svg
  build/pack-local.ps1
  src/Directory.Build.props
  src/Coworkee.Core/            Results/Error.cs, Results/Result.cs, Results/ResultFactory.cs, ConcurrencyConflictException.cs,
                                Modularity/CoworkeeModule.cs, Modularity/DependsOnAttribute.cs, Modularity/ModuleLoader.cs,
                                Modularity/ModuleServiceCollectionExtensions.cs, Security/ICurrentUser.cs, Security/AnonymousCurrentUser.cs
  src/Coworkee.Domain/          Entity.cs, AggregateRoot.cs, IDomainEvent.cs, Markers.cs, Attributes.cs
  src/Coworkee.Application/     Messaging/Requests.cs, Messaging/IRequestMiddleware.cs, Messaging/MiddlewareOrder.cs, Messaging/Dispatcher.cs,
                                Messaging/IDomainEventHandler.cs, Messaging/MessagingServiceCollectionExtensions.cs,
                                Messaging/LoggingMiddleware.cs, Messaging/ValidationMiddleware.cs, Messaging/UnitOfWorkMiddleware.cs,
                                IUnitOfWork.cs, CoworkeeApplicationModule.cs
  src/Coworkee.Infrastructure/  Persistence/CoworkeeDbContext.cs, Persistence/IModelContributor.cs, Persistence/CoworkeeModelContributor.cs,
                                Persistence/DbContextServiceCollectionExtensions.cs,
                                Persistence/Interceptors/{SoftDelete,AuditableProperties,MultiTenant,AuditTrail,Outbox}Interceptor.cs,
                                Auditing/AuditEntry.cs, Outbox/OutboxMessage.cs, Outbox/OutboxProcessor.cs, Outbox/OutboxBackgroundService.cs,
                                Outbox/DomainEventDispatch.cs, Outbox/OutboxServiceCollectionExtensions.cs
  src/Coworkee.AspNetCore/      IWebModule.cs, CoworkeeWebApplicationExtensions.cs, Security/HttpCurrentUser.cs, Http/ResultHttpExtensions.cs,
                                Http/CoworkeeExceptionHandler.cs
  src/Coworkee.Testing/         PostgresFixture.cs, TestCurrentUser.cs, PostgresWebApplicationFactory.cs
  src/Coworkee.Aspire/          CoworkeeInfrastructureExtensions.cs
  tests/Directory.Build.props
  tests/Coworkee.Core.Tests, Coworkee.Domain.Tests, Coworkee.Application.Tests, Coworkee.Infrastructure.Tests,
        Coworkee.AspNetCore.Tests, Coworkee.Aspire.Tests, Coworkee.ArchitectureTests

shareme/
  global.json, Directory.Build.props, Directory.Packages.props, Directory.Build.targets, nuget.config, .editorconfig, Shareme.slnx, README.md
  src/Shareme.ServiceDefaults/Extensions.cs
  src/Shareme.AppHost/AppHost.cs
  src/Shareme.Application/ShareMeApplicationModule.cs, System/GetSystemInfo.cs
  src/Shareme.Infrastructure/ShareMeDbContext.cs, ShareMeInfrastructureModule.cs, ShareMeDbContextDesignFactory.cs, Migrations/*
  src/Shareme.Migrations/Program.cs, MigrationWorker.cs
  src/Shareme.Api/Program.cs, ShareMeApiModule.cs
  tests/Shareme.Api.Tests/ApiTests.cs, ApiFixture.cs
  tests/Shareme.AppHost.Tests/AppHostTests.cs
```

---

### Task 1: Coworkee-Repo und Build-Infrastruktur

**Files:**
- Create: `coworkee/global.json`, `coworkee/Directory.Build.props`, `coworkee/src/Directory.Build.props`, `coworkee/tests/Directory.Build.props`, `coworkee/Directory.Packages.props`, `coworkee/Version.props`, `coworkee/nuget.config`, `coworkee/.editorconfig`, `coworkee/.gitignore`, `coworkee/assets/icon.svg`, `coworkee/assets/icon.png`, `coworkee/Coworkee.slnx`
- Create: `coworkee/src/Coworkee.Core/Coworkee.Core.csproj`, `coworkee/tests/Coworkee.Core.Tests/Coworkee.Core.Tests.csproj`, `coworkee/tests/Coworkee.Core.Tests/SmokeTests.cs`

**Interfaces:**
- Consumes: nichts
- Produces: Repo-Layout; jedes neue Projekt unter `src/` oder `tests/` erbt Einstellungen automatisch.

- [ ] **Step 1: Repo anlegen**

```bash
mkdir -p /c/dev/privat/github/coworkee/{assets,build,src,tests}
cd /c/dev/privat/github/coworkee && git init -q
cp /c/dev/privat/github/shareme/docs/assets/coworkee-icon.svg assets/icon.svg
npx -y @resvg/resvg-js-cli --fit-width 256 assets/icon.svg assets/icon.png
cp /c/dev/privat/github/shareme/.gitignore .gitignore
```

- [ ] **Step 2: Build-Dateien schreiben**

`global.json`:
```json
{ "sdk": { "version": "10.0.300", "rollForward": "latestFeature" } }
```

`Version.props`:
```xml
<Project>
  <PropertyGroup>
    <VersionPrefix>0.1.0</VersionPrefix>
  </PropertyGroup>
</Project>
```

`Directory.Build.props`:
```xml
<Project>
  <Import Project="$(MSBuildThisFileDirectory)Version.props" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

`src/Directory.Build.props`:
```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <IsPackable>true</IsPackable>
    <Authors>fgilde</Authors>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageIcon>icon.png</PackageIcon>
    <PackageProjectUrl>https://github.com/fgilde/coworkee</PackageProjectUrl>
    <RepositoryUrl>https://github.com/fgilde/coworkee</RepositoryUrl>
    <Description>Coworkee application framework – $(MSBuildProjectName)</Description>
  </PropertyGroup>
  <ItemGroup>
    <None Include="$(MSBuildThisFileDirectory)../assets/icon.png" Pack="true" PackagePath="/" Visible="false" />
    <InternalsVisibleTo Include="$(MSBuildProjectName).Tests" />
  </ItemGroup>
</Project>
```

`tests/Directory.Build.props`:
```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NSubstitute" />
    <Using Include="Xunit" />
    <Using Include="Shouldly" />
  </ItemGroup>
</Project>
```

`Directory.Packages.props`:
```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
    <PackageVersion Include="FluentValidation" Version="12.1.1" />
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageVersion Include="Npgsql" Version="10.0.3" />
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.12" />
    <PackageVersion Include="Swashbuckle.AspNetCore.SwaggerUI" Version="10.2.3" />
    <PackageVersion Include="Microsoft.AspNetCore.TestHost" Version="10.0.12" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Aspire.Hosting" Version="13.6.0" />
    <PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="13.6.0" />
    <PackageVersion Include="Aspire.Hosting.Redis" Version="13.6.0" />
    <PackageVersion Include="CommunityToolkit.Aspire.Hosting.MailPit" Version="13.6.0" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.15.0" />
    <PackageVersion Include="Respawn" Version="7.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageVersion Include="xunit.v3" Version="4.0.1" />
    <PackageVersion Include="xunit.v3.extensibility.core" Version="4.0.1" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="4.0.0" />
    <PackageVersion Include="Shouldly" Version="4.3.0" />
    <PackageVersion Include="NSubstitute" Version="6.2.0" />
    <PackageVersion Include="NetArchTest.eNhancedEdition" Version="1.4.5" />
  </ItemGroup>
</Project>
```

`nuget.config`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

`.editorconfig`:
```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = crlf
insert_final_newline = true
charset = utf-8

[*.{json,xml,csproj,props,targets,slnx,yml}]
indent_size = 2

[*.cs]
csharp_style_namespace_declarations = file_scoped:warning
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion
csharp_style_expression_bodied_methods = when_on_single_line:suggestion
csharp_style_prefer_primary_constructors = true:suggestion
dotnet_sort_system_directives_first = true
dotnet_style_qualification_for_field = false:suggestion
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_prefix
dotnet_naming_rule.private_fields_underscore.severity = suggestion
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_prefix.required_prefix = _
dotnet_naming_style.underscore_prefix.capitalization = camel_case
```

Append to `.gitignore`:
```
artifacts/
```

- [ ] **Step 3: Core-Projekt, Testprojekt, Solution**

`src/Coworkee.Core/Coworkee.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
  </ItemGroup>
</Project>
```

`tests/Coworkee.Core.Tests/Coworkee.Core.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.Core\Coworkee.Core.csproj" />
  </ItemGroup>
</Project>
```

`tests/Coworkee.Core.Tests/SmokeTests.cs`:
```csharp
namespace Coworkee.Core.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Build_pipeline_runs_tests() => true.ShouldBeTrue();
}
```

```bash
cd /c/dev/privat/github/coworkee
dotnet new sln -n Coworkee --format slnx
dotnet sln Coworkee.slnx add src/Coworkee.Core/Coworkee.Core.csproj tests/Coworkee.Core.Tests/Coworkee.Core.Tests.csproj
```

- [ ] **Step 4: Bauen und testen**

Run: `cd /c/dev/privat/github/coworkee && dotnet test Coworkee.slnx`
Expected: Build ohne Warnungen, `1 passed`.

- [ ] **Step 5: Commit**

```bash
cd /c/dev/privat/github/coworkee && git add -A && git commit -m "build: coworkee repository scaffold"
```

---

### Task 2: Result, Error, ResultFactory, ConcurrencyConflictException, ICurrentUser

**Files:**
- Create: `coworkee/src/Coworkee.Core/Results/Error.cs`, `Results/Result.cs`, `Results/ResultFactory.cs`, `ConcurrencyConflictException.cs`, `Security/ICurrentUser.cs`, `Security/AnonymousCurrentUser.cs`
- Test: `coworkee/tests/Coworkee.Core.Tests/ResultTests.cs`
- Delete: `coworkee/tests/Coworkee.Core.Tests/SmokeTests.cs`

**Interfaces:**
- Produces:
  - `enum ErrorKind { Validation, NotFound, Forbidden, Conflict, Unexpected }`
  - `record Error(string Code, string Message, ErrorKind Kind, IReadOnlyDictionary<string,string[]>? Details = null)` mit `Error.Validation(details)`, `NotFound(code,msg)`, `Forbidden(code,msg)`, `Conflict(code,msg)`, `Unexpected(code,msg)`
  - `class Result { Error? Error; bool IsSuccess; static Success(); static Failure(Error) }`, `sealed class Result<T> : Result { T Value; static Success(T); static new Failure(Error) }`, implizite Konvertierungen von `T`/`Error`
  - `static bool ResultFactory.TryCreateFailure<TResult>(Error, out TResult)`
  - `sealed class ConcurrencyConflictException(string message, Exception inner) : Exception`
  - `interface ICurrentUser { Guid? UserId; Guid? TenantId; bool IsAuthenticated; IReadOnlyCollection<string> Roles; }`, `sealed class AnonymousCurrentUser : ICurrentUser`

- [ ] **Step 1: Failing tests**

`tests/Coworkee.Core.Tests/ResultTests.cs`:
```csharp
using Coworkee.Core.Results;

namespace Coworkee.Core.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_has_value()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_throws_on_value_access()
    {
        Result<int> result = Error.NotFound("item.not_found", "Item not found");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(ErrorKind.NotFound);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Factory_creates_failure_for_result_types()
    {
        var error = Error.Conflict("x", "y");

        ResultFactory.TryCreateFailure<Result>(error, out var plain).ShouldBeTrue();
        plain.Error.ShouldBe(error);
        ResultFactory.TryCreateFailure<Result<string>>(error, out var typed).ShouldBeTrue();
        typed.Error.ShouldBe(error);
    }

    [Fact]
    public void Factory_refuses_non_result_types() =>
        ResultFactory.TryCreateFailure<string>(Error.Unexpected("x", "y"), out _).ShouldBeFalse();
}
```

- [ ] **Step 2: Run, expect FAIL**

Run: `dotnet test tests/Coworkee.Core.Tests`
Expected: Build error `The type or namespace name 'Results' does not exist`.

- [ ] **Step 3: Implementation**

`src/Coworkee.Core/Results/Error.cs`:
```csharp
namespace Coworkee.Core.Results;

public enum ErrorKind
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
    Unexpected,
}

public sealed record Error(string Code, string Message, ErrorKind Kind, IReadOnlyDictionary<string, string[]>? Details = null)
{
    public static Error Validation(IReadOnlyDictionary<string, string[]> details) =>
        new("validation", "One or more validation errors occurred.", ErrorKind.Validation, details);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Unexpected(string code, string message) => new(code, message, ErrorKind.Unexpected);
}
```

`src/Coworkee.Core/Results/Result.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;

namespace Coworkee.Core.Results;

public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(null) => _value = value;

    private Result(Error error) : base(error)
    {
    }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result failed with '{Error.Code}'.");

    public static Result<T> Success(T value) => new(value);

    public static new Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
```

`src/Coworkee.Core/Results/ResultFactory.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;

namespace Coworkee.Core.Results;

public static class ResultFactory
{
    public static bool TryCreateFailure<TResult>(Error error, [MaybeNullWhen(false)] out TResult result)
    {
        if (FailureFactory<TResult>.Create is { } create)
        {
            result = create(error);
            return true;
        }

        result = default;
        return false;
    }

    private static class FailureFactory<TResult>
    {
        public static readonly Func<Error, TResult>? Create = Build();

        private static Func<Error, TResult>? Build()
        {
            var type = typeof(TResult);
            if (type == typeof(Result))
            {
                return error => (TResult)(object)Result.Failure(error);
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
            {
                return type.GetMethod(nameof(Result.Failure), [typeof(Error)])!.CreateDelegate<Func<Error, TResult>>();
            }

            return null;
        }
    }
}
```

`src/Coworkee.Core/ConcurrencyConflictException.cs`:
```csharp
namespace Coworkee.Core;

public sealed class ConcurrencyConflictException(string message, Exception innerException) : Exception(message, innerException);
```

`src/Coworkee.Core/Security/ICurrentUser.cs`:
```csharp
namespace Coworkee.Core.Security;

public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid? TenantId { get; }

    bool IsAuthenticated { get; }

    IReadOnlyCollection<string> Roles { get; }
}
```

`src/Coworkee.Core/Security/AnonymousCurrentUser.cs`:
```csharp
namespace Coworkee.Core.Security;

public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;

    public Guid? TenantId => null;

    public bool IsAuthenticated => false;

    public IReadOnlyCollection<string> Roles => [];
}
```

```bash
rm tests/Coworkee.Core.Tests/SmokeTests.cs
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test tests/Coworkee.Core.Tests`
Expected: `4 passed`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(core): result, error and current user abstractions"
```

---

### Task 3: Modulsystem

**Files:**
- Create: `coworkee/src/Coworkee.Core/Modularity/CoworkeeModule.cs`, `DependsOnAttribute.cs`, `ModuleLoader.cs`, `ModuleServiceCollectionExtensions.cs`
- Test: `coworkee/tests/Coworkee.Core.Tests/ModuleLoaderTests.cs`

**Interfaces:**
- Produces:
  - `abstract class CoworkeeModule { virtual void ConfigureServices(ModuleServiceContext context) }`
  - `sealed record ModuleServiceContext(IServiceCollection Services, IConfiguration Configuration)`
  - `sealed class DependsOnAttribute(params Type[] modules) : Attribute { IReadOnlyList<Type> Modules }`
  - `static IReadOnlyList<Type> ModuleLoader.Resolve(Type rootModule)` – Abhängigkeiten zuerst, jede Klasse einmal; Zyklus → `InvalidOperationException` mit Pfad `A -> B -> A`
  - `static IReadOnlyList<CoworkeeModule> IServiceCollection.AddCoworkeeModules<TRoot>(IConfiguration)` – instanziiert, ruft `ConfigureServices` in Reihenfolge, registriert `IReadOnlyList<CoworkeeModule>` als Singleton

- [ ] **Step 1: Failing tests**

`tests/Coworkee.Core.Tests/ModuleLoaderTests.cs`:
```csharp
using Coworkee.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Core.Tests;

public sealed class ModuleLoaderTests
{
    [Fact]
    public void Resolves_dependencies_first_and_once() =>
        ModuleLoader.Resolve(typeof(AppModule)).ShouldBe([typeof(BaseModule), typeof(LeftModule), typeof(RightModule), typeof(AppModule)]);

    [Fact]
    public void Detects_cycles_with_path()
    {
        var ex = Should.Throw<InvalidOperationException>(() => ModuleLoader.Resolve(typeof(CycleA)));

        ex.Message.ShouldContain("CycleA -> CycleB -> CycleA");
    }

    [Fact]
    public void Rejects_non_module_types() =>
        Should.Throw<InvalidOperationException>(() => ModuleLoader.Resolve(typeof(string)));

    [Fact]
    public void Configures_services_in_dependency_order()
    {
        var services = new ServiceCollection();

        services.AddCoworkeeModules<AppModule>(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        provider.GetServices<string>().ShouldBe(["Base", "Left", "Right", "App"]);
        provider.GetRequiredService<IReadOnlyList<CoworkeeModule>>().Count.ShouldBe(4);
    }

    private abstract class NamedModule(string name) : CoworkeeModule
    {
        public override void ConfigureServices(ModuleServiceContext context) => context.Services.AddSingleton(name);
    }

    private sealed class BaseModule() : NamedModule("Base");

    [DependsOn(typeof(BaseModule))]
    private sealed class LeftModule() : NamedModule("Left");

    [DependsOn(typeof(BaseModule))]
    private sealed class RightModule() : NamedModule("Right");

    [DependsOn(typeof(LeftModule), typeof(RightModule))]
    private sealed class AppModule() : NamedModule("App");

    [DependsOn(typeof(CycleB))]
    private sealed class CycleA : CoworkeeModule;

    [DependsOn(typeof(CycleA))]
    private sealed class CycleB : CoworkeeModule;
}
```

- [ ] **Step 2: Run, expect FAIL**

Run: `dotnet test tests/Coworkee.Core.Tests`
Expected: Build error `The type or namespace name 'Modularity' does not exist`.

- [ ] **Step 3: Implementation**

`src/Coworkee.Core/Modularity/CoworkeeModule.cs`:
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Core.Modularity;

public abstract class CoworkeeModule
{
    public virtual void ConfigureServices(ModuleServiceContext context)
    {
    }
}

public sealed record ModuleServiceContext(IServiceCollection Services, IConfiguration Configuration);
```

`src/Coworkee.Core/Modularity/DependsOnAttribute.cs`:
```csharp
namespace Coworkee.Core.Modularity;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class DependsOnAttribute(params Type[] modules) : Attribute
{
    public IReadOnlyList<Type> Modules { get; } = modules;
}
```

`src/Coworkee.Core/Modularity/ModuleLoader.cs`:
```csharp
using System.Reflection;

namespace Coworkee.Core.Modularity;

public static class ModuleLoader
{
    public static IReadOnlyList<Type> Resolve(Type rootModule)
    {
        var ordered = new List<Type>();
        var done = new HashSet<Type>();
        var visiting = new Stack<Type>();

        Visit(rootModule);
        return ordered;

        void Visit(Type type)
        {
            if (done.Contains(type))
            {
                return;
            }

            if (type.IsAbstract || !typeof(CoworkeeModule).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"'{type.FullName}' is not a concrete {nameof(CoworkeeModule)}.");
            }

            if (visiting.Contains(type))
            {
                var path = visiting.Reverse().SkipWhile(t => t != type).Append(type).Select(t => t.Name);
                throw new InvalidOperationException($"Module dependency cycle: {string.Join(" -> ", path)}");
            }

            visiting.Push(type);
            foreach (var dependency in type.GetCustomAttributes<DependsOnAttribute>().SelectMany(a => a.Modules))
            {
                Visit(dependency);
            }

            visiting.Pop();
            done.Add(type);
            ordered.Add(type);
        }
    }
}
```

`src/Coworkee.Core/Modularity/ModuleServiceCollectionExtensions.cs`:
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Core.Modularity;

public static class ModuleServiceCollectionExtensions
{
    public static IReadOnlyList<CoworkeeModule> AddCoworkeeModules<TRoot>(this IServiceCollection services, IConfiguration configuration)
        where TRoot : CoworkeeModule
    {
        var modules = ModuleLoader.Resolve(typeof(TRoot))
            .Select(type => (CoworkeeModule)Activator.CreateInstance(type, nonPublic: true)!)
            .ToArray();

        var context = new ModuleServiceContext(services, configuration);
        foreach (var module in modules)
        {
            module.ConfigureServices(context);
        }

        services.AddSingleton<IReadOnlyList<CoworkeeModule>>(modules);
        return modules;
    }
}
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test tests/Coworkee.Core.Tests`
Expected: `8 passed`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(core): module system with dependency ordering"
```

---

### Task 4: Coworkee.Domain

**Files:**
- Create: `coworkee/src/Coworkee.Domain/Coworkee.Domain.csproj`, `Entity.cs`, `IDomainEvent.cs`, `AggregateRoot.cs`, `Markers.cs`, `Attributes.cs`
- Test: `coworkee/tests/Coworkee.Domain.Tests/Coworkee.Domain.Tests.csproj`, `AggregateRootTests.cs`

**Interfaces:**
- Produces:
  - `abstract class Entity { Guid Id { get; protected init; } }` (Default `Guid.CreateVersion7()`)
  - `interface IDomainEvent`
  - `abstract class AggregateRoot : Entity { IReadOnlyList<IDomainEvent> DomainEvents; protected void Raise(IDomainEvent); void ClearDomainEvents() }`
  - `interface IAuditable { DateTimeOffset CreatedAt; Guid? CreatedBy; DateTimeOffset? ModifiedAt; Guid? ModifiedBy }` (get/set)
  - `interface ISoftDelete { bool IsDeleted; DateTimeOffset? DeletedAt; Guid? DeletedBy }` (get/set)
  - `interface IMultiTenant { Guid TenantId }` (get/set)
  - `interface IHasConcurrencyToken { uint Version }` (get/set)
  - `NotAuditedAttribute` (Class, Property), `SensitiveAttribute` (Property)

- [ ] **Step 1: Failing test**

`tests/Coworkee.Domain.Tests/Coworkee.Domain.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.Domain\Coworkee.Domain.csproj" />
  </ItemGroup>
</Project>
```

`tests/Coworkee.Domain.Tests/AggregateRootTests.cs`:
```csharp
namespace Coworkee.Domain.Tests;

public sealed class AggregateRootTests
{
    [Fact]
    public void New_entities_get_unique_version7_ids()
    {
        var a = new Order();
        var b = new Order();

        a.Id.ShouldNotBe(Guid.Empty);
        a.Id.ShouldNotBe(b.Id);
        a.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Raised_events_are_collected_until_cleared()
    {
        var order = new Order();

        order.Place();
        order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderPlaced>().OrderId.ShouldBe(order.Id);

        order.ClearDomainEvents();
        order.DomainEvents.ShouldBeEmpty();
    }

    private sealed record OrderPlaced(Guid OrderId) : IDomainEvent;

    private sealed class Order : AggregateRoot
    {
        public void Place() => Raise(new OrderPlaced(Id));
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

```bash
dotnet sln Coworkee.slnx add tests/Coworkee.Domain.Tests/Coworkee.Domain.Tests.csproj
dotnet test tests/Coworkee.Domain.Tests
```
Expected: Build error, project `Coworkee.Domain` not found.

- [ ] **Step 3: Implementation**

`src/Coworkee.Domain/Coworkee.Domain.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

`src/Coworkee.Domain/Entity.cs`:
```csharp
namespace Coworkee.Domain;

public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}
```

`src/Coworkee.Domain/IDomainEvent.cs`:
```csharp
namespace Coworkee.Domain;

public interface IDomainEvent;
```

`src/Coworkee.Domain/AggregateRoot.cs`:
```csharp
namespace Coworkee.Domain;

public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
```

`src/Coworkee.Domain/Markers.cs`:
```csharp
namespace Coworkee.Domain;

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }

    Guid? CreatedBy { get; set; }

    DateTimeOffset? ModifiedAt { get; set; }

    Guid? ModifiedBy { get; set; }
}

public interface ISoftDelete
{
    bool IsDeleted { get; set; }

    DateTimeOffset? DeletedAt { get; set; }

    Guid? DeletedBy { get; set; }
}

public interface IMultiTenant
{
    Guid TenantId { get; set; }
}

public interface IHasConcurrencyToken
{
    uint Version { get; set; }
}
```

`src/Coworkee.Domain/Attributes.cs`:
```csharp
namespace Coworkee.Domain;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveAttribute : Attribute;
```

```bash
dotnet sln Coworkee.slnx add src/Coworkee.Domain/Coworkee.Domain.csproj
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test tests/Coworkee.Domain.Tests`
Expected: `2 passed`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(domain): entity, aggregate root and persistence markers"
```

---

### Task 5: Dispatcher

**Files:**
- Create: `coworkee/src/Coworkee.Application/Coworkee.Application.csproj`, `Messaging/Requests.cs`, `Messaging/IRequestMiddleware.cs`, `Messaging/MiddlewareOrder.cs`, `Messaging/Dispatcher.cs`, `Messaging/IDomainEventHandler.cs`, `Messaging/MessagingServiceCollectionExtensions.cs`
- Test: `coworkee/tests/Coworkee.Application.Tests/Coworkee.Application.Tests.csproj`, `DispatcherTests.cs`

**Interfaces:**
- Consumes: `Coworkee.Domain.IDomainEvent`
- Produces:
  - `interface IRequest<TResult>`, `ICommand<TResult> : IRequest<TResult>`, `IQuery<TResult> : IRequest<TResult>`
  - `interface IHandler<in TRequest, TResult> where TRequest : IRequest<TResult> { Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken); }`
  - `delegate Task<TResult> RequestHandlerDelegate<TResult>()`
  - `interface IRequestMiddleware { int Order { get; } Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken) where TRequest : IRequest<TResult>; }` – kleinere `Order` läuft weiter außen
  - `static class MiddlewareOrder { Logging = 100, Authorization = 200, Validation = 300, UnitOfWork = 400 }`
  - `interface IDispatcher { Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default); }`
  - `interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent { Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken); }`
  - `IServiceCollection.AddDispatcher()` (scoped `IDispatcher`), `IServiceCollection.AddMessagingFromAssembly(Assembly)` – registriert alle `IHandler<,>`, `IDomainEventHandler<>` (scoped) und FluentValidation-Validatoren (inkl. internal)

- [ ] **Step 1: Failing tests**

`tests/Coworkee.Application.Tests/Coworkee.Application.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.Application\Coworkee.Application.csproj" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>
</Project>
```

`tests/Coworkee.Application.Tests/DispatcherTests.cs`:
```csharp
using Coworkee.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Tests;

public sealed class DispatcherTests
{
    [Fact]
    public async Task Sends_request_to_its_handler()
    {
        await using var provider = Build();

        var result = await Dispatcher(provider).SendAsync(new Echo("hi"), TestContext.Current.CancellationToken);

        result.ShouldBe("hi");
    }

    [Fact]
    public async Task Runs_middlewares_by_order_outermost_first()
    {
        var log = new List<string>();
        await using var provider = Build(services =>
        {
            services.AddSingleton(log);
            services.AddScoped<IRequestMiddleware>(_ => new Recording(log, "inner", 20));
            services.AddScoped<IRequestMiddleware>(_ => new Recording(log, "outer", 10));
        });

        await Dispatcher(provider).SendAsync(new Echo("x"), TestContext.Current.CancellationToken);

        log.ShouldBe(["outer:before", "inner:before", "inner:after", "outer:after"]);
    }

    [Fact]
    public async Task Missing_handler_fails_with_clear_message()
    {
        await using var provider = Build();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => Dispatcher(provider).SendAsync(new Unhandled(), TestContext.Current.CancellationToken));

        ex.Message.ShouldContain(nameof(Unhandled));
    }

    private static IDispatcher Dispatcher(ServiceProvider provider) =>
        provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();

    private static ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddDispatcher();
        services.AddMessagingFromAssembly(typeof(DispatcherTests).Assembly);
        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public sealed record Echo(string Text) : IQuery<string>;

    public sealed record Unhandled : IQuery<int>;

    internal sealed class EchoHandler : IHandler<Echo, string>
    {
        public Task<string> HandleAsync(Echo request, CancellationToken cancellationToken) => Task.FromResult(request.Text);
    }

    private sealed class Recording(List<string> log, string name, int order) : IRequestMiddleware
    {
        public int Order => order;

        public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
            where TRequest : IRequest<TResult>
        {
            log.Add($"{name}:before");
            var result = await next();
            log.Add($"{name}:after");
            return result;
        }
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

```bash
dotnet sln Coworkee.slnx add tests/Coworkee.Application.Tests/Coworkee.Application.Tests.csproj
dotnet test tests/Coworkee.Application.Tests
```
Expected: Build error, project `Coworkee.Application` not found.

- [ ] **Step 3: Implementation**

`src/Coworkee.Application/Coworkee.Application.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Coworkee.Core\Coworkee.Core.csproj" />
    <ProjectReference Include="..\Coworkee.Domain\Coworkee.Domain.csproj" />
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```

`src/Coworkee.Application/Messaging/Requests.cs`:
```csharp
namespace Coworkee.Application.Messaging;

public interface IRequest<TResult>;

public interface ICommand<TResult> : IRequest<TResult>;

public interface IQuery<TResult> : IRequest<TResult>;

public interface IHandler<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
}
```

`src/Coworkee.Application/Messaging/IRequestMiddleware.cs`:
```csharp
namespace Coworkee.Application.Messaging;

public delegate Task<TResult> RequestHandlerDelegate<TResult>();

public interface IRequestMiddleware
{
    int Order { get; }

    Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>;
}
```

`src/Coworkee.Application/Messaging/MiddlewareOrder.cs`:
```csharp
namespace Coworkee.Application.Messaging;

public static class MiddlewareOrder
{
    public const int Logging = 100;
    public const int Authorization = 200;
    public const int Validation = 300;
    public const int UnitOfWork = 400;
}
```

`src/Coworkee.Application/Messaging/IDomainEventHandler.cs`:
```csharp
using Coworkee.Domain;

namespace Coworkee.Application.Messaging;

public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
```

`src/Coworkee.Application/Messaging/Dispatcher.cs`:
```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    public Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoker = (RequestInvoker<TResult>)Invokers.GetOrAdd(
            request.GetType(),
            type => Activator.CreateInstance(typeof(RequestInvoker<,>).MakeGenericType(type, typeof(TResult)))!);
        return invoker.InvokeAsync(request, services, cancellationToken);
    }
}

internal abstract class RequestInvoker<TResult>
{
    public abstract Task<TResult> InvokeAsync(object request, IServiceProvider services, CancellationToken cancellationToken);
}

internal sealed class RequestInvoker<TRequest, TResult> : RequestInvoker<TResult>
    where TRequest : IRequest<TResult>
{
    public override Task<TResult> InvokeAsync(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var typed = (TRequest)request;
        var handler = services.GetService<IHandler<TRequest, TResult>>()
            ?? throw new InvalidOperationException($"No handler registered for '{typeof(TRequest).Name}'.");

        RequestHandlerDelegate<TResult> pipeline = () => handler.HandleAsync(typed, cancellationToken);
        foreach (var middleware in services.GetServices<IRequestMiddleware>().OrderByDescending(m => m.Order))
        {
            var next = pipeline;
            pipeline = () => middleware.InvokeAsync(typed, next, cancellationToken);
        }

        return pipeline();
    }
}
```

`src/Coworkee.Application/Messaging/MessagingServiceCollectionExtensions.cs`:
```csharp
using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

public static class MessagingServiceCollectionExtensions
{
    private static readonly Type[] HandlerTypes = [typeof(IHandler<,>), typeof(IDomainEventHandler<>)];

    public static IServiceCollection AddDispatcher(this IServiceCollection services) =>
        services.AddScoped<IDispatcher, Dispatcher>();

    public static IServiceCollection AddMessagingFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }))
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType && HandlerTypes.Contains(i.GetGenericTypeDefinition())))
            {
                services.AddScoped(contract, type);
            }
        }

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped, includeInternalTypes: true);
        return services;
    }
}
```

```bash
dotnet sln Coworkee.slnx add src/Coworkee.Application/Coworkee.Application.csproj
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test tests/Coworkee.Application.Tests`
Expected: `3 passed`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(application): request dispatcher with ordered middleware pipeline"
```

---

### Task 6: Standard-Middlewares und CoworkeeApplicationModule

**Files:**
- Create: `coworkee/src/Coworkee.Application/IUnitOfWork.cs`, `Messaging/LoggingMiddleware.cs`, `Messaging/ValidationMiddleware.cs`, `Messaging/UnitOfWorkMiddleware.cs`, `CoworkeeApplicationModule.cs`
- Test: `coworkee/tests/Coworkee.Application.Tests/MiddlewareTests.cs`

**Interfaces:**
- Consumes: Dispatcher (Task 5), `Result`, `ResultFactory`, `Error.Validation`, `ICurrentUser`, `AnonymousCurrentUser`, `CoworkeeModule`
- Produces:
  - `interface IUnitOfWork { Task SaveChangesAsync(CancellationToken cancellationToken); }`
  - `ValidationMiddleware`: Fehler → `Result`-Failure mit `ErrorKind.Validation` (Details: Property → Meldungen) oder `FluentValidation.ValidationException`, wenn `TResult` kein Result ist
  - `UnitOfWorkMiddleware`: nur für `ICommand<>`, nur bei Erfolg, nur wenn `IUnitOfWork` registriert ist
  - `CoworkeeApplicationModule : CoworkeeModule` – registriert Dispatcher, alle drei Middlewares, `TryAdd` `TimeProvider.System` und `ICurrentUser` → `AnonymousCurrentUser`

- [ ] **Step 1: Failing tests**

`tests/Coworkee.Application.Tests/MiddlewareTests.cs`:
```csharp
using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Application.Tests;

public sealed class MiddlewareTests
{
    [Fact]
    public async Task Invalid_result_request_returns_validation_failure()
    {
        var (dispatcher, _) = Build();

        var result = await dispatcher.SendAsync(new Rename(""), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Details!.ShouldContainKey(nameof(Rename.Name));
    }

    [Fact]
    public async Task Invalid_plain_request_throws_validation_exception()
    {
        var (dispatcher, _) = Build();

        await Should.ThrowAsync<ValidationException>(() => dispatcher.SendAsync(new Shout(""), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Successful_command_saves_unit_of_work()
    {
        var (dispatcher, unitOfWork) = Build();

        await dispatcher.SendAsync(new Rename("ok"), TestContext.Current.CancellationToken);

        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_command_and_queries_do_not_save()
    {
        var (dispatcher, unitOfWork) = Build();

        await dispatcher.SendAsync(new Rename("fail"), TestContext.Current.CancellationToken);
        await dispatcher.SendAsync(new Shout("query"), TestContext.Current.CancellationToken);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Works_without_unit_of_work_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoworkeeModules<TestModule>(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();

        var result = await provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>()
            .SendAsync(new Rename("ok"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    private static (IDispatcher Dispatcher, IUnitOfWork UnitOfWork) Build()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoworkeeModules<TestModule>(new ConfigurationBuilder().Build());
        services.AddSingleton(unitOfWork);
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>(), unitOfWork);
    }

    [DependsOn(typeof(CoworkeeApplicationModule))]
    private sealed class TestModule : CoworkeeModule
    {
        public override void ConfigureServices(ModuleServiceContext context) =>
            context.Services.AddMessagingFromAssembly(typeof(MiddlewareTests).Assembly);
    }

    public sealed record Rename(string Name) : ICommand<Result>;

    public sealed record Shout(string Text) : IQuery<string>;

    internal sealed class RenameValidator : AbstractValidator<Rename>
    {
        public RenameValidator() => RuleFor(r => r.Name).NotEmpty();
    }

    internal sealed class ShoutValidator : AbstractValidator<Shout>
    {
        public ShoutValidator() => RuleFor(r => r.Text).NotEmpty();
    }

    internal sealed class RenameHandler : IHandler<Rename, Result>
    {
        public Task<Result> HandleAsync(Rename request, CancellationToken cancellationToken) =>
            Task.FromResult(request.Name == "fail" ? Result.Failure(Error.Conflict("rename.failed", "Failed")) : Result.Success());
    }

    internal sealed class ShoutHandler : IHandler<Shout, string>
    {
        public Task<string> HandleAsync(Shout request, CancellationToken cancellationToken) => Task.FromResult(request.Text.ToUpperInvariant());
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

Run: `dotnet test tests/Coworkee.Application.Tests`
Expected: Build error `'IUnitOfWork' could not be found`.

- [ ] **Step 3: Implementation**

`src/Coworkee.Application/IUnitOfWork.cs`:
```csharp
namespace Coworkee.Application;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

`src/Coworkee.Application/Messaging/LoggingMiddleware.cs`:
```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Coworkee.Application.Messaging;

internal sealed partial class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Logging;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await next();
            LogHandled(typeof(TRequest).Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception, typeof(TRequest).Name);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {Request} in {ElapsedMs} ms")]
    private partial void LogHandled(string request, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request {Request} failed")]
    private partial void LogFailed(Exception exception, string request);
}
```

`src/Coworkee.Application/Messaging/ValidationMiddleware.cs`:
```csharp
using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

internal sealed class ValidationMiddleware(IServiceProvider services) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Validation;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        var validators = services.GetServices<IValidator<TRequest>>().ToArray();
        if (validators.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(r => r.Errors).ToArray();
        if (failures.Length == 0)
        {
            return await next();
        }

        var error = Error.Validation(failures
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray(), StringComparer.Ordinal));

        return ResultFactory.TryCreateFailure<TResult>(error, out var failed) ? failed : throw new ValidationException(failures);
    }
}
```

`src/Coworkee.Application/Messaging/UnitOfWorkMiddleware.cs`:
```csharp
using Coworkee.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

internal sealed class UnitOfWorkMiddleware(IServiceProvider services) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.UnitOfWork;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        var result = await next();
        if (request is not ICommand<TResult> || result is Result { IsSuccess: false })
        {
            return result;
        }

        if (services.GetService<IUnitOfWork>() is { } unitOfWork)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
```

`src/Coworkee.Application/CoworkeeApplicationModule.cs`:
```csharp
using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Application;

public sealed class CoworkeeApplicationModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddDispatcher();
        services.AddScoped<IRequestMiddleware, LoggingMiddleware>();
        services.AddScoped<IRequestMiddleware, ValidationMiddleware>();
        services.AddScoped<IRequestMiddleware, UnitOfWorkMiddleware>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();
    }
}
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test tests/Coworkee.Application.Tests`
Expected: `8 passed`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(application): logging, validation and unit-of-work middlewares"
```

---

### Task 7: EF-Basis, Interceptors für SoftDelete/Audit-Felder/Tenant, Concurrency, Testing-Fixture

**Files:**
- Create: `coworkee/src/Coworkee.Infrastructure/Coworkee.Infrastructure.csproj`, `Persistence/IModelContributor.cs`, `Persistence/CoworkeeDbContext.cs`, `Persistence/DbContextServiceCollectionExtensions.cs`, `Persistence/Interceptors/SoftDeleteInterceptor.cs`, `Persistence/Interceptors/AuditablePropertiesInterceptor.cs`, `Persistence/Interceptors/MultiTenantInterceptor.cs`
- Create: `coworkee/src/Coworkee.Testing/Coworkee.Testing.csproj`, `PostgresFixture.cs`, `TestCurrentUser.cs`
- Test: `coworkee/tests/Coworkee.Infrastructure.Tests/Coworkee.Infrastructure.Tests.csproj`, `TestModel.cs`, `DatabaseFixture.cs`, `PersistenceConventionTests.cs`

**Interfaces:**
- Consumes: Domain-Marker (Task 4), `ICurrentUser`, `IUnitOfWork`, `ConcurrencyConflictException`
- Produces:
  - `interface IModelContributor { void Apply(ModelBuilder modelBuilder); }`
  - `abstract class CoworkeeDbContext(DbContextOptions options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors) : DbContext, IUnitOfWork` mit `const string SoftDeleteFilter = "SoftDelete"`, `const string TenantFilter = "Tenant"`, `Guid? CurrentTenantId`; `IUnitOfWork.SaveChangesAsync` übersetzt `DbUpdateConcurrencyException` in `ConcurrencyConflictException`
  - `IServiceCollection.AddCoworkeeDbContext<TContext>(Action<IServiceProvider, DbContextOptionsBuilder> configure)` – registriert Context, Interceptors (Reihenfolge SoftDelete → AuditableProperties → MultiTenant; Task 8/9 erweitern), `CoworkeeDbContext` und `IUnitOfWork` als Alias, `TryAdd` `TimeProvider.System` und `AnonymousCurrentUser`
  - `Coworkee.Testing.PostgresFixture : IAsyncLifetime { string ConnectionString; Task ResetAsync(); }`, `Coworkee.Testing.TestCurrentUser : ICurrentUser` (settable)

- [ ] **Step 1: Testing-Projekt und Failing tests**

`src/Coworkee.Testing/Coworkee.Testing.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Coworkee.Core\Coworkee.Core.csproj" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Respawn" />
    <PackageReference Include="Npgsql" />
    <PackageReference Include="xunit.v3.extensibility.core" />
  </ItemGroup>
</Project>
```

`src/Coworkee.Testing/PostgresFixture.cs`:
```csharp
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace Coworkee.Testing;

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private Respawner? _respawner;

    public string ConnectionString => _container.GetConnectionString();

    public virtual async ValueTask InitializeAsync() => await _container.StartAsync();

    public virtual async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner ??= await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
        await _respawner.ResetAsync(connection);
    }
}
```

`src/Coworkee.Testing/TestCurrentUser.cs`:
```csharp
using Coworkee.Core.Security;

namespace Coworkee.Testing;

public sealed class TestCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; } = Guid.CreateVersion7();

    public Guid? TenantId { get; set; } = Guid.CreateVersion7();

    public bool IsAuthenticated => UserId is not null;

    public IReadOnlyCollection<string> Roles { get; set; } = [];
}
```

`tests/Coworkee.Infrastructure.Tests/Coworkee.Infrastructure.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.Infrastructure\Coworkee.Infrastructure.csproj" />
    <ProjectReference Include="..\..\src\Coworkee.Testing\Coworkee.Testing.csproj" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>
</Project>
```

`tests/Coworkee.Infrastructure.Tests/TestModel.cs`:
```csharp
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Tests;

public sealed record DocumentRenamed(Guid DocumentId, string Title) : IDomainEvent;

public sealed class Document : AggregateRoot, IAuditable, ISoftDelete, IMultiTenant, IHasConcurrencyToken
{
    public required string Title { get; set; }

    [Sensitive]
    public string? Secret { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public Guid TenantId { get; set; }

    public uint Version { get; set; }

    public void Rename(string title)
    {
        Title = title;
        Raise(new DocumentRenamed(Id, title));
    }
}

public sealed class TestModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Document>();
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors)
{
    public DbSet<Document> Documents => Set<Document>();
}
```

`tests/Coworkee.Infrastructure.Tests/DatabaseFixture.cs`:
```csharp
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

[assembly: AssemblyFixture(typeof(Coworkee.Infrastructure.Tests.DatabaseFixture))]

namespace Coworkee.Infrastructure.Tests;

public sealed class DatabaseFixture : PostgresFixture
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var provider = CreateServices(new TestCurrentUser(), new FakeTimeProvider());
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
    }

    public ServiceProvider CreateServices(ICurrentUser user, TimeProvider clock, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(user);
        services.AddSingleton(clock);
        services.AddSingleton<IModelContributor, TestModelContributor>();
        services.AddCoworkeeDbContext<TestDbContext>((_, options) => options.UseNpgsql(ConnectionString));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
```

`tests/Coworkee.Infrastructure.Tests/PersistenceConventionTests.cs`:
```csharp
using Coworkee.Application;
using Coworkee.Core;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class PersistenceConventionTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Insert_sets_audit_fields_and_tenant()
    {
        var id = await InsertAsync("First");

        var stored = await QueryAsync(db => db.Documents.SingleAsync(d => d.Id == id));
        stored.CreatedAt.ShouldBe(_clock.GetUtcNow());
        stored.CreatedBy.ShouldBe(_user.UserId);
        stored.TenantId.ShouldBe(_user.TenantId!.Value);
    }

    [Fact]
    public async Task Insert_without_tenant_is_rejected()
    {
        _user.TenantId = null;

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => InsertAsync("Orphan"));

        ex.Message.ShouldContain("tenant", Case.Insensitive);
    }

    [Fact]
    public async Task Update_sets_modified_fields()
    {
        var id = await InsertAsync("Old");
        _clock.Advance(TimeSpan.FromMinutes(5));

        await ExecuteAsync(async db => (await db.Documents.SingleAsync(d => d.Id == id)).Title = "New");

        var stored = await QueryAsync(db => db.Documents.SingleAsync(d => d.Id == id));
        stored.ModifiedAt.ShouldBe(_clock.GetUtcNow());
        stored.ModifiedBy.ShouldBe(_user.UserId);
    }

    [Fact]
    public async Task Delete_is_soft_and_hidden_by_default()
    {
        var id = await InsertAsync("Doomed");

        await ExecuteAsync(async db => db.Documents.Remove(await db.Documents.SingleAsync(d => d.Id == id)));

        (await QueryAsync(db => db.Documents.AnyAsync(d => d.Id == id))).ShouldBeFalse();
        var deleted = await QueryAsync(db => db.Documents.IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]).SingleAsync(d => d.Id == id));
        deleted.IsDeleted.ShouldBeTrue();
        deleted.DeletedBy.ShouldBe(_user.UserId);
        deleted.DeletedAt.ShouldBe(_clock.GetUtcNow());
    }

    [Fact]
    public async Task Other_tenants_and_tenantless_users_see_nothing()
    {
        var id = await InsertAsync("Mine");

        _user.TenantId = Guid.CreateVersion7();
        (await QueryAsync(db => db.Documents.AnyAsync(d => d.Id == id))).ShouldBeFalse();

        _user.TenantId = null;
        (await QueryAsync(db => db.Documents.AnyAsync(d => d.Id == id))).ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_update_raises_conflict()
    {
        var id = await InsertAsync("Shared");
        await using var provider = database.CreateServices(_user, _clock);
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var a = first.ServiceProvider.GetRequiredService<TestDbContext>();
        var b = second.ServiceProvider.GetRequiredService<TestDbContext>();
        (await a.Documents.SingleAsync(d => d.Id == id)).Title = "A";
        (await b.Documents.SingleAsync(d => d.Id == id)).Title = "B";
        await ((IUnitOfWork)a).SaveChangesAsync(TestContext.Current.CancellationToken);

        await Should.ThrowAsync<ConcurrencyConflictException>(() => ((IUnitOfWork)b).SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private async Task<Guid> InsertAsync(string title)
    {
        var document = new Document { Title = title };
        await ExecuteAsync(db =>
        {
            db.Documents.Add(document);
            return Task.CompletedTask;
        });
        return document.Id;
    }

    private async Task ExecuteAsync(Func<TestDbContext, Task> action)
    {
        await using var provider = database.CreateServices(_user, _clock);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await action(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<T> QueryAsync<T>(Func<TestDbContext, Task<T>> query)
    {
        await using var provider = database.CreateServices(_user, _clock);
        await using var scope = provider.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

```bash
dotnet sln Coworkee.slnx add src/Coworkee.Testing/Coworkee.Testing.csproj tests/Coworkee.Infrastructure.Tests/Coworkee.Infrastructure.Tests.csproj
dotnet test tests/Coworkee.Infrastructure.Tests
```
Expected: Build error, project `Coworkee.Infrastructure` not found.

- [ ] **Step 3: Implementation**

`src/Coworkee.Infrastructure/Coworkee.Infrastructure.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Coworkee.Application\Coworkee.Application.csproj" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>
</Project>
```

`src/Coworkee.Infrastructure/Persistence/IModelContributor.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Persistence;

public interface IModelContributor
{
    void Apply(ModelBuilder modelBuilder);
}
```

`src/Coworkee.Infrastructure/Persistence/CoworkeeDbContext.cs`:
```csharp
using System.Reflection;
using Coworkee.Application;
using Coworkee.Core;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Persistence;

public abstract class CoworkeeDbContext(DbContextOptions options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : DbContext(options), IUnitOfWork
{
    public const string SoftDeleteFilter = "SoftDelete";
    public const string TenantFilter = "Tenant";

    private static readonly MethodInfo ApplyFiltersMethod =
        typeof(CoworkeeDbContext).GetMethod(nameof(ApplyQueryFilters), BindingFlags.Instance | BindingFlags.NonPublic)!;

    public Guid? CurrentTenantId => currentUser.TenantId;

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The entity was changed by someone else.", exception);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var contributor in contributors)
        {
            contributor.Apply(modelBuilder);
        }

        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null && !t.IsOwned()).ToArray())
        {
            ApplyFiltersMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            if (typeof(IHasConcurrencyToken).IsAssignableFrom(entityType.ClrType) && Database.IsNpgsql())
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(IHasConcurrencyToken.Version)).IsRowVersion();
            }
        }
    }

    private void ApplyQueryFilters<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class
    {
        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(SoftDeleteFilter, e => !((ISoftDelete)e).IsDeleted);
        }

        if (typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity)))
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(TenantFilter, e => ((IMultiTenant)e).TenantId == CurrentTenantId);
        }
    }
}
```

`src/Coworkee.Infrastructure/Persistence/Interceptors/SoftDeleteInterceptor.cs`:
```csharp
using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Persistence.Interceptors;

internal sealed class SoftDeleteInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        foreach (var entry in context?.ChangeTracker.Entries<ISoftDelete>().Where(e => e.State == EntityState.Deleted) ?? [])
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = clock.GetUtcNow();
            entry.Entity.DeletedBy = currentUser.UserId;
        }
    }
}
```

`src/Coworkee.Infrastructure/Persistence/Interceptors/AuditablePropertiesInterceptor.cs`:
```csharp
using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Persistence.Interceptors;

internal sealed class AuditablePropertiesInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        var now = clock.GetUtcNow();
        foreach (var entry in context?.ChangeTracker.Entries<IAuditable>() ?? [])
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = currentUser.UserId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedAt = now;
                entry.Entity.ModifiedBy = currentUser.UserId;
            }
        }
    }
}
```

`src/Coworkee.Infrastructure/Persistence/Interceptors/MultiTenantInterceptor.cs`:
```csharp
using Coworkee.Core.Security;
using Coworkee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Persistence.Interceptors;

internal sealed class MultiTenantInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        foreach (var entry in context?.ChangeTracker.Entries<IMultiTenant>().Where(e => e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty) ?? [])
        {
            entry.Entity.TenantId = currentUser.TenantId
                ?? throw new InvalidOperationException($"Cannot save {entry.Metadata.ClrType.Name} without a tenant.");
        }
    }
}
```

`src/Coworkee.Infrastructure/Persistence/DbContextServiceCollectionExtensions.cs`:
```csharp
using Coworkee.Application;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Infrastructure.Persistence;

public static class DbContextServiceCollectionExtensions
{
    public static IServiceCollection AddCoworkeeDbContext<TContext>(this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configure)
        where TContext : CoworkeeDbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<AuditablePropertiesInterceptor>();
        services.AddScoped<MultiTenantInterceptor>();

        services.AddDbContext<TContext>((provider, options) =>
        {
            configure(provider, options);
            options.AddInterceptors(
                provider.GetRequiredService<SoftDeleteInterceptor>(),
                provider.GetRequiredService<AuditablePropertiesInterceptor>(),
                provider.GetRequiredService<MultiTenantInterceptor>());
        });
        services.AddScoped<CoworkeeDbContext>(provider => provider.GetRequiredService<TContext>());
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<TContext>());
        return services;
    }
}
```

```bash
dotnet sln Coworkee.slnx add src/Coworkee.Infrastructure/Coworkee.Infrastructure.csproj
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test tests/Coworkee.Infrastructure.Tests`
Expected: `6 passed` (erster Lauf zieht das Postgres-Image).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(infrastructure): ef base context with soft delete, tenant, audit fields and concurrency"
```

---

### Task 8: Audit-Trail

**Files:**
- Create: `coworkee/src/Coworkee.Infrastructure/Auditing/AuditEntry.cs`, `Persistence/CoworkeeModelContributor.cs`, `Persistence/Interceptors/AuditTrailInterceptor.cs`
- Modify: `coworkee/src/Coworkee.Infrastructure/Persistence/DbContextServiceCollectionExtensions.cs`
- Test: `coworkee/tests/Coworkee.Infrastructure.Tests/AuditTrailTests.cs`

**Interfaces:**
- Consumes: Task 7
- Produces:
  - `enum AuditAction { Created, Updated, Deleted, Restored }`
  - `sealed class AuditEntry { Guid Id; Guid? TenantId; string EntityType; string EntityId; AuditAction Action; Guid? ActorId; string? CorrelationId; DateTimeOffset OccurredAt; List<AuditChange> Changes }` (Tabelle `cw.AuditEntries`, Changes als JSON)
  - `sealed class AuditChange { string Property; string? OldValue; string? NewValue }` – Werte JSON-serialisiert, `[Sensitive]` → `"***"`
  - `CoworkeeModelContributor : IModelContributor` (wird von `AddCoworkeeDbContext` registriert)

- [ ] **Step 1: Failing tests**

`tests/Coworkee.Infrastructure.Tests/AuditTrailTests.cs`:
```csharp
using Coworkee.Infrastructure.Auditing;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class AuditTrailTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Insert_records_created_entry_with_new_values()
    {
        var id = await InsertAsync("First");

        var entry = (await EntriesAsync(id)).ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditAction.Created);
        entry.EntityType.ShouldBe(nameof(Document));
        entry.ActorId.ShouldBe(_user.UserId);
        entry.TenantId.ShouldBe(_user.TenantId);
        entry.Changes.ShouldContain(c => c.Property == nameof(Document.Title) && c.OldValue == null && c.NewValue == "\"First\"");
        entry.Changes.ShouldNotContain(c => c.Property == nameof(Document.CreatedAt));
    }

    [Fact]
    public async Task Update_records_only_changed_values()
    {
        var id = await InsertAsync("Old");

        await ExecuteAsync(async db => (await db.Documents.SingleAsync(d => d.Id == id)).Title = "New");

        var update = (await EntriesAsync(id)).Single(e => e.Action == AuditAction.Updated);
        update.Changes.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            c => c.Property.ShouldBe(nameof(Document.Title)),
            c => c.OldValue.ShouldBe("\"Old\""),
            c => c.NewValue.ShouldBe("\"New\""));
    }

    [Fact]
    public async Task Save_without_real_change_records_nothing()
    {
        var id = await InsertAsync("Same");

        await ExecuteAsync(async db => (await db.Documents.SingleAsync(d => d.Id == id)).Title = "Same");

        (await EntriesAsync(id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Soft_delete_records_deleted()
    {
        var id = await InsertAsync("Doomed");

        await ExecuteAsync(async db => db.Documents.Remove(await db.Documents.SingleAsync(d => d.Id == id)));

        (await EntriesAsync(id)).ShouldContain(e => e.Action == AuditAction.Deleted);
    }

    [Fact]
    public async Task Sensitive_values_are_masked()
    {
        var document = new Document { Title = "T", Secret = "p@ss" };
        await ExecuteAsync(db =>
        {
            db.Documents.Add(document);
            return Task.CompletedTask;
        });

        (await EntriesAsync(document.Id)).Single().Changes.Single(c => c.Property == nameof(Document.Secret)).NewValue.ShouldBe("\"***\"");
    }

    private async Task<Guid> InsertAsync(string title)
    {
        var document = new Document { Title = title };
        await ExecuteAsync(db =>
        {
            db.Documents.Add(document);
            return Task.CompletedTask;
        });
        return document.Id;
    }

    private async Task ExecuteAsync(Func<TestDbContext, Task> action)
    {
        await using var provider = database.CreateServices(_user, _clock);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await action(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<AuditEntry>> EntriesAsync(Guid id)
    {
        await using var provider = database.CreateServices(_user, _clock);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<AuditEntry>()
            .Where(e => e.EntityId == id.ToString())
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

Run: `dotnet test tests/Coworkee.Infrastructure.Tests`
Expected: Build error `The type or namespace name 'Auditing' does not exist`.

- [ ] **Step 3: Implementation**

`src/Coworkee.Infrastructure/Auditing/AuditEntry.cs`:
```csharp
using Coworkee.Domain;

namespace Coworkee.Infrastructure.Auditing;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
    Restored,
}

[NotAudited]
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid? TenantId { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public AuditAction Action { get; init; }

    public Guid? ActorId { get; init; }

    public string? CorrelationId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public List<AuditChange> Changes { get; init; } = [];
}

public sealed class AuditChange
{
    public required string Property { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }
}
```

`src/Coworkee.Infrastructure/Persistence/CoworkeeModelContributor.cs`:
```csharp
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Persistence;

internal sealed class CoworkeeModelContributor : IModelContributor
{
    public const string Schema = "cw";

    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("AuditEntries", Schema);
            entity.Property(e => e.EntityType).HasMaxLength(200);
            entity.Property(e => e.EntityId).HasMaxLength(200);
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.OccurredAt);
            entity.OwnsMany(e => e.Changes, changes => changes.ToJson());
        });
    }
}
```

`src/Coworkee.Infrastructure/Persistence/Interceptors/AuditTrailInterceptor.cs`:
```csharp
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Persistence.Interceptors;

internal sealed class AuditTrailInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private const string Masked = "\"***\"";

    private static readonly HashSet<string> BookkeepingProperties = new(StringComparer.Ordinal)
    {
        nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy), nameof(IAuditable.ModifiedAt), nameof(IAuditable.ModifiedBy),
        nameof(ISoftDelete.IsDeleted), nameof(ISoftDelete.DeletedAt), nameof(ISoftDelete.DeletedBy), nameof(IHasConcurrencyToken.Version),
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var correlationId = Activity.Current?.TraceId.ToHexString();
        var entries = context.ChangeTracker.Entries()
            .Where(IsAudited)
            .Select(entry => Create(entry, now, correlationId))
            .OfType<AuditEntry>()
            .ToList();
        context.Set<AuditEntry>().AddRange(entries);
    }

    private static bool IsAudited(EntityEntry entry) =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
        && !entry.Metadata.IsOwned()
        && entry.Metadata.ClrType.GetCustomAttribute<NotAuditedAttribute>() is null;

    private AuditEntry? Create(EntityEntry entry, DateTimeOffset now, string? correlationId)
    {
        var action = ActionOf(entry);
        var changes = entry.Properties
            .Where(p => !p.Metadata.IsShadowProperty() && !p.Metadata.IsPrimaryKey() && !BookkeepingProperties.Contains(p.Metadata.Name))
            .Where(p => p.Metadata.PropertyInfo?.GetCustomAttribute<NotAuditedAttribute>() is null)
            .Where(p => entry.State != EntityState.Modified || (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
            .Select(p => new AuditChange
            {
                Property = p.Metadata.Name,
                OldValue = entry.State == EntityState.Added ? null : Format(p, p.OriginalValue),
                NewValue = entry.State == EntityState.Deleted ? null : Format(p, p.CurrentValue),
            })
            .ToList();

        if (action == AuditAction.Updated && changes.Count == 0)
        {
            return null;
        }

        return new AuditEntry
        {
            TenantId = (entry.Entity as IMultiTenant)?.TenantId ?? currentUser.TenantId,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = string.Join(',', entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue)),
            Action = action,
            ActorId = currentUser.UserId,
            CorrelationId = correlationId,
            OccurredAt = now,
            Changes = changes,
        };
    }

    private static AuditAction ActionOf(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            return AuditAction.Created;
        }

        if (entry.State == EntityState.Deleted)
        {
            return AuditAction.Deleted;
        }

        if (entry.Entity is ISoftDelete && entry.Property(nameof(ISoftDelete.IsDeleted)) is { IsModified: true } deleted
            && !Equals(deleted.OriginalValue, deleted.CurrentValue))
        {
            return (bool)deleted.CurrentValue! ? AuditAction.Deleted : AuditAction.Restored;
        }

        return AuditAction.Updated;
    }

    private static string? Format(PropertyEntry property, object? value)
    {
        if (value is null)
        {
            return null;
        }

        return property.Metadata.PropertyInfo?.GetCustomAttribute<SensitiveAttribute>() is not null ? Masked : JsonSerializer.Serialize(value);
    }
}
```

Modify `src/Coworkee.Infrastructure/Persistence/DbContextServiceCollectionExtensions.cs` – full new content:
```csharp
using Coworkee.Application;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Infrastructure.Persistence;

public static class DbContextServiceCollectionExtensions
{
    public static IServiceCollection AddCoworkeeDbContext<TContext>(this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configure)
        where TContext : CoworkeeDbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelContributor, CoworkeeModelContributor>());
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<AuditablePropertiesInterceptor>();
        services.AddScoped<MultiTenantInterceptor>();
        services.AddScoped<AuditTrailInterceptor>();

        services.AddDbContext<TContext>((provider, options) =>
        {
            configure(provider, options);
            options.AddInterceptors(
                provider.GetRequiredService<SoftDeleteInterceptor>(),
                provider.GetRequiredService<AuditablePropertiesInterceptor>(),
                provider.GetRequiredService<MultiTenantInterceptor>(),
                provider.GetRequiredService<AuditTrailInterceptor>());
        });
        services.AddScoped<CoworkeeDbContext>(provider => provider.GetRequiredService<TContext>());
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<TContext>());
        return services;
    }
}
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test tests/Coworkee.Infrastructure.Tests`
Expected: `11 passed`. (Die Fixture ruft `EnsureCreatedAsync` und legt `cw.AuditEntries` an.)

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(infrastructure): audit trail with field level changes"
```

---

### Task 9: Outbox

**Files:**
- Create: `coworkee/src/Coworkee.Infrastructure/Outbox/OutboxMessage.cs`, `Outbox/DomainEventDispatch.cs`, `Outbox/OutboxProcessor.cs`, `Outbox/OutboxBackgroundService.cs`, `Outbox/OutboxServiceCollectionExtensions.cs`, `Persistence/Interceptors/OutboxInterceptor.cs`
- Modify: `coworkee/src/Coworkee.Infrastructure/Persistence/CoworkeeModelContributor.cs`, `Persistence/DbContextServiceCollectionExtensions.cs`
- Test: `coworkee/tests/Coworkee.Infrastructure.Tests/OutboxTests.cs`

**Interfaces:**
- Consumes: `AggregateRoot.DomainEvents/ClearDomainEvents`, `IDomainEventHandler<T>`, `AddMessagingFromAssembly`
- Produces:
  - `sealed class OutboxMessage { Guid Id; string Type; string Payload; DateTimeOffset OccurredAt; DateTimeOffset? ProcessedAt; int Attempts; string? Error }` (Tabelle `cw.OutboxMessages`)
  - `sealed class OutboxProcessor<TContext> { const int MaxAttempts = 5; Task<int> ProcessBatchAsync(int batchSize, CancellationToken) }` – je Nachricht eigener Scope für Handler
  - `IServiceCollection.AddCoworkeeOutboxProcessing<TContext>()` – registriert Processor + Hosted Service (Polling 1 s)

- [ ] **Step 1: Failing tests**

`tests/Coworkee.Infrastructure.Tests/OutboxTests.cs`:
```csharp
using Coworkee.Application.Messaging;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Infrastructure.Tests;

public sealed class OutboxTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly TestCurrentUser _user = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly RecordingHandler _handler = new();

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Domain_events_are_stored_and_dispatched()
    {
        await using var provider = Services();
        var document = new Document { Title = "Draft" };
        document.Rename("Final");
        await SaveAsync(provider, document);

        var processed = await provider.GetRequiredService<OutboxProcessor<TestDbContext>>().ProcessBatchAsync(10, TestContext.Current.CancellationToken);

        processed.ShouldBe(1);
        _handler.Received.ShouldHaveSingleItem().Title.ShouldBe("Final");
        (await MessagesAsync(provider)).Single().ProcessedAt.ShouldBe(_clock.GetUtcNow());
        document.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Failing_message_is_marked_and_does_not_block_others()
    {
        await using var provider = Services();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            db.Set<OutboxMessage>().Add(new OutboxMessage { Type = "Unknown.Type, Missing", Payload = "{}", OccurredAt = _clock.GetUtcNow() });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var document = new Document { Title = "A" };
        document.Rename("B");
        await SaveAsync(provider, document);

        await provider.GetRequiredService<OutboxProcessor<TestDbContext>>().ProcessBatchAsync(10, TestContext.Current.CancellationToken);

        var messages = await MessagesAsync(provider);
        messages.Single(m => m.Type.StartsWith("Unknown", StringComparison.Ordinal)).ShouldSatisfyAllConditions(
            m => m.ProcessedAt.ShouldBeNull(),
            m => m.Attempts.ShouldBe(1),
            m => m.Error.ShouldNotBeNullOrEmpty());
        _handler.Received.ShouldHaveSingleItem();
    }

    private ServiceProvider Services() => database.CreateServices(_user, _clock, services =>
    {
        services.AddSingleton<IDomainEventHandler<DocumentRenamed>>(_handler);
        services.AddCoworkeeOutboxProcessing<TestDbContext>();
    });

    private static async Task SaveAsync(IServiceProvider provider, Document document)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        db.Documents.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<OutboxMessage>> MessagesAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<OutboxMessage>().ToListAsync(TestContext.Current.CancellationToken);
    }

    private sealed class RecordingHandler : IDomainEventHandler<DocumentRenamed>
    {
        public List<DocumentRenamed> Received { get; } = [];

        public Task HandleAsync(DocumentRenamed domainEvent, CancellationToken cancellationToken)
        {
            Received.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

Run: `dotnet test tests/Coworkee.Infrastructure.Tests`
Expected: Build error `The type or namespace name 'Outbox' does not exist`.

- [ ] **Step 3: Implementation**

`src/Coworkee.Infrastructure/Outbox/OutboxMessage.cs`:
```csharp
using Coworkee.Domain;

namespace Coworkee.Infrastructure.Outbox;

[NotAudited]
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? Error { get; set; }
}
```

Modify `src/Coworkee.Infrastructure/Persistence/CoworkeeModelContributor.cs` – add inside `Apply` after the `AuditEntry` block:
```csharp
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages", Schema);
            entity.Property(e => e.Type).HasMaxLength(1024);
            entity.HasIndex(e => new { e.ProcessedAt, e.OccurredAt });
        });
```
and add `using Coworkee.Infrastructure.Outbox;` at the top.

`src/Coworkee.Infrastructure/Persistence/Interceptors/OutboxInterceptor.cs`:
```csharp
using System.Text.Json;
using Coworkee.Domain;
using Coworkee.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Persistence.Interceptors;

internal sealed class OutboxInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var aggregate in context.ChangeTracker.Entries<AggregateRoot>().Select(e => e.Entity).Where(a => a.DomainEvents.Count > 0).ToArray())
        {
            context.Set<OutboxMessage>().AddRange(aggregate.DomainEvents.Select(domainEvent => new OutboxMessage
            {
                Type = domainEvent.GetType().AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                OccurredAt = now,
            }));
            aggregate.ClearDomainEvents();
        }
    }
}
```

`src/Coworkee.Infrastructure/Outbox/DomainEventDispatch.cs`:
```csharp
using System.Collections.Concurrent;
using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Infrastructure.Outbox;

internal static class DomainEventDispatch
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> Dispatchers = new();

    private static readonly MethodInfo DispatchTypedMethod =
        typeof(DomainEventDispatch).GetMethod(nameof(DispatchTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Task DispatchAsync(IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken) =>
        Dispatchers.GetOrAdd(domainEvent.GetType(), Build)(services, domainEvent, cancellationToken);

    private static Func<IServiceProvider, IDomainEvent, CancellationToken, Task> Build(Type eventType) =>
        DispatchTypedMethod.MakeGenericMethod(eventType).CreateDelegate<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>>();

    private static async Task DispatchTypedAsync<TEvent>(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
        }
    }
}
```

`src/Coworkee.Infrastructure/Outbox/OutboxProcessor.cs`:
```csharp
using System.Text.Json;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Infrastructure.Outbox;

public sealed partial class OutboxProcessor<TContext>(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboxProcessor<TContext>> logger)
    where TContext : CoworkeeDbContext
{
    public const int MaxAttempts = 5;

    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var messages = await db.Set<OutboxMessage>()
            .FromSql($"""SELECT * FROM cw."OutboxMessages" WHERE "ProcessedAt" IS NULL AND "Attempts" < {MaxAttempts} ORDER BY "OccurredAt" LIMIT {batchSize} FOR UPDATE SKIP LOCKED""")
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var type = Type.GetType(message.Type, throwOnError: true)!;
                var domainEvent = (IDomainEvent)JsonSerializer.Deserialize(message.Payload, type)!;
                await using var handlerScope = scopes.CreateAsyncScope();
                await DomainEventDispatch.DispatchAsync(domainEvent, handlerScope.ServiceProvider, cancellationToken);
                message.ProcessedAt = clock.GetUtcNow();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Attempts++;
                message.Error = exception.Message;
                LogFailed(exception, message.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} failed")]
    private partial void LogFailed(Exception exception, Guid messageId);
}
```

`src/Coworkee.Infrastructure/Outbox/OutboxBackgroundService.cs`:
```csharp
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;

namespace Coworkee.Infrastructure.Outbox;

internal sealed partial class OutboxBackgroundService<TContext>(OutboxProcessor<TContext> processor, ILogger<OutboxBackgroundService<TContext>> logger)
    : BackgroundService
    where TContext : CoworkeeDbContext
{
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ponytail: 1 s polling, add commit signal (LISTEN/NOTIFY) when event latency matters
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                while (await processor.ProcessBatchAsync(BatchSize, stoppingToken) == BatchSize)
                {
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogBatchFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox batch failed, retrying on next tick")]
    private partial void LogBatchFailed(Exception exception);
}
```
Add `using Microsoft.Extensions.Logging;` at the top of this file. A failing batch (e.g. database not yet migrated) must never stop the host.

`src/Coworkee.Infrastructure/Outbox/OutboxServiceCollectionExtensions.cs`:
```csharp
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Infrastructure.Outbox;

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddCoworkeeOutboxProcessing<TContext>(this IServiceCollection services)
        where TContext : CoworkeeDbContext
    {
        services.AddSingleton<OutboxProcessor<TContext>>();
        services.AddHostedService<OutboxBackgroundService<TContext>>();
        return services;
    }
}
```

Add `<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />` to `src/Coworkee.Infrastructure/Coworkee.Infrastructure.csproj` and `<PackageVersion Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.12" />` to `Directory.Packages.props`.

Modify `DbContextServiceCollectionExtensions.AddCoworkeeDbContext`: add `services.AddScoped<OutboxInterceptor>();` after the `AuditTrailInterceptor` registration and append `provider.GetRequiredService<OutboxInterceptor>()` as last argument of `options.AddInterceptors(...)`.

The test `Services()` registers the hosted service but never starts a host, so only `ProcessBatchAsync` runs – intended.

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test tests/Coworkee.Infrastructure.Tests`
Expected: `13 passed`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(infrastructure): transactional outbox for domain events"
```

---

### Task 10: Coworkee.AspNetCore

**Files:**
- Create: `coworkee/src/Coworkee.AspNetCore/Coworkee.AspNetCore.csproj`, `IWebModule.cs`, `CoworkeeWebApplicationExtensions.cs`, `Security/HttpCurrentUser.cs`, `Http/ResultHttpExtensions.cs`, `Http/CoworkeeExceptionHandler.cs`
- Test: `coworkee/tests/Coworkee.AspNetCore.Tests/Coworkee.AspNetCore.Tests.csproj`, `HttpPipelineTests.cs`, `HttpCurrentUserTests.cs`

**Interfaces:**
- Consumes: Module (Task 3), Result (Task 2), `ConcurrencyConflictException`, `CoworkeeApplicationModule`
- Produces:
  - `interface IWebModule { void ConfigureApplication(WebApplication app); }`
  - `WebApplicationBuilder.AddCoworkee<TRoot>()` – registriert `HttpCurrentUser` als `ICurrentUser` (vor den Modulen), Module, ProblemDetails, Exception-Handler, OpenAPI-Dokument `v1`
  - `WebApplication.UseCoworkee()` – `UseExceptionHandler`, `MapOpenApi` (`/openapi/v1.json`), Swagger UI (`/swagger`), `IWebModule.ConfigureApplication` in Modulreihenfolge
  - `Task<IResult> Task<Result<T>>.ToHttpResult()`, `Task<IResult> Task<Result>.ToHttpResult()`, `IResult Error.ToProblem()` – Validation 400 (ValidationProblem), NotFound 404, Forbidden 403, Conflict 409, Unexpected 500, `extensions.code`
  - Exception-Mapping: `FluentValidation.ValidationException` → 400 ValidationProblem, `ConcurrencyConflictException` → 409
  - Claims: `sub` (Fallback `ClaimTypes.NameIdentifier`) → UserId, `tenant` → TenantId, `role`/`ClaimTypes.Role` → Roles

- [ ] **Step 1: Failing tests**

`tests/Coworkee.AspNetCore.Tests/Coworkee.AspNetCore.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.AspNetCore\Coworkee.AspNetCore.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" />
  </ItemGroup>
</Project>
```

`tests/Coworkee.AspNetCore.Tests/HttpPipelineTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Core;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Coworkee.AspNetCore.Tests;

public sealed class HttpPipelineTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddCoworkee<TestWebModule>();
        _app = builder.Build();
        _app.UseCoworkee();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Success_returns_value()
    {
        var response = await _client.GetAsync("/value", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<int>(TestContext.Current.CancellationToken)).ShouldBe(42);
    }

    [Fact]
    public async Task Plain_success_returns_no_content() =>
        (await _client.PostAsync("/void", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

    [Theory]
    [InlineData("/missing", HttpStatusCode.NotFound, "thing.missing")]
    [InlineData("/conflict", HttpStatusCode.Conflict, "thing.conflict")]
    public async Task Failures_return_problem_with_code(string path, HttpStatusCode status, string code)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(status);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Validation_failure_returns_400_with_errors()
    {
        var response = await _client.GetAsync("/validate", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("errors").TryGetProperty(nameof(Named.Name), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Validation_exception_returns_400() =>
        (await _client.GetAsync("/throw-validation", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Concurrency_conflict_returns_409() =>
        (await _client.GetAsync("/throw-concurrency", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

    [Fact]
    public async Task OpenApi_document_lists_endpoints()
    {
        var document = await _client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        document.ShouldContain("\"/value\"");
    }

    public sealed record Named(string Name) : IQuery<Result<string>>;

    internal sealed class NamedValidator : AbstractValidator<Named>
    {
        public NamedValidator() => RuleFor(n => n.Name).NotEmpty();
    }

    internal sealed class NamedHandler : IHandler<Named, Result<string>>
    {
        public Task<Result<string>> HandleAsync(Named request, CancellationToken cancellationToken) => Task.FromResult<Result<string>>(request.Name);
    }

    [DependsOn(typeof(CoworkeeApplicationModule))]
    private sealed class TestWebModule : CoworkeeModule, IWebModule
    {
        public override void ConfigureServices(ModuleServiceContext context) => context.Services.AddMessagingFromAssembly(typeof(HttpPipelineTests).Assembly);

        public void ConfigureApplication(WebApplication app)
        {
            app.MapGet("/value", () => Task.FromResult<Result<int>>(42).ToHttpResult());
            app.MapPost("/void", () => Task.FromResult(Result.Success()).ToHttpResult());
            app.MapGet("/missing", () => Task.FromResult<Result<int>>(Error.NotFound("thing.missing", "Missing")).ToHttpResult());
            app.MapGet("/conflict", () => Task.FromResult(Result.Failure(Error.Conflict("thing.conflict", "Conflict"))).ToHttpResult());
            app.MapGet("/validate", (IDispatcher dispatcher, CancellationToken ct) => dispatcher.SendAsync(new Named(""), ct).ToHttpResult());
            app.MapGet("/throw-validation", IResult () => throw new ValidationException("bad"));
            app.MapGet("/throw-concurrency", IResult () => throw new ConcurrencyConflictException("changed", new InvalidOperationException()));
        }
    }
}
```

`tests/Coworkee.AspNetCore.Tests/HttpCurrentUserTests.cs`:
```csharp
using System.Security.Claims;
using Coworkee.AspNetCore.Security;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Tests;

public sealed class HttpCurrentUserTests
{
    [Fact]
    public void Reads_identity_from_claims()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", userId.ToString()), new Claim("tenant", tenantId.ToString()), new Claim("role", "admin")],
                authenticationType: "test")),
        };

        var user = new HttpCurrentUser(new HttpContextAccessor { HttpContext = context });

        user.IsAuthenticated.ShouldBeTrue();
        user.UserId.ShouldBe(userId);
        user.TenantId.ShouldBe(tenantId);
        user.Roles.ShouldBe(["admin"]);
    }

    [Fact]
    public void Anonymous_without_http_context()
    {
        var user = new HttpCurrentUser(new HttpContextAccessor());

        user.IsAuthenticated.ShouldBeFalse();
        user.UserId.ShouldBeNull();
        user.TenantId.ShouldBeNull();
        user.Roles.ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

```bash
dotnet sln Coworkee.slnx add tests/Coworkee.AspNetCore.Tests/Coworkee.AspNetCore.Tests.csproj
dotnet test tests/Coworkee.AspNetCore.Tests
```
Expected: Build error, project `Coworkee.AspNetCore` not found.

- [ ] **Step 3: Implementation**

`src/Coworkee.AspNetCore/Coworkee.AspNetCore.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\Coworkee.Application\Coworkee.Application.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Swashbuckle.AspNetCore.SwaggerUI" />
  </ItemGroup>
</Project>
```

`src/Coworkee.AspNetCore/IWebModule.cs`:
```csharp
using Microsoft.AspNetCore.Builder;

namespace Coworkee.AspNetCore;

public interface IWebModule
{
    void ConfigureApplication(WebApplication app);
}
```

`src/Coworkee.AspNetCore/Security/HttpCurrentUser.cs`:
```csharp
using System.Security.Claims;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => ParseGuid(Principal?.FindFirstValue("sub") ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier));

    public Guid? TenantId => ParseGuid(Principal?.FindFirstValue("tenant"));

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles =>
        Principal?.Claims.Where(c => c.Type is "role" or ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
```

`src/Coworkee.AspNetCore/Http/ResultHttpExtensions.cs`:
```csharp
using Coworkee.Core.Results;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Http;

public static class ResultHttpExtensions
{
    public static async Task<IResult> ToHttpResult<T>(this Task<Result<T>> task)
    {
        var result = await task;
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    public static async Task<IResult> ToHttpResult(this Task<Result> task)
    {
        var result = await task;
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code };
        return error.Kind == ErrorKind.Validation
            ? TypedResults.ValidationProblem(error.Details?.ToDictionary() ?? [], title: error.Message, extensions: extensions)
            : TypedResults.Problem(title: error.Message, statusCode: StatusCodeOf(error.Kind), extensions: extensions);
    }

    private static int StatusCodeOf(ErrorKind kind) => kind switch
    {
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };
}
```

`src/Coworkee.AspNetCore/Http/CoworkeeExceptionHandler.cs`:
```csharp
using Coworkee.Core;
using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Http;

internal sealed class CoworkeeExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var error = exception switch
        {
            ValidationException validation => Error.Validation(validation.Errors
                .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray(), StringComparer.Ordinal)),
            ConcurrencyConflictException conflict => Error.Conflict("concurrency.conflict", conflict.Message),
            _ => null,
        };

        if (error is null)
        {
            return false;
        }

        await error.ToProblem().ExecuteAsync(httpContext);
        return true;
    }
}
```

`src/Coworkee.AspNetCore/CoworkeeWebApplicationExtensions.cs`:
```csharp
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore.Security;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AspNetCore;

public static class CoworkeeWebApplicationExtensions
{
    public static WebApplicationBuilder AddCoworkee<TRoot>(this WebApplicationBuilder builder)
        where TRoot : CoworkeeModule
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
        builder.Services.AddCoworkeeModules<TRoot>(builder.Configuration);
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<CoworkeeExceptionHandler>();
        builder.Services.AddOpenApi("v1");
        return builder;
    }

    public static WebApplication UseCoworkee(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.MapOpenApi();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "v1"));
        foreach (var module in app.Services.GetRequiredService<IReadOnlyList<CoworkeeModule>>().OfType<IWebModule>())
        {
            module.ConfigureApplication(app);
        }

        return app;
    }
}
```

```bash
dotnet sln Coworkee.slnx add src/Coworkee.AspNetCore/Coworkee.AspNetCore.csproj
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test tests/Coworkee.AspNetCore.Tests`
Expected: `10 passed`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(aspnetcore): web host integration, result mapping and openapi"
```

---

### Task 11: Coworkee.Aspire, Testing-Factory, Architekturtests, lokales Packen

**Files:**
- Create: `coworkee/src/Coworkee.Aspire/Coworkee.Aspire.csproj`, `CoworkeeInfrastructureExtensions.cs`
- Create: `coworkee/src/Coworkee.Testing/PostgresWebApplicationFactory.cs`
- Modify: `coworkee/src/Coworkee.Testing/Coworkee.Testing.csproj`
- Create: `coworkee/build/pack-local.ps1`, `coworkee/README.md`
- Test: `coworkee/tests/Coworkee.Aspire.Tests/Coworkee.Aspire.Tests.csproj`, `InfrastructureTests.cs`, `coworkee/tests/Coworkee.ArchitectureTests/Coworkee.ArchitectureTests.csproj`, `LayerTests.cs`

**Interfaces:**
- Produces:
  - `sealed record CoworkeeInfrastructure(IResourceBuilder<PostgresDatabaseResource> Database, IResourceBuilder<RedisResource> Redis, IResourceBuilder<MailPitContainerResource> Mail)`
  - `IDistributedApplicationBuilder.AddCoworkeeInfrastructure(string databaseName)` – Ressourcen `postgres`, `<databaseName>`, `redis`, `mail`; Postgres und Redis persistent mit Volume
  - `class PostgresWebApplicationFactory<TProgram>(PostgresFixture postgres, string connectionStringName) : WebApplicationFactory<TProgram>` – setzt `ConnectionStrings:<name>`
  - `build/pack-local.ps1` → `artifacts/nuget/Coworkee.*.0.1.0-dev.<yyyyMMddHHmmss>.nupkg`

- [ ] **Step 1: Failing tests**

`tests/Coworkee.Aspire.Tests/Coworkee.Aspire.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.Aspire\Coworkee.Aspire.csproj" />
  </ItemGroup>
</Project>
```

`tests/Coworkee.Aspire.Tests/InfrastructureTests.cs`:
```csharp
using Aspire.Hosting;

namespace Coworkee.Aspire.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public void Adds_database_cache_and_mail_resources()
    {
        var builder = DistributedApplication.CreateBuilder();

        var infrastructure = builder.AddCoworkeeInfrastructure("shareme");

        builder.Resources.Select(r => r.Name).ShouldBe(["postgres", "shareme", "redis", "mail"], ignoreOrder: true);
        infrastructure.Database.Resource.DatabaseName.ShouldBe("shareme");
    }
}
```

`tests/Coworkee.ArchitectureTests/Coworkee.ArchitectureTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.AspNetCore\Coworkee.AspNetCore.csproj" />
    <ProjectReference Include="..\..\src\Coworkee.Infrastructure\Coworkee.Infrastructure.csproj" />
    <PackageReference Include="NetArchTest.eNhancedEdition" />
  </ItemGroup>
</Project>
```

`tests/Coworkee.ArchitectureTests/LayerTests.cs`:
```csharp
using System.Reflection;
using Coworkee.Application;
using Coworkee.Core.Results;
using Coworkee.Domain;
using NetArchTest.Rules;

namespace Coworkee.ArchitectureTests;

public sealed class LayerTests
{
    public static TheoryData<string, string[]> Rules() => new()
    {
        { typeof(Result).Assembly.GetName().Name!, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Coworkee.Domain", "Coworkee.Application"] },
        { typeof(Entity).Assembly.GetName().Name!, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Coworkee.Application"] },
        { typeof(IUnitOfWork).Assembly.GetName().Name!, ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Coworkee.Infrastructure"] },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Layer_does_not_depend_on_forbidden_namespaces(string assemblyName, string[] forbidden)
    {
        var result = Types.InAssembly(Assembly.Load(assemblyName)).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

```bash
dotnet sln Coworkee.slnx add tests/Coworkee.Aspire.Tests/Coworkee.Aspire.Tests.csproj tests/Coworkee.ArchitectureTests/Coworkee.ArchitectureTests.csproj
dotnet test tests/Coworkee.Aspire.Tests
```
Expected: Build error, project `Coworkee.Aspire` not found.

- [ ] **Step 3: Implementation**

`src/Coworkee.Aspire/Coworkee.Aspire.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting" />
    <PackageReference Include="Aspire.Hosting.PostgreSQL" />
    <PackageReference Include="Aspire.Hosting.Redis" />
    <PackageReference Include="CommunityToolkit.Aspire.Hosting.MailPit" />
  </ItemGroup>
</Project>
```

`src/Coworkee.Aspire/CoworkeeInfrastructureExtensions.cs`:
```csharp
using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.MailPit;

namespace Aspire.Hosting;

public sealed record CoworkeeInfrastructure(
    IResourceBuilder<PostgresDatabaseResource> Database,
    IResourceBuilder<RedisResource> Redis,
    IResourceBuilder<MailPitContainerResource> Mail);

public static class CoworkeeInfrastructureExtensions
{
    public static CoworkeeInfrastructure AddCoworkeeInfrastructure(this IDistributedApplicationBuilder builder, string databaseName)
    {
        var database = builder.AddPostgres("postgres")
            .WithDataVolume()
            .WithLifetime(ContainerLifetime.Persistent)
            .AddDatabase(databaseName);
        var redis = builder.AddRedis("redis")
            .WithDataVolume()
            .WithLifetime(ContainerLifetime.Persistent);
        var mail = builder.AddMailPit("mail");
        return new CoworkeeInfrastructure(database, redis, mail);
    }
}
```
If `MailPitContainerResource` lives in a different namespace in CommunityToolkit 13.6.0, fix the `using` per compiler hint (`dotnet build` names the type's namespace).

Modify `src/Coworkee.Testing/Coworkee.Testing.csproj` – add `<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />`.

`src/Coworkee.Testing/PostgresWebApplicationFactory.cs`:
```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Coworkee.Testing;

public class PostgresWebApplicationFactory<TProgram>(PostgresFixture postgres, string connectionStringName) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting($"ConnectionStrings:{connectionStringName}", postgres.ConnectionString);
}
```

`build/pack-local.ps1`:
```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$suffix = 'dev.' + (Get-Date -Format 'yyyyMMddHHmmss')
dotnet pack (Join-Path $root 'Coworkee.slnx') -c Release -o (Join-Path $root 'artifacts/nuget') --version-suffix $suffix
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Packed Coworkee 0.1.0-$suffix"
```

`README.md`:
```markdown
# Coworkee

Modulares .NET-10-Anwendungsframework (Module, Dispatcher, EF-Basis mit Audit/Outbox, ASP.NET-Integration, Aspire, Testing).

- Build & Test: `dotnet test Coworkee.slnx` (Docker erforderlich)
- Lokaler Feed: `pwsh build/pack-local.ps1` → `artifacts/nuget`
```

```bash
dotnet sln Coworkee.slnx add src/Coworkee.Aspire/Coworkee.Aspire.csproj
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test Coworkee.slnx`
Expected: alle Tests grün (Core 8, Domain 2, Application 8, Infrastructure 13, AspNetCore 10, Aspire 1, Architecture 3).

Run: `pwsh build/pack-local.ps1 && ls artifacts/nuget`
Expected: 7 Pakete `Coworkee.{Core,Domain,Application,Infrastructure,AspNetCore,Testing,Aspire}.0.1.0-dev.*.nupkg`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: aspire infrastructure, test factory, architecture tests and local packing"
```

---

### Task 12: Shareme-Repo, ServiceDefaults, AppHost

**Files:**
- Create: `shareme/global.json`, `shareme/Directory.Build.props`, `shareme/Directory.Packages.props`, `shareme/Directory.Build.targets`, `shareme/nuget.config`, `shareme/.editorconfig`, `shareme/Shareme.slnx`
- Create: `shareme/src/Shareme.ServiceDefaults/Shareme.ServiceDefaults.csproj`, `Extensions.cs`
- Create: `shareme/src/Shareme.AppHost/Shareme.AppHost.csproj`, `AppHost.cs`

**Interfaces:**
- Consumes: Coworkee-Pakete aus `../coworkee/artifacts/nuget` (Task 11)
- Produces:
  - Item `CoworkeePackage` in Projekten → je nach `CoworkeeReferenceMode` (Default `Package`) PackageReference oder ProjectReference auf `../coworkee/src/<Name>/<Name>.csproj`
  - `IHostApplicationBuilder.AddServiceDefaults()`, `WebApplication.MapDefaultEndpoints()` (`/health`, `/alive`)
  - AppHost mit Ressourcen `postgres`, `shareme`, `redis`, `mail`

- [ ] **Step 1: Build-Dateien**

`global.json`: identisch zu Coworkee (Task 1).

`.editorconfig`: identisch zu Coworkee (Task 1).

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
    <TreatWarningsAsErrors Condition="$(MSBuildProjectDirectory.Replace('\', '/').Contains('/src/'))">true</TreatWarningsAsErrors>
  </PropertyGroup>
  <PropertyGroup Condition="$(MSBuildProjectDirectory.Replace('\', '/').Contains('/tests/'))">
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup Condition="$(MSBuildProjectDirectory.Replace('\', '/').Contains('/tests/'))">
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
    <PackageReference Include="Shouldly" />
    <Using Include="Xunit" />
    <Using Include="Shouldly" />
  </ItemGroup>
</Project>
```

`Directory.Packages.props`:
```xml
<Project>
  <PropertyGroup>
    <CoworkeeVersion>0.1.0-*</CoworkeeVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Coworkee.Core" Version="$(CoworkeeVersion)" />
    <PackageVersion Include="Coworkee.Domain" Version="$(CoworkeeVersion)" />
    <PackageVersion Include="Coworkee.Application" Version="$(CoworkeeVersion)" />
    <PackageVersion Include="Coworkee.Infrastructure" Version="$(CoworkeeVersion)" />
    <PackageVersion Include="Coworkee.AspNetCore" Version="$(CoworkeeVersion)" />
    <PackageVersion Include="Coworkee.Testing" Version="$(CoworkeeVersion)" />
    <PackageVersion Include="Coworkee.Aspire" Version="$(CoworkeeVersion)" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageVersion Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.10.0" />
    <PackageVersion Include="Microsoft.Extensions.ServiceDiscovery" Version="10.10.0" />
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.19.1" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.19.1" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.19.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.19.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.19.0" />
    <PackageVersion Include="Aspire.Hosting.Testing" Version="13.6.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageVersion Include="xunit.v3" Version="4.0.1" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="4.0.0" />
    <PackageVersion Include="Shouldly" Version="4.3.0" />
  </ItemGroup>
</Project>
```

`Directory.Build.targets`:
```xml
<Project>
  <PropertyGroup>
    <CoworkeeReferenceMode Condition="'$(CoworkeeReferenceMode)' == ''">Package</CoworkeeReferenceMode>
    <CoworkeeRoot Condition="'$(CoworkeeRoot)' == ''">$(MSBuildThisFileDirectory)..\coworkee\</CoworkeeRoot>
  </PropertyGroup>
  <ItemGroup Condition="'$(CoworkeeReferenceMode)' == 'Project'">
    <ProjectReference Include="@(CoworkeePackage->'$(CoworkeeRoot)src\%(Identity)\%(Identity).csproj')" IsAspireProjectResource="false" />
  </ItemGroup>
  <ItemGroup Condition="'$(CoworkeeReferenceMode)' != 'Project'">
    <PackageReference Include="@(CoworkeePackage)" />
  </ItemGroup>
</Project>
```

`nuget.config`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="coworkee-local" value="../coworkee/artifacts/nuget" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="coworkee-local">
      <package pattern="Coworkee.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

- [ ] **Step 2: ServiceDefaults**

`src/Shareme.ServiceDefaults/Shareme.ServiceDefaults.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAspireSharedProject>true</IsAspireSharedProject>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
  </ItemGroup>
</Project>
```

`src/Shareme.ServiceDefaults/Extensions.cs`:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        return app;
    }

    private static void ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments(HealthEndpointPath) && !ctx.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
    }
}
```
Health endpoints are mapped in all environments (Api has no secrets in health output; Aspire and tests rely on them).

- [ ] **Step 3: AppHost**

`src/Shareme.AppHost/Shareme.AppHost.csproj`:
```xml
<Project Sdk="Aspire.AppHost.Sdk/13.6.0">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <UserSecretsId>shareme-apphost</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <CoworkeePackage Include="Coworkee.Aspire" />
  </ItemGroup>
</Project>
```

`src/Shareme.AppHost/AppHost.cs`:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddCoworkeeInfrastructure("shareme");

builder.Build().Run();
```

```bash
cd /c/dev/privat/github/shareme
dotnet new sln -n Shareme --format slnx
dotnet sln Shareme.slnx add src/Shareme.ServiceDefaults/Shareme.ServiceDefaults.csproj src/Shareme.AppHost/Shareme.AppHost.csproj
```

- [ ] **Step 4: Build in beiden Referenzmodi**

Run: `cd /c/dev/privat/github/shareme && dotnet build Shareme.slnx`
Expected: Build erfolgreich, Coworkee.Aspire aus `coworkee-local` aufgelöst.
If restore complains that `Aspire.Hosting.AppHost` needs a version under Central Package Management, add `<PackageVersion Include="Aspire.Hosting.AppHost" Version="13.6.0" />` to `Directory.Packages.props` and rebuild.

Run: `dotnet build Shareme.slnx -p:CoworkeeReferenceMode=Project`
Expected: Build erfolgreich mit Projektreferenz auf `../coworkee/src/Coworkee.Aspire`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "build: shareme scaffold with service defaults and aspire app host"
```

---

### Task 13: Shareme Application, Infrastructure, Migrations

**Files:**
- Create: `shareme/src/Shareme.Application/Shareme.Application.csproj`, `ShareMeApplicationModule.cs`, `System/GetSystemInfo.cs`
- Create: `shareme/src/Shareme.Infrastructure/Shareme.Infrastructure.csproj`, `ShareMeDbContext.cs`, `ShareMeInfrastructureModule.cs`, `ShareMeDbContextDesignFactory.cs`, `Migrations/*` (generated)
- Create: `shareme/src/Shareme.Migrations/Shareme.Migrations.csproj`, `Program.cs`, `MigrationWorker.cs`
- Modify: `shareme/src/Shareme.AppHost/AppHost.cs`

**Interfaces:**
- Consumes: `CoworkeeModule`, `DependsOn`, `CoworkeeApplicationModule`, `AddMessagingFromAssembly`, `IQuery`, `IHandler`, `Result`, `CoworkeeDbContext`, `AddCoworkeeDbContext`, `IModelContributor`
- Produces:
  - `sealed record GetSystemInfo : IQuery<Result<SystemInfo>>`, `sealed record SystemInfo(string Product, string Version)`
  - `ShareMeApplicationModule` (depends on `CoworkeeApplicationModule`), `ShareMeInfrastructureModule` (depends on `ShareMeApplicationModule`; registriert `ShareMeDbContext` mit Connection String `shareme`, Migrations-Historie in `cw.__EFMigrationsHistory`, Health-Check `database`)
  - `const string ShareMeInfrastructureModule.ConnectionStringName = "shareme"`
  - Migration `Initial` (Tabellen `cw.AuditEntries`, `cw.OutboxMessages`)

- [ ] **Step 1: Projekte anlegen**

`src/Shareme.Application/Shareme.Application.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <CoworkeePackage Include="Coworkee.Application" />
  </ItemGroup>
</Project>
```

`src/Shareme.Application/ShareMeApplicationModule.cs`:
```csharp
using Coworkee.Application;
using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;

namespace Shareme.Application;

[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class ShareMeApplicationModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddMessagingFromAssembly(typeof(ShareMeApplicationModule).Assembly);
}
```

`src/Shareme.Application/System/GetSystemInfo.cs`:
```csharp
using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Shareme.Application.System;

public sealed record GetSystemInfo : IQuery<Result<SystemInfo>>;

public sealed record SystemInfo(string Product, string Version);

internal sealed class GetSystemInfoHandler : IHandler<GetSystemInfo, Result<SystemInfo>>
{
    private static readonly string Version =
        typeof(GetSystemInfoHandler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public Task<Result<SystemInfo>> HandleAsync(GetSystemInfo request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<SystemInfo>>(new SystemInfo("Shareme", Version));
}
```

`src/Shareme.Infrastructure/Shareme.Infrastructure.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Shareme.Application\Shareme.Application.csproj" />
    <CoworkeePackage Include="Coworkee.Infrastructure" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`src/Shareme.Infrastructure/ShareMeDbContext.cs`:
```csharp
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Shareme.Infrastructure;

public sealed class ShareMeDbContext(DbContextOptions<ShareMeDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);
```

`src/Shareme.Infrastructure/ShareMeInfrastructureModule.cs`:
```csharp
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Application;

namespace Shareme.Infrastructure;

[DependsOn(typeof(ShareMeApplicationModule))]
public sealed class ShareMeInfrastructureModule : CoworkeeModule
{
    public const string ConnectionStringName = "shareme";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeDbContext<ShareMeDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "cw")));
        context.Services.AddHealthChecks().AddDbContextCheck<ShareMeDbContext>("database");
    }
}
```

`src/Shareme.Infrastructure/ShareMeDbContextDesignFactory.cs`:
```csharp
using Coworkee.Core.Modularity;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shareme.Infrastructure;

public sealed class ShareMeDbContextDesignFactory : IDesignTimeDbContextFactory<ShareMeDbContext>
{
    public ShareMeDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new($"ConnectionStrings:{ShareMeInfrastructureModule.ConnectionStringName}", "Host=localhost;Database=shareme_design")])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCoworkeeModules<ShareMeInfrastructureModule>(configuration);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ShareMeDbContext>();
    }
}
```
Add `<PackageReference Include="Microsoft.Extensions.Configuration" />` to `Shareme.Infrastructure.csproj` (for `AddInMemoryCollection`) and `<PackageVersion Include="Microsoft.Extensions.Configuration" Version="10.0.12" />` to `Directory.Packages.props`.

`src/Shareme.Migrations/Shareme.Migrations.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <ItemGroup>
    <ProjectReference Include="..\Shareme.Infrastructure\Shareme.Infrastructure.csproj" />
    <ProjectReference Include="..\Shareme.ServiceDefaults\Shareme.ServiceDefaults.csproj" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>
</Project>
```

`src/Shareme.Migrations/MigrationWorker.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Shareme.Infrastructure;

namespace Shareme.Migrations;

internal sealed class MigrationWorker(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShareMeDbContext>().Database.MigrateAsync(stoppingToken);
        lifetime.StopApplication();
    }
}
```

`src/Shareme.Migrations/Program.cs`:
```csharp
using Coworkee.Core.Modularity;
using Shareme.Infrastructure;
using Shareme.Migrations;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<ShareMeInfrastructureModule>(builder.Configuration);
builder.Services.AddHostedService<MigrationWorker>();
builder.Build().Run();
```

```bash
dotnet sln Shareme.slnx add src/Shareme.Application/Shareme.Application.csproj src/Shareme.Infrastructure/Shareme.Infrastructure.csproj src/Shareme.Migrations/Shareme.Migrations.csproj
```

- [ ] **Step 2: Initiale Migration erzeugen**

Run:
```bash
dotnet build Shareme.slnx
dotnet ef migrations add Initial --project src/Shareme.Infrastructure --startup-project src/Shareme.Infrastructure --output-dir Migrations
```
Expected: `src/Shareme.Infrastructure/Migrations/*_Initial.cs` mit `migrationBuilder.EnsureSchema(name: "cw")`, Tabellen `AuditEntries` (Spalte `Changes` vom Typ `jsonb`) und `OutboxMessages`.

- [ ] **Step 3: AppHost verdrahten**

`src/Shareme.AppHost/AppHost.cs`:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var infrastructure = builder.AddCoworkeeInfrastructure("shareme");

builder.AddProject<Projects.Shareme_Migrations>("shareme-migrations")
    .WithReference(infrastructure.Database)
    .WaitFor(infrastructure.Database);

builder.Build().Run();
```
Add `<ProjectReference Include="..\Shareme.Migrations\Shareme.Migrations.csproj" />` to `Shareme.AppHost.csproj`.

- [ ] **Step 4: Verifizieren**

Run: `dotnet build Shareme.slnx`
Expected: Build erfolgreich, keine Warnungen.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: shareme application, infrastructure with initial migration and migration worker"
```

---

### Task 14: Shareme.Api mit Integrationstests

**Files:**
- Create: `shareme/src/Shareme.Api/Shareme.Api.csproj`, `Program.cs`, `ShareMeApiModule.cs`, `Properties/launchSettings.json`
- Modify: `shareme/src/Shareme.AppHost/AppHost.cs`, `shareme/src/Shareme.AppHost/Shareme.AppHost.csproj`
- Test: `shareme/tests/Shareme.Api.Tests/Shareme.Api.Tests.csproj`, `ApiFixture.cs`, `ApiTests.cs`

**Interfaces:**
- Consumes: `AddCoworkee`, `UseCoworkee`, `IWebModule`, `ToHttpResult`, `GetSystemInfo`, `ShareMeInfrastructureModule`, `AddCoworkeeOutboxProcessing`, `PostgresFixture`, `PostgresWebApplicationFactory`
- Produces:
  - `GET /api/v1/system/info` → `200 {"product":"Shareme","version":"…"}`
  - `GET /health` → 200 (inkl. DB-Check), `/openapi/v1.json`, `/swagger`
  - `public partial class Program` für Tests

- [ ] **Step 1: Failing tests**

`tests/Shareme.Api.Tests/Shareme.Api.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Shareme.Api\Shareme.Api.csproj" />
    <CoworkeePackage Include="Coworkee.Testing" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  </ItemGroup>
</Project>
```

`tests/Shareme.Api.Tests/ApiFixture.cs`:
```csharp
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Infrastructure;

[assembly: AssemblyFixture(typeof(Shareme.Api.Tests.ApiFixture))]

namespace Shareme.Api.Tests;

public sealed class ApiFixture : PostgresFixture
{
    public PostgresWebApplicationFactory<Program> Factory { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory = new PostgresWebApplicationFactory<Program>(this, ShareMeInfrastructureModule.ConnectionStringName);
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShareMeDbContext>().Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await base.DisposeAsync();
    }
}
```

`tests/Shareme.Api.Tests/ApiTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Shareme.Api.Tests;

public sealed class ApiTests(ApiFixture api)
{
    [Fact]
    public async Task System_info_returns_product()
    {
        using var client = api.Factory.CreateClient();

        var info = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/info", TestContext.Current.CancellationToken);

        info.GetProperty("product").GetString().ShouldBe("Shareme");
        info.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Health_includes_database()
    {
        using var client = api.Factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApi_documents_system_info()
    {
        using var client = api.Factory.CreateClient();

        var document = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        document.ShouldContain("/api/v1/system/info");
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

```bash
dotnet sln Shareme.slnx add tests/Shareme.Api.Tests/Shareme.Api.Tests.csproj
dotnet test tests/Shareme.Api.Tests
```
Expected: Build error, project `Shareme.Api` not found.

- [ ] **Step 3: Implementation**

`src/Shareme.Api/Shareme.Api.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="..\Shareme.Infrastructure\Shareme.Infrastructure.csproj" />
    <ProjectReference Include="..\Shareme.ServiceDefaults\Shareme.ServiceDefaults.csproj" />
    <CoworkeePackage Include="Coworkee.AspNetCore" />
  </ItemGroup>
</Project>
```

`src/Shareme.Api/ShareMeApiModule.cs`:
```csharp
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Outbox;
using Shareme.Application.System;
using Shareme.Infrastructure;

namespace Shareme.Api;

[DependsOn(typeof(ShareMeInfrastructureModule))]
public sealed class ShareMeApiModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeOutboxProcessing<ShareMeDbContext>();

    public void ConfigureApplication(WebApplication app)
    {
        var system = app.MapGroup("/api/v1/system").WithTags("System");
        system.MapGet("/info", (IDispatcher dispatcher, CancellationToken ct) => dispatcher.SendAsync(new GetSystemInfo(), ct).ToHttpResult())
            .WithName("GetSystemInfo");
    }
}
```

`src/Shareme.Api/Program.cs`:
```csharp
using Coworkee.AspNetCore;
using Shareme.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCoworkee<ShareMeApiModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();

public partial class Program;
```

`src/Shareme.Api/Properties/launchSettings.json`:
```json
{
  "profiles": {
    "https": {
      "commandName": "Project",
      "launchBrowser": true,
      "launchUrl": "swagger",
      "applicationUrl": "https://localhost:7201;http://localhost:5201",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
    }
  }
}
```

`src/Shareme.AppHost/AppHost.cs`:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var infrastructure = builder.AddCoworkeeInfrastructure("shareme");

var migrations = builder.AddProject<Projects.Shareme_Migrations>("shareme-migrations")
    .WithReference(infrastructure.Database)
    .WaitFor(infrastructure.Database);

builder.AddProject<Projects.Shareme_Api>("shareme-api")
    .WithReference(infrastructure.Database)
    .WaitForCompletion(migrations)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();
```
Add `<ProjectReference Include="..\Shareme.Api\Shareme.Api.csproj" />` to `Shareme.AppHost.csproj`.

```bash
dotnet sln Shareme.slnx add src/Shareme.Api/Shareme.Api.csproj
```

- [ ] **Step 4: Run, expect PASS**

Run: `dotnet test tests/Shareme.Api.Tests`
Expected: `3 passed`.

If `System_info` fails because the connection string is null at runtime, the override from `UseSetting` did not reach the app configuration: change `PostgresWebApplicationFactory.ConfigureWebHost` (in Coworkee.Testing) to also call `builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection([new($"ConnectionStrings:{connectionStringName}", postgres.ConnectionString)]))`, repack Coworkee (`pwsh ../coworkee/build/pack-local.ps1`), rerun.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(api): shareme api host with system info, health and openapi"
```

---

### Task 15: Aspire-Smoke-Test, README

**Files:**
- Create: `shareme/tests/Shareme.AppHost.Tests/Shareme.AppHost.Tests.csproj`, `AppHostTests.cs`
- Create: `shareme/README.md`

**Interfaces:**
- Consumes: AppHost (Task 14) mit Ressource `shareme-api`
- Produces: End-to-End-Nachweis, dass die komplette Aspire-Topologie startet.

- [ ] **Step 1: Failing test**

`tests/Shareme.AppHost.Tests/Shareme.AppHost.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Shareme.AppHost\Shareme.AppHost.csproj" />
    <PackageReference Include="Aspire.Hosting.Testing" />
  </ItemGroup>
</Project>
```

`tests/Shareme.AppHost.Tests/AppHostTests.cs`:
```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace Shareme.AppHost.Tests;

public sealed class AppHostTests
{
    [Fact]
    public async Task Api_starts_after_migrations_and_serves_system_info()
    {
        var ct = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Shareme_AppHost>(ct);
        await using var app = await appHost.BuildAsync(ct);
        await app.StartAsync(ct);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("shareme-api", ct).WaitAsync(TimeSpan.FromMinutes(5), ct);
        using var client = app.CreateHttpClient("shareme-api");
        var info = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/info", ct);

        info.GetProperty("product").GetString().ShouldBe("Shareme");
    }
}
```

- [ ] **Step 2: Run, expect FAIL then PASS**

```bash
dotnet sln Shareme.slnx add tests/Shareme.AppHost.Tests/Shareme.AppHost.Tests.csproj
dotnet test tests/Shareme.AppHost.Tests
```
Expected: `1 passed` (Docker muss laufen; erster Lauf zieht Postgres/Redis/Mailpit-Images, bis zu einige Minuten). If it fails, read the resource logs printed by the test output; typical causes: migrations project exits non-zero (check connection string name `shareme`), api health check unreachable (check `MapDefaultEndpoints`).

- [ ] **Step 3: README**

`README.md` (Shareme-Repo-Root; `docs/` bleibt unverändert):
```markdown
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
`dotnet test Shareme.slnx`

## Dokumentation
- Architektur: `docs/superpowers/specs/`
- Sharedien-Analyse: `docs/analysis/`
```

- [ ] **Step 4: Gesamtlauf**

Run: `cd /c/dev/privat/github/coworkee && dotnet test Coworkee.slnx && pwsh build/pack-local.ps1 && cd ../shareme && dotnet test Shareme.slnx`
Expected: alle Tests beider Repos grün.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "test: aspire smoke test and readme"
```

---

## Bewusste Vereinfachungen gegenüber der Spec (P0.1)

- `Entity` nicht generisch (`Guid`-Id fest) – generischer Schlüssel erst bei echtem Bedarf.
- `IClock` entfällt zugunsten von `TimeProvider` (stdlib); Tests nutzen `FakeTimeProvider`.
- `Coworkee.Contracts`, `QueryOptions`, `PagedResult`, `MapCommand/MapQuery`-Helfer und `SeedAsync` kommen in P0.2, wenn der erste Endpunkt sie braucht.
- Correlation-Id = W3C-Trace-Id (`Activity.Current`), keine eigene Middleware.
- Concurrency-Token nur für Postgres (`xmin`); SQL-Server-Variante, wenn der Provider getestet wird.
- Outbox pollt jede Sekunde (`ponytail:`-Kommentar).
