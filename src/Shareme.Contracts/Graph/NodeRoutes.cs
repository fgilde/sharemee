using Shareme.Contracts.Assets;

namespace Shareme.Contracts.Graph;

/// <summary>Where a node opens in the app and its Material icon name, by its semantic.</summary>
public static class NodeRoutes
{
    public static string For(NodeDto node) => node.Semantic switch
    {
        "asset" => $"/assets/{node.Id}",
        "page" => $"/pages/{node.Id}",
        "news" => $"/news/{node.Id}",
        "workspace" => $"/workspaces/{node.Id}",
        "task" => $"/workspaces/{node.ParentId}",
        _ => $"/browse/{node.Id}",
    };

    public static string Icon(string semantic) => semantic switch
    {
        "asset" => "InsertDriveFile",
        "page" => "Article",
        "news" => "Newspaper",
        "workspace" => "Workspaces",
        "task" => "TaskAlt",
        _ => "Folder",
    };

    /// <summary>The thumbnail of a node with one (<see cref="NodeDto.Thumbnail"/> is its version tag).</summary>
    public static string? Thumbnail(NodeDto node) =>
        node.Thumbnail is { } tag ? $"/api/v1/assets/{node.Id}/derivations/{DerivationKinds.Thumbnail}?v={tag}" : null;
}
