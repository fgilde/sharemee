using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
internal sealed class NodeIndexer(ISearchIndex index, NodeDocuments documents, SearchSettings settings)
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

    public async Task<(int Indexed, int Failed)> IndexAsync(IReadOnlyCollection<Guid> ids, string? target, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return (0, 0);
        }

        var destination = target ?? settings.Alias;
        if (target is null)
        {
            await EnsureIndexAsync(cancellationToken);
        }

        var (built, removed) = await documents.BuildAsync(ids, cancellationToken);
        var failed = await index.UpsertAsync(destination, built, cancellationToken);
        if (removed.Count > 0)
        {
            await index.DeleteAsync(destination, removed, cancellationToken);
        }

        return (built.Count - failed.Count, failed.Count);
    }
}

public sealed record IndexNodes(Guid[] Ids);

internal sealed class IndexNodesJob(NodeIndexer indexer) : IBackgroundJob<IndexNodes>
{
    public Task ExecuteAsync(IndexNodes args, CancellationToken cancellationToken) => indexer.IndexAsync(args.Ids, null, cancellationToken);
}

/// <summary>Re-indexes a node and everything below it, deleted nodes included (their documents are removed).</summary>
internal sealed class IndexSubtreeJob(CoworkeeDbContext db, NodeIndexer indexer) : IBackgroundJob<Guid>
{
    public async Task ExecuteAsync(Guid rootId, CancellationToken cancellationToken)
    {
        var all = db.Set<Node>().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]).AsNoTracking();
        if (await all.Where(n => n.Id == rootId).Select(n => n.Path).SingleOrDefaultAsync(cancellationToken) is not { } path)
        {
            await indexer.IndexAsync([rootId], null, cancellationToken);
            return;
        }

        Guid? after = null;
        while (true)
        {
            var batch = await all.Where(n => n.Path.StartsWith(path) && (after == null || n.Id.CompareTo(after.Value) > 0)).OrderBy(n => n.Id).Select(n => n.Id)
                .Take(SearchQueue.BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            await indexer.IndexAsync(batch, null, cancellationToken);
            after = batch[^1];
        }
    }
}

/// <summary>Refreshes every node of the current tenant, for changes that may touch any of them (role permissions, memberships, the catalog).</summary>
internal sealed class IndexTenantJob(CoworkeeDbContext db, NodeIndexer indexer) : IBackgroundJob<string>
{
    public async Task ExecuteAsync(string reason, CancellationToken cancellationToken)
    {
        Guid? after = null;
        while (true)
        {
            var batch = await db.Set<Node>().AsNoTracking().Where(n => after == null || n.Id.CompareTo(after.Value) > 0).OrderBy(n => n.Id).Select(n => n.Id)
                .Take(SearchQueue.BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            await indexer.IndexAsync(batch, null, cancellationToken);
            after = batch[^1];
        }
    }
}

/// <summary>Builds a fresh index for all tenants, swaps the alias and catches up on changes made meanwhile.</summary>
internal sealed class ReindexJob(IServiceScopeFactory scopes, ISearchIndex index, SearchSettings settings, TimeProvider clock, ILogger<ReindexJob> logger) : IBackgroundJob<Guid>
{
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var started = clock.GetUtcNow();
        var (indexed, failed) = (0, 0);
        try
        {
            List<Guid> tenants;
            await using (var scope = scopes.CreateAsyncScope())
            {
                tenants = await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<Node>().IgnoreQueryFilters().Select(n => n.TenantId).Distinct().ToListAsync(cancellationToken);
            }

            // the mapping must know the fields of every tenant's catalog
            var fields = new List<SearchField>();
            foreach (var tenant in tenants)
            {
                using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenant));
                await using var scope = scopes.CreateAsyncScope();
                fields.AddRange((await scope.ServiceProvider.GetRequiredService<NodeDocuments>().SchemaAsync(settings.Alias, cancellationToken)).Fields);
            }

            var target = await index.CreateIndexAsync(new SearchSchema(settings.Alias, [.. NodeFields.Static.Concat(fields).DistinctBy(f => f.Name)]), cancellationToken);
            foreach (var tenant in tenants)
            {
                using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenant));
                Guid? after = null;
                while (true)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var batch = await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<Node>().AsNoTracking()
                        .Where(n => after == null || n.Id.CompareTo(after.Value) > 0).OrderBy(n => n.Id).Select(n => n.Id).Take(SearchQueue.BatchSize).ToListAsync(cancellationToken);
                    if (batch.Count == 0)
                    {
                        break;
                    }

                    var (ok, bad) = await scope.ServiceProvider.GetRequiredService<NodeIndexer>().IndexAsync(batch, target, cancellationToken);
                    (indexed, failed) = (indexed + ok, failed + bad);
                    after = batch[^1];
                    await ReportAsync(runId, "running", indexed, failed, null, null, cancellationToken);
                }
            }

            await index.SwapAsync(settings.Alias, target, cancellationToken);

            // changes written to the old index while this ran
            foreach (var tenant in tenants)
            {
                using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenant));
                await using var scope = scopes.CreateAsyncScope();
                var changed = await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<Node>().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]).AsNoTracking()
                    .Where(n => n.CreatedAt >= started || n.ModifiedAt >= started || n.DeletedAt >= started).Select(n => n.Id).ToListAsync(cancellationToken);
                await scope.ServiceProvider.GetRequiredService<NodeIndexer>().IndexAsync(changed, null, cancellationToken);
            }

            await ReportAsync(runId, "completed", indexed, failed, null, clock.GetUtcNow(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Reindex {RunId} failed.", runId);
            await ReportAsync(runId, "failed", indexed, failed, exception.Message, clock.GetUtcNow(), CancellationToken.None);
        }
    }

    private async Task ReportAsync(Guid runId, string state, int indexed, int failed, string? error, DateTimeOffset? finished, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<ReindexRun>().IgnoreQueryFilters().Where(r => r.Id == runId)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.State, state).SetProperty(x => x.Indexed, indexed).SetProperty(x => x.Failed, failed)
                .SetProperty(x => x.Error, error).SetProperty(x => x.FinishedAt, finished), cancellationToken);
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

internal sealed class IndexOnAccessChanged(IBackgroundJobs jobs) : IDomainEventHandler<ResourceAccessChanged>, IDomainEventHandler<AccessRulesChanged>
{
    public Task HandleAsync(ResourceAccessChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.ResourceType == NodePermissions.ResourceType)
        {
            jobs.Enqueue<IndexSubtreeJob, Guid>(domainEvent.ResourceId, SearchQueue.Name);
        }

        return Task.CompletedTask;
    }

    // ponytail: refreshes the whole tenant; narrow to the nodes granting the changed roles when tenants grow large
    public Task HandleAsync(AccessRulesChanged domainEvent, CancellationToken cancellationToken)
    {
        jobs.Enqueue<IndexTenantJob, string>("access rules changed", SearchQueue.Name);
        return Task.CompletedTask;
    }
}
