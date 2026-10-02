using Shareme.Contracts.Graph;

namespace Shareme.Contracts.Workspaces;

public static class WorkspaceKinds
{
    /// <summary>Files are uploaded into the workspace and finalised into a folder.</summary>
    public const string Upload = "upload";

    /// <summary>Links to assets that live elsewhere.</summary>
    public const string Collection = "collection";

    /// <summary>Both, plus tasks and comments.</summary>
    public const string Collaboration = "collaboration";

    public static readonly IReadOnlyList<string> All = [Upload, Collection, Collaboration];
}

public static class WorkspacePermissions
{
    public const string GroupName = "Workspaces";
    public const string Create = "Workspaces.Create";
    public const string ManageTemplates = "Workspaces.Templates";

    /// <summary>Granted per workspace (owners have it): move its files into the target folder.</summary>
    public const string Finalize = "Workspaces.Finalize";
}

public static class WorkspaceRoles
{
    public const string Owner = "owner";
    public const string Collaborator = "collaborator";

    public static readonly IReadOnlyList<string> All = [Owner, Collaborator];
}

public sealed record WorkspaceDto(
    Guid Id, string Name, string Kind, bool IsTemplate, Guid? TemplateId, Guid? FinalizationFolderId, bool CopyMetadataToAssets, Guid? RequiredSetId,
    DateTimeOffset? FinalizedAt, DateTimeOffset CreatedAt, int ItemCount, string? MyRole, bool CanManage = false, bool CanFinalize = false);

public sealed record CreateWorkspaceRequest(
    string Name, string Kind = WorkspaceKinds.Collaboration, Guid? TemplateId = null, bool IsTemplate = false, Guid? FinalizationFolderId = null,
    bool CopyMetadataToAssets = false, Guid? RequiredSetId = null);

public sealed record UpdateWorkspaceRequest(string Name, Guid? FinalizationFolderId = null, bool CopyMetadataToAssets = false, Guid? RequiredSetId = null);

public sealed record WorkspaceMemberDto(Guid UserId, string Email, string? Name, string Role);

public sealed record AddWorkspaceMemberRequest(Guid UserId, string Role = WorkspaceRoles.Collaborator);

public sealed record LinkAssetsRequest(IReadOnlyList<Guid> AssetIds);

/// <summary>An asset or other node of the workspace; linked ones live elsewhere and are only shown to members who may read them.</summary>
public sealed record WorkspaceItemDto(NodeDto Node, bool IsLinked);

public sealed record FinalizeResultDto(int Moved, Guid FolderId);

/// <summary>A column of a workspace's board; <see cref="AllowedTargets"/> lists the states tasks may move on to (empty: any), owners-only columns take tasks only from owners.</summary>
public sealed record StateDto(Guid Id, string Name, int SortIndex, bool IsDefault, bool IsDone, bool OwnersOnly, IReadOnlyList<Guid> AllowedTargets);

public sealed record StateRequest(string Name, bool IsDefault = false, bool IsDone = false, bool OwnersOnly = false, IReadOnlyList<Guid>? AllowedTargets = null);

public sealed record TaskDto(
    Guid Id, Guid WorkspaceId, string Title, string? Description, DateOnly? DueDate, Guid? StateId, IReadOnlyList<Guid> AssigneeIds, int SortIndex, uint Version,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, IReadOnlyList<Guid> LinkedAssetIds);

public sealed record TaskRequest(string Title, string? Description = null, DateOnly? DueDate = null, Guid? StateId = null, IReadOnlyList<Guid>? AssigneeIds = null);

/// <summary>Moves a task into a state at a position; <see cref="Version"/> is the task version the client saw, so concurrent moves are noticed.</summary>
public sealed record MoveTaskRequest(Guid StateId, int Index, uint Version);

/// <summary>A comment on a workspace, task or asset; <see cref="MentionIds"/> are people who are told about it.</summary>
public sealed record CommentDto(Guid Id, Guid NodeId, Guid AuthorId, string AuthorName, string Text, IReadOnlyList<Guid> MentionIds, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, bool CanEdit);

public sealed record CommentRequest(string Text, IReadOnlyList<Guid>? MentionIds = null);
