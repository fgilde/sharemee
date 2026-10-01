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
