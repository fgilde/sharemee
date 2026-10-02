using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Assets;
using Shareme.Graph;

namespace Shareme.Assets;

public sealed class AssetType
{
    public required string Key { get; set; }

    public required string Name { get; set; }

    public bool DownloadOriginalAlways { get; set; }
}

public sealed class FileType
{
    public required string Extension { get; set; }

    public required string MimeType { get; set; }

    public required string AssetTypeKey { get; set; }

    public bool ForbiddenForUpload { get; set; }

    /// <summary>Accepted leading bytes as hex; empty accepts any content.</summary>
    public List<string> MagicNumbers { get; set; } = [];
}

public sealed record AssetVersionCreated(Guid AssetId, Guid VersionId) : IDomainEvent;

public sealed class AssetVersion : AuditedEntity, IMultiTenant
{
    public Guid AssetId { get; set; }

    public int Number { get; set; }

    public required string FileName { get; set; }

    public long Size { get; set; }

    public required string Sha256 { get; set; }

    public required string MimeType { get; set; }

    public string? Extension { get; set; }

    public required string BlobKey { get; set; }

    /// <summary>The asset's metadata at the moment this version was replaced.</summary>
    public string? MetadataSnapshot { get; set; }

    public Guid TenantId { get; set; }
}

[NotAudited]
public sealed class UploadSession : Entity, IMultiTenant
{
    public Guid UserId { get; set; }

    public Guid? TargetFolderId { get; set; }

    public Guid? AssetId { get; set; }

    public required string FileName { get; set; }

    public long Size { get; set; }

    public string? Sha256 { get; set; }

    public int ChunkSize { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid TenantId { get; set; }

    public string State { get; set; } = UploadStates.Open;

    public string? Error { get; set; }

    public Guid? VersionId { get; set; }

    public int? VersionNumber { get; set; }

    public List<Guid> DuplicateOf { get; set; } = [];

    public string ChunkPrefix => $"{TenantId:N}/uploads/{Id:N}";

    public int ChunkCount => (int)((Size + ChunkSize - 1) / ChunkSize);

    public string ChunkKey(int index) => $"{ChunkPrefix}/{index}";
}

/// <summary>A received chunk; the primary key makes repeated or parallel PUTs of one index harmless.</summary>
public sealed class UploadChunk
{
    public Guid SessionId { get; set; }

    public int Index { get; set; }

    public long Size { get; set; }
}

/// <summary>A stored file whose rows are gone; the cleanup job deletes it.</summary>
[NotAudited]
public sealed class OrphanBlob : Entity
{
    public required string Key { get; set; }
}

internal sealed class AssetModelContributor : IModelContributor
{
    private const string Schema = "sm";

    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssetType>(type =>
        {
            type.ToTable("AssetTypes", Schema);
            type.HasKey(t => t.Key);
            type.Property(t => t.Key).HasMaxLength(50);
            type.Property(t => t.Name).HasMaxLength(100);
            type.HasData(
                new AssetType { Key = AssetTypeKeys.Image, Name = "Image" },
                new AssetType { Key = AssetTypeKeys.Video, Name = "Video" },
                new AssetType { Key = AssetTypeKeys.Audio, Name = "Audio" },
                new AssetType { Key = AssetTypeKeys.Document, Name = "Document" },
                new AssetType { Key = AssetTypeKeys.Archive, Name = "Archive", DownloadOriginalAlways = true },
                new AssetType { Key = AssetTypeKeys.Other, Name = "Other", DownloadOriginalAlways = true });
        });

        modelBuilder.Entity<FileType>(type =>
        {
            type.ToTable("FileTypes", Schema);
            type.HasKey(t => t.Extension);
            type.Property(t => t.Extension).HasMaxLength(20);
            type.Property(t => t.MimeType).HasMaxLength(100);
            type.Property(t => t.AssetTypeKey).HasMaxLength(50);
            type.Property(t => t.MagicNumbers).HasColumnType("jsonb");
            type.HasOne<AssetType>().WithMany().HasForeignKey(t => t.AssetTypeKey).OnDelete(DeleteBehavior.Restrict);
            type.HasData(FileTypeSeeds.All);
        });

        modelBuilder.Entity<AssetVersion>(version =>
        {
            version.ToTable("AssetVersions", Schema);
            version.Property(v => v.FileName).HasMaxLength(255);
            version.Property(v => v.Sha256).HasMaxLength(64);
            version.Property(v => v.MimeType).HasMaxLength(100);
            version.Property(v => v.Extension).HasMaxLength(20);
            version.Property(v => v.BlobKey).HasMaxLength(512);
            version.Property(v => v.MetadataSnapshot).HasColumnType("jsonb");
            version.HasIndex(v => new { v.AssetId, v.Number }).IsUnique();
            version.HasIndex(v => new { v.TenantId, v.Sha256 });
            version.HasIndex(v => v.BlobKey);
            version.HasOne<Node>().WithMany().HasForeignKey(v => v.AssetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UploadSession>(session =>
        {
            session.ToTable("UploadSessions", Schema);
            session.Ignore(s => s.ChunkCount);
            session.Property(s => s.FileName).HasMaxLength(255);
            session.Property(s => s.Sha256).HasMaxLength(64);
            session.Property(s => s.State).HasMaxLength(20);
            session.Property(s => s.Error).HasMaxLength(1000);
            session.Property(s => s.DuplicateOf).HasColumnType("jsonb");
            session.HasIndex(s => s.ExpiresAt);
            session.HasIndex(s => new { s.UserId, s.State });
        });

        modelBuilder.Entity<UploadChunk>(chunk =>
        {
            chunk.ToTable("UploadChunks", Schema);
            chunk.HasKey(c => new { c.SessionId, c.Index });
            chunk.HasOne<UploadSession>().WithMany().HasForeignKey(c => c.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrphanBlob>(blob =>
        {
            blob.ToTable("OrphanBlobs", Schema);
            blob.Property(b => b.Key).HasMaxLength(512);
        });
    }
}

internal static class FileTypeSeeds
{
    private const string Zip = "504B0304";
    private const string Ole = "D0CF11E0A1B11AE1";
    private const string Riff = "52494646";

    public static readonly FileType[] All =
    [
        Type("jpg", "image/jpeg", AssetTypeKeys.Image, "FFD8FF"),
        Type("jpeg", "image/jpeg", AssetTypeKeys.Image, "FFD8FF"),
        Type("png", "image/png", AssetTypeKeys.Image, "89504E470D0A1A0A"),
        Type("gif", "image/gif", AssetTypeKeys.Image, "474946383761", "474946383961"),
        Type("webp", "image/webp", AssetTypeKeys.Image, Riff),
        Type("tif", "image/tiff", AssetTypeKeys.Image, "49492A00", "4D4D002A"),
        Type("tiff", "image/tiff", AssetTypeKeys.Image, "49492A00", "4D4D002A"),
        Type("bmp", "image/bmp", AssetTypeKeys.Image, "424D"),
        Type("svg", "image/svg+xml", AssetTypeKeys.Image),
        Type("heic", "image/heic", AssetTypeKeys.Image),
        Type("psd", "image/vnd.adobe.photoshop", AssetTypeKeys.Image, "38425053"),
        Type("mp4", "video/mp4", AssetTypeKeys.Video),
        Type("mov", "video/quicktime", AssetTypeKeys.Video),
        Type("avi", "video/x-msvideo", AssetTypeKeys.Video, Riff),
        Type("webm", "video/webm", AssetTypeKeys.Video, "1A45DFA3"),
        Type("mkv", "video/x-matroska", AssetTypeKeys.Video, "1A45DFA3"),
        Type("mp3", "audio/mpeg", AssetTypeKeys.Audio),
        Type("wav", "audio/wav", AssetTypeKeys.Audio, Riff),
        Type("ogg", "audio/ogg", AssetTypeKeys.Audio, "4F676753"),
        Type("flac", "audio/flac", AssetTypeKeys.Audio, "664C6143"),
        Type("m4a", "audio/mp4", AssetTypeKeys.Audio),
        Type("pdf", "application/pdf", AssetTypeKeys.Document, "25504446"),
        Type("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", AssetTypeKeys.Document, Zip),
        Type("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", AssetTypeKeys.Document, Zip),
        Type("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", AssetTypeKeys.Document, Zip),
        Type("doc", "application/msword", AssetTypeKeys.Document, Ole),
        Type("xls", "application/vnd.ms-excel", AssetTypeKeys.Document, Ole),
        Type("ppt", "application/vnd.ms-powerpoint", AssetTypeKeys.Document, Ole),
        Type("txt", "text/plain", AssetTypeKeys.Document),
        Type("csv", "text/csv", AssetTypeKeys.Document),
        Type("md", "text/markdown", AssetTypeKeys.Document),
        Type("json", "application/json", AssetTypeKeys.Document),
        Type("zip", "application/zip", AssetTypeKeys.Archive, Zip),
        Type("7z", "application/x-7z-compressed", AssetTypeKeys.Archive, "377ABCAF271C"),
        Type("rar", "application/vnd.rar", AssetTypeKeys.Archive, "526172211A07"),
        Type("gz", "application/gzip", AssetTypeKeys.Archive, "1F8B"),
        Type("tar", "application/x-tar", AssetTypeKeys.Archive),
        .. new[] { "exe", "dll", "bat", "cmd", "com", "msi", "scr", "ps1", "vbs", "vbe", "js", "jse", "wsf", "wsh", "jar", "sh", "cpl", "hta", "lnk", "url", "msc", "pif", "reg", "inf" }
            .Select(extension => new FileType { Extension = extension, MimeType = "application/octet-stream", AssetTypeKey = AssetTypeKeys.Other, ForbiddenForUpload = true }),
    ];

    private static FileType Type(string extension, string mimeType, string assetTypeKey, params string[] magicNumbers) =>
        new() { Extension = extension, MimeType = mimeType, AssetTypeKey = assetTypeKey, MagicNumbers = [.. magicNumbers] };
}
