var builder = DistributedApplication.CreateBuilder(args);

var infrastructure = builder.AddCoworkeeInfrastructure("shareme");

builder.AddProject<Projects.Shareme_Migrations>("shareme-migrations")
    .WithReference(infrastructure.Database)
    .WaitFor(infrastructure.Database);

builder.Build().Run();
