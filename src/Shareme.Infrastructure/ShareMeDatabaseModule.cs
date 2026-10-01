using Coworkee.Auditing;
using Coworkee.AuthServer;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Modularity;
using Coworkee.Mailing;
using Coworkee.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace Shareme.Infrastructure;

[DependsOn(typeof(ShareMeInfrastructureModule), typeof(CoworkeeAuthStoreModule), typeof(CoworkeeMailingModule), typeof(CoworkeeAuditingModule), typeof(CoworkeeThemingModule))]
public sealed class ShareMeDatabaseModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.PostConfigure<BackgroundJobOptions>(options => options.ConnectionStringName = ShareMeInfrastructureModule.ConnectionStringName);
}
