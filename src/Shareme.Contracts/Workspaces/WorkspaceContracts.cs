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
