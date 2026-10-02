using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Workspaces;
using Shareme.Graph;

namespace Shareme.Workspaces;

/// <summary>A comment on a node; viewers of the node's page are told when comments change.</summary>
[Realtime(NodePermissions.Read, ResourceType = NodePermissions.ResourceType)]
public sealed class Comment : AuditedAggregateRoot, IMultiTenant, IHasRealtimeTopics
{
    public void Publish(IDomainEvent domainEvent) => Raise(domainEvent);

    public Guid NodeId { get; set; }

    public Guid AuthorId { get; set; }

    public required string Text { get; set; }

    public List<Guid> MentionIds { get; set; } = [];

    public DateTimeOffset? EditedAt { get; set; }

    public Guid TenantId { get; set; }

    public IEnumerable<string> RealtimeTopics => [GraphTopics.Children(NodeId)];
}

public sealed record CommentAdded(Guid CommentId, Guid NodeId, IReadOnlyList<Guid> MentionIds) : IDomainEvent;

public sealed record GetComments(Guid Id) : WorkspaceRequest(Id, NodePermissions.Read), IQuery<Result<IReadOnlyList<CommentDto>>>;

/// <summary>Commenting changes the conversation of a node, so it needs the update right on it.</summary>
public sealed record AddComment(Guid Id, CommentRequest Request) : WorkspaceRequest(Id, NodePermissions.Update), ICommand<Result<CommentDto>>;

public sealed record EditComment(Guid CommentId, CommentRequest Request) : ICommand<Result<CommentDto>>;

public sealed record DeleteComment(Guid CommentId) : ICommand<Result>;

internal sealed class CommentHandlers(CoworkeeDbContext db, ICurrentUser currentUser, NodeAccess access, WorkspaceRoleStore roles, Coworkee.Core.Security.IUserDirectory users, TimeProvider clock)
    : IHandler<GetComments, Result<IReadOnlyList<CommentDto>>>,
      IHandler<AddComment, Result<CommentDto>>,
      IHandler<EditComment, Result<CommentDto>>,
      IHandler<DeleteComment, Result>
{
    public const int MaxLength = 8000;

    private static readonly Error NotFound = Error.NotFound("comments.not_found", "The comment does not exist.");

    public async Task<Result<IReadOnlyList<CommentDto>>> HandleAsync(GetComments query, CancellationToken cancellationToken)
    {
        var comments = await db.Set<Comment>().AsNoTracking().Where(c => c.NodeId == query.Id).OrderBy(c => c.CreatedAt).Take(500).ToListAsync(cancellationToken);
        var names = await users.GetDisplayNamesAsync(comments.Select(c => c.AuthorId).Distinct().ToList(), cancellationToken);
        var moderator = await access.CanAsync(NodePermissions.ManagePermissions, query.Id, cancellationToken);
        IReadOnlyList<CommentDto> dtos = comments.Select(c => Map(c, names, moderator)).ToList();
        return Result<IReadOnlyList<CommentDto>>.Success(dtos);
    }

    public async Task<Result<CommentDto>> HandleAsync(AddComment command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Forbidden("auth.forbidden", "Comments are written by users.");
        }

        if (await db.Set<Node>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == command.Id, cancellationToken) is not { } node)
        {
            return Error.NotFound("nodes.not_found", "The node does not exist.");
        }

        if (await ValidateAsync(node, command.Request, cancellationToken) is { } problem)
        {
            return problem;
        }

        var comment = new Comment { NodeId = node.Id, AuthorId = userId, Text = command.Request.Text.Trim(), MentionIds = Mentions(command.Request) };
        comment.Publish(new CommentAdded(comment.Id, node.Id, comment.MentionIds));
        db.Add(comment);
        return Map(comment, await users.GetDisplayNamesAsync([userId], cancellationToken), moderator: false);
    }

    public async Task<Result<CommentDto>> HandleAsync(EditComment command, CancellationToken cancellationToken)
    {
        if (await EditableAsync(command.CommentId, authorOnly: true, cancellationToken) is not { } comment)
        {
            return NotFound;
        }

        var node = await db.Set<Node>().AsNoTracking().SingleAsync(n => n.Id == comment.NodeId, cancellationToken);
        if (await ValidateAsync(node, command.Request, cancellationToken) is { } problem)
        {
            return problem;
        }

        // people mentioned for the first time are told; the others were told already
        var mentions = Mentions(command.Request);
        var added = mentions.Except(comment.MentionIds).ToList();
        comment.Text = command.Request.Text.Trim();
        comment.MentionIds = mentions;
        comment.EditedAt = clock.GetUtcNow();
        if (added.Count > 0)
        {
            comment.Publish(new CommentAdded(comment.Id, comment.NodeId, added));
        }

        return Map(comment, await users.GetDisplayNamesAsync([comment.AuthorId], cancellationToken), moderator: false);
    }

    public async Task<Result> HandleAsync(DeleteComment command, CancellationToken cancellationToken)
    {
        if (await EditableAsync(command.CommentId, authorOnly: false, cancellationToken) is not { } comment)
        {
            return NotFound;
        }

        db.Remove(comment);
        return Result.Success();
    }

    /// <summary>The caller's own comment, or (for deleting) any comment on a node the caller moderates; comments on nodes the caller cannot read are missing.</summary>
    private async Task<Comment?> EditableAsync(Guid id, bool authorOnly, CancellationToken cancellationToken)
    {
        if (await db.Set<Comment>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken) is not { } comment
            || !await access.CanAsync(NodePermissions.Read, comment.NodeId, cancellationToken))
        {
            return null;
        }

        return comment.AuthorId == currentUser.UserId || (!authorOnly && await access.CanAsync(NodePermissions.ManagePermissions, comment.NodeId, cancellationToken)) ? comment : null;
    }

    /// <summary>Text length; mentions only of members of the node's workspace (outside workspaces nobody can be mentioned).</summary>
    private async Task<Error?> ValidateAsync(Node node, CommentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaxLength)
        {
            return Error.Validation("Text", $"A comment of up to {MaxLength} characters is required.");
        }

        var mentions = Mentions(request);
        if (mentions.Count == 0)
        {
            return null;
        }

        if (mentions.Count > 20)
        {
            return Error.Validation("MentionIds", "Mention up to 20 people.");
        }

        var workspaceId = node.PathIds[0];
        var roleIds = (await roles.KeysAsync(cancellationToken)).Keys.ToList();
        var members = await db.Set<ResourcePermission>().AsNoTracking()
            .Where(p => p.ResourceType == NodePermissions.ResourceType && p.ResourceId == workspaceId && p.PrincipalType == PrincipalType.User && roleIds.Contains(p.RoleId))
            .Select(p => p.PrincipalId).ToListAsync(cancellationToken);
        return mentions.All(members.Contains) ? null : Error.Validation("MentionIds", "Mention members of the workspace only.");
    }

    private static List<Guid> Mentions(CommentRequest request) => request.MentionIds?.Distinct().ToList() ?? [];

    private CommentDto Map(Comment c, IReadOnlyDictionary<Guid, string> names, bool moderator) => new(
        c.Id, c.NodeId, c.AuthorId, names.GetValueOrDefault(c.AuthorId) ?? "former member", c.Text, c.MentionIds, c.CreatedAt, c.EditedAt,
        c.AuthorId == currentUser.UserId || moderator);
}

/// <summary>Tells the people concerned: mentioned members, assignees and new members get a notification of their own; watchers get the node's change events.</summary>
internal sealed class CollaborationNotifications(CoworkeeDbContext db, ICurrentUser currentUser, INotifier notifier)
    : IDomainEventHandler<CommentAdded>, IDomainEventHandler<TaskAssigned>, IDomainEventHandler<TaskMoved>
{
    public async Task HandleAsync(CommentAdded domainEvent, CancellationToken cancellationToken)
    {
        if (await db.Set<Node>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == domainEvent.NodeId, cancellationToken) is not { } node)
        {
            return;
        }

        var link = WorkspaceLinks.For(node);
        var mentioned = domainEvent.MentionIds.Where(id => id != currentUser.UserId).ToList();
        if (mentioned.Count > 0)
        {
            await notifier.NotifyAsync(mentioned, "comment.mention", $"You were mentioned on {Short(node.Name)}", null, link, cancellationToken);
        }

        // watchers of the node or anything above it, except the author and those mentioned
        var watched = node.PathIds;
        var watchers = await db.Set<NodeSubscription>().Where(s => watched.Contains(s.NodeId) && s.UserId != currentUser.UserId && !mentioned.Contains(s.UserId))
            .Select(s => s.UserId).Distinct().ToListAsync(cancellationToken);
        if (watchers.Count > 0)
        {
            await notifier.NotifyAsync(watchers, "comment", $"New comment on {Short(node.Name)}", null, link, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleAsync(TaskAssigned domainEvent, CancellationToken cancellationToken)
    {
        var assigned = domainEvent.UserIds.Where(id => id != currentUser.UserId).ToList();
        if (assigned.Count == 0 || await db.Set<WorkTask>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == domainEvent.TaskId, cancellationToken) is not { } task)
        {
            return;
        }

        await notifier.NotifyAsync(assigned, "task.assigned", $"You were assigned to {Short(task.Name)}", task.DueDate is { } due ? $"Due {due:yyyy-MM-dd}" : null,
            WorkspaceLinks.For(task), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleAsync(TaskMoved domainEvent, CancellationToken cancellationToken)
    {
        if (await db.Set<WorkTask>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == domainEvent.TaskId, cancellationToken) is not { } task
            || task.AssigneeIds.Where(id => id != currentUser.UserId).ToList() is not { Count: > 0 } assignees)
        {
            return;
        }

        var state = await db.Set<WorkflowState>().AsNoTracking().Where(s => s.Id == domainEvent.ToStateId).Select(s => s.Name).SingleOrDefaultAsync(cancellationToken);
        await notifier.NotifyAsync(assignees, "task.moved", $"{Short(task.Name)} is now in {state ?? "another state"}", null, WorkspaceLinks.For(task), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Short(string name) => name.Length > 120 ? name[..120] + "…" : name;
}

public static class WorkspaceLinks
{
    /// <summary>Where a notification about a node leads: workspaces and their tasks to the workspace page, assets to their page, others to the browser.</summary>
    public static string For(Node node) => node switch
    {
        Workspace => $"/workspaces/{node.Id}",
        WorkTask => $"/workspaces/{node.ParentId}",
        Asset => $"/assets/{node.Id}",
        _ => $"/browse/{node.Id}",
    };
}
