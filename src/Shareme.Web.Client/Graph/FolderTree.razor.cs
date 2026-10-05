using Coworkee.Contracts;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Graph;

namespace Shareme.Web.Client.Graph;

public partial class FolderTree
{
    [Inject] private ISharemeApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    private List<TreeItemData<Guid>> _roots = [];
    private readonly Dictionary<Guid, TreeItemData<Guid>> _items = [];
    private readonly List<Guid> _loaded = [];

    [Parameter] public Guid SelectedId { get; set; }

    protected override async Task OnInitializedAsync() =>
        _roots = (await Api.GetEntryPointsAsync()).Select(Item).ToList();

    private async Task<IReadOnlyCollection<TreeItemData<Guid>>> LoadAsync(Guid parentId)
    {
        var children = await FetchAsync(parentId);
        if (!_loaded.Contains(parentId))
        {
            // folders seen once stay current: creating, renaming, moving or deleting below them refreshes their branch
            _loaded.Add(parentId);
            await InvokeAsync(StateHasChanged);
        }

        return children;
    }

    private async Task RefreshAsync(Guid parentId)
    {
        if (_items.TryGetValue(parentId, out var item))
        {
            item.Children = await FetchAsync(parentId);
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task<List<TreeItemData<Guid>>> FetchAsync(Guid parentId) =>
        (await Api.GetFoldersAsync(parentId, new PageRequest(1, 500))).Items.Select(Item).ToList();

    private void Open(Guid id)
    {
        if (id != Guid.Empty && id != SelectedId)
        {
            Nav.NavigateTo($"/browse/{id}");
        }
    }

    private TreeItemData<Guid> Item(NodeDto node)
    {
        var item = _items.TryGetValue(node.Id, out var known) ? known : new TreeItemData<Guid> { Value = node.Id, Icon = Icons.Material.Outlined.Folder, Expandable = true };
        item.Text = node.Name;
        _items[node.Id] = item;
        return item;
    }
}
