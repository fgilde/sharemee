namespace Shareme.Migrations;

/// <summary>
/// Development users: seeded into an empty database and, with Keycloak, into its realm with the same password (see LOGIN.md).
/// The passwords are for development only; change them before you deploy anywhere.
/// </summary>
public static class DemoUsers
{
    public const string MemberRole = "Member";

    public static readonly DemoUser[] All =
    [
        new("admin@sharemee.local", "Ada", "Admin", "Sm1!lmyfOhHoylC0", IsAdmin: true),
        new("user@sharemee.local", "Uwe", "User", "Sm1!06houIzPkGGB", Roles: [MemberRole]),
    ];
}

public sealed record DemoUser(string Email, string FirstName, string LastName, string Password, bool IsAdmin = false, string[]? Roles = null);
