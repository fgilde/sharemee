using Coworkee.Contracts.Identity;
using Coworkee.Identity.Domain;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Drm;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;
using Shareme.Contracts.Metadata;
using Shareme.Contracts.Workspaces;
using Shareme.Knowledge;

namespace Shareme.Importer;

/// <summary>
/// Sharedien Permission enum values (docs/analysis/sharedien-backend.md) to Shareme permissions. Values left out are
/// either pure UI switches (views of screens) or have no counterpart; the report lists the latter for review.
/// </summary>
public static class PermissionMap
{
    private static readonly string[] Admin =
    [
        NodePermissions.Admin, NodePermissions.ManagePermissions, MetadataPermissions.Manage, AutomationPermissions.ManageRules, DataPermissions.MassEdit,
        DrmPermissions.Manage, IdentityPermissions.Users.Manage, IdentityPermissions.Groups.Manage, IdentityPermissions.Roles.Manage,
        IdentityPermissions.Permissions.Manage, IdentityPermissions.ResourcePermissions.Manage, "Settings.Manage", "Jobs.View", "Audit.View",
    ];

    // Frontend views and object kinds Shareme has no separate right for: granted implicitly, so they are no problem
    private static readonly HashSet<int> UiOnly = [1, 2, 13, 19, 34, 39, 44, 50, 55, 69, 74, 75, 90, 95, 100, 106, 111, 116, 121, 124, 127, 132, 137, 142, 147, 152, 157,
        167, 172, 177, 178, 183, 191, 196, 206, 211, 216, 221, 231, 236, 242, 247, 252, 257, 262, 264, 269, 274, 281, 282, 285, 291, 293, 295, 302, 308, 311];

    private static readonly Dictionary<int, string[]> Map = Build();

    /// <summary>The Shareme permissions for a Sharedien value; null when there is no counterpart.</summary>
    public static IReadOnlyList<string>? For(int permission) =>
        Map.TryGetValue(permission, out var names) ? names : UiOnly.Contains(permission) ? [] : null;

    private static Dictionary<int, string[]> Build()
    {
        var map = new Dictionary<int, string[]>();
        void Add(string[] names, params int[] values)
        {
            foreach (var value in values)
            {
                map[value] = map.TryGetValue(value, out var existing) ? [.. existing.Union(names)] : names;
            }
        }

        // nodes: categories, assets, versions, relations, custom entities, tasks, workspaces, states, comments
        Add([NodePermissions.Read], 5, 17, 37, 42, 45, 48, 53, 58, 72, 150, 155, 194, 214, 219, 240, 245, 250, 279);
        Add([NodePermissions.Create], 3, 15, 35, 40, 46, 51, 56, 70, 148, 153, 192, 238, 243, 248, 277, 237);
        Add([NodePermissions.Update], 6, 18, 38, 43, 49, 54, 59, 73, 151, 156, 195, 215, 220, 241, 246, 251, 280);
        Add([NodePermissions.Delete], 4, 16, 36, 41, 47, 52, 57, 71, 122, 149, 154, 193, 213, 218, 239, 244, 249, 278);
        Add([NodePermissions.Read], 123);
        Add([AssetPermissions.Download], 14, 309);
        Add([NodePermissions.ManagePermissions], 102, 103, 105);
        Add([NodePermissions.Read], 104);

        // catalog
        Add([MetadataPermissions.Manage], 25, 26, 27, 28, 30, 33, 60, 61, 62, 63, 65, 68, 76, 77, 79, 107, 108, 110, 112, 113, 115, 158, 159, 160, 161, 163, 166,
            184, 185, 186, 197, 198, 199, 200, 202, 205, 222, 223, 224, 225, 227, 230, 265, 266, 268, 270, 271, 273);
        Add([NodePermissions.Admin], 86, 87, 89, 91, 92, 94, 207, 208, 210, 81, 82, 84, 96, 97, 99, 143, 144, 146, 173, 174, 176);

        // automation
        Add([DataPermissions.MassEdit], 20, 117);
        Add([AutomationPermissions.ManageRules], 258, 259, 261, 263);
        Add([AiPermissions.Manage], 298, 299, 301);
        Add([AiPermissions.Run], 305);
        Add(["Jobs.View"], 10, 22, 119, 126, 284, 306);

        // identity, settings, audit
        Add([IdentityPermissions.Users.Manage], 179, 180, 182);
        Add([IdentityPermissions.Users.View, PeoplePermissions.View], 181);
        Add([IdentityPermissions.Groups.Manage], 187, 188, 190);
        Add([IdentityPermissions.Groups.View], 189);
        Add([IdentityPermissions.Roles.Manage, IdentityPermissions.Permissions.Manage], 128, 129, 131);
        Add([IdentityPermissions.Roles.View], 130);
        Add(["Settings.Manage"], 253, 254, 256);
        Add(["Audit.View"], 290, 292, 294, 310);

        // workspaces and licensing
        Add([WorkspacePermissions.Create], 212);
        Add([WorkspacePermissions.Finalize], 283);
        Add([WorkspacePermissions.ManageTemplates], 232, 233, 235);
        Add([DrmPermissions.Manage], 287, 288, 289);
        Add([DrmPermissions.Offers], 296, 297, 286);
        Add([DrmPermissions.Purchase], 303);
        Add([DrmPermissions.Purchases], 304);

        Add(Admin, 101);
        return map;
    }
}

public sealed record IdentityPlan(
    IReadOnlyList<User> Users,
    IReadOnlyList<UserProfile> Profiles,
    IReadOnlyList<Role> Roles,
    IReadOnlyList<PermissionGrant> Grants,
    IReadOnlyList<UserGroup> Groups,
    IReadOnlyDictionary<long, Guid> UserIds,
    IReadOnlyDictionary<string, Guid> UserByName,
    IReadOnlyDictionary<long, Guid> RoleIds,
    IReadOnlyDictionary<long, Guid> GroupIds)
{
    public static readonly IdentityPlan Empty = new([], [], [], [], [], new Dictionary<long, Guid>(), new Dictionary<string, Guid>(), new Dictionary<long, Guid>(), new Dictionary<long, Guid>());
}

/// <summary>Users, groups, roles and their grants. Passwords never move: users set one through the reset mail or sign in externally.</summary>
public sealed class IdentityMapper(SourceIdentity source, MapTarget target, ImportReport report, Func<string?, string, string> name)
{
    private const int Active = 1;

    public IdentityPlan Map()
    {
        var users = new List<User>();
        var profiles = new List<UserProfile>();
        var userIds = new Dictionary<long, Guid>();
        var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in source.Users.OrderBy(u => u.Id))
        {
            var userName = (row.Email is { Length: > 0 } email ? email : row.UserName).Trim();
            var normalized = userName.ToUpperInvariant();
            var id = SourceIds.For(target.TenantId, "Admin.User", row.Id, row.Identifier);
            Guid? same = target.ExistingUsers.TryGetValue(normalized, out var known) && known != id ? known : FindTaken(users, normalized);
            if (same is { } existing)
            {
                // the same person (e.g. local and external account, or already set up): one Shareme user
                report.Problem("user_merged", "Admin.User", row.Id);
                userIds[row.Id] = existing;
            }
            else
            {
                var user = new User
                {
                    Id = id,
                    TenantId = target.TenantId,
                    UserName = userName,
                    NormalizedUserName = normalized,
                    Email = row.Email,
                    NormalizedEmail = row.Email?.ToUpperInvariant(),
                    EmailConfirmed = row.Email is not null,
                    FirstName = row.FirstName,
                    LastName = row.LastName,
                    IsActive = row.Status == Active,
                    LastLoginAt = Utc(row.LastLoginDate),
                    CreatedAt = Utc(row.CreatedAt) ?? DateTimeOffset.UtcNow,
                };
                users.Add(user);
                userIds[row.Id] = user.Id;
                if (row.Department is { Length: > 0 } || row.Phone is { Length: > 0 })
                {
                    profiles.Add(new UserProfile { UserId = user.Id, TenantId = target.TenantId, Department = Cap(row.Department, 200), Phone = Cap(row.Phone, 100) });
                }
            }

            byName[row.Identifier] = userIds[row.Id];
            byName[row.UserName] = userIds[row.Id];
        }

        var roles = new List<Role>();
        var grants = new List<PermissionGrant>();
        var roleIds = new Dictionary<long, Guid>();
        var roleNames = new HashSet<string>(target.ExistingRoleNames, StringComparer.OrdinalIgnoreCase);
        var permissions = source.RolePermissions.ToLookup(p => p.RoleId, p => p.Permission);
        var unmapped = new HashSet<int>();
        foreach (var row in source.Roles.OrderBy(r => r.Id))
        {
            var roleName = Unique(name(row.TranslationIdentifier, row.Identifier), roleNames);
            var role = new Role
            {
                Id = SourceIds.Derived(target.TenantId, "Admin.Role", row.Id),
                TenantId = target.TenantId,
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant(),
                Description = "Imported from Sharedien",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            roles.Add(role);
            roleIds[row.Id] = role.Id;
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var permission in permissions[row.Id])
            {
                if (PermissionMap.For(permission) is { } mapped)
                {
                    names.UnionWith(mapped);
                }
                else if (unmapped.Add(permission))
                {
                    report.Problem("unmapped_permission", "Admin.RoleObjectTypePermission", permission);
                }
            }

            grants.AddRange(names.Select(n => Grant(n, role.Id, row.Id)));
        }

        var groups = new List<UserGroup>();
        var groupIds = new Dictionary<long, Guid>();
        var groupNames = new HashSet<string>(target.ExistingGroupNames, StringComparer.OrdinalIgnoreCase);
        var members = source.GroupUsers.ToLookup(m => m.GroupId, m => m.UserId);
        var groupRoles = source.GroupRoles.ToLookup(g => g.GroupId, g => g.RoleId);
        foreach (var row in source.Groups.OrderBy(g => g.Id))
        {
            var group = new UserGroup
            {
                Name = Unique(Cap(row.Name, 200)!, groupNames),
                Description = Cap(row.Description, 2000),
                TenantId = target.TenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            Ids.Set(group, SourceIds.For(target.TenantId, "Admin.UserGroup", row.Id, row.Identifier));
            group.Members.AddRange(members[row.Id].Where(userIds.ContainsKey).Select(u => userIds[u]).Distinct().Select(u => new UserGroupMember { GroupId = group.Id, UserId = u }));
            group.Roles.AddRange(groupRoles[row.Id].Where(roleIds.ContainsKey).Select(r => roleIds[r]).Distinct().Select(r => new UserGroupRole { GroupId = group.Id, RoleId = r }));
            groups.Add(group);
            groupIds[row.Id] = group.Id;
        }

        report.Mapped["users"] = users.Count;
        report.Mapped["roles"] = roles.Count;
        report.Mapped["permission grants"] = grants.Count;
        report.Mapped["groups"] = groups.Count;
        return new IdentityPlan(users, profiles, roles, grants, groups, userIds, byName, roleIds, groupIds);
    }

    /// <summary>Node level role assignments; nodes not imported drop their assignments.</summary>
    public List<ResourcePermission> MapNodePermissions(IdentityPlan identity, IReadOnlyDictionary<long, Guid> nodeIds)
    {
        var result = new List<ResourcePermission>();
        var seen = new HashSet<(Guid, PrincipalType, Guid, Guid)>();
        foreach (var row in source.NodePermissions.OrderBy(p => p.Id))
        {
            (PrincipalType Type, Guid Id)? principal =
                row.UserId is { } u && identity.UserIds.TryGetValue(u, out var user) ? (PrincipalType.User, user)
                : row.GroupId is { } g && identity.GroupIds.TryGetValue(g, out var group) ? (PrincipalType.Group, group)
                : null;
            if (!nodeIds.TryGetValue(row.NodeId, out var node))
            {
                report.Skip("node permissions of nodes not imported");
                continue;
            }

            if (!identity.RoleIds.TryGetValue(row.RoleId, out var role) || principal is not { } p)
            {
                report.Problem("permission_without_role_or_principal", "Admin.NodePermission", row.Id);
                continue;
            }

            if (seen.Add((node, p.Type, p.Id, role)))
            {
                var permission = new ResourcePermission
                {
                    ResourceType = NodePermissions.ResourceType,
                    ResourceId = node,
                    PrincipalType = p.Type,
                    PrincipalId = p.Id,
                    RoleId = role,
                    TenantId = target.TenantId,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                Ids.Set(permission, SourceIds.For(target.TenantId, "Admin.NodePermission", row.Id, row.Identifier));
                result.Add(permission);
            }
        }

        report.Mapped["node permissions"] = result.Count;
        return result;
    }

    private PermissionGrant Grant(string permission, Guid roleId, long sourceRoleId)
    {
        var grant = new PermissionGrant
        {
            TenantId = target.TenantId,
            Name = permission,
            ProviderType = PermissionProviderType.Role,
            ProviderKey = roleId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Ids.Set(grant, SourceIds.Derived(target.TenantId, "Admin.RoleObjectTypePermission:" + permission, sourceRoleId));
        return grant;
    }

    private static Guid? FindTaken(List<User> users, string normalized) => users.FirstOrDefault(u => u.NormalizedUserName == normalized)?.Id;

    private static string Unique(string value, HashSet<string> taken)
    {
        var candidate = value;
        for (var i = 2; !taken.Add(candidate); i++)
        {
            candidate = $"{value} ({i})";
        }

        return candidate;
    }

    private static string? Cap(string? text, int length) => text is not null && text.Length > length ? text[..length] : text;

    private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
}
