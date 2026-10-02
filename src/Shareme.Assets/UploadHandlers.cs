using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Graph;
using Shareme.Metadata;

namespace Shareme.Assets;

public sealed record StartUpload(StartUploadRequest Request) : ICommand<Result<UploadSessionDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Request.AssetId ?? Request.TargetFolderId ?? Guid.Empty;

    public string RequiredPermission => Request.AssetId is null ? NodePermissions.Create : NodePermissions.Update;
}

public sealed record GetUpload(Guid Id) : IQuery<Result<UploadSessionDto>>;

public sealed record PutUploadChunk(Guid Id, int Index, Stream Content) : ICommand<Result>;

/// <summary>Claims the upload and hands the assembly to a background job; poll <see cref="GetUpload"/> for the outcome.</summary>
public sealed record CompleteUpload(Guid Id) : ICommand<Result<UploadSessionDto>>;

internal static class Uploads
{
    public const int ChunkSize = 8 * 1024 * 1024;

    // ponytail: fixed limits; move to options when tenants need different quotas
    public const long MaxSize = 20L * 1024 * 1024 * 1024;
    public const int MaxOpenSessionsPerUser = 20;

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public static readonly Error NotFound = Error.NotFound("uploads.not_found", "The upload does not exist or has expired.");

    /// <summary>The last extension in lower case, or null when there is none or it is not a plausible extension.</summary>
    public static string? Extension(string fileName) =>
        Path.GetExtension(fileName) is { Length: > 1 and <= 21 } extension ? extension[1..].ToLowerInvariant() : null;

    /// <summary>Strips directories and the trailing dots and spaces that Windows drops on save.</summary>
    public static string CleanFileName(string? fileName) =>
        Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim().TrimEnd('.', ' ');

    public static UploadSessionDto Map(UploadSession session, IReadOnlyList<int> received) => new(
        session.Id, session.ChunkSize, session.ChunkCount, received, session.ExpiresAt, session.State, session.AssetId,
        session is { State: UploadStates.Completed, AssetId: { } assetId, VersionId: { } versionId } ? new UploadResultDto(assetId, versionId, session.VersionNumber ?? 0, session.DuplicateOf) : null,
        session.Error);
}

internal sealed class UploadHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IBlobStorage storage, NodeAccess access, IBackgroundJobs jobs, TimeProvider clock)
    : IHandler<StartUpload, Result<UploadSessionDto>>, IHandler<GetUpload, Result<UploadSessionDto>>, IHandler<PutUploadChunk, Result>, IHandler<CompleteUpload, Result<UploadSessionDto>>
{
    public async Task<Result<UploadSessionDto>> HandleAsync(StartUpload command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var fileName = Uploads.CleanFileName(request.FileName);
        if (fileName.Length is 0 or > 255 || fileName.Any(char.IsControl))
        {
            return Error.Validation("FileName", "A file name of up to 255 characters without control characters is required.");
        }

        if (request.Size is <= 0 or > Uploads.MaxSize)
        {
            return Error.Validation("Size", $"Files from 1 byte to {Uploads.MaxSize / (1024 * 1024 * 1024)} GB can be uploaded.");
        }

        if (request.Sha256 is { } sha && (sha.Length != 64 || !sha.All(char.IsAsciiHexDigitLower)))
        {
            return Error.Validation("Sha256", "Use the lower case hex SHA-256 of the file.");
        }

        // every dotted part counts, so "invoice.exe.pdf" and "setup.exe." are caught as well
        var parts = fileName.Split('.').Skip(1).Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length is > 0 and <= 20).ToList();
        if (await db.Set<FileType>().Where(t => t.ForbiddenForUpload && parts.Contains(t.Extension)).Select(t => t.Extension).FirstOrDefaultAsync(cancellationToken) is { } forbidden)
        {
            return Error.Validation("FileName", $"Files of type '.{forbidden}' cannot be uploaded.");
        }

        if (request.AssetId is { } assetId)
        {
            if (!await db.Set<Asset>().AnyAsync(a => a.Id == assetId, cancellationToken))
            {
                return Error.NotFound("nodes.not_found", "The asset does not exist.");
            }
        }
        else if (request.TargetFolderId is not { } folderId || await db.Set<Node>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == folderId, cancellationToken) is not { } target || !target.AcceptsFiles())
        {
            return Error.Validation("TargetFolderId", "Choose a folder to upload into.");
        }

        var userId = currentUser.UserId!.Value;
        var now = clock.GetUtcNow();
        if (await db.Set<UploadSession>().CountAsync(s => s.UserId == userId && s.State == UploadStates.Open && s.ExpiresAt > now, cancellationToken) >= Uploads.MaxOpenSessionsPerUser)
        {
            return Error.Conflict("uploads.too_many", $"Finish or let expire some of your {Uploads.MaxOpenSessionsPerUser} open uploads first.");
        }

        var session = new UploadSession
        {
            UserId = userId,
            TargetFolderId = request.AssetId is null ? request.TargetFolderId : null,
            AssetId = request.AssetId,
            FileName = fileName,
            Size = request.Size,
            Sha256 = request.Sha256,
            ChunkSize = Uploads.ChunkSize,
            ExpiresAt = now.Add(Uploads.Lifetime),
        };
        db.Add(session);
        return Uploads.Map(session, []);
    }

    public async Task<Result<UploadSessionDto>> HandleAsync(GetUpload query, CancellationToken cancellationToken)
    {
        if (await SessionAsync(query.Id, cancellationToken) is not { } session)
        {
            return Uploads.NotFound;
        }

        return Uploads.Map(session, await ReceivedAsync(session.Id, cancellationToken));
    }

    public async Task<Result> HandleAsync(PutUploadChunk command, CancellationToken cancellationToken)
    {
        if (await SessionAsync(command.Id, cancellationToken) is not { State: UploadStates.Open } session)
        {
            return Uploads.NotFound;
        }

        if (command.Index < 0 || command.Index >= session.ChunkCount)
        {
            return Error.Validation("Index", $"Chunks are numbered from 0 to {session.ChunkCount - 1}.");
        }

        var expected = (int)(command.Index < session.ChunkCount - 1 ? session.ChunkSize : session.Size - ((long)session.ChunkCount - 1) * session.ChunkSize);
        var buffer = ArrayPool<byte>.Shared.Rent(expected + 1);
        try
        {
            var length = await ReadUpToAsync(command.Content, buffer, expected + 1, cancellationToken);
            if (length != expected)
            {
                return Error.Validation("Content", $"Chunk {command.Index} must have {expected} bytes, not {(length > expected ? "more" : length.ToString())}.");
            }

            using var content = new MemoryStream(buffer, 0, length, writable: false);
            await storage.PutAsync(session.ChunkKey(command.Index), content, "application/octet-stream", cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (!await db.Set<UploadChunk>().AnyAsync(c => c.SessionId == session.Id && c.Index == command.Index, cancellationToken))
        {
            db.Add(new UploadChunk { SessionId = session.Id, Index = command.Index, Size = expected });
        }

        return Result.Success();
    }

    public async Task<Result<UploadSessionDto>> HandleAsync(CompleteUpload command, CancellationToken cancellationToken)
    {
        if (await SessionAsync(command.Id, cancellationToken) is not { } session)
        {
            return Uploads.NotFound;
        }

        if (session.State != UploadStates.Open)
        {
            return Error.Conflict("uploads.not_open", $"The upload is already {session.State}.");
        }

        var received = await ReceivedAsync(session.Id, cancellationToken);
        var missing = Enumerable.Range(0, session.ChunkCount).Except(received).ToList();
        if (missing.Count > 0)
        {
            return Error.Validation("Chunks", $"Chunks {string.Join(", ", missing.Take(20))}{(missing.Count > 20 ? ", …" : string.Empty)} are missing.");
        }

        // rights may have changed since the upload started
        var target = session.AssetId ?? session.TargetFolderId!.Value;
        if (!await access.CanAsync(session.AssetId is null ? NodePermissions.Create : NodePermissions.Update, target, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "You may no longer upload here.");
        }

        Asset? placeholder = null;
        if (session.AssetId is null)
        {
            if (await db.Set<Node>().SingleOrDefaultAsync(f => f.Id == session.TargetFolderId, cancellationToken) is not { } folder || !folder.AcceptsFiles())
            {
                return Error.NotFound("nodes.not_found", "The target folder no longer exists.");
            }

            placeholder = new Asset { Name = session.FileName, NodeTypeId = NodeType.AssetTypeId, Status = AssetStatuses.Importing };
            placeholder.PlaceUnder(folder);
            placeholder.MarkChanged("created");
            db.Add(placeholder);
        }

        // the claim makes a second complete a no-op, even when both arrive at once
        var expires = clock.GetUtcNow().Add(Uploads.Lifetime);
        var assetId = placeholder?.Id ?? session.AssetId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await db.Set<UploadSession>().Where(s => s.Id == session.Id && s.State == UploadStates.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, UploadStates.Completing).SetProperty(x => x.AssetId, assetId).SetProperty(x => x.ExpiresAt, expires), cancellationToken);
        if (claimed == 0)
        {
            return Error.Conflict("uploads.not_open", "The upload is already being completed.");
        }

        db.Entry(session).State = EntityState.Detached;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        jobs.Enqueue<ProcessUploadJob, Guid>(session.Id);
        session.State = UploadStates.Completing;
        session.AssetId = assetId;
        session.ExpiresAt = expires;
        return Uploads.Map(session, received);
    }

    private async Task<IReadOnlyList<int>> ReceivedAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await db.Set<UploadChunk>().Where(c => c.SessionId == sessionId).OrderBy(c => c.Index).Select(c => c.Index).ToListAsync(cancellationToken);

    private Task<UploadSession?> SessionAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var now = clock.GetUtcNow();
        return db.Set<UploadSession>().SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId && s.ExpiresAt > now, cancellationToken);
    }

    private static async Task<int> ReadUpToAsync(Stream source, byte[] buffer, int limit, CancellationToken cancellationToken)
    {
        var total = 0;
        int read;
        while (total < limit && (read = await source.ReadAsync(buffer.AsMemory(total, limit - total), cancellationToken)) > 0)
        {
            total += read;
        }

        return total;
    }
}

/// <summary>Assembles, verifies and stores a claimed upload as a new asset or version.</summary>
internal sealed class ProcessUploadJob(CoworkeeDbContext db, IBlobStorage storage, NodeAccess access, IConfiguration configuration, TimeProvider clock, ILogger<ProcessUploadJob> logger)
    : IBackgroundJob<Guid>
{
    public async Task ExecuteAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (await db.Set<UploadSession>().SingleOrDefaultAsync(s => s.Id == sessionId && s.State == UploadStates.Completing, cancellationToken) is not { AssetId: { } assetId } session)
        {
            return;
        }

        // ponytail: assembles through a temp file (needs free disk of the file size); stream straight into multipart uploads when that hurts
        var tempRoot = configuration["Shareme:Uploads:TempRoot"] is { Length: > 0 } root ? root : Path.GetTempPath();
        Directory.CreateDirectory(tempRoot);
        var temp = Path.Combine(tempRoot, $"shareme-upload-{session.Id:N}-{Guid.NewGuid():N}");
        string? key = null;
        try
        {
            var (size, sha256, head) = await AssembleAsync(session, temp, cancellationToken);
            var extension = Uploads.Extension(session.FileName);
            var fileType = extension is null ? null : await db.Set<FileType>().AsNoTracking().SingleOrDefaultAsync(t => t.Extension == extension, cancellationToken);
            var problem = size != session.Size ? $"The chunks add up to {size} bytes instead of {session.Size}."
                : session.Sha256 is { } expected && expected != sha256 ? "The uploaded content does not match the announced SHA-256."
                : fileType is { MagicNumbers.Count: > 0 } && !fileType.MagicNumbers.Any(magic => Convert.ToHexString(head).StartsWith(magic, StringComparison.Ordinal)) ? $"The content is not a valid .{extension} file."
                : null;
            if (problem is not null)
            {
                await FailAsync(session, problem, cancellationToken);
                return;
            }

            key = BlobKeys.New(session.TenantId, clock);
            await using (var file = File.OpenRead(temp))
            {
                await storage.PutAsync(key, file, fileType?.MimeType ?? "application/octet-stream", cancellationToken);
            }

            await StoreAsync(session, assetId, key, size, sha256, fileType, extension, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Upload {SessionId} could not be stored.", session.Id);
            if (key is not null)
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }

            db.ChangeTracker.Clear();
            await FailAsync(await db.Set<UploadSession>().SingleAsync(s => s.Id == sessionId, CancellationToken.None), "The file could not be stored. Please try again.", CancellationToken.None);
            return;
        }
        finally
        {
            File.Delete(temp);
        }

        try
        {
            await storage.DeletePrefixAsync(session.ChunkPrefix, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // the cleanup job removes the chunks once the session expires
            logger.LogWarning(exception, "Chunks of upload {SessionId} stay until the session expires.", session.Id);
        }
    }

    private async Task StoreAsync(UploadSession session, Guid assetId, string key, long size, string sha256, FileType? fileType, string? extension, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // serialises version numbers of one asset
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM sm."Nodes" WHERE "Id" = {assetId} FOR UPDATE""", cancellationToken);
        var asset = await db.Set<Asset>().SingleOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset {assetId} disappeared during the upload.");
        var number = await db.Set<AssetVersion>().Where(v => v.AssetId == asset.Id).MaxAsync(v => (int?)v.Number, cancellationToken) + 1 ?? 1;
        if (number > 1)
        {
            if (await db.Set<AssetVersion>().SingleOrDefaultAsync(v => v.Id == asset.CurrentVersionId, cancellationToken) is { } previous)
            {
                previous.MetadataSnapshot = JsonSerializer.Serialize(asset.Metadata);
            }

            var dropped = await db.Set<PropertyDefinition>().Where(d => d.IgnoreOnNewVersion).Select(d => d.Key).ToListAsync(cancellationToken);
            asset.Metadata = asset.Metadata.Where(p => !dropped.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            asset.MarkChanged("versioned");
        }
        else
        {
            asset.MarkChanged("uploaded");
        }

        var version = new AssetVersion
        {
            AssetId = asset.Id,
            Number = number,
            FileName = session.FileName,
            Size = size,
            Sha256 = sha256,
            MimeType = fileType?.MimeType ?? "application/octet-stream",
            Extension = extension,
            BlobKey = key,
        };
        db.Add(version);
        asset.CurrentVersionId = version.Id;
        asset.RenditionTag = null;
        asset.AssetTypeKey = fileType?.AssetTypeKey ?? AssetTypeKeys.Other;

        // duplicates follow the current content; links the uploader cannot see stay out of the answer
        await db.Set<Edge>().Where(e => e.EdgeTypeId == EdgeType.DuplicateId && e.FromNodeId == asset.Id).ExecuteDeleteAsync(cancellationToken);
        var duplicates = await (from other in db.Set<Asset>()
                                join current in db.Set<AssetVersion>() on other.CurrentVersionId equals current.Id
                                where current.Sha256 == sha256 && other.Id != asset.Id
                                select other.Id).Distinct().Take(20).ToListAsync(cancellationToken);
        db.AddRange(duplicates.Select(duplicate => new Edge { EdgeTypeId = EdgeType.DuplicateId, FromNodeId = asset.Id, ToNodeId = duplicate }));
        var visible = new List<Guid>();
        foreach (var duplicate in duplicates)
        {
            if (await access.CanAsync(NodePermissions.Read, duplicate, cancellationToken))
            {
                visible.Add(duplicate);
            }
        }

        asset.Status = duplicates.Count > 0 ? AssetStatuses.Duplicate : AssetStatuses.Ready;
        asset.Publish(new AssetVersionCreated(asset.Id, version.Id));
        session.State = UploadStates.Completed;
        session.VersionId = version.Id;
        session.VersionNumber = number;
        session.DuplicateOf = visible;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task FailAsync(UploadSession session, string error, CancellationToken cancellationToken)
    {
        session.State = UploadStates.Failed;
        session.Error = error;
        if (session.AssetId is { } assetId && await db.Set<Asset>().SingleOrDefaultAsync(a => a.Id == assetId && a.CurrentVersionId == null, cancellationToken) is { } placeholder)
        {
            placeholder.Status = AssetStatuses.Failed;
            placeholder.MarkChanged("failed");
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Writes the chunks in order into one file while hashing; returns size, hash and the first bytes.</summary>
    private async Task<(long Size, string Sha256, byte[] Head)> AssembleAsync(UploadSession session, string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var head = new List<byte>(16);
        long size = 0;
        var buffer = new byte[81920];
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
        for (var index = 0; index < session.ChunkCount; index++)
        {
            await using var chunk = await storage.OpenReadAsync(session.ChunkKey(index), cancellationToken)
                ?? throw new InvalidOperationException($"Chunk {index} disappeared.");
            int read;
            while ((read = await chunk.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                head.AddRange(buffer.Take(Math.Min(read, 16 - head.Count)));
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                size += read;
            }
        }

        return (size, Convert.ToHexStringLower(hash.GetHashAndReset()), [.. head]);
    }
}
