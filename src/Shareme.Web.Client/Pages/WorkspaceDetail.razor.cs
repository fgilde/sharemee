using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Workspaces;
using Shareme.Web.Client.Graph;
using Shareme.Web.Client.Workspaces;

namespace Shareme.Web.Client.Pages;

public partial class WorkspaceDetail
{
    [Inject] private IWorkspacesApi Api { get; set; } = null!;

    [Inject] private ISharemeApi Graph { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private WorkspaceDto? _workspace;
    private IReadOnlyList<WorkspaceItemDto> _items = [];
    private IReadOnlyList<WorkspaceMemberDto> _members = [];
    private WorkspaceMemberDto? _candidate;
    private string _role = WorkspaceRoles.Collaborator;
    private string? _name, _folderName;
    private Guid? _folderId, _rootId;
    private bool _copyMetadata;
    private bool _subscribed;

    [Parameter] public Guid Id { get; set; }

    // by permission, so owners through a group and administrators get the same tools
    private bool IsOwner => _workspace?.CanManage == true;

    protected override async Task OnParametersSetAsync()
    {
        _workspace = null;
        await LoadAsync();
        if (_workspace is not null)
        {
            (_name, _folderId, _copyMetadata) = (_workspace.Name, _workspace.FinalizationFolderId, _workspace.CopyMetadataToAssets);
            try
            {
                _folderName = _folderId is { } folder ? (await Graph.GetNodeAsync(folder)).Node.Name : null;
            }
            catch (ApiException)
            {
                // a folder from a template the owner cannot read
                _folderName = "a folder you cannot see";
            }

            if (IsOwner)
            {
                try
                {
                    // the picker starts where the owner's folder access starts
                    _rootId = (await Graph.GetEntryPointsAsync()).FirstOrDefault()?.Id;
                }
                catch (ApiException)
                {
                    _rootId = null;
                }
            }
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            _workspace = await Api.GetAsync(Id);
            _subscribed = (await Graph.GetNodeAsync(Id)).IsSubscribed;
            _items = await Api.GetItemsAsync(Id);
            _members = await Api.GetMembersAsync(Id);
        }
        catch (ApiException exception) when (exception.Status is 403 or 404)
        {
            Snackbar.Add("This workspace is not available.", Severity.Warning);
            Nav.NavigateTo("/workspaces");
            return;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task<IEnumerable<WorkspaceMemberDto>> FindAsync(string? search, CancellationToken cancellationToken)
    {
        try
        {
            return await Api.FindCandidatesAsync(Id, search, cancellationToken);
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private Task ToggleWatchAsync() => RunAsync(() => Graph.SetSubscriptionAsync(Id, !_subscribed));

    private Task AddMemberAsync() => RunAsync(async () =>
    {
        await Api.AddMemberAsync(Id, new AddWorkspaceMemberRequest(_candidate!.UserId, _role));
        _candidate = null;
    });

    private async Task PickFolderAsync(Guid folder)
    {
        _folderId = folder;
        _folderName = (await Graph.GetNodeAsync(folder)).Node.Name;
    }

    private Task SaveAsync() => RunAsync(() => Api.UpdateAsync(Id, new UpdateWorkspaceRequest(_name ?? string.Empty, _folderId, _copyMetadata, _workspace!.RequiredSetId)));

    private Task FinalizeAsync() => RunAsync(async () =>
    {
        var result = await Api.FinalizeAsync(Id);
        Snackbar.Add($"{result.Moved} files moved to their folder.", Severity.Success);
    });

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You do not have permission for this." : exception.Message, Severity.Warning);
        }
    }
}
