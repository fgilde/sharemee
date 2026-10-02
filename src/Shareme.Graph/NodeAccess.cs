using Coworkee.Application.Authorization;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Realtime;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Graph;

namespace Shareme.Graph;

internal sealed class NodeHierarchy(CoworkeeDbContext db) : IResourceHierarchy
{
    public string ResourceType => NodePermissions.ResourceType;

    public async Task<IReadOnlyList<Guid>> GetInheritanceChainAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var path = await db.Set<Node>().Where(n => n.Id == resourceId).Select(n => n.Path).SingleOrDefaultAsync(cancellationToken);
        if (path is null)
        {
            return [];
        }

        var ids = NodePaths.Ids(path);
        var interrupted = await db.Set<Node>().Where(n => ids.Contains(n.Id) && n.InterruptInheritance).Select(n => n.Id).ToListAsync(cancellationToken);
        var chain = new List<Guid>();
        for (var index = ids.Count - 1; index >= 0; index--)
        {
            chain.Add(ids[index]);
            if (interrupted.Contains(ids[index]))
            {
                break;
            }
        }

        return chain;
    }
}

internal sealed class NodeAccess(IPermissionChecker permissions, Restrictions restrictions)
{
    /// <summary>What the current user's roles take away; single node checks apply hidden list values already, lists filter with it.</summary>
    public Task<RestrictionSet> RestrictionsAsync(CancellationToken cancellationToken) => restrictions.CurrentAsync(cancellationToken);

    /// <summary>Leaves out nodes with a list value hidden from the current user.</summary>
    public Task<IQueryable<T>> VisibleAsync<T>(IQueryable<T> nodes, CancellationToken cancellationToken)
        where T : Node => restrictions.VisibleAsync(nodes, cancellationToken);

    /// <summary>For each node on a path (root first): readable through a direct grant or inherited from a readable parent.</summary>
    public static bool[] Readable(IReadOnlyList<Guid> pathIds, IReadOnlySet<Guid> granted, IReadOnlySet<Guid> interrupted)
    {
        var readable = new bool[pathIds.Count];
        for (var index = 0; index < pathIds.Count; index++)
        {
            readable[index] = granted.Contains(pathIds[index]) || (index > 0 && readable[index - 1] && !interrupted.Contains(pathIds[index]));
        }

        return readable;
    }

    public Task<bool> CanAsync(string permission, Guid nodeId, CancellationToken cancellationToken) =>
        permissions.IsGrantedAsync(permission, NodePermissions.ResourceType, nodeId, cancellationToken);

    /// <summary>Those of the users who may read the node now, each checked as themselves; for notifications, which must not name nodes their recipient cannot see.</summary>
    public async Task<List<Guid>> ReadersAmongAsync(IEnumerable<Guid> userIds, Guid nodeId, Guid? tenantId, CancellationToken cancellationToken)
    {
        var readers = new List<Guid>();
        foreach (var userId in userIds.Distinct())
        {
            using (Coworkee.Core.Security.CurrentUserScope.Begin(new Coworkee.Core.Security.ImpersonatedUser(userId, tenantId)))
            {
                if (await CanAsync(NodePermissions.Read, nodeId, cancellationToken))
                {
                    readers.Add(userId);
                }
            }
        }

        return readers;
    }

    public Task<bool> IsGlobalAsync(string permission, CancellationToken cancellationToken) => permissions.IsGrantedAsync(permission, cancellationToken);

    public Task<IReadOnlyCollection<Guid>> GrantedNodesAsync(string permission, CancellationToken cancellationToken) =>
        permissions.GetGrantedResourcesAsync(permission, NodePermissions.ResourceType, cancellationToken);
}

internal sealed class ChildrenTopicAuthorizer(NodeAccess access) : IRealtimeTopicAuthorizer
{
    public bool Handles(string topic) => topic.StartsWith(GraphTopics.ChildrenPrefix, StringComparison.Ordinal);

    public async Task<bool> AuthorizeAsync(string topic, CancellationToken cancellationToken) =>
        Guid.TryParse(topic[GraphTopics.ChildrenPrefix.Length..], out var parentId) && await access.CanAsync(NodePermissions.Read, parentId, cancellationToken);
}
