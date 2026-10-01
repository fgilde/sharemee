# P0.2a Identity & Rechte (Backend) – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax. Code blocks are preceded by `File: <repo>/<path>`; `extract-plan.py` writes all blocks of a step to disk.

**Goal:** Coworkee bekommt ein Identity-Modul mit Mandanten, Usern, Gruppen, Rollen, globalen und ressourcenbezogenen Berechtigungen, Permission-Contributors, Autorisierung im Dispatcher und einen Setup-Flow mit Gate; Shareme nutzt es über die API.

**Architecture:** Neue Pakete `Coworkee.Contracts` (DTOs, Permission-Namen, Paging) und `Coworkee.Identity` (Entities, EF-Mapping, Handler, Endpunkte). ASP.NET Core Identity liefert nur `UserManager` (Passwort, Lockout); Rollen und Gruppen verwaltet Coworkee selbst, damit Rollen pro Mandant möglich sind. Berechtigungen werden serverseitig aufgelöst und per `HybridCache` gecacht.

**Tech Stack:** wie P0.1, plus Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.12, Microsoft.Extensions.Caching.Hybrid 10.10.0, Microsoft.AspNetCore.Authentication.JwtBearer 10.0.12.

**Spec:** `docs/superpowers/specs/2026-10-01-p0-foundation-design.md` Abschnitt 3 (3.1–3.3, 3.7); Review-Hinweis I6 aus P0.1 (Tenant vor Auth).

## Global Constraints

- Alle Constraints aus P0.1 gelten weiter (net10.0, TWAE in src, CPM, keine Kommentare außer `ponytail:`, Guid v7, Schema `cw`, Commits ohne Attribution).
- Identity-Tabellen (`Users`, `Roles`, …) sind **nicht** über `IMultiTenant` gefiltert; Login und Setup laufen ohne Mandant. Handler filtern explizit über `User.TenantId`/`Role.TenantId`.
- E-Mail/Username sind global eindeutig.
- System-Rolle `admin` (`IsSystem`, `TenantId = null`) hat implizit alle Permissions; System-Rolle `user` hat keine.
- Permission-Namen: `<Modul>.<Objekt>.<View|Manage>`; `Manage` impliziert `View`.
- Testaufruf: `dotnet test --project <dir>` / `dotnet test --solution <slnx>`.

## Review Focus

1. Letzter Admin eines Mandanten verliert die Admin-Rolle → 409 statt Aussperrung. Test in Task 7.
2. Grant entzogen → wirkt sofort (Cache-Invalidierung), nicht erst nach Ablauf. Test in Task 7.
3. Setup ein zweites Mal / mit falschem Token → 409 / 403, niemals zweiter Admin. Test in Task 6.
4. User eines anderen Mandanten → unsichtbar in Listen und nicht zuweisbar. Test in Task 7.
5. Passwort-Hash und Security-Stamp tauchen nie im Audit-Log auf. Test in Task 7.

---

### Task 1: Fehlerarten 401/403, JSON-Enums, Authentifizierung im Host

**Files:** Modify `coworkee/src/Coworkee.Core/Results/Error.cs`, `coworkee/src/Coworkee.AspNetCore/Http/ResultHttpExtensions.cs`, `coworkee/src/Coworkee.AspNetCore/Http/CoworkeeExceptionHandler.cs`, `coworkee/src/Coworkee.AspNetCore/CoworkeeWebApplicationExtensions.cs`; Create `coworkee/src/Coworkee.Core/ForbiddenException.cs`; Test `coworkee/tests/Coworkee.AspNetCore.Tests/HttpPipelineTests.cs`

**Interfaces:**
- Produces: `ErrorKind.Unauthorized`, `Error.Unauthorized(code, message)`, `ForbiddenException(string)`; HTTP: Unauthorized→401, Forbidden→403; `ForbiddenException`→403, `UnauthorizedAccessException`→401; Enums als Strings in JSON; `AddCoworkee` registriert `AddAuthentication()` + `AddAuthorization()`, `UseCoworkee` ruft `UseAuthentication()`/`UseAuthorization()` vor den Modulen.

- [ ] **Step 1: Failing tests** – in `HttpPipelineTests` Theory erweitern und zwei Endpunkte ergänzen.

In `Failures_return_problem_with_code` zusätzliche InlineData:
```csharp
    [InlineData("/unauthorized", HttpStatusCode.Unauthorized, "auth.required")]
    [InlineData("/forbidden", HttpStatusCode.Forbidden, "auth.forbidden")]
```
Neue Facts:
```csharp
    [Fact]
    public async Task Forbidden_exception_returns_403() =>
        (await _client.GetAsync("/throw-forbidden", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Enums_are_serialized_as_strings() =>
        (await _client.GetStringAsync("/enum", TestContext.Current.CancellationToken)).ShouldBe("\"Conflict\"");
```
In `TestWebModule.ConfigureApplication`:
```csharp
            app.MapGet("/unauthorized", () => Task.FromResult(Result.Failure(Error.Unauthorized("auth.required", "Login required"))).ToHttpResult());
            app.MapGet("/forbidden", () => Task.FromResult(Result.Failure(Error.Forbidden("auth.forbidden", "Nope"))).ToHttpResult());
            app.MapGet("/throw-forbidden", IResult () => throw new ForbiddenException("nope"));
            app.MapGet("/enum", () => Task.FromResult<Result<ErrorKind>>(ErrorKind.Conflict).ToHttpResult());
```

- [ ] **Step 2: Run, expect FAIL** – `dotnet test --project tests/Coworkee.AspNetCore.Tests` → Build error `Error.Unauthorized` fehlt.

- [ ] **Step 3: Implementation**

File: coworkee/src/Coworkee.Core/Results/Error.cs
```csharp
namespace Coworkee.Core.Results;

public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Unexpected,
}

public sealed record Error(string Code, string Message, ErrorKind Kind, IReadOnlyDictionary<string, string[]>? Details = null)
{
    public static Error Validation(IReadOnlyDictionary<string, string[]> details) =>
        new("validation", "One or more validation errors occurred.", ErrorKind.Validation, details);

    public static Error Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Unexpected(string code, string message) => new(code, message, ErrorKind.Unexpected);
}
```

File: coworkee/src/Coworkee.Core/ForbiddenException.cs
```csharp
namespace Coworkee.Core;

public sealed class ForbiddenException(string message) : Exception(message);
```

File: coworkee/src/Coworkee.AspNetCore/Http/ResultHttpExtensions.cs
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
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };
}
```

File: coworkee/src/Coworkee.AspNetCore/Http/CoworkeeExceptionHandler.cs
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
            ForbiddenException forbidden => Error.Forbidden("auth.forbidden", forbidden.Message),
            UnauthorizedAccessException unauthorized => Error.Unauthorized("auth.required", unauthorized.Message),
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

File: coworkee/src/Coworkee.AspNetCore/CoworkeeWebApplicationExtensions.cs
```csharp
using System.Text.Json.Serialization;
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
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddCoworkeeModules<TRoot>(builder.Configuration);
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<CoworkeeExceptionHandler>();
        builder.Services.AddOpenApi("v1");
        return builder;
    }

    public static WebApplication UseCoworkee(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
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

- [ ] **Step 4: Run, expect PASS** – `dotnet test --solution Coworkee.slnx` → alle grün (AspNetCore 14).
- [ ] **Step 5: Commit** – `git commit -am "feat: unauthorized/forbidden errors, string enums, authentication pipeline"`

---

### Task 2: Permission-Definitionen und Autorisierungs-Middleware

**Files:** Create `coworkee/src/Coworkee.Application/Authorization/*.cs`, `coworkee/src/Coworkee.Application/Paging/PageRequestValidator.cs`; Modify `coworkee/src/Coworkee.Application/CoworkeeApplicationModule.cs`, `coworkee/src/Coworkee.Application/Coworkee.Application.csproj`; Test `coworkee/tests/Coworkee.Application.Tests/AuthorizationTests.cs`

**Interfaces:**
- Consumes: Dispatcher, `MiddlewareOrder.Authorization`, `ResultFactory`, `ICurrentUser`, `ForbiddenException`
- Produces:
  - `[RequiresPermission("…")]` (Klasse, mehrfach), `interface IResourceRequest { string ResourceType; Guid ResourceId; string RequiredPermission; }`
  - `interface IPermissionChecker { Task<IReadOnlyCollection<string>> GetGrantedAsync(ct); Task<bool> IsGrantedAsync(string, ct); Task<bool> IsGrantedAsync(string, string resourceType, Guid resourceId, ct); }`
  - `record PermissionDefinition(string Name, string Group, string DisplayName, IReadOnlyList<string> Implies)`, `interface IPermissionDefinitionContributor { void Define(PermissionDefinitionContext) }`, `PermissionDefinitionContext.Group(name, displayName).Add(name, displayName, params implies)`
  - `interface IPermissionDefinitionManager { IReadOnlyList<PermissionDefinition> All; bool Exists(string); IReadOnlySet<string> Expand(IEnumerable<string>) }`
  - Anonym + Anforderung → `Error.Unauthorized("auth.required")` bzw. `UnauthorizedAccessException`; fehlende Permission → `Error.Forbidden("auth.forbidden")` bzw. `ForbiddenException`
  - `PageRequestValidator : AbstractValidator<PageRequest>` (Page ≥ 1, PageSize 1–200)

- [ ] **Step 1: Failing tests**

File: coworkee/tests/Coworkee.Application.Tests/AuthorizationTests.cs
```csharp
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Application.Tests;

public sealed class AuthorizationTests
{
    private readonly IPermissionChecker _checker = Substitute.For<IPermissionChecker>();
    private readonly FakeUser _user = new();

    [Fact]
    public async Task Requests_without_requirements_pass_anonymously()
    {
        _user.UserId = null;

        (await Dispatcher().SendAsync(new Open(), Ct)).ShouldBe("open");
    }

    [Fact]
    public async Task Anonymous_user_is_unauthorized()
    {
        _user.UserId = null;

        var result = await Dispatcher().SendAsync(new Guarded(), Ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Missing_permission_is_forbidden()
    {
        var result = await Dispatcher().SendAsync(new Guarded(), Ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Missing_permission_on_plain_request_throws()
    {
        await Should.ThrowAsync<ForbiddenException>(() => Dispatcher().SendAsync(new GuardedPlain(), Ct));
    }

    [Fact]
    public async Task Granted_permission_runs_the_handler()
    {
        _checker.IsGrantedAsync("Demo.Read", Arg.Any<CancellationToken>()).Returns(true);

        (await Dispatcher().SendAsync(new Guarded(), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Resource_requests_are_checked_against_the_resource()
    {
        var id = Guid.CreateVersion7();
        _checker.IsGrantedAsync("Folder.Edit", "Folder", id, Arg.Any<CancellationToken>()).Returns(true);

        (await Dispatcher().SendAsync(new OnResource(id), Ct)).IsSuccess.ShouldBeTrue();
        (await Dispatcher().SendAsync(new OnResource(Guid.CreateVersion7()), Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public void Definitions_expand_implications_transitively()
    {
        var manager = Manager(ctx => ctx.Group("Demo", "Demo")
            .Add("Demo.Read", "Read")
            .Add("Demo.Write", "Write", "Demo.Read")
            .Add("Demo.Admin", "Admin", "Demo.Write"));

        manager.Expand(["Demo.Admin"]).ShouldBe(["Demo.Admin", "Demo.Write", "Demo.Read"], ignoreOrder: true);
        manager.Exists("Demo.Write").ShouldBeTrue();
        manager.Exists("Demo.Unknown").ShouldBeFalse();
    }

    [Fact]
    public void Duplicate_definitions_are_rejected() =>
        Should.Throw<InvalidOperationException>(() => Manager(ctx => ctx.Group("A", "A").Add("X", "X").Add("X", "X")).All);

    [Fact]
    public void Unknown_implications_are_rejected() =>
        Should.Throw<InvalidOperationException>(() => Manager(ctx => ctx.Group("A", "A").Add("X", "X", "Missing")).All);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IDispatcher Dispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(_user);
        services.AddSingleton(_checker);
        services.AddCoworkeeModules<TestModule>(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();
    }

    private static IPermissionDefinitionManager Manager(Action<PermissionDefinitionContext> define)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPermissionDefinitionContributor>(new InlineContributor(define));
        services.AddCoworkeeModules<CoworkeeApplicationModule>(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetRequiredService<IPermissionDefinitionManager>();
    }

    private sealed class InlineContributor(Action<PermissionDefinitionContext> define) : IPermissionDefinitionContributor
    {
        public void Define(PermissionDefinitionContext context) => define(context);
    }

    private sealed class FakeUser : ICurrentUser
    {
        public Guid? UserId { get; set; } = Guid.CreateVersion7();

        public Guid? TenantId { get; set; } = Guid.CreateVersion7();

        public bool IsAuthenticated => UserId is not null;

        public IReadOnlyCollection<string> Roles => [];
    }

    [DependsOn(typeof(CoworkeeApplicationModule))]
    private sealed class TestModule : CoworkeeModule
    {
        public override void ConfigureServices(ModuleServiceContext context) =>
            context.Services.AddMessagingFromAssembly(typeof(AuthorizationTests).Assembly);
    }

    public sealed record Open : IQuery<string>;

    [RequiresPermission("Demo.Read")]
    public sealed record Guarded : IQuery<Result>;

    [RequiresPermission("Demo.Read")]
    public sealed record GuardedPlain : IQuery<string>;

    public sealed record OnResource(Guid ResourceId) : ICommand<Result>, IResourceRequest
    {
        public string ResourceType => "Folder";

        public string RequiredPermission => "Folder.Edit";
    }

    internal sealed class OpenHandler : IHandler<Open, string>
    {
        public Task<string> HandleAsync(Open request, CancellationToken cancellationToken) => Task.FromResult("open");
    }

    internal sealed class GuardedHandler : IHandler<Guarded, Result>
    {
        public Task<Result> HandleAsync(Guarded request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    internal sealed class GuardedPlainHandler : IHandler<GuardedPlain, string>
    {
        public Task<string> HandleAsync(GuardedPlain request, CancellationToken cancellationToken) => Task.FromResult("x");
    }

    internal sealed class OnResourceHandler : IHandler<OnResource, Result>
    {
        public Task<Result> HandleAsync(OnResource request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }
}
```

- [ ] **Step 2: Run, expect FAIL** – `dotnet test --project tests/Coworkee.Application.Tests` → Namespace `Coworkee.Application.Authorization` fehlt.

- [ ] **Step 3: Implementation**

File: coworkee/src/Coworkee.Application/Authorization/RequiresPermissionAttribute.cs
```csharp
namespace Coworkee.Application.Authorization;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

public interface IResourceRequest
{
    string ResourceType { get; }

    Guid ResourceId { get; }

    string RequiredPermission { get; }
}

public interface IPermissionChecker
{
    Task<IReadOnlyCollection<string>> GetGrantedAsync(CancellationToken cancellationToken);

    Task<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken);

    Task<bool> IsGrantedAsync(string permission, string resourceType, Guid resourceId, CancellationToken cancellationToken);
}
```

File: coworkee/src/Coworkee.Application/Authorization/PermissionDefinitions.cs
```csharp
namespace Coworkee.Application.Authorization;

public sealed record PermissionDefinition(string Name, string Group, string DisplayName, IReadOnlyList<string> Implies);

public interface IPermissionDefinitionContributor
{
    void Define(PermissionDefinitionContext context);
}

public sealed class PermissionDefinitionContext
{
    internal List<(string Name, string DisplayName)> Groups { get; } = [];

    internal List<PermissionDefinition> Definitions { get; } = [];

    public PermissionGroupBuilder Group(string name, string displayName)
    {
        Groups.Add((name, displayName));
        return new PermissionGroupBuilder(this, name);
    }
}

public sealed class PermissionGroupBuilder
{
    private readonly PermissionDefinitionContext _context;
    private readonly string _group;

    internal PermissionGroupBuilder(PermissionDefinitionContext context, string group)
    {
        _context = context;
        _group = group;
    }

    public PermissionGroupBuilder Add(string name, string displayName, params string[] implies)
    {
        _context.Definitions.Add(new PermissionDefinition(name, _group, displayName, implies));
        return this;
    }
}

public interface IPermissionDefinitionManager
{
    IReadOnlyList<PermissionDefinition> All { get; }

    IReadOnlyList<(string Name, string DisplayName)> Groups { get; }

    bool Exists(string name);

    IReadOnlySet<string> Expand(IEnumerable<string> granted);
}
```

File: coworkee/src/Coworkee.Application/Authorization/PermissionDefinitionManager.cs
```csharp
namespace Coworkee.Application.Authorization;

internal sealed class PermissionDefinitionManager(IEnumerable<IPermissionDefinitionContributor> contributors) : IPermissionDefinitionManager
{
    private readonly Lazy<(IReadOnlyList<PermissionDefinition> All, IReadOnlyList<(string, string)> Groups, Dictionary<string, PermissionDefinition> ByName)> _model =
        new(() => Build(contributors));

    public IReadOnlyList<PermissionDefinition> All => _model.Value.All;

    public IReadOnlyList<(string Name, string DisplayName)> Groups => _model.Value.Groups;

    public bool Exists(string name) => _model.Value.ByName.ContainsKey(name);

    public IReadOnlySet<string> Expand(IEnumerable<string> granted)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(granted.Where(Exists));
        while (pending.TryPop(out var name))
        {
            if (result.Add(name))
            {
                foreach (var implied in _model.Value.ByName[name].Implies)
                {
                    pending.Push(implied);
                }
            }
        }

        return result;
    }

    private static (IReadOnlyList<PermissionDefinition>, IReadOnlyList<(string, string)>, Dictionary<string, PermissionDefinition>) Build(
        IEnumerable<IPermissionDefinitionContributor> contributors)
    {
        var context = new PermissionDefinitionContext();
        foreach (var contributor in contributors)
        {
            contributor.Define(context);
        }

        var byName = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        foreach (var definition in context.Definitions)
        {
            if (!byName.TryAdd(definition.Name, definition))
            {
                throw new InvalidOperationException($"Permission '{definition.Name}' is defined twice.");
            }
        }

        var unknown = context.Definitions.SelectMany(d => d.Implies.Where(i => !byName.ContainsKey(i)).Select(i => $"{d.Name} -> {i}")).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException($"Unknown implied permissions: {string.Join(", ", unknown)}");
        }

        return (context.Definitions, context.Groups, byName);
    }
}
```

File: coworkee/src/Coworkee.Application/Authorization/AuthorizationMiddleware.cs
```csharp
using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.Core;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Authorization;

internal sealed class AuthorizationMiddleware(ICurrentUser currentUser, IServiceProvider services) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Authorization;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        var required = Requirements<TRequest>.Permissions;
        var resource = request as IResourceRequest;
        if (required.Length == 0 && resource is null)
        {
            return await next();
        }

        if (!currentUser.IsAuthenticated)
        {
            return Fail<TResult>(Error.Unauthorized("auth.required", "Authentication is required."), () => new UnauthorizedAccessException("Authentication is required."));
        }

        var checker = services.GetRequiredService<IPermissionChecker>();
        foreach (var permission in required)
        {
            if (!await checker.IsGrantedAsync(permission, cancellationToken))
            {
                return Denied<TResult>(permission);
            }
        }

        if (resource is not null
            && !await checker.IsGrantedAsync(resource.RequiredPermission, resource.ResourceType, resource.ResourceId, cancellationToken))
        {
            return Denied<TResult>(resource.RequiredPermission);
        }

        return await next();
    }

    private static TResult Denied<TResult>(string permission) =>
        Fail<TResult>(Error.Forbidden("auth.forbidden", $"Permission '{permission}' is required."), () => new ForbiddenException($"Permission '{permission}' is required."));

    private static TResult Fail<TResult>(Error error, Func<Exception> exception) =>
        ResultFactory.TryCreateFailure<TResult>(error, out var failed) ? failed : throw exception();

    private static class Requirements<TRequest>
    {
        public static readonly string[] Permissions =
            typeof(TRequest).GetCustomAttributes<RequiresPermissionAttribute>().Select(a => a.Permission).ToArray();
    }
}
```

File: coworkee/src/Coworkee.Application/Paging/PageRequestValidator.cs
```csharp
using Coworkee.Contracts;
using FluentValidation;

namespace Coworkee.Application.Paging;

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public PageRequestValidator()
    {
        RuleFor(p => p.Page).GreaterThanOrEqualTo(1);
        RuleFor(p => p.PageSize).InclusiveBetween(1, 200);
    }
}
```

Coworkee.Contracts is needed by `PageRequestValidator` — create the project now (it is filled in Task 4):

File: coworkee/src/Coworkee.Contracts/Coworkee.Contracts.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

File: coworkee/src/Coworkee.Contracts/Paging.cs
```csharp
namespace Coworkee.Contracts;

public sealed record PageRequest(int Page = 1, int PageSize = 25, string? Search = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
```

File: coworkee/src/Coworkee.Application/Coworkee.Application.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Coworkee.Core\Coworkee.Core.csproj" />
    <ProjectReference Include="..\Coworkee.Domain\Coworkee.Domain.csproj" />
    <ProjectReference Include="..\Coworkee.Contracts\Coworkee.Contracts.csproj" />
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```

File: coworkee/src/Coworkee.Application/CoworkeeApplicationModule.cs
```csharp
using Coworkee.Application.Authorization;
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
        services.AddScoped<IRequestMiddleware, AuthorizationMiddleware>();
        services.AddScoped<IRequestMiddleware, ValidationMiddleware>();
        services.AddScoped<IRequestMiddleware, UnitOfWorkMiddleware>();
        services.TryAddSingleton<IPermissionDefinitionManager, PermissionDefinitionManager>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();
    }
}
```

`dotnet sln Coworkee.slnx add src/Coworkee.Contracts/Coworkee.Contracts.csproj`

- [ ] **Step 4: Run, expect PASS** – `dotnet test --solution Coworkee.slnx` (Application +9).
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat(application): permission definitions and authorization middleware"`

---

### Task 3: Audited-Basisklassen und Audit-Annotationen

**Files:** Create `coworkee/src/Coworkee.Domain/AuditedEntities.cs`, `coworkee/src/Coworkee.Infrastructure/Auditing/AuditPropertyBuilderExtensions.cs`; Modify `coworkee/src/Coworkee.Infrastructure/Persistence/Interceptors/AuditTrailInterceptor.cs`; Test `coworkee/tests/Coworkee.Infrastructure.Tests/AuditTrailTests.cs`, `TestModel.cs`

**Interfaces:**
- Produces: `abstract class AuditedEntity : Entity, IAuditable`, `abstract class AuditedAggregateRoot : AggregateRoot, IAuditable`; `PropertyBuilder.IsSensitive()`, `PropertyBuilder.IsNotAudited()` (EF-Annotationen `Coworkee:Sensitive`, `Coworkee:NotAudited`), vom Audit-Trail wie die Attribute behandelt.

- [ ] **Step 1: Failing test** – `TestModel.cs`: `Document` bekommt `public string? Note { get; set; }` und `public string? Internal { get; set; }`; `TestModelContributor.Apply`:
```csharp
    public void Apply(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<Document>();
        document.OwnsOne(d => d.Settings, s => s.ToJson());
        document.Property(d => d.Note).IsSensitive();
        document.Property(d => d.Internal).IsNotAudited();
    }
```
(plus `using Coworkee.Infrastructure.Auditing;`). Neuer Test in `AuditTrailTests`:
```csharp
    [Fact]
    public async Task Fluent_annotations_mask_and_skip_properties()
    {
        var document = new Document { Title = "T", Note = "secret", Internal = "skip" };
        await ExecuteAsync(db =>
        {
            db.Documents.Add(document);
            return Task.CompletedTask;
        });

        var changes = (await EntriesAsync(document.Id)).Single().Changes;
        changes.Single(c => c.Property == nameof(Document.Note)).NewValue.ShouldBe("\"***\"");
        changes.ShouldNotContain(c => c.Property == nameof(Document.Internal));
    }
```

- [ ] **Step 2: Run, expect FAIL** – `IsSensitive` fehlt.

- [ ] **Step 3: Implementation**

File: coworkee/src/Coworkee.Domain/AuditedEntities.cs
```csharp
namespace Coworkee.Domain;

public abstract class AuditedEntity : Entity, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}

public abstract class AuditedAggregateRoot : AggregateRoot, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
```

File: coworkee/src/Coworkee.Infrastructure/Auditing/AuditPropertyBuilderExtensions.cs
```csharp
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coworkee.Infrastructure.Auditing;

public static class AuditPropertyBuilderExtensions
{
    internal const string Sensitive = "Coworkee:Sensitive";
    internal const string NotAudited = "Coworkee:NotAudited";

    public static PropertyBuilder<T> IsSensitive<T>(this PropertyBuilder<T> builder) => builder.HasAnnotation(Sensitive, true);

    public static PropertyBuilder<T> IsNotAudited<T>(this PropertyBuilder<T> builder) => builder.HasAnnotation(NotAudited, true);

    internal static bool HasFlag(this IReadOnlyProperty property, string annotation) => property.FindAnnotation(annotation)?.Value is true;
}
```

Modify `AuditTrailInterceptor.cs`:
- In `Create`, replace `.Where(p => p.Metadata.PropertyInfo?.GetCustomAttribute<NotAuditedAttribute>() is null)` by
```csharp
            .Where(p => p.Metadata.PropertyInfo?.GetCustomAttribute<NotAuditedAttribute>() is null && !p.Metadata.HasFlag(AuditPropertyBuilderExtensions.NotAudited))
```
- Replace the body of `Format` by
```csharp
        if (value is null)
        {
            return null;
        }

        return IsSensitive(property.Metadata) ? Masked : JsonSerializer.Serialize(value);
```
- Add
```csharp
    private static bool IsSensitive(IReadOnlyProperty property) =>
        property.PropertyInfo?.GetCustomAttribute<SensitiveAttribute>() is not null || property.HasFlag(AuditPropertyBuilderExtensions.Sensitive);
```
- In `Snapshot`, replace the masking condition `p.Metadata.PropertyInfo?.GetCustomAttribute<SensitiveAttribute>() is not null` by `IsSensitive(p.Metadata)` and filter `!p.Metadata.HasFlag(AuditPropertyBuilderExtensions.NotAudited)`.
- Add `using Microsoft.EntityFrameworkCore.Metadata;`.

- [ ] **Step 4: Run, expect PASS** – `dotnet test --solution Coworkee.slnx`.
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat: audited base classes and fluent audit annotations"`

---

### Task 4: Coworkee.Contracts – Identity-Verträge

**Files:** Create `coworkee/src/Coworkee.Contracts/Identity/*.cs`

**Interfaces:** Produces (alle `public sealed record`, Namespace `Coworkee.Contracts.Identity`):
- `IdentityPermissions` (Konstanten), `PermissionProviderType { Role, User, Group }`, `PrincipalType { User, Group }`
- `UserDto(Guid Id, string UserName, string Email, string? FirstName, string? LastName, bool IsActive, IReadOnlyList<RoleRefDto> Roles)`, `RoleRefDto(Guid Id, string Name)`, `CreateUserRequest(string Email, string Password, string? FirstName, string? LastName)`, `UpdateUserRequest(string? FirstName, string? LastName, bool IsActive)`, `IdListRequest(IReadOnlyList<Guid> Ids)`
- `RoleDto(Guid Id, string Name, string? Description, bool IsSystem)`, `RoleRequest(string Name, string? Description)`
- `GroupDto(Guid Id, string Name, string? Description, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> RoleIds)`, `GroupRequest(string Name, string? Description)`
- `PermissionGroupDto(string Name, string DisplayName, IReadOnlyList<PermissionDto> Permissions)`, `PermissionDto(string Name, string DisplayName, IReadOnlyList<string> Implies)`, `NameListRequest(IReadOnlyList<string> Names)`
- `ResourcePermissionDto(Guid Id, PrincipalType PrincipalType, Guid PrincipalId, Guid RoleId)`, `GrantResourcePermissionRequest(PrincipalType PrincipalType, Guid PrincipalId, Guid RoleId)`
- `SetupStatusDto(bool IsInitialized)`, `CompleteSetupRequest(string SetupToken, string TenantName, string AdminEmail, string AdminPassword, string? AdminFirstName, string? AdminLastName)`, `SetupResultDto(Guid TenantId, Guid AdminUserId)`

- [ ] **Step 1: Write files**

File: coworkee/src/Coworkee.Contracts/Identity/IdentityPermissions.cs
```csharp
namespace Coworkee.Contracts.Identity;

public static class IdentityPermissions
{
    public const string GroupName = "Identity";

    public static class Users
    {
        public const string View = "Identity.Users.View";
        public const string Manage = "Identity.Users.Manage";
    }

    public static class Groups
    {
        public const string View = "Identity.Groups.View";
        public const string Manage = "Identity.Groups.Manage";
    }

    public static class Roles
    {
        public const string View = "Identity.Roles.View";
        public const string Manage = "Identity.Roles.Manage";
    }

    public static class Permissions
    {
        public const string Manage = "Identity.Permissions.Manage";
    }

    public static class ResourcePermissions
    {
        public const string Manage = "Identity.ResourcePermissions.Manage";
    }
}

public enum PermissionProviderType
{
    Role,
    User,
    Group,
}

public enum PrincipalType
{
    User,
    Group,
}
```

File: coworkee/src/Coworkee.Contracts/Identity/IdentityDtos.cs
```csharp
namespace Coworkee.Contracts.Identity;

public sealed record RoleRefDto(Guid Id, string Name);

public sealed record UserDto(Guid Id, string UserName, string Email, string? FirstName, string? LastName, bool IsActive, IReadOnlyList<RoleRefDto> Roles);

public sealed record CreateUserRequest(string Email, string Password, string? FirstName, string? LastName);

public sealed record UpdateUserRequest(string? FirstName, string? LastName, bool IsActive);

public sealed record IdListRequest(IReadOnlyList<Guid> Ids);

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem);

public sealed record RoleRequest(string Name, string? Description);

public sealed record GroupDto(Guid Id, string Name, string? Description, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> RoleIds);

public sealed record GroupRequest(string Name, string? Description);

public sealed record PermissionDto(string Name, string DisplayName, IReadOnlyList<string> Implies);

public sealed record PermissionGroupDto(string Name, string DisplayName, IReadOnlyList<PermissionDto> Permissions);

public sealed record NameListRequest(IReadOnlyList<string> Names);

public sealed record ResourcePermissionDto(Guid Id, PrincipalType PrincipalType, Guid PrincipalId, Guid RoleId);

public sealed record GrantResourcePermissionRequest(PrincipalType PrincipalType, Guid PrincipalId, Guid RoleId);

public sealed record SetupStatusDto(bool IsInitialized);

public sealed record CompleteSetupRequest(string SetupToken, string TenantName, string AdminEmail, string AdminPassword, string? AdminFirstName, string? AdminLastName);

public sealed record SetupResultDto(Guid TenantId, Guid AdminUserId);
```

- [ ] **Step 2: Build** – `dotnet build Coworkee.slnx` → 0 Warnungen. (Reine Verträge, Verhalten wird in Task 6–8 getestet.)
- [ ] **Step 3: Commit** – `git add -A && git commit -m "feat(contracts): identity contracts"`

---

### Task 5: Test-Authentifizierung in Coworkee.Testing

**Files:** Create `coworkee/src/Coworkee.Testing/TestAuthentication.cs`; Modify `coworkee/src/Coworkee.Testing/Coworkee.Testing.csproj`

**Interfaces:** Produces `TestAuthHandler.Scheme = "Test"`; Header `Authorization: Test <userId>;<tenantId>` → Claims `sub`, `tenant`; `IServiceCollection.AddTestAuthentication()` setzt Test als Default-Schema; `HttpClient.AsUser(Guid userId, Guid? tenantId)`.

- [ ] **Step 1: Write files**

File: coworkee/src/Coworkee.Testing/Coworkee.Testing.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\Coworkee.Core\Coworkee.Core.csproj" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Respawn" />
    <PackageReference Include="Npgsql" />
    <PackageReference Include="xunit.v3.extensibility.core" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  </ItemGroup>
</Project>
```

File: coworkee/src/Coworkee.Testing/TestAuthentication.cs
```csharp
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Testing;

public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Scheme + " ", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = header[(Scheme.Length + 1)..].Split(';');
        var claims = new List<Claim> { new("sub", parts[0]) };
        if (parts.Length > 1 && parts[1].Length > 0)
        {
            claims.Add(new Claim("tenant", parts[1]));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}

public static class TestAuthenticationExtensions
{
    public static IServiceCollection AddTestAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.Scheme;
                options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthHandler.Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        return services;
    }

    public static HttpClient AsUser(this HttpClient client, Guid userId, Guid? tenantId)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthHandler.Scheme, $"{userId};{tenantId}");
        return client;
    }
}
```

- [ ] **Step 2: Build** – `dotnet build Coworkee.slnx` (Verhalten wird in Task 6 genutzt und getestet).
- [ ] **Step 3: Commit** – `git add -A && git commit -m "feat(testing): test authentication scheme"`

---

### Task 6: Coworkee.Identity – Entities, Mapping, Modul, Setup

**Files:** Create `coworkee/src/Coworkee.Identity/**`; Test `coworkee/tests/Coworkee.Identity.Tests/**`; Modify `coworkee/Directory.Packages.props`

**Interfaces:**
- Produces: Entities `Tenant`, `User : IdentityUser<Guid>`, `Role : IdentityRole<Guid>`, `UserGroup`, `UserGroupMember`, `UserGroupRole`, `PermissionGrant`, `ResourcePermission`, `SystemState`; `SystemRoles.Admin = "admin"`, `SystemRoles.User = "user"`; `CoworkeeIdentityModule : CoworkeeModule, IWebModule` (Abhängigkeiten: `CoworkeeApplicationModule`); `SetupGateOptions.AllowedPrefixes`; Endpunkte `GET /api/v1/setup/status`, `POST /api/v1/setup/complete`; Gate → 503 `setup_required` für `/api/*` bis zur Einrichtung.

- [ ] **Step 1: Failing tests**

File: coworkee/tests/Coworkee.Identity.Tests/Coworkee.Identity.Tests.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.Identity\Coworkee.Identity.csproj" />
    <ProjectReference Include="..\..\src\Coworkee.Testing\Coworkee.Testing.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" />
  </ItemGroup>
</Project>
```

File: coworkee/tests/Coworkee.Identity.Tests/IdentityApp.cs
```csharp
using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Identity.Tests.IdentityApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Identity.Tests;

public sealed class IdentityApp : PostgresFixture
{
    public const string SetupToken = "test-setup-token";

    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = SetupToken;
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.AddCoworkee<TestAppModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        App.UseCoworkee();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>().Database.EnsureCreatedAsync();
        }

        await App.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task ResetAllAsync()
    {
        await ResetAsync();
        App.Services.GetRequiredService<SystemStateCache>().Reset();
    }

    public HttpClient Anonymous() => App.GetTestClient();

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<SetupResultDto> SetupAsync()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest(SetupToken, "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public async Task<T> InDbAsync<T>(Guid? tenantId, Func<IdentityTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>();
        var result = await action(db);
        await db.SaveChangesAsync();
        return result;
    }
}

public sealed class IdentityTestDbContext(DbContextOptions<IdentityTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeIdentityModule))]
public sealed class TestAppModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeDbContext<IdentityTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
}
```

File: coworkee/tests/Coworkee.Identity.Tests/SetupTests.cs
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Tests;

public sealed class SetupTests(IdentityApp app) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await app.ResetAllAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Api_is_gated_until_setup()
    {
        var response = await app.As(Guid.CreateVersion7(), Guid.CreateVersion7()).GetAsync("/api/v1/identity/users", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("setup_required");
    }

    [Fact]
    public async Task Status_reports_state()
    {
        (await Status()).IsInitialized.ShouldBeFalse();
        await app.SetupAsync();
        (await Status()).IsInitialized.ShouldBeTrue();
    }

    [Fact]
    public async Task Wrong_token_is_forbidden()
    {
        var response = await Complete(new CompleteSetupRequest("wrong", "Acme", "admin@acme.test", "Admin#12345", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Weak_password_is_rejected()
    {
        var response = await Complete(new CompleteSetupRequest(IdentityApp.SetupToken, "Acme", "admin@acme.test", "short", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_from_setup_has_every_permission()
    {
        var setup = await app.SetupAsync();

        var permissions = await app.As(setup.AdminUserId, setup.TenantId).GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", Ct);

        permissions.ShouldContain(IdentityPermissions.Users.Manage);
        permissions.ShouldContain(IdentityPermissions.Permissions.Manage);
    }

    [Fact]
    public async Task Second_setup_conflicts()
    {
        await app.SetupAsync();

        var response = await Complete(new CompleteSetupRequest(IdentityApp.SetupToken, "Other", "other@acme.test", "Admin#12345", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<SetupStatusDto> Status() =>
        (await app.Anonymous().GetFromJsonAsync<SetupStatusDto>("/api/v1/setup/status", Ct))!;

    private Task<HttpResponseMessage> Complete(CompleteSetupRequest request) =>
        app.Anonymous().PostAsJsonAsync("/api/v1/setup/complete", request, Ct);
}
```

`dotnet sln Coworkee.slnx add tests/Coworkee.Identity.Tests/Coworkee.Identity.Tests.csproj`

- [ ] **Step 2: Run, expect FAIL** – Projekt `Coworkee.Identity` fehlt.

- [ ] **Step 3: Implementation**

Add to `coworkee/Directory.Packages.props`:
```xml
    <PackageVersion Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Caching.Hybrid" Version="10.10.0" />
```

File: coworkee/src/Coworkee.Identity/Coworkee.Identity.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\Coworkee.Contracts\Coworkee.Contracts.csproj" />
    <ProjectReference Include="..\Coworkee.Infrastructure\Coworkee.Infrastructure.csproj" />
    <ProjectReference Include="..\Coworkee.AspNetCore\Coworkee.AspNetCore.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.Extensions.Caching.Hybrid" />
  </ItemGroup>
</Project>
```

File: coworkee/src/Coworkee.Identity/Domain/IdentityEntities.cs
```csharp
using Coworkee.Contracts.Identity;
using Coworkee.Domain;
using Microsoft.AspNetCore.Identity;

namespace Coworkee.Identity.Domain;

public static class SystemRoles
{
    public const string Admin = "admin";
    public const string User = "user";
}

public sealed class Tenant : AuditedAggregateRoot
{
    public required string Name { get; set; }

    public required string Identifier { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; }

    public bool AcceptsRegistrations { get; set; }
}

public sealed class User : IdentityUser<Guid>, IAuditable
{
    public User()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public Guid TenantId { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}

public sealed class Role : IdentityRole<Guid>, IAuditable
{
    public Role() => Id = Guid.CreateVersion7();

    public Guid? TenantId { get; set; }

    public string? Description { get; set; }

    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}

public sealed class UserGroup : AuditedAggregateRoot, IMultiTenant
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public Guid TenantId { get; set; }

    public List<UserGroupMember> Members { get; } = [];

    public List<UserGroupRole> Roles { get; } = [];
}

public sealed class UserGroupMember
{
    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }
}

public sealed class UserGroupRole
{
    public Guid GroupId { get; set; }

    public Guid RoleId { get; set; }
}

public sealed class PermissionGrant : AuditedEntity
{
    public Guid? TenantId { get; set; }

    public required string Name { get; set; }

    public PermissionProviderType ProviderType { get; set; }

    public Guid ProviderKey { get; set; }
}

public sealed class ResourcePermission : AuditedEntity, IMultiTenant
{
    public required string ResourceType { get; set; }

    public Guid ResourceId { get; set; }

    public PrincipalType PrincipalType { get; set; }

    public Guid PrincipalId { get; set; }

    public Guid RoleId { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class SystemState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public bool IsInitialized { get; set; }

    public DateTimeOffset? InitializedAt { get; set; }
}
```

File: coworkee/src/Coworkee.Identity/Persistence/IdentityModelContributor.cs
```csharp
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Persistence;

internal sealed class IdentityModelContributor : IModelContributor
{
    private const string Schema = "cw";

    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.ToTable("Tenants", Schema);
            tenant.Property(t => t.Name).HasMaxLength(200);
            tenant.Property(t => t.Identifier).HasMaxLength(100);
            tenant.HasIndex(t => t.Identifier).IsUnique();
        });

        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users", Schema);
            user.HasKey(u => u.Id);
            user.HasIndex(u => u.NormalizedUserName).IsUnique();
            user.HasIndex(u => u.NormalizedEmail);
            user.HasIndex(u => u.TenantId);
            user.Property(u => u.UserName).HasMaxLength(256);
            user.Property(u => u.NormalizedUserName).HasMaxLength(256);
            user.Property(u => u.Email).HasMaxLength(256);
            user.Property(u => u.NormalizedEmail).HasMaxLength(256);
            user.Property(u => u.FirstName).HasMaxLength(100);
            user.Property(u => u.LastName).HasMaxLength(100);
            user.Property(u => u.PasswordHash).IsSensitive();
            user.Property(u => u.SecurityStamp).IsNotAudited();
            user.Property(u => u.ConcurrencyStamp).IsConcurrencyToken().IsNotAudited();
            user.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId);
            user.HasMany<IdentityUserClaim<Guid>>().WithOne().HasForeignKey(c => c.UserId).IsRequired();
            user.HasMany<IdentityUserLogin<Guid>>().WithOne().HasForeignKey(l => l.UserId).IsRequired();
            user.HasMany<IdentityUserToken<Guid>>().WithOne().HasForeignKey(t => t.UserId).IsRequired();
            user.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(r => r.UserId).IsRequired();
        });

        modelBuilder.Entity<Role>(role =>
        {
            role.ToTable("Roles", Schema);
            role.HasKey(r => r.Id);
            role.HasIndex(r => new { r.TenantId, r.NormalizedName }).IsUnique().AreNullsDistinct(false);
            role.Property(r => r.Name).HasMaxLength(256);
            role.Property(r => r.NormalizedName).HasMaxLength(256);
            role.Property(r => r.Description).HasMaxLength(1000);
            role.Property(r => r.ConcurrencyStamp).IsConcurrencyToken().IsNotAudited();
            role.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(r => r.RoleId).IsRequired();
            role.HasMany<IdentityRoleClaim<Guid>>().WithOne().HasForeignKey(c => c.RoleId).IsRequired();
        });

        modelBuilder.Entity<IdentityUserRole<Guid>>(b => { b.ToTable("UserRoles", Schema); b.HasKey(r => new { r.UserId, r.RoleId }); });
        modelBuilder.Entity<IdentityUserClaim<Guid>>(b => { b.ToTable("UserClaims", Schema); b.HasKey(c => c.Id); });
        modelBuilder.Entity<IdentityRoleClaim<Guid>>(b => { b.ToTable("RoleClaims", Schema); b.HasKey(c => c.Id); });
        modelBuilder.Entity<IdentityUserLogin<Guid>>(b =>
        {
            b.ToTable("UserLogins", Schema);
            b.HasKey(l => new { l.LoginProvider, l.ProviderKey });
            b.Property(l => l.LoginProvider).HasMaxLength(128);
            b.Property(l => l.ProviderKey).HasMaxLength(128);
        });
        modelBuilder.Entity<IdentityUserToken<Guid>>(b =>
        {
            b.ToTable("UserTokens", Schema);
            b.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
            b.Property(t => t.LoginProvider).HasMaxLength(128);
            b.Property(t => t.Name).HasMaxLength(128);
            b.Property(t => t.Value).IsSensitive();
        });

        modelBuilder.Entity<UserGroup>(group =>
        {
            group.ToTable("Groups", Schema);
            group.Property(g => g.Name).HasMaxLength(200);
            group.Property(g => g.Description).HasMaxLength(1000);
            group.HasIndex(g => new { g.TenantId, g.Name }).IsUnique();
            group.HasMany(g => g.Members).WithOne().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
            group.HasMany(g => g.Roles).WithOne().HasForeignKey(r => r.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserGroupMember>(member =>
        {
            member.ToTable("GroupMembers", Schema);
            member.HasKey(m => new { m.GroupId, m.UserId });
            member.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserGroupRole>(groupRole =>
        {
            groupRole.ToTable("GroupRoles", Schema);
            groupRole.HasKey(r => new { r.GroupId, r.RoleId });
            groupRole.HasOne<Role>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PermissionGrant>(grant =>
        {
            grant.ToTable("PermissionGrants", Schema);
            grant.Property(g => g.Name).HasMaxLength(200);
            grant.Property(g => g.ProviderType).HasConversion<string>().HasMaxLength(20);
            grant.HasIndex(g => new { g.TenantId, g.Name, g.ProviderType, g.ProviderKey }).IsUnique().AreNullsDistinct(false);
            grant.HasIndex(g => new { g.ProviderType, g.ProviderKey });
        });

        modelBuilder.Entity<ResourcePermission>(permission =>
        {
            permission.ToTable("ResourcePermissions", Schema);
            permission.Property(p => p.ResourceType).HasMaxLength(100);
            permission.Property(p => p.PrincipalType).HasConversion<string>().HasMaxLength(20);
            permission.HasIndex(p => new { p.ResourceType, p.ResourceId, p.PrincipalType, p.PrincipalId, p.RoleId }).IsUnique();
            permission.HasIndex(p => new { p.PrincipalType, p.PrincipalId });
            permission.HasOne<Role>().WithMany().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemState>(state =>
        {
            state.ToTable("SystemState", Schema);
            state.HasKey(s => s.Id);
            state.Property(s => s.Id).ValueGeneratedNever();
        });
    }
}
```

File: coworkee/src/Coworkee.Identity/Permissions/IdentityPermissionDefinitions.cs
```csharp
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Permissions;

internal sealed class IdentityPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(IdentityPermissions.GroupName, "Identity")
            .Add(IdentityPermissions.Users.View, "View users")
            .Add(IdentityPermissions.Users.Manage, "Manage users", IdentityPermissions.Users.View)
            .Add(IdentityPermissions.Groups.View, "View groups")
            .Add(IdentityPermissions.Groups.Manage, "Manage groups", IdentityPermissions.Groups.View, IdentityPermissions.Users.View)
            .Add(IdentityPermissions.Roles.View, "View roles")
            .Add(IdentityPermissions.Roles.Manage, "Manage roles", IdentityPermissions.Roles.View)
            .Add(IdentityPermissions.Permissions.Manage, "Manage permissions", IdentityPermissions.Roles.View)
            .Add(IdentityPermissions.ResourcePermissions.Manage, "Manage resource permissions", IdentityPermissions.Roles.View);
}
```

File: coworkee/src/Coworkee.Identity/Permissions/PermissionChecker.cs
```csharp
using Coworkee.Application.Authorization;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Coworkee.Identity.Permissions;

internal sealed class PermissionChecker(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionDefinitionManager definitions, HybridCache cache)
    : IPermissionChecker
{
    public async Task<IReadOnlyCollection<string>> GetGrantedAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return [];
        }

        var tenantId = currentUser.TenantId;
        return await cache.GetOrCreateAsync(
            $"coworkee:permissions:{tenantId}:{userId}",
            async ct => await LoadAsync(userId, tenantId, ct),
            tags: [PermissionCache.Tag],
            cancellationToken: cancellationToken);
    }

    public async Task<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken) =>
        (await GetGrantedAsync(cancellationToken)).Contains(permission);

    public async Task<bool> IsGrantedAsync(string permission, string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        if (await IsGrantedAsync(permission, cancellationToken))
        {
            return true;
        }

        if (currentUser.UserId is not { } userId)
        {
            return false;
        }

        var groupIds = await GroupIdsAsync(userId, cancellationToken);
        var roleIds = await db.Set<ResourcePermission>()
            .Where(p => p.ResourceType == resourceType && p.ResourceId == resourceId)
            .Where(p => (p.PrincipalType == PrincipalType.User && p.PrincipalId == userId)
                        || (p.PrincipalType == PrincipalType.Group && groupIds.Contains(p.PrincipalId)))
            .Select(p => p.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return roleIds.Count > 0 && (await GrantsForRolesAsync(roleIds, currentUser.TenantId, cancellationToken)).Contains(permission);
    }

    private async Task<string[]> LoadAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        var groupIds = await GroupIdsAsync(userId, cancellationToken);
        var roleIds = await db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == userId).Select(r => r.RoleId)
            .Union(db.Set<UserGroupRole>().Where(r => groupIds.Contains(r.GroupId)).Select(r => r.RoleId))
            .ToListAsync(cancellationToken);

        var roleGrants = await GrantsForRolesAsync(roleIds, tenantId, cancellationToken);
        var direct = await db.Set<PermissionGrant>()
            .Where(g => g.TenantId == null || g.TenantId == tenantId)
            .Where(g => (g.ProviderType == PermissionProviderType.User && g.ProviderKey == userId)
                        || (g.ProviderType == PermissionProviderType.Group && groupIds.Contains(g.ProviderKey)))
            .Select(g => g.Name)
            .ToListAsync(cancellationToken);

        return [.. definitions.Expand(roleGrants.Concat(direct))];
    }

    private async Task<IReadOnlySet<string>> GrantsForRolesAsync(List<Guid> roleIds, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (await db.Set<Role>().AnyAsync(r => roleIds.Contains(r.Id) && r.IsSystem && r.Name == SystemRoles.Admin, cancellationToken))
        {
            return definitions.Expand(definitions.All.Select(d => d.Name));
        }

        var names = await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == PermissionProviderType.Role && roleIds.Contains(g.ProviderKey))
            .Where(g => g.TenantId == null || g.TenantId == tenantId)
            .Select(g => g.Name)
            .ToListAsync(cancellationToken);
        return definitions.Expand(names);
    }

    private Task<List<Guid>> GroupIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (from member in db.Set<UserGroupMember>()
         join grp in db.Set<UserGroup>() on member.GroupId equals grp.Id
         where member.UserId == userId
         select grp.Id).ToListAsync(cancellationToken);
}

// ponytail: any grant/role/group change invalidates every cached permission set; per-user tags when write volume matters
public sealed class PermissionCache(HybridCache cache)
{
    internal const string Tag = "coworkee:permissions";

    public Task InvalidateAsync(CancellationToken cancellationToken) => cache.RemoveByTagAsync(Tag, cancellationToken).AsTask();
}
```

File: coworkee/src/Coworkee.Identity/Setup/SetupServices.cs
```csharp
using System.Security.Cryptography;
using System.Text;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Setup;

public sealed class SetupGateOptions
{
    public List<string> AllowedPrefixes { get; } = ["/api/v1/setup"];
}

public sealed class SetupToken
{
    public SetupToken(IConfiguration configuration)
    {
        var configured = configuration["Coworkee:SetupToken"];
        IsGenerated = string.IsNullOrWhiteSpace(configured);
        Value = IsGenerated ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_') : configured!;
    }

    internal string Value { get; }

    internal bool IsGenerated { get; }

    public bool Matches(string candidate) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Value), Encoding.UTF8.GetBytes(candidate ?? string.Empty));
}

public sealed class SystemStateCache(IServiceScopeFactory scopes)
{
    private volatile bool _initialized;

    public async Task<bool> IsInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return true;
        }

        await using var scope = scopes.CreateAsyncScope();
        _initialized = await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<SystemState>()
            .AnyAsync(s => s.IsInitialized, cancellationToken);
        return _initialized;
    }

    internal void Reset() => _initialized = false;
}

internal sealed partial class SetupTokenAnnouncer(SystemStateCache state, SetupToken token, ILogger<SetupTokenAnnouncer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (token.IsGenerated && !await state.IsInitializedAsync(stoppingToken))
            {
                LogToken(token.Value);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUnavailable(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "System is not set up yet. Setup token: {Token}")]
    private partial void LogToken(string token);

    [LoggerMessage(Level = LogLevel.Information, Message = "Setup state not available yet")]
    private partial void LogUnavailable(Exception exception);
}
```

File: coworkee/src/Coworkee.Identity/Setup/SetupHandlers.cs
```csharp
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Setup;

public sealed record GetSetupStatus : IQuery<Result<SetupStatusDto>>;

public sealed record CompleteSetup(CompleteSetupRequest Request) : ICommand<Result<SetupResultDto>>;

internal sealed class CompleteSetupValidator : AbstractValidator<CompleteSetup>
{
    public CompleteSetupValidator()
    {
        RuleFor(c => c.Request.SetupToken).NotEmpty();
        RuleFor(c => c.Request.TenantName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Request.AdminEmail).NotEmpty().EmailAddress();
        RuleFor(c => c.Request.AdminPassword).NotEmpty().MinimumLength(8);
    }
}

internal sealed class GetSetupStatusHandler(SystemStateCache state) : IHandler<GetSetupStatus, Result<SetupStatusDto>>
{
    public async Task<Result<SetupStatusDto>> HandleAsync(GetSetupStatus request, CancellationToken cancellationToken) =>
        new SetupStatusDto(await state.IsInitializedAsync(cancellationToken));
}

internal sealed class CompleteSetupHandler(CoworkeeDbContext db, UserManager<User> users, SetupToken token, SystemStateCache state, TimeProvider clock)
    : IHandler<CompleteSetup, Result<SetupResultDto>>
{
    public async Task<Result<SetupResultDto>> HandleAsync(CompleteSetup command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (await state.IsInitializedAsync(cancellationToken))
        {
            return Error.Conflict("setup.completed", "The system is already set up.");
        }

        if (!token.Matches(request.SetupToken))
        {
            return Error.Forbidden("setup.token_invalid", "The setup token is not valid.");
        }

        var tenant = new Tenant { Name = request.TenantName, Identifier = "default", IsDefault = true };
        db.Add(tenant);
        var admin = await SystemRoleAsync(SystemRoles.Admin, "Full access", cancellationToken);
        await SystemRoleAsync(SystemRoles.User, "Signed-in user", cancellationToken);

        var user = new User
        {
            TenantId = tenant.Id,
            UserName = request.AdminEmail,
            Email = request.AdminEmail,
            EmailConfirmed = true,
            FirstName = request.AdminFirstName,
            LastName = request.AdminLastName,
        };

        using (CurrentUserScope.Begin(new ImpersonatedUser(null, tenant.Id)))
        {
            var created = await users.CreateAsync(user, request.AdminPassword);
            if (!created.Succeeded)
            {
                return IdentityErrors.ToError(created);
            }
        }

        db.Set<IdentityUserRole<Guid>>().Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = admin.Id });
        var systemState = await db.Set<SystemState>().FindAsync([SystemState.SingletonId], cancellationToken);
        if (systemState is null)
        {
            systemState = new SystemState();
            db.Add(systemState);
        }

        systemState.IsInitialized = true;
        systemState.InitializedAt = clock.GetUtcNow();
        return new SetupResultDto(tenant.Id, user.Id);
    }

    private async Task<Role> SystemRoleAsync(string name, string description, CancellationToken cancellationToken)
    {
        var normalized = name.ToUpperInvariant();
        var role = await db.Set<Role>().FirstOrDefaultAsync(r => r.IsSystem && r.NormalizedName == normalized, cancellationToken);
        if (role is null)
        {
            role = new Role { Name = name, NormalizedName = normalized, Description = description, IsSystem = true };
            db.Add(role);
        }

        return role;
    }
}

internal static class IdentityErrors
{
    public static Error ToError(IdentityResult result) =>
        Error.Validation(result.Errors
            .GroupBy(e => e.Code.Contains("Password", StringComparison.Ordinal) ? "Password"
                : e.Code.Contains("Email", StringComparison.Ordinal) || e.Code.Contains("UserName", StringComparison.Ordinal) ? "Email"
                : "User")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal));
}
```

File: coworkee/src/Coworkee.Identity/IdentityEndpoints.cs
```csharp
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Setup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Identity;

internal static partial class IdentityEndpoints
{
    public static void Map(WebApplication app)
    {
        var setup = app.MapGroup("/api/v1/setup").WithTags("Setup").AllowAnonymous();
        setup.MapGet("/status", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetSetupStatus(), ct).ToHttpResult());
        setup.MapPost("/complete", (CompleteSetupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CompleteSetup(body), ct).ToHttpResult());

        var api = app.MapGroup("/api/v1/identity").WithTags("Identity").RequireAuthorization();
        api.MapGet("/permissions/me", async (IPermissionChecker checker, CancellationToken ct) =>
            TypedResults.Ok(await checker.GetGrantedAsync(ct)));
        MapIdentityApi(api);
    }

    static partial void MapIdentityApi(RouteGroupBuilder api);
}
```
Tasks 7 and 8 implement `MapIdentityApi` in `IdentityEndpoints.Api.cs`.

File: coworkee/src/Coworkee.Identity/CoworkeeIdentityModule.cs
```csharp
using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Identity.Persistence;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity;

[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class CoworkeeIdentityModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeIdentityModule).Assembly);
        services.AddSingleton<IModelContributor, IdentityModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, IdentityPermissionDefinitions>();
        services.AddHybridCache();
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddSingleton<PermissionCache>();
        services.AddSingleton<SetupToken>();
        services.AddSingleton<SystemStateCache>();
        services.AddHostedService<SetupTokenAnnouncer>();
        services.AddOptions<SetupGateOptions>();

        services.AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Lockout.MaxFailedAccessAttempts = 10;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddDefaultTokenProviders();
        services.AddScoped<IUserStore<User>>(provider => new UserStore<User, Role, CoworkeeDbContext, Guid>(provider.GetRequiredService<CoworkeeDbContext>()));
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.Use(async (httpContext, next) =>
        {
            var path = httpContext.Request.Path;
            var allowed = httpContext.RequestServices.GetRequiredService<IOptions<SetupGateOptions>>().Value.AllowedPrefixes;
            if (path.StartsWithSegments("/api")
                && !allowed.Any(prefix => path.StartsWithSegments(prefix))
                && !await httpContext.RequestServices.GetRequiredService<SystemStateCache>().IsInitializedAsync(httpContext.RequestAborted))
            {
                await TypedResults.Problem(
                    title: "The system has not been set up yet.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "setup_required" })
                    .ExecuteAsync(httpContext);
                return;
            }

            await next(httpContext);
        });

        IdentityEndpoints.Map(app);
    }
}
```

`dotnet sln Coworkee.slnx add src/Coworkee.Identity/Coworkee.Identity.csproj`

- [ ] **Step 4: Run, expect PASS** – `dotnet test --project tests/Coworkee.Identity.Tests` → 6 passed.
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat(identity): identity entities, setup flow and gate"`

---

### Task 7: User, Rollen, Grants – Handler und Endpunkte

**Files:** Create `coworkee/src/Coworkee.Identity/Users/UserHandlers.cs`, `coworkee/src/Coworkee.Identity/Roles/RoleHandlers.cs`, `coworkee/src/Coworkee.Identity/Permissions/GrantHandlers.cs`, `coworkee/src/Coworkee.Identity/IdentityEndpoints.Api.cs`; Test `coworkee/tests/Coworkee.Identity.Tests/AccessTests.cs`

**Interfaces:**
- Produces Endpunkte unter `/api/v1/identity`:
  - `GET /users?page&pageSize&search` (Users.View) → `PagedResult<UserDto>`; `POST /users` (Users.Manage) `CreateUserRequest` → `UserDto`; `PUT /users/{id}` `UpdateUserRequest` → 204; `PUT /users/{id}/roles` `IdListRequest` → 204 (409 `identity.last_admin`)
  - `GET /roles` (Roles.View) → `RoleDto[]`; `POST /roles` (Roles.Manage) `RoleRequest` → `Guid`; `PUT /roles/{id}` → 204; `DELETE /roles/{id}` → 204 (409 bei System-Rolle)
  - `GET /permissions/definitions` (Roles.View) → `PermissionGroupDto[]`; `GET /permissions/grants/{providerType}/{providerKey}` (Permissions.Manage) → `string[]`; `PUT /permissions/grants/{providerType}/{providerKey}` `NameListRequest` → 204
- `IdentityEndpoints.MapIdentityApi(RouteGroupBuilder)` ruft `MapUsers`, `MapRoles`, `MapGrants`, `MapGroups` (Task 8), `MapResourcePermissions` (Task 8) – Task 7 legt `MapGroups`/`MapResourcePermissions` als leere Methoden an, Task 8 füllt sie.

- [ ] **Step 1: Failing tests**

File: coworkee/tests/Coworkee.Identity.Tests/AccessTests.cs
```csharp
using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Tests;

public sealed class AccessTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task New_user_without_grants_is_forbidden()
    {
        var user = await CreateUserAsync("bob@acme.test");

        (await app.As(user.Id, _setup.TenantId).GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Role_grant_enables_access_and_manage_implies_view()
    {
        var user = await CreateUserAsync("carol@acme.test");
        var role = await CreateRoleAsync("Helpdesk", IdentityPermissions.Users.Manage);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();

        var page = await app.As(user.Id, _setup.TenantId).GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/identity/users", Ct);

        page!.TotalCount.ShouldBe(2);
        page.Items.Single(u => u.Id == user.Id).Roles.ShouldHaveSingleItem().Name.ShouldBe("Helpdesk");
    }

    [Fact]
    public async Task Revoked_grant_takes_effect_immediately()
    {
        var user = await CreateUserAsync("dave@acme.test");
        var role = await CreateRoleAsync("Viewer", IdentityPermissions.Users.View);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        var client = app.As(user.Id, _setup.TenantId);
        (await client.GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([]), Ct)).EnsureSuccessStatusCode();

        (await client.GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Last_admin_cannot_lose_the_admin_role()
    {
        var response = await Admin.PutAsJsonAsync($"/api/v1/identity/users/{_setup.AdminUserId}/roles", new IdListRequest([]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Users_of_other_tenants_are_invisible_and_unassignable()
    {
        var otherTenant = await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            db.Add(tenant);
            db.Add(new User { TenantId = tenant.Id, UserName = "eve@other.test", Email = "eve@other.test", NormalizedUserName = "EVE@OTHER.TEST", NormalizedEmail = "EVE@OTHER.TEST" });
            await Task.CompletedTask;
            return tenant.Id;
        });
        var eve = await app.InDbAsync(null, db => db.Set<User>().SingleAsync(u => u.TenantId == otherTenant));

        var page = await Admin.GetFromJsonAsync<PagedResult<UserDto>>("/api/v1/identity/users?search=eve", Ct);
        page!.TotalCount.ShouldBe(0);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{eve.Id}/roles", new IdListRequest([]), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_permission_names_are_rejected()
    {
        var role = await CreateRoleAsync("Odd");

        var response = await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(["Nope.Nothing"]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task System_roles_cannot_be_deleted()
    {
        var roles = await Admin.GetFromJsonAsync<RoleDto[]>("/api/v1/identity/roles", Ct);

        (await Admin.DeleteAsync($"/api/v1/identity/roles/{roles!.Single(r => r.Name == SystemRoles.Admin).Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Definitions_are_grouped()
    {
        var groups = await Admin.GetFromJsonAsync<PermissionGroupDto[]>("/api/v1/identity/permissions/definitions", Ct);

        groups!.Single(g => g.Name == IdentityPermissions.GroupName).Permissions.ShouldContain(p => p.Name == IdentityPermissions.Users.Manage);
    }

    [Fact]
    public async Task Password_hash_never_reaches_the_audit_log()
    {
        var user = await CreateUserAsync("frank@acme.test");

        var entries = await app.InDbAsync(_setup.TenantId, db => db.Set<AuditEntry>().Where(e => e.EntityType == nameof(User) && e.EntityId == user.Id.ToString()).ToListAsync());

        var changes = entries.SelectMany(e => e.Changes).ToList();
        changes.Single(c => c.Property == nameof(User.PasswordHash)).NewValue.ShouldBe("\"***\"");
        changes.ShouldNotContain(c => c.Property == nameof(User.SecurityStamp));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<UserDto> CreateUserAsync(string email)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", "First", "Last"), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;
    }

    private async Task<Guid> CreateRoleAsync(string name, params string[] permissions)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct);
        response.EnsureSuccessStatusCode();
        var id = await response.Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{id}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return id;
    }
}
```

- [ ] **Step 2: Run, expect FAIL** – `dotnet test --project tests/Coworkee.Identity.Tests` → Endpunkte fehlen (404).

- [ ] **Step 3: Implementation**

File: coworkee/src/Coworkee.Identity/Users/UserHandlers.cs
```csharp
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Paging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

[RequiresPermission(IdentityPermissions.Users.View)]
public sealed record GetUsers(PageRequest Page) : IQuery<Result<PagedResult<UserDto>>>;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record CreateUser(CreateUserRequest User) : ICommand<Result<UserDto>>;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record UpdateUser(Guid Id, UpdateUserRequest User) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Users.Manage)]
public sealed record SetUserRoles(Guid Id, IReadOnlyList<Guid> RoleIds) : ICommand<Result>;

internal sealed class GetUsersValidator : AbstractValidator<GetUsers>
{
    public GetUsersValidator() => RuleFor(q => q.Page).SetValidator(new PageRequestValidator());
}

internal sealed class CreateUserValidator : AbstractValidator<CreateUser>
{
    public CreateUserValidator()
    {
        RuleFor(c => c.User.Email).NotEmpty().EmailAddress();
        RuleFor(c => c.User.Password).NotEmpty();
    }
}

internal sealed class GetUsersHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetUsers, Result<PagedResult<UserDto>>>
{
    public async Task<Result<PagedResult<UserDto>>> HandleAsync(GetUsers query, CancellationToken cancellationToken)
    {
        var page = query.Page;
        var users = db.Set<User>().AsNoTracking().Where(u => u.TenantId == currentUser.TenantId);
        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var search = page.Search.Trim().ToLowerInvariant();
            users = users.Where(u => u.Email!.ToLower().Contains(search)
                                     || (u.FirstName ?? string.Empty).ToLower().Contains(search)
                                     || (u.LastName ?? string.Empty).ToLower().Contains(search));
        }

        var total = await users.CountAsync(cancellationToken);
        var rows = await users.OrderBy(u => u.Email).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(cancellationToken);
        var roles = await UserRoles.ForAsync(db, rows.Select(u => u.Id).ToList(), cancellationToken);
        return new PagedResult<UserDto>(rows.Select(u => u.ToDto(roles)).ToList(), total, page.Page, page.PageSize);
    }
}

internal sealed class CreateUserHandler(UserManager<User> users, ICurrentUser currentUser) : IHandler<CreateUser, Result<UserDto>>
{
    public async Task<Result<UserDto>> HandleAsync(CreateUser command, CancellationToken cancellationToken)
    {
        var request = command.User;
        var user = new User
        {
            TenantId = currentUser.TenantId!.Value,
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            FirstName = request.FirstName,
            LastName = request.LastName,
        };

        var created = await users.CreateAsync(user, request.Password);
        return created.Succeeded ? user.ToDto(new Dictionary<Guid, List<RoleRefDto>>()) : IdentityErrors.ToError(created);
    }
}

internal sealed class UpdateUserHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<UpdateUser, Result>
{
    public async Task<Result> HandleAsync(UpdateUser command, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        user.FirstName = command.User.FirstName;
        user.LastName = command.User.LastName;
        user.IsActive = command.User.IsActive;
        return Result.Success();
    }
}

internal sealed class SetUserRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser, PermissionCache cache) : IHandler<SetUserRoles, Result>
{
    public async Task<Result> HandleAsync(SetUserRoles command, CancellationToken cancellationToken)
    {
        if (!await db.Set<User>().AnyAsync(u => u.Id == command.Id && u.TenantId == currentUser.TenantId, cancellationToken))
        {
            return UserErrors.NotFound;
        }

        var wanted = command.RoleIds.Distinct().ToList();
        var roles = await db.Set<Role>().Where(r => wanted.Contains(r.Id) && (r.TenantId == null || r.TenantId == currentUser.TenantId)).ToListAsync(cancellationToken);
        if (roles.Count != wanted.Count)
        {
            return Error.Validation(nameof(command.RoleIds), "Unknown role.");
        }

        var adminRole = await db.Set<Role>().SingleAsync(r => r.IsSystem && r.Name == SystemRoles.Admin, cancellationToken);
        if (!wanted.Contains(adminRole.Id) && !await OtherAdminExistsAsync(command.Id, adminRole.Id, cancellationToken))
        {
            return Error.Conflict("identity.last_admin", "The last administrator cannot lose the administrator role.");
        }

        var current = await db.Set<IdentityUserRole<Guid>>().Where(r => r.UserId == command.Id).ToListAsync(cancellationToken);
        db.RemoveRange(current.Where(r => !wanted.Contains(r.RoleId)));
        db.AddRange(wanted.Where(id => current.All(r => r.RoleId != id)).Select(id => new IdentityUserRole<Guid> { UserId = command.Id, RoleId = id }));
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }

    private Task<bool> OtherAdminExistsAsync(Guid userId, Guid adminRoleId, CancellationToken cancellationToken) =>
        (from userRole in db.Set<IdentityUserRole<Guid>>()
         join user in db.Set<User>() on userRole.UserId equals user.Id
         where userRole.RoleId == adminRoleId && user.Id != userId && user.TenantId == currentUser.TenantId && user.IsActive
         select user.Id).AnyAsync(cancellationToken);
}

internal static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("identity.user_not_found", "User not found.");
}

internal static class UserRoles
{
    public static async Task<Dictionary<Guid, List<RoleRefDto>>> ForAsync(CoworkeeDbContext db, List<Guid> userIds, CancellationToken cancellationToken)
    {
        var rows = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                          join role in db.Set<Role>() on userRole.RoleId equals role.Id
                          where userIds.Contains(userRole.UserId)
                          select new { userRole.UserId, role.Id, role.Name }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(r => new RoleRefDto(r.Id, r.Name!)).ToList());
    }

    public static UserDto ToDto(this User user, Dictionary<Guid, List<RoleRefDto>> roles) =>
        new(user.Id, user.UserName!, user.Email!, user.FirstName, user.LastName, user.IsActive, roles.GetValueOrDefault(user.Id) ?? []);
}
```

File: coworkee/src/Coworkee.Identity/Roles/RoleHandlers.cs
```csharp
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Roles;

[RequiresPermission(IdentityPermissions.Roles.View)]
public sealed record GetRoles : IQuery<Result<IReadOnlyList<RoleDto>>>;

[RequiresPermission(IdentityPermissions.Roles.Manage)]
public sealed record CreateRole(RoleRequest Role) : ICommand<Result<Guid>>;

[RequiresPermission(IdentityPermissions.Roles.Manage)]
public sealed record UpdateRole(Guid Id, RoleRequest Role) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Roles.Manage)]
public sealed record DeleteRole(Guid Id) : ICommand<Result>;

internal sealed class CreateRoleValidator : AbstractValidator<CreateRole>
{
    public CreateRoleValidator() => RuleFor(c => c.Role.Name).NotEmpty().MaximumLength(256);
}

internal sealed class UpdateRoleValidator : AbstractValidator<UpdateRole>
{
    public UpdateRoleValidator() => RuleFor(c => c.Role.Name).NotEmpty().MaximumLength(256);
}

internal sealed class GetRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetRoles, Result<IReadOnlyList<RoleDto>>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> HandleAsync(GetRoles query, CancellationToken cancellationToken) =>
        await db.Set<Role>().AsNoTracking()
            .Where(r => r.TenantId == null || r.TenantId == currentUser.TenantId)
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description, r.IsSystem))
            .ToListAsync(cancellationToken);
}

internal sealed class CreateRoleHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<CreateRole, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateRole command, CancellationToken cancellationToken)
    {
        var normalized = command.Role.Name.Trim().ToUpperInvariant();
        if (await RoleNames.TakenAsync(db, normalized, currentUser.TenantId, null, cancellationToken))
        {
            return RoleNames.Taken;
        }

        var role = new Role { Name = command.Role.Name.Trim(), NormalizedName = normalized, Description = command.Role.Description, TenantId = currentUser.TenantId };
        db.Add(role);
        return role.Id;
    }
}

internal sealed class UpdateRoleHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<UpdateRole, Result>
{
    public async Task<Result> HandleAsync(UpdateRole command, CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == command.Id && r.TenantId == currentUser.TenantId, cancellationToken);
        if (role is null)
        {
            return RoleNames.NotFound;
        }

        var normalized = command.Role.Name.Trim().ToUpperInvariant();
        if (await RoleNames.TakenAsync(db, normalized, currentUser.TenantId, role.Id, cancellationToken))
        {
            return RoleNames.Taken;
        }

        role.Name = command.Role.Name.Trim();
        role.NormalizedName = normalized;
        role.Description = command.Role.Description;
        return Result.Success();
    }
}

internal sealed class DeleteRoleHandler(CoworkeeDbContext db, ICurrentUser currentUser, PermissionCache cache) : IHandler<DeleteRole, Result>
{
    public async Task<Result> HandleAsync(DeleteRole command, CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == command.Id && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken);
        if (role is null)
        {
            return RoleNames.NotFound;
        }

        if (role.IsSystem)
        {
            return Error.Conflict("identity.role_system", "System roles cannot be deleted.");
        }

        db.RemoveRange(await db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.Role && g.ProviderKey == role.Id).ToListAsync(cancellationToken));
        db.Remove(role);
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class RoleNames
{
    public static readonly Error Taken = Error.Conflict("identity.role_name_taken", "A role with this name already exists.");

    public static readonly Error NotFound = Error.NotFound("identity.role_not_found", "Role not found.");

    public static Task<bool> TakenAsync(CoworkeeDbContext db, string normalized, Guid? tenantId, Guid? except, CancellationToken cancellationToken) =>
        db.Set<Role>().AnyAsync(r => r.NormalizedName == normalized && (r.TenantId == null || r.TenantId == tenantId) && r.Id != except, cancellationToken);
}
```

File: coworkee/src/Coworkee.Identity/Permissions/GrantHandlers.cs
```csharp
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Permissions;

[RequiresPermission(IdentityPermissions.Roles.View)]
public sealed record GetPermissionDefinitions : IQuery<Result<IReadOnlyList<PermissionGroupDto>>>;

[RequiresPermission(IdentityPermissions.Permissions.Manage)]
public sealed record GetGrants(PermissionProviderType ProviderType, Guid ProviderKey) : IQuery<Result<IReadOnlyList<string>>>;

[RequiresPermission(IdentityPermissions.Permissions.Manage)]
public sealed record SetGrants(PermissionProviderType ProviderType, Guid ProviderKey, IReadOnlyList<string> Names) : ICommand<Result>;

internal sealed class GetPermissionDefinitionsHandler(IPermissionDefinitionManager definitions) : IHandler<GetPermissionDefinitions, Result<IReadOnlyList<PermissionGroupDto>>>
{
    public Task<Result<IReadOnlyList<PermissionGroupDto>>> HandleAsync(GetPermissionDefinitions query, CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionGroupDto> groups = definitions.Groups
            .Select(g => new PermissionGroupDto(g.Name, g.DisplayName, definitions.All
                .Where(d => d.Group == g.Name)
                .Select(d => new PermissionDto(d.Name, d.DisplayName, d.Implies))
                .ToList()))
            .ToList();
        return Task.FromResult<Result<IReadOnlyList<PermissionGroupDto>>>(Result<IReadOnlyList<PermissionGroupDto>>.Success(groups));
    }
}

internal sealed class GetGrantsHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetGrants, Result<IReadOnlyList<string>>>
{
    public async Task<Result<IReadOnlyList<string>>> HandleAsync(GetGrants query, CancellationToken cancellationToken) =>
        await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == query.ProviderType && g.ProviderKey == query.ProviderKey && g.TenantId == currentUser.TenantId)
            .OrderBy(g => g.Name)
            .Select(g => g.Name)
            .ToListAsync(cancellationToken);
}

internal sealed class SetGrantsHandler(CoworkeeDbContext db, ICurrentUser currentUser, IPermissionDefinitionManager definitions, PermissionCache cache)
    : IHandler<SetGrants, Result>
{
    public async Task<Result> HandleAsync(SetGrants command, CancellationToken cancellationToken)
    {
        var unknown = command.Names.Where(n => !definitions.Exists(n)).ToArray();
        if (unknown.Length > 0)
        {
            return Error.Validation(nameof(command.Names), $"Unknown permissions: {string.Join(", ", unknown)}");
        }

        if (!await ProviderExistsAsync(command, cancellationToken))
        {
            return Error.NotFound("identity.grant_target_not_found", "The role, user or group does not exist.");
        }

        var wanted = command.Names.Distinct(StringComparer.Ordinal).ToList();
        var current = await db.Set<PermissionGrant>()
            .Where(g => g.ProviderType == command.ProviderType && g.ProviderKey == command.ProviderKey && g.TenantId == currentUser.TenantId)
            .ToListAsync(cancellationToken);
        db.RemoveRange(current.Where(g => !wanted.Contains(g.Name)));
        db.AddRange(wanted.Where(n => current.All(g => g.Name != n)).Select(n => new PermissionGrant
        {
            Name = n,
            ProviderType = command.ProviderType,
            ProviderKey = command.ProviderKey,
            TenantId = currentUser.TenantId,
        }));
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }

    private Task<bool> ProviderExistsAsync(SetGrants command, CancellationToken cancellationToken) => command.ProviderType switch
    {
        PermissionProviderType.Role => db.Set<Role>().AnyAsync(r => r.Id == command.ProviderKey && !r.IsSystem && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken),
        PermissionProviderType.User => db.Set<User>().AnyAsync(u => u.Id == command.ProviderKey && u.TenantId == currentUser.TenantId, cancellationToken),
        _ => db.Set<UserGroup>().AnyAsync(g => g.Id == command.ProviderKey, cancellationToken),
    };
}
```

File: coworkee/src/Coworkee.Identity/IdentityEndpoints.Api.cs
```csharp
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Permissions;
using Coworkee.Identity.Roles;
using Coworkee.Identity.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Identity;

internal static partial class IdentityEndpoints
{
    static partial void MapIdentityApi(RouteGroupBuilder api)
    {
        MapUsers(api);
        MapRoles(api);
        MapGrants(api);
        MapGroups(api);
        MapResourcePermissions(api);
    }

    private static void MapUsers(RouteGroupBuilder api)
    {
        api.MapGet("/users", ([AsParameters] PageRequest page, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUsers(page), ct).ToHttpResult());
        api.MapPost("/users", (CreateUserRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateUser(body), ct).ToHttpResult());
        api.MapPut("/users/{id:guid}", (Guid id, UpdateUserRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateUser(id, body), ct).ToHttpResult());
        api.MapPut("/users/{id:guid}/roles", (Guid id, IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetUserRoles(id, body.Ids), ct).ToHttpResult());
    }

    private static void MapRoles(RouteGroupBuilder api)
    {
        api.MapGet("/roles", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRoles(), ct).ToHttpResult());
        api.MapPost("/roles", (RoleRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateRole(body), ct).ToHttpResult());
        api.MapPut("/roles/{id:guid}", (Guid id, RoleRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateRole(id, body), ct).ToHttpResult());
        api.MapDelete("/roles/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteRole(id), ct).ToHttpResult());
    }

    private static void MapGrants(RouteGroupBuilder api)
    {
        api.MapGet("/permissions/definitions", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPermissionDefinitions(), ct).ToHttpResult());
        api.MapGet("/permissions/grants/{providerType}/{providerKey:guid}", (PermissionProviderType providerType, Guid providerKey, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetGrants(providerType, providerKey), ct).ToHttpResult());
        api.MapPut("/permissions/grants/{providerType}/{providerKey:guid}", (PermissionProviderType providerType, Guid providerKey, NameListRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetGrants(providerType, providerKey, body.Names), ct).ToHttpResult());
    }

    static partial void MapGroups(RouteGroupBuilder api);

    static partial void MapResourcePermissions(RouteGroupBuilder api);
}
```
`MapGroups`/`MapResourcePermissions` are partial methods without implementation until Task 8 (calls compile away).

- [ ] **Step 4: Run, expect PASS** – `dotnet test --project tests/Coworkee.Identity.Tests` → 15 passed.
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat(identity): users, roles and permission grants api"`

---

### Task 8: Gruppen und Ressourcenrechte

**Files:** Create `coworkee/src/Coworkee.Identity/Groups/GroupHandlers.cs`, `coworkee/src/Coworkee.Identity/ResourcePermissions/ResourcePermissionHandlers.cs`, `coworkee/src/Coworkee.Identity/IdentityEndpoints.Groups.cs`; Test `coworkee/tests/Coworkee.Identity.Tests/GroupAndResourceTests.cs`

**Interfaces:**
- `/groups` GET (Groups.View) `PagedResult<GroupDto>`, POST `GroupRequest` → `Guid`, PUT `/groups/{id}`, DELETE `/groups/{id}`, PUT `/groups/{id}/members` `IdListRequest`, PUT `/groups/{id}/roles` `IdListRequest` (alle Manage, ändern → Cache-Invalidierung)
- `/resource-permissions/{resourceType}/{resourceId}` GET → `ResourcePermissionDto[]`, POST `GrantResourcePermissionRequest` → `Guid`, DELETE `/{id}` → 204; alle als `IResourceRequest` mit `IdentityPermissions.ResourcePermissions.Manage` (global oder auf der Ressource)

- [ ] **Step 1: Failing tests**

File: coworkee/tests/Coworkee.Identity.Tests/GroupAndResourceTests.cs
```csharp
using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.Tests;

public sealed class GroupAndResourceTests(IdentityApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await app.ResetAllAsync();
        _setup = await app.SetupAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Group_roles_apply_to_members_and_removal_revokes()
    {
        var user = await CreateUserAsync("gina@acme.test");
        var role = await CreateRoleAsync("Readers", IdentityPermissions.Users.View);
        var group = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("Support", null));
        await PutAsync($"/api/v1/identity/groups/{group}/roles", new IdListRequest([role]));
        await PutAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([user]));
        var client = app.As(user, _setup.TenantId);
        (await client.GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await PutAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([]));

        (await client.GetAsync("/api/v1/identity/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Groups_are_listed_with_members_and_roles()
    {
        var user = await CreateUserAsync("hank@acme.test");
        var group = await PostAsync<Guid>("/api/v1/identity/groups", new GroupRequest("Design", "Creative"));
        await PutAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([user]));

        var page = await Admin.GetFromJsonAsync<PagedResult<GroupDto>>("/api/v1/identity/groups", Ct);

        page!.Items.ShouldHaveSingleItem().MemberIds.ShouldBe([user]);
    }

    [Fact]
    public async Task Resource_role_applies_only_to_that_resource()
    {
        var user = await CreateUserAsync("ivy@acme.test");
        var role = await CreateRoleAsync("Folder manager", IdentityPermissions.ResourcePermissions.Manage);
        var folder = Guid.CreateVersion7();
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/Folder/{folder}",
            new GrantResourcePermissionRequest(PrincipalType.User, user, role), Ct)).EnsureSuccessStatusCode();
        var client = app.As(user, _setup.TenantId);

        (await client.GetAsync($"/api/v1/identity/resource-permissions/Folder/{folder}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/identity/resource-permissions/Folder/{Guid.CreateVersion7()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resource_permissions_can_be_listed_and_revoked()
    {
        var user = await CreateUserAsync("jack@acme.test");
        var role = await CreateRoleAsync("Reader");
        var folder = Guid.CreateVersion7();
        var id = await PostAsync<Guid>($"/api/v1/identity/resource-permissions/Folder/{folder}", new GrantResourcePermissionRequest(PrincipalType.User, user, role));

        (await Admin.GetFromJsonAsync<ResourcePermissionDto[]>($"/api/v1/identity/resource-permissions/Folder/{folder}", Ct))!.ShouldHaveSingleItem().Id.ShouldBe(id);
        (await Admin.DeleteAsync($"/api/v1/identity/resource-permissions/Folder/{folder}/{id}", Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<ResourcePermissionDto[]>($"/api/v1/identity/resource-permissions/Folder/{folder}", Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Duplicate_resource_grant_conflicts()
    {
        var user = await CreateUserAsync("kim@acme.test");
        var role = await CreateRoleAsync("Reader");
        var folder = Guid.CreateVersion7();
        var request = new GrantResourcePermissionRequest(PrincipalType.User, user, role);
        await PostAsync<Guid>($"/api/v1/identity/resource-permissions/Folder/{folder}", request);

        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/Folder/{folder}", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> CreateUserAsync(string email) =>
        (await PostAsync<UserDto>("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null))).Id;

    private async Task<Guid> CreateRoleAsync(string name, params string[] permissions)
    {
        var id = await PostAsync<Guid>("/api/v1/identity/roles", new RoleRequest(name, null));
        await PutAsync($"/api/v1/identity/permissions/grants/Role/{id}", new NameListRequest(permissions));
        return id;
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Admin.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task PutAsync(string url, object body) => (await Admin.PutAsJsonAsync(url, body, Ct)).EnsureSuccessStatusCode();
}
```

- [ ] **Step 2: Run, expect FAIL** – Gruppen-Endpunkte fehlen (404).

- [ ] **Step 3: Implementation**

File: coworkee/src/Coworkee.Identity/Groups/GroupHandlers.cs
```csharp
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Paging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Groups;

[RequiresPermission(IdentityPermissions.Groups.View)]
public sealed record GetGroups(PageRequest Page) : IQuery<Result<PagedResult<GroupDto>>>;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record CreateGroup(GroupRequest Group) : ICommand<Result<Guid>>;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record UpdateGroup(Guid Id, GroupRequest Group) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record DeleteGroup(Guid Id) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record SetGroupMembers(Guid Id, IReadOnlyList<Guid> UserIds) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Groups.Manage)]
public sealed record SetGroupRoles(Guid Id, IReadOnlyList<Guid> RoleIds) : ICommand<Result>;

internal sealed class GetGroupsValidator : AbstractValidator<GetGroups>
{
    public GetGroupsValidator() => RuleFor(q => q.Page).SetValidator(new PageRequestValidator());
}

internal sealed class CreateGroupValidator : AbstractValidator<CreateGroup>
{
    public CreateGroupValidator() => RuleFor(c => c.Group.Name).NotEmpty().MaximumLength(200);
}

internal sealed class UpdateGroupValidator : AbstractValidator<UpdateGroup>
{
    public UpdateGroupValidator() => RuleFor(c => c.Group.Name).NotEmpty().MaximumLength(200);
}

internal sealed class GetGroupsHandler(CoworkeeDbContext db) : IHandler<GetGroups, Result<PagedResult<GroupDto>>>
{
    public async Task<Result<PagedResult<GroupDto>>> HandleAsync(GetGroups query, CancellationToken cancellationToken)
    {
        var page = query.Page;
        var groups = db.Set<UserGroup>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var search = page.Search.Trim().ToLowerInvariant();
            groups = groups.Where(g => g.Name.ToLower().Contains(search));
        }

        var total = await groups.CountAsync(cancellationToken);
        var items = await groups.OrderBy(g => g.Name).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(g => new GroupDto(g.Id, g.Name, g.Description, g.Members.Select(m => m.UserId).ToList(), g.Roles.Select(r => r.RoleId).ToList()))
            .ToListAsync(cancellationToken);
        return new PagedResult<GroupDto>(items, total, page.Page, page.PageSize);
    }
}

internal sealed class CreateGroupHandler(CoworkeeDbContext db) : IHandler<CreateGroup, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateGroup command, CancellationToken cancellationToken)
    {
        var name = command.Group.Name.Trim();
        if (await db.Set<UserGroup>().AnyAsync(g => g.Name == name, cancellationToken))
        {
            return GroupErrors.NameTaken;
        }

        var group = new UserGroup { Name = name, Description = command.Group.Description };
        db.Add(group);
        return group.Id;
    }
}

internal sealed class UpdateGroupHandler(CoworkeeDbContext db) : IHandler<UpdateGroup, Result>
{
    public async Task<Result> HandleAsync(UpdateGroup command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var name = command.Group.Name.Trim();
        if (await db.Set<UserGroup>().AnyAsync(g => g.Name == name && g.Id != group.Id, cancellationToken))
        {
            return GroupErrors.NameTaken;
        }

        group.Name = name;
        group.Description = command.Group.Description;
        return Result.Success();
    }
}

internal sealed class DeleteGroupHandler(CoworkeeDbContext db, PermissionCache cache) : IHandler<DeleteGroup, Result>
{
    public async Task<Result> HandleAsync(DeleteGroup command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        db.RemoveRange(await db.Set<PermissionGrant>().Where(g => g.ProviderType == PermissionProviderType.Group && g.ProviderKey == group.Id).ToListAsync(cancellationToken));
        db.RemoveRange(await db.Set<ResourcePermission>().Where(p => p.PrincipalType == PrincipalType.Group && p.PrincipalId == group.Id).ToListAsync(cancellationToken));
        db.Remove(group);
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetGroupMembersHandler(CoworkeeDbContext db, ICurrentUser currentUser, PermissionCache cache) : IHandler<SetGroupMembers, Result>
{
    public async Task<Result> HandleAsync(SetGroupMembers command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().Include(g => g.Members).SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.UserIds.Distinct().ToList();
        var valid = await db.Set<User>().CountAsync(u => wanted.Contains(u.Id) && u.TenantId == currentUser.TenantId, cancellationToken);
        if (valid != wanted.Count)
        {
            return Error.Validation(nameof(command.UserIds), "Unknown user.");
        }

        group.Members.RemoveAll(m => !wanted.Contains(m.UserId));
        group.Members.AddRange(wanted.Where(id => group.Members.All(m => m.UserId != id)).Select(id => new UserGroupMember { GroupId = group.Id, UserId = id }));
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetGroupRolesHandler(CoworkeeDbContext db, ICurrentUser currentUser, PermissionCache cache) : IHandler<SetGroupRoles, Result>
{
    public async Task<Result> HandleAsync(SetGroupRoles command, CancellationToken cancellationToken)
    {
        var group = await db.Set<UserGroup>().Include(g => g.Roles).SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken);
        if (group is null)
        {
            return GroupErrors.NotFound;
        }

        var wanted = command.RoleIds.Distinct().ToList();
        var valid = await db.Set<Role>().CountAsync(r => wanted.Contains(r.Id) && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken);
        if (valid != wanted.Count)
        {
            return Error.Validation(nameof(command.RoleIds), "Unknown role.");
        }

        group.Roles.RemoveAll(r => !wanted.Contains(r.RoleId));
        group.Roles.AddRange(wanted.Where(id => group.Roles.All(r => r.RoleId != id)).Select(id => new UserGroupRole { GroupId = group.Id, RoleId = id }));
        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class GroupErrors
{
    public static readonly Error NotFound = Error.NotFound("identity.group_not_found", "Group not found.");

    public static readonly Error NameTaken = Error.Conflict("identity.group_name_taken", "A group with this name already exists.");
}
```

File: coworkee/src/Coworkee.Identity/ResourcePermissions/ResourcePermissionHandlers.cs
```csharp
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.ResourcePermissions;

public abstract record ResourcePermissionRequest(string ResourceType, Guid ResourceId) : IResourceRequest
{
    public string RequiredPermission => IdentityPermissions.ResourcePermissions.Manage;
}

public sealed record GetResourcePermissions(string ResourceType, Guid ResourceId)
    : ResourcePermissionRequest(ResourceType, ResourceId), IQuery<Result<IReadOnlyList<ResourcePermissionDto>>>;

public sealed record GrantResourcePermission(string ResourceType, Guid ResourceId, GrantResourcePermissionRequest Grant)
    : ResourcePermissionRequest(ResourceType, ResourceId), ICommand<Result<Guid>>;

public sealed record RevokeResourcePermission(string ResourceType, Guid ResourceId, Guid Id)
    : ResourcePermissionRequest(ResourceType, ResourceId), ICommand<Result>;

internal sealed class GetResourcePermissionsHandler(CoworkeeDbContext db) : IHandler<GetResourcePermissions, Result<IReadOnlyList<ResourcePermissionDto>>>
{
    public async Task<Result<IReadOnlyList<ResourcePermissionDto>>> HandleAsync(GetResourcePermissions query, CancellationToken cancellationToken) =>
        await db.Set<ResourcePermission>()
            .Where(p => p.ResourceType == query.ResourceType && p.ResourceId == query.ResourceId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new ResourcePermissionDto(p.Id, p.PrincipalType, p.PrincipalId, p.RoleId))
            .ToListAsync(cancellationToken);
}

internal sealed class GrantResourcePermissionHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GrantResourcePermission, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(GrantResourcePermission command, CancellationToken cancellationToken)
    {
        var grant = command.Grant;
        var principalExists = grant.PrincipalType == PrincipalType.User
            ? await db.Set<User>().AnyAsync(u => u.Id == grant.PrincipalId && u.TenantId == currentUser.TenantId, cancellationToken)
            : await db.Set<UserGroup>().AnyAsync(g => g.Id == grant.PrincipalId, cancellationToken);
        var roleExists = await db.Set<Role>().AnyAsync(r => r.Id == grant.RoleId && (r.TenantId == null || r.TenantId == currentUser.TenantId), cancellationToken);
        if (!principalExists || !roleExists)
        {
            return Error.Validation(nameof(command.Grant), "Unknown principal or role.");
        }

        if (await db.Set<ResourcePermission>().AnyAsync(p => p.ResourceType == command.ResourceType && p.ResourceId == command.ResourceId
                && p.PrincipalType == grant.PrincipalType && p.PrincipalId == grant.PrincipalId && p.RoleId == grant.RoleId, cancellationToken))
        {
            return Error.Conflict("identity.resource_permission_exists", "This permission is already granted.");
        }

        var permission = new ResourcePermission
        {
            ResourceType = command.ResourceType,
            ResourceId = command.ResourceId,
            PrincipalType = grant.PrincipalType,
            PrincipalId = grant.PrincipalId,
            RoleId = grant.RoleId,
        };
        db.Add(permission);
        return permission.Id;
    }
}

internal sealed class RevokeResourcePermissionHandler(CoworkeeDbContext db) : IHandler<RevokeResourcePermission, Result>
{
    public async Task<Result> HandleAsync(RevokeResourcePermission command, CancellationToken cancellationToken)
    {
        var permission = await db.Set<ResourcePermission>().SingleOrDefaultAsync(
            p => p.Id == command.Id && p.ResourceType == command.ResourceType && p.ResourceId == command.ResourceId, cancellationToken);
        if (permission is null)
        {
            return Error.NotFound("identity.resource_permission_not_found", "Resource permission not found.");
        }

        db.Remove(permission);
        return Result.Success();
    }
}
```

File: coworkee/src/Coworkee.Identity/IdentityEndpoints.Groups.cs
```csharp
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Groups;
using Coworkee.Identity.ResourcePermissions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Identity;

internal static partial class IdentityEndpoints
{
    static partial void MapGroups(RouteGroupBuilder api)
    {
        api.MapGet("/groups", ([AsParameters] PageRequest page, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetGroups(page), ct).ToHttpResult());
        api.MapPost("/groups", (GroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateGroup(body), ct).ToHttpResult());
        api.MapPut("/groups/{id:guid}", (Guid id, GroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateGroup(id, body), ct).ToHttpResult());
        api.MapDelete("/groups/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteGroup(id), ct).ToHttpResult());
        api.MapPut("/groups/{id:guid}/members", (Guid id, IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetGroupMembers(id, body.Ids), ct).ToHttpResult());
        api.MapPut("/groups/{id:guid}/roles", (Guid id, IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetGroupRoles(id, body.Ids), ct).ToHttpResult());
    }

    static partial void MapResourcePermissions(RouteGroupBuilder api)
    {
        api.MapGet("/resource-permissions/{resourceType}/{resourceId:guid}", (string resourceType, Guid resourceId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetResourcePermissions(resourceType, resourceId), ct).ToHttpResult());
        api.MapPost("/resource-permissions/{resourceType}/{resourceId:guid}", (string resourceType, Guid resourceId, GrantResourcePermissionRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GrantResourcePermission(resourceType, resourceId, body), ct).ToHttpResult());
        api.MapDelete("/resource-permissions/{resourceType}/{resourceId:guid}/{id:guid}", (string resourceType, Guid resourceId, Guid id, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RevokeResourcePermission(resourceType, resourceId, id), ct).ToHttpResult());
    }
}
```

- [ ] **Step 4: Run, expect PASS** – `dotnet test --solution Coworkee.slnx` → alles grün (Identity 20).
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat(identity): groups and resource permissions"` und `pwsh build/pack-local.ps1`.

---

### Task 9: Shareme nutzt Identity

**Files:** Modify `shareme/src/Shareme.Api/ShareMeApiModule.cs`, `shareme/src/Shareme.Api/Shareme.Api.csproj`, `shareme/src/Shareme.Infrastructure/Shareme.Infrastructure.csproj`, `shareme/src/Shareme.Infrastructure/ShareMeInfrastructureModule.cs`, `shareme/Directory.Packages.props`, `shareme/tests/Shareme.Api.Tests/*`; Create migration `Identity`

**Interfaces:**
- `ShareMeInfrastructureModule` hängt von `CoworkeeIdentityModule` ab; Gate erlaubt zusätzlich `/api/v1/system`.
- API validiert Bearer-Tokens (`Auth:Authority`, Audience `shareme_api`, `MapInboundClaims = false`); Tests nutzen `AddTestAuthentication`.

- [ ] **Step 1: Failing test** – neue Datei

File: shareme/tests/Shareme.Api.Tests/SetupFlowTests.cs
```csharp
using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;

namespace Shareme.Api.Tests;

public sealed class SetupFlowTests(ApiFixture api)
{
    [Fact]
    public async Task Identity_api_is_gated_until_setup_then_admin_has_access()
    {
        var ct = TestContext.Current.CancellationToken;
        var anonymous = api.Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/identity/users", ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var response = await anonymous.PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest(ApiFixture.SetupToken, "Shareme", "admin@shareme.test", "Admin#12345", null, null), ct);
        response.EnsureSuccessStatusCode();
        var setup = (await response.Content.ReadFromJsonAsync<SetupResultDto>(ct))!;

        var admin = api.Factory.CreateClient().AsUser(setup.AdminUserId, setup.TenantId);
        (await admin.GetAsync("/api/v1/identity/users", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
```
`ApiFixture`: add `public const string SetupToken = "shareme-test-token";`, create the factory as a subclass that sets `Coworkee:SetupToken` and adds test authentication:

File: shareme/tests/Shareme.Api.Tests/ApiFixture.cs
```csharp
using Coworkee.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Infrastructure;

[assembly: AssemblyFixture(typeof(Shareme.Api.Tests.ApiFixture))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Shareme.Api.Tests;

public sealed class ApiFixture : PostgresFixture
{
    public const string SetupToken = "shareme-test-token";

    public ApiFactory Factory { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory = new ApiFactory(this);
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShareMeDbContext>().Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await base.DisposeAsync();
    }
}

public sealed class ApiFactory(PostgresFixture postgres) : PostgresWebApplicationFactory<Program>(postgres, ShareMeInfrastructureModule.ConnectionStringName)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Coworkee:SetupToken", ApiFixture.SetupToken);
        builder.ConfigureTestServices(services => services.AddTestAuthentication());
    }
}
```

- [ ] **Step 2: Run, expect FAIL** – nach `pwsh ../coworkee/build/pack-local.ps1` und `dotnet restore --force-evaluate`: `dotnet test --project tests/Shareme.Api.Tests` → 404 statt 503 (Identity nicht eingebunden).

- [ ] **Step 3: Implementation**

`shareme/Directory.Packages.props` ergänzen:
```xml
    <PackageVersion Include="Coworkee.Contracts" Version="$(CoworkeeVersion)" />
    <PackageVersion Include="Coworkee.Identity" Version="$(CoworkeeVersion)" />
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.12" />
```

`Shareme.Infrastructure.csproj`: `<CoworkeePackage Include="Coworkee.Identity" />` ergänzen.

File: shareme/src/Shareme.Infrastructure/ShareMeInfrastructureModule.cs
```csharp
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Application;

namespace Shareme.Infrastructure;

[DependsOn(typeof(ShareMeApplicationModule), typeof(CoworkeeIdentityModule))]
public sealed class ShareMeInfrastructureModule : CoworkeeModule
{
    public const string ConnectionStringName = "shareme";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeDbContext<ShareMeDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "cw")));
        context.Services.AddHealthChecks().AddDbContextCheck<ShareMeDbContext>("database");
        context.Services.Configure<SetupGateOptions>(options => options.AllowedPrefixes.Add("/api/v1/system"));
    }
}
```
`Shareme.Infrastructure` brings ASP.NET types through Coworkee.Identity; add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to `Shareme.Infrastructure.csproj`.

`Shareme.Api.csproj`: `<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />`.

File: shareme/src/Shareme.Api/ShareMeApiModule.cs
```csharp
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Outbox;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Shareme.Application.System;
using Shareme.Infrastructure;

namespace Shareme.Api;

[DependsOn(typeof(ShareMeInfrastructureModule))]
public sealed class ShareMeApiModule : CoworkeeModule, IWebModule
{
    public const string Audience = "shareme_api";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeOutboxProcessing<ShareMeDbContext>();
        context.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = context.Configuration["Auth:Authority"];
                options.Audience = Audience;
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = !string.Equals(context.Configuration["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.Ordinal);
            });
    }

    public void ConfigureApplication(WebApplication app)
    {
        var system = app.MapGroup("/api/v1/system").WithTags("System");
        system.MapGet("/info", (IDispatcher dispatcher, CancellationToken ct) => dispatcher.SendAsync(new GetSystemInfo(), ct).ToHttpResult())
            .WithName("GetSystemInfo");
    }
}
```

Migration: `dotnet ef migrations add Identity --project src/Shareme.Infrastructure --startup-project src/Shareme.Infrastructure --output-dir Migrations`

- [ ] **Step 4: Run, expect PASS** – `dotnet test --solution Shareme.slnx` → alle grün (Api 4, Migrations 1, AppHost 1).
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat: shareme uses coworkee identity with setup gate"`
