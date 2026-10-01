using Coworkee.Core.Modularity;
using Microsoft.EntityFrameworkCore;
using Shareme.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<ShareMeInfrastructureModule>(builder.Configuration);
using var host = builder.Build();

await using var scope = host.Services.CreateAsyncScope();
try
{
    await scope.ServiceProvider.GetRequiredService<ShareMeDbContext>().Database.MigrateAsync();
    return 0;
}
catch (Exception exception)
{
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogCritical(exception, "Database migration failed");
    return 1;
}
