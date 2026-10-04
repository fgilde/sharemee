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
using Shareme.Contracts.Metadata;
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
        services.AddScoped<CropVariantJob>();
        services.AddRecurringJob<RenditionSweepJob>(RenditionSweepJob.Id, "*/10 * * * *");
        services.AddSingleton<ProcessingTools>();
        services.AddSingleton<PdfSource>();
        services.AddSingleton<IRenditionSource, VideoSource>();
        services.AddSingleton<IRenditionSource, AudioSource>();
        services.AddSingleton<IRenditionSource>(provider => provider.GetRequiredService<PdfSource>());
        services.AddSingleton<IRenditionSource, OfficeSource>();
        services.AddSingleton<IRenditionSource, ModelSource>();
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

    public void ConfigureApplication(WebApplication app)
    {
        var assets = app.MapGroup("/api/v1/assets").WithTags("Assets").RequireAuthorization();
        assets.MapPost("/{id:guid}/derivations/rerender", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new RerenderAsset(id), ct).ToHttpResult());
        assets.MapPut("/{id:guid}/derivations/{kind}/crop", (Guid id, string kind, CropArea body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CropDerivation(id, kind, body), ct).ToHttpResult());
        app.MapPost("/api/v1/downloads/contact-sheet", async (ContactSheetRequest body, HttpResponse response, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync(new CreateContactSheet(body), ct);
            if (!result.IsSuccess)
            {
                return result.Error!.ToProblem();
            }

            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.CacheControl = "private, no-store";
            return Results.File(result.Value.Content, result.Value.MimeType, result.Value.FileName);
        }).WithTags("Downloads").RequireAuthorization();
        assets.MapPost("/{id:guid}/variants", (Guid id, CropVariantRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateCropVariant(id, body), ct).ToHttpResult());
    }
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

        // hand made renditions stay; the still is dropped and made again, a renderer update may change it
        foreach (var still in existing.Where(d => d.Kind == DerivationKinds.Still))
        {
            if (still.BlobKey is { } key)
            {
                db.Add(new OrphanBlob { Key = key });
            }

            db.Remove(still);
        }

        foreach (var derivation in existing.Where(d => !d.IsManual && d.Kind != DerivationKinds.Still))
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

        var planned = await RenditionPlan.KindsAsync(db, asset.TenantId, mimeType, asset.AssetTypeKey, cancellationToken);
        db.AddRange(planned.Except(existing.Select(d => d.Kind))
            .Select(kind => new Derivation { VersionId = versionId, AssetId = asset.Id, Kind = kind, PlannedAt = now }));
        asset.RenditionTag = null;
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<RenderDerivationsJob, Guid>(versionId, ShareMeProcessingModule.Queue);
        return Result.Success();
    }
}

public static class RenditionPlan
{
    /// <summary>The system renditions of the file type and the organisation's default types for the asset type.</summary>
    public static async Task<IReadOnlyList<string>> KindsAsync(CoworkeeDbContext db, Guid tenantId, string mimeType, string assetTypeKey, CancellationToken cancellationToken)
    {
        var defaults = await db.Set<DerivationType>().IgnoreQueryFilters().AsNoTracking().Where(t => t.TenantId == tenantId && t.IsDefault).ToListAsync(cancellationToken);
        return [.. DerivationKinds.For(mimeType), .. defaults.Where(t => t.AppliesTo(assetTypeKey)).Select(t => t.Key)];
    }
}

/// <summary>Plans the renditions of every new version and hands them to the processing queue.</summary>
internal sealed class PlanDerivations(CoworkeeDbContext db, IBackgroundJobs jobs, TimeProvider clock) : IDomainEventHandler<AssetVersionCreated>, IDomainEventHandler<DerivationsRequested>
{
    public async Task HandleAsync(AssetVersionCreated domainEvent, CancellationToken cancellationToken)
    {
        if (await db.Set<AssetVersion>().Where(v => v.Id == domainEvent.VersionId).Select(v => new { v.MimeType, v.TenantId }).SingleOrDefaultAsync(cancellationToken) is not { } version
            || await db.Set<Asset>().Where(a => a.Id == domainEvent.AssetId).Select(a => a.AssetTypeKey).SingleOrDefaultAsync(cancellationToken) is not { } assetType)
        {
            return;
        }

        var existing = await db.Set<Derivation>().Where(d => d.VersionId == domainEvent.VersionId).Select(d => new { d.Kind, d.State }).ToListAsync(cancellationToken);
        var planned = await RenditionPlan.KindsAsync(db, version.TenantId, version.MimeType, assetType, cancellationToken);
        var missing = planned.Except(existing.Select(d => d.Kind)).ToList();
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

    public Task HandleAsync(DerivationsRequested domainEvent, CancellationToken cancellationToken)
    {
        jobs.Enqueue<RenderDerivationsJob, Guid>(domainEvent.VersionId, ShareMeProcessingModule.Queue);
        return Task.CompletedTask;
    }
}

/// <summary>Renditions with a watermark that depends on a property are made again when the asset's metadata changes; until then they are not served.</summary>
internal sealed class RerenderTriggeredWatermarks(CoworkeeDbContext db, IBackgroundJobs jobs, TimeProvider clock) : IDomainEventHandler<NodeChanged>
{
    public const string MetadataChanged = "metadata changed";

    public async Task HandleAsync(NodeChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.Kind != MetadataChanged
            || await db.Set<Asset>().Where(a => a.Id == domainEvent.NodeId).Select(a => a.CurrentVersionId).SingleOrDefaultAsync(cancellationToken) is not { } versionId)
        {
            return;
        }

        var triggered = db.Set<DerivationType>().Where(t => t.WatermarkId != null && db.Set<WatermarkDefinition>().Any(w => w.Id == t.WatermarkId && w.TriggerProperty != null)).Select(t => t.Key);
        var derivations = await db.Set<Derivation>().Where(d => d.VersionId == versionId && !d.IsManual && d.State == DerivationStates.Ready && triggered.Contains(d.Kind))
            .ToListAsync(cancellationToken);
        if (derivations.Count == 0)
        {
            return;
        }

        var busy = await db.Set<Derivation>().AnyAsync(d => d.VersionId == versionId && d.State == DerivationStates.Pending, cancellationToken);
        foreach (var derivation in derivations)
        {
            derivation.State = DerivationStates.Pending;
            derivation.PlannedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);

        // ponytail: a job already running for the version leaves these to the rendition sweep (two hours); a row claim would let a second job start at once
        if (!busy)
        {
            jobs.Enqueue<RenderDerivationsJob, Guid>(versionId, ShareMeProcessingModule.Queue);
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
        await db.Set<Asset>().IgnoreQueryFilters()
            .Where(a => a.Status == AssetStatuses.Importing && a.CurrentVersionId == null && a.CreatedAt < now - Lost
                && db.Set<Edge>().IgnoreQueryFilters().Any(e => e.EdgeTypeId == EdgeType.VariantId && e.ToNodeId == a.Id))
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, AssetStatuses.Failed), cancellationToken);
        foreach (var versionId in await pending.Where(d => d.PlannedAt < now - Lost).Select(d => d.VersionId).Distinct().Take(500).ToListAsync(cancellationToken))
        {
            jobs.Enqueue<RenderDerivationsJob, Guid>(versionId, ShareMeProcessingModule.Queue);
        }
    }
}

/// <summary>
/// Renders the pending derivations of one version: images directly, other media through a source that turns them into a still image
/// (and for video and audio a playable copy). The still of other media is kept, so later renditions skip the heavy tool.
/// Runs with or without a tenant (sweeps), so it reads by id across tenants.
/// </summary>
internal sealed class RenderDerivationsJob(
    CoworkeeDbContext db, IBlobStorage storage, IEnumerable<IRenditionSource> sources, TechnicalMetadataReader metadata,
    Microsoft.Extensions.Configuration.IConfiguration configuration, TimeProvider clock, ILogger<RenderDerivationsJob> logger)
    : IBackgroundJob<Guid>
{
    private static readonly Dictionary<string, (int Size, int Quality)> SystemSizes = new() { [DerivationKinds.Thumbnail] = (320, 80), [DerivationKinds.Preview] = (1600, 80) };

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
        var derivations = await db.Set<Derivation>().IgnoreQueryFilters().Where(d => d.VersionId == versionId).ToListAsync(cancellationToken);
        var pending = derivations.Where(d => d.State == DerivationStates.Pending && d.Kind != DerivationKinds.Still).ToList();
        if (version is null || pending.Count == 0)
        {
            return;
        }

        var media = pending.FirstOrDefault(d => d.Kind == DerivationKinds.Media);
        var animated = pending.FirstOrDefault(d => d.Kind == DerivationKinds.Animated);
        var stills = pending.Where(d => d.Kind is not (DerivationKinds.Media or DerivationKinds.Animated)).ToList();
        var isImage = Images.ContainsKey(version.MimeType);
        var kept = isImage ? null : derivations.FirstOrDefault(d => d is { Kind: DerivationKinds.Still, State: DerivationStates.Ready, BlobKey: not null });
        var work = Directory.CreateDirectory(Path.Combine(WorkRoot(configuration), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string? input = null;
            async Task<string> OriginalAsync()
            {
                if (input is null)
                {
                    // a missing original is a storage or configuration problem: throw so the job is retried instead of failing the rendition
                    input = Path.Combine(work, InputName(version));
                    await using var original = await storage.OpenReadAsync(version.BlobKey, cancellationToken)
                        ?? throw new FileNotFoundException($"The original of version {versionId} is not in storage.");
                    await using var file = File.Create(input);
                    await original.CopyToAsync(file, cancellationToken);
                }

                return input;
            }

            if (version.TechnicalMetadata.Count == 0)
            {
                version.TechnicalMetadata = await metadata.ReadAsync(await OriginalAsync(), version.MimeType, work, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            var source = sources.FirstOrDefault(s => s.Supports(version.MimeType));
            string? still = null;
            if (stills.Count > 0)
            {
                if (isImage)
                {
                    still = await OriginalAsync();
                }
                else if (kept is not null && await storage.OpenReadAsync(kept.BlobKey!, cancellationToken) is { } keptContent)
                {
                    still = Path.Combine(work, "still.png");
                    await using (keptContent)
                    await using (var file = File.Create(still))
                    {
                        await keptContent.CopyToAsync(file, cancellationToken);
                    }
                }
                else if (source is not null)
                {
                    try
                    {
                        still = await source.StillAsync(await OriginalAsync(), work, cancellationToken);
                    }
                    catch (ToolFailedException exception)
                    {
                        await FailAsync(stills, exception.Message, cancellationToken);
                        stills = [];
                    }

                    if (still is not null)
                    {
                        await KeepStillAsync(version, derivations, still, cancellationToken);
                    }
                }
            }

            await RenderStillsAsync(version, stills, still, source?.TrimStill == true, cancellationToken);
            if (media is not null)
            {
                var original = await OriginalAsync();
                await RenderCopyAsync(version, media, source is null ? null : () => source.MediaAsync(original, work, cancellationToken), cancellationToken);
            }

            if (animated is not null)
            {
                var original = await OriginalAsync();
                await RenderCopyAsync(version, animated, source is null ? null : async () => await source.AnimatedAsync(original, work, cancellationToken) is { } gif ? (gif, "image/gif") : null, cancellationToken);
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

    private async Task KeepStillAsync(AssetVersion version, List<Derivation> derivations, string still, CancellationToken cancellationToken)
    {
        if (derivations.FirstOrDefault(d => d.Kind == DerivationKinds.Still) is not { } row)
        {
            row = new Derivation { VersionId = version.Id, AssetId = version.AssetId, Kind = DerivationKinds.Still, PlannedAt = clock.GetUtcNow(), TenantId = version.TenantId };
            db.Add(row);
            derivations.Add(row);
        }

        await using var file = File.OpenRead(still);
        await StoreAsync(version, row, file, file.Length, "image/png", null, null, cancellationToken);
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

        var keys = derivations.Select(d => d.Kind).ToList();
        var types = await db.Set<DerivationType>().IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == version.TenantId && keys.Contains(t.Key)).ToDictionaryAsync(t => t.Key, cancellationToken);
        var watermarks = new Watermarks(db, storage, version);
        MagickImage image;
        try
        {
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
        using (watermarks)
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
                (int Width, int Height, string Format, int Quality, Guid? WatermarkId)? spec = SystemSizes.TryGetValue(derivation.Kind, out var system)
                    ? (system.Size, system.Size, DerivationFormats.WebP, system.Quality, null)
                    : types.TryGetValue(derivation.Kind, out var type) ? (type.Width, type.Height, type.Format, type.Quality, type.WatermarkId) : null;
                if (spec is not { } s)
                {
                    await FailAsync([derivation], "The rendition type no longer exists.", cancellationToken);
                    continue;
                }

                byte[] bytes;
                uint width, height;
                try
                {
                    using var rendition = image.Clone();
                    if (Crops.Parse(derivation.Crop) is { } crop)
                    {
                        Crops.Apply(rendition, crop);
                    }

                    rendition.Resize(new MagickGeometry((uint)s.Width, (uint)s.Height) { Greater = true });
                    rendition.Strip();
                    if (s.WatermarkId is { } watermarkId && await watermarks.ApplyAsync(watermarkId, rendition, cancellationToken) is { } watermarkError)
                    {
                        // a rendition that should carry a watermark is never stored without it
                        await FailAsync([derivation], watermarkError, cancellationToken);
                        continue;
                    }

                    if (s.Format == DerivationFormats.Jpeg)
                    {
                        rendition.BackgroundColor = MagickColors.White;
                        rendition.Alpha(AlphaOption.Remove);
                    }

                    rendition.Quality = (uint)s.Quality;
                    bytes = rendition.ToByteArray(s.Format switch { DerivationFormats.Jpeg => MagickFormat.Jpeg, DerivationFormats.Png => MagickFormat.Png, _ => MagickFormat.WebP });
                    (width, height) = (rendition.Width, rendition.Height);
                }
                catch (MagickException exception)
                {
                    await FailAsync([derivation], exception.Message, cancellationToken);
                    continue;
                }

                await StoreAsync(version, derivation, new MemoryStream(bytes), bytes.Length, DerivationFormats.MimeType(s.Format), (int)width, (int)height, cancellationToken);
            }
        }
    }

    /// <summary>Stores a file a tool made (playable copy, animation) as the derivation; no tool or no output means not supported.</summary>
    private async Task RenderCopyAsync(AssetVersion version, Derivation media, Func<Task<(string Path, string MimeType)?>>? make, CancellationToken cancellationToken)
    {
        (string Path, string MimeType)? copy;
        try
        {
            copy = make is null ? null : await make();
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

/// <summary>Watermarks of one render run, each loaded once; a watermark with a trigger only applies when the asset's property holds the value.</summary>
internal sealed class Watermarks(CoworkeeDbContext db, IBlobStorage storage, AssetVersion version) : IDisposable
{
    private readonly Dictionary<Guid, (WatermarkDefinition Definition, MagickImage Image)?> loaded = [];
    private Dictionary<string, List<MetadataValue>>? metadata;

    public static bool Triggered(WatermarkDefinition watermark, IReadOnlyDictionary<string, List<MetadataValue>> metadata) =>
        watermark.TriggerProperty is not { } property
        || (metadata.TryGetValue(property, out var values) && values.Any(v => string.Equals(v.List ?? Text(v.V), watermark.TriggerValue ?? "true", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Lays the watermark over the image when it applies; returns why it could not, so the rendition fails instead of going out unprotected.</summary>
    public async Task<string?> ApplyAsync(Guid id, IMagickImage<byte> target, CancellationToken cancellationToken)
    {
        if (!loaded.TryGetValue(id, out var watermark))
        {
            watermark = await LoadAsync(id, cancellationToken);
            loaded[id] = watermark;
        }

        if (watermark is not { } w)
        {
            return "The watermark has no readable image.";
        }

        if (w.Definition.TriggerProperty is not null)
        {
            metadata ??= await db.Set<Asset>().IgnoreQueryFilters().AsNoTracking().Where(a => a.Id == version.AssetId).Select(a => a.Metadata).SingleOrDefaultAsync(cancellationToken) ?? [];
            if (!Triggered(w.Definition, metadata))
            {
                return null;
            }
        }

        using var mark = w.Image.Clone();
        var edge = Math.Max(1u, target.Width * (uint)w.Definition.ScalePercent / 100);
        mark.Resize(new MagickGeometry(edge, edge));
        mark.Alpha(AlphaOption.Set);
        mark.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, w.Definition.Opacity / 100.0);
        target.Composite(mark, Enum.TryParse<Gravity>(w.Definition.Position, ignoreCase: true, out var gravity) ? gravity : Gravity.Southeast, CompositeOperator.Over);
        return null;
    }

    public void Dispose()
    {
        foreach (var watermark in loaded.Values)
        {
            watermark?.Image.Dispose();
        }
    }

    private async Task<(WatermarkDefinition, MagickImage)?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await db.Set<WatermarkDefinition>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(w => w.Id == id && w.TenantId == version.TenantId, cancellationToken) is not { BlobKey: { } key } definition
            || await storage.OpenReadAsync(key, cancellationToken) is not { } content)
        {
            return null;
        }

        await using (content)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            try
            {
                return (definition, new MagickImage(buffer.ToArray(), new MagickReadSettings { Format = MagickFormat.Png }));
            }
            catch (MagickException)
            {
                return null;
            }
        }
    }

    private static string? Text(System.Text.Json.JsonElement? value) => value switch
    {
        { ValueKind: System.Text.Json.JsonValueKind.String } text => text.GetString(),
        { } other => other.GetRawText(),
        null => null,
    };
}
