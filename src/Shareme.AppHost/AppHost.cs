var builder = DistributedApplication.CreateBuilder(args);

var infrastructure = builder.AddCoworkeeInfrastructure("shareme");

var migrations = builder.AddProject<Projects.Shareme_Migrations>("shareme-migrations")
    .WithReference(infrastructure.Database)
    .WaitFor(infrastructure.Database);

var auth = builder.AddProject<Projects.Shareme_Auth>("shareme-auth")
    .WithReference(infrastructure.Database)
    .WaitForCompletion(migrations)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var api = builder.AddProject<Projects.Shareme_Api>("shareme-api")
    .WithReference(infrastructure.Database)
    .WaitForCompletion(migrations)
    .WithEnvironment("Auth__Authority", auth.GetEndpoint("https"))
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

if (builder.Configuration["Shareme:SetupToken"] is { Length: > 0 } setupToken)
{
    api.WithEnvironment("Coworkee__SetupToken", setupToken);
}

var web = builder.AddProject<Projects.Shareme_Web>("shareme-web")
    .WithReference(api)
    .WaitFor(auth)
    .WithEnvironment("Coworkee__Bff__Authority", auth.GetEndpoint("https"))
    .WithEnvironment("Coworkee__Bff__ClientId", "shareme-web")
    .WithEnvironment("Coworkee__Bff__ApiAddress", "https+http://shareme-api")
    .WithEnvironment("Coworkee__Bff__Scopes__0", "shareme_api")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

auth.WithEnvironment("Coworkee__Auth__ApiScopes__shareme_api", "shareme_api")
    .WithEnvironment("Coworkee__Auth__Clients__0__ClientId", "shareme-web")
    .WithEnvironment("Coworkee__Auth__Clients__0__DisplayName", "Sharemee")
    .WithEnvironment("Coworkee__Auth__Clients__0__Scopes__0", "shareme_api")
    .WithEnvironment("Coworkee__Auth__Clients__0__RedirectUris__0", ReferenceExpression.Create($"{web.GetEndpoint("https")}/signin-oidc"))
    .WithEnvironment("Coworkee__Auth__Clients__0__PostLogoutRedirectUris__0", ReferenceExpression.Create($"{web.GetEndpoint("https")}/signout-callback-oidc"));

builder.Build().Run();
