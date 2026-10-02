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
    public const string Model = "model";
    public const string Other = "other";
}

/// <summary>Starts an upload into a folder (new asset) or onto an asset (new version).</summary>
public sealed record StartUploadRequest(string FileName, long Size, Guid? TargetFolderId = null, Guid? AssetId = null, string? Sha256 = null);

/// <summary>An upload; after complete it moves to completing and then to completed with <see cref="Result"/> or to failed with <see cref="Error"/>.</summary>
public sealed record UploadSessionDto(
    Guid Id, int ChunkSize, int ChunkCount, IReadOnlyList<int> Received, DateTimeOffset ExpiresAt, string State, Guid? AssetId = null, UploadResultDto? Result = null, string? Error = null);

public sealed record UploadResultDto(Guid AssetId, Guid VersionId, int VersionNumber, IReadOnlyList<Guid> DuplicateOf);

public sealed record AssetVersionDto(
    Guid Id, int Number, string FileName, long Size, string Sha256, string MimeType, string? Extension, DateTimeOffset CreatedAt, Guid? CreatedBy,
    IReadOnlyDictionary<string, string>? TechnicalMetadata = null);

public static class DerivationKinds
{
    public const string Thumbnail = "thumbnail";
    public const string Preview = "preview";

    /// <summary>A playable copy of video (MP4) or audio (MP3).</summary>
    public const string Media = "media";

    /// <summary>A short looping GIF of a video's first seconds, for galleries.</summary>
    public const string Animated = "animated";

    /// <summary>Internal: the full size still of non image files (video frame, page), kept so further renditions skip the heavy tool.</summary>
    public const string Still = "still";

    /// <summary>Kinds owned by the system; custom derivation types use other keys.</summary>
    public static readonly IReadOnlySet<string> System = new HashSet<string> { Thumbnail, Preview, Media, Animated, Still };

    public static readonly IReadOnlyList<string> All = [Thumbnail, Preview, Media];

    /// <summary>The renditions planned for a file of this type.</summary>
    public static IReadOnlyList<string> For(string mimeType) =>
        mimeType.StartsWith("video/", StringComparison.Ordinal) ? [Thumbnail, Preview, Media, Animated]
        : mimeType.StartsWith("audio/", StringComparison.Ordinal) ? All
        : [Thumbnail, Preview];
}

public static class DerivationStates
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string NotSupported = "notsupported";
}

public sealed record DerivationDto(string Kind, string State, int? Width, int? Height, string? MimeType = null, long Size = 0, bool IsManual = false, string? Error = null);

/// <summary>An asset; <see cref="CanDownload"/> tells whether the caller may fetch and view the original.</summary>
public sealed record AssetDto(
    Guid Id, Guid? FolderId, string Name, string AssetType, string Status, AssetVersionDto? CurrentVersion, IReadOnlyList<AssetVersionDto> Versions, IReadOnlyList<DerivationDto> Derivations,
    string? RenditionTag = null, bool CanDownload = false);

/// <summary>Originals of these types may be shown inline (no script can run in them); everything else is only downloaded.</summary>
public static class InlineTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        "application/pdf", "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp", "video/mp4", "video/webm", "audio/mpeg", "audio/wav", "audio/ogg", "audio/flac", "audio/mp4",
        "text/plain", "text/csv",

        // meshes, shown by the MudEx 3D viewer
        "model/gltf-binary", "model/stl", "model/obj", "model/3mf", "model/ply",
    };
}

public static class DerivationFormats
{
    public const string WebP = "webp";
    public const string Jpeg = "jpeg";
    public const string Png = "png";

    public static readonly IReadOnlyList<string> All = [WebP, Jpeg, Png];

    public static string MimeType(string format) => format switch { Jpeg => "image/jpeg", Png => "image/png", _ => "image/webp" };

    public static string Extension(string? mimeType) => mimeType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "video/mp4" => ".mp4",
        "audio/mpeg" => ".mp3",
        "image/gif" => ".gif",
        _ => ".webp",
    };
}

/// <summary>A part of the picture as fractions (0 to 1) of the upright image the preview shows.</summary>
public sealed record CropArea(double Left, double Top, double Width, double Height)
{
    public bool IsValid =>
        double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height)
        && Left >= 0 && Top >= 0 && Width > 0 && Height > 0 && Left + Width <= 1.000001 && Top + Height <= 1.000001;
}

/// <summary>A cropped copy as a new asset next to the source, linked as its variant and with its metadata.</summary>
public sealed record CropVariantRequest(CropArea Area, string? Name = null);

public sealed record CropVariantDto(Guid AssetId);

/// <summary>A rendition an organisation defines: rendered for every matching asset (default), on request, or uploaded by hand.</summary>
public sealed record DerivationTypeDto(
    Guid Id, string Key, string Name, int Width, int Height, string Format, int Quality, bool IsDefault, bool IsOnDemand, bool AllowManualUpload, Guid? WatermarkId,
    IReadOnlyList<string> AssetTypeKeys);

public sealed record DerivationTypeRequest(
    string Key, string Name, int Width, int Height, string Format = DerivationFormats.WebP, int Quality = 85, bool IsDefault = false, bool IsOnDemand = true,
    bool AllowManualUpload = false, Guid? WatermarkId = null, IReadOnlyList<string>? AssetTypeKeys = null);

public static class WatermarkPositions
{
    public static readonly IReadOnlyList<string> All = ["center", "northwest", "north", "northeast", "west", "east", "southwest", "south", "southeast"];
}

/// <summary>An overlay image for renditions; with a trigger it only applies to assets whose property holds the value (a list value key or "true").</summary>
public sealed record WatermarkDto(Guid Id, string Name, bool HasImage, string Position, int Opacity, int ScalePercent, string? TriggerProperty, string? TriggerValue);

public sealed record WatermarkRequest(string Name, string Position = "southeast", int Opacity = 50, int ScalePercent = 25, string? TriggerProperty = null, string? TriggerValue = null);

public sealed record DownloadLinkDto(string Url, DateTimeOffset ExpiresAt);
