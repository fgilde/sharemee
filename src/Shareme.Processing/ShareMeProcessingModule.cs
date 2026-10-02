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

/// <summary>Renders thumbnails, previews and playable copies; jobs run on the <see cref="Queue"/> queue, served by the processing host.</summary>
[DependsOn(typeof(ShareMeAssetsModule))]
public sealed class ShareMeProcessingModule : CoworkeeModule, IWebModule
{
    public const string Queue = "processing";

    /// <summary>Folder for work files; originals are copied there, so it needs room for the largest upload per worker.</summary>
    public const string TempRootSetting = "Shareme:Processing:TempRoot";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(ShareMeProcessingModule).Assembly);
        services.AddScoped<RenderDerivationsJob>();
        services.AddRecurringJob<RenditionSweepJob>(RenditionSweepJob.Id, "*/10 * * * *");
        services.AddSingleton<ProcessingTools>();
        services.AddSingleton<PdfSource>();
        services.AddSingleton<IRenditionSource, VideoSource>();
        services.AddSingleton<IRenditionSource, AudioSource>();
        services.AddSingleton<IRenditionSource>(provider => provider.GetRequiredService<PdfSource>());
        services.AddSingleton<IRenditionSource, OfficeSource>();
        services.AddSingleton<TechnicalMetadataReader>();

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

        var mimeType = await db.Set<AssetVersion>().Where(v => v.Id == versionId).Select(v => v.MimeType).SingleAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var existing = await db.Set<Derivation>().Where(d => d.VersionId == versionId).ToListAsync(cancellationToken);
        if (existing.Any(d => d.State == DerivationStates.Pending))
        {
            // renders can take many minutes; a second request would only run the same work twice
            return Error.Conflict("assets.rendering", "The renditions are being created already.");
        }

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

        db.AddRange(DerivationKinds.For(mimeType).Except(existing.Select(d => d.Kind))
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
        if (await db.Set<AssetVersion>().Where(v => v.Id == domainEvent.VersionId).Select(v => v.MimeType).SingleOrDefaultAsync(cancellationToken) is not { } mimeType)
        {
            return;
        }

        var existing = await db.Set<Derivation>().Where(d => d.VersionId == domainEvent.VersionId).Select(d => new { d.Kind, d.State }).ToListAsync(cancellationToken);
        var missing = DerivationKinds.For(mimeType).Except(existing.Select(d => d.Kind)).ToList();
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

/// <summary>Re-queues renditions whose job got lost, gives up on those stuck for a day and removes work folders of crashed jobs.</summary>
internal sealed class RenditionSweepJob(CoworkeeDbContext db, IBackgroundJobs jobs, Microsoft.Extensions.Configuration.IConfiguration configuration, TimeProvider clock) : IRecurringJob
{
    public const string Id = "shareme-rendition-sweep";

    // longer than the slowest render (copy, metadata, stills, ten minute encode), so a running job is not started twice
    private static readonly TimeSpan Lost = TimeSpan.FromHours(2);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var folder in new DirectoryInfo(RenderDerivationsJob.WorkRoot(configuration)) is { Exists: true } root ? root.EnumerateDirectories() : [])
        {
            if (folder.LastWriteTimeUtc < now.UtcDateTime.AddDays(-1))
            {
                try
                {
                    folder.Delete(recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // still in use; the next sweep tries again
                }
            }
        }

        var pending = db.Set<Derivation>().IgnoreQueryFilters().Where(d => d.State == DerivationStates.Pending);
        await pending.Where(d => d.PlannedAt < now.AddDays(-1))
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.State, DerivationStates.Failed).SetProperty(x => x.Error, "Rendering did not finish within a day."), cancellationToken);
        foreach (var versionId in await pending.Where(d => d.PlannedAt < now - Lost).Select(d => d.VersionId).Distinct().Take(500).ToListAsync(cancellationToken))
        {
            jobs.Enqueue<RenderDerivationsJob, Guid>(versionId, ShareMeProcessingModule.Queue);
        }
    }
}

/// <summary>
/// Renders the pending derivations of one version: images directly, other media through a source that turns them into a still image
/// (and for video and audio a playable copy). Runs with or without a tenant (sweeps), so it reads by id across tenants.
/// </summary>
internal sealed class RenderDerivationsJob(
    CoworkeeDbContext db, IBlobStorage storage, IEnumerable<IRenditionSource> sources, TechnicalMetadataReader metadata,
    Microsoft.Extensions.Configuration.IConfiguration configuration, TimeProvider clock, ILogger<RenderDerivationsJob> logger)
    : IBackgroundJob<Guid>
{
    private static readonly Dictionary<string, int> Sizes = new() { [DerivationKinds.Thumbnail] = 320, [DerivationKinds.Preview] = 1600 };

    private static readonly HashSet<string> TextTypes = ["text/plain", "text/csv", "text/markdown"];

    private static IReadOnlyDictionary<string, MagickFormat> Images => ImageFormats.ByMimeType;

    public static string WorkRoot(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
        Path.Combine(configuration[ShareMeProcessingModule.TempRootSetting] is { Length: > 0 } root ? root : Path.GetTempPath(), "shareme-render");

    /// <summary>The work file name: only a plain extension survives (tools pick filters by it), text goes in as .txt so every office version reads it.</summary>
    private static string InputName(AssetVersion version) =>
        "original" + (TextTypes.Contains(version.MimeType) ? ".txt"
            : version.Extension is { } extension && System.Text.RegularExpressions.Regex.IsMatch(extension, "^[a-z0-9]{1,10}$") ? "." + extension : string.Empty);

    public async Task ExecuteAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var version = await db.Set<AssetVersion>().IgnoreQueryFilters().SingleOrDefaultAsync(v => v.Id == versionId, cancellationToken);
        var pending = await db.Set<Derivation>().IgnoreQueryFilters().Where(d => d.VersionId == versionId && d.State == DerivationStates.Pending).ToListAsync(cancellationToken);
        if (version is null || pending.Count == 0)
        {
            return;
        }

        var work = Directory.CreateDirectory(Path.Combine(WorkRoot(configuration), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            // a missing original is a storage or configuration problem: throw so the job is retried instead of failing the rendition
            var input = Path.Combine(work, InputName(version));
            await using (var original = await storage.OpenReadAsync(version.BlobKey, cancellationToken)
                ?? throw new FileNotFoundException($"The original of version {versionId} is not in storage."))
            await using (var file = File.Create(input))
            {
                await original.CopyToAsync(file, cancellationToken);
            }

            if (version.TechnicalMetadata.Count == 0)
            {
                version.TechnicalMetadata = await metadata.ReadAsync(input, version.MimeType, work, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            var source = sources.FirstOrDefault(s => s.Supports(version.MimeType));
            string? still;
            try
            {
                still = Images.ContainsKey(version.MimeType) ? input : source is null ? null : await source.StillAsync(input, work, cancellationToken);
            }
            catch (ToolFailedException exception)
            {
                await FailAsync(pending.Where(d => d.Kind != DerivationKinds.Media), exception.Message, cancellationToken);
                still = null;
            }

            await RenderStillsAsync(version, pending.Where(d => d.Kind != DerivationKinds.Media && d.State == DerivationStates.Pending).ToList(), still, source?.TrimStill == true, cancellationToken);
            if (pending.FirstOrDefault(d => d.Kind == DerivationKinds.Media) is { } media)
            {
                await RenderMediaAsync(version, media, source, input, work, cancellationToken);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // a tool may still hold a file for a moment; the rendition sweep removes the folder later
            }
        }
    }

    private async Task RenderStillsAsync(AssetVersion version, List<Derivation> derivations, string? still, bool trim, CancellationToken cancellationToken)
    {
        if (derivations.Count == 0)
        {
            return;
        }

        if (still is null)
        {
            derivations.ForEach(d => d.State = DerivationStates.NotSupported);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        MagickImage image;
        try
        {
            // the format comes from the checked file type for originals, renderer outputs are PNG
            // originals are read with the coder of their checked type; everything the sources produce is PNG
            image = new MagickImage(still, new MagickReadSettings
            {
                Format = Images.TryGetValue(version.MimeType, out var format) ? format : MagickFormat.Png,
                FrameIndex = 0,
                FrameCount = 1,
            });
        }
        catch (MagickException exception)
        {
            await FailAsync(derivations, exception.Message, cancellationToken);
            return;
        }

        using (image)
        {
            if (trim)
            {
                image.Trim();
                image.ResetPage();
            }

            image.AutoOrient();
            if (image.GetColorProfile() is not null)
            {
                image.TransformColorSpace(ColorProfiles.SRGB);
            }
            else if (image.ColorSpace != ColorSpace.sRGB)
            {
                image.ColorSpace = ColorSpace.sRGB;
            }

            foreach (var derivation in derivations)
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
                    await FailAsync([derivation], exception.Message, cancellationToken);
                    continue;
                }

                await StoreAsync(version, derivation, new MemoryStream(bytes), bytes.Length, "image/webp", (int)width, (int)height, cancellationToken);
            }
        }
    }

    private async Task RenderMediaAsync(AssetVersion version, Derivation media, IRenditionSource? source, string input, string work, CancellationToken cancellationToken)
    {
        (string Path, string MimeType)? copy;
        try
        {
            copy = source is null ? null : await source.MediaAsync(input, work, cancellationToken);
        }
        catch (ToolFailedException exception)
        {
            await FailAsync([media], exception.Message, cancellationToken);
            return;
        }

        if (copy is not { } output)
        {
            media.State = DerivationStates.NotSupported;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var file = File.OpenRead(output.Path);
        await StoreAsync(version, media, file, file.Length, output.MimeType, null, null, cancellationToken);
    }

    private async Task StoreAsync(AssetVersion version, Derivation derivation, Stream content, long size, string mimeType, int? width, int? height, CancellationToken cancellationToken)
    {
        var key = BlobKeys.New(version.TenantId, clock);
        await storage.PutAsync(key, content, mimeType, cancellationToken);
        try
        {
            if (derivation.BlobKey is { } previous)
            {
                // a job that ran twice must not leave the first file behind
                db.Add(new OrphanBlob { Key = previous });
            }

            derivation.BlobKey = key;
            derivation.MimeType = mimeType;
            derivation.Size = size;
            derivation.Width = width;
            derivation.Height = height;
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

    private async Task FailAsync(IEnumerable<Derivation> derivations, string error, CancellationToken cancellationToken)
    {
        logger.LogWarning("A rendition could not be created: {Error}", error);
        foreach (var derivation in derivations)
        {
            derivation.State = DerivationStates.Failed;
            derivation.Error = error.Length > 1000 ? error[..1000] : error;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
