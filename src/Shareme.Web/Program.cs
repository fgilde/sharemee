using Coworkee.Bff;
using Shareme.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCoworkeeBff();

// the BFF forwards uploads to the api, so it takes bodies as large as the api does (Kestrel's default is about 28.6 MB)
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 101 * 1024 * 1024);
builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    // the debug proxy of WebAssembly: Visual Studio and VS Code attach to the browser through /_framework/debug
    app.UseWebAssemblyDebugging();
}

// Blazor WebAssembly needs wasm-unsafe-eval, BlazorJS (MudBlazor.Extensions) evaluates script strings (unsafe-eval), MudBlazor
// positions with inline styles; previews use blob URLs; the browser talks only to this host (the BFF forwards the API and hub)
const string ContentSecurityPolicy =
    "default-src 'self'; script-src 'self' 'wasm-unsafe-eval' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; "
    + "media-src 'self' blob:; font-src 'self' data:; connect-src 'self'; frame-src 'self' blob:; worker-src 'self' blob:; "
    + "object-src 'none'; base-uri 'self'; frame-ancestors 'self'; form-action 'self'";
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.ContentSecurityPolicy = ContentSecurityPolicy;
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
app.MapDefaultEndpoints();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapCoworkeeBff();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Shareme.Web.Client._Imports).Assembly, typeof(Coworkee.Client.Blazor.CoworkeeClientOptions).Assembly);
app.Run();
