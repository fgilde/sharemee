using Coworkee.AspNetCore;
using Coworkee.AuthServer;
using Coworkee.Core.Modularity;
using Shareme.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCoworkee<ShareMeAuthModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();

[DependsOn(typeof(ShareMeInfrastructureModule), typeof(CoworkeeAuthServerModule))]
internal sealed class ShareMeAuthModule : CoworkeeModule;
