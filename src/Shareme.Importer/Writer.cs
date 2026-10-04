using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Graph;
using Shareme.Infrastructure;
using Shareme.Metadata;

namespace Shareme.Importer;

/// <summary>
/// Writes a plan through a Shareme context without interceptors: historical timestamps stay, nothing is audited and no
/// domain events start indexing or rendering per row. Rows whose id exists are left alone, so a second run writes nothing.
/// </summary>
public sealed class ImportWriter(string targetConnectionString, Guid tenantId) : IAsyncDisposable
{
    private const int Batch = 1_000;

    private readonly ServiceProvider _services = Build(targetConnectionString);

    public ShareMeDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ShareMeDbContext>().UseNpgsql(targetConnectionString).Options,
            new ImpersonatedUser(null, tenantId), _services.GetServices<IModelContributor>());

    public async Task<MapTarget> TargetAsync(bool write, CancellationToken cancellationToken)
    {
        await using var db = CreateContext();
        var root = await db.Set<Folder>().SingleOrDefaultAsync(f => f.ParentId == null, cancellationToken);
        if (root is null)
        {
            // as EnsureRoot does when someone first opens the tenant
            root = new Folder { Name = "Root", Names = new() { ["en"] = "Root", ["de"] = "Stamm" }, NodeTypeId = NodeType.FolderTypeId, TenantId = tenantId, CreatedAt = DateTimeOffset.UtcNow };
            root.PlaceUnder(null);
            if (write)
            {
                db.Add(root);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        keys.UnionWith(await db.Set<NodeType>().Where(t => t.TenantId == null || t.TenantId == tenantId).Select(t => t.Key).ToListAsync(cancellationToken));
        keys.UnionWith(await db.Set<EdgeType>().Where(t => t.TenantId == null || t.TenantId == tenantId).Select(t => t.Key).ToListAsync(cancellationToken));
        keys.UnionWith(await db.Set<PropertyGroup>().Select(g => g.Key).ToListAsync(cancellationToken));
        keys.UnionWith(await db.Set<PropertyDefinition>().Select(d => d.Key).ToListAsync(cancellationToken));
        return new MapTarget(tenantId, root, keys);
    }

    public async Task WriteAsync(ImportPlan plan, ImportReport report, CancellationToken cancellationToken)
    {
        await InsertNewAsync("languages", plan.Languages, report, cancellationToken);
        await InsertNewAsync("node types", plan.NodeTypes, report, cancellationToken);
        await InsertNewAsync("edge types", plan.EdgeTypes, report, cancellationToken);
        await InsertNewAsync("property groups", plan.PropertyGroups, report, cancellationToken);
        await InsertNewAsync("property definitions", plan.PropertyDefinitions, report, cancellationToken);
        await InsertNewAsync("nodes", plan.Nodes, report, cancellationToken); // parents come first: the plan is in tree order
        await InsertNewAsync("edges", plan.Edges, report, cancellationToken);
    }

    private async Task InsertNewAsync<T>(string name, IReadOnlyList<T> entities, ImportReport report, CancellationToken cancellationToken)
        where T : Entity
    {
        var written = 0;
        foreach (var chunk in entities.Chunk(Batch))
        {
            await using var db = CreateContext();
            var ids = chunk.Select(e => e.Id).ToList();
            var existing = (await db.Set<T>().IgnoreQueryFilters().Where(e => ids.Contains(e.Id)).Select(e => e.Id).ToListAsync(cancellationToken)).ToHashSet();
            var fresh = chunk.Where(e => !existing.Contains(e.Id)).ToList();
            foreach (var entity in fresh.OfType<IAuditable>().Where(e => e.CreatedAt == default))
            {
                entity.CreatedAt = DateTimeOffset.UtcNow;
            }

            db.AddRange(fresh);
            written += await db.SaveChangesAsync(cancellationToken) > 0 ? fresh.Count : 0;
        }

        report.Written[name] = written;
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static ServiceProvider Build(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new($"ConnectionStrings:{ShareMeInfrastructureModule.ConnectionStringName}", connectionString)])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCoworkeeModules<ShareMeDatabaseModule>(configuration);
        return services.BuildServiceProvider();
    }
}
