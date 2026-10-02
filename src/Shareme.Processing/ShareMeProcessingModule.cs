using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using ImageMagick;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shareme.Assets;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Graph;

namespace Shareme.Processing;

/// <summary>Renders thumbnails and previews; jobs run on the <see cref="Queue"/> queue, served by the processing host.</summary>
[DependsOn(typeof(ShareMeAssetsModule))]
public sealed class ShareMeProcessingModule : CoworkeeModule, IWebModule
{
    public const string Queue = "processing";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(ShareMeProcessingModule).Assembly);
        context.Services.AddScoped<RenderDerivationsJob>();
        context.Services.AddRecurringJob<RenditionSweepJob>(RenditionSweepJob.Id, "*/10 * * * *");

        // oversized or slow images fail instead of eating memory, disk and cores of the host
        ResourceLimits.Width = 16_000;
        ResourceLimits.Height = 16_000;
        ResourceLimits.Area = 200_000_000;
        ResourceLimits.Memory = 1024UL * 1024 * 1024;
        ResourceLimits.Disk = 0;
        ResourceLimits.Thread = 2;
        ResourceLimits.Time = 120;
    }

    public void ConfigureApplication(WebApplication app) =>
        app.MapPost("/api/v1/assets/{id:guid}/derivations/rerender", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new RerenderAsset(id), ct).ToHttpResult())
            .WithTags("Assets").RequireAuthorization();
}

/// <summary>Renders the current version again, for example after a failure or a renderer update.</summary>
public sealed record RerenderAsset(Guid Id) : ICommand<Result>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Update;
}

internal sealed class RerenderHandler(CoworkeeDbContext db, IBackgroundJobs jobs, TimeProvider clock) : IHandler<RerenderAsset, Result>
{
    public async Task<Result> HandleAsync(RerenderAsset command, CancellationToken cancellationToken)
    {
        if (await db.Set<Asset>().SingleOrDefaultAsync(a => a.Id == command.Id, cancellationToken) is not { CurrentVersionId: { } versionId } asset)
        {
            return Error.NotFound("assets.not_found", "The asset has no content yet.");
        }

        var now = clock.GetUtcNow();
        var existing = await db.Set<Derivation>().Where(d => d.VersionId == versionId).ToListAsync(cancellationToken);
        foreach (var derivation in existing)
        {
            if (derivation.BlobKey is { } key)
            {
                db.Add(new OrphanBlob { Key = key });
            }

            derivation.State = DerivationStates.Pending;
            derivation.BlobKey = null;
            derivation.Error = null;
            derivation.PlannedAt = now;
        }

        db.AddRange(DerivationKinds.All.Except(existing.Select(d => d.Kind))
            .Select(kind => new Derivation { VersionId = versionId, AssetId = asset.Id, Kind = kind, PlannedAt = now }));
        asset.RenditionTag = null;
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<RenderDerivationsJob, Guid>(versionId, ShareMeProcessingModule.Queue);
        return Result.Success();
    }
}

/// <summary>Plans the renditions of every new version and hands them to the processing queue.</summary>
internal sealed class PlanDerivations(CoworkeeDbContext db, IBackgroundJobs jobs, TimeProvider clock) : IDomainEventHandler<AssetVersionCreated>
{
    public async Task HandleAsync(AssetVersionCreated domainEvent, CancellationToken cancellationToken)
    {
        var existing = await db.Set<Derivation>().Where(d => d.VersionId == domainEvent.VersionId).Select(d => new { d.Kind, d.State }).ToListAsync(cancellationToken);
        var missing = DerivationKinds.All.Except(existing.Select(d => d.Kind)).ToList();
        if (missing.Count > 0)
        {
            db.AddRange(missing.Select(kind => new Derivation { VersionId = domainEvent.VersionId, AssetId = domainEvent.AssetId, Kind = kind, PlannedAt = clock.GetUtcNow() }));
            await db.SaveChangesAsync(cancellationToken);
        }

        // also on redelivery, in case the first enqueue failed after the rows were saved
        if (missing.Count > 0 || existing.Any(d => d.State == DerivationStates.Pending))
        {
            jobs.Enqueue<RenderDerivationsJob, Guid>(domainEvent.VersionId, ShareMeProcessingModule.Queue);
        }
    }
}

/// <summary>Re-queues renditions whose job got lost and gives up on those stuck for a day.</summary>
internal sealed class RenditionSweepJob(CoworkeeDbContext db, IBackgroundJobs jobs, TimeProvider clock) : IRecurringJob
{
    public const string Id = "shareme-rendition-sweep";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var pending = db.Set<Derivation>().IgnoreQueryFilters().Where(d => d.State == DerivationStates.Pending);
        await pending.Where(d => d.PlannedAt < now.AddDays(-1))
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.State, DerivationStates.Failed).SetProperty(x => x.Error, "Rendering did not finish within a day."), cancellationToken);
        foreach (var versionId in await pending.Where(d => d.PlannedAt < now.AddMinutes(-10)).Select(d => d.VersionId).Distinct().Take(500).ToListAsync(cancellationToken))
        {
            jobs.Enqueue<RenderDerivationsJob, Guid>(versionId, ShareMeProcessingModule.Queue);
        }
    }
}

/// <summary>Renders the pending derivations of one version; runs with or without a tenant (sweeps), so it reads by id across tenants.</summary>
internal sealed class RenderDerivationsJob(CoworkeeDbContext db, IBlobStorage storage, TimeProvider clock, ILogger<RenderDerivationsJob> logger) : IBackgroundJob<Guid>
{
    private static readonly Dictionary<string, int> Sizes = new() { [DerivationKinds.Thumbnail] = 320, [DerivationKinds.Preview] = 1600 };

    // ponytail: still images through Magick.NET only; video, office, pdf and 3d renditions come in P3
    private static readonly Dictionary<string, MagickFormat> Renderable = new()
    {
        ["image/jpeg"] = MagickFormat.Jpeg,
        ["image/png"] = MagickFormat.Png,
        ["image/gif"] = MagickFormat.Gif,
        ["image/webp"] = MagickFormat.WebP,
        ["image/tiff"] = MagickFormat.Tiff,
        ["image/bmp"] = MagickFormat.Bmp,
        ["image/vnd.adobe.photoshop"] = MagickFormat.Psd,
    };

    public async Task ExecuteAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var version = await db.Set<AssetVersion>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId, cancellationToken);
        var pending = await db.Set<Derivation>().IgnoreQueryFilters().Where(d => d.VersionId == versionId && d.State == DerivationStates.Pending).ToListAsync(cancellationToken);
        if (version is null || pending.Count == 0)
        {
            return;
        }

        if (!Renderable.TryGetValue(version.MimeType, out var format))
        {
            pending.ForEach(d => d.State = DerivationStates.NotSupported);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // a missing original is a storage or configuration problem: throw so the job is retried instead of failing the rendition
        await using var original = await storage.OpenReadAsync(version.BlobKey, cancellationToken)
            ?? throw new FileNotFoundException($"The original of version {versionId} is not in storage.");
        MagickImage image;
        try
        {
            // the format comes from the checked file type, never from content sniffing
            image = new MagickImage(original, new MagickReadSettings { Format = format, FrameIndex = 0, FrameCount = 1 });
        }
        catch (MagickException exception)
        {
            await FailAsync(pending, exception, cancellationToken);
            return;
        }

        using (image)
        {
            image.AutoOrient();
            if (image.GetColorProfile() is not null)
            {
                image.TransformColorSpace(ColorProfiles.SRGB);
            }
            else if (image.ColorSpace != ColorSpace.sRGB)
            {
                image.ColorSpace = ColorSpace.sRGB;
            }

            foreach (var derivation in pending)
            {
                await RenderAsync(image, version, derivation, cancellationToken);
            }
        }
    }

    private async Task RenderAsync(MagickImage image, AssetVersion version, Derivation derivation, CancellationToken cancellationToken)
    {
        byte[] bytes;
        uint width, height;
        try
        {
            using var rendition = image.Clone();
            var size = (uint)Sizes[derivation.Kind];
            rendition.Resize(new MagickGeometry(size, size) { Greater = true });
            rendition.Strip();
            rendition.Quality = 80;
            bytes = rendition.ToByteArray(MagickFormat.WebP);
            (width, height) = (rendition.Width, rendition.Height);
        }
        catch (MagickException exception)
        {
            await FailAsync([derivation], exception, cancellationToken);
            return;
        }

        var key = BlobKeys.New(version.TenantId, clock);
        await storage.PutAsync(key, new MemoryStream(bytes), "image/webp", cancellationToken);
        try
        {
            derivation.BlobKey = key;
            derivation.MimeType = "image/webp";
            derivation.Size = bytes.Length;
            derivation.Width = (int)width;
            derivation.Height = (int)height;
            derivation.State = DerivationStates.Ready;
            if (derivation.Kind == DerivationKinds.Thumbnail
                && await db.Set<Asset>().IgnoreQueryFilters().SingleOrDefaultAsync(a => a.Id == version.AssetId && a.CurrentVersionId == version.Id, cancellationToken) is { } asset)
            {
                // lists pick up the new thumbnail through the asset's own change event
                asset.RenditionTag = Guid.CreateVersion7().ToString("N");
            }

            // each rendition lands on its own, so a later failure keeps the earlier ones
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }
    }

    private async Task FailAsync(IEnumerable<Derivation> derivations, MagickException exception, CancellationToken cancellationToken)
    {
        logger.LogWarning(exception, "A rendition could not be created.");
        foreach (var derivation in derivations)
        {
            derivation.State = DerivationStates.Failed;
            derivation.Error = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
