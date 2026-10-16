using Shareme.Contracts.Graph;

namespace Shareme.Web.Client.Knowledge;

/// <summary>Where a node opens and which icon it shows, by its semantic.</summary>
public static class NodeLinks
{
    public static string For(NodeDto node) => NodeRoutes.For(node);

    public static string Icon(string semantic) => semantic switch
    {
        "asset" => MudBlazor.Icons.Material.Outlined.InsertDriveFile,
        "page" => MudBlazor.Icons.Material.Outlined.Article,
        "news" => MudBlazor.Icons.Material.Outlined.Newspaper,
        "workspace" => MudBlazor.Icons.Material.Outlined.Workspaces,
        "task" => MudBlazor.Icons.Material.Outlined.TaskAlt,
        _ => MudBlazor.Icons.Material.Outlined.Folder,
    };
}
