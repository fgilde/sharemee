using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Workspaces;

namespace Shareme.Web.Client.Workspaces;

public partial class TaskBoard
{
    [Inject] private IWorkspacesApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private MudDropContainer<TaskDto>? _container;
    private IReadOnlyList<StateDto> _states = [];
    private List<TaskDto> _tasks = [];
    private string? _title;
    private TaskDto? _editing;
    private string? _editTitle, _editDescription;
    private DateTime? _editDue;
    private IReadOnlyCollection<Guid> _editAssignees = [];

    [Parameter, EditorRequired] public Guid WorkspaceId { get; set; }

    [Parameter] public IReadOnlyList<WorkspaceMemberDto> Members { get; set; } = [];

    /// <summary>Owners may use owner-only columns.</summary>
    [Parameter] public bool IsOwner { get; set; }

    private Guid _loaded;
    private bool _reloadPending;

    protected override Task OnParametersSetAsync()
    {
        if (_loaded == WorkspaceId)
        {
            return Task.CompletedTask;
        }

        _loaded = WorkspaceId;
        return LoadAsync();
    }

    /// <summary>A move renumbers a column and sends one event per card; they are answered with one reload.</summary>
    private async Task ReloadSoonAsync()
    {
        if (_reloadPending)
        {
            return;
        }

        _reloadPending = true;
        await Task.Delay(300);
        _reloadPending = false;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _states = await Api.GetStatesAsync(WorkspaceId);
            _tasks = (await Api.GetTasksAsync(WorkspaceId)).OrderBy(t => t.SortIndex).ToList();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }

        await InvokeAsync(StateHasChanged);
        _container?.Refresh();
    }

    /// <summary>The same checks as the server, so forbidden drops are not offered.</summary>
    internal bool CanMove(TaskDto task, Guid target)
    {
        if (task.StateId == target)
        {
            return true;
        }

        var from = _states.FirstOrDefault(s => s.Id == task.StateId);
        var to = _states.FirstOrDefault(s => s.Id == target);
        return to is not null && (from is null || from.AllowedTargets.Count == 0 || from.AllowedTargets.Contains(target)) && (!to.OwnersOnly || IsOwner);
    }

    private async Task DroppedAsync(MudItemDropInfo<TaskDto> drop)
    {
        if (drop.Item is not { } task)
        {
            return;
        }

        try
        {
            await Api.MoveTaskAsync(task.Id, new MoveTaskRequest(Guid.Parse(drop.DropzoneIdentifier), drop.IndexInZone, task.Version));
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }

        await LoadAsync();
    }

    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(_title))
        {
            return;
        }

        try
        {
            await Api.CreateTaskAsync(WorkspaceId, new TaskRequest(_title.Trim()));
            _title = null;
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }

        await LoadAsync();
    }

    private void Edit(TaskDto task)
    {
        _editing = task;
        (_editTitle, _editDescription, _editDue, _editAssignees) = (task.Title, task.Description, task.DueDate?.ToDateTime(TimeOnly.MinValue), task.AssigneeIds.ToList());
    }

    private async Task SaveAsync(TaskDto task)
    {
        try
        {
            await Api.UpdateTaskAsync(task.Id, new TaskRequest(_editTitle ?? task.Title, _editDescription, _editDue is { } due ? DateOnly.FromDateTime(due) : null, null, _editAssignees.ToList()));
            _editing = null;
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }

        await LoadAsync();
    }

    private string MemberName(Guid id) => Members.FirstOrDefault(m => m.UserId == id) is { } member ? (string.IsNullOrWhiteSpace(member.Name) ? member.Email : member.Name!) : "former member";

    private string Initials(Guid id) => string.Concat(MemberName(id).Split(' ', '@', '.').Where(p => p.Length > 0).Take(2).Select(p => char.ToUpperInvariant(p[0])));
}
