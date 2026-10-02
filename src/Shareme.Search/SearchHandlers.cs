using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Search;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Search;
using Shareme.Graph;
using Shareme.Metadata;

namespace Shareme.Search;

public sealed record SearchNodes(SearchRequest Request) : IQuery<Result<SearchResponse>>;

public sealed record SuggestNodes(SuggestRequest Request) : IQuery<Result<IReadOnlyList<SuggestionDto>>>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record StartReindex : ICommand<Result<ReindexRunDto>>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record GetReindexRuns : IQuery<Result<IReadOnlyList<ReindexRunDto>>>;

public sealed record GetSavedSearches : IQuery<Result<IReadOnlyList<SavedSearchDto>>>;

public sealed record SaveSearch(Guid? Id, SaveSearchRequest Request) : ICommand<Result<SavedSearchDto>>;

public sealed record DeleteSavedSearch(Guid Id) : ICommand<Result>;

/// <summary>Maps the public field names of a request to index fields of the tenant's catalog.</summary>
internal sealed class SearchFieldMap(IReadOnlyDictionary<string, PropertyDefinition> definitions)
{
    public string? Resolve(string field, string? lang = null)
    {
        if (NodeFields.StaticLogical.Contains(field))
        {
            return field;
        }

        return field.StartsWith(SearchFields.MetaPrefix, StringComparison.Ordinal)
            && definitions.TryGetValue(field[SearchFields.MetaPrefix.Length..], out var definition) && NodeFields.Filterable(definition)
            ? NodeFields.Meta(definition, definition.Localizable ? lang : null)
            : null;
    }

    public PropertyDefinition? Definition(string field) =>
        field.StartsWith(SearchFields.MetaPrefix, StringComparison.Ordinal) ? definitions.GetValueOrDefault(field[SearchFields.MetaPrefix.Length..]) : null;
}

internal sealed class SearchHandlers(
    CoworkeeDbContext db, ISearchIndex index, SearchSettings settings, NodeAccess access, IResourceAccessReader principals, ICurrentUser currentUser,
    NodeMapper mapper, MetadataCatalog catalog, IBackgroundJobs jobs, IPermissionChecker permissions, NodeIndexer indexer, TimeProvider clock)
    : IHandler<SearchNodes, Result<SearchResponse>>,
      IHandler<SuggestNodes, Result<IReadOnlyList<SuggestionDto>>>,
      IHandler<StartReindex, Result<ReindexRunDto>>,
      IHandler<GetReindexRuns, Result<IReadOnlyList<ReindexRunDto>>>,
      IHandler<GetSavedSearches, Result<IReadOnlyList<SavedSearchDto>>>,
      IHandler<SaveSearch, Result<SavedSearchDto>>,
      IHandler<DeleteSavedSearch, Result>
{
    private static readonly Dictionary<string, FilterOperator> Operators = new(StringComparer.Ordinal)
    {
        [SearchOperators.HasValue] = FilterOperator.HasValue,
        [SearchOperators.HasNoValue] = FilterOperator.HasNoValue,
        [SearchOperators.Is] = FilterOperator.Equals,
        [SearchOperators.NotEquals] = FilterOperator.NotEquals,
        [SearchOperators.Contains] = FilterOperator.Contains,
        [SearchOperators.DoesNotContain] = FilterOperator.DoesNotContain,
        [SearchOperators.FuzzyEquals] = FilterOperator.FuzzyEquals,
        [SearchOperators.FuzzyContains] = FilterOperator.FuzzyContains,
        [SearchOperators.GreaterThanOrEquals] = FilterOperator.GreaterThanOrEquals,
        [SearchOperators.LessThanOrEquals] = FilterOperator.LessThanOrEquals,
        [SearchOperators.Between] = FilterOperator.Between,
        [SearchOperators.IdentifierEquals] = FilterOperator.IdentifierEquals,
        [SearchOperators.IdentifierNotEquals] = FilterOperator.IdentifierNotEquals,
    };

    private static readonly string[] TextFields = [SearchFields.Name, SearchFields.FileName, NodeFields.Fulltext];

    public async Task<Result<SearchResponse>> HandleAsync(SearchNodes query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var map = new SearchFieldMap((await catalog.DefinitionsAsync(cancellationToken)).ToDictionary(d => d.Key));
        if (await ScopeAsync(request.FolderId, cancellationToken) is not { } filters)
        {
            return new SearchResponse([], 0, new Dictionary<string, IReadOnlyList<FacetValueDto>>(), null);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var filter in request.Filters ?? [])
        {
            var values = filter.Values ?? [];
            if (map.Resolve(filter.Field, filter.Lang) is not { } field)
            {
                errors[filter.Field] = [$"'{filter.Field}' cannot be searched."];
            }
            else if (!Operators.TryGetValue(filter.Operator, out var op))
            {
                errors[filter.Field] = [$"Use one of: {string.Join(", ", SearchOperators.All)}."];
            }
            else if (op == FilterOperator.Between ? values.Count != 2 : op is not (FilterOperator.HasValue or FilterOperator.HasNoValue) && values.Count == 0)
            {
                errors[filter.Field] = [op == FilterOperator.Between ? "Between needs a lower and an upper value." : "A value is required."];
            }
            else
            {
                filters.Add(new SearchFilter(field, op, values));
            }
        }

        var facets = new List<(string Logical, string Field)>();
        foreach (var facet in request.Facets ?? [])
        {
            if (map.Resolve(facet) is { } field)
            {
                facets.Add((facet, field));
            }
            else
            {
                errors[facet] = [$"'{facet}' cannot be used as a facet."];
            }
        }

        var sort = new List<SearchSort>();
        foreach (var item in request.Sort ?? [])
        {
            if (map.Resolve(item.Field) is { } field)
            {
                sort.Add(new SearchSort(field, item.Descending));
            }
            else
            {
                errors[item.Field] = [$"'{item.Field}' cannot be used for sorting."];
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await indexer.EnsureIndexAsync(cancellationToken);
        var result = await index.SearchAsync(settings.Alias, new SearchQuery
            {
                Term = request.Term,
                Exact = request.Exact,
                AllWords = request.AllWords,
                TextFields = TextFields,
                Filters = filters,
                Facets = [.. facets.Select(f => f.Field)],
                Sort = sort,
                Size = Math.Clamp(request.Size, 1, 200),
                Cursor = request.Cursor,
            }, cancellationToken);

        // the database is the truth for what is shown: deleted meanwhile, renamed, current thumbnail
        var ids = result.Hits.Select(h => Guid.Parse(h.Id)).ToList();
        var nodes = (await db.Set<Node>().AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync(cancellationToken)).ToDictionary(n => n.Id);
        var items = await mapper.MapAsync(ids.Where(nodes.ContainsKey).Select(id => nodes[id]), cancellationToken);
        var labels = await LabelsAsync(map, facets, cancellationToken);
        var facetResult = facets.ToDictionary(
            f => f.Logical,
            f => (IReadOnlyList<FacetValueDto>)(result.Facets.GetValueOrDefault(f.Field) ?? []).Select(v => new FacetValueDto(v.Value, v.Count, labels.GetValueOrDefault((f.Logical, v.Value)))).ToList());
        return new SearchResponse(items, result.Total, facetResult, result.Cursor);
    }

    public async Task<Result<IReadOnlyList<SuggestionDto>>> HandleAsync(SuggestNodes query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Request.Prefix) || await ScopeAsync(query.Request.FolderId, cancellationToken) is not { } filters)
        {
            return Result<IReadOnlyList<SuggestionDto>>.Success([]);
        }

        await indexer.EnsureIndexAsync(cancellationToken);
        var hits = await index.SuggestAsync(settings.Alias, query.Request.Prefix.Trim(), [SearchFields.Name, SearchFields.FileName], filters, Math.Clamp(query.Request.Size, 1, 20), cancellationToken);

        var ids = hits.Select(h => Guid.Parse(h.Id)).ToList();
        var nodes = (await db.Set<Node>().AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync(cancellationToken)).ToDictionary(n => n.Id);
        IReadOnlyList<SuggestionDto> suggestions = ids.Where(nodes.ContainsKey).Select(id => new SuggestionDto(id, nodes[id].Name, nodes[id].Semantic.ToString().ToLowerInvariant())).ToList();
        return Result<IReadOnlyList<SuggestionDto>>.Success(suggestions);
    }

    public async Task<Result<ReindexRunDto>> HandleAsync(StartReindex command, CancellationToken cancellationToken)
    {
        var run = new ReindexRun { State = "queued", StartedAt = clock.GetUtcNow(), StartedBy = currentUser.UserId };
        db.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<ReindexJob, Guid>(run.Id, SearchQueue.Name);
        return Map(run);
    }

    public async Task<Result<IReadOnlyList<ReindexRunDto>>> HandleAsync(GetReindexRuns query, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReindexRunDto> runs = (await db.Set<ReindexRun>().AsNoTracking().OrderByDescending(r => r.StartedAt).Take(20).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<ReindexRunDto>>.Success(runs);
    }

    public async Task<Result<IReadOnlyList<SavedSearchDto>>> HandleAsync(GetSavedSearches query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var shared = await permissions.GetGrantedResourcesAsync(SearchPermissions.UseSavedSearch, SearchPermissions.SavedSearchResourceType, cancellationToken);
        IReadOnlyList<SavedSearchDto> saved = (await db.Set<SavedSearch>().AsNoTracking().Where(s => s.OwnerId == userId || shared.Contains(s.Id)).OrderBy(s => s.Name).ToListAsync(cancellationToken))
            .Select(s => new SavedSearchDto(s.Id, s.Name, s.Request, s.OwnerId, s.OwnerId == userId)).ToList();
        return Result<IReadOnlyList<SavedSearchDto>>.Success(saved);
    }

    public async Task<Result<SavedSearchDto>> HandleAsync(SaveSearch command, CancellationToken cancellationToken)
    {
        var name = command.Request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 200)
        {
            return Error.Validation("Name", "A name of up to 200 characters is required.");
        }

        SavedSearch saved;
        if (command.Id is { } id)
        {
            if (await db.Set<SavedSearch>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) is not { } existing)
            {
                return Error.NotFound("search.not_found", "The saved search does not exist.");
            }

            if (!await MayManageAsync(existing, cancellationToken))
            {
                return Error.Forbidden("auth.forbidden", "Only the owner or a manager of this search can change it.");
            }

            saved = existing;
            saved.Name = name;
            saved.Request = command.Request.Request with { Cursor = null };
        }
        else
        {
            saved = new SavedSearch { Name = name, Request = command.Request.Request with { Cursor = null }, OwnerId = currentUser.UserId!.Value };
            db.Add(saved);
        }

        return new SavedSearchDto(saved.Id, saved.Name, saved.Request, saved.OwnerId, saved.OwnerId == currentUser.UserId);
    }

    public async Task<Result> HandleAsync(DeleteSavedSearch command, CancellationToken cancellationToken)
    {
        if (await db.Set<SavedSearch>().SingleOrDefaultAsync(s => s.Id == command.Id, cancellationToken) is not { } saved)
        {
            return Error.NotFound("search.not_found", "The saved search does not exist.");
        }

        if (!await MayManageAsync(saved, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "Only the owner or a manager of this search can delete it.");
        }

        db.Remove(saved);
        return Result.Success();
    }

    /// <summary>Tenant, readers and folder conditions every query gets; null when the caller can see nothing.</summary>
    private async Task<List<SearchFilter>?> ScopeAsync(Guid? folderId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return null;
        }

        var filters = new List<SearchFilter> { new(NodeFields.Tenant, FilterOperator.Equals, [tenantId.ToString()]) };
        if (!await access.IsGlobalAsync(NodePermissions.Read, cancellationToken))
        {
            var keys = await principals.GetCurrentPrincipalsAsync(cancellationToken);
            if (keys.Count == 0)
            {
                return null;
            }

            filters.Add(new SearchFilter(NodeFields.Readers, FilterOperator.IdentifierEquals, keys));
        }

        if (folderId is { } folder)
        {
            filters.Add(new SearchFilter(NodeFields.PathIds, FilterOperator.IdentifierEquals, [folder.ToString()]));
            filters.Add(new SearchFilter(NodeFields.Id, FilterOperator.IdentifierNotEquals, [folder.ToString()]));
        }

        return filters;
    }

    private async Task<Dictionary<(string, string), string>> LabelsAsync(SearchFieldMap map, List<(string Logical, string Field)> facets, CancellationToken cancellationToken)
    {
        var labels = new Dictionary<(string, string), string>();
        if (facets.Any(f => f.Logical == SearchFields.Type))
        {
            foreach (var type in await db.Set<NodeType>().AsNoTracking().ToListAsync(cancellationToken))
            {
                labels[(SearchFields.Type, type.Key)] = type.Name;
            }
        }

        foreach (var (logical, _) in facets)
        {
            foreach (var value in map.Definition(logical)?.ListValues ?? [])
            {
                labels[(logical, value.Key)] = value.Name;
            }
        }

        return labels;
    }

    private async Task<bool> MayManageAsync(SavedSearch saved, CancellationToken cancellationToken) =>
        saved.OwnerId == currentUser.UserId
        || await permissions.IsGrantedAsync(SearchPermissions.ManageSavedSearch, SearchPermissions.SavedSearchResourceType, saved.Id, cancellationToken);

    private static ReindexRunDto Map(ReindexRun run) => new(run.Id, run.State, run.StartedAt, run.FinishedAt, run.Indexed, run.Failed, run.Error);
}

