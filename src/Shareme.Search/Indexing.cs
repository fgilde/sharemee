using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shareme.Contracts.Graph;
using Shareme.Graph;

namespace Shareme.Search;

internal static class SearchQueue
{
    public const string Name = "search";
    public const int BatchSize = 500;
}

/// <summary>Writes documents for nodes of the current tenant; keeps the mapping in step with the metadata catalog.</summary>
internal sealed class NodeIndexer(ISearchIndex index, NodeDocuments documents, SearchSettings settings, ILogger<NodeIndexer> logger)
{
    /// <summary>Creates the alias and adds the current catalog's fields; cheap after the first call for an unchanged catalog.</summary>
    public async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        var schema = await documents.SchemaAsync(settings.Alias, cancellationToken);
        var signature = settings.Alias + "|" + string.Join(",", schema.Fields.Select(f => f.Name + ":" + f.Type));
        if (settings.Ensured.ContainsKey(signature))
        {
            return;
        }

        await index.EnsureAsync(schema, cancellationToken);
        settings.Ensured[signature] = true;
    }

    /// <summary>Indexes the nodes and removes documents of nodes that are gone; returns how many were written and the refused ones.</summary>
    public async Task<(int Indexed, IReadOnlyList<SearchWriteError> Errors)> IndexAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return (0, []);
        }

        await EnsureIndexAsync(cancellationToken);
        var (built, removed) = await documents.BuildAsync(ids, cancellationToken);
        var errors = await index.UpsertAsync(settings.Alias, built, cancellationToken);
        if (removed.Count > 0)
        {
            await index.DeleteAsync(settings.Alias, removed, cancellationToken);
        }

        foreach (var error in errors.Take(5))
        {
            logger.LogWarning("Node {NodeId} was refused by the search index: {Reason}", error.Id, error.Reason);
        }

        return (built.Count - errors.Count, errors);
    }

    public Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken) => index.DeleteAsync(settings.Alias, ids, cancellationToken);

    /// <summary>Removes the tenant's documents that were not written since <paramref name="before"/>, i.e. of nodes a full pass did not see.</summary>
    public Task DeleteStaleAsync(Guid tenantId, DateTimeOffset before, CancellationToken cancellationToken) => index.DeleteWhereAsync(settings.Alias,
        [new SearchFilter(NodeFields.Tenant, FilterOperator.Equals, [tenantId.ToString()]), new SearchFilter(NodeFields.IndexedAt, FilterOperator.LessThanOrEquals, [before.ToString("O")])],
        cancellationToken);
}

/// <summary>Thrown so the job is retried: some documents were refused.</summary>
internal sealed class IndexingFailedException(string message) : Exception(message);

public sealed record IndexNodes(Guid[] Ids);

internal sealed class IndexNodesJob(NodeIndexer indexer) : IBackgroundJob<IndexNodes>
{
    public async Task ExecuteAsync(IndexNodes args, CancellationToken cancellationToken)
    {
        var (_, errors) = await indexer.IndexAsync(args.Ids, cancellationToken);
        if (errors.Count > 0)
        {
            throw new IndexingFailedException($"{errors.Count} nodes were refused, first: {errors[0].Reason}");
        }
    }
}

/// <summary>Re-indexes a node and everything below it, deleted nodes included (their documents are removed).</summary>
internal sealed class IndexSubtreeJob(CoworkeeDbContext db, NodeIndexer indexer) : IBackgroundJob<Guid>
{
    public async Task ExecuteAsync(Guid rootId, CancellationToken cancellationToken)
    {
        var all = db.Set<Node>().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]).AsNoTracking();
        if (await all.Where(n => n.Id == rootId).Select(n => n.Path).SingleOrDefaultAsync(cancellationToken) is not { } path)
        {
            return;
        }

        var failed = 0;
        Guid? after = null;
        while (true)
        {
            var batch = await all.Where(n => n.Path.StartsWith(path) && (after == null || n.Id.CompareTo(after.Value) > 0)).OrderBy(n => n.Id).Select(n => n.Id)
                .Take(SearchQueue.BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            failed += (await indexer.IndexAsync(batch, cancellationToken)).Errors.Count;
            after = batch[^1];
        }

        if (failed > 0)
        {
            throw new IndexingFailedException($"{failed} nodes below {rootId} were refused.");
        }
    }
}

/// <summary>Rebuilds the current tenant's documents in place: writes every node, then removes documents the pass did not write.</summary>
internal sealed class ReindexJob(CoworkeeDbContext db, NodeIndexer indexer, ICurrentUser currentUser, TimeProvider clock, ILogger<ReindexJob> logger) : IBackgroundJob<Guid>
{
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (await db.Set<ReindexRun>().SingleOrDefaultAsync(r => r.Id == runId, cancellationToken) is not { } run || currentUser.TenantId is not { } tenantId)
        {
            return;
        }

        var started = clock.GetUtcNow();
        run.State = "running";
        await db.SaveChangesAsync(cancellationToken);
        var errors = new List<SearchWriteError>();
        try
        {
            Guid? after = null;
            while (true)
            {
                var batch = await db.Set<Node>().AsNoTracking().Where(n => after == null || n.Id.CompareTo(after.Value) > 0).OrderBy(n => n.Id).Select(n => n.Id)
                    .Take(SearchQueue.BatchSize).ToListAsync(cancellationToken);
                if (batch.Count == 0)
                {
                    break;
                }

                var (indexed, refused) = await indexer.IndexAsync(batch, cancellationToken);
                errors.AddRange(refused);
                run.Indexed += indexed;
                run.Failed = errors.Count;
                after = batch[^1];
                await db.SaveChangesAsync(cancellationToken);
            }

            // documents written by incremental jobs meanwhile are newer than the start and stay; on errors nothing is removed
            if (errors.Count == 0)
            {
                await indexer.DeleteStaleAsync(tenantId, started, cancellationToken);
            }

            run.State = errors.Count == 0 ? "completed" : "completed with errors";
            run.Error = errors.Count == 0 ? null : string.Join(" | ", errors.Take(3).Select(e => $"{e.Id}: {e.Reason}"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Reindex {RunId} failed.", runId);
            run.State = "failed";
            run.Error = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
        }

        run.FinishedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None);
    }
}

/// <summary>New restrictions change what goes into full text and which nodes are marked hidden: the tenant is indexed again (a running rebuild picks them up already for what it has not done yet).</summary>
internal sealed class IndexOnRestrictionsChanged(CoworkeeDbContext db, IBackgroundJobs jobs, ICurrentUser currentUser, TimeProvider clock) : IDomainEventHandler<RestrictionsChanged>
{
    public async Task HandleAsync(RestrictionsChanged domainEvent, CancellationToken cancellationToken)
    {
        if (await db.Set<ReindexRun>().AnyAsync(r => r.State == "queued", cancellationToken))
        {
            return;
        }

        var run = new ReindexRun { State = "queued", StartedAt = clock.GetUtcNow(), StartedBy = currentUser.UserId };
        db.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<ReindexJob, Guid>(run.Id, SearchQueue.Name);
    }
}

/// <summary>A restricted list value set or removed on a folder marks or unmarks everything below it.</summary>
internal sealed class IndexOnRestrictedValueChanged(CoworkeeDbContext db, IBackgroundJobs jobs) : IDomainEventHandler<Shareme.Metadata.NodeMetadataChanged>
{
    public async Task HandleAsync(Shareme.Metadata.NodeMetadataChanged domainEvent, CancellationToken cancellationToken)
    {
        var keys = domainEvent.Keys.ToList();
        if (await db.Set<Restriction>().AnyAsync(r => r.Kind == RestrictionKinds.HiddenListValue && r.PropertyKey != null && keys.Contains(r.PropertyKey), cancellationToken)
            && await db.Set<Node>().AnyAsync(n => n.ParentId == domainEvent.NodeId, cancellationToken))
        {
            jobs.Enqueue<IndexSubtreeJob, Guid>(domainEvent.NodeId, SearchQueue.Name);
        }
    }
}

/// <summary>Keeps the index in step with the graph: the node itself, or its whole subtree when paths or access change.</summary>
internal sealed class IndexOnNodeChanged(IBackgroundJobs jobs) : IDomainEventHandler<NodeChanged>
{
    public Task HandleAsync(NodeChanged domainEvent, CancellationToken cancellationToken)
    {
        if (NodeChangeKinds.Subtree.Contains(domainEvent.Kind))
        {
            jobs.Enqueue<IndexSubtreeJob, Guid>(domainEvent.NodeId, SearchQueue.Name);
        }
        else
        {
            jobs.Enqueue<IndexNodesJob, IndexNodes>(new IndexNodes([domainEvent.NodeId]), SearchQueue.Name);
        }

        return Task.CompletedTask;
    }
}

internal sealed class IndexOnAccessChanged(CoworkeeDbContext db, IBackgroundJobs jobs) : IDomainEventHandler<ResourceAccessChanged>, IDomainEventHandler<AccessRulesChanged>
{
    public Task HandleAsync(ResourceAccessChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.ResourceType == NodePermissions.ResourceType)
        {
            jobs.Enqueue<IndexSubtreeJob, Guid>(domainEvent.ResourceId, SearchQueue.Name);
        }

        return Task.CompletedTask;
    }

    /// <summary>Only the nodes granting one of the changed roles can have other readers now; roles shared by all tenants are followed into every tenant.</summary>
    public async Task HandleAsync(AccessRulesChanged domainEvent, CancellationToken cancellationToken)
    {
        var roleIds = domainEvent.RoleIds;
        var grants = await db.Set<ResourcePermission>().IgnoreQueryFilters()
            .Where(p => p.ResourceType == NodePermissions.ResourceType && roleIds.Contains(p.RoleId))
            .Select(p => new { p.TenantId, p.ResourceId }).Distinct().ToListAsync(cancellationToken);
        foreach (var tenant in grants.GroupBy(g => g.TenantId))
        {
            using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenant.Key));
            foreach (var grant in tenant)
            {
                jobs.Enqueue<IndexSubtreeJob, Guid>(grant.ResourceId, SearchQueue.Name);
            }
        }
    }
}

/// <summary>Purged nodes leave the index with the purge, whether or not their deletion was indexed already.</summary>
internal sealed class SearchPurgeListener(NodeIndexer indexer) : INodePurgeListener
{
    public async Task OnPurgingAsync(IQueryable<Guid> nodeIds, CancellationToken cancellationToken) =>
        await indexer.DeleteAsync((await nodeIds.ToListAsync(cancellationToken)).Select(id => id.ToString()), cancellationToken);
}
