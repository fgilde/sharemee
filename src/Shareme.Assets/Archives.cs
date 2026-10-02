using System.IO.Compression;
using System.Security.Cryptography;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Results;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Graph;

namespace Shareme.Assets;

/// <summary>Unpacking of a zip asset into a folder; files become assets, directories folders (unless only files are taken).</summary>
[NotAudited]
public sealed class ArchiveExtraction : Entity, IMultiTenant
{
    public Guid ArchiveId { get; set; }

    public Guid TargetFolderId { get; set; }

    public bool KeepFolders { get; set; } = true;

    public string State { get; set; } = ExtractionStates.Queued;

    public int Extracted { get; set; }

    public int Filtered { get; set; }

    public int Failed { get; set; }

    public string? Error { get; set; }

    public List<string> Problems { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public Guid TenantId { get; set; }
}

internal sealed class ArchiveModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ArchiveExtraction>(extraction =>
        {
            extraction.ToTable("ArchiveExtractions", "sm");
            extraction.Property(e => e.State).HasMaxLength(20);
            extraction.Property(e => e.Error).HasMaxLength(1000);
            extraction.Property(e => e.Problems).HasColumnType("jsonb");
            extraction.HasIndex(e => e.ArchiveId).IsUnique().HasFilter("\"State\" IN ('queued', 'running')").HasDatabaseName("IX_ArchiveExtractions_Active");
        });
}

/// <summary>Unpacks a zip asset; needs the download right on the archive and the create right in the target folder (default: the archive's folder).</summary>
public sealed record ExtractArchive(Guid Id, ExtractArchiveRequest Request) : ICommand<Result<ArchiveExtractionDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => AssetPermissions.Download;
}

public sealed record GetArchiveExtraction(Guid Id) : IQuery<Result<ArchiveExtractionDto>>;

internal static class Archives
{
    public const int MaxEntries = 10_000;

    /// <summary>Node paths hold about a hundred levels; archives get far fewer.</summary>
    public const int MaxDepth = 32;

    public const string MaxTotalSetting = "Shareme:Archives:MaxUnpackedBytes";

    public const long DefaultMaxTotal = 20L * 1024 * 1024 * 1024;

    /// <summary>System leftovers that are never imported.</summary>
    public static bool IsJunk(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries) is var parts
        && parts.Any(p => p.StartsWith('.') || p.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)
            || p.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || p.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase));

    public static ArchiveExtractionDto Map(ArchiveExtraction e) => new(e.Id, e.ArchiveId, e.TargetFolderId, e.State, e.Extracted, e.Filtered, e.Failed, e.Error, e.Problems);
}

internal sealed class ArchiveHandlers(CoworkeeDbContext db, NodeAccess access, IBackgroundJobs jobs, TimeProvider clock)
    : IHandler<ExtractArchive, Result<ArchiveExtractionDto>>, IHandler<GetArchiveExtraction, Result<ArchiveExtractionDto>>
{
    public async Task<Result<ArchiveExtractionDto>> HandleAsync(ExtractArchive command, CancellationToken cancellationToken)
    {
        if (await db.Set<Asset>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == command.Id, cancellationToken) is not { CurrentVersionId: { } versionId } archive)
        {
            return Error.NotFound("assets.not_found", "The asset has no content yet.");
        }

        if (await db.Set<AssetVersion>().Where(v => v.Id == versionId).Select(v => v.MimeType).SingleAsync(cancellationToken) != "application/zip")
        {
            return Error.Validation("Id", "Only zip archives can be unpacked.");
        }

        var targetId = command.Request.TargetFolderId ?? archive.ParentId;
        if (targetId is null || await db.Set<Folder>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == targetId, cancellationToken) is not { } target)
        {
            return Error.NotFound("nodes.not_found", "The target folder does not exist.");
        }

        if (!await access.CanAsync(NodePermissions.Create, target.Id, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "You may not add files to this folder.");
        }

        if (await db.Set<ArchiveExtraction>().AnyAsync(e => e.ArchiveId == archive.Id && (e.State == ExtractionStates.Queued || e.State == ExtractionStates.Running), cancellationToken))
        {
            return Error.Conflict("archives.running", "This archive is being unpacked already.");
        }

        var extraction = new ArchiveExtraction { ArchiveId = archive.Id, TargetFolderId = target.Id, KeepFolders = command.Request.KeepFolders, CreatedAt = clock.GetUtcNow() };
        db.Add(extraction);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // the partial unique index lets only one run per archive be active
            return Error.Conflict("archives.running", "This archive is being unpacked already.");
        }

        jobs.Enqueue<ArchiveExtractionJob, Guid>(extraction.Id);
        return Archives.Map(extraction);
    }

    public async Task<Result<ArchiveExtractionDto>> HandleAsync(GetArchiveExtraction query, CancellationToken cancellationToken)
    {
        if (await db.Set<ArchiveExtraction>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == query.Id, cancellationToken) is not { } extraction
            || !await access.CanAsync(NodePermissions.Read, extraction.ArchiveId, cancellationToken))
        {
            return Error.NotFound("archives.not_found", "The extraction does not exist.");
        }

        return Archives.Map(extraction);
    }
}

/// <summary>
/// Reads the archive from a temp copy and imports entry by entry: junk is filtered, names are cleaned, zip slip is impossible because
/// paths only become folder names; depth, entry count and unpacked size are capped (declared sizes are not trusted).
/// Runs as the user who asked, so folder rights are checked again here.
/// </summary>
internal sealed class ArchiveExtractionJob(CoworkeeDbContext db, IBlobStorage storage, NodeAccess access, IConfiguration configuration, TimeProvider clock, ILogger<ArchiveExtractionJob> logger)
    : IBackgroundJob<Guid>
{
    private readonly Dictionary<string, FileType?> fileTypes = [];

    public async Task ExecuteAsync(Guid extractionId, CancellationToken cancellationToken)
    {
        if (await db.Set<ArchiveExtraction>().IgnoreQueryFilters().SingleOrDefaultAsync(e => e.Id == extractionId && e.State == ExtractionStates.Queued, cancellationToken) is not { } extraction)
        {
            return;
        }

        extraction.State = ExtractionStates.Running;
        await db.SaveChangesAsync(cancellationToken);
        var root = configuration["Shareme:Uploads:TempRoot"] is { Length: > 0 } configured ? configured : Path.GetTempPath();
        var work = Path.Combine(root, $"shareme-unzip-{extraction.Id:N}");
        Directory.CreateDirectory(work);
        try
        {
            if (await db.Set<Asset>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == extraction.ArchiveId, cancellationToken) is not { CurrentVersionId: { } versionId })
            {
                throw new InvalidOperationException("The archive was deleted.");
            }

            var version = await db.Set<AssetVersion>().AsNoTracking().SingleAsync(v => v.Id == versionId, cancellationToken);
            var copy = Path.Combine(work, "archive.zip");
            await using (var original = await storage.OpenReadAsync(version.BlobKey, cancellationToken) ?? throw new InvalidOperationException("The archive is not in storage."))
            await using (var file = File.Create(copy))
            {
                await original.CopyToAsync(file, cancellationToken);
            }

            await UnpackAsync(extraction, copy, work, cancellationToken);
            extraction.State = ExtractionStates.Completed;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // every failure ends the run visibly; a retry would find it no longer queued
            logger.LogWarning(exception, "Archive extraction {ExtractionId} failed.", extractionId);
            db.ChangeTracker.Clear();
            var failed = await db.Set<ArchiveExtraction>().IgnoreQueryFilters().SingleAsync(e => e.Id == extractionId, CancellationToken.None);
            failed.State = ExtractionStates.Failed;
            failed.Error = exception switch
            {
                InvalidDataException => "The archive is damaged or not a zip file.",
                OperationCanceledException => "The extraction was interrupted.",
                InvalidOperationException or IOException => exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message,
                _ => "The extraction stopped because of an internal error.",
            };
            failed.Extracted = extraction.Extracted;
            failed.Filtered = extraction.Filtered;
            failed.Failed = extraction.Failed;
            failed.Problems = extraction.Problems;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // the temp folder is cleaned by the system
            }
        }
    }

    private async Task UnpackAsync(ArchiveExtraction extraction, string path, string work, CancellationToken cancellationToken)
    {
        if (await db.Set<Folder>().SingleOrDefaultAsync(f => f.Id == extraction.TargetFolderId, cancellationToken) is not { } target
            || !await access.CanAsync(NodePermissions.Create, target.Id, cancellationToken))
        {
            throw new InvalidOperationException("The target folder is gone or no longer writable for you.");
        }

        var maxTotal = configuration.GetValue(Archives.MaxTotalSetting, Archives.DefaultMaxTotal);
        var folders = new Dictionary<string, Folder?>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = target };
        long total = 0;

        // ponytail: the central directory is read before the count is checked; bounded by the archive size, an EOCD pre-check would stop huge ones earlier
        await using var zip = await ZipArchive.CreateAsync(File.OpenRead(path), ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: null, cancellationToken);
        if (zip.Entries.Count > Archives.MaxEntries)
        {
            throw new InvalidOperationException($"The archive holds more than {Archives.MaxEntries} entries.");
        }

        foreach (var entry in zip.Entries)
        {
            var entryPath = entry.FullName.Replace('\\', '/');
            if (entryPath.EndsWith('/'))
            {
                continue;
            }

            if (Archives.IsJunk(entryPath))
            {
                extraction.Filtered++;
                continue;
            }

            var segments = entryPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(FileNames.Clean).ToList();
            if (segments.Count == 0 || segments.Count > Archives.MaxDepth)
            {
                Fail(extraction, entryPath, segments.Count == 0 ? "the entry has no name." : $"folders deeper than {Archives.MaxDepth} levels are not taken.");
                continue;
            }

            var folder = extraction.KeepFolders ? await FolderAsync(folders, segments[..^1], cancellationToken) : target;
            if (folder is null)
            {
                Fail(extraction, entryPath, "you may not add files to its folder.");
                continue;
            }

            var temp = Path.Combine(work, "entry");
            try
            {
                await using (var source = await entry.OpenAsync(cancellationToken))
                await using (var file = File.Create(temp))
                {
                    // counts what really comes out, so a zip bomb stops at the cap whatever it declares
                    var buffer = new byte[81920];
                    long size = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        total += read;
                        size += read;
                        if (total > maxTotal)
                        {
                            throw new InvalidOperationException($"The archive unpacks to more than {maxTotal / (1024 * 1024)} MB.");
                        }

                        if (size > Uploads.MaxSize)
                        {
                            throw new InvalidDataException();
                        }

                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }
            }
            catch (InvalidDataException)
            {
                Fail(extraction, entryPath, "the entry is damaged or too large.");
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            if (await ImportAsync(folder, segments[^1], temp, cancellationToken) is not { } imported)
            {
                Fail(extraction, entryPath, problem);
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            extraction.Extracted++;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await storage.DeleteAsync(imported.Version.BlobKey, CancellationToken.None);
                throw;
            }

            // saved files leave the change tracker, so each save only looks at the few new rows
            db.Entry(imported.Asset).State = EntityState.Detached;
            db.Entry(imported.Version).State = EntityState.Detached;
        }
    }

    /// <summary>The folder for a path below the target, made where missing; null when an existing one along the way is not writable for the caller.</summary>
    private async Task<Folder?> FolderAsync(Dictionary<string, Folder?> folders, List<string> segments, CancellationToken cancellationToken)
    {
        var parent = folders[string.Empty]!;
        for (var depth = 1; depth <= segments.Count; depth++)
        {
            var key = string.Join('/', segments.Take(depth));
            if (!folders.TryGetValue(key, out var folder))
            {
                // an existing subfolder of the same name is reused, so unpacking twice fills the same tree, but only where the caller may add files
                var name = segments[depth - 1];
                var parentId = parent.Id;
                folder = await db.Set<Folder>().FirstOrDefaultAsync(f => f.ParentId == parentId && f.Name == name, cancellationToken);
                if (folder is null)
                {
                    folder = new Folder { Name = name, NodeTypeId = NodeType.FolderTypeId, TenantId = parent.TenantId };
                    folder.PlaceUnder(parent);
                    folder.MarkChanged("created");
                    db.Add(folder);
                }
                else if (!await access.CanAsync(NodePermissions.Create, folder.Id, cancellationToken))
                {
                    folder = null;
                }

                folders[key] = folder;
            }

            if (folder is null)
            {
                return null;
            }

            parent = folder;
        }

        return parent;
    }

    private string problem = string.Empty;

    /// <summary>Stores one file as a new asset like an upload would (type and content checked); null with <see cref="problem"/> set when it is refused.</summary>
    private async Task<(Asset Asset, AssetVersion Version)?> ImportAsync(Folder folder, string fileName, string path, CancellationToken cancellationToken)
    {
        var parts = fileName.Split('.').Skip(1).Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length is > 0 and <= 20).ToList();
        if (await db.Set<FileType>().Where(t => t.ForbiddenForUpload && parts.Contains(t.Extension)).Select(t => t.Extension).FirstOrDefaultAsync(cancellationToken) is { } forbidden)
        {
            problem = $"files of type .{forbidden} are not taken.";
            return null;
        }

        var extension = Uploads.Extension(fileName);
        if (extension is not null && !fileTypes.ContainsKey(extension))
        {
            fileTypes[extension] = await db.Set<FileType>().AsNoTracking().SingleOrDefaultAsync(t => t.Extension == extension, cancellationToken);
        }

        var fileType = extension is null ? null : fileTypes[extension];
        var info = new FileInfo(path);
        if (info.Length == 0)
        {
            problem = "the file is empty.";
            return null;
        }

        string sha256;
        var head = new byte[16];
        await using (var file = File.OpenRead(path))
        {
            var read = await file.ReadAsync(head, cancellationToken);
            head = head[..read];
            file.Position = 0;
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
        }

        if (fileType is { MagicNumbers.Count: > 0 } && !fileType.MagicNumbers.Any(magic => Convert.ToHexString(head).StartsWith(magic, StringComparison.Ordinal)))
        {
            problem = $"the content is not a valid .{extension} file.";
            return null;
        }

        var key = BlobKeys.New(folder.TenantId, clock);
        await using (var file = File.OpenRead(path))
        {
            await storage.PutAsync(key, file, fileType?.MimeType ?? "application/octet-stream", cancellationToken);
        }

        var asset = new Asset
        {
            Name = fileName,
            NodeTypeId = NodeType.AssetTypeId,
            Status = AssetStatuses.Ready,
            AssetTypeKey = fileType?.AssetTypeKey ?? AssetTypeKeys.Other,
            TenantId = folder.TenantId,
        };
        asset.PlaceUnder(folder);
        var version = new AssetVersion
        {
            AssetId = asset.Id,
            Number = 1,
            FileName = fileName,
            Size = info.Length,
            Sha256 = sha256,
            MimeType = fileType?.MimeType ?? "application/octet-stream",
            Extension = extension,
            BlobKey = key,
            TenantId = folder.TenantId,
        };
        asset.CurrentVersionId = version.Id;
        asset.MarkChanged("uploaded");
        asset.Publish(new AssetVersionCreated(asset.Id, version.Id));
        db.Add(asset);
        db.Add(version);
        return (asset, version);
    }

    private static void Fail(ArchiveExtraction extraction, string entryPath, string reason)
    {
        extraction.Failed++;

        // the first problems are enough to act on; the counts tell the rest
        if (extraction.Problems.Count < 50)
        {
            var text = $"{entryPath}: {reason}";
            extraction.Problems.Add(text.Length > 300 ? text[..300] : text);
        }
    }
}
