using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shareme.Assets;
using Shareme.Contracts.Assets;

namespace Shareme.Processing;

/// <summary>Renders thumbnails and previews; jobs run on the <see cref="Queue"/> queue, served by the processing host.</summary>
[DependsOn(typeof(ShareMeAssetsModule))]
public sealed class ShareMeProcessingModule : CoworkeeModule
{
    public const string Queue = "processing";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(ShareMeProcessingModule).Assembly);
        context.Services.AddScoped<RenderDerivationsJob>();

        // decompression bombs fail instead of eating the host
        ResourceLimits.Width = 30_000;
        ResourceLimits.Height = 30_000;
        ResourceLimits.Area = 300_000_000;
    }
}

/// <summary>Plans the renditions of every new version and hands them to the processing queue.</summary>
internal sealed class PlanDerivations(CoworkeeDbContext db, IBackgroundJobs jobs) : IDomainEventHandler<AssetVersionCreated>
{
    public async Task HandleAsync(AssetVersionCreated domainEvent, CancellationToken cancellationToken)
    {
        var existing = await db.Set<Derivation>().Where(d => d.VersionId == domainEvent.VersionId).Select(d => d.Kind).ToListAsync(cancellationToken);
        var missing = DerivationKinds.All.Except(existing).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        db.AddRange(missing.Select(kind => new Derivation { VersionId = domainEvent.VersionId, AssetId = domainEvent.AssetId, Kind = kind }));
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<RenderDerivationsJob, Guid>(domainEvent.VersionId, ShareMeProcessingModule.Queue);
    }
}

internal sealed class RenderDerivationsJob(CoworkeeDbContext db, IBlobStorage storage, TimeProvider clock, ILogger<RenderDerivationsJob> logger) : IBackgroundJob<Guid>
{
    private static readonly Dictionary<string, int> Sizes = new() { [DerivationKinds.Thumbnail] = 320, [DerivationKinds.Preview] = 1600 };

    // ponytail: still images through Magick.NET only; video, office, pdf and 3d renditions come in P3
    private static readonly HashSet<string> Renderable = ["image/jpeg", "image/png", "image/gif", "image/webp", "image/tiff", "image/bmp", "image/vnd.adobe.photoshop"];

    public async Task ExecuteAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var version = await db.Set<AssetVersion>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId, cancellationToken);
        var pending = await db.Set<Derivation>().Where(d => d.VersionId == versionId && d.State == DerivationStates.Pending).ToListAsync(cancellationToken);
        if (version is null || pending.Count == 0)
        {
            return;
        }

        if (!Renderable.Contains(version.MimeType))
        {
            pending.ForEach(d => d.State = DerivationStates.NotSupported);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            await using var original = await storage.OpenReadAsync(version.BlobKey, cancellationToken) ?? throw new InvalidOperationException("The original is missing.");
            using var image = new MagickImage(original, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
            image.AutoOrient();
            foreach (var derivation in pending)
            {
                using var rendition = image.Clone();
                var size = (uint)Sizes[derivation.Kind];
                rendition.Resize(new MagickGeometry(size, size) { Greater = true });
                rendition.Strip();
                rendition.Quality = 80;
                var bytes = rendition.ToByteArray(MagickFormat.WebP);
                var key = BlobKeys.New(version.TenantId, clock);
                await storage.PutAsync(key, new MemoryStream(bytes), "image/webp", cancellationToken);
                derivation.BlobKey = key;
                derivation.MimeType = "image/webp";
                derivation.Size = bytes.Length;
                derivation.Width = (int)rendition.Width;
                derivation.Height = (int)rendition.Height;
                derivation.State = DerivationStates.Ready;
            }
        }
        catch (Exception exception) when (exception is MagickException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Version {VersionId} could not be rendered.", versionId);
            foreach (var derivation in pending.Where(d => d.State == DerivationStates.Pending))
            {
                derivation.State = DerivationStates.Failed;
                derivation.Error = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
