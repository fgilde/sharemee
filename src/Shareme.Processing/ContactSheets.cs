using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Shareme.Assets;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Graph;

namespace Shareme.Processing;

/// <summary>A PDF of thumbnails with names, 4 × 5 per page; thumbnails are for readers, so reading the assets is enough.</summary>
public sealed record CreateContactSheet(ContactSheetRequest Request) : IQuery<Result<BlobDownload>>;

/// <summary>ImageMagick reads a file for text starting with '@' and expands '%' escapes in labels and titles; user text must stay literal.</summary>
internal static class MagickText
{
    public static string Literal(string text)
    {
        var escaped = text.Replace("%", "%%", StringComparison.Ordinal);
        return escaped.StartsWith('@') ? " " + escaped : escaped;
    }
}

internal sealed class ContactSheetHandler(CoworkeeDbContext db, IBlobStorage storage, NodeAccess access, TimeProvider clock) : IHandler<CreateContactSheet, Result<BlobDownload>>
{
    public const int MaxAssets = 200;

    private const int Columns = 4;
    private const int Rows = 5;
    private const int Tile = 320;

    public async Task<Result<BlobDownload>> HandleAsync(CreateContactSheet query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var resolved = await AssetSelection.ResolveAsync(db, access, request.AssetIds, request.FolderId, MaxAssets, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.Error!;
        }

        var assets = await AssetSelection.WithPermissionAsync(db, access, resolved.Value.Assets, NodePermissions.Read, cancellationToken);
        var versionIds = assets.Select(a => a.CurrentVersionId).OfType<Guid>().ToList();
        // a blocked thumbnail leaves the sheet with names only
        var thumbnails = (await access.RestrictionsAsync(cancellationToken)).BlockedRenditions.Contains(DerivationKinds.Thumbnail)
            ? []
            : await db.Set<Derivation>().AsNoTracking()
                .Where(d => versionIds.Contains(d.VersionId) && d.Kind == DerivationKinds.Thumbnail && d.State == DerivationStates.Ready && d.BlobKey != null)
                .ToDictionaryAsync(d => d.VersionId, d => d.BlobKey!, cancellationToken);
        if (assets.Count == 0)
        {
            return Error.Validation("AssetIds", "None of these files can be shown.");
        }

        using var pages = new MagickImageCollection();
        foreach (var chunk in assets.Chunk(Columns * Rows))
        {
            using var tiles = new MagickImageCollection();
            foreach (var asset in chunk)
            {
                var tile = await TileAsync(asset.CurrentVersionId is { } versionId && thumbnails.TryGetValue(versionId, out var key) ? key : null, cancellationToken);
                tile.Label = MagickText.Literal(asset.Name.Length > 40 ? asset.Name[..39] + "…" : asset.Name);
                tiles.Add(tile);
            }

            var settings = new MontageSettings
            {
                Geometry = new MagickGeometry(Tile, Tile) { Greater = true },
                TileGeometry = new MagickGeometry(Columns, Rows),
                BackgroundColor = MagickColors.White,
                FillColor = MagickColors.Black,
                FontPointsize = 14,
                Title = request.Title is { Length: > 0 } title ? MagickText.Literal(title[..Math.Min(title.Length, 100)]) : null,
            };
            pages.Add(tiles.Montage(settings));
        }

        var bytes = pages.ToByteArray(MagickFormat.Pdf);
        var name = FileNames.Clean(request.Title is { Length: > 0 } t ? t : resolved.Value.Name) + "-contact-sheet.pdf";
        return new BlobDownload(new MemoryStream(bytes), name, "application/pdf", null, clock.GetUtcNow());
    }

    /// <summary>The thumbnail, or a grey square for files without one.</summary>
    private async Task<MagickImage> TileAsync(string? key, CancellationToken cancellationToken)
    {
        if (key is not null && await storage.OpenReadAsync(key, cancellationToken) is { } content)
        {
            await using (content)
            {
                try
                {
                    return new MagickImage(content, new MagickReadSettings { Format = MagickFormat.WebP });
                }
                catch (MagickException)
                {
                    // shown as missing
                }
            }
        }

        return new MagickImage(new MagickColor("#e0e0e0"), Tile, Tile);
    }
}
