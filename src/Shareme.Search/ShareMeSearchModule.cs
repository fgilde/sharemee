using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Search.Elasticsearch;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Assets;
using Shareme.Contracts.Search;

namespace Shareme.Search;

/// <summary>Full text search over nodes: one shared index, readers materialised per node, kept current by outbox events.</summary>
[DependsOn(typeof(ShareMeAssetsModule), typeof(CoworkeeElasticsearchModule), typeof(CoworkeeBackgroundJobsModule))]
public sealed class ShareMeSearchModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(ShareMeSearchModule).Assembly);
        services.AddSingleton<IModelContributor, SearchModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, SearchPermissionDefinitions>();
        services.AddSingleton<SearchSettings>();
        services.AddScoped<NodeDocuments>();
        services.AddScoped<NodeIndexer>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var search = app.MapGroup("/api/v1/search").WithTags("Search").RequireAuthorization();
        search.MapPost("/", (SearchRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SearchNodes(body), ct).ToHttpResult());
        search.MapPost("/suggest", (SuggestRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SuggestNodes(body), ct).ToHttpResult());
        search.MapPost("/reindex", (IDispatcher d, CancellationToken ct) => d.SendAsync(new StartReindex(), ct).ToHttpResult());
        search.MapGet("/reindex", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetReindexRuns(), ct).ToHttpResult());

        var saved = app.MapGroup("/api/v1/saved-searches").WithTags("Search").RequireAuthorization();
        saved.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetSavedSearches(), ct).ToHttpResult());
        saved.MapPost("/", (SaveSearchRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveSearch(null, body), ct).ToHttpResult());
        saved.MapPut("/{id:guid}", (Guid id, SaveSearchRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveSearch(id, body), ct).ToHttpResult());
        saved.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteSavedSearch(id), ct).ToHttpResult());
    }
}

internal sealed class SearchPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(SearchPermissions.GroupName, "Saved searches")
            .Add(SearchPermissions.UseSavedSearch, "Use a shared search")
            .Add(SearchPermissions.ManageSavedSearch, "Change or delete a shared search", SearchPermissions.UseSavedSearch);
}
