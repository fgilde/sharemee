using Coworkee.Core.Modularity;
using Coworkee.Identity.Setup;
using Microsoft.EntityFrameworkCore;
using Shareme.Infrastructure;
using Shareme.Migrations;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<ShareMeDatabaseModule>(builder.Configuration);
// the app host turns the demo users off for tests that walk through the setup wizard; a deployment gets its first administrator
// from the deploy parameters, without them it is set up in the browser with the setup token
var demoUsers = builder.Environment.IsDevelopment() && builder.Configuration.GetValue("Shareme:DemoUsers", false);
var admin = InitialAdmin.From(builder.Configuration);
builder.Services.AddCoworkeeIdentitySeed(demoUsers || admin is null ? DemoSeed.Configure : options => DemoSeed.ConfigureDeployment(options, admin));
using var host = builder.Build();

await using var scope = host.Services.CreateAsyncScope();
try
{
    await scope.ServiceProvider.GetRequiredService<ShareMeDbContext>().Database.MigrateAsync();

    if (demoUsers)
    {
        await host.Services.SeedCoworkeeIdentityAsync();
        // a database set up earlier gets the users and roles added to DemoUsers.cs since, and their passwords again
        await host.Services.SyncDevelopmentUsersAsync();
    }
    else if (admin is not null)
    {
        await host.Services.SeedCoworkeeIdentityAsync();
    }

    // the Keycloak of a deployment gets the clients and the administrator of the app host
    await scope.ServiceProvider.ReconcileKeycloakRealmAsync();

    return 0;
}
catch (Exception exception)
{
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogCritical(exception, "Database migration or seed failed");
    return 1;
}
