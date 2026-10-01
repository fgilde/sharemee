using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Outbox;
using Shareme.Application.System;
using Shareme.Infrastructure;

namespace Shareme.Api;

[DependsOn(typeof(ShareMeInfrastructureModule))]
public sealed class ShareMeApiModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeOutboxProcessing<ShareMeDbContext>();

    public void ConfigureApplication(WebApplication app)
    {
        var system = app.MapGroup("/api/v1/system").WithTags("System");
        system.MapGet("/info", (IDispatcher dispatcher, CancellationToken ct) => dispatcher.SendAsync(new GetSystemInfo(), ct).ToHttpResult())
            .WithName("GetSystemInfo");
    }
}
