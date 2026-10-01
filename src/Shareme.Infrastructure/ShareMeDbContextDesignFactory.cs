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
