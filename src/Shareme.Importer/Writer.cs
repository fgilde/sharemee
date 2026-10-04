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
        var users = await db.Set<User>().Where(u => u.NormalizedUserName != null).Select(u => new { u.NormalizedUserName, u.Id }).ToListAsync(cancellationToken);
        return new MapTarget(tenantId, root)
        {
            Keys = new Dictionary<string, IReadOnlyDictionary<Guid, string>>
            {
                [KeyKinds.NodeType] = await db.Set<NodeType>().Where(t => t.TenantId == null || t.TenantId == tenantId).ToDictionaryAsync(t => t.Id, t => t.Key, cancellationToken),
                [KeyKinds.EdgeType] = await db.Set<EdgeType>().Where(t => t.TenantId == null || t.TenantId == tenantId).ToDictionaryAsync(t => t.Id, t => t.Key, cancellationToken),
                [KeyKinds.Group] = await db.Set<PropertyGroup>().ToDictionaryAsync(g => g.Id, g => g.Key, cancellationToken),
                [KeyKinds.Definition] = await db.Set<PropertyDefinition>().ToDictionaryAsync(d => d.Id, d => d.Key, cancellationToken),
            },
            ListValues = listValues.ToDictionary(v => v.Id, v => (v.DefinitionId, v.Key)),
            LanguageCodes = languages.Select(l => l.Code).ToHashSet(),
            HasDefaultLanguage = languages.Any(l => l.IsDefault),
            ExistingUsers = users.ToDictionary(u => u.NormalizedUserName!, u => u.Id),
            ExistingRoleNames = (await db.Set<Role>().Where(r => r.TenantId == null || r.TenantId == tenantId).Select(r => r.Name!).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            ExistingGroupNames = (await db.Set<UserGroup>().Select(g => g.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
    }

    public async Task WriteAsync(ImportPlan plan, ImportReport report, CancellationToken cancellationToken)
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
            await InsertNewAsync("groups", plan.Identity.Groups, report, cancellationToken);
            await InsertNewAsync("languages", plan.Languages, report, cancellationToken);
            await InsertNewAsync("node types", plan.NodeTypes, report, cancellationToken);
            await InsertNewAsync("edge types", plan.EdgeTypes, report, cancellationToken);
            await InsertNewAsync("property groups", plan.PropertyGroups, report, cancellationToken);
            await InsertNewAsync("property definitions", plan.PropertyDefinitions, report, cancellationToken);
            await InsertNewAsync("nodes", plan.Nodes, report, cancellationToken); // parents come first: the plan is in tree order
            await InsertNewAsync("edges", plan.Edges, report, cancellationToken);
            await InsertNewAsync("node permissions", plan.NodePermissions, report, cancellationToken);
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
