using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Graph;

namespace Shareme.Graph;

public sealed record GetEdges(Guid NodeId) : IQuery<Result<IReadOnlyList<EdgeDto>>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => NodeId;

    public string RequiredPermission => NodePermissions.Read;
}

public sealed record CreateEdge(Guid FromNodeId, CreateEdgeRequest Request) : ICommand<Result<EdgeDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => FromNodeId;

    public string RequiredPermission => NodePermissions.Update;
}

public sealed record DeleteEdge(Guid EdgeId) : ICommand<Result>;

public sealed record GetRecycleBin(PageRequest Page) : IQuery<Result<PagedResult<RecycleBinItemDto>>>;

public sealed record RestoreNode(Guid Id) : ICommand<Result>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record PurgeNode(Guid Id) : ICommand<Result>;

public sealed record GetNodeTypes : IQuery<Result<IReadOnlyList<NodeTypeDto>>>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record CreateNodeType(CreateNodeTypeRequest Request) : ICommand<Result<NodeTypeDto>>;

internal sealed class EdgeHandlers(CoworkeeDbContext db, NodeAccess access)
    : IHandler<GetEdges, Result<IReadOnlyList<EdgeDto>>>, IHandler<CreateEdge, Result<EdgeDto>>, IHandler<DeleteEdge, Result>
{
    public async Task<Result<IReadOnlyList<EdgeDto>>> HandleAsync(GetEdges query, CancellationToken cancellationToken)
    {
        var edges = await (from edge in db.Set<Edge>().AsNoTracking()
                           join type in db.Set<EdgeType>() on edge.EdgeTypeId equals type.Id
                           join source in db.Set<Node>() on edge.FromNodeId equals source.Id
                           join target in db.Set<Node>() on edge.ToNodeId equals target.Id
                           where edge.FromNodeId == query.NodeId || edge.ToNodeId == query.NodeId
                           orderby type.Key, target.Name
                           select new EdgeDto(edge.Id, type.Key, edge.FromNodeId, edge.ToNodeId, source.Name, target.Name)).ToListAsync(cancellationToken);
        if (await access.IsGlobalAsync(NodePermissions.Read, cancellationToken))
        {
            return edges;
        }

        var visible = new List<EdgeDto>();
        foreach (var edge in edges)
        {
            if (await access.CanAsync(NodePermissions.Read, edge.FromNodeId == query.NodeId ? edge.ToNodeId : edge.FromNodeId, cancellationToken))
            {
                visible.Add(edge);
            }
        }

        return visible;
    }

    public async Task<Result<EdgeDto>> HandleAsync(CreateEdge command, CancellationToken cancellationToken)
    {
        var from = await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == command.FromNodeId, cancellationToken);
        var to = await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == command.Request.ToNodeId, cancellationToken);
        if (from is null || to is null)
        {
            return NodeErrors.NotFound;
        }

        var type = await db.Set<EdgeType>().SingleOrDefaultAsync(t => t.Key == command.Request.Type && (t.TenantId == null || t.TenantId == from.TenantId), cancellationToken);
        if (type is null)
        {
            return NodeErrors.NotFound;
        }

        if (!await access.CanAsync(NodePermissions.Read, to.Id, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", $"Permission '{NodePermissions.Read}' is required on the target.");
        }

        var keys = await db.Set<NodeType>().Where(t => t.Id == from.NodeTypeId || t.Id == to.NodeTypeId).ToDictionaryAsync(t => t.Id, t => t.Key, cancellationToken);
        if (from.Id == to.Id || !type.Allows(keys[from.NodeTypeId], keys[to.NodeTypeId]))
        {
            return Error.Validation("Type", $"A '{type.Key}' relation is not allowed between these nodes.");
        }

        if (await db.Set<Edge>().AnyAsync(e => e.EdgeTypeId == type.Id && e.FromNodeId == from.Id && e.ToNodeId == to.Id, cancellationToken))
        {
            return Error.Conflict("edges.exists", "This relation already exists.");
        }

        var edge = new Edge { EdgeTypeId = type.Id, FromNodeId = from.Id, ToNodeId = to.Id };
        db.Add(edge);
        return new EdgeDto(edge.Id, type.Key, from.Id, to.Id, from.Name, to.Name);
    }

    public async Task<Result> HandleAsync(DeleteEdge command, CancellationToken cancellationToken)
    {
        if (await db.Set<Edge>().SingleOrDefaultAsync(e => e.Id == command.EdgeId, cancellationToken) is not { } edge)
        {
            return NodeErrors.NotFound;
        }

        if (!await access.CanAsync(NodePermissions.Update, edge.FromNodeId, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", $"Permission '{NodePermissions.Update}' is required.");
        }

        db.Remove(edge);
        return Result.Success();
    }
}

internal sealed class RecycleBinHandlers(CoworkeeDbContext db, ICurrentUser currentUser, NodeAccess access, IEnumerable<INodePurgeListener> purgeListeners)
    : IHandler<GetRecycleBin, Result<PagedResult<RecycleBinItemDto>>>, IHandler<RestoreNode, Result>, IHandler<PurgeNode, Result>
{
    public async Task<Result<PagedResult<RecycleBinItemDto>>> HandleAsync(GetRecycleBin query, CancellationToken cancellationToken)
    {
        var items = Deleted().Where(n => n.DeletedWithId == n.Id);
        if (!await access.IsGlobalAsync(NodePermissions.Admin, cancellationToken))
        {
            var userId = currentUser.UserId;
            items = items.Where(n => n.DeletedBy == userId);
        }

        var page = Math.Max(1, query.Page.Page);
        var size = Math.Clamp(query.Page.PageSize, 1, 200);
        var total = await items.CountAsync(cancellationToken);
        var rows = await items.OrderByDescending(n => n.DeletedAt).Skip((page - 1) * size).Take(size)
            .Join(db.Set<NodeType>(), n => n.NodeTypeId, t => t.Id, (n, t) => new RecycleBinItemDto(n.Id, n.Name, t.Key, n.ParentId, n.DeletedAt, n.DeletedBy))
            .ToListAsync(cancellationToken);
        return new PagedResult<RecycleBinItemDto>(rows, total, page, size);
    }

    public async Task<Result> HandleAsync(RestoreNode command, CancellationToken cancellationToken)
    {
        var node = await Deleted().SingleOrDefaultAsync(n => n.Id == command.Id && n.DeletedWithId == n.Id, cancellationToken);
        if (node is null)
        {
            return NodeErrors.NotFound;
        }

        if (node.DeletedBy != currentUser.UserId && !await access.IsGlobalAsync(NodePermissions.Admin, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "Only the person who deleted this or an administrator can restore it.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var parentExists = node.ParentId is { } parentId && await db.Set<Node>().AnyAsync(n => n.Id == parentId, cancellationToken);

        // a workspace is a root itself; everything else whose parent is gone comes back under the folder root
        if (!parentExists && node is not Workspace)
        {
            var oldPrefix = node.Path;
            node.PlaceUnder(await db.Set<Folder>().SingleAsync(n => n.ParentId == null, cancellationToken));
            await NodeCommandHandlers.RebaseDescendantsAsync(db, node, oldPrefix, cancellationToken);
        }

        await Deleted().Where(n => n.DeletedWithId == node.Id && n.Id != node.Id).ExecuteUpdateAsync(s => s
            .SetProperty(n => n.IsDeleted, false).SetProperty(n => n.DeletedAt, (DateTimeOffset?)null)
            .SetProperty(n => n.DeletedBy, (Guid?)null).SetProperty(n => n.DeletedWithId, (Guid?)null), cancellationToken);
        node.IsDeleted = false;
        node.DeletedAt = null;
        node.DeletedBy = null;
        node.DeletedWithId = null;
        node.MarkChanged(NodeChangeKinds.Restored);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(PurgeNode command, CancellationToken cancellationToken)
    {
        var path = await Deleted().Where(n => n.Id == command.Id && n.DeletedWithId == n.Id).Select(n => n.Path).SingleOrDefaultAsync(cancellationToken);
        if (path is null)
        {
            return NodeErrors.NotFound;
        }

        // ponytail: hard delete bypasses audit; stored files are queued for cleanup by the purge listeners
        // earlier bin entries inside the subtree go with it, their parent is gone afterwards
        var subtree = Deleted().Where(n => n.Path.StartsWith(path));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var listener in purgeListeners)
        {
            await listener.OnPurgingAsync(subtree.Select(n => n.Id), cancellationToken);
        }

        await db.Set<ResourcePermission>().Where(p => p.ResourceType == NodePermissions.ResourceType && subtree.Select(n => n.Id).Contains(p.ResourceId))
            .ExecuteDeleteAsync(cancellationToken);
        await subtree.ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private IQueryable<Node> Deleted() => db.Set<Node>().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]).Where(n => n.IsDeleted);
}

internal sealed class NodeTypeHandlers(CoworkeeDbContext db, ICurrentUser currentUser)
    : IHandler<GetNodeTypes, Result<IReadOnlyList<NodeTypeDto>>>, IHandler<CreateNodeType, Result<NodeTypeDto>>
{
    public async Task<Result<IReadOnlyList<NodeTypeDto>>> HandleAsync(GetNodeTypes query, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        IReadOnlyList<NodeTypeDto> types = (await db.Set<NodeType>().AsNoTracking().Where(t => t.TenantId == null || t.TenantId == tenantId).OrderBy(t => t.Name).ToListAsync(cancellationToken))
            .Select(Map).ToList();
        return Result<IReadOnlyList<NodeTypeDto>>.Success(types);
    }

    public async Task<Result<NodeTypeDto>> HandleAsync(CreateNodeType command, CancellationToken cancellationToken)
    {
        var key = command.Request.Key.Trim().ToLowerInvariant();
        if (key.Length is 0 or > 100 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return Error.Validation("Key", "Use letters, digits, '-' or '_' (up to 100 characters).");
        }

        var tenantId = currentUser.TenantId;
        if (await db.Set<NodeType>().AnyAsync(t => t.Key == key && (t.TenantId == null || t.TenantId == tenantId), cancellationToken))
        {
            return Error.Conflict("node_types.exists", "A node type with this key already exists.");
        }

        var type = new NodeType { Key = key, Name = command.Request.Name.Trim(), Icon = command.Request.Icon, Semantic = NodeSemantic.Custom, TenantId = tenantId };
        db.Add(type);
        return Map(type);
    }

    private static NodeTypeDto Map(NodeType type) => new(type.Key, type.Semantic.ToString().ToLowerInvariant(), type.Name, type.Icon, type.IsSystem);
}

internal sealed class NotifySubscribers(CoworkeeDbContext db, ICurrentUser currentUser, INotifier notifier) : IDomainEventHandler<NodeChanged>
{
    public async Task HandleAsync(NodeChanged domainEvent, CancellationToken cancellationToken)
    {
        var node = await db.Set<Node>().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]).AsNoTracking()
            .SingleOrDefaultAsync(n => n.Id == domainEvent.NodeId, cancellationToken);
        if (node is null)
        {
            return;
        }

        var watched = node.PathIds;
        var recipients = await db.Set<NodeSubscription>().Where(s => watched.Contains(s.NodeId) && s.UserId != currentUser.UserId)
            .Select(s => s.UserId).Distinct().ToListAsync(cancellationToken);
        if (recipients.Count == 0)
        {
            return;
        }

        await notifier.NotifyAsync(recipients, "node." + domainEvent.Kind, $"{(node.Name.Length > 200 ? node.Name[..200] + "…" : node.Name)} was {domainEvent.Kind}", null, $"/browse/{node.ParentId ?? node.Id}", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
