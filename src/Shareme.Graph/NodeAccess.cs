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

internal sealed class NodeAccess(IPermissionChecker permissions)
{
    public Task<bool> CanAsync(string permission, Guid nodeId, CancellationToken cancellationToken) =>
        permissions.IsGrantedAsync(permission, NodePermissions.ResourceType, nodeId, cancellationToken);

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
