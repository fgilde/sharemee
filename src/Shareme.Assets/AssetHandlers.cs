using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Graph;

namespace Shareme.Assets;

public sealed record GetAsset(Guid Id) : IQuery<Result<AssetDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Read;
}

public sealed record BlobDownload(Stream Content, string FileName, string MimeType, string? Sha256 = null, DateTimeOffset? LastModified = null);

public sealed record DownloadAsset(Guid Id, int? Version) : IQuery<Result<BlobDownload>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => AssetPermissions.Download;
}

public sealed record CreateDownloadLink(Guid Id, int? Version) : ICommand<Result<DownloadLinkDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => AssetPermissions.Download;
}

internal sealed class AssetHandlers(CoworkeeDbContext db, IBlobStorage storage, BlobLinks links, TimeProvider clock)
    : IHandler<GetAsset, Result<AssetDto>>, IHandler<DownloadAsset, Result<BlobDownload>>, IHandler<CreateDownloadLink, Result<DownloadLinkDto>>
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);

    private static readonly Error NotFound = Error.NotFound("assets.not_found", "The asset or version does not exist.");

    public async Task<Result<AssetDto>> HandleAsync(GetAsset query, CancellationToken cancellationToken)
    {
        if (await db.Set<Asset>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == query.Id, cancellationToken) is not { } asset)
        {
            return NotFound;
        }

        var versions = await db.Set<AssetVersion>().AsNoTracking().Where(v => v.AssetId == asset.Id).OrderByDescending(v => v.Number)
            .Select(v => new AssetVersionDto(v.Id, v.Number, v.FileName, v.Size, v.Sha256, v.MimeType, v.Extension, v.CreatedAt, v.CreatedBy))
            .ToListAsync(cancellationToken);
        return new AssetDto(asset.Id, asset.ParentId, asset.Name, asset.AssetTypeKey, asset.Status, versions.FirstOrDefault(v => v.Id == asset.CurrentVersionId), versions);
    }

    public async Task<Result<BlobDownload>> HandleAsync(DownloadAsset query, CancellationToken cancellationToken)
    {
        if (await VersionAsync(query.Id, query.Version, cancellationToken) is not { } version
            || await storage.OpenReadAsync(version.BlobKey, cancellationToken) is not { } content)
        {
            return NotFound;
        }

        return new BlobDownload(content, version.FileName, version.MimeType, version.Sha256, version.CreatedAt);
    }

    public async Task<Result<DownloadLinkDto>> HandleAsync(CreateDownloadLink command, CancellationToken cancellationToken)
    {
        if (await VersionAsync(command.Id, command.Version, cancellationToken) is not { } version)
        {
            return NotFound;
        }

        var token = links.Create(version.BlobKey, LinkLifetime);
        return new DownloadLinkDto($"/api/v1/blobs/{Uri.EscapeDataString(token)}", clock.GetUtcNow().Add(LinkLifetime));
    }

    private async Task<AssetVersion?> VersionAsync(Guid assetId, int? number, CancellationToken cancellationToken)
    {
        if (await db.Set<Asset>().AsNoTracking().Where(a => a.Id == assetId).Select(a => new { a.CurrentVersionId }).SingleOrDefaultAsync(cancellationToken) is not { } asset)
        {
            return null;
        }

        var versions = db.Set<AssetVersion>().AsNoTracking().Where(v => v.AssetId == assetId);
        return await (number is { } n ? versions.Where(v => v.Number == n) : versions.Where(v => v.Id == asset.CurrentVersionId)).SingleOrDefaultAsync(cancellationToken);
    }
}

/// <summary>Resolves anonymous download links; the token is the authorization.</summary>
internal sealed class BlobLinkResolver(CoworkeeDbContext db, IBlobStorage storage, BlobLinks links)
{
    public async Task<BlobDownload?> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        if (!links.TryResolve(token, out var key))
        {
            return null;
        }

        var version = await db.Set<AssetVersion>().IgnoreQueryFilters().AsNoTracking().Where(v => v.BlobKey == key)
            .Select(v => new { v.FileName, v.MimeType, v.Sha256, v.CreatedAt }).FirstOrDefaultAsync(cancellationToken);
        return version is not null && await storage.OpenReadAsync(key, cancellationToken) is { } content
            ? new BlobDownload(content, version.FileName, version.MimeType, version.Sha256, version.CreatedAt)
            : null;
    }
}

/// <summary>Queues the files of purged assets for deletion in the purge transaction.</summary>
internal sealed class AssetPurgeListener(CoworkeeDbContext db) : INodePurgeListener
{
    public async Task OnPurgingAsync(IQueryable<Guid> nodeIds, CancellationToken cancellationToken)
    {
        var keys = await db.Set<AssetVersion>().Where(v => nodeIds.Contains(v.AssetId)).Select(v => v.BlobKey).ToListAsync(cancellationToken);
        if (keys.Count == 0)
        {
            return;
        }

        db.AddRange(keys.Select(key => new OrphanBlob { Key = key }));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Deletes files of purged assets and abandoned uploads.</summary>
public sealed class StorageCleanupJob(CoworkeeDbContext db, IBlobStorage storage, TimeProvider clock, ILogger<StorageCleanupJob> logger) : IRecurringJob
{
    public const string Id = "shareme-storage-cleanup";

    private const int BatchSize = 500;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var skipped = new HashSet<Guid>();
        while (await db.Set<OrphanBlob>().Where(o => !skipped.Contains(o.Id)).OrderBy(o => o.Id).Take(BatchSize).ToListAsync(cancellationToken) is { Count: > 0 } orphans)
        {
            foreach (var orphan in orphans)
            {
                try
                {
                    await storage.DeleteAsync(orphan.Key, cancellationToken);
                    db.Remove(orphan);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // stays for the next run
                    skipped.Add(orphan.Id);
                    logger.LogWarning(exception, "Blob {Key} could not be deleted.", orphan.Key);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var now = clock.GetUtcNow();
        while (await db.Set<UploadSession>().IgnoreQueryFilters().Where(s => s.ExpiresAt <= now && !skipped.Contains(s.Id)).OrderBy(s => s.Id).Take(BatchSize).ToListAsync(cancellationToken) is { Count: > 0 } sessions)
        {
            foreach (var session in sessions)
            {
                try
                {
                    await storage.DeletePrefixAsync(session.ChunkPrefix, cancellationToken);
                    if (session is { State: UploadStates.Completing, AssetId: { } assetId })
                    {
                        // the job never finished; a placeholder without content is marked failed
                        await db.Set<Asset>().IgnoreQueryFilters().Where(a => a.Id == assetId && a.CurrentVersionId == null)
                            .ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, AssetStatuses.Failed), cancellationToken);
                    }

                    db.Remove(session);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    skipped.Add(session.Id);
                    logger.LogWarning(exception, "Upload {SessionId} could not be cleaned up.", session.Id);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
