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
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Shareme_AppHost>([$"--{CoworkeeAppExtensions.EphemeralSetting}=true"], ct);
        var app = await appHost.BuildAsync(ct);
        await using var stop = AppHostDiagnostics.Guard(app);
        await app.StartAsync(ct);

        await AppHostDiagnostics.WaitHealthyAsync(app, ["shareme-api"], ct);
        using var client = app.CreateHttpClient("shareme-api");
        var info = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/info", ct);

        info.GetProperty("product").GetString().ShouldBe("Shareme");
    }

    [Fact]
    public async Task Web_and_auth_start_and_serve()
    {
        var ct = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Shareme_AppHost>([$"--{CoworkeeAppExtensions.EphemeralSetting}=true"], ct);
        var app = await appHost.BuildAsync(ct);
        await using var stop = AppHostDiagnostics.Guard(app);
        await app.StartAsync(ct);
        await AppHostDiagnostics.WaitHealthyAsync(app, ["shareme-web", "shareme-auth"], ct);

        using var web = app.CreateHttpClient("shareme-web");
        (await web.GetAsync("/", ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        (await web.GetStringAsync("/bff/user", ct)).ShouldContain("\"isAuthenticated\":false");
        using var auth = app.CreateHttpClient("shareme-auth");
        (await auth.GetAsync("/.well-known/openid-configuration", ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }
}
