using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shareme.Infrastructure;

namespace Shareme.Migrations;

/// <summary>
/// Development only: a system set up before <see cref="DemoUsers"/> existed gets the demo users and their roles it lacks.
/// Existing users and roles stay as they are.
/// </summary>
internal static class DemoUserTopUp
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<ShareMeDbContext>();
        if (await db.Set<Tenant>().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.IsDefault, cancellationToken) is not { } tenant)
        {
            return;
        }

        var seed = new IdentitySeedOptions();
        DemoSeed.Configure(seed);
        var missing = new List<SeedUser>();
        foreach (var user in seed.Users)
        {
            var email = user.Email.ToUpperInvariant();
            if (!await db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == email && u.TenantId == tenant.Id, cancellationToken))
            {
                missing.Add(user);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        var adminRole = await db.Set<Role>().IgnoreQueryFilters().FirstAsync(r => (r.TenantId == null || r.TenantId == tenant.Id) && r.IsSystem && r.Name == SystemRoles.Admin, cancellationToken);
        var actorId = await db.Set<IdentityUserRole<Guid>>().Where(r => r.RoleId == adminRole.Id).Select(r => r.UserId).FirstOrDefaultAsync(cancellationToken);
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(actorId == Guid.Empty ? null : actorId, tenant.Id));

        var roles = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in missing.SelectMany(u => u.Roles).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            roles[name] = await db.Set<Role>().IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Name == name, cancellationToken) ?? AddRole(db, seed.Roles.First(r => r.Name == name), tenant.Id);
        }

        await db.SaveChangesAsync(cancellationToken);

        var users = services.GetRequiredService<UserManager<User>>();
        foreach (var seedUser in missing)
        {
            var user = new User
            {
                TenantId = tenant.Id, UserName = seedUser.Email, Email = seedUser.Email, EmailConfirmed = true, FirstName = seedUser.FirstName, LastName = seedUser.LastName,
            };
            var created = await users.CreateAsync(user, seedUser.Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException($"Demo user {seedUser.Email}: {string.Join(", ", created.Errors.Select(e => e.Description))}");
            }

            var roleIds = seedUser.Roles.Select(name => roles[name].Id).Concat(seedUser.IsAdmin ? [adminRole.Id] : []);
            db.Set<IdentityUserRole<Guid>>().AddRange(roleIds.Select(id => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = id }));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Role AddRole(ShareMeDbContext db, SeedRole seed, Guid tenantId)
    {
        var role = new Role { Name = seed.Name, NormalizedName = seed.Name.ToUpperInvariant(), Description = seed.Description, TenantId = tenantId };
        db.Add(role);
        db.AddRange(seed.Permissions.Select(p => new PermissionGrant { TenantId = tenantId, Name = p, ProviderType = PermissionProviderType.Role, ProviderKey = role.Id }));
        return role;
    }
}
