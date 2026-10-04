using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Shareme.Contracts.Metadata;
using Shareme.Graph;
using Shareme.Metadata;

namespace Shareme.Importer;

/// <summary>What one run writes, built in memory from the source rows.</summary>
public sealed record ImportPlan(
    IReadOnlyList<DataLanguage> Languages,
    IReadOnlyList<NodeType> NodeTypes,
    IReadOnlyList<EdgeType> EdgeTypes,
    IReadOnlyList<PropertyGroup> PropertyGroups,
    IReadOnlyList<PropertyDefinition> PropertyDefinitions,
    IReadOnlyList<Node> Nodes,
    IReadOnlyList<Edge> Edges);

/// <param name="Root">The target tenant's root folder; top level categories and nodes without a parent go below it.</param>
/// <param name="TakenKeys">Keys of node types, edge types, groups and definitions already in the tenant, so imported ones never clash.</param>
public sealed record MapTarget(Guid TenantId, Folder Root, IReadOnlySet<string> TakenKeys);

/// <summary>Maps Sharedien rows to Shareme entities. Pure: no database, so every rule is testable with synthetic rows.</summary>
public sealed class SharedienMapper(SourceData source, MapTarget target, ImportReport report)
{
    // Sharedien NodeTypeSemantic and EdgeTypeSemantic values
    private const int Category = 1, AssetSet = 2, CustomEntity = 1000;
    private const int ParentChild = 1, Link = 3, Translation = 5, Variant = 6, Licence = 7, Duplicate = 8, CustomRelation = 0;

    private readonly Dictionary<long, string> _languageCodes = source.Languages.ToDictionary(l => l.Id, l => LanguageCode(l.Identifier));
    private readonly string _defaultLanguage = source.Languages.FirstOrDefault(l => l.IsDefault) is { } d ? LanguageCode(d.Identifier) : "en";
    private readonly Dictionary<(string, string), Dictionary<string, string>> _translations = [];
    private readonly HashSet<string> _keys = new(target.TakenKeys, StringComparer.Ordinal);

    public ImportPlan Map()
    {
        foreach (var t in source.Translations)
        {
            if (_languageCodes.TryGetValue(t.LanguageId, out var code))
            {
                var key = (t.TranslationIdentifier, t.FieldName);
                (_translations.TryGetValue(key, out var texts) ? texts : _translations[key] = [])[code] = t.Value;
            }
        }

        var languages = source.Languages.Select(l => Tenant(new DataLanguage
        {
            Code = LanguageCode(l.Identifier),
            Name = Cap(Name(l.TranslationIdentifier, l.Identifier), 100),
            IsDefault = l.IsDefault,
        }, "MAM.Language", l.Id, l.Identifier)).DistinctBy(l => l.Code).ToList();

        var nodeTypes = MapNodeTypes(out var typeOf);
        var edgeTypes = MapEdgeTypes(out var edgeTypeOf);
        var groups = source.PropertyGroups.Select(g => Tenant(new PropertyGroup
        {
            Key = Key(g.Identifier, Name(g.TranslationIdentifier, null), "group", g.Id),
            Name = Name(g.TranslationIdentifier, g.Identifier),
            SortIndex = g.SequenceNr,
        }, "MAM.PropertyGroup", g.Id, g.Identifier)).ToList();
        var groupIds = source.PropertyGroups.Zip(groups).ToDictionary(p => p.First.Id, p => p.Second.Id);
        var definitions = MapDefinitions(typeOf, groupIds, out var definitionOf, out var listKeyOf);
        var nodes = MapNodes(typeOf, out var nodeOf);
        var edges = MapEdges(nodeOf, edgeTypeOf);
        MapValues(nodeOf, definitionOf, listKeyOf);

        report.Mapped["languages"] = languages.Count;
        report.Mapped["node types"] = nodeTypes.Count;
        report.Mapped["edge types"] = edgeTypes.Count;
        report.Mapped["property groups"] = groups.Count;
        report.Mapped["property definitions"] = definitions.Count;
        report.Mapped["nodes"] = nodes.Count;
        report.Mapped["edges"] = edges.Count;
        return new ImportPlan(languages, nodeTypes, edgeTypes, groups, definitions, nodes, edges);
    }

    private List<NodeType> MapNodeTypes(out Dictionary<long, (Guid Id, string Key, int Semantic)> typeOf)
    {
        typeOf = [];
        var created = new List<NodeType>();
        foreach (var row in source.NodeTypes)
        {
            switch (row.Semantic)
            {
                case Category:
                    typeOf[row.Id] = (NodeType.FolderTypeId, "folder", Category);
                    break;
                case AssetSet:
                    typeOf[row.Id] = (NodeType.AssetTypeId, "asset", AssetSet);
                    break;
                case CustomEntity:
                    var type = Tenant(new NodeType
                    {
                        Key = Key(row.Identifier, Name(row.TranslationIdentifier, null), "type", row.Id),
                        Name = Name(row.TranslationIdentifier, row.Identifier),
                        Semantic = NodeSemantic.Custom,
                    }, "MAM.NodeType", row.Id, row.Identifier);
                    typeOf[row.Id] = (type.Id, type.Key, CustomEntity);
                    created.Add(type);
                    break;
            }
        }

        return created;
    }

    private List<EdgeType> MapEdgeTypes(out Dictionary<long, Guid> edgeTypeOf)
    {
        edgeTypeOf = [];
        var created = new List<EdgeType>();
        foreach (var row in source.EdgeTypes)
        {
            Guid? seeded = row.Semantic switch
            {
                Link => EdgeType.LinkId,
                Duplicate => EdgeType.DuplicateId,
                Variant => EdgeType.VariantId,
                Translation => EdgeType.TranslationId,
                _ => null,
            };
            if (seeded is { } id)
            {
                edgeTypeOf[row.Id] = id;
            }
            else if (row.Semantic is CustomRelation or Licence)
            {
                var type = Tenant(new EdgeType
                {
                    Key = Key(row.Semantic == Licence ? "licence" : row.Identifier, Name(row.TranslationIdentifier, null), "relation", row.Id),
                    Name = Name(row.TranslationIdentifier, row.Identifier),
                    Semantic = EdgeSemantic.Custom,
                    IsDirected = row.IsDirected,
                }, "MAM.EdgeType", row.Id, row.Identifier);
                edgeTypeOf[row.Id] = type.Id;
                created.Add(type);
            }
        }

        return created;
    }

    private List<PropertyDefinition> MapDefinitions(
        Dictionary<long, (Guid Id, string Key, int Semantic)> typeOf, Dictionary<long, Guid> groupIds,
        out Dictionary<long, PropertyDefinition> definitionOf, out Dictionary<long, string> listKeyOf)
    {
        definitionOf = [];
        listKeyOf = [];
        var listValues = source.ListValues.ToLookup(v => v.DefinitionId);
        var nodeTypes = source.DefinitionNodeTypes.ToLookup(t => t.DefinitionId);
        foreach (var row in source.PropertyDefinitions)
        {
            if (MapValueType(row) is not { } valueType)
            {
                report.Problem("unknown_enum", "MAM.PropertyDefinition", row.Id);
                continue;
            }

            var definition = Tenant(new PropertyDefinition
            {
                Key = Key(row.Identifier, Name(row.TranslationIdentifier, null), "property", row.Id),
                Name = Name(row.TranslationIdentifier, row.Identifier),
                Description = Text(row.TranslationIdentifier, "Description") is { } description ? Cap(description, 2000) : null,
                GroupId = row.GroupId is { } g && groupIds.TryGetValue(g, out var groupId) ? groupId : null,
                ValueType = valueType,
                ListMode = !row.HasListValues ? ListModes.None : row.AllowMultipleValues ? ListModes.Multi : ListModes.Single,
                MultiValue = row.AllowMultipleValues && !row.HasListValues,
                AllowCustomValues = row.AllowCustomListValue,
                Localizable = row.IsLanguageDependant,
                HasContext = row.HasUserContext,
                Editable = row.Editable,
                Searchable = row.SearchOption switch { 1 => SearchModes.Filter, 2 => SearchModes.Fulltext, 3 => SearchModes.Both, _ => SearchModes.None },
                Facet = row.IsSearchableFacet,
                IgnoreOnNewVersion = row.IgnoreCopyingOnNewVersion,
                SortIndex = row.SortKey,
                NodeTypeKeys = [.. nodeTypes[row.Id].Select(t => typeOf.TryGetValue(t.NodeTypeId, out var type) ? type.Key : null).OfType<string>().Distinct()],
            }, "MAM.PropertyDefinition", row.Id, row.Identifier);

            var listKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in listValues[row.Id].OrderBy(v => v.SortKey))
            {
                var key = Unique(Slug(Guid.TryParse(value.Identifier, out _) ? Name(value.TranslationIdentifier, value.Text) : value.Identifier) ?? $"value-{value.Id}", listKeys);
                var entry = new PropertyListValue
                {
                    DefinitionId = definition.Id,
                    Key = key,
                    Name = Name(value.TranslationIdentifier, value.Text ?? value.Identifier),
                    SortIndex = value.SortKey,
                    IsActive = value.IsActive,
                };
                Ids.Set(entry, SourceIds.For("MAM.PropertyListValue", value.Id, value.Identifier));
                definition.ListValues.Add(entry);
                listKeyOf[value.Id] = key;
            }

            definitionOf[row.Id] = definition;
        }

        return [.. definitionOf.Values];
    }

    private List<Node> MapNodes(Dictionary<long, (Guid Id, string Key, int Semantic)> typeOf, out Dictionary<long, Node> nodeOf)
    {
        nodeOf = [];
        var latest = source.Versions.GroupBy(v => v.AssetNodeId).ToDictionary(g => g.Key, g => g.MaxBy(v => v.Version)!);
        var usedIds = new HashSet<Guid>();
        foreach (var row in source.Nodes)
        {
            if (!typeOf.TryGetValue(row.NodeTypeId, out var type))
            {
                continue; // versions, workspaces, states, DRM objects: other phases
            }

            Node node = type.Semantic switch
            {
                Category => new Folder { Name = Name(row.TranslationIdentifier, row.Identifier), Names = Names(row.TranslationIdentifier) },
                AssetSet => new Asset
                {
                    Name = Cap(latest.TryGetValue(row.Id, out var version) && version.OriginalFilename is { Length: > 0 } file ? file : row.Identifier, 400),
                    Status = row.AssetSetStatus == 2 ? "duplicate" : "importing",
                },
                _ => new CustomNode { Name = Name(row.TranslationIdentifier, row.Identifier), Names = Names(row.TranslationIdentifier) },
            };

            var id = SourceIds.For("MAM.Node", row.Id, row.Identifier);
            if (!usedIds.Add(id))
            {
                report.Problem("duplicate_identifier", "MAM.Node", row.Id);
                id = SourceIds.Derived("MAM.Node", row.Id);
                usedIds.Add(id);
            }

            Ids.Set(node, id);
            node.NodeTypeId = type.Id;
            node.TenantId = target.TenantId;
            node.SortIndex = row.SequenceNr;
            node.InterruptInheritance = row.InterruptInheritance;
            node.CreatedAt = Utc(row.CreateDate);
            node.ModifiedAt = Utc(row.MutDate);
            nodeOf[row.Id] = node;
        }

        // Shareme is a tree: the first parent wins, further parents become links
        var parentOf = new Dictionary<long, long>();
        foreach (var edge in source.Edges.Where(e => SemanticOf(e) == ParentChild).OrderBy(e => e.Id))
        {
            if (!nodeOf.ContainsKey(edge.FromNodeId) || !nodeOf.ContainsKey(edge.ToNodeId))
            {
                continue;
            }

            if (!parentOf.TryAdd(edge.ToNodeId, edge.FromNodeId))
            {
                report.Problem("multi_parent", "MAM.Edge", edge.Id);
            }
        }

        var children = parentOf.ToLookup(p => p.Value, p => p.Key);
        var placed = new List<Node>();
        var queue = new Queue<(long Id, Node Parent)>(nodeOf.Keys.Where(id => !parentOf.ContainsKey(id)).Select(id => (id, (Node)target.Root)));
        while (queue.TryDequeue(out var item))
        {
            var node = nodeOf[item.Id];
            node.PlaceUnder(item.Parent);
            placed.Add(node);
            foreach (var child in children[item.Id])
            {
                queue.Enqueue((child, node));
            }
        }

        var placedIds = placed.Select(n => n.Id).ToHashSet();
        foreach (var lost in nodeOf.Where(n => !placedIds.Contains(n.Value.Id)).Select(n => n.Key).ToList())
        {
            report.Problem("cycle", "MAM.Node", lost);
            nodeOf.Remove(lost);
        }

        return placed;
    }

    private List<Edge> MapEdges(Dictionary<long, Node> nodeOf, Dictionary<long, Guid> edgeTypeOf)
    {
        var edges = new List<Edge>();
        var seen = new HashSet<(Guid, Guid, Guid)>();
        var parentEdges = new HashSet<(long, long)>();
        foreach (var row in source.Edges.OrderBy(e => e.Id))
        {
            if (!nodeOf.TryGetValue(row.FromNodeId, out var from) || !nodeOf.TryGetValue(row.ToNodeId, out var to))
            {
                continue;
            }

            Guid type;
            if (SemanticOf(row) == ParentChild)
            {
                // the parent itself is the tree; a second parent stays reachable as a link
                if (to.ParentId == from.Id || !parentEdges.Add((row.FromNodeId, row.ToNodeId)))
                {
                    continue;
                }

                type = EdgeType.LinkId;
            }
            else if (!edgeTypeOf.TryGetValue(row.EdgeTypeId, out type))
            {
                continue;
            }

            if (seen.Add((type, from.Id, to.Id)))
            {
                var edge = new Edge { EdgeTypeId = type, FromNodeId = from.Id, ToNodeId = to.Id, TenantId = target.TenantId };
                Ids.Set(edge, SourceIds.For("MAM.Edge", row.Id, row.Identifier));
                edges.Add(edge);
            }
        }

        return edges;
    }

    private void MapValues(Dictionary<long, Node> nodeOf, Dictionary<long, PropertyDefinition> definitionOf, Dictionary<long, string> listKeyOf)
    {
        // asset metadata lives on the current version in Sharedien
        var assetOfVersion = source.Versions.GroupBy(v => v.AssetNodeId)
            .Select(g => g.MaxBy(v => v.Version)!).ToDictionary(v => v.VersionNodeId, v => v.AssetNodeId);
        foreach (var row in source.Values.OrderBy(v => v.Id))
        {
            var owner = assetOfVersion.TryGetValue(row.NodeId, out var asset) ? asset : row.NodeId;
            if (!nodeOf.TryGetValue(owner, out var node) || !definitionOf.TryGetValue(row.DefinitionId, out var definition))
            {
                continue; // older versions, system properties and nodes of later phases
            }

            if (row.UserContextId is not null)
            {
                report.Problem("user_context_not_imported", "MAM.PropertyValue", row.Id);
                continue;
            }

            var values = node.Metadata.TryGetValue(definition.Key, out var existing) ? existing : node.Metadata[definition.Key] = [];
            if (row.ListValueId is { } listValue)
            {
                if (listKeyOf.TryGetValue(listValue, out var key))
                {
                    values.Add(new MetadataValue(List: key));
                }
                else
                {
                    report.Problem("unknown_list_value", "MAM.PropertyValue", row.Id);
                }
            }
            else
            {
                IEnumerable<(string? Lang, JsonElement? V)> candidates =
                    definition.Localizable && row.TranslationIdentifier is { } ti && _translations.TryGetValue((ti, "TextValue"), out var texts)
                        ? texts.Select(t => ((string?)t.Key, (JsonElement?)JsonSerializer.SerializeToElement(t.Value)))
                        : [(null, Value(definition.ValueType, row))];
                foreach (var (lang, candidate) in candidates)
                {
                    // the same check as the API, so every imported value can be edited and searched like a typed one
                    if (MetadataRules.Check(definition, candidate) is (null, { } valid))
                    {
                        values.Add(new MetadataValue(Lang: lang, V: valid));
                    }
                    else
                    {
                        report.Problem("invalid_value", "MAM.PropertyValue", row.Id);
                    }
                }
            }

            if (values.Count == 0)
            {
                node.Metadata.Remove(definition.Key);
            }
        }
    }

    public static JsonElement? Value(string valueType, ValueRow row)
    {
        object? value = valueType switch
        {
            PropertyValueTypes.String or PropertyValueTypes.Text or PropertyValueTypes.Url => row.Text,
            PropertyValueTypes.Date => (row.Date ?? row.DateTime)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PropertyValueTypes.DateTime => row.DateTime is { } moment ? Utc(moment).ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture) : null,
            PropertyValueTypes.Time => row.DateTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                ?? (row.DurationTicks is { } t ? TimeSpan.FromTicks(t).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : row.Text),
            PropertyValueTypes.Integer => row.Int,
            PropertyValueTypes.Decimal => row.Decimal,
            PropertyValueTypes.Boolean => row.Bool,
            PropertyValueTypes.Duration => row.DurationTicks is { } ticks ? XmlConvert.ToString(TimeSpan.FromTicks(ticks)) : null,
            _ => null,
        };
        return value is null or "" ? null : JsonSerializer.SerializeToElement(value);
    }

    private static string? MapValueType(PropertyDefinitionRow row) => row.ValueType switch
    {
        0 => row.IsUrl ? PropertyValueTypes.Url : PropertyValueTypes.String,
        1 => PropertyValueTypes.Text,
        2 => PropertyValueTypes.Date,
        3 => PropertyValueTypes.DateTime,
        4 => PropertyValueTypes.Time,
        5 => PropertyValueTypes.Integer,
        6 or 7 => PropertyValueTypes.Decimal,
        8 => PropertyValueTypes.Boolean,
        9 => PropertyValueTypes.Duration,
        _ => null,
    };

    private Dictionary<long, int>? _edgeSemantics;

    private int SemanticOf(EdgeRow edge) =>
        (_edgeSemantics ??= source.EdgeTypes.ToDictionary(t => t.Id, t => t.Semantic)).GetValueOrDefault(edge.EdgeTypeId, -1);

    private T Tenant<T>(T entity, string table, long sourceId, string? identifier)
        where T : Coworkee.Domain.Entity
    {
        Ids.Set(entity, SourceIds.For(table, sourceId, identifier));
        switch (entity)
        {
            case Coworkee.Domain.IMultiTenant tenant:
                tenant.TenantId = target.TenantId;
                break;
            case NodeType type:
                type.TenantId = target.TenantId;
                break;
            case EdgeType type:
                type.TenantId = target.TenantId;
                break;
        }

        return entity;
    }

    private string Key(string identifier, string? name, string fallback, long sourceId) =>
        Unique((Guid.TryParse(identifier, out _) ? Slug(name) : Slug(identifier)) ?? $"{fallback}-{sourceId}", _keys);

    private string Name(string? translationIdentifier, string? fallback) =>
        Text(translationIdentifier, "Name") is { Length: > 0 } name ? (name.Length > 200 ? name[..200] : name) : fallback ?? "?";

    private string? Text(string? translationIdentifier, string field) =>
        translationIdentifier is not null && _translations.TryGetValue((translationIdentifier, field), out var texts)
            ? texts.GetValueOrDefault(_defaultLanguage) ?? texts.Values.FirstOrDefault()
            : null;

    private Dictionary<string, string> Names(string? translationIdentifier) =>
        translationIdentifier is not null && _translations.TryGetValue((translationIdentifier, "Name"), out var texts) ? new(texts) : [];

    public static string LanguageCode(string identifier) => Cap(identifier.Split('-', '_')[0].ToLowerInvariant(), 20);

    private static string Cap(string text, int length) => text.Length > length ? text[..length] : text;

    public static string? Slug(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var slug = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_')
            {
                slug.Append(c);
            }
            else if (c is ' ' or '.' && slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var result = slug.ToString().Trim('-');
        return result.Length == 0 ? null : result.Length > 90 ? result[..90] : result;
    }

    private static string Unique(string key, HashSet<string> taken)
    {
        var candidate = key;
        for (var i = 2; !taken.Add(candidate); i++)
        {
            candidate = $"{key}-{i}";
        }

        return candidate;
    }

    /// <summary>Sharedien stores UTC without an offset (getutcdate() defaults).</summary>
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

public static class Ids
{
    private static readonly System.Reflection.PropertyInfo Id = typeof(Coworkee.Domain.Entity).GetProperty(nameof(Coworkee.Domain.Entity.Id))!;

    public static void Set(Coworkee.Domain.Entity entity, Guid id) => Id.SetValue(entity, id);
}
