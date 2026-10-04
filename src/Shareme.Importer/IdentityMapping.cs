using System.Text;
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
/// either pure UI switches (views of screens, personal property sets) or have no counterpart; the report lists the latter.
/// GlobalAdmin is no list of rights: it maps to the tenant's admin role.
/// </summary>
public static class PermissionMap
{
    public const int GlobalAdmin = 101;

    /// <summary>Rights that apply to a node and its subtree; only these go into node level role bindings.</summary>
    public static readonly IReadOnlySet<string> NodeScoped = new HashSet<string>
    {
        NodePermissions.Read, NodePermissions.Create, NodePermissions.Update, NodePermissions.Delete, NodePermissions.Move,
        NodePermissions.ManagePermissions, AssetPermissions.Download,
    };

    // Frontend views, object kinds and personal property sets Shareme has no separate right for: no problem when left out
    private static readonly HashSet<int> UiOnly = [1, 2, 13, 19, 34, 39, 44, 50, 55, 69, 74, 75, 90, 95, 100, 106, 111, 116, 121, 124, 127, 132, 137, 142, 147, 152, 157,
        167, 172, 177, 178, 183, 191, 196, 206, 211, 216, 221, 231, 236, 242, 247, 252, 257, 262, 264, 269, 274, 281, 282, 285, 291, 293, 295, 302, 308, 311,
        25, 26, 32, 33, 60, 61, 67, 68, 158, 159, 165, 166, 197, 198, 204, 205, 222, 223, 229, 230, 265, 266, 267, 268, GlobalAdmin];

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
        Add([NodePermissions.Read], 5, 17, 37, 42, 45, 48, 53, 58, 72, 104, 123, 150, 155, 194, 214, 219, 240, 245, 250, 279);
        Add([NodePermissions.Create], 3, 15, 35, 40, 46, 51, 56, 70, 148, 153, 192, 238, 243, 248, 277, 237);
        Add([NodePermissions.Update], 6, 18, 38, 43, 49, 54, 59, 73, 151, 156, 195, 215, 220, 241, 246, 251, 280);
        Add([NodePermissions.Move], 43, 49, 246); // Sharedien has no move right: editing an asset, category or entity included it
        Add([NodePermissions.Delete], 4, 16, 36, 41, 47, 52, 57, 71, 122, 149, 154, 193, 213, 218, 239, 244, 249, 278);
        Add([AssetPermissions.Download], 14, 309);
        Add([NodePermissions.ManagePermissions], 102, 103, 105);

        // catalog: global property sets, definitions, groups, languages, user contexts
        Add([MetadataPermissions.Manage], 27, 28, 30, 62, 63, 65, 76, 77, 79, 107, 108, 110, 112, 113, 115, 160, 161, 163,
            184, 185, 186, 199, 200, 202, 224, 225, 227, 270, 271, 273);

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
        return map;
    }
}

/// <param name="TenantRoleIds">Sharedien role to the Shareme role used where Sharedien assigned it tenant wide (no node rights in it).</param>
/// <param name="NodeRoleIds">Sharedien role to the Shareme role used in node level bindings (node rights only).</param>
public sealed record IdentityPlan(
    IReadOnlyList<User> Users,
    IReadOnlyList<UserProfile> Profiles,
    IReadOnlyList<Role> Roles,
    IReadOnlyList<PermissionGrant> Grants,
    IReadOnlyList<UserGroup> Groups,
    IReadOnlyDictionary<long, Guid> UserIds,
    IReadOnlyDictionary<string, Guid> UserByName,
    IReadOnlyDictionary<long, Guid> TenantRoleIds,
    IReadOnlyDictionary<long, Guid> NodeRoleIds,
    IReadOnlyDictionary<long, Guid> GroupIds,
    IReadOnlyList<Shareme.Workspaces.WorkspaceRoleBinding>? WorkspaceRoleBindings = null)
{
    public static readonly IdentityPlan Empty = new([], [], [], [], [], new Dictionary<long, Guid>(), new Dictionary<string, Guid>(),
        new Dictionary<long, Guid>(), new Dictionary<long, Guid>(), new Dictionary<long, Guid>());
}

/// <summary>
/// Users, groups, roles and their grants of the source tenant. Passwords never move: users set one through "forgot
/// password". Sharedien gives node access only through node assignments, so tenant wide roles carry no node rights here.
/// </summary>
public sealed class IdentityMapper(SourceIdentity source, MapTarget target, ImportReport report, Func<string?, string, string> name)
{
    private const int Active = 1;

    // Sharedien's system roles on workspaces and collections become Shareme's workspace roles, so members show as such
    private static readonly Dictionary<string, string> WorkspaceRoleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["workspaceOwner"] = Shareme.Contracts.Workspaces.WorkspaceRoles.Owner,
        ["collectionOwner"] = Shareme.Contracts.Workspaces.WorkspaceRoles.Owner,
        ["workspaceCollaborator"] = Shareme.Contracts.Workspaces.WorkspaceRoles.Collaborator,
    };
    private static readonly HashSet<int> NeverApproved = [10, 11, 20, 21]; // email verification or approval pending, expired or denied

    public IdentityPlan Map()
    {
        // only the groups and users the source tenant uses; Sharedien keeps the accounts of all tenants in one table
        var tenantGroups = source.GroupRoles.Select(g => g.GroupId).Concat(source.NodePermissions.Select(p => p.GroupId).OfType<long>()).ToHashSet();
        var tenantUsers = source.GroupUsers.Where(m => tenantGroups.Contains(m.GroupId)).Select(m => m.UserId)
            .Concat(source.NodePermissions.Select(p => p.UserId).OfType<long>()).ToHashSet();

        var users = new List<User>();
        var profiles = new List<UserProfile>();
        var userIds = new Dictionary<long, Guid>();
        var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in source.Users.OrderBy(u => u.Id))
        {
            if (!tenantUsers.Contains(row.Id))
            {
                report.Skip("users of other tenants");
                continue;
            }

            if (NeverApproved.Contains(row.Status))
            {
                report.Skip("registrations never approved");
                continue;
            }

            var email = row.Email?.Trim().Normalize(NormalizationForm.FormKC);
            var userName = Cap(email is { Length: > 0 } ? email : row.UserName.Trim().Normalize(NormalizationForm.FormKC), 256)!;
            var normalized = userName.ToUpperInvariant();
            var id = SourceIds.For(target.TenantId, "Admin.User", row.Id, row.Identifier);
            if (target.ExistingUsers.TryGetValue(normalized, out var known) && known.Id != id)
            {
                if (known.TenantId != target.TenantId)
                {
                    // user names are unique across tenants: this person already has an account elsewhere
                    report.Problem("user_in_other_tenant", "Admin.User", row.Id);
                    continue;
                }

                report.Problem("user_merged", "Admin.User", row.Id);
                userIds[row.Id] = known.Id;
            }
            else if (users.FirstOrDefault(u => u.NormalizedUserName == normalized) is { } twin)
            {
                // the same person with a local and an external account in Sharedien: one Shareme user
                report.Problem("user_merged", "Admin.User", row.Id);
                userIds[row.Id] = twin.Id;
            }
            else
            {
                var external = row.IsExternal;
                if (external)
                {
                    // ponytail: no external sign in in Shareme yet; a password would bypass the identity provider, so these stay inactive
                    report.Problem("external_user_inactive", "Admin.User", row.Id);
                }

                var user = new User
                {
                    Id = id,
                    TenantId = target.TenantId,
                    UserName = userName,
                    NormalizedUserName = normalized,
                    Email = Cap(email, 256),
                    NormalizedEmail = Cap(email, 256)?.ToUpperInvariant(),
                    EmailConfirmed = email is not null,
                    LockoutEnabled = true,
                    FirstName = Cap(row.FirstName, 100),
                    LastName = Cap(row.LastName, 100),
                    IsActive = row.Status == Active && !external,
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
        var tenantRoleIds = new Dictionary<long, Guid>();
        var nodeRoleIds = new Dictionary<long, Guid>();
        var roleNames = new HashSet<string>(target.ExistingRoleNames, StringComparer.OrdinalIgnoreCase);
        var permissions = source.RolePermissions.ToLookup(p => p.RoleId, p => p.Permission);
        var unmapped = new HashSet<int>();
        var bindings = new List<Shareme.Workspaces.WorkspaceRoleBinding>();
        var workspaceRoles = new Dictionary<string, Guid>(target.WorkspaceRoles);
        foreach (var row in source.Roles.OrderBy(r => r.Id))
        {
            if (row.IsSystem && WorkspaceRoleKeys.TryGetValue(row.Identifier, out var key))
            {
                if (!workspaceRoles.TryGetValue(key, out var workspaceRole))
                {
                    var (workspaceRoleName, rights) = Shareme.Workspaces.WorkspaceRoleDefinitions.All[key];
                    workspaceRole = AddRole(roles, grants, roleNames, workspaceRoleName, "WorkspaceRole:" + key, 0, rights);
                    var binding = new Shareme.Workspaces.WorkspaceRoleBinding { Key = key, RoleId = workspaceRole, TenantId = target.TenantId };
                    Ids.Set(binding, SourceIds.Derived(target.TenantId, "WorkspaceRoleBinding:" + key, 0));
                    bindings.Add(binding);
                    workspaceRoles[key] = workspaceRole;
                }

                nodeRoleIds[row.Id] = workspaceRole;
                continue;
            }

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

            var roleName = name(row.TranslationIdentifier, row.Identifier);
            if (permissions[row.Id].Contains(PermissionMap.GlobalAdmin) && target.AdminRoleId is { } admin)
            {
                // the admin role holds every permission, on every node
                tenantRoleIds[row.Id] = admin;
                nodeRoleIds[row.Id] = admin;
                continue;
            }

            var tenantNames = names.Where(n => !PermissionMap.NodeScoped.Contains(n)).ToList();
            var nodeNames = names.Where(PermissionMap.NodeScoped.Contains).ToList();
            if (tenantNames.Count > 0)
            {
                tenantRoleIds[row.Id] = AddRole(roles, grants, roleNames, roleName, "Admin.Role", row.Id, tenantNames);
            }

            if (nodeNames.Count > 0)
            {
                nodeRoleIds[row.Id] = AddRole(roles, grants, roleNames, roleName + " (items)", "Admin.Role:items", row.Id, nodeNames);
            }
        }

        var groups = new List<UserGroup>();
        var groupIds = new Dictionary<long, Guid>();
        var groupNames = new HashSet<string>(target.ExistingGroupNames, StringComparer.OrdinalIgnoreCase);
        var members = source.GroupUsers.ToLookup(m => m.GroupId, m => m.UserId);
        var groupRoles = source.GroupRoles.ToLookup(g => g.GroupId, g => g.RoleId);
        foreach (var row in source.Groups.OrderBy(g => g.Id))
        {
            if (!tenantGroups.Contains(row.Id))
            {
                report.Skip("groups of other tenants");
                continue;
            }

            var group = new UserGroup
            {
                Name = Unique(Cap(row.Name, 200)!, groupNames),
                Description = Cap(row.Description, 1000),
                TenantId = target.TenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            Ids.Set(group, SourceIds.For(target.TenantId, "Admin.UserGroup", row.Id, row.Identifier));
            group.Members.AddRange(members[row.Id].Where(userIds.ContainsKey).Select(u => userIds[u]).Distinct().Select(u => new UserGroupMember { GroupId = group.Id, UserId = u }));
            group.Roles.AddRange(groupRoles[row.Id].Where(tenantRoleIds.ContainsKey).Select(r => tenantRoleIds[r]).Distinct().Select(r => new UserGroupRole { GroupId = group.Id, RoleId = r }));
            groups.Add(group);
            groupIds[row.Id] = group.Id;
        }

        report.Mapped["users"] = users.Count;
        report.Mapped["roles"] = roles.Count;
        report.Mapped["permission grants"] = grants.Count;
        report.Mapped["groups"] = groups.Count;
        return new IdentityPlan(users, profiles, roles, grants, groups, userIds, byName, tenantRoleIds, nodeRoleIds, groupIds, bindings);
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

            if (!identity.NodeRoleIds.TryGetValue(row.RoleId, out var role) || principal is not { } p)
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

    private Guid AddRole(List<Role> roles, List<PermissionGrant> grants, HashSet<string> taken, string roleName, string table, long sourceId, IEnumerable<string> names)
    {
        var unique = Unique(Cap(roleName, 200)!, taken);
        var role = new Role
        {
            Id = SourceIds.Derived(target.TenantId, table, sourceId),
            TenantId = target.TenantId,
            Name = unique,
            NormalizedName = unique.ToUpperInvariant(),
            Description = "Imported from Sharedien",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        roles.Add(role);
        foreach (var permission in names)
        {
            var grant = new PermissionGrant
            {
                TenantId = target.TenantId,
                Name = permission,
                ProviderType = PermissionProviderType.Role,
                ProviderKey = role.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            Ids.Set(grant, SourceIds.Derived(target.TenantId, $"{table}:{permission}", sourceId));
            grants.Add(grant);
        }

        return role.Id;
    }

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
