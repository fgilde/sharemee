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
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Shareme_AppHost>([$"--{CoworkeeInfrastructureExtensions.EphemeralSetting}=true"], ct);
        await using var app = await appHost.BuildAsync(ct);
        await app.StartAsync(ct);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("shareme-api", ct).WaitAsync(TimeSpan.FromMinutes(5), ct);
        using var client = app.CreateHttpClient("shareme-api");
        var info = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/info", ct);

        info.GetProperty("product").GetString().ShouldBe("Shareme");
    }
}
