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

    public static ElasticContainer Elastic { get; } = new();

    public override async ValueTask InitializeAsync()
    {
        await Elastic.StartAsync();
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
        await Elastic.DisposeAsync();
    }
}

/// <summary>A single node Elasticsearch with a small heap and security off.</summary>
public sealed class ElasticContainer : IAsyncDisposable
{
    private readonly DotNet.Testcontainers.Containers.IContainer _container = new DotNet.Testcontainers.Builders.ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.1.4")
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m -XX:TieredStopAtLevel=1")
        .WithEnvironment("cluster.routing.allocation.disk.threshold_enabled", "false")
        .WithPortBinding(9200, true)
        .WithWaitStrategy(DotNet.Testcontainers.Builders.Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9200).ForPath("/_cluster/health")))
        .Build();

    public string Url => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(9200)}";

    public Task StartAsync() => _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

public sealed class ApiFactory(PostgresFixture postgres) : PostgresWebApplicationFactory<Program>(postgres, ShareMeInfrastructureModule.ConnectionStringName)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Coworkee:SetupToken", ApiFixture.SetupToken);
        builder.UseSetting("Coworkee:Jobs:PollingInterval", "00:00:01");
        builder.UseSetting("Shareme:Shares:RequestsPerMinute", "100");
        builder.UseSetting("Shareme:Drm:PurchaseEmails", "licensing@acme.test");

        // the api also serves the processing queue in tests, so renditions appear without the processing host
        builder.UseSetting("Coworkee:Jobs:Queues:0", "default");
        builder.UseSetting("Coworkee:Jobs:Queues:1", "mail");
        builder.UseSetting("Coworkee:Jobs:Queues:2", "processing");
        builder.UseSetting("Coworkee:Jobs:Queues:3", "search");
        builder.UseSetting("ConnectionStrings:elasticsearch", ApiFixture.Elastic.Url);
        builder.UseSetting("Shareme:Search:Alias", "shareme-tests-" + Guid.NewGuid().ToString("N")[..8]);
        builder.UseSetting("Coworkee:Storage:FileSystem:Root", Path.Combine(Path.GetTempPath(), "shareme-api-tests-blobs"));
        builder.ConfigureTestServices(services =>
        {
            services.AddTestAuthentication();
            services.AddHttpClient("Shareme.Ai").ConfigurePrimaryHttpMessageHandler(() => FakeAi.Instance);
        });
    }
}
