namespace Shareme.Contracts.Knowledge;

/// <summary>A new page under a folder or workspace, written in Markdown.</summary>
public sealed record PageRequest(Guid ParentId, string Title, string Markdown);

/// <summary><see cref="ExpectedVersion"/> is the version the edit started from; a newer one makes the save fail (409).</summary>
public sealed record PageUpdateRequest(string Title, string Markdown, int ExpectedVersion, string? Comment = null);

/// <summary><see cref="Html"/> is rendered from <see cref="Markdown"/> on the server and safe to show.</summary>
public sealed record PageDto(Guid Id, Guid? ParentId, string Title, string Markdown, string Html, int Version, DateTimeOffset ModifiedAt, Guid? ModifiedBy, bool CanEdit);

public sealed record PageVersionDto(int Number, string Title, string? Comment, Guid? CreatedBy, DateTimeOffset CreatedAt);

public sealed record PageVersionContentDto(int Number, string Title, string Markdown, string Html, string? Comment, DateTimeOffset CreatedAt);

public static class DiffKinds
{
    public const string Same = "same";
    public const string Added = "added";
    public const string Removed = "removed";
}

public sealed record DiffLineDto(string Kind, string Text);
