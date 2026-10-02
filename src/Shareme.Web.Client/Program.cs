using Coworkee.Client.Blazor;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Shareme.Web.Client.Assets;
using Shareme.Web.Client.Graph;
using Shareme.Web.Client.Metadata;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddCoworkeeClient(baseAddress, options => options.AppTitle = "Sharemee");
builder.Services.AddHttpClient<ISharemeApi, SharemeApi>(client =>
{
    client.BaseAddress = baseAddress;
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<IMetadataApi, MetadataApi>(client => client.BaseAddress = baseAddress);
builder.Services.AddHttpClient<Shareme.Web.Client.Assets.IRenditionsApi, Shareme.Web.Client.Assets.RenditionsApi>(client =>
{
    client.BaseAddress = baseAddress;
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient<Shareme.Web.Client.Search.ISearchApi, Shareme.Web.Client.Search.SearchApi>(client => client.BaseAddress = baseAddress);
builder.Services.AddScoped<UploadQueue>();
builder.Services.AddSingleton<INavigationContributor, SharemeNavigation>();
await builder.Build().RunAsync();
