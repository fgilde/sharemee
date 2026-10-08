using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Application.Setup;
using Coworkee.Settings;
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
        context.Services.AddCoworkeeAppConfiguration<Shareme.Contracts.Configuration.SharemeConfiguration>(
            context.Configuration, Shareme.Contracts.Configuration.SharemeConfiguration.Section, Shareme.Contracts.Configuration.SharemeConfiguration.Title);
        context.Services.Configure<SetupGateOptions>(options => options.AllowedPrefixes.Add("/api/v1/system"));
        context.Services.PostConfigure<Coworkee.Contracts.Configuration.BackgroundJobOptions>(options => options.ConnectionStringName = ConnectionStringName);
    }
}
