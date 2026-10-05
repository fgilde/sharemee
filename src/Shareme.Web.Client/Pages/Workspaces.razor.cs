using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Workspaces;
using Shareme.Web.Client.Workspaces;

namespace Shareme.Web.Client.Pages;

public partial class Workspaces
{
    [Inject] private IWorkspacesApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<WorkspaceDto> _workspaces = [];
    private IReadOnlyList<WorkspaceDto> _available = [];
    private bool _templates;
    private string? _name;
    private string _kind = WorkspaceKinds.Collaboration;
    private Guid? _templateId;

    protected override Task OnInitializedAsync() => LoadAsync();

    private Task TabAsync(int index)
    {
        _templates = index == 1;
        _templateId = null;
        return LoadAsync();
    }

    // templates are listed to everyone who may create workspaces, but opened only by their members
    private void Open(WorkspaceDto workspace)
    {
        if (workspace.MyRole is not null || !workspace.IsTemplate)
        {
            Nav.NavigateTo($"/workspaces/{workspace.Id}");
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            _workspaces = await Api.GetAsync(_templates);
            _available = _templates ? [] : await Api.GetAsync(templates: true);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private async Task CreateAsync()
    {
        try
        {
            var created = await Api.CreateAsync(new CreateWorkspaceRequest(_name!, _kind, _templateId, _templates));
            Nav.NavigateTo($"/workspaces/{created.Id}");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    internal static string Icon(string kind) => kind switch
    {
        WorkspaceKinds.Upload => Icons.Material.Outlined.CloudUpload,
        WorkspaceKinds.Collection => Icons.Material.Outlined.CollectionsBookmark,
        _ => Icons.Material.Outlined.Groups,
    };
}
