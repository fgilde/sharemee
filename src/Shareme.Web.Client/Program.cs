using Coworkee.Client.Blazor;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Shareme.Web.Client.Graph;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddCoworkeeClient(baseAddress, options => options.AppTitle = "Sharemee");
builder.Services.AddHttpClient<ISharemeApi, SharemeApi>(client => client.BaseAddress = baseAddress);
builder.Services.AddSingleton<INavigationContributor, SharemeNavigation>();
await builder.Build().RunAsync();
