using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Graph;

namespace Shareme.Assets;

public sealed class DerivationType : AuditedEntity, IMultiTenant
{
    public required string Key { get; set; }

    public required string Name { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public string Format { get; set; } = DerivationFormats.WebP;

    public int Quality { get; set; } = 85;

    public bool IsDefault { get; set; }

    public bool IsOnDemand { get; set; } = true;

    public bool AllowManualUpload { get; set; }

    public Guid? WatermarkId { get; set; }

    public List<string> AssetTypeKeys { get; set; } = [];

    public Guid TenantId { get; set; }

    public bool AppliesTo(string assetTypeKey) => AssetTypeKeys.Count == 0 || AssetTypeKeys.Contains(assetTypeKey);
}

public sealed class WatermarkDefinition : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public string? BlobKey { get; set; }

    public string Position { get; set; } = "southeast";

    public int Opacity { get; set; } = 50;

    public int ScalePercent { get; set; } = 25;

    public string? TriggerProperty { get; set; }

    public string? TriggerValue { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>Renditions of a version were planned or reset and wait for the processing host.</summary>
public sealed record DerivationsRequested(Guid AssetId, Guid VersionId) : IDomainEvent;

internal sealed class RenditionModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DerivationType>(type =>
        {
            type.ToTable("DerivationTypes", "sm");
            type.Property(t => t.Key).HasMaxLength(50);
            type.Property(t => t.Name).HasMaxLength(200);
            type.Property(t => t.Format).HasMaxLength(10);
            type.Property(t => t.AssetTypeKeys).HasColumnType("jsonb");
            type.HasIndex(t => new { t.TenantId, t.Key }).IsUnique();
            type.HasOne<WatermarkDefinition>().WithMany().HasForeignKey(t => t.WatermarkId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WatermarkDefinition>(watermark =>
        {
            watermark.ToTable("Watermarks", "sm");
            watermark.Property(w => w.Name).HasMaxLength(200);
            watermark.Property(w => w.BlobKey).HasMaxLength(512);
            watermark.Property(w => w.Position).HasMaxLength(20);
            watermark.Property(w => w.TriggerProperty).HasMaxLength(100);
            watermark.Property(w => w.TriggerValue).HasMaxLength(200);
        });
    }
}

[RequiresPermission(NodePermissions.Admin)]
public sealed record SaveDerivationType(Guid? Id, DerivationTypeRequest Request) : ICommand<Result<DerivationTypeDto>>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record DeleteDerivationType(Guid Id) : ICommand<Result>;

public sealed record GetDerivationTypes : IQuery<Result<IReadOnlyList<DerivationTypeDto>>>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record SaveWatermark(Guid? Id, WatermarkRequest Request) : ICommand<Result<WatermarkDto>>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record SetWatermarkImage(Guid Id, Stream Content) : ICommand<Result>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record DeleteWatermark(Guid Id) : ICommand<Result>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record GetWatermarks : IQuery<Result<IReadOnlyList<WatermarkDto>>>;

/// <summary>Asks for an on-demand rendition of the current version (or a fresh try of a failed one).</summary>
public sealed record RequestDerivation(Guid Id, string Kind) : ICommand<Result<DerivationDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => AssetPermissions.Download;
}

/// <summary>Stores a hand made rendition (PNG, JPEG or WebP) for a type that allows it; it survives re-rendering.</summary>
public sealed record UploadDerivation(Guid Id, string Kind, Stream Content) : ICommand<Result<DerivationDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Update;
}

internal static class RenditionLimits
{
    public const int MaxEdge = 8000;
    public const long MaxUpload = 100L * 1024 * 1024;
    public const long MaxWatermark = 5L * 1024 * 1024;

    public static readonly Error NotFound = Error.NotFound("renditions.not_found", "The rendition type does not exist.");

    /// <summary>Reads at most <paramref name="limit"/> bytes; null when the content is larger.</summary>
    public static async Task<byte[]?> ReadAsync(Stream content, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>The image type of uploaded bytes by their signature; only PNG, JPEG and WebP are taken.</summary>
    public static string? Sniff(byte[] bytes) => bytes switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };
}

internal sealed class RenditionHandlers(CoworkeeDbContext db, IBlobStorage storage, TimeProvider clock)
    : IHandler<SaveDerivationType, Result<DerivationTypeDto>>,
      IHandler<DeleteDerivationType, Result>,
      IHandler<GetDerivationTypes, Result<IReadOnlyList<DerivationTypeDto>>>,
      IHandler<SaveWatermark, Result<WatermarkDto>>,
      IHandler<SetWatermarkImage, Result>,
      IHandler<DeleteWatermark, Result>,
      IHandler<GetWatermarks, Result<IReadOnlyList<WatermarkDto>>>,
      IHandler<RequestDerivation, Result<DerivationDto>>,
      IHandler<UploadDerivation, Result<DerivationDto>>
{
    public async Task<Result<DerivationTypeDto>> HandleAsync(SaveDerivationType command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.Key) || request.Key.Length > 50 || !request.Key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_'))
        {
            errors["Key"] = ["Use lower case letters, digits, '-' or '_' (up to 50 characters)."];
        }
        else if (DerivationKinds.System.Contains(request.Key))
        {
            errors["Key"] = [$"'{request.Key}' is reserved."];
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            errors["Name"] = ["A name of up to 200 characters is required."];
        }

        if (request.Width is < 16 or > RenditionLimits.MaxEdge || request.Height is < 16 or > RenditionLimits.MaxEdge)
        {
            errors["Width"] = [$"Width and height from 16 to {RenditionLimits.MaxEdge} pixels."];
        }

        if (!DerivationFormats.All.Contains(request.Format))
        {
            errors["Format"] = [$"Use one of: {string.Join(", ", DerivationFormats.All)}."];
        }

        if (request.Quality is < 10 or > 100)
        {
            errors["Quality"] = ["Quality from 10 to 100."];
        }

        if (request.WatermarkId is { } watermarkId && !await db.Set<WatermarkDefinition>().AnyAsync(w => w.Id == watermarkId, cancellationToken))
        {
            errors["WatermarkId"] = ["The watermark does not exist."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        DerivationType type;
        if (command.Id is { } id)
        {
            if (await db.Set<DerivationType>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken) is not { } existing)
            {
                return RenditionLimits.NotFound;
            }

            if (existing.Key != request.Key)
            {
                return Error.Validation("Key", "The key cannot change once renditions may exist.");
            }

            type = existing;
        }
        else
        {
            if (await db.Set<DerivationType>().AnyAsync(t => t.Key == request.Key, cancellationToken))
            {
                return Error.Conflict("renditions.exists", "A rendition type with this key exists.");
            }

            type = new DerivationType { Key = request.Key, Name = string.Empty };
            db.Add(type);
        }

        type.Name = request.Name.Trim();
        type.Width = request.Width;
        type.Height = request.Height;
        type.Format = request.Format;
        type.Quality = request.Quality;
        type.IsDefault = request.IsDefault;
        type.IsOnDemand = request.IsOnDemand;
        type.AllowManualUpload = request.AllowManualUpload;
        type.WatermarkId = request.WatermarkId;
        type.AssetTypeKeys = request.AssetTypeKeys?.Distinct().ToList() ?? [];
        return Map(type);
    }

    public async Task<Result> HandleAsync(DeleteDerivationType command, CancellationToken cancellationToken)
    {
        if (await db.Set<DerivationType>().SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken) is not { } type)
        {
            return RenditionLimits.NotFound;
        }

        // ponytail: existing renditions of the type stay until their asset is purged or re-rendered
        db.Remove(type);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<DerivationTypeDto>>> HandleAsync(GetDerivationTypes query, CancellationToken cancellationToken)
    {
        IReadOnlyList<DerivationTypeDto> types = (await db.Set<DerivationType>().AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<DerivationTypeDto>>.Success(types);
    }

    public async Task<Result<WatermarkDto>> HandleAsync(SaveWatermark command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Error.Validation("Name", "A name of up to 200 characters is required.");
        }

        if (!WatermarkPositions.All.Contains(request.Position) || request.Opacity is < 1 or > 100 || request.ScalePercent is < 1 or > 100)
        {
            return Error.Validation("Position", $"Use one of {string.Join(", ", WatermarkPositions.All)}, opacity and scale from 1 to 100.");
        }

        WatermarkDefinition watermark;
        if (command.Id is { } id)
        {
            if (await db.Set<WatermarkDefinition>().SingleOrDefaultAsync(w => w.Id == id, cancellationToken) is not { } existing)
            {
                return Error.NotFound("watermarks.not_found", "The watermark does not exist.");
            }

            watermark = existing;
        }
        else
        {
            watermark = new WatermarkDefinition { Name = string.Empty };
            db.Add(watermark);
        }

        watermark.Name = request.Name.Trim();
        watermark.Position = request.Position;
        watermark.Opacity = request.Opacity;
        watermark.ScalePercent = request.ScalePercent;
        watermark.TriggerProperty = string.IsNullOrWhiteSpace(request.TriggerProperty) ? null : request.TriggerProperty.Trim();
        watermark.TriggerValue = string.IsNullOrWhiteSpace(request.TriggerValue) ? null : request.TriggerValue.Trim();
        return Map(watermark);
    }

    public async Task<Result> HandleAsync(SetWatermarkImage command, CancellationToken cancellationToken)
    {
        if (await db.Set<WatermarkDefinition>().SingleOrDefaultAsync(w => w.Id == command.Id, cancellationToken) is not { } watermark)
        {
            return Error.NotFound("watermarks.not_found", "The watermark does not exist.");
        }

        if (await RenditionLimits.ReadAsync(command.Content, RenditionLimits.MaxWatermark, cancellationToken) is not { } bytes || RenditionLimits.Sniff(bytes) is not "image/png")
        {
            return Error.Validation("Content", "Use a PNG of up to 5 MB, ideally with transparency.");
        }

        if (watermark.BlobKey is { } previous)
        {
            db.Add(new OrphanBlob { Key = previous });
        }

        watermark.BlobKey = BlobKeys.New(watermark.TenantId, clock);
        await storage.PutAsync(watermark.BlobKey, new MemoryStream(bytes), "image/png", cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteWatermark command, CancellationToken cancellationToken)
    {
        if (await db.Set<WatermarkDefinition>().SingleOrDefaultAsync(w => w.Id == command.Id, cancellationToken) is not { } watermark)
        {
            return Error.NotFound("watermarks.not_found", "The watermark does not exist.");
        }

        // removing it would let its rendition types render unprotected
        if (await db.Set<DerivationType>().Where(t => t.WatermarkId == watermark.Id).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken) is { } user)
        {
            return Error.Conflict("watermarks.in_use", $"The rendition type '{user}' uses this watermark.");
        }

        if (watermark.BlobKey is { } key)
        {
            db.Add(new OrphanBlob { Key = key });
        }

        db.Remove(watermark);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<WatermarkDto>>> HandleAsync(GetWatermarks query, CancellationToken cancellationToken)
    {
        IReadOnlyList<WatermarkDto> watermarks = (await db.Set<WatermarkDefinition>().AsNoTracking().OrderBy(w => w.Name).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<WatermarkDto>>.Success(watermarks);
    }

    public async Task<Result<DerivationDto>> HandleAsync(RequestDerivation command, CancellationToken cancellationToken)
    {
        if (await db.Set<Asset>().SingleOrDefaultAsync(a => a.Id == command.Id, cancellationToken) is not { CurrentVersionId: { } versionId } asset)
        {
            return Error.NotFound("assets.not_found", "The asset has no content yet.");
        }

        if (await db.Set<DerivationType>().AsNoTracking().SingleOrDefaultAsync(t => t.Key == command.Kind, cancellationToken) is not { } type || !type.AppliesTo(asset.AssetTypeKey))
        {
            return RenditionLimits.NotFound;
        }

        if (!type.IsOnDemand && !type.IsDefault)
        {
            return Error.Validation("Kind", "This rendition is only uploaded by hand.");
        }

        var derivation = await db.Set<Derivation>().SingleOrDefaultAsync(d => d.VersionId == versionId && d.Kind == type.Key, cancellationToken);
        if (derivation is { State: not DerivationStates.Failed })
        {
            return Map(derivation);
        }

        // a running job would not see the new row, and a second job would render its rows twice
        if (await db.Set<Derivation>().AnyAsync(d => d.VersionId == versionId && d.State == DerivationStates.Pending, cancellationToken))
        {
            return Error.Conflict("assets.rendering", "Renditions of this file are being created; try again in a moment.");
        }

        if (derivation is null)
        {
            derivation = new Derivation { VersionId = versionId, AssetId = asset.Id, Kind = type.Key };
            db.Add(derivation);
        }

        derivation.State = DerivationStates.Pending;
        derivation.Error = null;
        derivation.PlannedAt = clock.GetUtcNow();
        asset.Publish(new DerivationsRequested(asset.Id, versionId));
        return Map(derivation);
    }

    public async Task<Result<DerivationDto>> HandleAsync(UploadDerivation command, CancellationToken cancellationToken)
    {
        if (await db.Set<Asset>().SingleOrDefaultAsync(a => a.Id == command.Id, cancellationToken) is not { CurrentVersionId: { } versionId } asset)
        {
            return Error.NotFound("assets.not_found", "The asset has no content yet.");
        }

        if (await db.Set<DerivationType>().AsNoTracking().SingleOrDefaultAsync(t => t.Key == command.Kind, cancellationToken) is not { AllowManualUpload: true } type || !type.AppliesTo(asset.AssetTypeKey))
        {
            return Error.Validation("Kind", "This rendition cannot be uploaded by hand.");
        }

        if (await RenditionLimits.ReadAsync(command.Content, RenditionLimits.MaxUpload, cancellationToken) is not { } bytes || RenditionLimits.Sniff(bytes) is not { } mimeType)
        {
            return Error.Validation("Content", "Use a PNG, JPEG or WebP of up to 100 MB.");
        }

        var derivation = await db.Set<Derivation>().SingleOrDefaultAsync(d => d.VersionId == versionId && d.Kind == type.Key, cancellationToken);
        if (derivation is null)
        {
            derivation = new Derivation { VersionId = versionId, AssetId = asset.Id, Kind = type.Key };
            db.Add(derivation);
        }
        else if (derivation.State == DerivationStates.Pending)
        {
            // the job would overwrite the upload
            return Error.Conflict("assets.rendering", "This rendition is being created; try again in a moment.");
        }
        else if (derivation.BlobKey is { } previous)
        {
            db.Add(new OrphanBlob { Key = previous });
        }

        derivation.BlobKey = BlobKeys.New(asset.TenantId, clock);
        // ponytail: the API host has no image library, so hand made renditions carry no pixel size
        await storage.PutAsync(derivation.BlobKey, new MemoryStream(bytes), mimeType, cancellationToken);
        derivation.State = DerivationStates.Ready;
        derivation.IsManual = true;
        derivation.MimeType = mimeType;
        derivation.Size = bytes.Length;
        derivation.Width = null;
        derivation.Height = null;
        derivation.Error = null;
        return Map(derivation);
    }

    internal static DerivationDto Map(Derivation d) => new(d.Kind, d.State, d.Width, d.Height, d.MimeType, d.Size, d.IsManual, d.Error);

    private static DerivationTypeDto Map(DerivationType t) =>
        new(t.Id, t.Key, t.Name, t.Width, t.Height, t.Format, t.Quality, t.IsDefault, t.IsOnDemand, t.AllowManualUpload, t.WatermarkId, t.AssetTypeKeys);

    private static WatermarkDto Map(WatermarkDefinition w) => new(w.Id, w.Name, w.BlobKey is not null, w.Position, w.Opacity, w.ScalePercent, w.TriggerProperty, w.TriggerValue);
}
