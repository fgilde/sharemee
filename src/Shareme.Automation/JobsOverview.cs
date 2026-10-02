using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Shareme.Assets;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Graph;
using Shareme.Graph;
using Shareme.Search;

namespace Shareme.Automation;

/// <summary>All Shareme runs of the current user in one list: mass edits, imports, AI tagging, rule runs, archive extractions, and reindexing for admins.</summary>
public sealed record GetJobsOverview : IQuery<Result<JobsOverviewDto>>;

internal static class JobsOverviewEndpoints
{
    public static void Map(WebApplication app) =>
        app.MapGet("/api/v1/jobs", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetJobsOverview(), ct).ToHttpResult()).WithTags("Jobs").RequireAuthorization();
}

internal sealed class JobsOverviewHandler(CoworkeeDbContext db, ICurrentUser currentUser, NodeAccess access) : IHandler<GetJobsOverview, Result<JobsOverviewDto>>
{
    private const int PerKind = 20;

    public async Task<Result<JobsOverviewDto>> HandleAsync(GetJobsOverview query, CancellationToken cancellationToken)
    {
        // admins see every run of the tenant, others their own
        var me = currentUser.UserId ?? Guid.Empty;
        var admin = await access.IsGlobalAsync(NodePermissions.Admin, cancellationToken);
        var runs = new List<JobSummaryDto>();
        runs.AddRange((await db.Set<DataJob>().AsNoTracking().Where(j => admin || j.StartedBy == me).OrderByDescending(j => j.CreatedAt).Take(PerKind * 3).ToListAsync(cancellationToken))
            .Select(j => new JobSummaryDto(j.Id, j.Kind, j.State, j.Total, j.Executed, j.Failed, j.Error, j.CreatedAt, $"/data-jobs/{j.Id}")));
        runs.AddRange((await db.Set<RuleJob>().AsNoTracking().Where(j => admin || j.StartedBy == me).OrderByDescending(j => j.CreatedAt).Take(PerKind).ToListAsync(cancellationToken))
            .Select(j => new JobSummaryDto(j.Id, JobKinds.Rules, j.State, j.Total, j.Executed, j.Failed, j.Error, j.CreatedAt, "/admin/rules")));

        // extractions keep no starter; they show for archives the user can read
        foreach (var extraction in await db.Set<ArchiveExtraction>().AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(PerKind * 5).ToListAsync(cancellationToken))
        {
            if (runs.Count(r => r.Kind == JobKinds.Archive) < PerKind && await access.CanAsync(NodePermissions.Read, extraction.ArchiveId, cancellationToken))
            {
                runs.Add(new JobSummaryDto(extraction.Id, JobKinds.Archive, extraction.State, extraction.Extracted + extraction.Filtered + extraction.Failed, extraction.Extracted, extraction.Failed,
                    extraction.Error, extraction.CreatedAt, $"/assets/{extraction.ArchiveId}"));
            }
        }

        if (admin)
        {
            runs.AddRange((await db.Set<ReindexRun>().AsNoTracking().OrderByDescending(r => r.StartedAt).Take(PerKind).ToListAsync(cancellationToken))
                .Select(r => new JobSummaryDto(r.Id, JobKinds.Reindex, r.State, r.Indexed + r.Failed, r.Indexed, r.Failed, r.Error, r.StartedAt, "/admin/node-types")));
        }

        var dashboard = await access.IsGlobalAsync(Coworkee.Contracts.Jobs.JobsPermissions.View, cancellationToken) ? "/admin/jobs" : null;
        return new JobsOverviewDto(runs.OrderByDescending(r => r.CreatedAt).ToList(), dashboard);
    }
}
