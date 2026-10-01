using Microsoft.EntityFrameworkCore;
using Shareme.Infrastructure;

namespace Shareme.Migrations.Tests;

public sealed class ModelTests
{
    [Fact]
    public void Migrations_cover_the_whole_model_including_auth_tables()
    {
        using var db = new ShareMeDbContextDesignFactory().CreateDbContext([]);

        db.Model.GetEntityTypes().ShouldContain(t => t.GetTableName() == "OpenIddictApplications" && t.GetSchema() == "auth");
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
