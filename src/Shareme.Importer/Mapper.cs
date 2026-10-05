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
    IReadOnlyList<Coworkee.Identity.Domain.ResourcePermission> NodePermissions,
    IReadOnlyList<UserContext> UserContexts,
    IReadOnlyList<Restriction> Restrictions,
    IReadOnlyList<Shareme.Workspaces.WorkflowState> States,
    IReadOnlyList<Shareme.Workspaces.Comment> Comments,
    IReadOnlyList<VersionCandidate> Versions,
    DrmPlan Drm,
    RenditionTypePlan Renditions);

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

    /// <summary>Whether this run copies files; without them assets stay "importing" and variant edges wait.</summary>
    public bool WithFiles { get; init; }

    /// <summary>The tenant's workspace roles by key (owner, collaborator), as far as they exist.</summary>
    public IReadOnlyDictionary<string, Guid> WorkspaceRoles { get; init; } = new Dictionary<string, Guid>();

    public IReadOnlyDictionary<Guid, string> KeysOf(string kind) => Keys.TryGetValue(kind, out var keys) ? keys : new Dictionary<Guid, string>();
}

public static class KeyKinds
{
    public const string NodeType = "node type";
    public const string EdgeType = "edge type";
    public const string Group = "property group";
    public const string Definition = "property definition";
    public const string UserContext = "user context";
    public const string RenditionType = "rendition type";
}

/// <summary>Maps Sharedien rows to Shareme entities. Pure: no database, so every rule is testable with synthetic rows.</summary>
public sealed class SharedienMapper(SourceData source, MapTarget target, ImportReport report)
{
    // Sharedien NodeTypeSemantic and EdgeTypeSemantic values
    private const int Category = 1, AssetSet = 2, Version = 3, Workspace = 4, Task = 5, CustomEntity = 1000;
    private const int ParentChild = 1, Link = 3, Translation = 5, Variant = 6, Licence = 7, Duplicate = 8, Transition = 9, LicenseDrm = 10, CustomRelation = 0;

    private readonly Dictionary<long, string> _languageCodes = source.Languages.ToDictionary(l => l.Id, l => LanguageCode(l.Identifier));
    private readonly string _defaultLanguage = source.Languages.FirstOrDefault(l => l.IsDefault) is { } d ? LanguageCode(d.Identifier) : "en";
    private readonly Dictionary<(string, string), Dictionary<string, string>> _translations = [];
    private readonly Dictionary<string, HashSet<string>> _takenKeys = [];

    private string? _defaultContext;

    // the catalog's single context when a tenant has none of its own
    private const string VirtualDefaultContext = "default";

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

        var contexts = MapContexts(out var contextKeyOf);
        var definitions = MapDefinitions(groupIds, typeOf, out var definitionOf, out var listKeyOf);
        var nodes = MapNodes(typeOf, identity, out var nodeOf, out var stateIds, out var taskLinks);
        foreach (var row in source.Nodes)
        {
            if (row.CreateUser is { } creator && identity.UserByName.TryGetValue(creator, out var userId) && nodeOf.TryGetValue(row.Id, out var created))
            {
                created.CreatedBy = userId;
            }
        }

        var nodePermissions = identityMapper.MapNodePermissions(identity, nodeOf.ToDictionary(n => n.Key, n => n.Value.Id));
        var edges = MapEdges(nodeOf, edgeTypeOf, taskLinks);
        var states = MapStates(nodeOf, stateIds);
        var comments = MapComments(nodeOf, identity);
        var versions = MapVersions(nodeOf);
        var drm = new DrmMapper(source.Drm ?? SourceDrm.Empty, target.TenantId, report, Name).Map(
            [.. source.Edges.Where(e => SemanticOf(e) == LicenseDrm).Select(e => (e.FromNodeId, e.ToNodeId))], nodeOf, contextKeyOf);
        MapValues(nodeOf, typeOf, definitionOf, listKeyOf, contextKeyOf, [.. target.LanguageCodes, .. languages.Select(l => l.Code)],
            target.KeysOf(KeyKinds.UserContext).Count + contexts.Count == 0 ? [VirtualDefaultContext] : [.. target.KeysOf(KeyKinds.UserContext).Values, .. contexts.Select(c => c.Key)]);
        var restrictions = MapRestrictions(identity, definitionOf, listKeyOf);
        var renditions = new RenditionMapper(source.Renditions ?? SourceRenditions.Empty, target.TenantId, target.KeysOf(KeyKinds.RenditionType), report, Name)
            .Map(identity, target.AdminRoleId);

        report.Mapped["languages"] = languages.Count;
        report.Mapped["node types"] = nodeTypes.Count;
        report.Mapped["edge types"] = edgeTypes.Count;
        report.Mapped["property groups"] = groups.Count;
        report.Mapped["property definitions"] = definitions.Count;
        report.Mapped["nodes"] = nodes.Count;
        report.Mapped["edges"] = edges.Count;
        report.Mapped["workflow states"] = states.Count;
        report.Mapped["comments"] = comments.Count;
        report.Mapped["user contexts"] = contexts.Count;
        report.Mapped["restrictions"] = restrictions.Count;
        return new ImportPlan(languages, nodeTypes, edgeTypes, groups, definitions, nodes, edges, identity, nodePermissions, contexts, restrictions, states, comments, versions, drm, renditions);
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

    private List<UserContext> MapContexts(out Dictionary<long, string> contextKeyOf)
    {
        contextKeyOf = [];
        var contexts = new List<UserContext>();
        var hasDefault = target.KeysOf(KeyKinds.UserContext).Count > 0;
        foreach (var row in source.UserContexts ?? [])
        {
            var context = Tenant(new UserContext { Key = string.Empty, Name = Name(row.TranslationIdentifier, row.Identifier), IsDefault = row.IsDefault && !hasDefault },
                "MAM.UserContext", row.Id, row.Identifier);
            context.Key = Key(KeyKinds.UserContext, context.Id, row.Identifier, Name(row.TranslationIdentifier, null), "context", row.Id);
            hasDefault |= context.IsDefault;
            if (row.IsDefault || _defaultContext is null)
            {
                _defaultContext = context.Key;
            }
            contexts.Add(context);
            contextKeyOf[row.Id] = context.Key;
        }

        return contexts;
    }

    /// <summary>
    /// Property blacklists of roles: reading forbidden hides the property, changing forbidden makes it read only. Shareme
    /// applies restrictions through tenant roles; a role used only on nodes cannot carry one (reported).
    /// </summary>
    private List<Restriction> MapRestrictions(IdentityPlan identity, Dictionary<long, PropertyDefinition> definitionOf, Dictionary<long, string> listKeyOf)
    {
        var restrictions = new Dictionary<(Guid Role, string Key), Restriction>();
        foreach (var row in source.Identity?.PropertyRestrictions ?? [])
        {
            if (!definitionOf.TryGetValue(row.DefinitionId, out var definition))
            {
                report.Skip("restrictions of definitions not imported");
                continue;
            }

            if (RestrictedRole(identity, target.AdminRoleId, row.RoleId, "Admin.PropertyDefinitionRestriction", row.Id, report) is not { } role)
            {
                continue;
            }

            // AuthorizableAction: Read 0, Create 1, Update 2, Delete 3, View 4, Any 5
            var kind = row.Action is 1 or 2 or 3 ? Shareme.Contracts.Graph.RestrictionKinds.ReadOnlyProperty : Shareme.Contracts.Graph.RestrictionKinds.HiddenProperty;
            if (restrictions.TryGetValue((role, definition.Key), out var existing) && existing.Kind == Shareme.Contracts.Graph.RestrictionKinds.HiddenProperty)
            {
                continue;
            }

            var restriction = new Restriction { RoleId = role, Kind = kind, PropertyKey = definition.Key, TenantId = target.TenantId, CreatedAt = DateTimeOffset.UtcNow };
            Ids.Set(restriction, SourceIds.Derived(Scope, "Admin.PropertyDefinitionRestriction:" + definition.Key, BitConverter.ToInt64(role.ToByteArray())));
            restrictions[(role, definition.Key)] = restriction;
        }

        // value blacklists: Sharedien keeps the list value on a property without node; a role must not see nodes carrying it
        var hidden = new Dictionary<(Guid Role, string Key, string Value), Restriction>();
        foreach (var row in source.Identity?.ListValueRestrictions ?? [])
        {
            if (row.Action is not (0 or 4 or 5))
            {
                report.Skip("list value restrictions on changing");
                continue;
            }

            if (!definitionOf.TryGetValue(row.DefinitionId, out var definition) || row.ListValueId is not { } valueId || !listKeyOf.TryGetValue(valueId, out var value))
            {
                report.Problem("value_restriction_without_list_value", "Admin.PropertyValueRestriction", row.Id);
                continue;
            }

            if (RestrictedRole(identity, target.AdminRoleId, row.RoleId, "Admin.PropertyValueRestriction", row.Id, report) is not { } role || hidden.ContainsKey((role, definition.Key, value)))
            {
                continue;
            }

            var restriction = new Restriction
            {
                RoleId = role, Kind = Shareme.Contracts.Graph.RestrictionKinds.HiddenListValue, PropertyKey = definition.Key, Value = value, TenantId = target.TenantId, CreatedAt = DateTimeOffset.UtcNow,
            };
            Ids.Set(restriction, SourceIds.Derived(Scope, $"Admin.PropertyValueRestriction:{definition.Key}={value}", BitConverter.ToInt64(role.ToByteArray())));
            hidden[(role, definition.Key, value)] = restriction;
        }

        return [.. restrictions.Values, .. hidden.Values];
    }

    /// <summary>
    /// The tenant role a Sharedien restriction applies to. Shareme applies restrictions through tenant roles only: a role used
    /// only on nodes cannot carry one, and members through node assignments stay unrestricted (both reported); GlobalAdmin
    /// passes every check in Sharedien.
    /// </summary>
    internal static Guid? RestrictedRole(IdentityPlan identity, Guid? adminRoleId, long roleId, string table, long sourceId, ImportReport report)
    {
        if (!identity.TenantRoleIds.TryGetValue(roleId, out var role))
        {
            if (identity.NodeRoleIds.ContainsKey(roleId))
            {
                report.Problem("restriction_on_item_role", table, sourceId);
            }
            else
            {
                report.Skip("restrictions of roles without rights");
            }

            return null;
        }

        if (role == adminRoleId)
        {
            report.Skip("restrictions of global admin roles");
            return null;
        }

        if (identity.NodeRoleIds.ContainsKey(roleId))
        {
            report.Problem("restriction_not_on_item_role", table, sourceId);
        }

        return role;
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
                case Workspace:
                    typeOf[row.Id] = (NodeType.WorkspaceTypeId, "workspace", Workspace);
                    break;
                case Task:
                    typeOf[row.Id] = (NodeType.TaskTypeId, "task", Task);
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
                Variant when target.WithFiles => EdgeType.VariantId,
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

                HasContext = row.HasUserContext,
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

    private SourceWorkspaces Work => source.Workspaces ?? SourceWorkspaces.Empty;

    private List<Node> MapNodes(
        Dictionary<long, (Guid Id, string Key, int Semantic)> typeOf, IdentityPlan identity, out Dictionary<long, Node> nodeOf,
        out Dictionary<long, Guid> stateIds, out List<(long Task, long Asset)> taskLinks)
    {
        var nodes = new Dictionary<long, Node>();
        nodeOf = nodes;
        var latest = source.Versions.GroupBy(v => v.AssetNodeId).ToDictionary(g => g.Key, g => g.MaxBy(v => v.Version)!);
        var workspaces = Work.Workspaces.ToDictionary(w => w.NodeId);
        var tasks = Work.Tasks.ToDictionary(t => t.NodeId);
        var usedIds = new HashSet<Guid>();
        foreach (var row in source.Nodes)
        {
            if (!typeOf.TryGetValue(row.NodeTypeId, out var type))
            {
                continue; // versions, states, comments, DRM objects: mapped elsewhere or in later phases
            }

            Node node = type.Semantic switch
            {
                Category => new Folder { Name = Name(row.TranslationIdentifier, row.Identifier), Names = Names(row.TranslationIdentifier) },
                AssetSet => new Asset
                {
                    Name = Cap(latest.TryGetValue(row.Id, out var version) && version.OriginalFilename is { Length: > 0 } file ? file : row.Identifier, 400),
                    Status = row.AssetSetStatus == 2 ? "duplicate" : "importing",
                },
                Workspace when workspaces.TryGetValue(row.Id, out var w) => new Shareme.Graph.Workspace
                {
                    Name = Name(w.TranslationIdentifier, row.Identifier),
                    Names = Names(w.TranslationIdentifier),
                    WorkspaceKind = w.Type switch { 0 => "upload", 1 => "collection", _ => "collaboration" },
                    IsTemplate = w.IsTemplate,
                    CopyMetadataToAssets = w.CopyProperties,
                },
                Task when tasks.TryGetValue(row.Id, out var t) => new WorkTask
                {
                    Name = Cap(t.Name is { Length: > 0 } taskName ? taskName : row.Identifier, 400),
                    Description = t.Description is { } description ? Cap(description, 8000) : null,
                    DueDate = t.DueDate is { } due ? DateOnly.FromDateTime(due) : null,
                    AssigneeIds = t.AssignedUserId is { } assignee && identity.UserIds.TryGetValue(assignee, out var user) ? [user] : [],
                    CompletedAt = t.Status == 1 ? Utc(row.MutDate ?? row.CreateDate) : null,
                },
                Workspace or Task => null!,
                _ => new CustomNode { Name = Name(row.TranslationIdentifier, row.Identifier), Names = Names(row.TranslationIdentifier) },
            };
            if (node is null)
            {
                report.Problem("missing_detail_row", "MAM.Node", row.Id);
                continue;
            }

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
            nodes[row.Id] = node;
        }

        // first parent edge of every node; states are no nodes in Shareme: a child of a state hangs at the state's workspace
        var sourceNodes = source.Nodes.Select(n => n.Id).ToHashSet();
        var states = Work.States.Select(st => st.NodeId).ToHashSet();
        var firstParent = new Dictionary<long, long>();
        var extraParents = new List<EdgeRow>();
        foreach (var edge in source.Edges.Where(e => SemanticOf(e) == ParentChild).OrderBy(e => e.Id))
        {
            if (sourceNodes.Contains(edge.FromNodeId) && !firstParent.TryAdd(edge.ToNodeId, edge.FromNodeId))
            {
                extraParents.Add(edge);
            }
        }

        _firstParent = firstParent;
        long Through(long parent) => states.Contains(parent) ? firstParent.GetValueOrDefault(parent, -1) : parent;

        // Sharedien hangs every workspace under a state of one system workspace: workspaces are roots here, the container stays out
        foreach (var (child, from) in firstParent)
        {
            if (nodes.TryGetValue(child, out var node) && node is Shareme.Graph.Workspace
                && nodes.TryGetValue(Through(from), out var container) && container is Shareme.Graph.Workspace)
            {
                _containers.Add(Through(from));
            }
        }

        foreach (var container in _containers)
        {
            report.Skip("workspace containers");
            nodes.Remove(container);
        }

        var identifiers = source.Nodes.ToDictionary(n => n.Id, n => n.Identifier);
        stateIds = Work.States.Where(st => firstParent.TryGetValue(st.NodeId, out var w) && nodes.TryGetValue(w, out var owner) && owner is Shareme.Graph.Workspace)
            .ToDictionary(st => st.NodeId, st => SourceIds.For(Scope, "MAM.State", st.NodeId, identifiers.GetValueOrDefault(st.NodeId)));
        taskLinks = [];
        var parentOf = new Dictionary<long, long>();
        var deferred = new HashSet<long>();
        var lostParent = new HashSet<long>();
        foreach (var (child, from) in firstParent)
        {
            if (!nodes.TryGetValue(child, out var node) || node is Shareme.Graph.Workspace)
            {
                continue;
            }

            if (node is WorkTask task && states.Contains(from) && stateIds.TryGetValue(from, out var stateId))
            {
                task.StateId = stateId;
            }

            var parent = Through(from);
            if (nodes.TryGetValue(parent, out var parentNode) && parentNode is WorkTask)
            {
                // files of a task live in its workspace and stay linked to the task
                taskLinks.Add((parent, child));
                parent = Through(firstParent.GetValueOrDefault(parent, -1));
            }

            if (nodes.TryGetValue(parent, out var resolved) && resolved is not WorkTask)
            {
                parentOf[child] = parent;
            }
            else if (sourceNodes.Contains(parent) && !nodes.ContainsKey(parent))
            {
                deferred.Add(child);
            }
            else
            {
                lostParent.Add(child); // a state or task without its own parent: it would end up under the root
            }
        }

        foreach (var edge in extraParents.Where(e => nodes.ContainsKey(e.FromNodeId) && nodes.ContainsKey(e.ToNodeId)))
        {
            report.Problem("multi_parent", "MAM.Edge", edge.Id);
        }

        var children = parentOf.ToLookup(p => p.Value, p => p.Key);
        var placed = new List<Node>();
        var queue = new Queue<(long Id, Node? Parent)>(nodes
            .Where(n => !parentOf.ContainsKey(n.Key) && !deferred.Contains(n.Key) && !lostParent.Contains(n.Key) && n.Value is not WorkTask)
            .Select(n => (n.Key, n.Value is Shareme.Graph.Workspace ? null : (Node?)target.Root)));
        while (queue.TryDequeue(out var item))
        {
            var node = nodes[item.Id];
            node.PlaceUnder(node is Shareme.Graph.Workspace ? null : item.Parent); // a workspace is a root of its own
            placed.Add(node);
            foreach (var child in children[item.Id])
            {
                queue.Enqueue((child, node));
            }
        }

        var placedIds = placed.Select(n => n.Id).ToHashSet();
        foreach (var lost in nodes.Where(n => !placedIds.Contains(n.Value.Id)).Select(n => n.Key).ToList())
        {
            var top = lost;
            var steps = 0;
            while (parentOf.TryGetValue(top, out var parent) && steps++ < nodes.Count)
            {
                top = parent;
            }

            report.Problem(deferred.Contains(top) ? "deferred_parent" : lostParent.Contains(top) || nodes[top] is WorkTask ? "lost_parent" : "cycle", "MAM.Node", lost);
            nodes.Remove(lost);
        }

        foreach (var (sourceId, node) in nodes)
        {
            if (node is Shareme.Graph.Workspace workspace && workspaces[sourceId].FinalizationNodeId is { } folder && nodes.TryGetValue(folder, out var finalization) && finalization is Folder)
            {
                workspace.FinalizationFolderId = finalization.Id;
            }
        }

        // Shareme takes tasks only in collaboration workspaces
        foreach (var workspace in nodes.Values.OfType<WorkTask>().Select(t => nodes.Values.FirstOrDefault(w => w.Id == t.ParentId)).OfType<Shareme.Graph.Workspace>().Distinct())
        {
            if (workspace.WorkspaceKind != "collaboration")
            {
                report.Problem("workspace_with_tasks_now_collaboration", "MAM.Node", nodes.First(n => n.Value == workspace).Key);
                workspace.WorkspaceKind = "collaboration";
            }
        }

        return placed;
    }

    private Dictionary<long, long> _firstParent = [];
    private readonly HashSet<long> _containers = [];

    private List<Shareme.Workspaces.WorkflowState> MapStates(Dictionary<long, Node> nodeOf, Dictionary<long, Guid> stateIds)
    {
        var transitions = source.Edges.Where(e => SemanticOf(e) == Transition).ToLookup(e => e.FromNodeId, e => e.ToNodeId);
        var result = new List<Shareme.Workspaces.WorkflowState>();
        foreach (var row in Work.States)
        {
            if (!stateIds.TryGetValue(row.NodeId, out var id) || !nodeOf.TryGetValue(_firstParent[row.NodeId], out var workspace))
            {
                report.Skip(_containers.Contains(_firstParent.GetValueOrDefault(row.NodeId, -1)) ? "states of workspace containers" : "states of workspaces not imported");
                continue;
            }

            var state = new Shareme.Workspaces.WorkflowState
            {
                WorkspaceId = workspace.Id,
                Name = Cap(Name(row.TranslationIdentifier, "State"), 100),
                SortIndex = row.SequenceNr,
                IsDefault = row.IsDefault,
                TenantId = target.TenantId,
                AllowedTargets = [.. transitions[row.NodeId].Where(stateIds.ContainsKey).Select(t => stateIds[t]).Distinct()],
            };
            Ids.Set(state, id);
            result.Add(state);
        }

        foreach (var board in result.GroupBy(s => s.WorkspaceId))
        {
            // exactly one column where new tasks start: the source default, else the first
            var start = board.Where(s => s.IsDefault).OrderBy(s => s.SortIndex).FirstOrDefault() ?? board.MinBy(s => s.SortIndex)!;
            foreach (var state in board)
            {
                state.IsDefault = state == start;
            }

            // with a workflow, a state nothing leads out of is where tasks end
            if (board.Any(s => s.AllowedTargets.Count > 0))
            {
                foreach (var state in board.Where(s => s.AllowedTargets.Count == 0))
                {
                    state.IsDone = true;
                }
            }
        }

        var surviving = result.Select(s => s.Id).ToHashSet();
        foreach (var task in nodeOf.Values.OfType<WorkTask>().Where(t => t.StateId is { } state && !surviving.Contains(state)))
        {
            task.StateId = null;
        }

        return result;
    }

    /// <summary>Every Sharedien version with an original file becomes a file version of its asset.</summary>
    private List<VersionCandidate> MapVersions(Dictionary<long, Node> nodeOf)
    {
        var originals = (source.Originals ?? []).Where(o => o.DataId is { Length: > 0 }).GroupBy(o => o.VersionNodeId).ToDictionary(g => g.Key, g => g.First());
        var identifiers = source.Nodes.ToDictionary(n => n.Id, n => n.Identifier);
        var result = new List<VersionCandidate>();
        foreach (var versions in source.Versions.GroupBy(v => v.AssetNodeId))
        {
            if (!nodeOf.TryGetValue(versions.Key, out var node) || node is not Asset asset)
            {
                continue;
            }

            var current = versions.Max(v => v.Version);
            foreach (var version in versions.OrderBy(v => v.Version))
            {
                if (!originals.TryGetValue(version.VersionNodeId, out var original))
                {
                    report.Problem("version_without_original", "MAM.AssetVersion", version.VersionNodeId);
                    continue;
                }

                result.Add(new VersionCandidate(
                    version.VersionNodeId, asset, SourceIds.For(Scope, "MAM.Node", version.VersionNodeId, identifiers.GetValueOrDefault(version.VersionNodeId)),
                    version.Version, Cap(version.OriginalFilename is { Length: > 0 } name ? name : asset.Name, 255), original.DataId!, original.FileHash,
                    Utc(version.Created), version.Version == current));
            }
        }

        report.Mapped["file versions"] = result.Count;
        return result;
    }

    private List<Shareme.Workspaces.Comment> MapComments(Dictionary<long, Node> nodeOf, IdentityPlan identity)
    {
        var parent = source.Edges.Where(e => SemanticOf(e) == ParentChild).GroupBy(e => e.ToNodeId).ToDictionary(g => g.Key, g => g.MinBy(e => e.Id)!.FromNodeId);
        var result = new List<Shareme.Workspaces.Comment>();
        foreach (var row in Work.Comments)
        {
            if (row.Text is { Length: > 8000 })
            {
                report.Problem("comment_shortened", "MAM.Comment", row.NodeId);
            }

            if (!parent.TryGetValue(row.NodeId, out var on) || !nodeOf.TryGetValue(on, out var node))
            {
                report.Skip("comments of nodes not imported");
                continue;
            }

            if (row.CreatedBy is not { } author || !identity.UserByName.TryGetValue(author, out var authorId) || string.IsNullOrWhiteSpace(row.Text))
            {
                report.Problem("comment_without_author_or_text", "MAM.Comment", row.NodeId);
                continue;
            }

            var comment = new Shareme.Workspaces.Comment
            {
                NodeId = node.Id,
                AuthorId = authorId,
                Text = Cap(row.Text, 8000),
                TenantId = target.TenantId,
                CreatedAt = row.CreatedAt is { } at ? Utc(at) : DateTimeOffset.UtcNow,
                CreatedBy = authorId,
            };
            Ids.Set(comment, SourceIds.Derived(Scope, "MAM.Comment", row.NodeId));
            result.Add(comment);
        }

        return result;
    }

    private List<Edge> MapEdges(Dictionary<long, Node> nodeOf, Dictionary<long, Guid> edgeTypeOf, List<(long Task, long Asset)> taskLinks)
    {
        var edges = new List<Edge>();
        var seen = new HashSet<(Guid, Guid, Guid)>();
        foreach (var (task, asset) in taskLinks.Where(l => nodeOf.ContainsKey(l.Task) && nodeOf.ContainsKey(l.Asset)))
        {
            if (seen.Add((EdgeType.LinkId, nodeOf[task].Id, nodeOf[asset].Id)))
            {
                var link = new Edge { EdgeTypeId = EdgeType.LinkId, FromNodeId = nodeOf[task].Id, ToNodeId = nodeOf[asset].Id, TenantId = target.TenantId, CreatedAt = DateTimeOffset.UtcNow };
                Ids.Set(link, SourceIds.Derived(Scope, "TaskLink:" + task, asset));
                edges.Add(link);
            }
        }

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
                if (to.ParentId == from.Id || from is WorkTask)
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
        Dictionary<long, string> listKeyOf, Dictionary<long, string> contextKeyOf, HashSet<string> languages, HashSet<string> contexts)
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

            string? ctx = null;
            if (row.UserContextId is { } contextId && !contextKeyOf.TryGetValue(contextId, out ctx))
            {
                report.Problem("unknown_user_context", "MAM.PropertyValue", row.Id);
                continue;
            }

            // Sharedien lets a value without context stand for every context; Shareme names one: the default
            ctx = definition.HasContext ? ctx ?? _defaultContext ?? VirtualDefaultContext : null;

            var values = collected.TryGetValue((owner, row.DefinitionId), out var list) ? list : collected[(owner, row.DefinitionId)] = [];
            if (row.ListValueId is { } listValue)
            {
                if (listKeyOf.TryGetValue(listValue, out var key))
                {
                    values.Add(new MetadataValue(Ctx: ctx, List: key));
                }
                else
                {
                    report.Problem("unknown_list_value", "MAM.PropertyValue", row.Id);
                }

                continue;
            }

            if (definition.Localizable && row.TranslationIdentifier is { } ti && _translations.TryGetValue((ti, "TextValue"), out var texts))
            {
                values.AddRange(texts.Select(t => new MetadataValue(Lang: t.Key, Ctx: ctx, V: JsonSerializer.SerializeToElement(t.Value))));
            }
            else if (Value(definition.ValueType, row) is { } value)
            {
                values.Add(new MetadataValue(Lang: definition.Localizable ? _defaultLanguage : null, Ctx: ctx, V: value));
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

            if (MetadataRules.Check(definition, kept, languages, contexts, out var normalized) is [var first, ..])
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
