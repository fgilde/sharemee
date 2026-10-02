using System.Text.Json;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Search;

namespace Shareme.Search;

public sealed class SavedSearch : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public required SearchRequest Request { get; set; }

    public Guid OwnerId { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>One full rebuild of the search index, across all tenants.</summary>
[NotAudited]
public sealed class ReindexRun : Entity
{
    public string State { get; set; } = "queued";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int Indexed { get; set; }

    public int Failed { get; set; }

    public string? Error { get; set; }

    public Guid? StartedBy { get; set; }
}

internal sealed class SearchModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SavedSearch>(saved =>
        {
            saved.ToTable("SavedSearches", "sm");
            saved.Property(s => s.Name).HasMaxLength(200);
            saved.Property(s => s.Request).HasColumnType("jsonb").HasConversion(
                r => JsonSerializer.Serialize(r, (JsonSerializerOptions?)null),
                s => JsonSerializer.Deserialize<SearchRequest>(s, (JsonSerializerOptions?)null)!);
            saved.HasIndex(s => new { s.TenantId, s.OwnerId });
        });

        modelBuilder.Entity<ReindexRun>(run =>
        {
            run.ToTable("ReindexRuns", "sm");
            run.Property(r => r.State).HasMaxLength(20);
            run.Property(r => r.Error).HasMaxLength(2000);
        });
    }
}
