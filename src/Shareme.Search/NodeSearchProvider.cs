using System.Runtime.CompilerServices;
using Coworkee.Application.GlobalSearch;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Search;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Search;

namespace Shareme.Search;

/// <summary>Ctrl+K and the global search find assets, folders, workspaces, pages and news through the node index, with the reader rights of the search page.</summary>
internal sealed class NodeSearchProvider : IGlobalSearchProvider
{
    public string Name => "content";

    public string Group => "Content";

    public string? Icon => "Folder";

    public string? Permission => null;

    public async IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (query.Text.Length == 0)
        {
            yield break;
        }

        var result = await context.Services.GetRequiredService<IDispatcher>()
            .SendAsync(new SearchNodes(new SearchRequest(query.Text, query.Exact, Size: context.Limit)), cancellationToken);
        if (!result.IsSuccess)
        {
            yield break;
        }

        // the index ranks; SearchMatch only puts the scores next to the other providers'
        foreach (var (node, rank) in result.Value.Items.Select((node, rank) => (node, rank)))
        {
            var match = SearchMatch.Score(node.Name, query);
            yield return new GlobalSearchHit(Name, Group, node.Name)
            {
                Subtitle = node.Type,
                Url = NodeRoutes.For(node),
                Id = node.Id.ToString(),
                Score = match?.Score ?? Math.Max(0.1, 0.35 - (rank * 0.01)),
                Highlights = match?.Highlights ?? [],
                Thumbnail = NodeRoutes.Thumbnail(node),
                Icon = NodeRoutes.Icon(node.Semantic),
            };
        }
    }
}
