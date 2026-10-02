using System.Security.Cryptography;
using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
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

public sealed record UploadChunk(Guid Id, int Index, Stream Content) : ICommand<Result>;

public sealed record CompleteUpload(Guid Id) : ICommand<Result<UploadResultDto>>;

internal static class Uploads
{
    public const int ChunkSize = 8 * 1024 * 1024;

    // ponytail: fixed limits; move to options when tenants need different quotas
    public const long MaxSize = 20L * 1024 * 1024 * 1024;

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public static readonly Error NotFound = Error.NotFound("uploads.not_found", "The upload does not exist or has expired.");

    public static string? Extension(string fileName) =>
        Path.GetExtension(fileName) is { Length: > 1 } extension ? extension[1..].ToLowerInvariant() : null;
}

internal sealed class UploadHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IBlobStorage storage, TimeProvider clock)
    : IHandler<StartUpload, Result<UploadSessionDto>>, IHandler<GetUpload, Result<UploadSessionDto>>, IHandler<UploadChunk, Result>, IHandler<CompleteUpload, Result<UploadResultDto>>
{
    public async Task<Result<UploadSessionDto>> HandleAsync(StartUpload command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var fileName = Path.GetFileName(request.FileName?.Trim() ?? string.Empty);
        if (fileName.Length is 0 or > 255)
        {
            return Error.Validation("FileName", "A file name of up to 255 characters is required.");
        }

        if (request.Size is <= 0 or > Uploads.MaxSize)
        {
            return Error.Validation("Size", $"Files from 1 byte to {Uploads.MaxSize / (1024 * 1024 * 1024)} GB can be uploaded.");
        }

        if (request.Sha256 is { } sha && (sha.Length != 64 || !sha.All(char.IsAsciiHexDigitLower)))
        {
            return Error.Validation("Sha256", "Use the lower case hex SHA-256 of the file.");
        }

        var extension = Uploads.Extension(fileName);
        if (extension is not null && await db.Set<FileType>().AnyAsync(t => t.Extension == extension && t.ForbiddenForUpload, cancellationToken))
        {
            return Error.Validation("FileName", $"Files of type '.{extension}' cannot be uploaded.");
        }

        if (request.AssetId is { } assetId)
        {
            if (!await db.Set<Asset>().AnyAsync(a => a.Id == assetId, cancellationToken))
            {
                return Error.NotFound("nodes.not_found", "The asset does not exist.");
            }
        }
        else if (request.TargetFolderId is not { } folderId || !await db.Set<Folder>().AnyAsync(f => f.Id == folderId, cancellationToken))
        {
            return Error.Validation("TargetFolderId", "Choose a folder to upload into.");
        }

        var session = new UploadSession
        {
            UserId = currentUser.UserId!.Value,
            TargetFolderId = request.AssetId is null ? request.TargetFolderId : null,
            AssetId = request.AssetId,
            FileName = fileName,
            Size = request.Size,
            Sha256 = request.Sha256,
            ChunkSize = Uploads.ChunkSize,
            ExpiresAt = clock.GetUtcNow().Add(Uploads.Lifetime),
        };
        db.Add(session);
        return new UploadSessionDto(session.Id, session.ChunkSize, session.ChunkCount, [], session.ExpiresAt);
    }

    public async Task<Result<UploadSessionDto>> HandleAsync(GetUpload query, CancellationToken cancellationToken)
    {
        if (await SessionAsync(query.Id, cancellationToken) is not { } session)
        {
            return Uploads.NotFound;
        }

        var received = new List<int>();
        for (var index = 0; index < session.ChunkCount; index++)
        {
            if (await storage.ExistsAsync(session.ChunkKey(index), cancellationToken))
            {
                received.Add(index);
            }
        }

        return new UploadSessionDto(session.Id, session.ChunkSize, session.ChunkCount, received, session.ExpiresAt);
    }

    public async Task<Result> HandleAsync(UploadChunk command, CancellationToken cancellationToken)
    {
        if (await SessionAsync(command.Id, cancellationToken) is not { } session)
        {
            return Uploads.NotFound;
        }

        if (command.Index < 0 || command.Index >= session.ChunkCount)
        {
            return Error.Validation("Index", $"Chunks are numbered from 0 to {session.ChunkCount - 1}.");
        }

        var expected = command.Index < session.ChunkCount - 1 ? session.ChunkSize : session.Size - ((long)session.ChunkCount - 1) * session.ChunkSize;
        using var buffer = new MemoryStream((int)expected);
        await CopyLimitedAsync(command.Content, buffer, expected + 1, cancellationToken);
        if (buffer.Length != expected)
        {
            return Error.Validation("Content", $"Chunk {command.Index} must have {expected} bytes, not {buffer.Length}.");
        }

        buffer.Position = 0;
        await storage.PutAsync(session.ChunkKey(command.Index), buffer, "application/octet-stream", cancellationToken);
        return Result.Success();
    }

    public async Task<Result<UploadResultDto>> HandleAsync(CompleteUpload command, CancellationToken cancellationToken)
    {
        if (await SessionAsync(command.Id, cancellationToken) is not { } session)
        {
            return Uploads.NotFound;
        }

        var missing = new List<int>();
        for (var index = 0; index < session.ChunkCount; index++)
        {
            if (!await storage.ExistsAsync(session.ChunkKey(index), cancellationToken))
            {
                missing.Add(index);
            }
        }

        if (missing.Count > 0)
        {
            return Error.Validation("Chunks", $"Chunks {string.Join(", ", missing)} are missing.");
        }

        var temp = Path.Combine(Path.GetTempPath(), "shareme-upload-" + session.Id.ToString("N"));
        try
        {
            var (size, sha256, head) = await AssembleAsync(session, temp, cancellationToken);
            if (size != session.Size)
            {
                return Error.Validation("Size", $"The chunks add up to {size} bytes instead of {session.Size}.");
            }

            if (session.Sha256 is { } expected && expected != sha256)
            {
                return Error.Validation("Sha256", "The uploaded content does not match the announced SHA-256.");
            }

            var extension = Uploads.Extension(session.FileName);
            var fileType = extension is null ? null : await db.Set<FileType>().AsNoTracking().SingleOrDefaultAsync(t => t.Extension == extension, cancellationToken);
            if (fileType is { MagicNumbers.Count: > 0 } && !fileType.MagicNumbers.Any(magic => Convert.ToHexString(head).StartsWith(magic, StringComparison.Ordinal)))
            {
                return Error.Validation("Content", $"The content is not a valid .{extension} file.");
            }

            var key = BlobKeys.New(session.TenantId, clock);
            await using (var file = File.OpenRead(temp))
            {
                await storage.PutAsync(key, file, fileType?.MimeType ?? "application/octet-stream", cancellationToken);
            }

            var result = await StoreAsync(session, key, size, sha256, fileType, extension, cancellationToken);
            for (var index = 0; index < session.ChunkCount; index++)
            {
                await storage.DeleteAsync(session.ChunkKey(index), cancellationToken);
            }

            db.Remove(session);
            return result;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private async Task<UploadResultDto> StoreAsync(UploadSession session, string key, long size, string sha256, FileType? fileType, string? extension, CancellationToken cancellationToken)
    {
        Asset asset;
        int number;
        if (session.AssetId is { } assetId)
        {
            asset = await db.Set<Asset>().SingleAsync(a => a.Id == assetId, cancellationToken);
            number = await db.Set<AssetVersion>().Where(v => v.AssetId == asset.Id).MaxAsync(v => (int?)v.Number, cancellationToken) + 1 ?? 1;
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
            var folder = await db.Set<Folder>().SingleAsync(f => f.Id == session.TargetFolderId, cancellationToken);
            asset = new Asset { Name = session.FileName, NodeTypeId = NodeType.AssetTypeId };
            asset.PlaceUnder(folder);
            asset.MarkChanged("created");
            db.Add(asset);
            number = 1;
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
        asset.AssetTypeKey = fileType?.AssetTypeKey ?? AssetTypeKeys.Other;

        var duplicates = await (from other in db.Set<Asset>()
                                join current in db.Set<AssetVersion>() on other.CurrentVersionId equals current.Id
                                where current.Sha256 == sha256 && other.Id != asset.Id
                                select other.Id).Distinct().Take(20).ToListAsync(cancellationToken);
        var linked = await db.Set<Edge>().Where(e => e.EdgeTypeId == EdgeType.DuplicateId && e.FromNodeId == asset.Id).Select(e => e.ToNodeId).ToListAsync(cancellationToken);
        foreach (var duplicate in duplicates.Except(linked))
        {
            db.Add(new Edge { EdgeTypeId = EdgeType.DuplicateId, FromNodeId = asset.Id, ToNodeId = duplicate });
        }

        asset.Status = duplicates.Count > 0 ? AssetStatuses.Duplicate : AssetStatuses.Ready;
        asset.Publish(new AssetVersionCreated(asset.Id, version.Id));
        return new UploadResultDto(asset.Id, version.Id, number, duplicates);
    }

    /// <summary>Writes the chunks in order into one file while hashing; returns size, hash and the first bytes.</summary>
    private async Task<(long Size, string Sha256, byte[] Head)> AssembleAsync(UploadSession session, string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var head = new List<byte>(16);
        long size = 0;
        var buffer = new byte[81920];
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
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

    private Task<UploadSession?> SessionAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var now = clock.GetUtcNow();
        return db.Set<UploadSession>().SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId && s.ExpiresAt > now, cancellationToken);
    }

    private static async Task CopyLimitedAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while (total < limit && (read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - total)), cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }
    }
}
