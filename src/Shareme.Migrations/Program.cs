using Coworkee.Core.Modularity;
using Coworkee.Identity.Setup;
using Microsoft.EntityFrameworkCore;
using Shareme.Infrastructure;
using Shareme.Migrations;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<ShareMeDatabaseModule>(builder.Configuration);
builder.Services.AddCoworkeeIdentitySeed(DemoSeed.Configure);
using var host = builder.Build();

await using var scope = host.Services.CreateAsyncScope();
try
{
    await scope.ServiceProvider.GetRequiredService<ShareMeDbContext>().Database.MigrateAsync();

    // the app host turns the demo users off for tests that walk through the setup wizard
    if (builder.Configuration.GetValue("Shareme:DemoUsers", false))
    {
        await host.Services.SeedCoworkeeIdentityAsync();
    }

    return 0;
}
catch (Exception exception)
{
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogCritical(exception, "Database migration or seed failed");
    return 1;
}
