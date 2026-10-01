var builder = DistributedApplication.CreateBuilder(args);

builder.AddCoworkeeInfrastructure("shareme");

builder.Build().Run();
