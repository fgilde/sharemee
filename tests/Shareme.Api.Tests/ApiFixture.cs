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

    protected override string[] SchemasToExclude => ["hangfire"];

    protected override IReadOnlyList<(string Schema, string Table)> TablesToKeep => [("sm", "NodeTypes"), ("sm", "EdgeTypes"), ("sm", "AssetTypes"), ("sm", "FileTypes")];

    public async Task ResetAllAsync()
    {
        await ResetAsync();
        Factory.Services.GetRequiredService<Coworkee.Identity.Setup.SystemStateCache>().Reset();
        var cache = Factory.Services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();
        await cache.RemoveByTagAsync("coworkee:permissions");
        await cache.RemoveByTagAsync("coworkee:settings");
    }

    public async Task<Coworkee.Contracts.Identity.SetupResultDto> SetupAsync()
    {
        await ResetAllAsync();
        var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(Factory.CreateClient(), "/api/v1/setup/complete",
            new Coworkee.Contracts.Identity.CompleteSetupRequest(SetupToken, "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<Coworkee.Contracts.Identity.SetupResultDto>(response.Content))!;
    }

    public HttpClient As(Guid userId, Guid tenantId) => Factory.CreateClient().AsUser(userId, tenantId);

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
        builder.UseSetting("Coworkee:Jobs:PollingInterval", "00:00:01");

        // the api also serves the processing queue in tests, so renditions appear without the processing host
        builder.UseSetting("Coworkee:Jobs:Queues:0", "default");
        builder.UseSetting("Coworkee:Jobs:Queues:1", "mail");
        builder.UseSetting("Coworkee:Jobs:Queues:2", "processing");
        builder.UseSetting("Coworkee:Storage:FileSystem:Root", Path.Combine(Path.GetTempPath(), "shareme-api-tests-blobs"));
        builder.ConfigureTestServices(services => services.AddTestAuthentication());
    }
}
