using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;

namespace Shareme.Graph;

/// <summary>Takes something away from users with the role: a property, nodes with a list value, or a rendition kind.</summary>
public sealed class Restriction : AuditedEntity, IMultiTenant
{
    public Guid RoleId { get; set; }

    public required string Kind { get; set; }

    public string? PropertyKey { get; set; }

    public string? Value { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>The restrictions that apply to one user, merged over their roles.</summary>
public sealed record RestrictionSet(
    IReadOnlySet<string> HiddenProperties, IReadOnlySet<string> ReadOnlyProperties, IReadOnlyList<(string Key, string Value)> HiddenListValues, IReadOnlySet<string> BlockedRenditions)
{
    public static readonly RestrictionSet None = new(new HashSet<string>(), new HashSet<string>(), [], new HashSet<string>());

    public bool IsEmpty => HiddenProperties.Count == 0 && ReadOnlyProperties.Count == 0 && HiddenListValues.Count == 0 && BlockedRenditions.Count == 0;

    public bool Hides(IReadOnlyDictionary<string, List<MetadataValue>> metadata) =>
        HiddenListValues.Any(h => metadata.TryGetValue(h.Key, out var values) && values.Any(v => v.List == h.Value));

    public bool Hides(Node node) => Hides(node.Metadata);

    public bool CannotChange(string key) => HiddenProperties.Contains(key) || ReadOnlyProperties.Contains(key);

    /// <summary>The jsonb containment documents of hidden list values, to leave such nodes out in queries.</summary>
    public IEnumerable<string> HiddenDocuments() =>
        HiddenListValues.Select(h => JsonSerializer.Serialize(new Dictionary<string, object[]> { [h.Key] = [new Dictionary<string, string> { ["list"] = h.Value }] }));
}

/// <summary>Restrictions of the tenant by role, loaded once per scope.</summary>
internal sealed class RestrictionStore(CoworkeeDbContext db)
{
    private List<Restriction>? _all;

    public async Task<RestrictionSet> ForRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        _all ??= await db.Set<Restriction>().AsNoTracking().ToListAsync(cancellationToken);
        var applying = _all.Where(r => roleIds.Contains(r.RoleId)).ToList();
        if (applying.Count == 0)
        {
            return RestrictionSet.None;
        }

        HashSet<string> Keys(string kind, Func<Restriction, string?> pick) => applying.Where(r => r.Kind == kind).Select(pick).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return new RestrictionSet(
            Keys(RestrictionKinds.HiddenProperty, r => r.PropertyKey),
            Keys(RestrictionKinds.ReadOnlyProperty, r => r.PropertyKey),
            applying.Where(r => r.Kind == RestrictionKinds.HiddenListValue && r.PropertyKey is not null && r.Value is not null).Select(r => (r.PropertyKey!, r.Value!)).Distinct().ToList(),
            Keys(RestrictionKinds.BlockedRendition, r => r.Value));
    }

    public void Forget() => _all = null;
}

/// <summary>Nodes with a hidden list value are out of reach for every permission, also for holders of a global grant.</summary>
internal sealed class NodeRestriction(CoworkeeDbContext db, RestrictionStore store) : IResourceRestriction
{
    public string ResourceType => NodePermissions.ResourceType;

    public async Task<bool> IsRestrictedAsync(Guid resourceId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var set = await store.ForRolesAsync(roleIds, cancellationToken);
        if (set.HiddenListValues.Count == 0)
        {
            return false;
        }

        var metadata = await db.Set<Node>().AsNoTracking().Where(n => n.Id == resourceId).Select(n => n.Metadata).SingleOrDefaultAsync(cancellationToken);
        return metadata is not null && set.Hides(metadata);
    }
}

/// <summary>The restrictions of the current user (also when acting for someone in a job).</summary>
public sealed class Restrictions
{
    private readonly RestrictionStore _store;
    private readonly IPermissionChecker _permissions;
    private readonly ICurrentUser _currentUser;
    private readonly Dictionary<Guid, RestrictionSet> _byUser = [];

    internal Restrictions(RestrictionStore store, IPermissionChecker permissions, ICurrentUser currentUser) =>
        (_store, _permissions, _currentUser) = (store, permissions, currentUser);

    public async Task<RestrictionSet> CurrentAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return RestrictionSet.None;
        }

        if (!_byUser.TryGetValue(userId, out var set))
        {
            _byUser[userId] = set = await _store.ForRolesAsync(await _permissions.GetRoleIdsAsync(cancellationToken), cancellationToken);
        }

        return set;
    }

    /// <summary>Leaves out nodes with a hidden list value.</summary>
    public async Task<IQueryable<T>> VisibleAsync<T>(IQueryable<T> nodes, CancellationToken cancellationToken)
        where T : Node
    {
        foreach (var document in (await CurrentAsync(cancellationToken)).HiddenDocuments())
        {
            nodes = nodes.Where(n => !EF.Functions.JsonContains(n.Metadata, document));
        }

        return nodes;
    }
}

[RequiresPermission(NodePermissions.Admin)]
public sealed record GetRestrictions : IQuery<Result<IReadOnlyList<RestrictionDto>>>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record SaveRestriction(RestrictionRequest Request) : ICommand<Result<RestrictionDto>>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record DeleteRestriction(Guid Id) : ICommand<Result>;

internal sealed class RestrictionHandlers(CoworkeeDbContext db)
    : IHandler<GetRestrictions, Result<IReadOnlyList<RestrictionDto>>>, IHandler<SaveRestriction, Result<RestrictionDto>>, IHandler<DeleteRestriction, Result>
{
    public async Task<Result<IReadOnlyList<RestrictionDto>>> HandleAsync(GetRestrictions query, CancellationToken cancellationToken)
    {
        IReadOnlyList<RestrictionDto> list = (await db.Set<Restriction>().AsNoTracking().OrderBy(r => r.Kind).ThenBy(r => r.PropertyKey).ThenBy(r => r.Value).ToListAsync(cancellationToken))
            .Select(Map).ToList();
        return Result<IReadOnlyList<RestrictionDto>>.Success(list);
    }

    public async Task<Result<RestrictionDto>> HandleAsync(SaveRestriction command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var needsKey = request.Kind is RestrictionKinds.HiddenProperty or RestrictionKinds.ReadOnlyProperty or RestrictionKinds.HiddenListValue;
        var needsValue = request.Kind is RestrictionKinds.HiddenListValue or RestrictionKinds.BlockedRendition;
        if (request.RoleId == Guid.Empty || !RestrictionKinds.All.Contains(request.Kind)
            || needsKey == string.IsNullOrWhiteSpace(request.PropertyKey) || needsValue == string.IsNullOrWhiteSpace(request.Value))
        {
            return Error.Validation("Kind", "A role and a known kind with its property and value are required.");
        }

        if (!await db.Set<Coworkee.Identity.Domain.Role>().AnyAsync(r => r.Id == request.RoleId, cancellationToken))
        {
            return Error.Validation("RoleId", "The role does not exist.");
        }

        var key = needsKey ? request.PropertyKey!.Trim() : null;
        var value = needsValue ? request.Value!.Trim() : null;
        if (await db.Set<Restriction>().AnyAsync(r => r.RoleId == request.RoleId && r.Kind == request.Kind && r.PropertyKey == key && r.Value == value, cancellationToken))
        {
            return Error.Conflict("restrictions.exists", "The role has this restriction already.");
        }

        var restriction = new Restriction { RoleId = request.RoleId, Kind = request.Kind, PropertyKey = key, Value = value };
        db.Add(restriction);
        return Map(restriction);
    }

    public async Task<Result> HandleAsync(DeleteRestriction command, CancellationToken cancellationToken)
    {
        if (await db.Set<Restriction>().SingleOrDefaultAsync(r => r.Id == command.Id, cancellationToken) is not { } restriction)
        {
            return Error.NotFound("restrictions.not_found", "The restriction does not exist.");
        }

        db.Remove(restriction);
        return Result.Success();
    }

    private static RestrictionDto Map(Restriction r) => new(r.Id, r.RoleId, r.Kind, r.PropertyKey, r.Value);
}
