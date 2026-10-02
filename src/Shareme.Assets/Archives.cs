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
            extraction.HasIndex(e => e.ArchiveId);
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
        await db.SaveChangesAsync(cancellationToken);
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
/// paths only become folder names; the unpacked size is capped (declared sizes are not trusted).
/// </summary>
internal sealed class ArchiveExtractionJob(CoworkeeDbContext db, IBlobStorage storage, IConfiguration configuration, TimeProvider clock, ILogger<ArchiveExtractionJob> logger)
    : IBackgroundJob<Guid>
{
    public async Task ExecuteAsync(Guid extractionId, CancellationToken cancellationToken)
    {
        if (await db.Set<ArchiveExtraction>().IgnoreQueryFilters().SingleOrDefaultAsync(e => e.Id == extractionId && e.State == ExtractionStates.Queued, cancellationToken) is not { } extraction)
        {
            return;
        }

        extraction.State = ExtractionStates.Running;
        await db.SaveChangesAsync(cancellationToken);
        var work = Path.Combine(Path.GetTempPath(), $"shareme-unzip-{extraction.Id:N}");
        Directory.CreateDirectory(work);
        try
        {
            var archive = await db.Set<Asset>().IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == extraction.ArchiveId, cancellationToken);
            var version = await db.Set<AssetVersion>().IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.Id == archive.CurrentVersionId, cancellationToken);
            var copy = Path.Combine(work, "archive.zip");
            await using (var original = await storage.OpenReadAsync(version.BlobKey, cancellationToken) ?? throw new FileNotFoundException("The archive is not in storage."))
            await using (var file = File.Create(copy))
            {
                await original.CopyToAsync(file, cancellationToken);
            }

            await UnpackAsync(extraction, copy, work, cancellationToken);
            extraction.State = ExtractionStates.Completed;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Archive extraction {ExtractionId} failed.", extractionId);
            db.ChangeTracker.Clear();
            extraction = await db.Set<ArchiveExtraction>().IgnoreQueryFilters().SingleAsync(e => e.Id == extractionId, CancellationToken.None);
            extraction.State = ExtractionStates.Failed;
            extraction.Error = exception is InvalidDataException ? "The archive is damaged or not a zip file." : exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
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

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task UnpackAsync(ArchiveExtraction extraction, string path, string work, CancellationToken cancellationToken)
    {
        var target = await db.Set<Folder>().IgnoreQueryFilters().SingleAsync(f => f.Id == extraction.TargetFolderId && !f.IsDeleted, cancellationToken);
        var maxTotal = configuration.GetValue(Archives.MaxTotalSetting, Archives.DefaultMaxTotal);
        var folders = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = target };
        long total = 0;
        using var zip = ZipFile.OpenRead(path);
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
            var fileName = segments[^1];
            var folder = extraction.KeepFolders ? await FolderAsync(folders, segments[..^1], cancellationToken) : target;
            var temp = Path.Combine(work, "entry");
            try
            {
                await using (var source = entry.Open())
                await using (var file = File.Create(temp))
                {
                    // counts what really comes out, so a zip bomb stops at the cap whatever it declares
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        total += read;
                        if (total > maxTotal)
                        {
                            throw new InvalidOperationException($"The archive unpacks to more than {maxTotal / (1024 * 1024)} MB.");
                        }

                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }

                if (await ImportAsync(folder, fileName, temp, cancellationToken) is { } problem)
                {
                    extraction.Failed++;
                    Note(extraction, $"{entryPath}: {problem}");
                }
                else
                {
                    extraction.Extracted++;
                }
            }
            catch (InvalidDataException)
            {
                extraction.Failed++;
                Note(extraction, $"{entryPath}: the entry is damaged.");
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Folder> FolderAsync(Dictionary<string, Folder> folders, List<string> segments, CancellationToken cancellationToken)
    {
        var parent = folders[string.Empty];
        for (var depth = 1; depth <= segments.Count; depth++)
        {
            var key = string.Join('/', segments.Take(depth));
            if (!folders.TryGetValue(key, out var folder))
            {
                // an existing subfolder of the same name is reused, so unpacking twice fills the same tree
                var name = segments[depth - 1];
                var parentId = parent.Id;
                folder = await db.Set<Folder>().IgnoreQueryFilters().FirstOrDefaultAsync(f => f.ParentId == parentId && f.Name == name && !f.IsDeleted, cancellationToken);
                if (folder is null)
                {
                    folder = new Folder { Name = name, NodeTypeId = NodeType.FolderTypeId, TenantId = parent.TenantId };
                    folder.PlaceUnder(parent);
                    folder.MarkChanged("created");
                    db.Add(folder);
                }

                folders[key] = folder;
            }

            parent = folder;
        }

        return parent;
    }

    /// <summary>Stores one file as a new asset like an upload would (type and content checked); returns why it was refused.</summary>
    private async Task<string?> ImportAsync(Folder folder, string fileName, string path, CancellationToken cancellationToken)
    {
        var parts = fileName.Split('.').Skip(1).Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length is > 0 and <= 20).ToList();
        if (await db.Set<FileType>().Where(t => t.ForbiddenForUpload && parts.Contains(t.Extension)).Select(t => t.Extension).FirstOrDefaultAsync(cancellationToken) is { } forbidden)
        {
            return $"files of type .{forbidden} are not taken.";
        }

        var extension = Uploads.Extension(fileName);
        var fileType = extension is null ? null : await db.Set<FileType>().AsNoTracking().SingleOrDefaultAsync(t => t.Extension == extension, cancellationToken);
        var info = new FileInfo(path);
        if (info.Length == 0)
        {
            return "the file is empty.";
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
            return $"the content is not a valid .{extension} file.";
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
        return null;
    }

    private static void Note(ArchiveExtraction extraction, string problem)
    {
        // the first problems are enough to act on; the counts tell the rest
        if (extraction.Problems.Count < 50)
        {
            extraction.Problems.Add(problem.Length > 300 ? problem[..300] : problem);
        }
    }
}
