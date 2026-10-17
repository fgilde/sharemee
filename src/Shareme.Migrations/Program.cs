using Coworkee.Core.Modularity;
using Coworkee.Identity.Setup;
using Shareme.Infrastructure;
using Shareme.Migrations;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<ShareMeDatabaseModule>(builder.Configuration);
builder.AddCoworkeeMigrations<ShareMeDbContext>(options =>
{
    // the app host turns the demo users off for tests that walk through the setup wizard
    options.DevelopmentUsers = builder.Configuration.GetValue("Shareme:DemoUsers", false);
    options.DevelopmentSeed = DemoSeed.Configure;
    options.DeploymentSeed = DemoSeed.ConfigureDeployment;
});
using var host = builder.Build();
return await host.RunCoworkeeMigrationsAsync();
