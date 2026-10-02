using Coworkee.AspNetCore;
using Shareme.Api;

var builder = WebApplication.CreateBuilder(args);

// the api works the light queues; renditions run on the processing host
if (!builder.Configuration.GetSection("Coworkee:Jobs:Queues").Exists())
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Coworkee:Jobs:Queues:0"] = "default",
        ["Coworkee:Jobs:Queues:1"] = "mail",
        ["Coworkee:Jobs:Queues:2"] = "search",
    });
}
builder.AddServiceDefaults();
builder.AddCoworkee<ShareMeApiModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();

public partial class Program;
