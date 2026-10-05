using System.Data.Common;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shareme.Graph;
using Shareme.Infrastructure;
using Shareme.Metadata;

namespace Shareme.Importer;

/// <summary>
/// Writes a plan through a Shareme context without interceptors: historical timestamps stay, nothing is audited and no
/// domain events start indexing or rendering per row. The whole run is one transaction, so a failed run leaves nothing
/// behind; rows whose id exists are left alone, so a second run writes nothing.
/// </summary>
public sealed class ImportWriter(string targetConnectionString, Guid tenantId) : IAsyncDisposable
{
    private const int Batch = 1_000;

    private readonly ServiceProvider _services = Build(targetConnectionString);
    private NpgsqlConnection? _connection;
    private DbTransaction? _transaction;
    private Folder? _newRoot;

    private ShareMeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ShareMeDbContext>();
        if (_connection is null)
        {
            options.UseNpgsql(targetConnectionString);
        }
        else
        {
            options.UseNpgsql(_connection);
        }

        var db = new ShareMeDbContext(options.Options, new ImpersonatedUser(null, tenantId), _services.GetServices<IModelContributor>());
        if (_transaction is not null)
        {
            db.Database.UseTransaction(_transaction);
        }

        return db;
    }

    public Coworkee.Storage.IBlobStorage Storage => _services.GetRequiredService<Coworkee.Storage.IBlobStorage>();

    public async Task<Dictionary<string, (string MimeType, string AssetTypeKey)>> FileTypesAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateContext();
        return await db.Set<Shareme.Assets.FileType>().ToDictionaryAsync(t => t.Extension, t => (t.MimeType, t.AssetTypeKey), cancellationToken);
    }

    public async Task<HashSet<Guid>> ExistingVersionsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        await using var db = CreateContext();
        var existing = new HashSet<Guid>();
        foreach (var chunk in ids.Chunk(Batch))
        {
            existing.UnionWith(await db.Set<Shareme.Assets.AssetVersion>().IgnoreQueryFilters().Where(v => chunk.Contains(v.Id)).Select(v => v.Id).ToListAsync(cancellationToken));
        }

        return existing;
    }

    /// <summary>
    /// Plans the renditions of every current version that has none yet and hands them to the running Shareme through the
    /// normal outbox. Only <c>DerivationsRequested</c> goes out, so no automation rule runs for old files. Safe to repeat:
    /// a run after a failed request picks up what is missing.
    /// </summary>
    public async Task<int> RequestRenditionsAsync(CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        List<(Guid Asset, Guid Version)> pending;
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShareMeDbContext>();
            pending = (await db.Set<Shareme.Graph.Asset>().Where(a => a.CurrentVersionId != null && !db.Set<Shareme.Assets.Derivation>().Any(d => d.VersionId == a.CurrentVersionId))
                .Select(a => new { a.Id, Version = a.CurrentVersionId!.Value }).ToListAsync(cancellationToken)).Select(a => (a.Id, a.Version)).ToList();
        }

        var requested = 0;
        foreach (var chunk in pending.Chunk(200))
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ShareMeDbContext>();
            var assetIds = chunk.Select(c => c.Asset).ToList();
            var versionIds = chunk.Select(c => c.Version).ToList();
            var assets = await db.Set<Shareme.Graph.Asset>().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
            var mimeTypes = await db.Set<Shareme.Assets.AssetVersion>().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.MimeType, cancellationToken);
            foreach (var (assetId, versionId) in chunk)
            {
                var kinds = await Shareme.Processing.RenditionPlan.KindsAsync(db, tenantId, mimeTypes[versionId], assets[assetId].AssetTypeKey, cancellationToken);
                if (kinds.Count == 0)
                {
                    continue;
                }

                db.AddRange(kinds.Select(kind => new Shareme.Assets.Derivation { VersionId = versionId, AssetId = assetId, Kind = kind, PlannedAt = DateTimeOffset.UtcNow }));
                assets[assetId].Publish(new Shareme.Assets.DerivationsRequested(assetId, versionId));
                requested++;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return requested;
    }

    /// <summary>Queues a search rebuild of the tenant through the outbox: the import wrote past the indexing events.</summary>
    public async Task RequestReindexAsync(CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShareMeDbContext>();
        var root = await db.Set<Folder>().SingleAsync(f => f.ParentId == null, cancellationToken);
        root.Publish(new Shareme.Search.ReindexRequested());
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Nodes of the plan that are already in the target.</summary>
    public async Task<HashSet<Guid>> ExistingNodesAsync(IEnumerable<Guid> nodeIds, CancellationToken cancellationToken)
    {
        await using var db = CreateContext();
        var existing = new HashSet<Guid>();
        foreach (var chunk in nodeIds.Chunk(Batch))
        {
            existing.UnionWith(await db.Set<Shareme.Graph.Node>().IgnoreQueryFilters().Where(n => chunk.Contains(n.Id)).Select(n => n.Id).ToListAsync(cancellationToken));
        }

        return existing;
    }

    public async Task<MapTarget> TargetAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateContext();
        if (tenantId == Guid.Empty || !await db.Set<Tenant>().AnyAsync(t => t.Id == tenantId, cancellationToken))
        {
            throw new InvalidOperationException($"The target tenant {tenantId} does not exist. Set up the tenant in Shareme first.");
        }

        var root = await db.Set<Folder>().AsNoTracking().SingleOrDefaultAsync(f => f.ParentId == null, cancellationToken);
        if (root is null)
        {
            // as EnsureRoot does when someone first opens the tenant; written together with the plan
            root = new Folder { Name = "Root", Names = new() { ["en"] = "Root", ["de"] = "Stamm" }, NodeTypeId = NodeType.FolderTypeId, TenantId = tenantId, CreatedAt = DateTimeOffset.UtcNow };
            root.PlaceUnder(null);
            _newRoot = root;
        }

        var definitions = await db.Set<PropertyDefinition>().Select(d => d.Id).ToListAsync(cancellationToken);
        var listValues = await db.Set<PropertyListValue>().Where(v => definitions.Contains(v.DefinitionId)).Select(v => new { v.Id, v.DefinitionId, v.Key }).ToListAsync(cancellationToken);
        var languages = await db.Set<DataLanguage>().Select(l => new { l.Code, l.IsDefault }).ToListAsync(cancellationToken);
        var workspaceRoles = await db.Set<Shareme.Workspaces.WorkspaceRoleBinding>().ToDictionaryAsync(b => b.Key, b => b.RoleId, cancellationToken);
        var users = await db.Set<User>().Where(u => u.NormalizedUserName != null).Select(u => new { u.NormalizedUserName, u.Id, u.TenantId }).ToListAsync(cancellationToken);
        var admin = await db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin && (r.TenantId == tenantId || r.TenantId == null))
            .OrderByDescending(r => r.TenantId).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);
        return new MapTarget(tenantId, root)
        {
            Keys = new Dictionary<string, IReadOnlyDictionary<Guid, string>>
            {
                [KeyKinds.NodeType] = await db.Set<NodeType>().Where(t => t.TenantId == null || t.TenantId == tenantId).ToDictionaryAsync(t => t.Id, t => t.Key, cancellationToken),
                [KeyKinds.EdgeType] = await db.Set<EdgeType>().Where(t => t.TenantId == null || t.TenantId == tenantId).ToDictionaryAsync(t => t.Id, t => t.Key, cancellationToken),
                [KeyKinds.Group] = await db.Set<PropertyGroup>().ToDictionaryAsync(g => g.Id, g => g.Key, cancellationToken),
                [KeyKinds.Definition] = await db.Set<PropertyDefinition>().ToDictionaryAsync(d => d.Id, d => d.Key, cancellationToken),
                [KeyKinds.UserContext] = await db.Set<UserContext>().ToDictionaryAsync(c => c.Id, c => c.Key, cancellationToken),
                [KeyKinds.RenditionType] = await db.Set<Shareme.Assets.DerivationType>().ToDictionaryAsync(t => t.Id, t => t.Key, cancellationToken),
            },
            ListValues = listValues.ToDictionary(v => v.Id, v => (v.DefinitionId, v.Key)),
            LanguageCodes = languages.Select(l => l.Code).ToHashSet(),
            HasDefaultLanguage = languages.Any(l => l.IsDefault),
            ExistingUsers = users.ToDictionary(u => u.NormalizedUserName!, u => (u.Id, u.TenantId)),
            AdminRoleId = admin,
            WorkspaceRoles = workspaceRoles,
            ExistingRoleNames = (await db.Set<Role>().Where(r => r.TenantId == null || r.TenantId == tenantId).Select(r => r.Name!).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            ExistingGroupNames = (await db.Set<UserGroup>().Select(g => g.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
    }

    public async Task WriteAsync(ImportPlan plan, ImportReport report, CancellationToken cancellationToken) => await WriteAsync(plan, [], report, cancellationToken);

    public async Task WriteAsync(ImportPlan plan, IReadOnlyList<Shareme.Assets.AssetVersion> versions, ImportReport report, CancellationToken cancellationToken)
    {
        _connection = new NpgsqlConnection(targetConnectionString);
        await _connection.OpenAsync(cancellationToken);
        _transaction = await _connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (_newRoot is not null)
            {
                await InsertNewAsync("root", [(Node)_newRoot], report, cancellationToken);
            }

            await InsertNewAsync("users", plan.Identity.Users, report, cancellationToken, u => u.Id);
            await InsertNewAsync("profiles", plan.Identity.Profiles, report, cancellationToken, p => p.UserId);
            await InsertNewAsync("roles", plan.Identity.Roles, report, cancellationToken, r => r.Id);
            await InsertNewAsync("permission grants", plan.Identity.Grants, report, cancellationToken);
            await InsertNewAsync("workspace roles", plan.Identity.WorkspaceRoleBindings ?? [], report, cancellationToken);
            await InsertNewAsync("groups", plan.Identity.Groups, report, cancellationToken);
            await InsertNewAsync("languages", plan.Languages, report, cancellationToken);
            await InsertNewAsync("node types", plan.NodeTypes, report, cancellationToken);
            await InsertNewAsync("edge types", plan.EdgeTypes, report, cancellationToken);
            await InsertNewAsync("user contexts", plan.UserContexts, report, cancellationToken);
            await InsertNewAsync("property groups", plan.PropertyGroups, report, cancellationToken);
            await InsertNewAsync("property definitions", plan.PropertyDefinitions, report, cancellationToken);
            await InsertNewAsync("nodes", plan.Nodes, report, cancellationToken); // parents come first: the plan is in tree order
            await InsertNewAsync("file versions", versions, report, cancellationToken);
            await InsertNewAsync("edges", plan.Edges, report, cancellationToken);
            await InsertNewAsync("workflow states", plan.States, report, cancellationToken);
            await InsertNewAsync("comments", plan.Comments, report, cancellationToken);
            await InsertNewAsync("node permissions", plan.NodePermissions, report, cancellationToken);
            await InsertNewAsync("restrictions", plan.Restrictions, report, cancellationToken);
            await InsertNewAsync("rendition types", plan.Renditions.Types, report, cancellationToken);
            await InsertNewAsync("rendition restrictions", plan.Renditions.Restrictions, report, cancellationToken);
            await InsertNewAsync("rules", plan.Rules, report, cancellationToken);
            await InsertNewAsync("billing contacts", plan.Drm.BillingContacts, report, cancellationToken);
            await InsertNewAsync("rights owners", plan.Drm.RightsOwners, report, cancellationToken);
            await InsertNewAsync("licenses", plan.Drm.Licenses, report, cancellationToken);
            await InsertNewAsync("licensed assets", plan.Drm.LicenseAssets, report, cancellationToken);
            await InsertNewAsync("context groups", plan.Drm.ContextGroups, report, cancellationToken);
            await InsertNewAsync("price lists", plan.Drm.PriceLists, report, cancellationToken);
            await InsertNewAsync("buyouts", plan.Drm.Buyouts, report, cancellationToken);
            await _transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            report.Written.Clear();
            throw;
        }
    }

    private Task InsertNewAsync<T>(string name, IReadOnlyList<T> entities, ImportReport report, CancellationToken cancellationToken)
        where T : Coworkee.Domain.Entity => InsertNewAsync(name, entities, report, cancellationToken, e => e.Id);

    private async Task InsertNewAsync<T>(string name, IReadOnlyList<T> entities, ImportReport report, CancellationToken cancellationToken, Func<T, Guid> key)
        where T : class
    {
        var written = 0;
        foreach (var chunk in entities.Chunk(Batch))
        {
            await using var db = CreateContext();
            var entityType = db.Model.FindEntityType(typeof(T))!;
            var keyName = entityType.FindPrimaryKey()!.Properties.Single().Name;
            var ids = chunk.Select(key).ToList();
            var rows = db.Set<T>().IgnoreQueryFilters().Where(e => ids.Contains(EF.Property<Guid>(e, keyName)));
            var known = (await rows.Select(e => EF.Property<Guid>(e, keyName)).ToListAsync(cancellationToken)).ToHashSet();

            // a kept source GUID that lives in another tenant: never mix tenants, the operator decides
            if (known.Count > 0 && entityType.FindProperty("TenantId") is { } tenantProperty)
            {
                var foreign = tenantProperty.ClrType == typeof(Guid)
                    ? await rows.Where(e => EF.Property<Guid>(e, "TenantId") != tenantId).Select(e => (Guid?)EF.Property<Guid>(e, keyName)).FirstOrDefaultAsync(cancellationToken)
                    : await rows.Where(e => EF.Property<Guid?>(e, "TenantId") != tenantId).Select(e => (Guid?)EF.Property<Guid>(e, keyName)).FirstOrDefaultAsync(cancellationToken);
                if (foreign is { } id)
                {
                    throw new InvalidOperationException($"{name}: id {id} already belongs to another tenant. This source was imported elsewhere in this database.");
                }
            }

            var fresh = chunk.Where(e => !known.Contains(key(e))).ToList();
            foreach (var entity in fresh.OfType<Coworkee.Domain.IAuditable>().Where(e => e.CreatedAt == default))
            {
                entity.CreatedAt = DateTimeOffset.UtcNow;
            }

            db.AddRange(fresh);
            await db.SaveChangesAsync(cancellationToken);
            written += fresh.Count;
        }

        report.Written[name] = written;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await _services.DisposeAsync();
    }

    private static ServiceProvider Build(string connectionString)
    {
        // the target storage and everything else are configured like Shareme itself, through environment variables
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddInMemoryCollection([new($"ConnectionStrings:{ShareMeInfrastructureModule.ConnectionStringName}", connectionString)])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCoworkeeModules<ShareMeDatabaseModule>(configuration);
        return services.BuildServiceProvider();
    }
}
