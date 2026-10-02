namespace Shareme.Contracts.Assets;

public static class FileNamingCasings
{
    public const string None = "none";
    public const string Lower = "lower";
    public const string Upper = "upper";

    public static readonly IReadOnlyList<string> All = [None, Lower, Upper];
}

/// <summary>Tokens of a file naming format; the extension is always added, <c>{meta:key}</c> takes the first value of a property.</summary>
public static class FileNamingTokens
{
    public static readonly IReadOnlyList<string> Variables = ["name", "ext", "seq", "date", "year", "month", "day", "type", "kind"];

    public const string MetaPrefix = "meta";

    public const string DefaultFormat = "{name}";
}

/// <summary>A rule for the names of downloaded files; the default one of an organisation applies when a download names none.</summary>
public sealed record FileNamingDto(Guid Id, string Name, string Format, string Casing, bool IsDefault);

public sealed record FileNamingRequest(string Name, string Format, string Casing = FileNamingCasings.None, bool IsDefault = false);

public sealed record FileNamingPreviewRequest(string Format, IReadOnlyList<Guid> AssetIds, string Casing = FileNamingCasings.None);

public static class DownloadKinds
{
    public const string Original = "original";
}

/// <summary>Several assets (or every asset below a folder, with its subfolders) as one zip, either as originals or as one rendition kind.</summary>
public sealed record DownloadPackageRequest(IReadOnlyList<Guid>? AssetIds = null, Guid? FolderId = null, string Kind = DownloadKinds.Original, Guid? NamingId = null);

/// <summary>A link that streams the zip and expires; <see cref="Skipped"/> counts assets left out (no right, no content or rendition).</summary>
public sealed record DownloadPackageDto(string Url, DateTimeOffset ExpiresAt, int Count, int Skipped);

public static class ExtractionStates
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>Unpacks a zip; <see cref="KeepFolders"/> turns its directories into folders, otherwise all files land in the target.</summary>
public sealed record ExtractArchiveRequest(Guid? TargetFolderId = null, bool KeepFolders = true);

public sealed record ArchiveExtractionDto(Guid Id, Guid ArchiveId, Guid TargetFolderId, string State, int Extracted, int Filtered, int Failed, string? Error, IReadOnlyList<string> Problems);

/// <summary>A PDF with the thumbnails and names of several assets (or those below a folder).</summary>
public sealed record ContactSheetRequest(IReadOnlyList<Guid>? AssetIds = null, Guid? FolderId = null, string? Title = null);
