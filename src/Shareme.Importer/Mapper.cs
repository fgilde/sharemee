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
    IReadOnlyList<Edge> Edges,
    IdentityPlan Identity,
    IReadOnlyList<Coworkee.Identity.Domain.ResourcePermission> NodePermissions);

/// <summary>What the target tenant already holds, so a run reuses keys and names instead of inventing new ones.</summary>
public sealed record MapTarget(Guid TenantId, Folder Root)
{
    /// <summary>Keys by kind (<see cref="KeyKinds"/>) and id, of everything already stored, imported or not.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>> Keys { get; init; } = new Dictionary<string, IReadOnlyDictionary<Guid, string>>();

    public IReadOnlyDictionary<Guid, (Guid DefinitionId, string Key)> ListValues { get; init; } = new Dictionary<Guid, (Guid, string)>();

    public IReadOnlySet<string> LanguageCodes { get; init; } = new HashSet<string>();

    public bool HasDefaultLanguage { get; init; }

    /// <summary>Users already in Shareme (any tenant: user names are unique across tenants) by normalized user name.</summary>
    public IReadOnlyDictionary<string, (Guid Id, Guid TenantId)> ExistingUsers { get; init; } = new Dictionary<string, (Guid, Guid)>();

    /// <summary>The tenant's system admin role; Sharedien GlobalAdmin roles map to it.</summary>
    public Guid? AdminRoleId { get; init; }

    public IReadOnlySet<string> ExistingRoleNames { get; init; } = new HashSet<string>();

    public IReadOnlySet<string> ExistingGroupNames { get; init; } = new HashSet<string>();

    public IReadOnlyDictionary<Guid, string> KeysOf(string kind) => Keys.TryGetValue(kind, out var keys) ? keys : new Dictionary<Guid, string>();
}

public static class KeyKinds
{
    public const string NodeType = "node type";
    public const string EdgeType = "edge type";
    public const string Group = "property group";
    public const string Definition = "property definition";
}

/// <summary>Maps Sharedien rows to Shareme entities. Pure: no database, so every rule is testable with synthetic rows.</summary>
public sealed class SharedienMapper(SourceData source, MapTarget target, ImportReport report)
{
    // Sharedien NodeTypeSemantic and EdgeTypeSemantic values
    private const int Category = 1, AssetSet = 2, Version = 3, Workspace = 4, Task = 5, CustomEntity = 1000;
    private const int ParentChild = 1, Link = 3, Translation = 5, Variant = 6, Licence = 7, Duplicate = 8, CustomRelation = 0;

    private readonly Dictionary<long, string> _languageCodes = source.Languages.ToDictionary(l => l.Id, l => LanguageCode(l.Identifier));
    private readonly string _defaultLanguage = source.Languages.FirstOrDefault(l => l.IsDefault) is { } d ? LanguageCode(d.Identifier) : "en";
    private readonly Dictionary<(string, string), Dictionary<string, string>> _translations = [];
    private readonly Dictionary<string, HashSet<string>> _takenKeys = [];

    private Guid Scope => target.TenantId;

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

        var languages = MapLanguages();
        var identityMapper = new IdentityMapper(source.Identity ?? SourceIdentity.Empty, target, report, Name);
        var identity = source.Identity is null ? IdentityPlan.Empty : identityMapper.Map();
        var nodeTypes = MapNodeTypes(out var typeOf);
        var edgeTypes = MapEdgeTypes(out var edgeTypeOf);
        var groups = new List<PropertyGroup>();
        var groupIds = new Dictionary<long, Guid>();
        foreach (var g in source.PropertyGroups)
        {
            var group = Tenant(new PropertyGroup { Key = string.Empty, Name = Name(g.TranslationIdentifier, g.Identifier), SortIndex = g.SequenceNr }, "MAM.PropertyGroup", g.Id, g.Identifier);
            group.Key = Key(KeyKinds.Group, group.Id, g.Identifier, Name(g.TranslationIdentifier, null), "group", g.Id);
            groups.Add(group);
            groupIds[g.Id] = group.Id;
        }

        var definitions = MapDefinitions(groupIds, typeOf, out var definitionOf, out var listKeyOf);
        var nodes = MapNodes(typeOf, out var nodeOf);
        foreach (var row in source.Nodes)
        {
            if (row.CreateUser is { } creator && identity.UserByName.TryGetValue(creator, out var userId) && nodeOf.TryGetValue(row.Id, out var created))
            {
                created.CreatedBy = userId;
            }
        }

        var nodePermissions = identityMapper.MapNodePermissions(identity, nodeOf.ToDictionary(n => n.Key, n => n.Value.Id));
        var edges = MapEdges(nodeOf, edgeTypeOf);
        MapValues(nodeOf, typeOf, definitionOf, listKeyOf, [.. target.LanguageCodes, .. languages.Select(l => l.Code)]);

        report.Mapped["languages"] = languages.Count;
        report.Mapped["node types"] = nodeTypes.Count;
        report.Mapped["edge types"] = edgeTypes.Count;
        report.Mapped["property groups"] = groups.Count;
        report.Mapped["property definitions"] = definitions.Count;
        report.Mapped["nodes"] = nodes.Count;
        report.Mapped["edges"] = edges.Count;
        return new ImportPlan(languages, nodeTypes, edgeTypes, groups, definitions, nodes, edges, identity, nodePermissions);
    }

    private List<DataLanguage> MapLanguages()
    {
        // one row per code; a code the tenant already has stays as it is, and only a tenant without default gets one
        var languages = new List<DataLanguage>();
        foreach (var row in source.Languages.OrderByDescending(l => l.IsDefault).ThenBy(l => l.Id))
        {
            var code = LanguageCode(row.Identifier);
            if (target.LanguageCodes.Contains(code) || languages.Any(l => l.Code == code))
            {
                continue;
            }

            languages.Add(Tenant(new DataLanguage
            {
                Code = code,
                Name = Cap(Name(row.TranslationIdentifier, row.Identifier), 100),
                IsDefault = row.IsDefault && !target.HasDefaultLanguage,
            }, "MAM.Language", row.Id, row.Identifier));
        }

        return languages;
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
                    var type = Tenant(new NodeType { Key = string.Empty, Name = Name(row.TranslationIdentifier, row.Identifier), Semantic = NodeSemantic.Custom },
                        "MAM.NodeType", row.Id, row.Identifier);
                    type.Key = Key(KeyKinds.NodeType, type.Id, row.Identifier, Name(row.TranslationIdentifier, null), "type", row.Id);
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
            // Variant edges wait for the file phase: the processing sweep fails assets that are variant targets without a file
            Guid? seeded = row.Semantic switch
            {
                Link => EdgeType.LinkId,
                Duplicate => EdgeType.DuplicateId,
                Translation => EdgeType.TranslationId,
                _ => null,
            };
            if (seeded is { } id)
            {
                edgeTypeOf[row.Id] = id;
            }
            else if (row.Semantic is CustomRelation or Licence)
            {
                var type = Tenant(new EdgeType { Key = string.Empty, Name = Name(row.TranslationIdentifier, row.Identifier), Semantic = EdgeSemantic.Custom, IsDirected = row.IsDirected },
                    "MAM.EdgeType", row.Id, row.Identifier);
                type.Key = Key(KeyKinds.EdgeType, type.Id, row.Semantic == Licence ? "licence" : row.Identifier, Name(row.TranslationIdentifier, null), "relation", row.Id);
                edgeTypeOf[row.Id] = type.Id;
                created.Add(type);
            }
        }

        return created;
    }

    private List<PropertyDefinition> MapDefinitions(
        Dictionary<long, Guid> groupIds, Dictionary<long, (Guid Id, string Key, int Semantic)> typeOf,
        out Dictionary<long, PropertyDefinition> definitionOf, out Dictionary<long, string> listKeyOf)
    {
        definitionOf = [];
        listKeyOf = [];
        var listValues = source.ListValues.ToLookup(v => v.DefinitionId);
        var nodeTypes = source.DefinitionNodeTypes.ToLookup(t => t.DefinitionId);
        var typeKeys = TypeKeys(typeOf);
        foreach (var row in source.PropertyDefinitions)
        {
            if (MapValueType(row) is not { } valueType)
            {
                report.Problem("unknown_enum", "MAM.PropertyDefinition", row.Id);
                continue;
            }

            // metadata of versions belongs to the asset in Shareme; types without a counterpart (states, DRM objects) do not count
            var linked = nodeTypes[row.Id].ToList();
            var keys = linked.Select(t => typeKeys.GetValueOrDefault(t.NodeTypeId)).OfType<string>().Distinct().ToList();
            if (linked.Count > 0 && keys.Count == 0)
            {
                report.Problem("definition_without_types", "MAM.PropertyDefinition", row.Id);
                continue;
            }

            var definition = Tenant(new PropertyDefinition
            {
                Key = string.Empty,
                Name = Name(row.TranslationIdentifier, row.Identifier),
                Description = Text(row.TranslationIdentifier, "Description") is { } description ? Cap(description, 2000) : null,
                GroupId = row.GroupId is { } g && groupIds.TryGetValue(g, out var groupId) ? groupId : null,
                ValueType = valueType,
                ListMode = !row.HasListValues ? ListModes.None : row.AllowMultipleValues ? ListModes.Multi : ListModes.Single,
                MultiValue = row.AllowMultipleValues && !row.HasListValues,
                AllowCustomValues = row.AllowCustomListValue,
                // a list value is the same in every language, Sharedien only translates its label
                Localizable = row.IsLanguageDependant && !row.HasListValues,

                // ponytail: user contexts come with a later phase; until then context bound values stay behind (reported)
                HasContext = false,
                Editable = row.Editable,
                Searchable = row.SearchOption switch { 1 => SearchModes.Filter, 2 => SearchModes.Fulltext, 3 => SearchModes.Both, _ => SearchModes.None },
                Facet = row.IsSearchableFacet,
                IgnoreOnNewVersion = row.IgnoreCopyingOnNewVersion,
                SortIndex = row.SortKey,
                NodeTypeKeys = keys,
            }, "MAM.PropertyDefinition", row.Id, row.Identifier);
            definition.Key = Key(KeyKinds.Definition, definition.Id, row.Identifier, Name(row.TranslationIdentifier, null), "property", row.Id);

            var listKeys = target.ListValues.Values.Where(v => v.DefinitionId == definition.Id).Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var value in listValues[row.Id].OrderBy(v => v.SortKey).ThenBy(v => v.Id))
            {
                var id = SourceIds.For(Scope, "MAM.PropertyListValue", value.Id, value.Identifier);
                var key = target.ListValues.TryGetValue(id, out var stored)
                    ? stored.Key
                    : Unique(Slug(Guid.TryParse(value.Identifier, out _) ? Name(value.TranslationIdentifier, value.Text) : value.Identifier) ?? $"value-{value.Id}", listKeys);
                var entry = new PropertyListValue
                {
                    DefinitionId = definition.Id,
                    Key = key,
                    Name = Name(value.TranslationIdentifier, value.Text ?? value.Identifier),
                    SortIndex = value.SortKey,
                    IsActive = value.IsActive,
                };
                Ids.Set(entry, id);
                definition.ListValues.Add(entry);
                listKeyOf[value.Id] = key;
            }

            definitionOf[row.Id] = definition;
        }

        return [.. definitionOf.Values];
    }

    /// <summary>Shareme node type keys of Sharedien node types, as far as they have one.</summary>
    private Dictionary<long, string> TypeKeys(Dictionary<long, (Guid Id, string Key, int Semantic)> typeOf)
    {
        var keys = new Dictionary<long, string>();
        foreach (var row in source.NodeTypes)
        {
            string? key = row.Semantic switch
            {
                Category => "folder",
                AssetSet or Version => "asset",
                Workspace => "workspace",
                Task => "task",
                CustomEntity when typeOf.TryGetValue(row.Id, out var custom) => custom.Key,
                _ => null,
            };
            if (key is not null)
            {
                keys[row.Id] = key;
            }
        }

        return keys;
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

            var id = SourceIds.For(Scope, "MAM.Node", row.Id, row.Identifier);
            if (!usedIds.Add(id))
            {
                report.Problem("duplicate_identifier", "MAM.Node", row.Id);
                id = SourceIds.Derived(Scope, "MAM.Node", row.Id);
                usedIds.Add(id);
            }

            Ids.Set(node, id);
            node.NodeTypeId = type.Id;
            node.TenantId = target.TenantId;
            node.SortIndex = row.SequenceNr;
            node.InterruptInheritance = row.InterruptInheritance;
            node.CreatedAt = Utc(row.CreateDate);
            node.ModifiedAt = row.MutDate is { } modified ? Utc(modified) : null;
            nodeOf[row.Id] = node;
        }

        // Shareme is a tree: the first parent wins, further parents become links. A first parent of a later phase
        // (workspace, task) holds the node back with its subtree: placed under the root it would be visible to all.
        var sourceNodes = source.Nodes.Select(n => n.Id).ToHashSet();
        var parentOf = new Dictionary<long, long>();
        var deferred = new HashSet<long>();
        foreach (var edge in source.Edges.Where(e => SemanticOf(e) == ParentChild).OrderBy(e => e.Id))
        {
            if (!nodeOf.ContainsKey(edge.ToNodeId) || !sourceNodes.Contains(edge.FromNodeId))
            {
                continue;
            }

            if (parentOf.ContainsKey(edge.ToNodeId) || deferred.Contains(edge.ToNodeId))
            {
                if (nodeOf.ContainsKey(edge.FromNodeId))
                {
                    report.Problem("multi_parent", "MAM.Edge", edge.Id);
                }
            }
            else if (nodeOf.ContainsKey(edge.FromNodeId))
            {
                parentOf[edge.ToNodeId] = edge.FromNodeId;
            }
            else
            {
                deferred.Add(edge.ToNodeId);
            }
        }

        var children = parentOf.ToLookup(p => p.Value, p => p.Key);
        var placed = new List<Node>();
        var queue = new Queue<(long Id, Node Parent)>(nodeOf.Keys.Where(id => !parentOf.ContainsKey(id) && !deferred.Contains(id)).Select(id => (id, (Node)target.Root)));
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
            var top = lost;
            var steps = 0;
            while (parentOf.TryGetValue(top, out var parent) && steps++ < nodeOf.Count)
            {
                top = parent;
            }

            report.Problem(deferred.Contains(top) ? "deferred_parent" : "cycle", "MAM.Node", lost);
            nodeOf.Remove(lost);
        }

        return placed;
    }

    private List<Edge> MapEdges(Dictionary<long, Node> nodeOf, Dictionary<long, Guid> edgeTypeOf)
    {
        var edges = new List<Edge>();
        var seen = new HashSet<(Guid, Guid, Guid)>();
        foreach (var row in source.Edges.OrderBy(e => e.Id))
        {
            var semantic = SemanticOf(row);
            if (!nodeOf.TryGetValue(row.FromNodeId, out var from) || !nodeOf.TryGetValue(row.ToNodeId, out var to))
            {
                report.Skip(semantic == ParentChild ? "parent edges outside the phase" : "edges to nodes not imported");
                continue;
            }

            Guid type;
            if (semantic == ParentChild)
            {
                // the parent itself is the tree; a second parent stays reachable as a link
                if (to.ParentId == from.Id)
                {
                    continue;
                }

                type = EdgeType.LinkId;
            }
            else if (!edgeTypeOf.TryGetValue(row.EdgeTypeId, out type))
            {
                report.Skip($"edges of semantic {semantic}");
                continue;
            }

            if (seen.Add((type, from.Id, to.Id)))
            {
                var edge = new Edge { EdgeTypeId = type, FromNodeId = from.Id, ToNodeId = to.Id, TenantId = target.TenantId, CreatedAt = DateTimeOffset.UtcNow };
                Ids.Set(edge, SourceIds.For(Scope, "MAM.Edge", row.Id, row.Identifier));
                edges.Add(edge);
            }
        }

        return edges;
    }

    private void MapValues(
        Dictionary<long, Node> nodeOf, Dictionary<long, (Guid Id, string Key, int Semantic)> typeOf, Dictionary<long, PropertyDefinition> definitionOf,
        Dictionary<long, string> listKeyOf, HashSet<string> languages)
    {
        // asset metadata lives on the current version in Sharedien
        var assetOfVersion = source.Versions.GroupBy(v => v.AssetNodeId)
            .Select(g => g.MaxBy(v => v.Version)!).ToDictionary(v => v.VersionNodeId, v => v.AssetNodeId);
        var typeKeyOf = source.Nodes.Where(n => typeOf.ContainsKey(n.NodeTypeId)).ToDictionary(n => n.Id, n => typeOf[n.NodeTypeId].Key);
        var collected = new Dictionary<(long Node, long Definition), List<MetadataValue>>();
        foreach (var row in source.Values.OrderBy(v => v.Id))
        {
            var owner = assetOfVersion.TryGetValue(row.NodeId, out var asset) ? asset : row.NodeId;
            if (!nodeOf.ContainsKey(owner) || !definitionOf.TryGetValue(row.DefinitionId, out var definition))
            {
                report.Skip("values of nodes or definitions not imported");
                continue;
            }

            if (row.UserContextId is not null)
            {
                report.Problem("user_context_not_imported", "MAM.PropertyValue", row.Id);
                continue;
            }

            var values = collected.TryGetValue((owner, row.DefinitionId), out var list) ? list : collected[(owner, row.DefinitionId)] = [];
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

                continue;
            }

            if (definition.Localizable && row.TranslationIdentifier is { } ti && _translations.TryGetValue((ti, "TextValue"), out var texts))
            {
                values.AddRange(texts.Select(t => new MetadataValue(Lang: t.Key, V: JsonSerializer.SerializeToElement(t.Value))));
            }
            else if (Value(definition.ValueType, row) is { } value)
            {
                values.Add(new MetadataValue(Lang: definition.Localizable ? _defaultLanguage : null, V: value));
            }
            else
            {
                report.Problem("invalid_value", "MAM.PropertyValue", row.Id);
            }
        }

        foreach (var ((owner, definitionId), values) in collected.OrderBy(c => c.Key))
        {
            var definition = definitionOf[definitionId];
            if (!definition.AppliesTo(typeKeyOf[owner]))
            {
                report.Problem("property_not_for_type", "MAM.Node", owner);
                continue;
            }

            // single valued properties keep their first value per language and context
            var single = definition.ListMode != ListModes.Multi && !definition.MultiValue;
            var kept = single ? values.GroupBy(v => (v.Lang, v.Ctx)).Select(g => g.First()).ToList() : values;
            if (kept.Count < values.Count)
            {
                report.Problem("extra_values_dropped", "MAM.Node", owner);
            }

            // the same checks as the API, so every imported value can be edited and searched like a typed one
            if (kept.Count == 0)
            {
                continue;
            }

            if (MetadataRules.Check(definition, kept, languages, [], out var normalized) is [var first, ..])
            {
                // the API message without quoted parts (keys, codes), so the report names the rule but no content
                report.Problem("invalid_property: " + System.Text.RegularExpressions.Regex.Replace(first, "'[^']*'", "'…'"), "MAM.Node", owner);
                continue;
            }

            nodeOf[owner].Metadata[definition.Key] = normalized;
        }
    }

    public static JsonElement? Value(string valueType, ValueRow row)
    {
        object? value = valueType switch
        {
            PropertyValueTypes.String or PropertyValueTypes.Text or PropertyValueTypes.Url => row.Text,
            PropertyValueTypes.Date => (row.Date ?? row.DateTime)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PropertyValueTypes.DateTime => row.DateTime is { } moment ? Utc(moment).ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture) : null,
            PropertyValueTypes.Time => row.DateTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                ?? (row.DurationTicks is { } t ? TimeSpan.FromTicks(t).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : row.Text),
            PropertyValueTypes.Integer => row.Int,
            PropertyValueTypes.Decimal => row.Decimal is { } number ? number / 1.0000000000000000000000000000m : null, // drops the scale-10 trailing zeros
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
        Ids.Set(entity, SourceIds.For(Scope, table, sourceId, identifier));
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

    /// <summary>The stored key of an entity that exists already, otherwise a new one unique within its kind.</summary>
    private string Key(string kind, Guid id, string identifier, string? name, string fallback, long sourceId)
    {
        var stored = target.KeysOf(kind);
        if (stored.TryGetValue(id, out var key))
        {
            return key;
        }

        var taken = _takenKeys.TryGetValue(kind, out var set) ? set : _takenKeys[kind] = new HashSet<string>(stored.Values, StringComparer.Ordinal);
        return Unique((Guid.TryParse(identifier, out _) ? Slug(name) : Slug(identifier)) ?? $"{fallback}-{sourceId}", taken);
    }

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
