using Coworkee.Contracts.Chat;
using Coworkee.Identity.Setup;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Drm;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;
using Shareme.Contracts.Workspaces;

namespace Shareme.Migrations;

/// <summary>Development seed with the users of <see cref="DemoUsers"/>; a system that is already set up stays untouched.</summary>
public static class DemoSeed
{
    public static void Configure(IdentitySeedOptions seed)
    {
        seed.TenantName = "Sharemee";
        seed.Roles.Add(new SeedRole(DemoUsers.MemberRole, "Reads, uploads and edits content, works in workspaces",
        [
            NodePermissions.Read, NodePermissions.Create, NodePermissions.Update, NodePermissions.Move, AssetPermissions.Download,
            WorkspacePermissions.Create, PeoplePermissions.View, DrmPermissions.Offers, DrmPermissions.Purchase, ChatPermissions.Use,
        ]));
        seed.Users.AddRange(DemoUsers.All.Select(u => new SeedUser(u.Email, u.Password, u.FirstName, u.LastName, u.IsAdmin) { Roles = u.Roles ?? [] }));
    }

    /// <summary>A deployment: the organisation and the administrator of the deploy parameters, no demo users.</summary>
    public static void ConfigureDeployment(IdentitySeedOptions seed, SeedUser admin)
    {
        seed.TenantName = "Sharemee";
        seed.Users.Add(admin);
    }
}
