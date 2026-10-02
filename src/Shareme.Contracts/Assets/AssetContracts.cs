namespace Shareme.Contracts.Assets;

public static class AssetPermissions
{
    public const string Download = "Assets.Download";
}

public static class AssetStatuses
{
    public const string Importing = "importing";
    public const string Ready = "ready";
    public const string Duplicate = "duplicate";
}

public static class AssetTypeKeys
{
    public const string Image = "image";
    public const string Video = "video";
    public const string Audio = "audio";
    public const string Document = "document";
    public const string Archive = "archive";
    public const string Other = "other";
}

/// <summary>Starts an upload into a folder (new asset) or onto an asset (new version).</summary>
public sealed record StartUploadRequest(string FileName, long Size, Guid? TargetFolderId = null, Guid? AssetId = null, string? Sha256 = null);

public sealed record UploadSessionDto(Guid Id, int ChunkSize, int ChunkCount, IReadOnlyList<int> Received, DateTimeOffset ExpiresAt);

public sealed record UploadResultDto(Guid AssetId, Guid VersionId, int VersionNumber, IReadOnlyList<Guid> DuplicateOf);

public sealed record AssetVersionDto(Guid Id, int Number, string FileName, long Size, string Sha256, string MimeType, string? Extension, DateTimeOffset CreatedAt, Guid? CreatedBy);

public sealed record AssetDto(Guid Id, Guid? FolderId, string Name, string AssetType, string Status, AssetVersionDto? CurrentVersion, IReadOnlyList<AssetVersionDto> Versions);

public sealed record DownloadLinkDto(string Url, DateTimeOffset ExpiresAt);
