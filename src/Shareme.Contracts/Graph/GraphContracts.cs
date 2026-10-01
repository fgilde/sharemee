namespace Shareme.Contracts.Graph;

public static class NodeTypeKeys
{
    public const string Folder = "folder";
    public const string Asset = "asset";
}

public static class EdgeTypeKeys
{
    public const string Link = "link";
    public const string Duplicate = "duplicate";
    public const string Variant = "variant";
    public const string Translation = "translation";
}

public static class NodePermissions
{
    public const string ResourceType = "Node";
    public const string GroupName = "Nodes";
    public const string Read = "Nodes.Read";
    public const string Create = "Nodes.Create";
    public const string Update = "Nodes.Update";
    public const string Delete = "Nodes.Delete";
    public const string Move = "Nodes.Move";
    public const string ManagePermissions = "Nodes.ManagePermissions";
    public const string Admin = "Nodes.Admin";
}

public static class NodeSorts
{
    public const string Name = "name";
    public const string Created = "created";
    public const string Manual = "manual";
}

public sealed record NodeDto(
    Guid Id,
    Guid? ParentId,
    string Type,
    string Semantic,
    string Name,
    IReadOnlyDictionary<string, string> Names,
    int SortIndex,
    bool InterruptInheritance,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

public sealed record BreadcrumbDto(Guid Id, string Name);

public sealed record NodeDetailDto(NodeDto Node, IReadOnlyList<BreadcrumbDto> Breadcrumb, bool IsSubscribed);

public sealed record CreateNodeRequest(Guid ParentId, string Name, string Type = NodeTypeKeys.Folder, IReadOnlyDictionary<string, string>? Names = null);

public sealed record UpdateNodeRequest(string Name, IReadOnlyDictionary<string, string>? Names = null, int? SortIndex = null);

public sealed record MoveNodeRequest(Guid TargetParentId);

public sealed record InheritanceRequest(bool Interrupt);

public sealed record EdgeDto(Guid Id, string Type, Guid FromNodeId, Guid ToNodeId, string FromName, string ToName);

public sealed record CreateEdgeRequest(string Type, Guid ToNodeId);

public sealed record RecycleBinItemDto(Guid Id, string Name, string Type, Guid? OriginalParentId, DateTimeOffset? DeletedAt, Guid? DeletedBy);

public sealed record NodeTypeDto(string Key, string Semantic, string Name, string? Icon, bool IsSystem);

public sealed record CreateNodeTypeRequest(string Key, string Name, string? Icon);
