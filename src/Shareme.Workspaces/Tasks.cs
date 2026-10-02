using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Domain;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Workspaces;
using Shareme.Graph;

namespace Shareme.Workspaces;

/// <summary>A column of a workspace's board; viewers of the workspace are told when it changes.</summary>
[NotAudited]
[Realtime(NodePermissions.Read, ResourceType = NodePermissions.ResourceType)]
public sealed class WorkflowState : Entity, IMultiTenant, IHasRealtimeTopics
{
    public Guid WorkspaceId { get; set; }

    public required string Name { get; set; }

    public int SortIndex { get; set; }

    public bool IsDefault { get; set; }

    public bool IsDone { get; set; }

    public bool OwnersOnly { get; set; }

    public List<Guid> AllowedTargets { get; set; } = [];

    public Guid TenantId { get; set; }

    public IEnumerable<string> RealtimeTopics => [GraphTopics.Children(WorkspaceId)];
}

/// <summary>Task domain events for notifications (P4c).</summary>
public sealed record TaskMoved(Guid TaskId, Guid WorkspaceId, Guid? FromStateId, Guid ToStateId) : IDomainEvent;

public sealed record TaskAssigned(Guid TaskId, Guid WorkspaceId, IReadOnlyList<Guid> UserIds) : IDomainEvent;

public static class TaskChangeKinds
{
    /// <summary>A task moved on the board (column or place); tree moves are NodeChangeKinds.Moved.</summary>
    public const string StateChanged = "state changed";
}

internal static class WorkflowStates
{
    public static readonly (string Name, bool IsDefault, bool IsDone)[] Defaults = [("To do", true, false), ("In progress", false, false), ("Done", false, true)];

    /// <summary>The default columns of a new workspace, or a copy of its template's columns with their transitions.</summary>
    public static async Task AddAsync(CoworkeeDbContext db, Guid workspaceId, Guid? templateId, CancellationToken cancellationToken)
    {
        var source = templateId is { } id ? await db.Set<WorkflowState>().AsNoTracking().Where(s => s.WorkspaceId == id).OrderBy(s => s.SortIndex).ToListAsync(cancellationToken) : [];
        if (source.Count == 0)
        {
            db.AddRange(Defaults.Select((d, index) => new WorkflowState { WorkspaceId = workspaceId, Name = d.Name, SortIndex = index, IsDefault = d.IsDefault, IsDone = d.IsDone }));
            return;
        }

        var copies = source.ToDictionary(s => s.Id, s => new WorkflowState { WorkspaceId = workspaceId, Name = s.Name, SortIndex = s.SortIndex, IsDefault = s.IsDefault, IsDone = s.IsDone, OwnersOnly = s.OwnersOnly });
        foreach (var state in source)
        {
            copies[state.Id].AllowedTargets = state.AllowedTargets.Where(copies.ContainsKey).Select(t => copies[t].Id).ToList();
        }

        db.AddRange(copies.Values);
    }

    public static StateDto Map(WorkflowState s) => new(s.Id, s.Name, s.SortIndex, s.IsDefault, s.IsDone, s.OwnersOnly, s.AllowedTargets);
}

public sealed record GetStates(Guid Id) : WorkspaceRequest(Id, NodePermissions.Read), IQuery<Result<IReadOnlyList<StateDto>>>;

public sealed record SaveState(Guid Id, Guid? StateId, StateRequest Request) : WorkspaceRequest(Id, NodePermissions.ManagePermissions), ICommand<Result<StateDto>>;

public sealed record DeleteState(Guid Id, Guid StateId) : WorkspaceRequest(Id, NodePermissions.ManagePermissions), ICommand<Result>;

public sealed record OrderStates(Guid Id, IReadOnlyList<Guid> StateIds) : WorkspaceRequest(Id, NodePermissions.ManagePermissions), ICommand<Result>;

public sealed record GetTasks(Guid Id) : WorkspaceRequest(Id, NodePermissions.Read), IQuery<Result<IReadOnlyList<TaskDto>>>;

public sealed record CreateTask(Guid Id, TaskRequest Request) : WorkspaceRequest(Id, NodePermissions.Create), ICommand<Result<TaskDto>>;

/// <summary>Changes a task; the id is the task's, so its own permissions apply.</summary>
public sealed record UpdateTask(Guid Id, TaskRequest Request) : WorkspaceRequest(Id, NodePermissions.Update), ICommand<Result<TaskDto>>;

public sealed record MoveTask(Guid Id, MoveTaskRequest Request) : WorkspaceRequest(Id, NodePermissions.Update), ICommand<Result<TaskDto>>;

public sealed record LinkTaskAssets(Guid Id, LinkAssetsRequest Request) : WorkspaceRequest(Id, NodePermissions.Update), ICommand<Result<TaskDto>>;

internal sealed class TaskHandlers(CoworkeeDbContext db, NodeAccess access, WorkspaceRoleStore roles, TimeProvider clock)
    : IHandler<GetStates, Result<IReadOnlyList<StateDto>>>,
      IHandler<SaveState, Result<StateDto>>,
      IHandler<DeleteState, Result>,
      IHandler<OrderStates, Result>,
      IHandler<GetTasks, Result<IReadOnlyList<TaskDto>>>,
      IHandler<CreateTask, Result<TaskDto>>,
      IHandler<UpdateTask, Result<TaskDto>>,
      IHandler<MoveTask, Result<TaskDto>>,
      IHandler<LinkTaskAssets, Result<TaskDto>>
{
    private static readonly Error WorkspaceNotFound = Error.NotFound("workspaces.not_found", "The workspace does not exist.");
    private static readonly Error TaskNotFound = Error.NotFound("tasks.not_found", "The task does not exist.");
    private static readonly Error StateNotFound = Error.NotFound("tasks.state_not_found", "The state does not exist.");

    public async Task<Result<IReadOnlyList<StateDto>>> HandleAsync(GetStates query, CancellationToken cancellationToken)
    {
        IReadOnlyList<StateDto> states = (await db.Set<WorkflowState>().AsNoTracking().Where(s => s.WorkspaceId == query.Id).OrderBy(s => s.SortIndex).ToListAsync(cancellationToken))
            .Select(WorkflowStates.Map).ToList();
        return Result<IReadOnlyList<StateDto>>.Success(states);
    }

    public async Task<Result<StateDto>> HandleAsync(SaveState command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
        {
            return Error.Validation("Name", "A name of up to 100 characters is required.");
        }

        if (await TemplateProblemAsync(command.Id, cancellationToken) is { } problem)
        {
            return problem;
        }

        var states = await db.Set<WorkflowState>().Where(s => s.WorkspaceId == command.Id).ToListAsync(cancellationToken);
        var targets = request.AllowedTargets?.Distinct().ToList() ?? [];
        if (targets.Any(t => states.All(s => s.Id != t)))
        {
            return Error.Validation("AllowedTargets", "Transitions lead to states of the same workspace.");
        }

        WorkflowState state;
        if (command.StateId is { } stateId)
        {
            if (states.FirstOrDefault(s => s.Id == stateId) is not { } existing)
            {
                return StateNotFound;
            }

            state = existing;
        }
        else
        {
            if (states.Count >= 30)
            {
                return Error.Validation("Name", "A board holds up to 30 states.");
            }

            state = new WorkflowState { WorkspaceId = command.Id, Name = string.Empty, SortIndex = states.Count == 0 ? 0 : states.Max(s => s.SortIndex) + 1 };
            db.Add(state);
            states.Add(state);
        }

        if (state.IsDone != request.IsDone)
        {
            // tasks already in the column follow its done flag
            foreach (var task in await db.Set<WorkTask>().Where(t => t.ParentId == command.Id && t.StateId == state.Id).ToListAsync(cancellationToken))
            {
                task.CompletedAt = request.IsDone ? clock.GetUtcNow() : null;
            }
        }

        state.Name = request.Name.Trim();
        state.IsDone = request.IsDone;
        state.OwnersOnly = request.OwnersOnly;

        // an empty list means "any state"; a list naming only the state itself means "none"
        var others = targets.Where(t => t != state.Id).ToList();
        state.AllowedTargets = targets.Count > 0 && others.Count == 0 ? [state.Id] : others;

        // exactly one column takes new tasks
        if (request.IsDefault || !states.Any(s => s.IsDefault && s != state))
        {
            states.ForEach(s => s.IsDefault = s == state);
        }
        else
        {
            state.IsDefault = false;
        }

        return WorkflowStates.Map(state);
    }

    public async Task<Result> HandleAsync(DeleteState command, CancellationToken cancellationToken)
    {
        if (await TemplateProblemAsync(command.Id, cancellationToken) is { } problem)
        {
            return problem;
        }

        var states = await db.Set<WorkflowState>().Where(s => s.WorkspaceId == command.Id).ToListAsync(cancellationToken);
        if (states.FirstOrDefault(s => s.Id == command.StateId) is not { } state)
        {
            return StateNotFound;
        }

        if (states.Count == 1)
        {
            return Error.Conflict("tasks.last_state", "A board keeps at least one state.");
        }

        // its tasks go to the default column (or the first other one)
        var fallback = states.Where(s => s != state).OrderByDescending(s => s.IsDefault).ThenBy(s => s.SortIndex).First();
        // tasks in the recycle bin too, so they come back into a column that exists
        var tasks = await db.Set<WorkTask>().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]).Where(t => t.ParentId == command.Id && t.StateId == state.Id).ToListAsync(cancellationToken);
        var next = await db.Set<WorkTask>().Where(t => t.ParentId == command.Id && t.StateId == fallback.Id).Select(t => (int?)t.SortIndex).MaxAsync(cancellationToken) + 1 ?? 0;
        foreach (var task in tasks)
        {
            task.StateId = fallback.Id;
            task.SortIndex = next++;
            task.CompletedAt = fallback.IsDone ? task.CompletedAt ?? clock.GetUtcNow() : null;
            task.Publish(new TaskMoved(task.Id, command.Id, state.Id, fallback.Id));
            task.MarkChanged(TaskChangeKinds.StateChanged);
        }

        foreach (var other in states.Where(s => s != state && s.AllowedTargets.Contains(state.Id)))
        {
            // a column whose only way out was the deleted one keeps having no way out
            var remaining = other.AllowedTargets.Where(t => t != state.Id).ToList();
            other.AllowedTargets = remaining.Count == 0 ? [other.Id] : remaining;
        }

        fallback.IsDefault |= state.IsDefault;
        db.Remove(state);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(OrderStates command, CancellationToken cancellationToken)
    {
        if (await TemplateProblemAsync(command.Id, cancellationToken) is { } problem)
        {
            return problem;
        }

        var states = await db.Set<WorkflowState>().Where(s => s.WorkspaceId == command.Id).ToListAsync(cancellationToken);
        if (command.StateIds.Count != states.Count || states.Any(s => !command.StateIds.Contains(s.Id)))
        {
            return Error.Validation("StateIds", "Name every state of the workspace once.");
        }

        foreach (var state in states)
        {
            state.SortIndex = command.StateIds.ToList().IndexOf(state.Id);
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<TaskDto>>> HandleAsync(GetTasks query, CancellationToken cancellationToken)
    {
        var tasks = await db.Set<WorkTask>().AsNoTracking().Where(t => t.ParentId == query.Id).OrderBy(t => t.SortIndex).ToListAsync(cancellationToken);
        var visible = await Shareme.Assets.AssetSelection.WithPermissionAsync(db, access, tasks, NodePermissions.Read, cancellationToken);
        var links = await LinksAsync(visible.Select(t => t.Id).ToList(), cancellationToken);
        var states = await db.Set<WorkflowState>().AsNoTracking().Where(s => s.WorkspaceId == query.Id).ToListAsync(cancellationToken);
        var fallback = states.FirstOrDefault(s => s.IsDefault)?.Id ?? states.MinBy(s => s.SortIndex)?.Id;

        // a task whose column is gone (a race with deleting it) shows in the default column instead of nowhere
        IReadOnlyList<TaskDto> dtos = visible.Select(t => Map(t, links) with { StateId = states.Any(s => s.Id == t.StateId) ? t.StateId : fallback }).ToList();
        return Result<IReadOnlyList<TaskDto>>.Success(dtos);
    }

    public async Task<Result<TaskDto>> HandleAsync(CreateTask command, CancellationToken cancellationToken)
    {
        if (await db.Set<Workspace>().SingleOrDefaultAsync(w => w.Id == command.Id && !w.IsTemplate, cancellationToken) is not { } workspace)
        {
            return WorkspaceNotFound;
        }

        if (workspace.WorkspaceKind != WorkspaceKinds.Collaboration)
        {
            return Error.Validation("Id", "Tasks belong to collaboration workspaces.");
        }

        // members start in the default column; owners may start anywhere
        if (command.Request.StateId is not null && !await access.CanAsync(NodePermissions.ManagePermissions, workspace.Id, cancellationToken))
        {
            command = command with { Request = command.Request with { StateId = null } };
        }

        if (await ValidateAsync(workspace.Id, command.Request, cancellationToken) is { } problem)
        {
            return problem;
        }

        var states = await db.Set<WorkflowState>().AsNoTracking().Where(s => s.WorkspaceId == workspace.Id).ToListAsync(cancellationToken);
        if ((command.Request.StateId is { } wanted ? states.FirstOrDefault(s => s.Id == wanted) : states.FirstOrDefault(s => s.IsDefault) ?? states.MinBy(s => s.SortIndex)) is not { } state)
        {
            return Error.Validation("StateId", "The workspace has no such state.");
        }

        if (state.OwnersOnly && !await access.CanAsync(NodePermissions.ManagePermissions, workspace.Id, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "Only owners put tasks into this state.");
        }

        var task = new WorkTask
        {
            Name = command.Request.Title.Trim(),
            NodeTypeId = NodeType.TaskTypeId,
            StateId = state.Id,
            SortIndex = await db.Set<WorkTask>().Where(t => t.ParentId == workspace.Id && t.StateId == state.Id).Select(t => (int?)t.SortIndex).MaxAsync(cancellationToken) + 1 ?? 0,
            CompletedAt = state.IsDone ? clock.GetUtcNow() : null,
        };
        Apply(task, command.Request);
        task.PlaceUnder(workspace);
        task.MarkChanged("created");
        if (task.AssigneeIds.Count > 0)
        {
            task.Publish(new TaskAssigned(task.Id, workspace.Id, task.AssigneeIds));
        }

        db.Add(task);

        // saved here, so the answer carries the version the next move needs
        await db.SaveChangesAsync(cancellationToken);
        return Map(task, []);
    }

    public async Task<Result<TaskDto>> HandleAsync(UpdateTask command, CancellationToken cancellationToken)
    {
        if (await db.Set<WorkTask>().SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken) is not { ParentId: { } workspaceId } task)
        {
            return TaskNotFound;
        }

        // the state changes through a move, which checks the transitions; people assigned before may have left meanwhile
        var before = task.AssigneeIds.ToList();
        if (await ValidateAsync(workspaceId, command.Request with { StateId = null }, cancellationToken, keep: before) is { } problem)
        {
            return problem;
        }

        task.Name = command.Request.Title.Trim();
        Apply(task, command.Request);
        if (task.AssigneeIds.Except(before).ToList() is { Count: > 0 } added)
        {
            task.Publish(new TaskAssigned(task.Id, workspaceId, added));
        }

        task.MarkChanged("updated");
        await db.SaveChangesAsync(cancellationToken);
        return Map(task, await LinksAsync([task.Id], cancellationToken));
    }

    public async Task<Result<TaskDto>> HandleAsync(MoveTask command, CancellationToken cancellationToken)
    {
        if (await db.Set<WorkTask>().SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken) is not { ParentId: { } workspaceId } task)
        {
            return TaskNotFound;
        }

        if (task.Version != command.Request.Version)
        {
            return Error.Conflict("tasks.changed", "Someone else changed this task; reload the board.");
        }

        var states = await db.Set<WorkflowState>().AsNoTracking().Where(s => s.WorkspaceId == workspaceId).ToListAsync(cancellationToken);
        if (states.FirstOrDefault(s => s.Id == command.Request.StateId) is not { } target)
        {
            return StateNotFound;
        }

        var from = states.FirstOrDefault(s => s.Id == task.StateId);
        if (from is not null && from.Id != target.Id && from.AllowedTargets.Count > 0 && !from.AllowedTargets.Contains(target.Id))
        {
            return Error.Conflict("tasks.transition", $"Tasks in '{from.Name}' cannot move to '{target.Name}'.");
        }

        if (target.OwnersOnly && from?.Id != target.Id && !await access.CanAsync(NodePermissions.ManagePermissions, workspaceId, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", $"Only owners move tasks to '{target.Name}'.");
        }

        // the column is renumbered with the task at its new place; only rows whose position changes are written
        var column = await db.Set<WorkTask>().Where(t => t.ParentId == workspaceId && t.StateId == target.Id && t.Id != task.Id).OrderBy(t => t.SortIndex).ToListAsync(cancellationToken);
        var position = Math.Clamp(command.Request.Index, 0, column.Count);
        if (from?.Id == target.Id && column.Count(t => t.SortIndex < task.SortIndex) == position)
        {
            return Map(task, await LinksAsync([task.Id], cancellationToken));
        }

        column.Insert(position, task);
        for (var index = 0; index < column.Count; index++)
        {
            column[index].SortIndex = index;
        }

        if (from?.Id != target.Id)
        {
            task.StateId = target.Id;
            task.CompletedAt = target.IsDone ? clock.GetUtcNow() : null;
            task.Publish(new TaskMoved(task.Id, workspaceId, from?.Id, target.Id));
        }

        task.MarkChanged(TaskChangeKinds.StateChanged);
        await db.SaveChangesAsync(cancellationToken);
        return Map(task, await LinksAsync([task.Id], cancellationToken));
    }

    public async Task<Result<TaskDto>> HandleAsync(LinkTaskAssets command, CancellationToken cancellationToken)
    {
        if (await db.Set<WorkTask>().SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken) is not { } task)
        {
            return TaskNotFound;
        }

        var ids = command.Request.AssetIds.Distinct().ToList();
        if (ids.Count is 0 or > 100)
        {
            return Error.Validation("AssetIds", "Link 1 to 100 assets at once.");
        }

        var assets = await db.Set<Asset>().AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
        if ((await Shareme.Assets.AssetSelection.WithPermissionAsync(db, access, assets, NodePermissions.Read, cancellationToken)).Count != ids.Count)
        {
            return Error.Validation("AssetIds", "Some assets do not exist or are not visible to you.");
        }

        var already = await db.Set<Edge>().Where(e => e.EdgeTypeId == EdgeType.LinkId && e.FromNodeId == task.Id && ids.Contains(e.ToNodeId)).Select(e => e.ToNodeId).ToListAsync(cancellationToken);
        db.AddRange(ids.Except(already).Select(id => new Edge { EdgeTypeId = EdgeType.LinkId, FromNodeId = task.Id, ToNodeId = id }));
        task.MarkChanged("updated");
        return Map(task, new Dictionary<Guid, List<Guid>> { [task.Id] = [.. already, .. ids.Except(already)] });
    }

    /// <summary>Title, description length and assignees, who must be members of the workspace.</summary>
    private async Task<Error?> ValidateAsync(Guid workspaceId, TaskRequest request, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? keep = null)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 400)
        {
            return Error.Validation("Title", "A title of up to 400 characters is required.");
        }

        if (request.Description is { Length: > 8000 })
        {
            return Error.Validation("Description", "Up to 8000 characters.");
        }

        var assignees = request.AssigneeIds?.Distinct().ToList() ?? [];
        if (assignees.Count > 20)
        {
            return Error.Validation("AssigneeIds", "Up to 20 people per task.");
        }

        if (assignees.Count > 0)
        {
            // ponytail: members through a direct grant; members through a group cannot be assigned yet
            var roleIds = (await roles.KeysAsync(cancellationToken)).Keys.ToList();
            var members = await db.Set<ResourcePermission>().Where(p => p.ResourceType == NodePermissions.ResourceType && p.ResourceId == workspaceId
                && p.PrincipalType == PrincipalType.User && roleIds.Contains(p.RoleId)).Select(p => p.PrincipalId).ToListAsync(cancellationToken);
            if (assignees.Any(a => !members.Contains(a) && keep?.Contains(a) != true))
            {
                return Error.Validation("AssigneeIds", "Assign members of the workspace only.");
            }
        }

        return null;
    }

    /// <summary>Columns of templates are part of the template: changed by template managers only.</summary>
    private async Task<Error?> TemplateProblemAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (await db.Set<Workspace>().AsNoTracking().Where(w => w.Id == workspaceId).Select(w => (bool?)w.IsTemplate).SingleOrDefaultAsync(cancellationToken) is not { } isTemplate)
        {
            return WorkspaceNotFound;
        }

        return isTemplate && !await access.IsGlobalAsync(WorkspacePermissions.ManageTemplates, cancellationToken)
            ? Error.Forbidden("auth.forbidden", "You may not change templates.")
            : null;
    }

    private static void Apply(WorkTask task, TaskRequest request)
    {
        task.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        task.DueDate = request.DueDate;
        task.AssigneeIds = request.AssigneeIds?.Distinct().ToList() ?? [];
    }

    private async Task<Dictionary<Guid, List<Guid>>> LinksAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        (await db.Set<Edge>().AsNoTracking().Where(e => e.EdgeTypeId == EdgeType.LinkId && taskIds.Contains(e.FromNodeId)).Select(e => new { e.FromNodeId, e.ToNodeId }).ToListAsync(cancellationToken))
            .GroupBy(e => e.FromNodeId).ToDictionary(g => g.Key, g => g.Select(e => e.ToNodeId).ToList());

    private static TaskDto Map(WorkTask t, Dictionary<Guid, List<Guid>> links) => new(
        t.Id, t.ParentId ?? Guid.Empty, t.Name, t.Description, t.DueDate, t.StateId, t.AssigneeIds, t.SortIndex, t.Version, t.CreatedAt, t.CompletedAt,
        links.TryGetValue(t.Id, out var linked) ? linked : []);
}
