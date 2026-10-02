using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Application.Authorization;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shareme.Assets;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Contracts.Search;
using Shareme.Graph;
using Shareme.Metadata;

namespace Shareme.Search;

/// <summary>Index fields of nodes. Metadata fields carry their value type in the prefix, so equal keys of different tenants never collide in the shared index.</summary>
internal static class NodeFields
{
    public const string Tenant = "tenant";
    public const string Readers = "readers";
    public const string ParentId = "parentId";
    public const string PathIds = "pathIds";
    public const string Id = "id";
    public const string Fulltext = "fulltext";

    public static readonly IReadOnlyList<SearchField> Static =
    [
        new(Id, SearchFieldType.Keyword),
        new(Tenant, SearchFieldType.Keyword),
        new(Readers, SearchFieldType.Keyword),
        new(ParentId, SearchFieldType.Keyword),
        new(PathIds, SearchFieldType.Keyword),
        new(SearchFields.Name, SearchFieldType.Text),
        new(SearchFields.Type, SearchFieldType.Keyword),
        new(SearchFields.Semantic, SearchFieldType.Keyword),
        new(SearchFields.CreatedAt, SearchFieldType.Date),
        new(SearchFields.ModifiedAt, SearchFieldType.Date),
        new(SearchFields.CreatedBy, SearchFieldType.Keyword),
        new(SearchFields.FileName, SearchFieldType.Text),
        new(SearchFields.Extension, SearchFieldType.Keyword),
        new(SearchFields.MimeType, SearchFieldType.Keyword),
        new(SearchFields.AssetType, SearchFieldType.Keyword),
        new(SearchFields.Size, SearchFieldType.Long),
        new(SearchFields.Sha256, SearchFieldType.Keyword),
        new(SearchFields.Status, SearchFieldType.Keyword),
        new(Fulltext, SearchFieldType.Text),
    ];

    public static readonly IReadOnlySet<string> StaticLogical = new HashSet<string>(
        [SearchFields.Name, SearchFields.Type, SearchFields.Semantic, SearchFields.CreatedAt, SearchFields.ModifiedAt, SearchFields.CreatedBy, SearchFields.FileName,
         SearchFields.Extension, SearchFields.MimeType, SearchFields.AssetType, SearchFields.Size, SearchFields.Sha256, SearchFields.Status]);

    public static (string Prefix, SearchFieldType Type) MetaType(string valueType, string listMode) => listMode != ListModes.None
        ? ("ms", SearchFieldType.Keyword)
        : valueType switch
        {
            PropertyValueTypes.Text or PropertyValueTypes.RichText => ("mt", SearchFieldType.Text),
            PropertyValueTypes.Date or PropertyValueTypes.DateTime => ("md", SearchFieldType.Date),
            PropertyValueTypes.Integer => ("ml", SearchFieldType.Long),
            PropertyValueTypes.Decimal => ("mf", SearchFieldType.Double),
            PropertyValueTypes.Boolean => ("mb", SearchFieldType.Boolean),
            _ => ("ms", SearchFieldType.Keyword),
        };

    public static string Meta(PropertyDefinition definition, string? lang = null) =>
        MetaType(definition.ValueType, definition.ListMode).Prefix + "." + definition.Key + (lang is null ? string.Empty : "__" + lang);

    public static bool Filterable(PropertyDefinition definition) => definition.Searchable is SearchModes.Filter or SearchModes.Both || definition.Facet;

    public static bool InFulltext(PropertyDefinition definition) => definition.Searchable is SearchModes.Fulltext or SearchModes.Both;

    /// <summary>The schema for a set of definitions and data languages.</summary>
    public static SearchSchema Schema(string alias, IEnumerable<PropertyDefinition> definitions, IEnumerable<string> languages)
    {
        var languageList = languages.ToList();
        var fields = Static.ToList();
        foreach (var definition in definitions.Where(Filterable))
        {
            var type = MetaType(definition.ValueType, definition.ListMode).Type;
            fields.Add(new SearchField(Meta(definition), type));
            if (definition.Localizable)
            {
                fields.AddRange(languageList.Select(lang => new SearchField(Meta(definition, lang), type)));
            }
        }

        return new SearchSchema(alias, fields.DistinctBy(f => f.Name).ToList());
    }
}

internal sealed class SearchSettings(IConfiguration configuration)
{
    public string Alias { get; } = configuration["Shareme:Search:Alias"] is { Length: > 0 } alias ? alias : "shareme-nodes";

    /// <summary>Schemas already pushed to the index by this process.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, bool> Ensured { get; } = new(StringComparer.Ordinal);
}

/// <summary>Builds index documents for nodes of the current tenant, with readers materialised along the inheritance chain.</summary>
internal sealed class NodeDocuments(CoworkeeDbContext db, IResourceAccessReader access, MetadataCatalog catalog)
{
    public async Task<SearchSchema> SchemaAsync(string alias, CancellationToken cancellationToken) =>
        NodeFields.Schema(alias, await catalog.DefinitionsAsync(cancellationToken), (await catalog.LanguagesAsync(cancellationToken)).Select(l => l.Code));

    /// <summary>Documents for the ids that exist and are not deleted; the rest are returned for removal.</summary>
    public async Task<(IReadOnlyList<SearchDocument> Documents, IReadOnlyList<string> Removed)> BuildAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var nodes = await db.Set<Node>().AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync(cancellationToken);
        var removed = ids.Except(nodes.Select(n => n.Id)).Select(id => id.ToString()).ToList();
        if (nodes.Count == 0)
        {
            return ([], removed);
        }

        var typeKeys = await db.Set<NodeType>().AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Key, cancellationToken);
        var versionIds = nodes.OfType<Asset>().Select(a => a.CurrentVersionId).OfType<Guid>().ToList();
        var versions = await db.Set<AssetVersion>().AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
        var definitions = (await catalog.DefinitionsAsync(cancellationToken)).ToDictionary(d => d.Key);

        // readers: principals with Nodes.Read on any node of the chain up to and including the first interrupted one
        var pathIds = nodes.SelectMany(n => n.PathIds).Distinct().ToList();
        var interrupted = (await db.Set<Node>().AsNoTracking().Where(n => pathIds.Contains(n.Id) && n.InterruptInheritance).Select(n => n.Id).ToListAsync(cancellationToken)).ToHashSet();
        var principals = await access.GetPrincipalsAsync(NodePermissions.Read, NodePermissions.ResourceType, pathIds, cancellationToken);

        var documents = new List<SearchDocument>(nodes.Count);
        foreach (var node in nodes)
        {
            var readers = new HashSet<string>();
            foreach (var id in Enumerable.Reverse(node.PathIds))
            {
                readers.UnionWith(principals.GetValueOrDefault(id) ?? []);
                if (interrupted.Contains(id))
                {
                    break;
                }
            }

            var typeKey = typeKeys.GetValueOrDefault(node.NodeTypeId, "unknown");
            var fulltext = new List<string> { node.Name };
            fulltext.AddRange(node.Names.Values);
            var body = new JsonObject
            {
                [NodeFields.Id] = node.Id.ToString(),
                [NodeFields.Tenant] = node.TenantId.ToString(),
                [NodeFields.Readers] = Array(readers),
                [NodeFields.ParentId] = node.ParentId?.ToString(),
                [NodeFields.PathIds] = Array(node.PathIds.Select(i => i.ToString())),
                [SearchFields.Name] = node.Name,
                [SearchFields.Type] = typeKey,
                [SearchFields.Semantic] = node.Semantic.ToString().ToLowerInvariant(),
                [SearchFields.CreatedAt] = node.CreatedAt,
                [SearchFields.ModifiedAt] = node.ModifiedAt,
                [SearchFields.CreatedBy] = node.CreatedBy?.ToString(),
            };

            if (node is Asset asset)
            {
                body[SearchFields.AssetType] = asset.AssetTypeKey;
                body[SearchFields.Status] = asset.Status;
                if (asset.CurrentVersionId is { } versionId && versions.TryGetValue(versionId, out var version))
                {
                    body[SearchFields.FileName] = version.FileName;
                    body[SearchFields.Extension] = version.Extension;
                    body[SearchFields.MimeType] = version.MimeType;
                    body[SearchFields.Size] = version.Size;
                    body[SearchFields.Sha256] = version.Sha256;
                    fulltext.Add(version.FileName);
                }
            }

            foreach (var (key, values) in node.Metadata)
            {
                if (definitions.TryGetValue(key, out var definition) && definition.AppliesTo(typeKey))
                {
                    AddMetadata(body, fulltext, definition, values);
                }
            }

            body[NodeFields.Fulltext] = string.Join(' ', fulltext.Where(t => !string.IsNullOrWhiteSpace(t)));
            documents.Add(new SearchDocument(node.Id.ToString(), body));
        }

        return (documents, removed);
    }

    private static void AddMetadata(JsonObject body, List<string> fulltext, PropertyDefinition definition, List<MetadataValue> values)
    {
        var all = new JsonArray();
        var byLanguage = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            JsonNode? indexed = value.List is { } listKey ? JsonValue.Create(listKey) : Plain(value.V);
            if (indexed is null)
            {
                continue;
            }

            all.Add(indexed.DeepClone());
            if (definition.Localizable && value.Lang is { } lang)
            {
                (byLanguage.TryGetValue(lang, out var list) ? list : byLanguage[lang] = []).Add(indexed);
            }

            if (NodeFields.InFulltext(definition))
            {
                fulltext.Add(value.List is { } key ? definition.ListValues.FirstOrDefault(l => l.Key == key)?.Name ?? key : indexed.ToString());
            }
        }

        if (!NodeFields.Filterable(definition) || all.Count == 0)
        {
            return;
        }

        body[NodeFields.Meta(definition)] = all;
        foreach (var (lang, list) in byLanguage)
        {
            body[NodeFields.Meta(definition, lang)] = list;
        }
    }

    private static JsonNode? Plain(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.String } v => JsonValue.Create(v.GetString()),
        { ValueKind: JsonValueKind.Number } v => v.TryGetInt64(out var l) ? JsonValue.Create(l) : JsonValue.Create(v.GetDouble()),
        { ValueKind: JsonValueKind.True } => JsonValue.Create(true),
        { ValueKind: JsonValueKind.False } => JsonValue.Create(false),
        _ => null,
    };

    private static JsonArray Array(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode)JsonValue.Create(v))]);
}
