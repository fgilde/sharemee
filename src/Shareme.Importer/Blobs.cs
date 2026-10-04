using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Coworkee.Storage;
using Shareme.Assets;
using Shareme.Contracts.Assets;
using Shareme.Graph;

namespace Shareme.Importer;

/// <summary>Where Sharedien kept its files: one provider per installation.</summary>
public interface ISourceBlobs
{
    /// <summary>The file of a derivation, or null when it is missing.</summary>
    Task<Stream?> OpenAsync(string dataId, CancellationToken cancellationToken);
}

/// <summary>StorageHandler on a file system (on premise): <c>root/id[0..2]/id[0..4]/id</c>.</summary>
public sealed class FileSystemBlobs(string root) : ISourceBlobs
{
    public Task<Stream?> OpenAsync(string dataId, CancellationToken cancellationToken)
    {
        if (dataId.Length < 4 || !dataId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return Task.FromResult<Stream?>(null);
        }

        var path = Path.Combine(root, dataId[..2], dataId[..4], dataId);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }
}

/// <summary>Azure Blob Storage (Azure deployments): the blob name is the data id.</summary>
public sealed class AzureBlobs(string connectionString, string container) : ISourceBlobs
{
    private readonly BlobContainerClient _container = new(connectionString, container);

    public async Task<Stream?> OpenAsync(string dataId, CancellationToken cancellationToken)
    {
        try
        {
            return await _container.GetBlobClient(dataId).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }
}

/// <summary>A file version to bring over: the original derivation of a Sharedien asset version.</summary>
public sealed record VersionCandidate(
    long VersionNodeId, Asset Asset, Guid VersionId, int Number, string FileName, string DataId, string? Md5, DateTimeOffset CreatedAt, bool IsCurrent);

/// <summary>
/// Copies the originals into Shareme's storage before the database transaction (files are not transactional): a version
/// row is only written for a file that arrived, with the SHA-256 computed while copying.
/// </summary>
public sealed class BlobCopier(ISourceBlobs source, IBlobStorage target, IReadOnlyDictionary<string, (string MimeType, string AssetTypeKey)> fileTypes, ImportReport report)
{
    public async Task<List<AssetVersion>> CopyAsync(IReadOnlyList<VersionCandidate> candidates, IReadOnlySet<Guid> existing, Guid tenantId, CancellationToken cancellationToken)
    {
        var versions = new List<AssetVersion>();
        foreach (var candidate in candidates.Where(c => !existing.Contains(c.VersionId)))
        {
            // one unreadable file must not stop hours of copying: retry once, then report it and go on
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (await CopyOneAsync(candidate, tenantId, cancellationToken) is { } version)
                    {
                        versions.Add(version);
                    }

                    break;
                }
                catch (Exception exception) when (exception is not OperationCanceledException && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    report.Problem("blob_read_failed", "MAM.AssetVersion", candidate.VersionNodeId);
                    if (candidate.IsCurrent)
                    {
                        candidate.Asset.Status = AssetStatuses.Failed;
                    }

                    break;
                }
            }
        }

        report.Written["files"] = versions.Count;
        return versions;
    }

    private async Task<AssetVersion?> CopyOneAsync(VersionCandidate candidate, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var content = await source.OpenAsync(candidate.DataId, cancellationToken);
        if (content is null)
        {
            report.Problem("missing_blob", "MAM.AssetVersion", candidate.VersionNodeId);
            if (candidate.IsCurrent)
            {
                candidate.Asset.Status = AssetStatuses.Failed;
            }

            return null;
        }

        var extension = Path.GetExtension(candidate.FileName) is { Length: > 1 and <= 21 } dotted ? dotted[1..].ToLowerInvariant() : null; // as uploads do
        var type = extension is not null && fileTypes.TryGetValue(extension, out var known) ? known : ("application/octet-stream", AssetTypeKeys.Other);
        // the key follows the version: a re-run after a failed write overwrites instead of copying everything again
        var key = $"{tenantId:N}/import/{candidate.VersionId:N}";
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        await using var hashing = new HashingStream(content, sha256, md5);
        await target.PutAsync(key, hashing, type.Item1, cancellationToken);
        if (candidate.Md5 is { Length: > 0 } expected && Convert.ToBase64String(md5.GetHashAndReset()) != expected)
        {
            report.Problem("checksum_mismatch", "MAM.AssetVersion", candidate.VersionNodeId); // kept: the source said what it said
        }

        var version = new AssetVersion
        {
            AssetId = candidate.Asset.Id,
            Number = candidate.Number,
            FileName = candidate.FileName,
            Extension = extension,
            MimeType = type.Item1,
            Size = hashing.BytesRead,
            Sha256 = Convert.ToHexStringLower(sha256.GetHashAndReset()),
            BlobKey = key,
            TenantId = tenantId,
            CreatedAt = candidate.CreatedAt,
        };
        Ids.Set(version, candidate.VersionId);
        if (candidate.IsCurrent)
        {
            candidate.Asset.CurrentVersionId = version.Id;
            candidate.Asset.AssetTypeKey = type.Item2;
            candidate.Asset.Status = candidate.Asset.Status == AssetStatuses.Duplicate ? AssetStatuses.Duplicate : AssetStatuses.Ready;
        }

        return version;
    }

    /// <summary>Hashes what passes through and counts it, without buffering the file.</summary>
    private sealed class HashingStream(Stream inner, params IncrementalHash[] hashes) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public long BytesRead => _read;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _read; set => throw new NotSupportedException(); }

        private long _read;

        public override int Read(byte[] buffer, int offset, int count) => Count(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            return Count(buffer.Span[..read]);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Count(ReadOnlySpan<byte> data)
        {
            foreach (var hash in hashes)
            {
                hash.AppendData(data);
            }

            _read += data.Length;
            return data.Length;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
