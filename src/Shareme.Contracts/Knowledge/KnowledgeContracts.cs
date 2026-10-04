namespace Shareme.Contracts.Knowledge;

/// <summary>A new page under a folder or workspace, written in Markdown.</summary>
public sealed record PageRequest(Guid ParentId, string Title, string Markdown);

/// <summary><see cref="ExpectedVersion"/> is the version the edit started from; a newer one makes the save fail (409).</summary>
public sealed record PageUpdateRequest(string Title, string Markdown, int ExpectedVersion, string? Comment = null);

/// <summary><see cref="Html"/> is rendered from <see cref="Markdown"/> on the server and safe to show.</summary>
public sealed record PageDto(Guid Id, Guid? ParentId, string Title, string Markdown, string Html, int Version, DateTimeOffset ModifiedAt, Guid? ModifiedBy, bool CanEdit);

public sealed record PagePreviewRequest(string Markdown);

/// <summary>The HTML a save would store, for previews.</summary>
public sealed record PagePreviewDto(string Html);

public sealed record PageVersionDto(int Number, string Title, string? Comment, Guid? CreatedBy, DateTimeOffset CreatedAt);

public sealed record PageVersionContentDto(int Number, string Title, string Markdown, string Html, string? Comment, DateTimeOffset CreatedAt);

public static class DiffKinds
{
    public const string Same = "same";
    public const string Added = "added";
    public const string Removed = "removed";
}

public sealed record DiffLineDto(string Kind, string Text);

/// <summary>News under a folder or workspace, shown from <see cref="PublishAt"/> (now when empty) until <see cref="ExpiresAt"/>.</summary>
public sealed record NewsRequest(Guid ParentId, string Title, string Markdown, DateTimeOffset? PublishAt = null, DateTimeOffset? ExpiresAt = null, bool Pinned = false);

public sealed record NewsUpdateRequest(string Title, string Markdown, DateTimeOffset PublishAt, DateTimeOffset? ExpiresAt, bool Pinned);

public sealed record NewsDto(Guid Id, Guid? ParentId, string Title, string Markdown, string Html, DateTimeOffset PublishAt, DateTimeOffset? ExpiresAt, bool Pinned, Guid? AuthorId, bool CanEdit);

/// <summary>A news item in the feed: the beginning of its text instead of the whole of it.</summary>
public sealed record NewsSummaryDto(Guid Id, string Title, string Teaser, DateTimeOffset PublishAt, bool Pinned);

public static class HomeWidgets
{
    public const string News = "news";
    public const string Favorites = "favorites";
    public const string Recent = "recent";
    public const string Tasks = "tasks";
    public const string Due = "due";
    public const string Search = "search";

    public static readonly IReadOnlyList<string> All = [Search, News, Favorites, Tasks, Due, Recent];
}

/// <summary>The widgets of the start page in their order; <see cref="IsDefault"/> when it is the tenant's (or the built-in) layout.</summary>
public sealed record HomeLayoutDto(IReadOnlyList<string> Widgets, bool IsDefault);

public sealed record HomeLayoutRequest(IReadOnlyList<string> Widgets);

/// <summary>A task of the current user for the start page.</summary>
public sealed record HomeTaskDto(Guid Id, Guid WorkspaceId, string WorkspaceName, string Title, DateOnly? DueDate);

/// <summary>A member of the tenant as the people directory shows them.</summary>
/// <summary><see cref="AvatarVersion"/> changes with each picture, so its URL can be cached for long.</summary>
public sealed record PersonDto(Guid UserId, string DisplayName, string? Email, string? Department, string? Position, string? Phone, string? Location, string? About, bool HasAvatar, string? AvatarVersion = null);

public static class PeoplePermissions
{
    /// <summary>See the people directory with contact details.</summary>
    public const string View = "People.View";
}

public sealed record ProfileRequest(string? Department = null, string? Position = null, string? Phone = null, string? Location = null, string? About = null);

/// <summary>Everything matching a term: nodes from the search index (as far as the user may see them) and people.</summary>
public sealed record GlobalSearchDto(IReadOnlyList<Shareme.Contracts.Graph.NodeDto> Nodes, long NodeTotal, IReadOnlyList<PersonDto> People);
