using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Shareme.Infrastructure;

// renders derivations: a Hangfire server for the processing queue only, no public API
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Coworkee:Jobs:Queues:0"] = Shareme.Processing.ShareMeProcessingModule.Queue,
    ["Coworkee:Jobs:WorkerCount"] = Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
});
builder.AddServiceDefaults();
builder.AddCoworkee<ShareMeProcessingHostModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();

[DependsOn(typeof(ShareMeDatabaseModule))]
internal sealed class ShareMeProcessingHostModule : CoworkeeModule;
