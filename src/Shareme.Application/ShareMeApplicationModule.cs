using Coworkee.Application;
using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;

namespace Shareme.Application;

[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class ShareMeApplicationModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddMessagingFromAssembly(typeof(ShareMeApplicationModule).Assembly);
}
