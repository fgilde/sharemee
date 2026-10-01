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

    public required string Key { get; set; }

    public NodeSemantic Semantic { get; set; }

    public required string Name { get; set; }

    public string? Icon { get; set; }

    public bool IsSystem { get; set; }

    public Guid? TenantId { get; set; }
}

public sealed record NodeChanged(Guid NodeId, string Kind) : IDomainEvent;

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
}

public sealed class Folder : Node
{
    public override NodeSemantic Semantic => NodeSemantic.Folder;
}

public sealed class Asset : Node
{
    public override NodeSemantic Semantic => NodeSemantic.Asset;
}

public sealed class CustomNode : Node
{
    public override NodeSemantic Semantic => NodeSemantic.Custom;
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
                new { Id = NodeType.AssetTypeId, Key = NodeTypeKeys.Asset, Semantic = NodeSemantic.Asset, Name = "Asset", Icon = (string?)"insert_drive_file", IsSystem = true, TenantId = (Guid?)null });
        });

        modelBuilder.Entity<Node>(node =>
        {
            node.ToTable("Nodes", Schema);
            node.HasDiscriminator<string>("Kind")
                .HasValue<Folder>("folder")
                .HasValue<Asset>("asset")
                .HasValue<CustomNode>("custom");
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
            node.HasIndex(n => n.TenantId).IsUnique().HasFilter("\"ParentId\" IS NULL").HasDatabaseName("IX_Nodes_Root");
            node.HasIndex(n => n.DeletedWithId);
            node.HasOne<Node>().WithMany().HasForeignKey(n => n.ParentId).OnDelete(DeleteBehavior.NoAction);
            node.HasOne<NodeType>().WithMany().HasForeignKey(n => n.NodeTypeId).OnDelete(DeleteBehavior.Restrict);
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
