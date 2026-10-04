using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Graph;

namespace Shareme.Graph;

public sealed record EnsureRoot : ICommand<Result<NodeDto>>;

public sealed record GetEntryPoints : IQuery<Result<IReadOnlyList<NodeDto>>>;

public sealed record GetNode(Guid Id) : IQuery<Result<NodeDetailDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Read;
}

/// <summary>Children of a node; <paramref name="Kind"/> "folder" lists only folders.</summary>
public sealed record GetChildren(Guid Id, PageRequest Page, string? Sort, string? Kind = null) : IQuery<Result<PagedResult<NodeDto>>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Read;
}

public sealed record CreateNode(CreateNodeRequest Request) : ICommand<Result<NodeDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Request.ParentId;

    public string RequiredPermission => NodePermissions.Create;
}

public sealed record UpdateNode(Guid Id, UpdateNodeRequest Request) : ICommand<Result>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Update;
}

public sealed record MoveNode(Guid Id, Guid TargetParentId) : ICommand<Result>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Move;
}

public sealed record SetInheritance(Guid Id, bool Interrupt) : ICommand<Result>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.ManagePermissions;
}

public sealed record DeleteNode(Guid Id) : ICommand<Result>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Delete;
}

public sealed record Subscribe(Guid Id, bool On) : ICommand<Result>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Read;
}

internal static class NodeErrors
{
    public static readonly Error NotFound = Error.NotFound("nodes.not_found", "The node does not exist.");
    public static readonly Error RootIsFixed = Error.Conflict("nodes.root_is_fixed", "The root folder cannot be moved or deleted.");
}

internal sealed class NodeMapper(CoworkeeDbContext db)
{
    private Dictionary<Guid, string>? _typeKeys;

    public async Task<NodeDto> MapAsync(Node node, CancellationToken cancellationToken) => Map(node, await TypeKeysAsync(cancellationToken));

    public async Task<IReadOnlyList<NodeDto>> MapAsync(IEnumerable<Node> nodes, CancellationToken cancellationToken)
    {
        var keys = await TypeKeysAsync(cancellationToken);
        return nodes.Select(n => Map(n, keys)).ToList();
    }

    private static NodeDto Map(Node node, Dictionary<Guid, string> keys) => new(
        node.Id, node.ParentId, keys.GetValueOrDefault(node.NodeTypeId, "unknown"), node.Semantic.ToString().ToLowerInvariant(), node.Name, node.Names,
        node.SortIndex, node.InterruptInheritance, node.CreatedAt, node.ModifiedAt, (node as Asset)?.RenditionTag);

    private async Task<Dictionary<Guid, string>> TypeKeysAsync(CancellationToken cancellationToken) =>
        _typeKeys ??= await db.Set<NodeType>().AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Key, cancellationToken);
}

internal sealed class NodeQueryHandlers(CoworkeeDbContext db, ICurrentUser currentUser, NodeAccess access, NodeMapper mapper)
    : IHandler<EnsureRoot, Result<NodeDto>>,
      IHandler<GetEntryPoints, Result<IReadOnlyList<NodeDto>>>,
      IHandler<GetNode, Result<NodeDetailDto>>,
      IHandler<GetChildren, Result<PagedResult<NodeDto>>>
{
    public async Task<Result<NodeDto>> HandleAsync(EnsureRoot command, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Error.Validation("Tenant", "The root folder belongs to an organisation.");
        }

        var root = await db.Set<Folder>().SingleOrDefaultAsync(n => n.ParentId == null, cancellationToken);
        if (root is null)
        {
            root = new Folder { Name = "Root", Names = new() { ["en"] = "Root", ["de"] = "Stamm" }, NodeTypeId = NodeType.FolderTypeId };
            root.PlaceUnder(null);
            db.Add(root);
        }

        return await mapper.MapAsync(root, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<NodeDto>>> HandleAsync(GetEntryPoints query, CancellationToken cancellationToken)
    {
        if (await access.IsGlobalAsync(NodePermissions.Read, cancellationToken))
        {
            var root = await (await access.VisibleAsync(db.Set<Folder>().AsNoTracking().Where(n => n.ParentId == null), cancellationToken)).ToListAsync(cancellationToken);
            return Result<IReadOnlyList<NodeDto>>.Success(await mapper.MapAsync(root, cancellationToken));
        }

        var granted = (await access.GrantedNodesAsync(NodePermissions.Read, cancellationToken)).ToHashSet();
        // entry points of the folder tree only; workspaces have their own list
        var tree = await db.Set<Folder>().AsNoTracking().Where(n => n.ParentId == null).Select(n => n.Path).SingleOrDefaultAsync(cancellationToken) ?? "/";
        var nodes = await (await access.VisibleAsync(db.Set<Node>().AsNoTracking().Where(n => granted.Contains(n.Id) && n.Path.StartsWith(tree)), cancellationToken)).ToListAsync(cancellationToken);
        var interrupted = await InterruptedAsync(nodes.SelectMany(n => n.PathIds).Distinct().ToList(), cancellationToken);
        var topMost = nodes.Where(n => n.PathIds is var ids && (ids.Count == 1 || interrupted.Contains(n.Id) || !NodeAccess.Readable(ids, granted, interrupted)[^2]))
            .OrderBy(n => n.Name).ToList();
        return Result<IReadOnlyList<NodeDto>>.Success(await mapper.MapAsync(topMost, cancellationToken));
    }

    public async Task<Result<NodeDetailDto>> HandleAsync(GetNode query, CancellationToken cancellationToken)
    {
        var node = await db.Set<Node>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == query.Id, cancellationToken);
        if (node is null)
        {
            return NodeErrors.NotFound;
        }

        var ids = node.PathIds;
        var path = await db.Set<Node>().AsNoTracking().Where(n => ids.Contains(n.Id)).Select(n => new { n.Id, n.Name, n.InterruptInheritance })
            .ToDictionaryAsync(n => n.Id, cancellationToken);
        var first = 0;
        if (!await access.IsGlobalAsync(NodePermissions.Read, cancellationToken))
        {
            // the trail starts where the caller's access starts, names above it stay hidden
            var granted = (await access.GrantedNodesAsync(NodePermissions.Read, cancellationToken)).ToHashSet();
            var readable = NodeAccess.Readable(ids, granted, path.Values.Where(p => p.InterruptInheritance).Select(p => p.Id).ToHashSet());
            first = ids.Count - 1;
            while (first > 0 && readable[first - 1])
            {
                first--;
            }
        }

        var breadcrumb = ids.Skip(first).Where(path.ContainsKey).Select(id => new BreadcrumbDto(id, path[id].Name)).ToList();
        var subscribed = await db.Set<NodeSubscription>().AnyAsync(s => s.NodeId == node.Id && s.UserId == currentUser.UserId, cancellationToken);
        return new NodeDetailDto(await mapper.MapAsync(node, cancellationToken), breadcrumb, subscribed);
    }

    private async Task<HashSet<Guid>> InterruptedAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<Node>().AsNoTracking().Where(n => ids.Contains(n.Id) && n.InterruptInheritance).Select(n => n.Id).ToListAsync(cancellationToken)).ToHashSet();

    public async Task<Result<PagedResult<NodeDto>>> HandleAsync(GetChildren query, CancellationToken cancellationToken)
    {
        // tasks live on their board
        var children = db.Set<Node>().AsNoTracking().Where(n => n.ParentId == query.Id && !(n is WorkTask));
        if (query.Kind == NodeTypeKeys.Folder)
        {
            children = children.Where(n => n is Folder);
        }
        if (!await access.IsGlobalAsync(NodePermissions.Read, cancellationToken))
        {
            var hidden = new List<Guid>();
            foreach (var interrupted in await children.Where(n => n.InterruptInheritance).Select(n => n.Id).ToListAsync(cancellationToken))
            {
                if (!await access.CanAsync(NodePermissions.Read, interrupted, cancellationToken))
                {
                    hidden.Add(interrupted);
                }
            }

            children = children.Where(n => !hidden.Contains(n.Id));
        }

        children = await access.VisibleAsync(children, cancellationToken);

        // news before and after their time are for their authors only
        if (!await access.CanAsync(NodePermissions.Update, query.Id, cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            children = children.Where(n => !(n is News) || (((News)n).PublishAt <= now && (((News)n).ExpiresAt == null || ((News)n).ExpiresAt > now)));
        }
        if (!string.IsNullOrWhiteSpace(query.Page.Search))
        {
            var search = query.Page.Search.Trim();
            children = children.Where(n => EF.Functions.ILike(n.Name, "%" + search + "%"));
        }

        children = query.Sort switch
        {
            NodeSorts.Created => children.OrderByDescending(n => n.CreatedAt),
            NodeSorts.Manual => children.OrderBy(n => n.SortIndex).ThenBy(n => n.Name),
            _ => children.OrderBy(n => n.Name),
        };
        var page = Math.Max(1, query.Page.Page);
        var size = Math.Clamp(query.Page.PageSize, 1, 500);
        var total = await children.CountAsync(cancellationToken);
        var items = await children.Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<NodeDto>(await mapper.MapAsync(items, cancellationToken), total, page, size);
    }
}

internal sealed class NodeCommandHandlers(CoworkeeDbContext db, ICurrentUser currentUser, NodeAccess access, NodeMapper mapper, TimeProvider clock)
    : IHandler<CreateNode, Result<NodeDto>>,
      IHandler<UpdateNode, Result>,
      IHandler<MoveNode, Result>,
      IHandler<SetInheritance, Result>,
      IHandler<DeleteNode, Result>,
      IHandler<Subscribe, Result>
{
    public async Task<Result<NodeDto>> HandleAsync(CreateNode command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 400)
        {
            return Error.Validation("Name", "A name of up to 400 characters is required.");
        }

        var parent = await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == request.ParentId, cancellationToken);
        if (parent is null)
        {
            return NodeErrors.NotFound;
        }

        if (parent.Semantic is NodeSemantic.Asset or NodeSemantic.Task or NodeSemantic.Page or NodeSemantic.News)
        {
            return Error.Validation("ParentId", "Assets and tasks cannot contain other nodes.");
        }

        var tenantId = currentUser.TenantId;
        var type = await db.Set<NodeType>().SingleOrDefaultAsync(t => t.Key == request.Type && (t.TenantId == null || t.TenantId == tenantId), cancellationToken);
        Node? node = type?.Semantic switch
        {
            NodeSemantic.Folder => new Folder { Name = string.Empty },
            NodeSemantic.Custom => new CustomNode { Name = string.Empty },
            _ => null,
        };
        if (type is null || node is null)
        {
            return Error.Validation("Type", $"Nodes of type '{request.Type}' cannot be created here.");
        }

        node.NodeTypeId = type.Id;
        node.Name = request.Name.Trim();
        node.Names = request.Names?.ToDictionary() ?? [];
        node.SortIndex = await db.Set<Node>().Where(n => n.ParentId == parent.Id).Select(n => (int?)n.SortIndex).MaxAsync(cancellationToken) + 1 ?? 0;
        node.PlaceUnder(parent);
        node.MarkChanged("created");
        db.Add(node);
        return await mapper.MapAsync(node, cancellationToken);
    }

    public async Task<Result> HandleAsync(UpdateNode command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Request.Name) || command.Request.Name.Length > 400)
        {
            return Error.Validation("Name", "A name of up to 400 characters is required.");
        }

        if (await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == command.Id, cancellationToken) is not { } node)
        {
            return NodeErrors.NotFound;
        }

        // a page title is part of its versions
        if (node is Page)
        {
            return Error.Validation("Name", "Rename pages by editing them, so the title stays in their history.");
        }

        node.Name = command.Request.Name.Trim();
        if (command.Request.Names is { } names)
        {
            node.Names = names.ToDictionary();
        }

        if (command.Request.SortIndex is { } sortIndex)
        {
            node.SortIndex = sortIndex;
        }

        node.MarkChanged("updated");
        return Result.Success();
    }

    public async Task<Result> HandleAsync(MoveNode command, CancellationToken cancellationToken)
    {
        var node = await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == command.Id, cancellationToken);
        var target = await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == command.TargetParentId, cancellationToken);
        if (node is null || target is null)
        {
            return NodeErrors.NotFound;
        }

        if (node.ParentId is null)
        {
            return node is Workspace ? Error.Conflict("nodes.workspace_is_root", "Workspaces stay where they are.") : NodeErrors.RootIsFixed;
        }

        if (target.Path.StartsWith(node.Path, StringComparison.Ordinal) || target.Semantic is NodeSemantic.Asset or NodeSemantic.Task or NodeSemantic.Page or NodeSemantic.News || node is WorkTask)
        {
            return Error.Conflict("nodes.invalid_target", "A node cannot move into itself, its descendants, an asset or a task; tasks stay in their workspace.");
        }

        if (!await access.CanAsync(NodePermissions.Create, target.Id, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", $"Permission '{NodePermissions.Create}' is required on the target.");
        }

        if (node.ParentId == target.Id)
        {
            return Result.Success();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var oldPrefix = node.Path;
        node.PlaceUnder(target);
        await RebaseDescendantsAsync(db, node, oldPrefix, cancellationToken);
        node.MarkChanged(NodeChangeKinds.Moved);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Moves every descendant of the node, earlier deleted ones included, from the old prefix to the node's current path.</summary>
    internal static Task RebaseDescendantsAsync(CoworkeeDbContext db, Node node, string oldPrefix, CancellationToken cancellationToken)
    {
        var newPrefix = node.Path;
        var depthDelta = node.PathIds.Count - NodePaths.Ids(oldPrefix).Count;
        return db.Set<Node>().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter])
            .Where(n => n.Path.StartsWith(oldPrefix) && n.Id != node.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Path, n => newPrefix + n.Path.Substring(oldPrefix.Length)).SetProperty(n => n.Depth, n => n.Depth + depthDelta), cancellationToken);
    }

    public async Task<Result> HandleAsync(SetInheritance command, CancellationToken cancellationToken)
    {
        if (await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == command.Id, cancellationToken) is not { } node)
        {
            return NodeErrors.NotFound;
        }

        if (node.InterruptInheritance != command.Interrupt)
        {
            node.InterruptInheritance = command.Interrupt;
            node.MarkChanged(NodeChangeKinds.Permissions);
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteNode command, CancellationToken cancellationToken)
    {
        if (await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == command.Id, cancellationToken) is not { } node)
        {
            return NodeErrors.NotFound;
        }

        if (node is { ParentId: null } and not Workspace)
        {
            return NodeErrors.RootIsFixed;
        }

        var now = clock.GetUtcNow();
        foreach (var member in await db.Set<Node>().Where(n => n.Path.StartsWith(node.Path)).ToListAsync(cancellationToken))
        {
            member.IsDeleted = true;
            member.DeletedAt = now;
            member.DeletedBy = currentUser.UserId;
            member.DeletedWithId = node.Id;
        }

        node.MarkChanged(NodeChangeKinds.Deleted);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(Subscribe command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized("auth.required", "Authentication is required.");
        }

        var existing = await db.Set<NodeSubscription>().SingleOrDefaultAsync(s => s.NodeId == command.Id && s.UserId == userId, cancellationToken);
        if (command.On && existing is null)
        {
            db.Add(new NodeSubscription { NodeId = command.Id, UserId = userId });
        }
        else if (!command.On && existing is not null)
        {
            db.Remove(existing);
        }

        return Result.Success();
    }
}
