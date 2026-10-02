using Shareme.Contracts.Graph;

namespace Shareme.Contracts.Search;

/// <summary>Logical field names for filters, facets and sorting; metadata fields are "meta:{key}".</summary>
public static class SearchFields
{
    public const string Name = "name";
    public const string Type = "type";
    public const string Semantic = "semantic";
    public const string AssetType = "assetType";
    public const string Extension = "extension";
    public const string MimeType = "mimeType";
    public const string FileName = "fileName";
    public const string Size = "size";
    public const string Status = "status";
    public const string CreatedAt = "createdAt";
    public const string ModifiedAt = "modifiedAt";
    public const string CreatedBy = "createdBy";
    public const string Sha256 = "sha256";
    public const string MetaPrefix = "meta:";

    public static string Meta(string key) => MetaPrefix + key;
}

public static class SearchOperators
{
    public const string HasValue = "hasValue";
    public const string HasNoValue = "hasNoValue";
    public const string Is = "equals";
    public const string NotEquals = "notEquals";
    public const string Contains = "contains";
    public const string DoesNotContain = "doesNotContain";
    public const string FuzzyEquals = "fuzzyEquals";
    public const string FuzzyContains = "fuzzyContains";
    public const string GreaterThanOrEquals = "gte";
    public const string LessThanOrEquals = "lte";
    public const string Between = "between";
    public const string IdentifierEquals = "idEquals";
    public const string IdentifierNotEquals = "idNotEquals";

    public static readonly IReadOnlyList<string> All =
        [HasValue, HasNoValue, Is, NotEquals, Contains, DoesNotContain, FuzzyEquals, FuzzyContains, GreaterThanOrEquals, LessThanOrEquals, Between, IdentifierEquals, IdentifierNotEquals];
}

public sealed record SearchFilterDto(string Field, string Operator, IReadOnlyList<string>? Values = null, string? Lang = null);

public sealed record SearchSortDto(string Field, bool Descending = false);

public sealed record SearchRequest(
    string? Term = null,
    bool Exact = false,
    bool AllWords = true,
    IReadOnlyList<SearchFilterDto>? Filters = null,
    IReadOnlyList<string>? Facets = null,
    IReadOnlyList<SearchSortDto>? Sort = null,
    Guid? FolderId = null,
    bool Recursive = true,
    int Size = 50,
    string? Cursor = null);

public sealed record FacetValueDto(string Value, long Count, string? Label = null);

public sealed record SearchResponse(IReadOnlyList<NodeDto> Items, long Total, IReadOnlyDictionary<string, IReadOnlyList<FacetValueDto>> Facets, string? Cursor);

public sealed record SuggestRequest(string Prefix, Guid? FolderId = null, int Size = 10);

public sealed record SuggestionDto(Guid Id, string Name, string Semantic);

public sealed record SavedSearchDto(Guid Id, string Name, SearchRequest Request, Guid OwnerId, bool IsOwner, bool IsShared);

public sealed record SaveSearchRequest(string Name, SearchRequest Request, bool Shared = false);

public sealed record ReindexRunDto(Guid Id, string State, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int Indexed, int Failed, string? Error);
