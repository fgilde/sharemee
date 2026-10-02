using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shareme.Assets;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Graph;

namespace Shareme.Processing;

/// <summary>Renders a rendition type from a part of the picture; the crop stays when the asset is rendered again.</summary>
public sealed record CropDerivation(Guid Id, string Kind, CropArea Area) : ICommand<Result<DerivationDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Update;
}

/// <summary>Creates a cropped copy at full resolution; needs the download right on the source and the create right in its folder.</summary>
public sealed record CreateCropVariant(Guid Id, CropVariantRequest Request) : ICommand<Result<CropVariantDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => AssetPermissions.Download;
}

public sealed record CropVariantJobArgs(Guid AssetId, Guid SourceVersionId, double Left, double Top, double Width, double Height);

internal static class Crops
{
    public static string Format(CropArea area) =>
        string.Join(',', new[] { area.Left, area.Top, area.Width, area.Height }.Select(v => v.ToString("0.######", CultureInfo.InvariantCulture)));

    public static CropArea? Parse(string? value) =>
        value?.Split(',') is [var left, var top, var width, var height]
        && double.TryParse(left, CultureInfo.InvariantCulture, out var l) && double.TryParse(top, CultureInfo.InvariantCulture, out var t)
        && double.TryParse(width, CultureInfo.InvariantCulture, out var w) && double.TryParse(height, CultureInfo.InvariantCulture, out var h)
            ? new CropArea(l, t, w, h)
            : null;

    /// <summary>Cuts the area out of an upright image; at least one pixel survives.</summary>
    public static void Apply(IMagickImage<byte> image, CropArea area)
    {
        var x = (int)Math.Min(image.Width - 1, Math.Round(area.Left * image.Width));
        var y = (int)Math.Min(image.Height - 1, Math.Round(area.Top * image.Height));
        var width = (uint)Math.Clamp(Math.Round(area.Width * image.Width), 1, image.Width - x);
        var height = (uint)Math.Clamp(Math.Round(area.Height * image.Height), 1, image.Height - y);
        image.Crop(new MagickGeometry(x, y, width, height));
        image.ResetPage();
    }

    public static readonly Error InvalidArea = Error.Validation("Area", "Choose a part of the picture (fractions from 0 to 1).");
}

internal sealed class CropHandlers(CoworkeeDbContext db, NodeAccess access, IBackgroundJobs jobs, TimeProvider clock)
    : IHandler<CropDerivation, Result<DerivationDto>>, IHandler<CreateCropVariant, Result<CropVariantDto>>
{
    public async Task<Result<DerivationDto>> HandleAsync(CropDerivation command, CancellationToken cancellationToken)
    {
        if (!command.Area.IsValid)
        {
            return Crops.InvalidArea;
        }

        if (await db.Set<Asset>().SingleOrDefaultAsync(a => a.Id == command.Id, cancellationToken) is not { CurrentVersionId: { } versionId } asset)
        {
            return Error.NotFound("assets.not_found", "The asset has no content yet.");
        }

        if (await db.Set<DerivationType>().AsNoTracking().SingleOrDefaultAsync(t => t.Key == command.Kind, cancellationToken) is not { } type || !type.AppliesTo(asset.AssetTypeKey))
        {
            return Error.NotFound("renditions.not_found", "The rendition type does not exist.");
        }

        if (await db.Set<Derivation>().AnyAsync(d => d.VersionId == versionId && d.State == DerivationStates.Pending, cancellationToken))
        {
            return Error.Conflict("assets.rendering", "Renditions of this file are being created; try again in a moment.");
        }

        var derivation = await db.Set<Derivation>().SingleOrDefaultAsync(d => d.VersionId == versionId && d.Kind == type.Key, cancellationToken);
        if (derivation is null)
        {
            derivation = new Derivation { VersionId = versionId, AssetId = asset.Id, Kind = type.Key };
            db.Add(derivation);
        }

        // the old file stays on the row, so the job queues it for deletion when the crop is stored
        derivation.Crop = Crops.Format(command.Area);
        derivation.IsManual = false;
        derivation.State = DerivationStates.Pending;
        derivation.Error = null;
        derivation.PlannedAt = clock.GetUtcNow();
        asset.Publish(new DerivationsRequested(asset.Id, versionId));
        return RenditionHandlers.Map(derivation);
    }

    public async Task<Result<CropVariantDto>> HandleAsync(CreateCropVariant command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (!request.Area.IsValid)
        {
            return Crops.InvalidArea;
        }

        if (await db.Set<Asset>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == command.Id, cancellationToken) is not { CurrentVersionId: { } versionId, ParentId: { } parentId } source)
        {
            return Error.NotFound("assets.not_found", "The asset has no content yet.");
        }

        // the variant gets the folder's permissions; a source shut off from them would leak through it
        if (source.InterruptInheritance)
        {
            return Error.Validation("Id", "This asset has its own permissions; a variant next to it would be visible to the whole folder.");
        }

        // the variant edge is a change of the source
        if (!await access.CanAsync(NodePermissions.Update, source.Id, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "You may not change this asset.");
        }

        var mimeType = await db.Set<AssetVersion>().Where(v => v.Id == versionId).Select(v => v.MimeType).SingleAsync(cancellationToken);
        if (!ImageFormats.ByMimeType.ContainsKey(mimeType)
            && !await db.Set<Derivation>().AnyAsync(d => d.VersionId == versionId && d.Kind == DerivationKinds.Still && d.State == DerivationStates.Ready, cancellationToken))
        {
            return Error.Validation("Area", "Only pictures, and files with a rendered page or frame, can be cropped.");
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? Path.GetFileNameWithoutExtension(source.Name) + " (crop)" : Path.GetFileNameWithoutExtension(request.Name.Trim());
        if (name.Length > 200 || name.Any(char.IsControl) || name.IndexOfAny(['/', '\\']) >= 0)
        {
            return Error.Validation("Name", "A name of up to 200 characters without slashes or control characters.");
        }

        if (await db.Set<Node>().SingleOrDefaultAsync(f => f.Id == parentId && (f is Folder || f is Workspace), cancellationToken) is not { } folder)
        {
            return Error.NotFound("nodes.not_found", "The folder of the asset does not exist.");
        }

        if (!await access.CanAsync(NodePermissions.Create, folder.Id, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "You may not add files to this folder.");
        }

        var variant = new Asset
        {
            Name = name + "." + VariantExtension(mimeType),
            NodeTypeId = NodeType.AssetTypeId,
            Status = AssetStatuses.Importing,
            AssetTypeKey = AssetTypeKeys.Image,
            Metadata = JsonSerializer.Deserialize<Dictionary<string, List<MetadataValue>>>(JsonSerializer.Serialize(source.Metadata)) ?? [],
        };
        variant.PlaceUnder(folder);
        variant.MarkChanged("created");
        db.Add(variant);
        db.Add(new Edge { EdgeTypeId = EdgeType.VariantId, FromNodeId = source.Id, ToNodeId = variant.Id });
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<CropVariantJob, CropVariantJobArgs>(
            new CropVariantJobArgs(variant.Id, versionId, request.Area.Left, request.Area.Top, request.Area.Width, request.Area.Height), ShareMeProcessingModule.Queue);
        return new CropVariantDto(variant.Id);
    }

    /// <summary>JPEG and WebP stay as they are, everything else becomes PNG.</summary>
    internal static string VariantExtension(string mimeType) => mimeType switch { "image/jpeg" => "jpg", "image/webp" => "webp", _ => "png" };
}

/// <summary>Cuts the variant out of the source (its original, or the kept still of other media) and stores it as the variant's first version.</summary>
internal sealed class CropVariantJob(CoworkeeDbContext db, IBlobStorage storage, IEnumerable<IRenditionSource> sources, TimeProvider clock, ILogger<CropVariantJob> logger)
    : IBackgroundJob<CropVariantJobArgs>
{
    public async Task ExecuteAsync(CropVariantJobArgs args, CancellationToken cancellationToken)
    {
        if (await db.Set<Asset>().IgnoreQueryFilters().SingleOrDefaultAsync(a => a.Id == args.AssetId && a.CurrentVersionId == null && !a.IsDeleted, cancellationToken) is not { } variant
            || await db.Set<AssetVersion>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(v => v.Id == args.SourceVersionId, cancellationToken) is not { } source)
        {
            return;
        }

        var isImage = ImageFormats.ByMimeType.TryGetValue(source.MimeType, out var format);
        var key = isImage ? source.BlobKey
            : await db.Set<Derivation>().IgnoreQueryFilters().Where(d => d.VersionId == source.Id && d.Kind == DerivationKinds.Still && d.State == DerivationStates.Ready).Select(d => d.BlobKey).SingleOrDefaultAsync(cancellationToken);
        if (key is null)
        {
            await FailAsync(variant, "The source has no picture to crop.", cancellationToken);
            return;
        }

        byte[] bytes;
        string mimeType, extension;
        // a picture that went away (the still is made again on rerender) fails the variant instead of leaving it importing
        if (await storage.OpenReadAsync(key, cancellationToken) is not { } content)
        {
            await FailAsync(variant, "The picture to crop is no longer stored; try again.", cancellationToken);
            return;
        }

        await using (content)
        {
            try
            {
                using var image = new MagickImage(content, new MagickReadSettings { Format = isImage ? format : MagickFormat.Png, FrameIndex = 0, FrameCount = 1 });

                // the same upright picture the preview shows, so the area matches what was chosen
                if (!isImage && sources.FirstOrDefault(s => s.Supports(source.MimeType)) is { TrimStill: true })
                {
                    image.Trim();
                    image.ResetPage();
                }

                image.AutoOrient();
                Crops.Apply(image, new CropArea(args.Left, args.Top, args.Width, args.Height));
                (var output, mimeType, extension) = isImage ? format switch
                {
                    MagickFormat.Jpeg => (MagickFormat.Jpeg, "image/jpeg", "jpg"),
                    MagickFormat.WebP => (MagickFormat.WebP, "image/webp", "webp"),
                    _ => (MagickFormat.Png, "image/png", "png"),
                } : (MagickFormat.Png, "image/png", "png");
                if (output == MagickFormat.Png && image.ColorSpace != ColorSpace.sRGB)
                {
                    // PNG holds no CMYK; convert with the profile so colours stay
                    if (image.GetColorProfile() is not null)
                    {
                        image.TransformColorSpace(ColorProfiles.SRGB);
                    }
                    else
                    {
                        image.ColorSpace = ColorSpace.sRGB;
                    }
                }

                image.Quality = 92;
                bytes = image.ToByteArray(output);
            }
            catch (MagickException exception)
            {
                await FailAsync(variant, exception.Message, cancellationToken);
                return;
            }
        }

        var blobKey = BlobKeys.New(variant.TenantId, clock);
        await storage.PutAsync(blobKey, new MemoryStream(bytes), mimeType, cancellationToken);
        try
        {
            var version = new AssetVersion
            {
                AssetId = variant.Id,
                Number = 1,
                FileName = variant.Name,
                Size = bytes.Length,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                MimeType = mimeType,
                Extension = extension,
                BlobKey = blobKey,
                TenantId = variant.TenantId,
            };
            db.Add(version);
            variant.CurrentVersionId = version.Id;
            variant.Status = AssetStatuses.Ready;
            variant.MarkChanged("uploaded");
            variant.Publish(new AssetVersionCreated(variant.Id, version.Id));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(blobKey, CancellationToken.None);
            throw;
        }
    }

    private async Task FailAsync(Asset variant, string error, CancellationToken cancellationToken)
    {
        logger.LogWarning("Crop variant {AssetId} could not be created: {Error}", variant.Id, error);
        variant.Status = AssetStatuses.Failed;
        variant.MarkChanged("failed");
        await db.SaveChangesAsync(cancellationToken);
    }
}
