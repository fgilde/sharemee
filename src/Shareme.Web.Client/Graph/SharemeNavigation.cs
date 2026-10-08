using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Navigation;
using MudBlazor;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Drm;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;

namespace Shareme.Web.Client.Graph;

internal sealed class SharemeNavigation : INavigationContributor
{
    private const string Content = "Content";
    private const string Collaboration = "Collaboration";
    private const string Licensing = "Licensing";
    private const string Admin = NavigationGroups.Administration;

    public static void Order(NavigationMenuOptions menu) => menu
        .OrderGroup(Content, 0)
        .OrderGroup(Collaboration, 1)
        .OrderGroup(Licensing, 2)
        .OrderGroup(Admin, 3)
        .IconForGroup(Content, Icons.Material.Outlined.FolderOpen)
        .IconForGroup(Collaboration, Icons.Material.Outlined.Groups)
        .IconForGroup(Licensing, Icons.Material.Outlined.ShoppingCart);

    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Browse", "/browse", Icons.Material.Outlined.FolderOpen, Group: Content),
        new("Quick search", "/find", Icons.Material.Outlined.TravelExplore, Group: Content, Order: 1),
        new("File search", "/search", Icons.Material.Outlined.Search, Group: Content, Order: 2),
        new("My background tasks", "/jobs", Icons.Material.Outlined.PlaylistPlay, Group: Content, Order: 3),
        new("Recycle bin", "/recycle-bin", Icons.Material.Outlined.Delete, Group: Content, Order: 4),
        new("Workspaces", "/workspaces", Icons.Material.Outlined.Workspaces, Group: Collaboration),
        new("People", "/people", Icons.Material.Outlined.People, Group: Collaboration, Order: 1),
        new("License shop", "/licenses", Icons.Material.Outlined.ShoppingCart, DrmPermissions.Offers, Group: Licensing),
        new("My purchases", "/purchases", Icons.Material.Outlined.Receipt, Group: Licensing, Order: 1),
        new("Metadata", "/admin/metadata", Icons.Material.Outlined.Label, MetadataPermissions.Manage, Group: Admin, Order: -100),
        new("Item types", "/admin/node-types", Icons.Material.Outlined.Category, NodePermissions.Admin, Group: Admin, Order: -99),
        new("Rules", "/admin/rules", Icons.Material.Outlined.Rule, AutomationPermissions.ManageRules, Group: Admin, Order: -98),
        new("AI tagging", "/admin/ai", Icons.Material.Outlined.AutoAwesome, AiPermissions.Run, Group: Admin, Order: -97),
        new("Previews & formats", "/admin/renditions", Icons.Material.Outlined.PhotoSizeSelectLarge, NodePermissions.Admin, Group: Admin, Order: -96),
        new("Restrictions", "/admin/restrictions", Icons.Material.Outlined.Block, NodePermissions.Admin, Group: Admin, Order: -95),
        new("License management", "/admin/drm", Icons.Material.Outlined.Gavel, DrmPermissions.Manage, Group: Admin, Order: -94),
    ];
}
