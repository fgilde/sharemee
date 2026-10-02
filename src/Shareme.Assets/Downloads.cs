using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Graph;

namespace Shareme.Assets;

public sealed class FileNamingDefinition : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public string Format { get; set; } = FileNamingTokens.DefaultFormat;

    public string Casing { get; set; } = FileNamingCasings.None;

    public bool IsDefault { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>Files of a prepared zip; the random token is the link, valid for an hour.</summary>
[NotAudited]
public sealed class DownloadPackage : Entity, IMultiTenant
{
    /// <summary>Random (not time ordered) so links cannot be guessed.</summary>
    public Guid Token { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public required string Name { get; set; }

    public List<DownloadItem> Items { get; set; } = [];

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid TenantId { get; set; }
}

public sealed record DownloadItem(string BlobKey, string Path);

internal sealed class DownloadModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileNamingDefinition>(naming =>
        {
            naming.ToTable("FileNamingDefinitions", "sm");
            naming.Property(n => n.Name).HasMaxLength(200);
            naming.Property(n => n.Format).HasMaxLength(300);
            naming.Property(n => n.Casing).HasMaxLength(10);
        });

        modelBuilder.Entity<DownloadPackage>(package =>
        {
            package.ToTable("DownloadPackages", "sm");
            package.Property(p => p.Name).HasMaxLength(200);
            package.Property(p => p.Items).HasColumnType("jsonb")
                .HasConversion(i => JsonSerializer.Serialize(i, (JsonSerializerOptions?)null), s => JsonSerializer.Deserialize<List<DownloadItem>>(s, (JsonSerializerOptions?)null) ?? new())
                .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<DownloadItem>>(
                    (a, b) => a!.SequenceEqual(b!), i => i.Count, i => i.ToList()));
            package.HasIndex(p => p.ExpiresAt);
            package.HasIndex(p => p.Token).IsUnique();
        });
    }
}

/// <summary>Builds file names from a format: tokens replaced, characters that no file system takes removed, the extension added.</summary>
internal static partial class FileNames
{
    public sealed record Context(string Name, string? Extension, int Sequence, int Count, DateTimeOffset Date, string AssetType, string Kind, IReadOnlyDictionary<string, List<MetadataValue>> Metadata);

    [GeneratedRegex(@"\{([a-z]+)(?::([a-z0-9_-]+))?\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"\{[^}]*\}?")]
    private static partial Regex Braces();

    /// <summary>The file name without its extension, but only the extension the file type check found (so "v1.2 final" stays whole).</summary>
    public static string Stem(string fileName, string? extension) =>
        extension is { Length: > 0 } && fileName.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase) ? fileName[..^(extension.Length + 1)] : fileName;

    /// <summary>Unknown tokens, or null when the format is fine.</summary>
    public static string? Validate(string format)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Length > 300)
        {
            return "A format of up to 300 characters is required.";
        }

        var unknown = Braces().Matches(format).Select(m => m.Value)
            .Where(token => Token().Match(token) is not { Success: true } m || m.Length != token.Length
                || (m.Groups[1].Value == FileNamingTokens.MetaPrefix ? !m.Groups[2].Success : !FileNamingTokens.Variables.Contains(m.Groups[1].Value)))
            .ToList();
        return unknown.Count > 0 ? $"Unknown tokens: {string.Join(", ", unknown)}." : null;
    }

    public static string Apply(string format, string casing, Context context)
    {
        var name = Token().Replace(format, match => match.Groups[1].Value switch
        {
            "name" => context.Name,
            "ext" => context.Extension ?? string.Empty,
            "seq" => context.Sequence.ToString(new string('0', Math.Max(3, context.Count.ToString(CultureInfo.InvariantCulture).Length)), CultureInfo.InvariantCulture),
            "date" => context.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "year" => context.Date.ToString("yyyy", CultureInfo.InvariantCulture),
            "month" => context.Date.ToString("MM", CultureInfo.InvariantCulture),
            "day" => context.Date.ToString("dd", CultureInfo.InvariantCulture),
            "type" => context.AssetType,
            "kind" => context.Kind,
            FileNamingTokens.MetaPrefix => context.Metadata.TryGetValue(match.Groups[2].Value, out var values) && values.FirstOrDefault() is { } value ? Text(value) : string.Empty,
            _ => match.Value,
        });
        name = casing switch { FileNamingCasings.Lower => name.ToLowerInvariant(), FileNamingCasings.Upper => name.ToUpperInvariant(), _ => name };
        return Clean(name) + (context.Extension is { Length: > 0 } extension ? "." + Clean(extension) : string.Empty);
    }

    /// <summary>Without path separators, reserved or control characters and leading or trailing dots and spaces; never empty, at most 200 characters.</summary>
    public static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        }

        var cleaned = builder.ToString().Trim().Trim('.').Trim();
        cleaned = cleaned.Length > 200 ? cleaned[..200].TrimEnd() : cleaned;
        return cleaned.Length == 0 ? "file" : cleaned;
    }

    /// <summary>Appends " (2)", " (3)" … to names already taken (case-insensitive, as on Windows and macOS).</summary>
    public static string Unique(string path, string? extension, ISet<string> taken)
    {
        var candidate = path;
        var suffix = extension is { Length: > 0 } ? "." + extension : string.Empty;
        var stem = suffix.Length > 0 && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? path[..^suffix.Length] : path;
        extension = suffix.Length > 0 && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? suffix : string.Empty;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            candidate = $"{stem} ({n}){extension}";
        }

        return candidate;
    }

    private static string Text(MetadataValue value) =>
        value.List ?? value.V switch
        {
            { ValueKind: JsonValueKind.String } text => text.GetString() ?? string.Empty,
            { } other => other.GetRawText(),
            null => string.Empty,
        };
}

/// <summary>Which renditions need the download right: custom ones without a watermark and hand made ones (they may be as good as the original).</summary>
internal static class RenditionAccess
{
    public static bool NeedsDownload(string kind, bool isManual, IReadOnlySet<string> watermarked) =>
        !DerivationKinds.System.Contains(kind) && (isManual || !watermarked.Contains(kind));

    public static async Task<IReadOnlySet<string>> WatermarkedAsync(CoworkeeDbContext db, CancellationToken cancellationToken) =>
        (await db.Set<DerivationType>().Where(t => t.WatermarkId != null).Select(t => t.Key).ToListAsync(cancellationToken)).ToHashSet();
}

[RequiresPermission(NodePermissions.Admin)]
public sealed record SaveFileNaming(Guid? Id, FileNamingRequest Request) : ICommand<Result<FileNamingDto>>;

[RequiresPermission(NodePermissions.Admin)]
public sealed record DeleteFileNaming(Guid Id) : ICommand<Result>;

public sealed record GetFileNamings : IQuery<Result<IReadOnlyList<FileNamingDto>>>;

public sealed record PreviewFileNaming(FileNamingPreviewRequest Request) : IQuery<Result<IReadOnlyList<string>>>;

public sealed record CreateDownloadPackage(DownloadPackageRequest Request) : ICommand<Result<DownloadPackageDto>>;

internal sealed class DownloadHandlers(CoworkeeDbContext db, ICurrentUser currentUser, NodeAccess access, TimeProvider clock)
    : IHandler<SaveFileNaming, Result<FileNamingDto>>,
      IHandler<DeleteFileNaming, Result>,
      IHandler<GetFileNamings, Result<IReadOnlyList<FileNamingDto>>>,
      IHandler<PreviewFileNaming, Result<IReadOnlyList<string>>>,
      IHandler<CreateDownloadPackage, Result<DownloadPackageDto>>
{
    public const int MaxAssets = 5000;

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public async Task<Result<FileNamingDto>> HandleAsync(SaveFileNaming command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Error.Validation("Name", "A name of up to 200 characters is required.");
        }

        if (FileNames.Validate(request.Format) is { } problem)
        {
            return Error.Validation("Format", problem);
        }

        if (!FileNamingCasings.All.Contains(request.Casing))
        {
            return Error.Validation("Casing", $"Use one of: {string.Join(", ", FileNamingCasings.All)}.");
        }

        FileNamingDefinition naming;
        if (command.Id is { } id)
        {
            if (await db.Set<FileNamingDefinition>().SingleOrDefaultAsync(n => n.Id == id, cancellationToken) is not { } existing)
            {
                return Error.NotFound("namings.not_found", "The file naming rule does not exist.");
            }

            naming = existing;
        }
        else
        {
            naming = new FileNamingDefinition { Name = string.Empty };
            db.Add(naming);
        }

        if (request.IsDefault)
        {
            // one default per organisation
            foreach (var other in await db.Set<FileNamingDefinition>().Where(n => n.IsDefault && n.Id != naming.Id).ToListAsync(cancellationToken))
            {
                other.IsDefault = false;
            }
        }

        naming.Name = request.Name.Trim();
        naming.Format = request.Format;
        naming.Casing = request.Casing;
        naming.IsDefault = request.IsDefault;
        return Map(naming);
    }

    public async Task<Result> HandleAsync(DeleteFileNaming command, CancellationToken cancellationToken)
    {
        if (await db.Set<FileNamingDefinition>().SingleOrDefaultAsync(n => n.Id == command.Id, cancellationToken) is not { } naming)
        {
            return Error.NotFound("namings.not_found", "The file naming rule does not exist.");
        }

        db.Remove(naming);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<FileNamingDto>>> HandleAsync(GetFileNamings query, CancellationToken cancellationToken)
    {
        IReadOnlyList<FileNamingDto> namings = (await db.Set<FileNamingDefinition>().AsNoTracking().OrderBy(n => n.Name).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<FileNamingDto>>.Success(namings);
    }

    public async Task<Result<IReadOnlyList<string>>> HandleAsync(PreviewFileNaming query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        if (FileNames.Validate(request.Format) is { } problem)
        {
            return Error.Validation("Format", problem);
        }

        if (request.AssetIds.Count is 0 or > 50)
        {
            return Error.Validation("AssetIds", "Preview names for 1 to 50 assets.");
        }

        var ids = request.AssetIds.Distinct().ToList();
        var found = await db.Set<Asset>().AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
        var assets = await AssetSelection.WithPermissionAsync(db, access, found.OrderBy(a => ids.IndexOf(a.Id)).ToList(), NodePermissions.Read, cancellationToken);
        var versions = await AssetSelection.CurrentVersionsAsync(db, assets, cancellationToken);
        var now = clock.GetUtcNow();
        IReadOnlyList<string> names = assets.Where(a => versions.ContainsKey(a.Id)).Select((asset, index) => FileNames.Apply(request.Format, request.Casing,
            new FileNames.Context(FileNames.Stem(versions[asset.Id].FileName, versions[asset.Id].Extension), versions[asset.Id].Extension, index + 1, assets.Count, now, asset.AssetTypeKey, DownloadKinds.Original, asset.Metadata)))
            .ToList();
        return Result<IReadOnlyList<string>>.Success(names);
    }

    public async Task<Result<DownloadPackageDto>> HandleAsync(CreateDownloadPackage command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var original = request.Kind == DownloadKinds.Original;
        if (!original && (request.Kind == DerivationKinds.Still || (!DerivationKinds.System.Contains(request.Kind) && !await db.Set<DerivationType>().AnyAsync(t => t.Key == request.Kind, cancellationToken))))
        {
            return Error.Validation("Kind", "Choose originals or an existing rendition.");
        }

        if ((await access.RestrictionsAsync(cancellationToken)).BlockedRenditions.Contains(request.Kind))
        {
            return Error.Forbidden("downloads.rendition_blocked", "You may not fetch this rendition.");
        }

        var naming = request.NamingId is { } namingId
            ? await db.Set<FileNamingDefinition>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == namingId, cancellationToken)
            : await db.Set<FileNamingDefinition>().AsNoTracking().FirstOrDefaultAsync(n => n.IsDefault, cancellationToken);
        if (request.NamingId is not null && naming is null)
        {
            return Error.NotFound("namings.not_found", "The file naming rule does not exist.");
        }

        var resolved = await AssetSelection.ResolveAsync(db, access, request.AssetIds, request.FolderId, MaxAssets, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.Error!;
        }

        var (candidates, folderNames, packageName) = resolved.Value;
        var readable = (await AssetSelection.WithPermissionAsync(db, access, candidates, NodePermissions.Read, cancellationToken)).ToList();
        var downloadable = (await AssetSelection.WithPermissionAsync(db, access, readable, AssetPermissions.Download, cancellationToken)).Select(a => a.Id).ToHashSet();
        var versions = await AssetSelection.CurrentVersionsAsync(db, readable, cancellationToken);
        var versionIds = versions.Values.Select(v => v.Id).ToList();
        var derivations = original ? [] : await db.Set<Derivation>().AsNoTracking()
            .Where(d => versionIds.Contains(d.VersionId) && d.Kind == request.Kind && d.State == DerivationStates.Ready && d.BlobKey != null).ToDictionaryAsync(d => d.VersionId, cancellationToken);
        var watermarked = original ? new HashSet<string>() : await RenditionAccess.WatermarkedAsync(db, cancellationToken);

        var now = clock.GetUtcNow();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<DownloadItem>();
        foreach (var asset in readable)
        {
            var version = versions[asset.Id];
            string key, extension;
            if (original)
            {
                if (!downloadable.Contains(asset.Id))
                {
                    continue;
                }

                (key, extension) = (version.BlobKey, version.Extension ?? string.Empty);
            }
            else
            {
                if (!derivations.TryGetValue(version.Id, out var derivation) || (RenditionAccess.NeedsDownload(derivation.Kind, derivation.IsManual, watermarked) && !downloadable.Contains(asset.Id)))
                {
                    continue;
                }

                (key, extension) = (derivation.BlobKey!, DerivationFormats.Extension(derivation.MimeType).TrimStart('.'));
            }

            var fileName = FileNames.Apply(naming?.Format ?? FileNamingTokens.DefaultFormat, naming?.Casing ?? FileNamingCasings.None,
                new FileNames.Context(FileNames.Stem(version.FileName, version.Extension), extension, items.Count + 1, readable.Count, now, asset.AssetTypeKey, request.Kind, asset.Metadata));
            var folder = string.Join('/', asset.PathIds.Where(folderNames.ContainsKey).Select(id => folderNames[id]));
            items.Add(new DownloadItem(key, FileNames.Unique(folder.Length > 0 ? folder + "/" + fileName : fileName, FileNames.Clean(extension), taken)));
        }

        if (items.Count == 0)
        {
            return Error.Validation("AssetIds", "None of these files can be downloaded.");
        }

        var package = new DownloadPackage
        {
            UserId = currentUser.UserId ?? Guid.Empty,
            Name = FileNames.Clean(packageName) + "-" + now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture),
            Items = items,
            ExpiresAt = now.Add(Lifetime),
        };
        db.Add(package);
        return new DownloadPackageDto($"/api/v1/downloads/{package.Token}", package.ExpiresAt, items.Count, readable.Count - items.Count);
    }

    private static FileNamingDto Map(FileNamingDefinition n) => new(n.Id, n.Name, n.Format, n.Casing, n.IsDefault);
}

/// <summary>Assets chosen by id or as everything below a folder, and which of them the caller holds a permission on.</summary>
internal static class AssetSelection
{
    public sealed record Selection(List<Asset> Assets, Dictionary<Guid, string> FolderNames, string Name);

    /// <summary>Assets with content in the given order, or below the folder in path order with the cleaned names of its subfolders (so they keep their place).</summary>
    public static async Task<Result<Selection>> ResolveAsync(CoworkeeDbContext db, NodeAccess access, IReadOnlyList<Guid>? assetIds, Guid? folderId, int max, CancellationToken cancellationToken)
    {
        if ((assetIds is { Count: > 0 }) == folderId.HasValue || assetIds is { } many && many.Count > max)
        {
            return Error.Validation("AssetIds", $"Choose up to {max} assets or one folder.");
        }

        if (folderId is { } id)
        {
            if (await db.Set<Node>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == id && (f is Folder || f is Workspace), cancellationToken) is not { } folder
                || !await access.CanAsync(NodePermissions.Read, folder.Id, cancellationToken))
            {
                return Error.NotFound("nodes.not_found", "The folder does not exist.");
            }

            var below = await db.Set<Asset>().AsNoTracking().Where(a => a.Path.StartsWith(folder.Path) && a.CurrentVersionId != null)
                .OrderBy(a => a.Path).Take(max + 1).ToListAsync(cancellationToken);
            if (below.Count > max)
            {
                return Error.Validation("FolderId", $"The folder holds more than {max} files; take its subfolders one by one.");
            }

            // folders the caller cannot read keep their names to themselves; their files land one level up
            var subfolders = await db.Set<Folder>().AsNoTracking().Where(f => f.Path.StartsWith(folder.Path) && f.Id != folder.Id).ToListAsync(cancellationToken);
            var names = (await WithPermissionAsync(db, access, subfolders, NodePermissions.Read, cancellationToken)).ToDictionary(f => f.Id, f => FileNames.Clean(f.Name));
            return new Selection(below, names, folder.Name);
        }

        var ids = assetIds!.Distinct().ToList();
        var assets = await db.Set<Asset>().AsNoTracking().Where(a => ids.Contains(a.Id) && a.CurrentVersionId != null).ToListAsync(cancellationToken);
        return new Selection(assets.OrderBy(a => ids.IndexOf(a.Id)).ToList(), [], "download");
    }

    /// <summary>The assets the caller holds the permission on, in their order: a grant on the asset or on a folder above it, unless inheritance is interrupted in between.</summary>
    public static async Task<IReadOnlyList<T>> WithPermissionAsync<T>(CoworkeeDbContext db, NodeAccess access, IReadOnlyList<T> assets, string permission, CancellationToken cancellationToken)
        where T : Node
    {
        // nodes with a list value hidden from the caller drop out first
        var restrictions = await access.RestrictionsAsync(cancellationToken);
        assets = assets.Where(a => !restrictions.Hides(a)).ToList();
        if (assets.Count == 0 || await access.IsGlobalAsync(permission, cancellationToken))
        {
            return assets;
        }

        var granted = (await access.GrantedNodesAsync(permission, cancellationToken)).ToHashSet();
        var pathIds = assets.SelectMany(a => a.PathIds).Distinct().ToList();
        var interrupted = (await db.Set<Node>().AsNoTracking().Where(n => pathIds.Contains(n.Id) && n.InterruptInheritance).Select(n => n.Id).ToListAsync(cancellationToken)).ToHashSet();
        return assets.Where(a => NodeAccess.Readable(a.PathIds, granted, interrupted)[^1]).ToList();
    }

    public static async Task<Dictionary<Guid, AssetVersion>> CurrentVersionsAsync(CoworkeeDbContext db, IReadOnlyList<Asset> assets, CancellationToken cancellationToken)
    {
        var versionIds = assets.Select(a => a.CurrentVersionId).OfType<Guid>().ToList();
        var versions = await db.Set<AssetVersion>().AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
        return assets.Where(a => a.CurrentVersionId is { } id && versions.ContainsKey(id)).ToDictionary(a => a.Id, a => versions[a.CurrentVersionId!.Value]);
    }
}

/// <summary>Streams a prepared package as zip; the link is the authorization. Files gone from storage meanwhile are left out.</summary>
internal sealed class DownloadPackageWriter(CoworkeeDbContext db, IBlobStorage storage, TimeProvider clock, Microsoft.Extensions.Logging.ILogger<DownloadPackageWriter> logger)
{
    private static readonly HashSet<string> Packed = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".mp4", ".mov", ".webm", ".mkv", ".mp3", ".m4a", ".ogg", ".flac", ".zip", ".7z", ".rar", ".gz", ".docx", ".xlsx", ".pptx", ".pdf"];

    public async Task<DownloadPackage?> FindAsync(Guid token, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return await db.Set<DownloadPackage>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p => p.Token == token && p.ExpiresAt > now, cancellationToken);
    }

    /// <summary>A file that cannot be opened is left out; one that breaks while being copied ends the response, the zip is then incomplete.</summary>
    public async Task WriteAsync(DownloadPackage package, Stream output, CancellationToken cancellationToken)
    {
        await using var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        foreach (var item in package.Items)
        {
            Stream? content;
            try
            {
                content = await storage.OpenReadAsync(item.BlobKey, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "{Path} is left out of a download package.", item.Path);
                continue;
            }

            if (content is null)
            {
                continue;
            }

            await using (content)
            {
                // already compressed formats are stored, which keeps big packages fast
                var entry = zip.CreateEntry(item.Path, Packed.Contains(Path.GetExtension(item.Path).ToLowerInvariant()) ? CompressionLevel.NoCompression : CompressionLevel.Fastest);
                await using var target = await entry.OpenAsync(cancellationToken);
                await content.CopyToAsync(target, cancellationToken);
            }
        }
    }
}
