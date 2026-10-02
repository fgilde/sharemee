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
    public const string Failed = "failed";
}

public static class UploadStates
{
    public const string Open = "open";
    public const string Completing = "completing";
    public const string Completed = "completed";
    public const string Failed = "failed";
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

/// <summary>An upload; after complete it moves to completing and then to completed with <see cref="Result"/> or to failed with <see cref="Error"/>.</summary>
public sealed record UploadSessionDto(
    Guid Id, int ChunkSize, int ChunkCount, IReadOnlyList<int> Received, DateTimeOffset ExpiresAt, string State, Guid? AssetId = null, UploadResultDto? Result = null, string? Error = null);

public sealed record UploadResultDto(Guid AssetId, Guid VersionId, int VersionNumber, IReadOnlyList<Guid> DuplicateOf);

public sealed record AssetVersionDto(Guid Id, int Number, string FileName, long Size, string Sha256, string MimeType, string? Extension, DateTimeOffset CreatedAt, Guid? CreatedBy);

public static class DerivationKinds
{
    public const string Thumbnail = "thumbnail";
    public const string Preview = "preview";

    public static readonly IReadOnlyList<string> All = [Thumbnail, Preview];
}

public static class DerivationStates
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string NotSupported = "notsupported";
}

public sealed record DerivationDto(string Kind, string State, int? Width, int? Height);

public sealed record AssetDto(
    Guid Id, Guid? FolderId, string Name, string AssetType, string Status, AssetVersionDto? CurrentVersion, IReadOnlyList<AssetVersionDto> Versions, IReadOnlyList<DerivationDto> Derivations);

public sealed record DownloadLinkDto(string Url, DateTimeOffset ExpiresAt);
