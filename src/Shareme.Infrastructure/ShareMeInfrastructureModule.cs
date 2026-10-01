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
