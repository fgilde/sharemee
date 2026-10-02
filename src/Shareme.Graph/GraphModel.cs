using System.Text.Json;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;

namespace Shareme.Graph;

public enum NodeSemantic
{
    Folder,
    Asset,
    Custom,

    /// <summary>A root of its own: members get access through grants on it, never through the folder tree.</summary>
    Workspace,

    /// <summary>A card on a workspace's board; lives directly under its workspace.</summary>
    Task,
}

public enum EdgeSemantic
{
    Link,
    Duplicate,
    Variant,
    Translation,
    Custom,
}

public sealed class NodeType : Entity
{
    public static readonly Guid FolderTypeId = new("0190f000-0000-7000-8000-000000000001");
    public static readonly Guid AssetTypeId = new("0190f000-0000-7000-8000-000000000002");
    public static readonly Guid WorkspaceTypeId = new("0190f000-0000-7000-8000-000000000003");
    public static readonly Guid TaskTypeId = new("0190f000-0000-7000-8000-000000000004");

    public required string Key { get; set; }

    public NodeSemantic Semantic { get; set; }

    public required string Name { get; set; }

    public string? Icon { get; set; }

    public bool IsSystem { get; set; }

    public Guid? TenantId { get; set; }
}

public sealed record NodeChanged(Guid NodeId, string Kind) : IDomainEvent;

/// <summary>Kinds of <see cref="NodeChanged"/> that affect the whole subtree, not only the node.</summary>
public static class NodeChangeKinds
{
    public const string Moved = "moved";
    public const string Deleted = "deleted";
    public const string Restored = "restored";
    public const string Permissions = "permissions changed";

    public static readonly IReadOnlySet<string> Subtree = new HashSet<string> { Moved, Deleted, Restored, Permissions };
}

[Realtime(NodePermissions.Read, ResourceType = NodePermissions.ResourceType)]
public abstract class Node : AuditedAggregateRoot, IMultiTenant, ISoftDelete, IHasConcurrencyToken, IHasRealtimeTopics
{
    public Guid NodeTypeId { get; set; }

    public Guid? ParentId { get; private set; }

    public string Path { get; private set; } = string.Empty;

    public int Depth { get; private set; }

    public string Name { get; set; } = string.Empty;

    public Dictionary<string, string> Names { get; set; } = [];

    /// <summary>Own property values by definition key; validated by Shareme.Metadata.</summary>
    public Dictionary<string, List<MetadataValue>> Metadata { get; set; } = [];

    public int SortIndex { get; set; }

    public bool InterruptInheritance { get; set; }

    public Guid TenantId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public Guid? DeletedWithId { get; set; }

    public uint Version { get; set; }

    public Guid? PreviousParentId { get; private set; }

    public IEnumerable<string> RealtimeTopics =>
        // own id too: viewers of this folder learn about its rename, move or deletion
        new[] { ParentId, PreviousParentId, Id }.OfType<Guid>().Distinct().Select(GraphTopics.Children);

    public IReadOnlyList<Guid> PathIds => NodePaths.Ids(Path);

    public abstract NodeSemantic Semantic { get; }

    public void PlaceUnder(Node? parent)
    {
        PreviousParentId = ParentId;
        ParentId = parent?.Id;
        Path = (parent?.Path ?? "/") + Id + "/";
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    public void MarkChanged(string kind) => Raise(new NodeChanged(Id, kind));

    /// <summary>Lets modules that extend nodes raise their own events on the node.</summary>
    public void Publish(IDomainEvent domainEvent) => Raise(domainEvent);
}

public sealed class Folder : Node
{
    public override NodeSemantic Semantic => NodeSemantic.Folder;
}

public sealed class Asset : Node
{
    public override NodeSemantic Semantic => NodeSemantic.Asset;

    public string AssetTypeKey { get; set; } = "other";

    public Guid? CurrentVersionId { get; set; }

    public string Status { get; set; } = "importing";

    /// <summary>Changes whenever the current version's thumbnail becomes available; null while there is none. Part of rendition URLs so caches never serve stale pictures.</summary>
    public string? RenditionTag { get; set; }
}

public sealed class CustomNode : Node
{
    public override NodeSemantic Semantic => NodeSemantic.Custom;
}

/// <summary>A place to work together; see Shareme.Workspaces for kinds, members, templates and finalisation.</summary>
public sealed class Workspace : Node
{
    public override NodeSemantic Semantic => NodeSemantic.Workspace;

    public string WorkspaceKind { get; set; } = "collaboration";

    public bool IsTemplate { get; set; }

    public Guid? TemplateId { get; set; }

    public Guid? FinalizationFolderId { get; set; }

    public bool CopyMetadataToAssets { get; set; }

    public Guid? RequiredSetId { get; set; }

    public DateTimeOffset? FinalizedAt { get; set; }
}

/// <summary>A task of a workspace: title is the node name, the state is one of the workspace's states, the order within a state is the sort index.</summary>
public sealed class WorkTask : Node
{
    public override NodeSemantic Semantic => NodeSemantic.Task;

    public string? Description { get; set; }

    public DateOnly? DueDate { get; set; }

    public Guid? StateId { get; set; }

    public List<Guid> AssigneeIds { get; set; } = [];

    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class EdgeType : Entity
{
    public static readonly Guid LinkId = new("0190f000-0000-7000-8000-000000000101");
    public static readonly Guid DuplicateId = new("0190f000-0000-7000-8000-000000000102");
    public static readonly Guid VariantId = new("0190f000-0000-7000-8000-000000000103");
    public static readonly Guid TranslationId = new("0190f000-0000-7000-8000-000000000104");

    public required string Key { get; set; }

    public EdgeSemantic Semantic { get; set; }

    public required string Name { get; set; }

    public bool IsDirected { get; set; } = true;

    public bool IsSystem { get; set; }

    public Guid? TenantId { get; set; }

    public List<string> AllowedPairs { get; set; } = [];

    public bool Allows(string fromTypeKey, string toTypeKey) =>
        AllowedPairs.Count == 0 || AllowedPairs.Any(pair => pair.Split(':') is [var from, var to]
            && (from == "*" || from == fromTypeKey) && (to == "*" || to == toTypeKey));
}

public sealed class Edge : AuditedEntity, IMultiTenant
{
    public Guid EdgeTypeId { get; set; }

    public Guid FromNodeId { get; set; }

    public Guid ToNodeId { get; set; }

    public Guid TenantId { get; set; }
}

[NotAudited]
public sealed class NodeSubscription : Entity, IMultiTenant
{
    public Guid NodeId { get; set; }

    public Guid UserId { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>Runs inside the purge transaction before nodes are hard deleted, for cleanup of data that lives outside the graph.</summary>
public interface INodePurgeListener
{
    Task OnPurgingAsync(IQueryable<Guid> nodeIds, CancellationToken cancellationToken);
}

public static class NodeContainers
{
    /// <summary>Folders and open workspaces take files; templates, collections and finalised workspaces do not.</summary>
    public static bool AcceptsFiles(this Node node) =>
        node is Folder || node is Workspace { IsTemplate: false, FinalizedAt: null } workspace && workspace.WorkspaceKind != "collection";
}

public static class NodePaths
{
    public static IReadOnlyList<Guid> Ids(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
}

public static class GraphTopics
{
    public const string ChildrenPrefix = "children:";

    public static string Children(Guid parentId) => ChildrenPrefix + parentId;
}

internal sealed class GraphModelContributor : IModelContributor
{
    private const string Schema = "sm";

    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Restriction>(restriction =>
        {
            restriction.ToTable("Restrictions", Schema);
            restriction.Property(r => r.Kind).HasMaxLength(40);
            restriction.Property(r => r.PropertyKey).HasMaxLength(100);
            restriction.Property(r => r.Value).HasMaxLength(200);
            restriction.HasIndex(r => r.RoleId);
        });

        var names = new ValueComparer<Dictionary<string, string>>(
            (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
            d => d.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
            d => new Dictionary<string, string>(d));

        modelBuilder.Entity<NodeType>(type =>
        {
            type.ToTable("NodeTypes", Schema);
            type.Property(t => t.Key).HasMaxLength(100);
            type.Property(t => t.Name).HasMaxLength(200);
            type.Property(t => t.Semantic).HasConversion<string>().HasMaxLength(20);
            type.HasIndex(t => new { t.TenantId, t.Key }).IsUnique().AreNullsDistinct(false);
            type.HasData(
                new { Id = NodeType.FolderTypeId, Key = NodeTypeKeys.Folder, Semantic = NodeSemantic.Folder, Name = "Folder", Icon = (string?)"folder", IsSystem = true, TenantId = (Guid?)null },
                new { Id = NodeType.AssetTypeId, Key = NodeTypeKeys.Asset, Semantic = NodeSemantic.Asset, Name = "Asset", Icon = (string?)"insert_drive_file", IsSystem = true, TenantId = (Guid?)null },
                new { Id = NodeType.WorkspaceTypeId, Key = NodeTypeKeys.Workspace, Semantic = NodeSemantic.Workspace, Name = "Workspace", Icon = (string?)"workspaces", IsSystem = true, TenantId = (Guid?)null },
                new { Id = NodeType.TaskTypeId, Key = NodeTypeKeys.Task, Semantic = NodeSemantic.Task, Name = "Task", Icon = (string?)"task_alt", IsSystem = true, TenantId = (Guid?)null });
        });

        modelBuilder.Entity<Node>(node =>
        {
            node.ToTable("Nodes", Schema);
            node.HasDiscriminator<string>("Kind")
                .HasValue<Folder>("folder")
                .HasValue<Asset>("asset")
                .HasValue<CustomNode>("custom")
                .HasValue<Workspace>("workspace")
                .HasValue<WorkTask>("task");
            node.Property<string>("Kind").HasMaxLength(20);
            node.Ignore(n => n.RealtimeTopics);
            node.Ignore(n => n.PathIds);
            node.Ignore(n => n.PreviousParentId);
            node.Ignore(n => n.Semantic);
            node.Property(n => n.Name).HasMaxLength(400);
            node.Property(n => n.Path).HasMaxLength(4000);
            node.Property(n => n.Names).HasColumnType("jsonb")
                .HasConversion(d => JsonSerializer.Serialize(d, (JsonSerializerOptions?)null), s => JsonSerializer.Deserialize<Dictionary<string, string>>(s, (JsonSerializerOptions?)null) ?? new())
                .Metadata.SetValueComparer(names);
            node.Property(n => n.Metadata).HasColumnType("jsonb")
                .HasConversion(d => JsonSerializer.Serialize(d, (JsonSerializerOptions?)null), s => JsonSerializer.Deserialize<Dictionary<string, List<MetadataValue>>>(s, (JsonSerializerOptions?)null) ?? new())
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, List<MetadataValue>>>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions?)null).GetHashCode(),
                    d => JsonSerializer.Deserialize<Dictionary<string, List<MetadataValue>>>(JsonSerializer.Serialize(d, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
            node.HasIndex(n => n.Path).HasOperators("text_pattern_ops");
            node.HasIndex(n => new { n.TenantId, n.ParentId, n.SortIndex });
            // one folder root per tenant; workspaces are roots of their own
            node.HasIndex(n => n.TenantId).IsUnique().HasFilter("\"ParentId\" IS NULL AND \"Kind\" = 'folder'").HasDatabaseName("IX_Nodes_Root");
            node.HasIndex(n => n.DeletedWithId);
            node.HasOne<Node>().WithMany().HasForeignKey(n => n.ParentId).OnDelete(DeleteBehavior.NoAction);
            node.HasOne<NodeType>().WithMany().HasForeignKey(n => n.NodeTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Asset>(asset =>
        {
            asset.Property(a => a.AssetTypeKey).HasMaxLength(50);
            asset.Property(a => a.Status).HasMaxLength(20);
            asset.Property(a => a.RenditionTag).HasMaxLength(64);
            asset.HasIndex(a => a.CurrentVersionId);
        });

        modelBuilder.Entity<Workspace>(workspace =>
        {
            workspace.Property(w => w.WorkspaceKind).HasMaxLength(20);
            workspace.HasIndex(w => new { w.TenantId, w.IsTemplate });
        });

        modelBuilder.Entity<WorkTask>(task =>
        {
            task.Property(t => t.Description).HasMaxLength(8000);
            task.Property(t => t.AssigneeIds).HasColumnType("jsonb");
            task.HasIndex(t => t.StateId);
        });

        modelBuilder.Entity<EdgeType>(type =>
        {
            type.ToTable("EdgeTypes", Schema);
            type.Property(t => t.Key).HasMaxLength(100);
            type.Property(t => t.Name).HasMaxLength(200);
            type.Property(t => t.Semantic).HasConversion<string>().HasMaxLength(20);
            type.Property(t => t.AllowedPairs).HasColumnType("jsonb");
            type.HasIndex(t => new { t.TenantId, t.Key }).IsUnique().AreNullsDistinct(false);
            type.HasData(
                new { Id = EdgeType.LinkId, Key = EdgeTypeKeys.Link, Semantic = EdgeSemantic.Link, Name = "Link", IsDirected = true, IsSystem = true, TenantId = (Guid?)null, AllowedPairs = new List<string> { "folder:*" } },
                new { Id = EdgeType.DuplicateId, Key = EdgeTypeKeys.Duplicate, Semantic = EdgeSemantic.Duplicate, Name = "Duplicate", IsDirected = false, IsSystem = true, TenantId = (Guid?)null, AllowedPairs = new List<string> { "asset:asset" } },
                new { Id = EdgeType.VariantId, Key = EdgeTypeKeys.Variant, Semantic = EdgeSemantic.Variant, Name = "Variant", IsDirected = true, IsSystem = true, TenantId = (Guid?)null, AllowedPairs = new List<string>() },
                new { Id = EdgeType.TranslationId, Key = EdgeTypeKeys.Translation, Semantic = EdgeSemantic.Translation, Name = "Translation", IsDirected = true, IsSystem = true, TenantId = (Guid?)null, AllowedPairs = new List<string>() });
        });

        modelBuilder.Entity<Edge>(edge =>
        {
            edge.ToTable("Edges", Schema);
            edge.HasIndex(e => new { e.EdgeTypeId, e.FromNodeId, e.ToNodeId }).IsUnique();
            edge.HasIndex(e => e.ToNodeId);
            edge.HasOne<EdgeType>().WithMany().HasForeignKey(e => e.EdgeTypeId).OnDelete(DeleteBehavior.Restrict);
            edge.HasOne<Node>().WithMany().HasForeignKey(e => e.FromNodeId).OnDelete(DeleteBehavior.Cascade);
            edge.HasOne<Node>().WithMany().HasForeignKey(e => e.ToNodeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NodeSubscription>(subscription =>
        {
            subscription.ToTable("NodeSubscriptions", Schema);
            subscription.HasIndex(s => new { s.NodeId, s.UserId }).IsUnique();
            subscription.HasOne<Node>().WithMany().HasForeignKey(s => s.NodeId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
