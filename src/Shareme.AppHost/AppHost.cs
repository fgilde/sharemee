var builder = DistributedApplication.CreateBuilder(args);

var app = builder.AddCoworkeeApp("shareme", options =>
{
    options.DisplayName = "Sharemee";
    options.UseKeycloak(keycloak => keycloak.Users.Add(new KeycloakUser("demo@sharemee.local", "Demo", "User")));
});

app.AddMigrations<Projects.Shareme_Migrations>();
app.AddAuthServer<Projects.Shareme_Auth>();
app.AddApi<Projects.Shareme_Api>();
app.AddWorker<Projects.Shareme_Processing_Host>("processing");
app.AddWeb<Projects.Shareme_Web>();

builder.Build().Run();
