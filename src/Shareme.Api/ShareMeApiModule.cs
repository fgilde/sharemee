using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Outbox;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Shareme.Application.System;
using Shareme.Infrastructure;

namespace Shareme.Api;

[DependsOn(typeof(ShareMeDatabaseModule))]
public sealed class ShareMeApiModule : CoworkeeModule, IWebModule
{
    public const string Audience = "shareme_api";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeOutboxProcessing<ShareMeDbContext>();
        context.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var authority = context.Configuration["Auth:Authority"];
                options.Authority = authority;
                options.Audience = Audience;
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = authority?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) != true;
            });
    }

    public void ConfigureApplication(WebApplication app)
    {
        var system = app.MapGroup("/api/v1/system").WithTags("System");
        system.MapGet("/info", (IDispatcher dispatcher, CancellationToken ct) => dispatcher.SendAsync(new GetSystemInfo(), ct).ToHttpResult())
            .WithName("GetSystemInfo");
    }
}
