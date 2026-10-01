using Coworkee.Core.Modularity;
using Shareme.Infrastructure;
using Shareme.Migrations;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<ShareMeInfrastructureModule>(builder.Configuration);
builder.Services.AddHostedService<MigrationWorker>();
builder.Build().Run();
