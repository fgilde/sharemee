using Coworkee.Client.Blazor;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddCoworkeeClient(new Uri(builder.HostEnvironment.BaseAddress), options => options.AppTitle = "Sharemee");
await builder.Build().RunAsync();
