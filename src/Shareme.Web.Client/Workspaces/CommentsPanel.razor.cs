using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Workspaces;

namespace Shareme.Web.Client.Workspaces;

public partial class CommentsPanel
{
    [Inject] private IWorkspacesApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<CommentDto> _comments = [];
    private string? _text;
    private IReadOnlyCollection<Guid> _mentions = [];
    private Guid _loaded;

    [Parameter, EditorRequired] public Guid NodeId { get; set; }

    /// <summary>Members who may be mentioned (inside workspaces).</summary>
    [Parameter] public IReadOnlyList<WorkspaceMemberDto> Members { get; set; } = [];

    protected override Task OnParametersSetAsync()
    {
        if (_loaded == NodeId)
        {
            return Task.CompletedTask;
        }

        _loaded = NodeId;
        return LoadAsync();
    }

    private bool _reloadPending;

    /// <summary>Workspace events (task moves) also arrive here; a burst of them is answered with one reload.</summary>
    private async Task ReloadSoonAsync()
    {
        if (_reloadPending)
        {
            return;
        }

        _reloadPending = true;
        await Task.Delay(500);
        _reloadPending = false;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _comments = await Api.GetCommentsAsync(NodeId);
        }
        catch (ApiException)
        {
            _comments = [];
        }

        await InvokeAsync(StateHasChanged);
    }

    private Task AddAsync() => RunAsync(async () =>
    {
        await Api.AddCommentAsync(NodeId, new CommentRequest(_text!.Trim(), _mentions.ToList()));
        (_text, _mentions) = (null, []);
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
            Snackbar.Add(exception.Status == 403 ? "You may not comment here." : exception.Message, Severity.Warning);
        }
    }

    private string MemberName(Guid id) => Members.FirstOrDefault(m => m.UserId == id) is { } member ? (string.IsNullOrWhiteSpace(member.Name) ? member.Email : member.Name!) : "someone";
}
