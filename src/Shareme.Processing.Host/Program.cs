using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Shareme.Infrastructure;

// renders derivations: a Hangfire server for the processing queue only, no endpoints except health
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Coworkee:Jobs:Queues:0"] = Shareme.Processing.ShareMeProcessingModule.Queue,

    // every render uses up to two threads (Magick resource limit)
    ["Coworkee:Jobs:WorkerCount"] = Math.Max(1, Environment.ProcessorCount / 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
});

// renditions tell browsers through the api's SignalR hub, which this host only reaches over the Redis backplane
if (string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
{
    throw new InvalidOperationException("The processing host needs the 'redis' connection string to notify browsers.");
}

builder.Configuration.AddSharemeConfiguration();
builder.AddServiceDefaults();
builder.AddCoworkee<ShareMeProcessingHostModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkeeWorker();
app.Run();

[DependsOn(typeof(ShareMeDatabaseModule))]
internal sealed class ShareMeProcessingHostModule : CoworkeeModule;
