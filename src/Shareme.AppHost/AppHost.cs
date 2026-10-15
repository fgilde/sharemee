using Shareme.Migrations;

var builder = DistributedApplication.CreateBuilder(args);

var app = builder.AddCoworkeeApp("shareme", options =>
{
    options.DisplayName = "Sharemee";
    options.UseKeycloak(keycloak => keycloak.Users.AddRange(DemoUsers.All.Select(u =>
        new KeycloakUser(u.Email, u.FirstName, u.LastName) { Password = u.Password, Roles = u.IsAdmin ? ["Administrator", .. u.Roles ?? []] : u.Roles ?? [] })));
});

// the users of LOGIN.md; tests that walk through the setup wizard pass --Shareme:DemoUsers=false
app.AddMigrations<Projects.Shareme_Migrations>()
    .WithEnvironment("Shareme__DemoUsers", builder.Configuration["Shareme:DemoUsers"] ?? "true");
app.AddAuthServer<Projects.Shareme_Auth>();
app.AddApi<Projects.Shareme_Api>();
app.AddWorker<Projects.Shareme_Processing_Host>("processing");
app.AddWeb<Projects.Shareme_Web>();

builder.Build().Run();
