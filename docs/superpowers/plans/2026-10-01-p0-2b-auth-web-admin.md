# P0.2b Auth-Server, BFF, Blazor-Admin – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Code blocks are preceded by `File: <repo>/<path>`; `.superpowers/extract-plan.py <plan> <task> <step>` writes them.

**Goal:** Echte Anmeldung über einen eigenen OpenID-Connect-Server, eine Blazor-Web-App als BFF, die Tokens nur serverseitig hält, und eine Admin-Oberfläche für User, Rollen mit Permission-Matrix, Gruppen, Ressourcenrechte sowie den Setup-Wizard.

**Architecture:** Drei neue Coworkee-Pakete: `Coworkee.AuthServer` (OpenIddict-Server + Login-Seiten als Razor Class Library), `Coworkee.Bff` (Cookie + OIDC + Token-Refresh + CSRF-Schutz + YARP-Forwarder `/api/**` + `/bff/user`), `Coworkee.Client.Blazor` (MudBlazor-Shell, API-Client, Auth-State, Permission-Policies, Admin-Seiten, Wizard). Shareme bekommt `Shareme.Auth`, `Shareme.Web` (Host) und `Shareme.Web.Client` (WASM, ohne Prerendering).

**Tech Stack:** OpenIddict 7.7.1 (AspNetCore, EntityFrameworkCore), Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.12, Microsoft.Extensions.ServiceDiscovery.Yarp 10.10.0, MudBlazor 9.10.0, MudBlazor.Extensions 9.10.0, Microsoft.AspNetCore.Components.WebAssembly(.Server) 10.0.12, Microsoft.AspNetCore.Components.Authorization 10.0.12, bunit 2.11.3.

**Spec:** `docs/superpowers/specs/2026-10-01-p0-foundation-design.md` 3.4–3.7.

## Global Constraints

- Alle Constraints aus P0.1/P0.2a.
- Flows: Authorization Code + PKCE (Pflicht) und Refresh Token. Client Credentials kommt mit den Integrationen (P4). Erste Clients sind öffentlich (PKCE); vertrauliche Clients sind per Konfiguration möglich.
- Access Token als signiertes JWT (keine Verschlüsselung), Audience `shareme_api`, Laufzeit 15 min; Claims `sub`, `tenant`, `name`, `email`, `role`.
- Browser sieht nie ein Token. Unsichere Methoden auf `/api/**` und `/bff/logout` verlangen Header `X-CSRF: 1`.
- WASM ohne Prerendering (`InteractiveWebAssemblyRenderMode(prerender: false)`).
- Inaktive User können sich nicht anmelden und bekommen keine neuen Tokens per Refresh.

## Review Focus

1. Inaktiver User: Login scheitert, Refresh liefert `invalid_grant`. Test in Task 1.
2. Fremde `redirect_uri` / fehlendes PKCE → Fehler statt Code. Test in Task 1.
3. POST über den BFF ohne `X-CSRF` → 400, nicht weitergeleitet. Test in Task 2.
4. Permission-Matrix speichert genau die angehakten Rechte; System-Rolle `admin` ist nicht editierbar. Test in Task 4.
5. Setup-Wizard mit falschem Token zeigt Fehler und bleibt auf dem Schritt. Test in Task 5.

---

### Task 1: Coworkee.AuthServer

**Files:** Create `coworkee/src/Coworkee.AuthServer/**`; Test `coworkee/tests/Coworkee.AuthServer.Tests/**`; Modify `coworkee/Directory.Packages.props`

**Interfaces:**
- `AuthServerOptions` (Section `Coworkee:Auth`): `bool AllowHttp`, `TimeSpan AccessTokenLifetime = 15 min`, `Dictionary<string,string> ApiScopes` (Scope → Audience), `List<AuthClientOptions> Clients`
- `AuthClientOptions`: `ClientId`, `DisplayName`, `string? ClientSecret`, `List<string> RedirectUris`, `List<string> PostLogoutRedirectUris`, `List<string> Scopes`
- `CoworkeeAuthServerModule : CoworkeeModule, IWebModule` (depends on `CoworkeeIdentityModule`): OpenIddict-Server + Validation, Identity-Cookie (`/Account/Login`), Razor Pages, Endpunkte `/connect/authorize` (GET/POST), `/connect/token` (POST), `/connect/endsession` (GET/POST), Seeding der Clients beim Start (`AuthClientSeeder`), Tabellen im Schema `auth`.
- Login-Seite `/Account/Login` (E-Mail, Passwort, „angemeldet bleiben“), Logout `/Account/Logout`.

- [ ] **Step 1: Failing tests**

File: coworkee/tests/Coworkee.AuthServer.Tests/Coworkee.AuthServer.Tests.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.AuthServer\Coworkee.AuthServer.csproj" />
    <ProjectReference Include="..\..\src\Coworkee.Testing\Coworkee.Testing.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" />
  </ItemGroup>
</Project>
```

File: coworkee/tests/Coworkee.AuthServer.Tests/AuthApp.cs
```csharp
using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.AuthServer.Tests.AuthApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.AuthServer.Tests;

public sealed class AuthApp : PostgresFixture
{
    public const string ClientId = "test-web";
    public const string RedirectUri = "https://client.test/signin-oidc";

    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:test"] = ConnectionString,
            ["Coworkee:SetupToken"] = "token",
            ["Coworkee:Auth:AllowHttp"] = "true",
            ["Coworkee:Auth:ApiScopes:test_api"] = "test_api",
            ["Coworkee:Auth:Clients:0:ClientId"] = ClientId,
            ["Coworkee:Auth:Clients:0:DisplayName"] = "Test",
            ["Coworkee:Auth:Clients:0:RedirectUris:0"] = RedirectUri,
            ["Coworkee:Auth:Clients:0:PostLogoutRedirectUris:0"] = "https://client.test/",
            ["Coworkee:Auth:Clients:0:Scopes:0"] = "test_api",
        });
        builder.AddCoworkee<TestAuthModule>();
        App = builder.Build();
        App.UseCoworkee();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthTestDbContext>().Database.EnsureCreatedAsync();
        }

        await App.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await base.DisposeAsync();
    }

    public HttpClient Browser()
    {
        var server = App.GetTestServer();
        return new HttpClient(new CookieContainerHandler(server.CreateHandler())) { BaseAddress = server.BaseAddress };
    }

    public async Task<SetupResultDto> SetupAsync()
    {
        await ResetAsync();
        App.Services.GetRequiredService<Coworkee.Identity.Setup.SystemStateCache>().GetType()
            .GetMethod("Reset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(App.Services.GetRequiredService<Coworkee.Identity.Setup.SystemStateCache>(), null);
        await App.Services.GetRequiredService<AuthClientSeeder>().SeedAsync(CancellationToken.None);
        var response = await Browser().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public async Task DeactivateAsync(Guid userId)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthTestDbContext>();
        var user = await db.Set<Coworkee.Identity.Domain.User>().SingleAsync(u => u.Id == userId);
        user.IsActive = false;
        await db.SaveChangesAsync();
    }
}

public sealed class AuthTestDbContext(DbContextOptions<AuthTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeAuthServerModule))]
public sealed class TestAuthModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeDbContext<AuthTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
}
```
`SystemStateCache.Reset` is internal; add `[assembly: InternalsVisibleTo("Coworkee.AuthServer.Tests")]` support by adding `<InternalsVisibleTo Include="Coworkee.AuthServer.Tests" />` to `coworkee/src/Coworkee.Identity/Coworkee.Identity.csproj`, then replace the reflection call by `App.Services.GetRequiredService<Coworkee.Identity.Setup.SystemStateCache>().Reset();`.

File: coworkee/tests/Coworkee.AuthServer.Tests/AuthorizationCodeFlowTests.cs
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Coworkee.AuthServer.Tests;

public sealed partial class AuthorizationCodeFlowTests(AuthApp app) : IAsyncLifetime
{
    private Coworkee.Contracts.Identity.SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Discovery_document_is_published()
    {
        var discovery = await app.Browser().GetStringAsync("/.well-known/openid-configuration", Ct);

        discovery.ShouldContain("/connect/authorize");
        discovery.ShouldContain("/connect/token");
    }

    [Fact]
    public async Task Anonymous_authorize_redirects_to_login()
    {
        var browser = NoRedirects();
        var response = await browser.GetAsync(AuthorizeUrl(Challenge(Verifier())), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/Account/Login");
    }

    [Fact]
    public async Task Code_flow_issues_tokens_with_user_and_tenant_claims()
    {
        var tokens = await SignInAsync("admin@acme.test", "Admin#12345");

        var access = new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty("access_token").GetString());
        access.Claims.Single(c => c.Type == "sub").Value.ShouldBe(_setup.AdminUserId.ToString());
        access.Claims.Single(c => c.Type == "tenant").Value.ShouldBe(_setup.TenantId.ToString());
        access.Audiences.ShouldContain("test_api");
        tokens.TryGetProperty("refresh_token", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_issues_new_tokens()
    {
        var tokens = await SignInAsync("admin@acme.test", "Admin#12345");

        var refreshed = await RefreshAsync(tokens.GetProperty("refresh_token").GetString()!);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deactivated_user_cannot_refresh()
    {
        var tokens = await SignInAsync("admin@acme.test", "Admin#12345");
        await app.DeactivateAsync(_setup.AdminUserId);

        var refreshed = await RefreshAsync(tokens.GetProperty("refresh_token").GetString()!);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refreshed.Content.ReadAsStringAsync(Ct)).ShouldContain("invalid_grant");
    }

    [Fact]
    public async Task Deactivated_user_cannot_sign_in()
    {
        await app.DeactivateAsync(_setup.AdminUserId);

        var (response, _) = await PostLoginAsync(NoRedirects(), "admin@acme.test", "Admin#12345", "/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Wrong_password_stays_on_login_page()
    {
        var (response, _) = await PostLoginAsync(NoRedirects(), "admin@acme.test", "wrong-password", "/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_redirect_uri_is_rejected()
    {
        var url = AuthorizeUrl(Challenge(Verifier())).Replace(Uri.EscapeDataString(AuthApp.RedirectUri), Uri.EscapeDataString("https://evil.test/cb"), StringComparison.Ordinal);

        var response = await NoRedirects().GetAsync(url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Missing_pkce_is_rejected()
    {
        var url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = AuthApp.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = AuthApp.RedirectUri,
            ["scope"] = "openid profile",
            ["state"] = "s",
        });

        var response = await NoRedirects().GetAsync(url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient NoRedirects() => app.Browser();

    private async Task<JsonElement> SignInAsync(string email, string password)
    {
        var browser = NoRedirects();
        var verifier = Verifier();
        var authorize = await browser.GetAsync(AuthorizeUrl(Challenge(verifier)), Ct);
        var loginUrl = authorize.Headers.Location!.ToString();
        var returnUrl = QueryHelpers.ParseQuery(new Uri(new Uri("http://localhost"), loginUrl).Query)["ReturnUrl"].ToString();

        var (login, _) = await PostLoginAsync(browser, email, password, returnUrl);
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var callback = await browser.GetAsync(login.Headers.Location, Ct);
        var code = QueryHelpers.ParseQuery(callback.Headers.Location!.Query)["code"].ToString();
        code.ShouldNotBeNullOrEmpty();

        var token = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = AuthApp.RedirectUri,
            ["client_id"] = AuthApp.ClientId,
            ["code_verifier"] = verifier,
        }), Ct);
        token.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await token.Content.ReadAsStringAsync(Ct)).RootElement;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        NoRedirects().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = AuthApp.ClientId,
        }), Ct);

    private static async Task<(HttpResponseMessage Response, string Html)> PostLoginAsync(HttpClient browser, string email, string password, string returnUrl)
    {
        var page = await browser.GetStringAsync("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl), Ct);
        var antiforgery = AntiforgeryToken().Match(page).Groups[1].Value;
        var response = await browser.PostAsync("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = antiforgery,
        }), Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static string AuthorizeUrl(string challenge) => QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
    {
        ["client_id"] = AuthApp.ClientId,
        ["response_type"] = "code",
        ["redirect_uri"] = AuthApp.RedirectUri,
        ["scope"] = "openid profile email roles offline_access test_api",
        ["code_challenge"] = challenge,
        ["code_challenge_method"] = "S256",
        ["state"] = "state",
    });

    private static string Verifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
```
The browser client must not follow redirects: build it with `new HttpClient(new CookieContainerHandler(server.CreateHandler()))` – `TestServer.CreateHandler()` does not follow redirects by itself, so redirects are observable.

Add to `Coworkee.AuthServer.Tests.csproj`: `<PackageReference Include="System.IdentityModel.Tokens.Jwt" />` and `<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />`; versions in `Directory.Packages.props`: `System.IdentityModel.Tokens.Jwt` 8.14.0.

- [ ] **Step 2: Run, expect FAIL** – Projekt `Coworkee.AuthServer` fehlt.

- [ ] **Step 3: Implementation**

Add to `coworkee/Directory.Packages.props`:
```xml
    <PackageVersion Include="OpenIddict.AspNetCore" Version="7.7.1" />
    <PackageVersion Include="OpenIddict.EntityFrameworkCore" Version="7.7.1" />
    <PackageVersion Include="System.IdentityModel.Tokens.Jwt" Version="8.14.0" />
```

File: coworkee/src/Coworkee.AuthServer/Coworkee.AuthServer.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <AddRazorSupportForMvc>true</AddRazorSupportForMvc>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\Coworkee.Identity\Coworkee.Identity.csproj" />
    <PackageReference Include="OpenIddict.AspNetCore" />
    <PackageReference Include="OpenIddict.EntityFrameworkCore" />
  </ItemGroup>
</Project>
```

File: coworkee/src/Coworkee.AuthServer/AuthServerOptions.cs
```csharp
namespace Coworkee.AuthServer;

public sealed class AuthServerOptions
{
    public const string Section = "Coworkee:Auth";

    public bool AllowHttp { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public Dictionary<string, string> ApiScopes { get; set; } = new(StringComparer.Ordinal);

    public List<AuthClientOptions> Clients { get; set; } = [];
}

public sealed class AuthClientOptions
{
    public required string ClientId { get; set; }

    public string? DisplayName { get; set; }

    public string? ClientSecret { get; set; }

    public List<string> RedirectUris { get; set; } = [];

    public List<string> PostLogoutRedirectUris { get; set; } = [];

    public List<string> Scopes { get; set; } = [];
}
```

File: coworkee/src/Coworkee.AuthServer/AuthModelContributor.cs
```csharp
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.AuthServer;

internal sealed class AuthModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.UseOpenIddict<Guid>();
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.ClrType.Namespace?.StartsWith("OpenIddict", StringComparison.Ordinal) == true))
        {
            entityType.SetSchema("auth");
        }
    }
}
```

File: coworkee/src/Coworkee.AuthServer/AuthClientSeeder.cs
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

public sealed partial class AuthClientSeeder(IServiceScopeFactory scopes, IOptions<AuthServerOptions> options, ILogger<AuthClientSeeder> logger) : BackgroundService
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var client in options.Value.Clients)
        {
            var descriptor = Describe(client);
            if (await manager.FindByClientIdAsync(client.ClientId, cancellationToken) is { } existing)
            {
                await manager.UpdateAsync(existing, descriptor, cancellationToken);
            }
            else
            {
                await manager.CreateAsync(descriptor, cancellationToken);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SeedAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSeedFailed(exception);
        }
    }

    private static OpenIddictApplicationDescriptor Describe(AuthClientOptions client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            DisplayName = client.DisplayName ?? client.ClientId,
            ClientSecret = client.ClientSecret,
            ClientType = string.IsNullOrEmpty(client.ClientSecret) ? ClientTypes.Public : ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        foreach (var scope in client.Scopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        foreach (var uri in client.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri));
        }

        foreach (var uri in client.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
        }

        return descriptor;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeding OpenID Connect clients failed")]
    private partial void LogSeedFailed(Exception exception);
}
```

File: coworkee/src/Coworkee.AuthServer/AuthEndpoints.cs
```csharp
using System.Security.Claims;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

internal static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync).ExcludeFromDescription();
        app.MapPost("/connect/token", TokenAsync).ExcludeFromDescription();
        app.MapMethods("/connect/endsession", [HttpMethods.Get, HttpMethods.Post], EndSessionAsync).ExcludeFromDescription();
    }

    private static async Task<IResult> AuthorizeAsync(HttpContext context, CoworkeeDbContext db, IOptions<AuthServerOptions> options)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No OpenID Connect request.");
        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!cookie.Succeeded || request.HasPromptValue(PromptValues.Login))
        {
            var parameters = context.Request.HasFormContentType ? context.Request.Form.ToList() : context.Request.Query.ToList();
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = context.Request.PathBase + context.Request.Path + QueryString.Create(parameters) },
                [IdentityConstants.ApplicationScheme]);
        }

        var userId = Guid.Parse(cookie.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var principal = await CreatePrincipalAsync(db, userId, request.GetScopes(), options.Value);
        return principal is null
            ? Results.Forbid(Error(Errors.AccessDenied, "The account is not active."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme])
            : Results.SignIn(principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> TokenAsync(HttpContext context, CoworkeeDbContext db, IOptions<AuthServerOptions> options)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("No OpenID Connect request.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Results.Forbid(Error(Errors.UnsupportedGrantType, "The grant type is not supported."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = result.Principal?.GetClaim(Claims.Subject);
        var principal = Guid.TryParse(subject, out var userId)
            ? await CreatePrincipalAsync(db, userId, result.Principal!.GetScopes(), options.Value)
            : null;
        return principal is null
            ? Results.Forbid(Error(Errors.InvalidGrant, "The account is no longer allowed to sign in."), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme])
            : Results.SignIn(principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> EndSessionAsync(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static async Task<ClaimsPrincipal?> CreatePrincipalAsync(CoworkeeDbContext db, Guid userId, IEnumerable<string> scopes, AuthServerOptions options)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user is null)
        {
            return null;
        }

        var roles = await (from userRole in db.Set<IdentityUserRole<Guid>>()
                           join role in db.Set<Role>() on userRole.RoleId equals role.Id
                           where userRole.UserId == userId
                           select role.Name!).ToListAsync();

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, string.Join(' ', new[] { user.FirstName, user.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : user.Email)
            .SetClaim("tenant", user.TenantId.ToString())
            .SetClaims(Claims.Role, [.. roles]);

        var granted = scopes.ToArray();
        identity.SetScopes(granted);
        identity.SetResources(granted.Where(options.ApiScopes.ContainsKey).Select(s => options.ApiScopes[s]).Distinct());
        identity.SetDestinations(_ => [Destinations.AccessToken, Destinations.IdentityToken]);
        return new ClaimsPrincipal(identity);
    }

    private static AuthenticationProperties Error(string error, string description) => new(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
    });
}
```

File: coworkee/src/Coworkee.AuthServer/CoworkeeAuthServerModule.cs
```csharp
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

[DependsOn(typeof(CoworkeeIdentityModule))]
public sealed class CoworkeeAuthServerModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var options = context.Configuration.GetSection(AuthServerOptions.Section).Get<AuthServerOptions>() ?? new AuthServerOptions();
        services.Configure<AuthServerOptions>(context.Configuration.GetSection(AuthServerOptions.Section));
        services.AddSingleton<IModelContributor, AuthModelContributor>();
        services.AddSingleton<AuthClientSeeder>();
        services.AddHostedService(provider => provider.GetRequiredService<AuthClientSeeder>());
        services.AddRazorPages();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.LoginPath = "/Account/Login";
            cookie.LogoutPath = "/Account/Logout";
        });
        new IdentityBuilder(typeof(User), typeof(Role), services).AddSignInManager();

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<CoworkeeDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/endsession");
                server.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
                server.RequireProofKeyForCodeExchange();
                server.RegisterScopes([Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles, Scopes.OfflineAccess, .. options.ApiScopes.Keys]);
                server.SetAccessTokenLifetime(options.AccessTokenLifetime);
                server.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
                server.DisableAccessTokenEncryption();
                var aspNetCore = server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
                if (options.AllowHttp)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.UseAspNetCore();
            });
    }

    public void ConfigureApplication(WebApplication app)
    {
        AuthEndpoints.Map(app);
        app.MapRazorPages();
    }
}
```
`// ponytail: development certificates; load signing/encryption certificates from configuration before production` – place this comment above `server.AddDevelopmentEncryptionCertificate()`.

File: coworkee/src/Coworkee.AuthServer/Pages/_ViewImports.cshtml
```cshtml
@using Coworkee.AuthServer.Pages
@namespace Coworkee.AuthServer.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

File: coworkee/src/Coworkee.AuthServer/Pages/_Layout.cshtml
```cshtml
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>@ViewData["Title"]</title>
  <style>
    :root { --paper:#f3efe6; --ink:#16140f; --ink-2:#4a463d; --rule:#d6cfbf; --signal:#e2552d; }
    @@media (prefers-color-scheme: dark) { :root { --paper:#14130f; --ink:#efe9dc; --ink-2:#bdb6a6; --rule:#34312a; --signal:#f06a43; } }
    * { box-sizing: border-box; }
    body { margin:0; min-height:100vh; display:grid; place-items:center; background:var(--paper); color:var(--ink); font:16px/1.5 "Segoe UI", system-ui, sans-serif; }
    main { width:min(420px, 92vw); border:1px solid var(--ink); padding:32px; }
    h1 { font-family: Georgia, serif; font-weight:400; font-size:34px; margin:0 0 24px; }
    label { display:block; font-size:13px; text-transform:uppercase; letter-spacing:.08em; color:var(--ink-2); margin:16px 0 6px; }
    input[type=email], input[type=password] { width:100%; padding:12px; border:1px solid var(--rule); background:transparent; color:var(--ink); font:inherit; }
    .check { display:flex; gap:8px; align-items:center; margin:16px 0; text-transform:none; letter-spacing:0; }
    button { width:100%; padding:13px; background:var(--ink); color:var(--paper); border:0; font:inherit; text-transform:uppercase; letter-spacing:.08em; cursor:pointer; }
    button:hover { background:var(--signal); }
    .error { color:var(--signal); margin:12px 0 0; }
  </style>
</head>
<body><main>@RenderBody()</main></body>
</html>
```

File: coworkee/src/Coworkee.AuthServer/Pages/_ViewStart.cshtml
```cshtml
@{ Layout = "_Layout"; }
```

File: coworkee/src/Coworkee.AuthServer/Pages/Account/Login.cshtml
```cshtml
@page
@model Coworkee.AuthServer.Pages.Account.LoginModel
@{ ViewData["Title"] = "Sign in"; }
<h1>Sign in</h1>
<form method="post">
  <label for="email">Email</label>
  <input id="email" type="email" asp-for="Input.Email" autocomplete="username" required autofocus />
  <label for="password">Password</label>
  <input id="password" type="password" asp-for="Input.Password" autocomplete="current-password" required />
  <label class="check"><input type="checkbox" asp-for="Input.RememberMe" /> Stay signed in</label>
  <button type="submit">Sign in</button>
  @if (Model.ErrorMessage is not null)
  {
    <p class="error" role="alert">@Model.ErrorMessage</p>
  }
</form>
```

File: coworkee/src/Coworkee.AuthServer/Pages/Account/Login.cshtml.cs
```csharp
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class LoginModel(SignInManager<User> signIn, UserManager<User> users) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        var user = await users.FindByEmailAsync(Input.Email);
        if (user is null || !user.IsActive)
        {
            ErrorMessage = "Email or password is not correct.";
            return Page();
        }

        var result = await signIn.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            ErrorMessage = "Too many attempts. Try again later.";
            return Page();
        }

        if (!result.Succeeded)
        {
            ErrorMessage = "Email or password is not correct.";
            return Page();
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    public sealed class LoginInput
    {
        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
```

File: coworkee/src/Coworkee.AuthServer/Pages/Account/Logout.cshtml
```cshtml
@page
@model Coworkee.AuthServer.Pages.Account.LogoutModel
@{ ViewData["Title"] = "Signed out"; }
<h1>Signed out</h1>
<p>You can close this window.</p>
```

File: coworkee/src/Coworkee.AuthServer/Pages/Account/Logout.cshtml.cs
```csharp
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class LogoutModel(SignInManager<User> signIn) : PageModel
{
    public async Task OnGetAsync() => await signIn.SignOutAsync();
}
```

`dotnet sln Coworkee.slnx add src/Coworkee.AuthServer/Coworkee.AuthServer.csproj tests/Coworkee.AuthServer.Tests/Coworkee.AuthServer.Tests.csproj`

- [ ] **Step 4: Run, expect PASS** – `dotnet test --project tests/Coworkee.AuthServer.Tests` → 9 passed. Debug with real failures (OpenIddict error descriptions arrive in the response body).
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat(auth): openid connect server with login pages"`

---

### Task 2: Coworkee.Bff

**Files:** Create `coworkee/src/Coworkee.Bff/**`; Test `coworkee/tests/Coworkee.Bff.Tests/**`

**Interfaces:**
- `BffOptions` (Section `Coworkee:Bff`): `string Authority`, `string ClientId`, `string? ClientSecret`, `string ApiAddress` (z. B. `https+http://shareme-api`), `List<string> Scopes`
- `WebApplicationBuilder.AddCoworkeeBff()` – Cookie (`SameSite=Strict`, HttpOnly) + OIDC (Code + PKCE, `SaveTokens`, `MapInboundClaims=false`, Name `name`, Role `role`), `TokenRefresher` (Cookie-Events: Refresh 60 s vor Ablauf), `AddHttpForwarderWithServiceDiscovery`
- `WebApplication.MapCoworkeeBff()` – `GET /bff/login?returnUrl`, `POST /bff/logout`, `GET /bff/user` → `BffUserDto(bool IsAuthenticated, string? Name, string? Email, Guid? UserId, Guid? TenantId, IReadOnlyList<string> Roles)`, Forwarder `/api/{**catch-all}` (hängt Access Token an, falls vorhanden); Middleware: unsichere Methoden auf `/api` und `/bff/logout` ohne `X-CSRF: 1` → 400.
- `BffUserDto` liegt in `Coworkee.Contracts.Identity`.

- [ ] **Step 1: Failing tests**

File: coworkee/tests/Coworkee.Bff.Tests/Coworkee.Bff.Tests.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.Bff\Coworkee.Bff.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" />
  </ItemGroup>
</Project>
```

File: coworkee/tests/Coworkee.Bff.Tests/BffTests.cs
```csharp
using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Coworkee.Bff.Tests;

public sealed class BffTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "web",
            ["Coworkee:Bff:ApiAddress"] = "http://api.test",
        });
        builder.AddCoworkeeBff();
        _app = builder.Build();
        _app.MapCoworkeeBff();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Anonymous_user_endpoint_reports_signed_out()
    {
        var user = await _client.GetFromJsonAsync<BffUserDto>("/bff/user", TestContext.Current.CancellationToken);

        user!.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task Unsafe_api_call_without_csrf_header_is_rejected() =>
        (await _client.PostAsync("/api/v1/identity/users", new StringContent("{}"), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Logout_without_csrf_header_is_rejected() =>
        (await _client.PostAsync("/bff/logout", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Login_challenges_the_identity_provider()
    {
        var response = await _client.GetAsync("/bff/login?returnUrl=/admin", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }
}
```
`Login_challenges_the_identity_provider` needs the OIDC metadata: configure `OpenIdConnectOptions.Configuration` statically in the test by adding to the configuration `["Coworkee:Bff:AuthorizationEndpoint"] = "https://auth.test/connect/authorize"`; `AddCoworkeeBff` uses `options.Configuration = new OpenIdConnectConfiguration { AuthorizationEndpoint = …, TokenEndpoint = …, EndSessionEndpoint = … }` only when `AuthorizationEndpoint` is configured (tests); otherwise discovery via `Authority`.

- [ ] **Step 2: Run, expect FAIL** – Projekt fehlt.

- [ ] **Step 3: Implementation**

Add to `coworkee/Directory.Packages.props`:
```xml
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.ServiceDiscovery.Yarp" Version="10.10.0" />
```

File: coworkee/src/Coworkee.Contracts/Identity/BffUserDto.cs
```csharp
namespace Coworkee.Contracts.Identity;

public sealed record BffUserDto(bool IsAuthenticated, string? Name, string? Email, Guid? UserId, Guid? TenantId, IReadOnlyList<string> Roles)
{
    public static readonly BffUserDto Anonymous = new(false, null, null, null, null, []);
}
```

File: coworkee/src/Coworkee.Bff/Coworkee.Bff.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\Coworkee.Contracts\Coworkee.Contracts.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery.Yarp" />
  </ItemGroup>
</Project>
```

File: coworkee/src/Coworkee.Bff/BffOptions.cs
```csharp
namespace Coworkee.Bff;

public sealed class BffOptions
{
    public const string Section = "Coworkee:Bff";

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public string ApiAddress { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = [];

    public string? AuthorizationEndpoint { get; set; }
}
```

File: coworkee/src/Coworkee.Bff/TokenRefresher.cs
```csharp
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Coworkee.Bff;

internal sealed class TokenRefresher(IOptionsMonitor<OpenIdConnectOptions> oidc, IHttpClientFactory httpClients, TimeProvider clock) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var expiresAt = context.Properties.GetTokenValue("expires_at");
        if (expiresAt is null || DateTimeOffset.Parse(expiresAt, CultureInfo.InvariantCulture) > clock.GetUtcNow().AddSeconds(60))
        {
            return;
        }

        var refreshToken = context.Properties.GetTokenValue("refresh_token");
        var options = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var configuration = options.ConfigurationManager is null ? options.Configuration : await options.ConfigurationManager.GetConfigurationAsync(context.HttpContext.RequestAborted);
        if (refreshToken is null || configuration?.TokenEndpoint is null)
        {
            context.RejectPrincipal();
            return;
        }

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = options.ClientId!,
        };
        if (!string.IsNullOrEmpty(options.ClientSecret))
        {
            form["client_secret"] = options.ClientSecret;
        }

        using var response = await httpClients.CreateClient(nameof(TokenRefresher)).PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(form), context.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
        var root = json.RootElement;
        context.Properties.UpdateTokenValue("access_token", root.GetProperty("access_token").GetString()!);
        if (root.TryGetProperty("refresh_token", out var newRefresh))
        {
            context.Properties.UpdateTokenValue("refresh_token", newRefresh.GetString()!);
        }

        var expires = clock.GetUtcNow().AddSeconds(root.GetProperty("expires_in").GetInt32());
        context.Properties.UpdateTokenValue("expires_at", expires.ToString("o", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }
}
```

File: coworkee/src/Coworkee.Bff/BffExtensions.cs
```csharp
using System.Net.Http.Headers;
using System.Security.Claims;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

namespace Coworkee.Bff;

public static class BffExtensions
{
    private const string CsrfHeader = "X-CSRF";

    public static WebApplicationBuilder AddCoworkeeBff(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(BffOptions.Section).Get<BffOptions>() ?? new BffOptions();
        builder.Services.Configure<BffOptions>(builder.Configuration.GetSection(BffOptions.Section));
        builder.Services.AddHttpClient(nameof(TokenRefresher));
        builder.Services.AddScoped<TokenRefresher>();
        builder.Services.AddHttpForwarderWithServiceDiscovery();

        builder.Services.AddAuthentication(auth =>
            {
                auth.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                auth.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(cookie =>
            {
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.EventsType = typeof(TokenRefresher);
                cookie.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(oidc =>
            {
                oidc.Authority = options.Authority;
                oidc.ClientId = options.ClientId;
                oidc.ClientSecret = options.ClientSecret;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.UsePkce = true;
                oidc.SaveTokens = true;
                oidc.MapInboundClaims = false;
                oidc.GetClaimsFromUserInfoEndpoint = false;
                oidc.RequireHttpsMetadata = !options.Authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
                oidc.TokenValidationParameters.NameClaimType = "name";
                oidc.TokenValidationParameters.RoleClaimType = "role";
                oidc.Scope.Clear();
                foreach (var scope in new[] { "openid", "profile", "email", "roles", "offline_access" }.Concat(options.Scopes))
                {
                    oidc.Scope.Add(scope);
                }

                if (options.AuthorizationEndpoint is not null)
                {
                    oidc.Configuration = new OpenIdConnectConfiguration
                    {
                        AuthorizationEndpoint = options.AuthorizationEndpoint,
                        TokenEndpoint = options.Authority.TrimEnd('/') + "/connect/token",
                        EndSessionEndpoint = options.Authority.TrimEnd('/') + "/connect/endsession",
                    };
                }
            });
        builder.Services.AddAuthorization();
        return builder;
    }

    public static WebApplication MapCoworkeeBff(this WebApplication app)
    {
        var options = app.Configuration.GetSection(BffOptions.Section).Get<BffOptions>() ?? new BffOptions();
        app.Use(async (context, next) =>
        {
            var unsafeMethod = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
            if (unsafeMethod
                && (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/bff/logout"))
                && context.Request.Headers[CsrfHeader] != "1")
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await next(context);
        });
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/bff/login", (string? returnUrl) => Results.Challenge(
            new AuthenticationProperties { RedirectUri = returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) ? returnUrl : "/" },
            [OpenIdConnectDefaults.AuthenticationScheme]));
        app.MapPost("/bff/logout", () => Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
        app.MapGet("/bff/user", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
            ? new BffUserDto(
                true,
                user.FindFirstValue("name"),
                user.FindFirstValue("email"),
                Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : null,
                Guid.TryParse(user.FindFirstValue("tenant"), out var tenant) ? tenant : null,
                user.FindAll("role").Select(c => c.Value).ToArray())
            : BffUserDto.Anonymous);

        app.MapForwarder("/api/{**catch-all}", options.ApiAddress, transforms => transforms.AddRequestTransform(async transform =>
        {
            var token = await transform.HttpContext.GetTokenAsync("access_token");
            if (token is not null)
            {
                transform.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }));
        return app;
    }
}
```

`dotnet sln Coworkee.slnx add src/Coworkee.Bff/Coworkee.Bff.csproj tests/Coworkee.Bff.Tests/Coworkee.Bff.Tests.csproj`

- [ ] **Step 4: Run, expect PASS** – `dotnet test --project tests/Coworkee.Bff.Tests` → 4 passed.
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat(bff): cookie oidc bff with csrf guard and api forwarder"`

---

### Task 3: Coworkee.Client.Blazor – Grundgerüst, API-Client, Auth-State, Permissions, Layout

**Files:** Create `coworkee/src/Coworkee.Client.Blazor/**`; Test `coworkee/tests/Coworkee.Client.Blazor.Tests/**`

**Interfaces:**
- `ICoworkeeApi` (Methoden für alle Identity-/Setup-Endpunkte, siehe Datei) und `CoworkeeApi : ICoworkeeApi` (HttpClient, `X-CSRF: 1`, Fehler → `ApiException(int Status, string? Code, IReadOnlyDictionary<string,string[]>? Errors)`)
- `BffAuthenticationStateProvider` (lädt `/bff/user`), `PermissionStore` (`Task<bool> HasAsync(string)`, `Reset()`), Policy `perm:<name>` über `PermissionPolicyProvider`, `<PermissionView Permission="…">`
- `CoworkeeNavItem(string Title, string Href, string Icon, string? Permission)`, `INavigationContributor { IEnumerable<CoworkeeNavItem> Items }`
- `CoworkeeLayout` (AppBar mit Titel aus `CoworkeeClientOptions.AppTitle`, Theme-Umschalter, Login/Logout; Drawer mit Navigation gefiltert nach Permission; prüft Setup-Status und leitet auf `/setup`)
- `services.AddCoworkeeClient(Action<CoworkeeClientOptions>?)`

- [ ] **Step 1: Failing tests**

File: coworkee/tests/Coworkee.Client.Blazor.Tests/Coworkee.Client.Blazor.Tests.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <ItemGroup>
    <ProjectReference Include="..\..\src\Coworkee.Client.Blazor\Coworkee.Client.Blazor.csproj" />
    <PackageReference Include="bunit" />
  </ItemGroup>
</Project>
```

File: coworkee/tests/Coworkee.Client.Blazor.Tests/ClientTestBase.cs
```csharp
using Bunit;
using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Api;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public abstract class ClientTestBase : BunitContext
{
    protected ClientTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(Api);
        Services.AddSingleton(new CoworkeeClientOptions());
    }

    protected ICoworkeeApi Api { get; } = Substitute.For<ICoworkeeApi>();
}
```

File: coworkee/tests/Coworkee.Client.Blazor.Tests/PermissionStoreTests.cs
```csharp
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Security;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class PermissionStoreTests
{
    [Fact]
    public async Task Loads_once_and_answers_from_cache()
    {
        var api = Substitute.For<ICoworkeeApi>();
        api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns(["Identity.Users.View"]);
        var store = new PermissionStore(api);

        (await store.HasAsync("Identity.Users.View")).ShouldBeTrue();
        (await store.HasAsync("Identity.Users.Manage")).ShouldBeFalse();

        await api.Received(1).GetMyPermissionsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_reloads()
    {
        var api = Substitute.For<ICoworkeeApi>();
        api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns(["A"], ["A", "B"]);
        var store = new PermissionStore(api);
        (await store.HasAsync("B")).ShouldBeFalse();

        store.Reset();

        (await store.HasAsync("B")).ShouldBeTrue();
    }

    [Fact]
    public async Task Failed_load_means_no_permissions()
    {
        var api = Substitute.For<ICoworkeeApi>();
        api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyCollection<string>>(_ => throw new ApiException(401, "auth.required", null));
        var store = new PermissionStore(api);

        (await store.HasAsync("A")).ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run, expect FAIL** – Projekt fehlt.

- [ ] **Step 3: Implementation**

Add to `coworkee/Directory.Packages.props`:
```xml
    <PackageVersion Include="MudBlazor" Version="9.10.0" />
    <PackageVersion Include="MudBlazor.Extensions" Version="9.10.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Components.Web" Version="10.0.12" />
    <PackageVersion Include="Microsoft.AspNetCore.Components.Authorization" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.12" />
    <PackageVersion Include="bunit" Version="2.11.3" />
```

File: coworkee/src/Coworkee.Client.Blazor/Coworkee.Client.Blazor.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <ItemGroup>
    <SupportedPlatform Include="browser" />
    <ProjectReference Include="..\Coworkee.Contracts\Coworkee.Contracts.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.Components.Web" />
    <PackageReference Include="Microsoft.AspNetCore.Components.Authorization" />
    <PackageReference Include="Microsoft.Extensions.Http" />
    <PackageReference Include="MudBlazor" />
    <PackageReference Include="MudBlazor.Extensions" />
  </ItemGroup>
</Project>
```

File: coworkee/src/Coworkee.Client.Blazor/_Imports.razor
```razor
@using System.Net.Http
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using MudBlazor
@using Coworkee.Contracts
@using Coworkee.Contracts.Identity
@using Coworkee.Client.Blazor
@using Coworkee.Client.Blazor.Api
@using Coworkee.Client.Blazor.Security
@using Coworkee.Client.Blazor.Components
```

File: coworkee/src/Coworkee.Client.Blazor/Api/ICoworkeeApi.cs
```csharp
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Api;

public interface ICoworkeeApi
{
    Task<BffUserDto> GetUserAsync(CancellationToken cancellationToken = default);

    Task<SetupStatusDto> GetSetupStatusAsync(CancellationToken cancellationToken = default);

    Task<SetupResultDto> CompleteSetupAsync(CompleteSetupRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetMyPermissionsAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<UserDto>> GetUsersAsync(PageRequest page, CancellationToken cancellationToken = default);

    Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task SetUserRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<Guid> CreateRoleAsync(RoleRequest request, CancellationToken cancellationToken = default);

    Task UpdateRoleAsync(Guid id, RoleRequest request, CancellationToken cancellationToken = default);

    Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGroupDto>> GetPermissionDefinitionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetGrantsAsync(PermissionProviderType providerType, Guid providerKey, CancellationToken cancellationToken = default);

    Task SetGrantsAsync(PermissionProviderType providerType, Guid providerKey, IReadOnlyList<string> names, CancellationToken cancellationToken = default);

    Task<PagedResult<GroupDto>> GetGroupsAsync(PageRequest page, CancellationToken cancellationToken = default);

    Task<Guid> CreateGroupAsync(GroupRequest request, CancellationToken cancellationToken = default);

    Task UpdateGroupAsync(Guid id, GroupRequest request, CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetGroupMembersAsync(Guid id, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    Task SetGroupRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourcePermissionDto>> GetResourcePermissionsAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default);

    Task<Guid> GrantResourcePermissionAsync(string resourceType, Guid resourceId, GrantResourcePermissionRequest request, CancellationToken cancellationToken = default);

    Task RevokeResourcePermissionAsync(string resourceType, Guid resourceId, Guid id, CancellationToken cancellationToken = default);
}

public sealed class ApiException(int status, string? code, IReadOnlyDictionary<string, string[]>? errors)
    : Exception(code ?? $"Request failed with status {status}.")
{
    public int Status { get; } = status;

    public string? Code { get; } = code;

    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}
```

File: coworkee/src/Coworkee.Client.Blazor/Api/CoworkeeApi.cs
```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Api;

internal sealed class CoworkeeApi(HttpClient http) : ICoworkeeApi
{
    private const string Identity = "api/v1/identity";

    public Task<BffUserDto> GetUserAsync(CancellationToken cancellationToken = default) => GetAsync<BffUserDto>("bff/user", cancellationToken);

    public Task<SetupStatusDto> GetSetupStatusAsync(CancellationToken cancellationToken = default) => GetAsync<SetupStatusDto>("api/v1/setup/status", cancellationToken);

    public Task<SetupResultDto> CompleteSetupAsync(CompleteSetupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SetupResultDto>(HttpMethod.Post, "api/v1/setup/complete", request, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetMyPermissionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<string[]>($"{Identity}/permissions/me", cancellationToken);

    public Task<PagedResult<UserDto>> GetUsersAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<UserDto>>($"{Identity}/users?page={page.Page}&pageSize={page.PageSize}&search={Uri.EscapeDataString(page.Search ?? string.Empty)}", cancellationToken);

    public Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<UserDto>(HttpMethod.Post, $"{Identity}/users", request, cancellationToken);

    public Task UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{id}", request, cancellationToken);

    public Task SetUserRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/users/{id}/roles", new IdListRequest(roleIds), cancellationToken);

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<RoleDto[]>($"{Identity}/roles", cancellationToken);

    public Task<Guid> CreateRoleAsync(RoleRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, $"{Identity}/roles", request, cancellationToken);

    public Task UpdateRoleAsync(Guid id, RoleRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/roles/{id}", request, cancellationToken);

    public Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/roles/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<PermissionGroupDto>> GetPermissionDefinitionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<PermissionGroupDto[]>($"{Identity}/permissions/definitions", cancellationToken);

    public async Task<IReadOnlyList<string>> GetGrantsAsync(PermissionProviderType providerType, Guid providerKey, CancellationToken cancellationToken = default) =>
        await GetAsync<string[]>($"{Identity}/permissions/grants/{providerType}/{providerKey}", cancellationToken);

    public Task SetGrantsAsync(PermissionProviderType providerType, Guid providerKey, IReadOnlyList<string> names, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/permissions/grants/{providerType}/{providerKey}", new NameListRequest(names), cancellationToken);

    public Task<PagedResult<GroupDto>> GetGroupsAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<GroupDto>>($"{Identity}/groups?page={page.Page}&pageSize={page.PageSize}&search={Uri.EscapeDataString(page.Search ?? string.Empty)}", cancellationToken);

    public Task<Guid> CreateGroupAsync(GroupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, $"{Identity}/groups", request, cancellationToken);

    public Task UpdateGroupAsync(Guid id, GroupRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/groups/{id}", request, cancellationToken);

    public Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/groups/{id}", null, cancellationToken);

    public Task SetGroupMembersAsync(Guid id, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/groups/{id}/members", new IdListRequest(userIds), cancellationToken);

    public Task SetGroupRolesAsync(Guid id, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Identity}/groups/{id}/roles", new IdListRequest(roleIds), cancellationToken);

    public async Task<IReadOnlyList<ResourcePermissionDto>> GetResourcePermissionsAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default) =>
        await GetAsync<ResourcePermissionDto[]>($"{Identity}/resource-permissions/{resourceType}/{resourceId}", cancellationToken);

    public Task<Guid> GrantResourcePermissionAsync(string resourceType, Guid resourceId, GrantResourcePermissionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Guid>(HttpMethod.Post, $"{Identity}/resource-permissions/{resourceType}/{resourceId}", request, cancellationToken);

    public Task RevokeResourcePermissionAsync(string resourceType, Guid resourceId, Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Identity}/resource-permissions/{resourceType}/{resourceId}/{id}", null, cancellationToken);

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, url, body, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    private async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, url, body, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-CSRF", "1");
        var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return response;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        Dictionary<string, string[]>? errors = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            code = problem.TryGetProperty("code", out var c) ? c.GetString() : null;
            errors = problem.TryGetProperty("errors", out var e) ? e.Deserialize<Dictionary<string, string[]>>() : null;
        }
        catch (JsonException)
        {
        }

        throw new ApiException((int)response.StatusCode, code, errors);
    }
}
```

File: coworkee/src/Coworkee.Client.Blazor/Security/PermissionStore.cs
```csharp
using Coworkee.Client.Blazor.Api;

namespace Coworkee.Client.Blazor.Security;

public sealed class PermissionStore(ICoworkeeApi api)
{
    private Task<HashSet<string>>? _permissions;

    public event Action? Changed;

    public async Task<bool> HasAsync(string permission) => (await (_permissions ??= LoadAsync())).Contains(permission);

    public void Reset()
    {
        _permissions = null;
        Changed?.Invoke();
    }

    private async Task<HashSet<string>> LoadAsync()
    {
        try
        {
            return new HashSet<string>(await api.GetMyPermissionsAsync(), StringComparer.Ordinal);
        }
        catch (ApiException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
```

File: coworkee/src/Coworkee.Client.Blazor/Security/BffAuthenticationStateProvider.cs
```csharp
using System.Security.Claims;
using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components.Authorization;

namespace Coworkee.Client.Blazor.Security;

public sealed class BffAuthenticationStateProvider(ICoworkeeApi api) : AuthenticationStateProvider
{
    private Task<AuthenticationState>? _state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            var user = await api.GetUserAsync();
            if (!user.IsAuthenticated)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var claims = new List<Claim> { new("name", user.Name ?? user.Email ?? string.Empty), new("email", user.Email ?? string.Empty), new("sub", user.UserId?.ToString() ?? string.Empty) };
            claims.AddRange(user.Roles.Select(r => new Claim("role", r)));
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "bff", "name", "role")));
        }
        catch (ApiException)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }
}
```

File: coworkee/src/Coworkee.Client.Blazor/Security/PermissionPolicies.cs
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Coworkee.Client.Blazor.Security;

public static class PermissionPolicy
{
    public const string Prefix = "perm:";

    public static string For(string permission) => Prefix + permission;
}

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal)
            ? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName[PermissionPolicy.Prefix.Length..])).Build()
            : await base.GetPolicyAsync(policyName);
}

internal sealed class PermissionHandler(PermissionStore permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await permissions.HasAsync(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
```

File: coworkee/src/Coworkee.Client.Blazor/CoworkeeClientOptions.cs
```csharp
namespace Coworkee.Client.Blazor;

public sealed class CoworkeeClientOptions
{
    public string AppTitle { get; set; } = "Coworkee";

    public string LoginPath { get; set; } = "/bff/login";

    public string LogoutPath { get; set; } = "/bff/logout";
}

public sealed record CoworkeeNavItem(string Title, string Href, string Icon, string? Permission = null);

public interface INavigationContributor
{
    IEnumerable<CoworkeeNavItem> Items { get; }
}
```

File: coworkee/src/Coworkee.Client.Blazor/CoworkeeClientExtensions.cs
```csharp
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor;

public static class CoworkeeClientExtensions
{
    public static IServiceCollection AddCoworkeeClient(this IServiceCollection services, Uri baseAddress, Action<CoworkeeClientOptions>? configure = null)
    {
        var options = new CoworkeeClientOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddMudServicesWithExtensions();
        services.AddHttpClient<ICoworkeeApi, CoworkeeApi>(client => client.BaseAddress = baseAddress);
        services.AddScoped<PermissionStore>();
        services.AddScoped<BffAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(provider => provider.GetRequiredService<BffAuthenticationStateProvider>());
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<INavigationContributor, AdminNavigation>();
        return services;
    }
}

internal sealed class AdminNavigation : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Users", "/admin/users", MudBlazor.Icons.Material.Outlined.Person, Coworkee.Contracts.Identity.IdentityPermissions.Users.View),
        new("Groups", "/admin/groups", MudBlazor.Icons.Material.Outlined.Groups, Coworkee.Contracts.Identity.IdentityPermissions.Groups.View),
        new("Roles", "/admin/roles", MudBlazor.Icons.Material.Outlined.Shield, Coworkee.Contracts.Identity.IdentityPermissions.Roles.View),
    ];
}
```

File: coworkee/src/Coworkee.Client.Blazor/Components/PermissionView.razor
```razor
<AuthorizeView Policy="@PermissionPolicy.For(Permission)">
    <Authorized>@ChildContent</Authorized>
    <NotAuthorized>@NotAuthorized</NotAuthorized>
</AuthorizeView>

@code {
    [Parameter, EditorRequired] public string Permission { get; set; } = string.Empty;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public RenderFragment? NotAuthorized { get; set; }
}
```

File: coworkee/src/Coworkee.Client.Blazor/Components/CoworkeeTheme.cs
```csharp
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public static class CoworkeeTheme
{
    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#16140f",
            Secondary = "#e2552d",
            Background = "#f3efe6",
            Surface = "#fbf9f4",
            AppbarBackground = "#f3efe6",
            AppbarText = "#16140f",
            DrawerBackground = "#ebe5d8",
            TextPrimary = "#16140f",
            TextSecondary = "#4a463d",
            LinesDefault = "#d6cfbf",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#efe9dc",
            Secondary = "#f06a43",
            Background = "#14130f",
            Surface = "#1f1d18",
            AppbarBackground = "#14130f",
            AppbarText = "#efe9dc",
            DrawerBackground = "#1c1a15",
            TextPrimary = "#efe9dc",
            TextSecondary = "#bdb6a6",
            LinesDefault = "#34312a",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "0px" },
    };
}
```

File: coworkee/src/Coworkee.Client.Blazor/Components/CoworkeeLayout.razor
```razor
@inherits LayoutComponentBase
@inject CoworkeeClientOptions Options
@inject IEnumerable<INavigationContributor> Navigation
@inject ICoworkeeApi Api
@inject NavigationManager Nav

<MudThemeProvider Theme="CoworkeeTheme.Default" @bind-IsDarkMode="_dark" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudAppBar Elevation="0" Dense="true">
        <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start" OnClick="() => _drawer = !_drawer" aria-label="Toggle navigation" />
        <MudText Typo="Typo.h6" Class="ml-2">@Options.AppTitle</MudText>
        <MudSpacer />
        <MudIconButton Icon="@(_dark ? Icons.Material.Outlined.LightMode : Icons.Material.Outlined.DarkMode)" Color="Color.Inherit" OnClick="() => _dark = !_dark" aria-label="Toggle dark mode" />
        <AuthorizeView>
            <Authorized>
                <MudText Typo="Typo.body2" Class="mx-3">@context.User.Identity?.Name</MudText>
                <form method="post" action="@Options.LogoutPath" @formname="logout">
                    <input type="hidden" name="X-CSRF" value="1" />
                    <MudButton Variant="Variant.Outlined" Color="Color.Inherit" Size="Size.Small" OnClick="LogoutAsync">Sign out</MudButton>
                </form>
            </Authorized>
            <NotAuthorized>
                <MudButton Variant="Variant.Outlined" Color="Color.Inherit" Size="Size.Small" Href="@($"{Options.LoginPath}?returnUrl={Uri.EscapeDataString(Nav.ToBaseRelativePath(Nav.Uri) is { Length: > 0 } p ? "/" + p : "/")}")">Sign in</MudButton>
            </NotAuthorized>
        </AuthorizeView>
    </MudAppBar>
    <MudDrawer @bind-Open="_drawer" Elevation="0" Variant="DrawerVariant.Responsive">
        <MudNavMenu>
            <MudNavLink Href="/" Match="NavLinkMatch.All" Icon="@Icons.Material.Outlined.Home">Home</MudNavLink>
            @foreach (var item in Navigation.SelectMany(n => n.Items))
            {
                if (item.Permission is null)
                {
                    <MudNavLink Href="@item.Href" Icon="@item.Icon">@item.Title</MudNavLink>
                }
                else
                {
                    <PermissionView Permission="@item.Permission">
                        <MudNavLink Href="@item.Href" Icon="@item.Icon">@item.Title</MudNavLink>
                    </PermissionView>
                }
            }
        </MudNavMenu>
    </MudDrawer>
    <MudMainContent>
        <MudContainer MaxWidth="MaxWidth.Large" Class="py-6">@Body</MudContainer>
    </MudMainContent>
</MudLayout>

@code {
    private bool _drawer = true;
    private bool _dark;

    protected override async Task OnInitializedAsync()
    {
        if (!Nav.Uri.Contains("/setup", StringComparison.Ordinal))
        {
            try
            {
                if (!(await Api.GetSetupStatusAsync()).IsInitialized)
                {
                    Nav.NavigateTo("/setup");
                }
            }
            catch (ApiException)
            {
            }
        }
    }

    private async Task LogoutAsync()
    {
        await Api.LogoutAsync();
        Nav.NavigateTo("/", forceLoad: true);
    }
}
```
Add to `ICoworkeeApi`: `Task LogoutAsync(CancellationToken cancellationToken = default);` and to `CoworkeeApi`: `public Task LogoutAsync(CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, "bff/logout", null, cancellationToken);`. Drop the `<form>` wrapper in the layout (the button calls `LogoutAsync`; the BFF answers the POST with a sign-out redirect, `forceLoad` reloads the app).

`dotnet sln Coworkee.slnx add src/Coworkee.Client.Blazor/Coworkee.Client.Blazor.csproj tests/Coworkee.Client.Blazor.Tests/Coworkee.Client.Blazor.Tests.csproj`

- [ ] **Step 4: Run, expect PASS** – `dotnet test --project tests/Coworkee.Client.Blazor.Tests` → 3 passed; `dotnet build Coworkee.slnx` without warnings (fix MudBlazor 9 API mismatches the compiler reports and ledger them).
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat(client): blazor client foundation with bff auth, permissions and layout"`

---

### Task 4: Admin-Seiten, Permission-Matrix, Ressourcenrechte-Panel

**Files:** Create `coworkee/src/Coworkee.Client.Blazor/Pages/Admin/*.razor`, `coworkee/src/Coworkee.Client.Blazor/Components/PermissionMatrix.razor`, `coworkee/src/Coworkee.Client.Blazor/Components/ResourcePermissionsPanel.razor`; Test `coworkee/tests/Coworkee.Client.Blazor.Tests/PermissionMatrixTests.cs`, `ResourcePermissionsPanelTests.cs`

**Interfaces:**
- `PermissionMatrix` (Parameter `PermissionProviderType ProviderType`, `Guid ProviderKey`, `bool ReadOnly`): Gruppen mit Checkboxen (`data-permission="<name>"`), Button „Save“ (`data-testid="save-permissions"`) → `SetGrantsAsync` mit genau den angehakten Namen; Implikationen werden als Hinweis angezeigt.
- `ResourcePermissionsPanel` (Parameter `string ResourceType`, `Guid ResourceId`): listet Einträge (Principal, Rolle), Revoke-Button je Eintrag (`data-testid="revoke-<id>"`), Formular zum Vergeben (User oder Gruppe + Rolle).
- Seiten: `/admin/users` (Tabelle, Suche, Dialog „Neuer User“, Rollen-Zuweisung), `/admin/roles` (Liste, Neu, Löschen; Klick → `/admin/roles/{id}` mit `PermissionMatrix`, für System-Rollen `ReadOnly`), `/admin/groups` (Liste, Neu, Mitglieder, Rollen).

- [ ] **Step 1: Failing tests**

File: coworkee/tests/Coworkee.Client.Blazor.Tests/PermissionMatrixTests.cs
```csharp
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Identity;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class PermissionMatrixTests : ClientTestBase
{
    private readonly Guid _role = Guid.CreateVersion7();

    public PermissionMatrixTests()
    {
        Api.GetPermissionDefinitionsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new PermissionGroupDto("Identity", "Identity",
            [
                new PermissionDto("Identity.Users.View", "View users", []),
                new PermissionDto("Identity.Users.Manage", "Manage users", ["Identity.Users.View"]),
            ]),
        ]);
        Api.GetGrantsAsync(PermissionProviderType.Role, _role, Arg.Any<CancellationToken>()).Returns(["Identity.Users.View"]);
    }

    [Fact]
    public void Shows_current_grants_as_checked()
    {
        var matrix = Render<PermissionMatrix>(p => p.Add(m => m.ProviderType, PermissionProviderType.Role).Add(m => m.ProviderKey, _role));

        matrix.WaitForAssertion(() => matrix.Find("[data-permission='Identity.Users.View'] input").HasAttribute("checked").ShouldBeTrue());
        matrix.Find("[data-permission='Identity.Users.Manage'] input").HasAttribute("checked").ShouldBeFalse();
    }

    [Fact]
    public async Task Saves_exactly_the_checked_permissions()
    {
        var matrix = Render<PermissionMatrix>(p => p.Add(m => m.ProviderType, PermissionProviderType.Role).Add(m => m.ProviderKey, _role));
        matrix.WaitForElement("[data-permission='Identity.Users.Manage'] input").Change(true);
        matrix.Find("[data-permission='Identity.Users.View'] input").Change(false);

        await matrix.Find("[data-testid='save-permissions']").ClickAsync(new());

        await Api.Received(1).SetGrantsAsync(PermissionProviderType.Role, _role,
            Arg.Is<IReadOnlyList<string>>(names => names.SequenceEqual(new[] { "Identity.Users.Manage" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Read_only_matrix_has_no_save_button()
    {
        var matrix = Render<PermissionMatrix>(p => p.Add(m => m.ProviderType, PermissionProviderType.Role).Add(m => m.ProviderKey, _role).Add(m => m.ReadOnly, true));

        matrix.WaitForElement("[data-permission='Identity.Users.View']");
        matrix.FindAll("[data-testid='save-permissions']").ShouldBeEmpty();
    }
}
```

File: coworkee/tests/Coworkee.Client.Blazor.Tests/ResourcePermissionsPanelTests.cs
```csharp
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ResourcePermissionsPanelTests : ClientTestBase
{
    private readonly Guid _folder = Guid.CreateVersion7();
    private readonly Guid _entry = Guid.CreateVersion7();
    private readonly Guid _user = Guid.CreateVersion7();
    private readonly Guid _role = Guid.CreateVersion7();

    public ResourcePermissionsPanelTests()
    {
        Api.GetResourcePermissionsAsync("Folder", _folder, Arg.Any<CancellationToken>()).Returns([new ResourcePermissionDto(_entry, PrincipalType.User, _user, _role)]);
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([new RoleDto(_role, "Editor", null, false)]);
        Api.GetUsersAsync(Arg.Any<PageRequest>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<UserDto>([new UserDto(_user, "ann@acme.test", "ann@acme.test", "Ann", null, true, [])], 1, 1, 200));
        Api.GetGroupsAsync(Arg.Any<PageRequest>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<GroupDto>([], 0, 1, 200));
    }

    [Fact]
    public void Lists_entries_with_principal_and_role_names()
    {
        var panel = Render<ResourcePermissionsPanel>(p => p.Add(x => x.ResourceType, "Folder").Add(x => x.ResourceId, _folder));

        panel.WaitForAssertion(() => panel.Markup.ShouldContain("ann@acme.test"));
        panel.Markup.ShouldContain("Editor");
    }

    [Fact]
    public async Task Revoke_calls_the_api_for_that_entry()
    {
        var panel = Render<ResourcePermissionsPanel>(p => p.Add(x => x.ResourceType, "Folder").Add(x => x.ResourceId, _folder));

        await panel.WaitForElement($"[data-testid='revoke-{_entry}']").ClickAsync(new());

        await Api.Received(1).RevokeResourcePermissionAsync("Folder", _folder, _entry, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run, expect FAIL** – Komponenten fehlen.

- [ ] **Step 3: Implementation**

File: coworkee/src/Coworkee.Client.Blazor/Components/PermissionMatrix.razor
```razor
@inject ICoworkeeApi Api
@inject ISnackbar Snackbar

@if (_groups is null)
{
    <MudProgressLinear Indeterminate="true" />
}
else
{
    @foreach (var group in _groups)
    {
        <MudText Typo="Typo.overline" Class="mt-4 d-block">@group.DisplayName</MudText>
        <MudSimpleTable Dense="true" Hover="true" Bordered="false" Class="mb-2">
            <tbody>
                @foreach (var permission in group.Permissions)
                {
                    <tr data-permission="@permission.Name">
                        <td style="width:48px">
                            <input type="checkbox" checked="@_selected.Contains(permission.Name)" disabled="@ReadOnly" aria-label="@permission.DisplayName"
                                   @onchange="e => Toggle(permission.Name, e.Value is true)" />
                        </td>
                        <td>@permission.DisplayName</td>
                        <td><MudText Typo="Typo.caption">@permission.Name</MudText></td>
                        <td>
                            @if (permission.Implies.Count > 0)
                            {
                                <MudText Typo="Typo.caption">includes @string.Join(", ", permission.Implies)</MudText>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </MudSimpleTable>
    }

    @if (!ReadOnly)
    {
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="SaveAsync" Disabled="_saving" data-testid="save-permissions">Save</MudButton>
    }
}

@code {
    private IReadOnlyList<PermissionGroupDto>? _groups;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private bool _saving;

    [Parameter, EditorRequired] public PermissionProviderType ProviderType { get; set; }

    [Parameter, EditorRequired] public Guid ProviderKey { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        var definitions = Api.GetPermissionDefinitionsAsync();
        var grants = ReadOnly ? Task.FromResult<IReadOnlyList<string>>([]) : Api.GetGrantsAsync(ProviderType, ProviderKey);
        _groups = await definitions;
        _selected.Clear();
        _selected.UnionWith(await grants);
    }

    private void Toggle(string name, bool on)
    {
        if (on)
        {
            _selected.Add(name);
        }
        else
        {
            _selected.Remove(name);
        }
    }

    private async Task SaveAsync()
    {
        _saving = true;
        try
        {
            var ordered = _groups!.SelectMany(g => g.Permissions).Select(p => p.Name).Where(_selected.Contains).ToList();
            await Api.SetGrantsAsync(ProviderType, ProviderKey, ordered);
            Snackbar.Add("Permissions saved.", Severity.Success);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
        finally
        {
            _saving = false;
        }
    }
}
```
Read-only (system role): grants are not loaded because `GET /permissions/grants` for the `admin` role answers with an empty list anyway; the matrix shows every checkbox disabled and unchecked plus, on the role page, a note "Administrators have every permission".

File: coworkee/src/Coworkee.Client.Blazor/Components/ResourcePermissionsPanel.razor
```razor
@inject ICoworkeeApi Api
@inject ISnackbar Snackbar

<MudPaper Outlined="true" Class="pa-4">
    <MudText Typo="Typo.h6">Permissions</MudText>
    @if (_entries is null)
    {
        <MudProgressLinear Indeterminate="true" />
    }
    else
    {
        <MudSimpleTable Dense="true">
            <thead><tr><th>Who</th><th>Role</th><th></th></tr></thead>
            <tbody>
                @foreach (var entry in _entries)
                {
                    <tr>
                        <td>@PrincipalName(entry)</td>
                        <td>@(_roles.GetValueOrDefault(entry.RoleId) ?? "?")</td>
                        <td style="text-align:right">
                            <MudIconButton Icon="@Icons.Material.Outlined.Delete" Size="Size.Small" aria-label="Revoke"
                                           OnClick="() => RevokeAsync(entry)" data-testid="@($"revoke-{entry.Id}")" />
                        </td>
                    </tr>
                }
            </tbody>
        </MudSimpleTable>

        <MudStack Row="true" AlignItems="AlignItems.End" Class="mt-4">
            <MudSelect T="string" Label="User or group" @bind-Value="_principal" Dense="true">
                @foreach (var user in _users)
                {
                    <MudSelectItem Value="@($"User:{user.Key}")">@user.Value</MudSelectItem>
                }
                @foreach (var group in _groups)
                {
                    <MudSelectItem Value="@($"Group:{group.Key}")">@group.Value (group)</MudSelectItem>
                }
            </MudSelect>
            <MudSelect T="Guid" Label="Role" @bind-Value="_role" Dense="true">
                @foreach (var role in _roles)
                {
                    <MudSelectItem Value="@role.Key">@role.Value</MudSelectItem>
                }
            </MudSelect>
            <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="GrantAsync" Disabled="@(_principal is null || _role == Guid.Empty)">Grant</MudButton>
        </MudStack>
    }
</MudPaper>

@code {
    private IReadOnlyList<ResourcePermissionDto>? _entries;
    private Dictionary<Guid, string> _roles = [];
    private Dictionary<Guid, string> _users = [];
    private Dictionary<Guid, string> _groups = [];
    private string? _principal;
    private Guid _role;

    [Parameter, EditorRequired] public string ResourceType { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid ResourceId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        var roles = Api.GetRolesAsync();
        var users = Api.GetUsersAsync(new PageRequest(1, 200));
        var groups = Api.GetGroupsAsync(new PageRequest(1, 200));
        _roles = (await roles).Where(r => !r.IsSystem).ToDictionary(r => r.Id, r => r.Name);
        _users = (await users).Items.ToDictionary(u => u.Id, u => u.Email);
        _groups = (await groups).Items.ToDictionary(g => g.Id, g => g.Name);
        await ReloadAsync();
    }

    private async Task ReloadAsync() => _entries = await Api.GetResourcePermissionsAsync(ResourceType, ResourceId);

    private string PrincipalName(ResourcePermissionDto entry) => entry.PrincipalType == PrincipalType.User
        ? _users.GetValueOrDefault(entry.PrincipalId) ?? entry.PrincipalId.ToString()
        : (_groups.GetValueOrDefault(entry.PrincipalId) ?? entry.PrincipalId.ToString()) + " (group)";

    private async Task GrantAsync()
    {
        var parts = _principal!.Split(':');
        try
        {
            await Api.GrantResourcePermissionAsync(ResourceType, ResourceId, new GrantResourcePermissionRequest(Enum.Parse<PrincipalType>(parts[0]), Guid.Parse(parts[1]), _role));
            await ReloadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task RevokeAsync(ResourcePermissionDto entry)
    {
        try
        {
            await Api.RevokeResourcePermissionAsync(ResourceType, ResourceId, entry.Id);
            await ReloadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }
}
```

File: coworkee/src/Coworkee.Client.Blazor/Pages/Admin/Users.razor
```razor
@page "/admin/users"
@attribute [Authorize(Policy = "perm:" + IdentityPermissions.Users.View)]
@inject ICoworkeeApi Api
@inject ISnackbar Snackbar

<PageTitle>Users</PageTitle>
<MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-4">
    <MudText Typo="Typo.h4">Users</MudText>
    <MudSpacer />
    <MudTextField T="string" @bind-Value="_search" Placeholder="Search" Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Outlined.Search"
                  Immediate="true" DebounceInterval="300" OnDebounceIntervalElapsed="_ => _table!.ReloadServerData()" />
    <PermissionView Permission="@IdentityPermissions.Users.Manage">
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="() => _creating = true">New user</MudButton>
    </PermissionView>
</MudStack>

@if (_creating)
{
    <MudPaper Outlined="true" Class="pa-4 mb-4">
        <MudStack Row="true" Wrap="Wrap.Wrap">
            <MudTextField @bind-Value="_new.Email" Label="Email" InputType="InputType.Email" Required="true" />
            <MudTextField @bind-Value="_new.FirstName" Label="First name" />
            <MudTextField @bind-Value="_new.LastName" Label="Last name" />
            <MudTextField @bind-Value="_new.Password" Label="Initial password" InputType="InputType.Password" Required="true" />
        </MudStack>
        <MudStack Row="true" Class="mt-3">
            <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="CreateAsync">Create</MudButton>
            <MudButton OnClick="() => _creating = false">Cancel</MudButton>
        </MudStack>
    </MudPaper>
}

<MudTable @ref="_table" T="UserDto" ServerData="LoadAsync" Dense="true" Hover="true" RowsPerPage="25">
    <HeaderContent>
        <MudTh>Email</MudTh><MudTh>Name</MudTh><MudTh>Roles</MudTh><MudTh>Active</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd>@context.Email</MudTd>
        <MudTd>@context.FirstName @context.LastName</MudTd>
        <MudTd>
            <PermissionView Permission="@IdentityPermissions.Users.Manage">
                <ChildContent>
                    <MudSelect T="Guid" MultiSelection="true" Dense="true" SelectedValues="@context.Roles.Select(r => r.Id)"
                               SelectedValuesChanged="ids => SetRolesAsync(context, ids)" ToStringFunc="RoleName">
                        @foreach (var role in _roles)
                        {
                            <MudSelectItem Value="@role.Id">@role.Name</MudSelectItem>
                        }
                    </MudSelect>
                </ChildContent>
                <NotAuthorized>@string.Join(", ", context.Roles.Select(r => r.Name))</NotAuthorized>
            </PermissionView>
        </MudTd>
        <MudTd>
            <MudSwitch T="bool" Value="@context.IsActive" ValueChanged="v => SetActiveAsync(context, v)" Color="Color.Secondary" />
        </MudTd>
    </RowTemplate>
    <PagerContent><MudTablePager /></PagerContent>
</MudTable>

@code {
    private MudTable<UserDto>? _table;
    private string? _search;
    private bool _creating;
    private NewUser _new = new();
    private IReadOnlyList<RoleDto> _roles = [];

    protected override async Task OnInitializedAsync() => _roles = await Api.GetRolesAsync();

    private async Task<TableData<UserDto>> LoadAsync(TableState state, CancellationToken cancellationToken)
    {
        var page = await Api.GetUsersAsync(new PageRequest(state.Page + 1, state.PageSize, _search), cancellationToken);
        return new TableData<UserDto> { Items = page.Items, TotalItems = page.TotalCount };
    }

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private async Task CreateAsync() => await RunAsync(async () =>
    {
        await Api.CreateUserAsync(new CreateUserRequest(_new.Email, _new.Password, _new.FirstName, _new.LastName));
        _new = new NewUser();
        _creating = false;
    });

    private Task SetRolesAsync(UserDto user, IEnumerable<Guid> ids) => RunAsync(() => Api.SetUserRolesAsync(user.Id, ids.ToList()));

    private Task SetActiveAsync(UserDto user, bool active) => RunAsync(() => Api.UpdateUserAsync(user.Id, new UpdateUserRequest(user.FirstName, user.LastName, active)));

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message, Severity.Error);
        }

        await _table!.ReloadServerData();
    }

    private sealed class NewUser
    {
        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string? FirstName { get; set; }

        public string? LastName { get; set; }
    }
}
```

File: coworkee/src/Coworkee.Client.Blazor/Pages/Admin/Roles.razor
```razor
@page "/admin/roles"
@attribute [Authorize(Policy = "perm:" + IdentityPermissions.Roles.View)]
@inject ICoworkeeApi Api
@inject ISnackbar Snackbar

<PageTitle>Roles</PageTitle>
<MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-4">
    <MudText Typo="Typo.h4">Roles</MudText>
    <MudSpacer />
    <PermissionView Permission="@IdentityPermissions.Roles.Manage">
        <MudTextField @bind-Value="_name" Placeholder="New role name" />
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="CreateAsync" Disabled="@string.IsNullOrWhiteSpace(_name)">Add role</MudButton>
    </PermissionView>
</MudStack>

<MudSimpleTable Hover="true" Dense="true">
    <thead><tr><th>Name</th><th>Description</th><th></th></tr></thead>
    <tbody>
        @foreach (var role in _roles)
        {
            <tr>
                <td><MudLink Href="@($"/admin/roles/{role.Id}")">@role.Name</MudLink> @if (role.IsSystem) { <MudChip T="string" Size="Size.Small">system</MudChip> }</td>
                <td>@role.Description</td>
                <td style="text-align:right">
                    @if (!role.IsSystem)
                    {
                        <PermissionView Permission="@IdentityPermissions.Roles.Manage">
                            <MudIconButton Icon="@Icons.Material.Outlined.Delete" Size="Size.Small" aria-label="Delete role" OnClick="() => DeleteAsync(role)" />
                        </PermissionView>
                    }
                </td>
            </tr>
        }
    </tbody>
</MudSimpleTable>

@code {
    private IReadOnlyList<RoleDto> _roles = [];
    private string? _name;

    protected override async Task OnInitializedAsync() => _roles = await Api.GetRolesAsync();

    private async Task CreateAsync()
    {
        try
        {
            await Api.CreateRoleAsync(new RoleRequest(_name!, null));
            _name = null;
            _roles = await Api.GetRolesAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task DeleteAsync(RoleDto role)
    {
        try
        {
            await Api.DeleteRoleAsync(role.Id);
            _roles = await Api.GetRolesAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }
}
```

File: coworkee/src/Coworkee.Client.Blazor/Pages/Admin/RoleDetail.razor
```razor
@page "/admin/roles/{Id:guid}"
@attribute [Authorize(Policy = "perm:" + IdentityPermissions.Roles.View)]
@inject ICoworkeeApi Api

<PageTitle>@(_role?.Name ?? "Role")</PageTitle>
@if (_role is not null)
{
    <MudText Typo="Typo.h4">@_role.Name</MudText>
    <MudText Typo="Typo.body2" Class="mb-4">@_role.Description</MudText>
    @if (_role.IsSystem && _role.Name == "admin")
    {
        <MudAlert Severity="Severity.Info" Class="mb-4">Administrators have every permission.</MudAlert>
    }

    <PermissionView Permission="@IdentityPermissions.Permissions.Manage">
        <ChildContent><PermissionMatrix ProviderType="PermissionProviderType.Role" ProviderKey="_role.Id" ReadOnly="_role.IsSystem" /></ChildContent>
        <NotAuthorized><PermissionMatrix ProviderType="PermissionProviderType.Role" ProviderKey="_role.Id" ReadOnly="true" /></NotAuthorized>
    </PermissionView>
}

@code {
    private RoleDto? _role;

    [Parameter] public Guid Id { get; set; }

    protected override async Task OnParametersSetAsync() => _role = (await Api.GetRolesAsync()).FirstOrDefault(r => r.Id == Id);
}
```

File: coworkee/src/Coworkee.Client.Blazor/Pages/Admin/Groups.razor
```razor
@page "/admin/groups"
@attribute [Authorize(Policy = "perm:" + IdentityPermissions.Groups.View)]
@inject ICoworkeeApi Api
@inject ISnackbar Snackbar

<PageTitle>Groups</PageTitle>
<MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-4">
    <MudText Typo="Typo.h4">Groups</MudText>
    <MudSpacer />
    <PermissionView Permission="@IdentityPermissions.Groups.Manage">
        <MudTextField @bind-Value="_name" Placeholder="New group name" />
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="CreateAsync" Disabled="@string.IsNullOrWhiteSpace(_name)">Add group</MudButton>
    </PermissionView>
</MudStack>

<MudSimpleTable Dense="true" Hover="true">
    <thead><tr><th>Name</th><th>Members</th><th>Roles</th><th></th></tr></thead>
    <tbody>
        @foreach (var group in _groups)
        {
            <tr>
                <td>@group.Name</td>
                <td>
                    <MudSelect T="Guid" MultiSelection="true" Dense="true" SelectedValues="group.MemberIds" ToStringFunc="UserName"
                               SelectedValuesChanged="ids => RunAsync(() => Api.SetGroupMembersAsync(group.Id, ids.ToList()))">
                        @foreach (var user in _users)
                        {
                            <MudSelectItem Value="@user.Id">@user.Email</MudSelectItem>
                        }
                    </MudSelect>
                </td>
                <td>
                    <MudSelect T="Guid" MultiSelection="true" Dense="true" SelectedValues="group.RoleIds" ToStringFunc="RoleName"
                               SelectedValuesChanged="ids => RunAsync(() => Api.SetGroupRolesAsync(group.Id, ids.ToList()))">
                        @foreach (var role in _roles.Where(r => !r.IsSystem))
                        {
                            <MudSelectItem Value="@role.Id">@role.Name</MudSelectItem>
                        }
                    </MudSelect>
                </td>
                <td style="text-align:right">
                    <MudIconButton Icon="@Icons.Material.Outlined.Delete" Size="Size.Small" aria-label="Delete group" OnClick="() => RunAsync(() => Api.DeleteGroupAsync(group.Id))" />
                </td>
            </tr>
        }
    </tbody>
</MudSimpleTable>

@code {
    private IReadOnlyList<GroupDto> _groups = [];
    private IReadOnlyList<UserDto> _users = [];
    private IReadOnlyList<RoleDto> _roles = [];
    private string? _name;

    protected override async Task OnInitializedAsync()
    {
        _users = (await Api.GetUsersAsync(new PageRequest(1, 200))).Items;
        _roles = await Api.GetRolesAsync();
        await ReloadAsync();
    }

    private async Task ReloadAsync() => _groups = (await Api.GetGroupsAsync(new PageRequest(1, 200))).Items;

    private string UserName(Guid id) => _users.FirstOrDefault(u => u.Id == id)?.Email ?? string.Empty;

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;

    private Task CreateAsync() => RunAsync(async () =>
    {
        await Api.CreateGroupAsync(new GroupRequest(_name!, null));
        _name = null;
    });

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message, Severity.Error);
        }

        await ReloadAsync();
    }
}
```

- [ ] **Step 4: Run, expect PASS** – `dotnet test --project tests/Coworkee.Client.Blazor.Tests` → 8 passed; build without warnings (MudBlazor 9 API deviations are fixed against compiler output and ledgered).
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat(client): admin pages, permission matrix and resource permissions panel"`

---

### Task 5: Setup-Wizard

**Files:** Create `coworkee/src/Coworkee.Client.Blazor/Pages/Setup.razor`; Test `coworkee/tests/Coworkee.Client.Blazor.Tests/SetupWizardTests.cs`

**Interfaces:** Seite `/setup` (anonym): Schritt 1 Setup-Token (`data-testid="setup-token"`), Schritt 2 Organisation (`tenant-name`), Schritt 3 Administrator (`admin-email`, `admin-password`, `admin-first`, `admin-last`), Weiter-Button `data-testid="next"`, Abschluss-Button `data-testid="finish"` → `CompleteSetupAsync`; bei Erfolg Hinweis und Link „Sign in“ (`/bff/login`); bei 403 Fehlertext `data-testid="setup-error"` und Rücksprung auf Schritt 1; bei 400 Feldfehler.

- [ ] **Step 1: Failing tests**

File: coworkee/tests/Coworkee.Client.Blazor.Tests/SetupWizardTests.cs
```csharp
using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Contracts.Identity;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SetupWizardTests : ClientTestBase
{
    [Fact]
    public async Task Completes_setup_with_entered_values()
    {
        Api.CompleteSetupAsync(Arg.Any<CompleteSetupRequest>(), Arg.Any<CancellationToken>()).Returns(new SetupResultDto(Guid.CreateVersion7(), Guid.CreateVersion7()));
        var wizard = Render<Setup>();

        await FillAsync(wizard, "token-123");

        await Api.Received(1).CompleteSetupAsync(
            Arg.Is<CompleteSetupRequest>(r => r.SetupToken == "token-123" && r.TenantName == "Acme" && r.AdminEmail == "ada@acme.test" && r.AdminPassword == "Admin#12345"),
            Arg.Any<CancellationToken>());
        wizard.WaitForAssertion(() => wizard.Markup.ShouldContain("/bff/login"));
    }

    [Fact]
    public async Task Wrong_token_shows_error_and_returns_to_first_step()
    {
        Api.CompleteSetupAsync(Arg.Any<CompleteSetupRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ApiException(403, "setup.token_invalid", null));
        var wizard = Render<Setup>();

        await FillAsync(wizard, "wrong");

        wizard.WaitForAssertion(() => wizard.Find("[data-testid='setup-error']").TextContent.ShouldNotBeNullOrWhiteSpace());
        wizard.FindAll("[data-testid='setup-token']").ShouldNotBeEmpty();
    }

    private static async Task FillAsync(IRenderedComponent<Setup> wizard, string token)
    {
        wizard.Find("[data-testid='setup-token'] input").Change(token);
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        wizard.Find("[data-testid='tenant-name'] input").Change("Acme");
        await wizard.Find("[data-testid='next']").ClickAsync(new());
        wizard.Find("[data-testid='admin-email'] input").Change("ada@acme.test");
        wizard.Find("[data-testid='admin-password'] input").Change("Admin#12345");
        await wizard.Find("[data-testid='finish']").ClickAsync(new());
    }
}
```

- [ ] **Step 2: Run, expect FAIL** – `Setup` fehlt.

- [ ] **Step 3: Implementation**

File: coworkee/src/Coworkee.Client.Blazor/Pages/Setup.razor
```razor
@page "/setup"
@attribute [AllowAnonymous]
@inject ICoworkeeApi Api
@inject CoworkeeClientOptions Options

<PageTitle>Set up @Options.AppTitle</PageTitle>
<MudPaper Outlined="true" Class="pa-6 mx-auto" Style="max-width:560px">
    <MudText Typo="Typo.overline">Step @(Math.Min(_step + 1, 3)) of 3</MudText>
    <MudText Typo="Typo.h4" Class="mb-4">Set up @Options.AppTitle</MudText>

    @if (_done)
    {
        <MudAlert Severity="Severity.Success" Class="mb-4">The system is ready. Sign in with the administrator account.</MudAlert>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" Href="@($"{Options.LoginPath}?returnUrl=/admin/users")">Sign in</MudButton>
    }
    else
    {
        @switch (_step)
        {
            case 0:
                <MudText Class="mb-2">Enter the setup token. It is printed in the API log on first start, or configured as <code>Coworkee:SetupToken</code>.</MudText>
                <MudTextField @bind-Value="_request.SetupToken" Label="Setup token" data-testid="setup-token" Required="true" />
                break;
            case 1:
                <MudTextField @bind-Value="_request.TenantName" Label="Organisation name" data-testid="tenant-name" Required="true" />
                break;
            default:
                <MudTextField @bind-Value="_request.AdminEmail" Label="Administrator email" InputType="InputType.Email" data-testid="admin-email" Required="true"
                              Error="@HasError("AdminEmail")" ErrorText="@ErrorFor("AdminEmail")" />
                <MudTextField @bind-Value="_request.AdminPassword" Label="Password" InputType="InputType.Password" data-testid="admin-password" Required="true"
                              Error="@HasError("Password")" ErrorText="@ErrorFor("Password")" />
                <MudTextField @bind-Value="_request.AdminFirstName" Label="First name" data-testid="admin-first" />
                <MudTextField @bind-Value="_request.AdminLastName" Label="Last name" data-testid="admin-last" />
                break;
        }

        @if (_error is not null)
        {
            <MudAlert Severity="Severity.Error" Class="mt-4" data-testid="setup-error">@_error</MudAlert>
        }

        <MudStack Row="true" Class="mt-6">
            @if (_step > 0)
            {
                <MudButton OnClick="() => _step--">Back</MudButton>
            }
            <MudSpacer />
            @if (_step < 2)
            {
                <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="() => _step++" data-testid="next">Next</MudButton>
            }
            else
            {
                <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="FinishAsync" Disabled="_busy" data-testid="finish">Finish setup</MudButton>
            }
        </MudStack>
    }
</MudPaper>

@code {
    private readonly SetupInput _request = new();
    private int _step;
    private bool _busy;
    private bool _done;
    private string? _error;
    private IReadOnlyDictionary<string, string[]>? _errors;

    private bool HasError(string field) => _errors?.Keys.Any(k => k.EndsWith(field, StringComparison.OrdinalIgnoreCase)) == true;

    private string? ErrorFor(string field) => _errors?.FirstOrDefault(e => e.Key.EndsWith(field, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();

    private async Task FinishAsync()
    {
        _busy = true;
        _error = null;
        _errors = null;
        try
        {
            await Api.CompleteSetupAsync(new CompleteSetupRequest(_request.SetupToken, _request.TenantName, _request.AdminEmail, _request.AdminPassword, _request.AdminFirstName, _request.AdminLastName));
            _done = true;
        }
        catch (ApiException exception) when (exception.Status == 403)
        {
            _error = "The setup token is not valid.";
            _step = 0;
        }
        catch (ApiException exception) when (exception.Status == 409)
        {
            _error = "The system is already set up.";
        }
        catch (ApiException exception)
        {
            _errors = exception.Errors;
            _error = exception.Errors is null ? exception.Message : "Please correct the highlighted fields.";
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed class SetupInput
    {
        public string SetupToken { get; set; } = string.Empty;

        public string TenantName { get; set; } = string.Empty;

        public string AdminEmail { get; set; } = string.Empty;

        public string AdminPassword { get; set; } = string.Empty;

        public string? AdminFirstName { get; set; }

        public string? AdminLastName { get; set; }
    }
}
```

- [ ] **Step 4: Run, expect PASS** – `dotnet test --solution Coworkee.slnx` (Client 10).
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat(client): setup wizard"` and `pwsh build/pack-local.ps1`.

---

### Task 6: Shareme.Auth, Shareme.Web, Shareme.Web.Client, AppHost

**Files:** Create `shareme/src/Shareme.Auth/**`, `shareme/src/Shareme.Web/**`, `shareme/src/Shareme.Web.Client/**`; Modify `shareme/src/Shareme.AppHost/*`, `shareme/Directory.Packages.props`, `shareme/src/Shareme.Infrastructure/*` (OpenIddict-Tabellen), `shareme/tests/Shareme.AppHost.Tests/AppHostTests.cs`; Migration `AuthServer`

**Interfaces:**
- AppHost-Ressourcen: `shareme-auth` (Projekt, wartet auf Migrations, `Coworkee__Auth__Clients__0__*` für `shareme-web` mit Redirect `https://localhost:{web}/signin-oidc` – Aspire-Endpunktausdrücke), `shareme-api` (`Auth__Authority` = Auth-Endpunkt), `shareme-web` (`Coworkee__Bff__Authority`, `Coworkee__Bff__ClientId=shareme-web`, `Coworkee__Bff__ApiAddress=https+http://shareme-api`, `Coworkee__Bff__Scopes__0=shareme_api`).
- Smoke-Test: Web liefert `/` (200), Auth liefert Discovery (200), Web `/bff/user` → `isAuthenticated=false`.

- [ ] **Step 1: Failing test** – `AppHostTests` ergänzen:
```csharp
    [Fact]
    public async Task Web_and_auth_start_and_serve()
    {
        var ct = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Shareme_AppHost>([$"--{CoworkeeInfrastructureExtensions.EphemeralSetting}=true"], ct);
        await using var app = await appHost.BuildAsync(ct);
        await app.StartAsync(ct);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("shareme-web", ct).WaitAsync(TimeSpan.FromMinutes(5), ct);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("shareme-auth", ct).WaitAsync(TimeSpan.FromMinutes(5), ct);

        using var web = app.CreateHttpClient("shareme-web");
        (await web.GetAsync("/", ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        (await web.GetStringAsync("/bff/user", ct)).ShouldContain("\"isAuthenticated\":false");
        using var auth = app.CreateHttpClient("shareme-auth");
        (await auth.GetAsync("/.well-known/openid-configuration", ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }
```

- [ ] **Step 2: Run, expect FAIL** – Ressourcen fehlen.

- [ ] **Step 3: Implementation**

`shareme/Directory.Packages.props` ergänzen: `Coworkee.AuthServer`, `Coworkee.Bff`, `Coworkee.Client.Blazor` (`$(CoworkeeVersion)`), `Microsoft.AspNetCore.Components.WebAssembly` 10.0.12, `Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.12.

`Shareme.Infrastructure.csproj`: `<CoworkeePackage Include="Coworkee.AuthServer" />`; `ShareMeInfrastructureModule` bleibt unverändert, Auth-Module hängt nur am Auth-Host. Die Migration braucht aber das OpenIddict-Modell: der Design-Time-Factory-Root wird `ShareMeDesignModule` mit `[DependsOn(typeof(ShareMeInfrastructureModule), typeof(CoworkeeAuthServerModule))]`, und der Migrations-Worker nutzt denselben Root, damit `auth`-Tabellen migriert werden.

File: shareme/src/Shareme.Infrastructure/ShareMeDesignModule.cs
```csharp
using Coworkee.AuthServer;
using Coworkee.Core.Modularity;

namespace Shareme.Infrastructure;

[DependsOn(typeof(ShareMeInfrastructureModule), typeof(CoworkeeAuthServerModule))]
public sealed class ShareMeDatabaseModule : CoworkeeModule;
```
`ShareMeDbContextDesignFactory` and `Shareme.Migrations/Program.cs` use `ShareMeDatabaseModule` instead of `ShareMeInfrastructureModule`. Shareme.Api keeps `ShareMeInfrastructureModule`; since the API model then lacks the OpenIddict entities, the API context must also include them to share one model: `ShareMeApiModule` depends on `ShareMeDatabaseModule`. Consequence: the API also registers the OpenIddict server/validation services; acceptable (no endpoints mapped because `CoworkeeAuthServerModule.ConfigureApplication` maps `/connect/*` and Razor pages). To avoid that, split `CoworkeeAuthServerModule` into `CoworkeeAuthStoreModule` (only `AuthModelContributor` + OpenIddict Core/EF) and `CoworkeeAuthServerModule` (depends on the store module, adds server, cookies, pages, endpoints). Shareme.Api and Migrations depend on the store module, Shareme.Auth on the server module. Implement this split in Coworkee in this task (move `AuthModelContributor` and `AddOpenIddict().AddCore(...)` into `CoworkeeAuthStoreModule`, re-pack).

File: shareme/src/Shareme.Auth/Shareme.Auth.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="..\Shareme.Infrastructure\Shareme.Infrastructure.csproj" />
    <ProjectReference Include="..\Shareme.ServiceDefaults\Shareme.ServiceDefaults.csproj" />
    <CoworkeePackage Include="Coworkee.AuthServer" />
  </ItemGroup>
</Project>
```

File: shareme/src/Shareme.Auth/Program.cs
```csharp
using Coworkee.AspNetCore;
using Coworkee.AuthServer;
using Coworkee.Core.Modularity;
using Shareme.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCoworkee<ShareMeAuthModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();

[DependsOn(typeof(ShareMeInfrastructureModule), typeof(CoworkeeAuthServerModule))]
internal sealed class ShareMeAuthModule : CoworkeeModule;
```

File: shareme/src/Shareme.Web.Client/Shareme.Web.Client.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <NoDefaultLaunchSettingsFile>true</NoDefaultLaunchSettingsFile>
    <StaticWebAssetProjectMode>Default</StaticWebAssetProjectMode>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" />
    <CoworkeePackage Include="Coworkee.Client.Blazor" />
  </ItemGroup>
</Project>
```

File: shareme/src/Shareme.Web.Client/Program.cs
```csharp
using Coworkee.Client.Blazor;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddCoworkeeClient(new Uri(builder.HostEnvironment.BaseAddress), options => options.AppTitle = "Sharemee");
await builder.Build().RunAsync();
```

File: shareme/src/Shareme.Web.Client/_Imports.razor
```razor
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Authorization
@using MudBlazor
@using Coworkee.Client.Blazor.Components
```

File: shareme/src/Shareme.Web.Client/Routes.razor
```razor
<Router AppAssembly="typeof(Program).Assembly" AdditionalAssemblies="new[] { typeof(Coworkee.Client.Blazor.CoworkeeClientOptions).Assembly }">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(CoworkeeLayout)">
            <NotAuthorized>
                <MudAlert Severity="Severity.Warning">You need to sign in, or you lack the permission for this page.</MudAlert>
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
    <NotFound>
        <LayoutView Layout="typeof(CoworkeeLayout)"><MudText>Not found.</MudText></LayoutView>
    </NotFound>
</Router>
```

File: shareme/src/Shareme.Web.Client/Pages/Home.razor
```razor
@page "/"
<PageTitle>Sharemee</PageTitle>
<MudText Typo="Typo.h3">Sharemee</MudText>
<MudText Class="mt-2">Assets, documents, knowledge and intranet. The admin area is in the menu.</MudText>
```

File: shareme/src/Shareme.Web/Shareme.Web.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="..\Shareme.Web.Client\Shareme.Web.Client.csproj" />
    <ProjectReference Include="..\Shareme.ServiceDefaults\Shareme.ServiceDefaults.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.Server" />
    <CoworkeePackage Include="Coworkee.Bff" />
  </ItemGroup>
</Project>
```

File: shareme/src/Shareme.Web/Program.cs
```csharp
using Coworkee.Bff;
using Shareme.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCoworkeeBff();
builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapCoworkeeBff();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Shareme.Web.Client._Imports).Assembly, typeof(Coworkee.Client.Blazor.CoworkeeClientOptions).Assembly);
app.Run();
```
`Shareme.Web.Client._Imports` is generated from `_Imports.razor`; use namespace `Shareme.Web.Client`.

File: shareme/src/Shareme.Web/App.razor
```razor
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <base href="/" />
    <title>Sharemee</title>
    <link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
    <link href="_content/MudBlazor.Extensions/mudBlazorExtensions.min.css" rel="stylesheet" />
    <HeadOutlet @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />
</head>
<body>
    <Shareme.Web.Client.Routes @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

File: shareme/src/Shareme.Web/_Imports.razor
```razor
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
```

File: shareme/src/Shareme.AppHost/AppHost.cs
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var infrastructure = builder.AddCoworkeeInfrastructure("shareme");

var migrations = builder.AddProject<Projects.Shareme_Migrations>("shareme-migrations")
    .WithReference(infrastructure.Database)
    .WaitFor(infrastructure.Database);

var auth = builder.AddProject<Projects.Shareme_Auth>("shareme-auth")
    .WithReference(infrastructure.Database)
    .WaitForCompletion(migrations)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var api = builder.AddProject<Projects.Shareme_Api>("shareme-api")
    .WithReference(infrastructure.Database)
    .WaitForCompletion(migrations)
    .WithEnvironment("Auth__Authority", auth.GetEndpoint("https"))
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var web = builder.AddProject<Projects.Shareme_Web>("shareme-web")
    .WithReference(api)
    .WaitFor(auth)
    .WithEnvironment("Coworkee__Bff__Authority", auth.GetEndpoint("https"))
    .WithEnvironment("Coworkee__Bff__ClientId", "shareme-web")
    .WithEnvironment("Coworkee__Bff__ApiAddress", "https+http://shareme-api")
    .WithEnvironment("Coworkee__Bff__Scopes__0", "shareme_api")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

auth.WithEnvironment("Coworkee__Auth__ApiScopes__shareme_api", "shareme_api")
    .WithEnvironment("Coworkee__Auth__Clients__0__ClientId", "shareme-web")
    .WithEnvironment("Coworkee__Auth__Clients__0__DisplayName", "Sharemee")
    .WithEnvironment("Coworkee__Auth__Clients__0__Scopes__0", "shareme_api")
    .WithEnvironment("Coworkee__Auth__Clients__0__RedirectUris__0", ReferenceExpression.Create($"{web.GetEndpoint("https")}/signin-oidc"))
    .WithEnvironment("Coworkee__Auth__Clients__0__PostLogoutRedirectUris__0", ReferenceExpression.Create($"{web.GetEndpoint("https")}/signout-callback-oidc"));

builder.Build().Run();
```
Add project references for `Shareme.Auth` and `Shareme.Web` to `Shareme.AppHost.csproj`.

Migration: `dotnet ef migrations add AuthServer --project src/Shareme.Infrastructure --startup-project src/Shareme.Infrastructure --output-dir Migrations`

- [ ] **Step 4: Run, expect PASS** – `dotnet test --solution Shareme.slnx` → alle grün (AppHost 2).
- [ ] **Step 5: Commit** – `git add -A && git commit -m "feat: shareme auth server, blazor web bff and admin ui"`
