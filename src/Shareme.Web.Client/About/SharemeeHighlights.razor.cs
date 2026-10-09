using MudBlazor;

namespace Shareme.Web.Client.About;

public partial class SharemeeHighlights
{
    private static readonly (string Icon, string Title, string Text)[] Highlights =
    [
        (Icons.Material.Outlined.PermMedia, "Assets", "Files with previews, formats, metadata and versions in one place."),
        (Icons.Material.Outlined.Workspaces, "Workspaces", "Teams share collections and work on them together."),
        (Icons.Material.Outlined.AutoStories, "Knowledge", "Pages, intranet and people next to the files they belong to."),
    ];
}
