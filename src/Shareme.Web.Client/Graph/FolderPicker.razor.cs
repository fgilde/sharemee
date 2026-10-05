using Coworkee.Contracts;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Graph;

namespace Shareme.Web.Client.Graph;

public partial class FolderPicker
{
    [Inject] private ISharemeApi Api { get; set; } = null!;

    private NodeDetailDto? _current;
    private IReadOnlyList<NodeDto> _folders = [];
    private List<BreadcrumbItem> _crumbs = [];

    [Parameter, EditorRequired] public Guid StartId { get; set; }

    /// <summary>The node being moved; it and its subtree are no valid targets.</summary>
    [Parameter] public Guid? ExcludeId { get; set; }

    [Parameter] public EventCallback<Guid> OnPicked { get; set; }

    protected override async Task OnInitializedAsync() => await OpenAsync(StartId);

    private async Task OpenAsync(Guid id)
    {
        _current = await Api.GetNodeAsync(id);
        _crumbs = _current.Breadcrumb.Select(c => new BreadcrumbItem(c.Name, c.Id.ToString())).ToList();
        _folders = (await Api.GetFoldersAsync(id, new PageRequest(1, 500))).Items.Where(n => n.Id != ExcludeId).ToList();
    }
}
