using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Assets;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Contracts.Workspaces;
using Shareme.Graph;
using Shareme.Metadata;

namespace Shareme.Workspaces;

/// <summary>Workspaces: roots of their own that members reach through grants on them, with kinds, templates, collection links and finalisation into a folder.</summary>
[DependsOn(typeof(ShareMeAssetsModule))]
public sealed class ShareMeWorkspacesModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(ShareMeWorkspacesModule).Assembly);
        context.Services.AddSingleton<IPermissionDefinitionContributor, WorkspacePermissionDefinitions>();
        context.Services.AddScoped<WorkspaceRoleStore>();
        context.Services.AddSingleton<IModelContributor, WorkspaceModelContributor>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var workspaces = app.MapGroup("/api/v1/workspaces").WithTags("Workspaces").RequireAuthorization();
        workspaces.MapGet("/", (bool? templates, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetWorkspaces(templates == true), ct).ToHttpResult());
        workspaces.MapPost("/", (CreateWorkspaceRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateWorkspace(body), ct).ToHttpResult());
        workspaces.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetWorkspace(id), ct).ToHttpResult());
        workspaces.MapPut("/{id:guid}", (Guid id, UpdateWorkspaceRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateWorkspace(id, body), ct).ToHttpResult());
        workspaces.MapGet("/{id:guid}/members", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetWorkspaceMembers(id), ct).ToHttpResult());
        workspaces.MapGet("/{id:guid}/candidates", (Guid id, string? search, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetMemberCandidates(id, search), ct).ToHttpResult());
        workspaces.MapPost("/{id:guid}/members", (Guid id, AddWorkspaceMemberRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new AddWorkspaceMember(id, body), ct).ToHttpResult());
        workspaces.MapDelete("/{id:guid}/members/{userId:guid}", (Guid id, Guid userId, IDispatcher d, CancellationToken ct) => d.SendAsync(new RemoveWorkspaceMember(id, userId), ct).ToHttpResult());
        workspaces.MapGet("/{id:guid}/items", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetWorkspaceItems(id), ct).ToHttpResult());
        workspaces.MapPost("/{id:guid}/links", (Guid id, LinkAssetsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new LinkAssets(id, body), ct).ToHttpResult());
        workspaces.MapDelete("/{id:guid}/links/{assetId:guid}", (Guid id, Guid assetId, IDispatcher d, CancellationToken ct) => d.SendAsync(new UnlinkAsset(id, assetId), ct).ToHttpResult());
        workspaces.MapGet("/{id:guid}/states", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetStates(id), ct).ToHttpResult());
        workspaces.MapPost("/{id:guid}/states", (Guid id, StateRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveState(id, null, body), ct).ToHttpResult());
        workspaces.MapPut("/{id:guid}/states/{stateId:guid}", (Guid id, Guid stateId, StateRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveState(id, stateId, body), ct).ToHttpResult());
        workspaces.MapDelete("/{id:guid}/states/{stateId:guid}", (Guid id, Guid stateId, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteState(id, stateId), ct).ToHttpResult());
        workspaces.MapPut("/{id:guid}/states/order", (Guid id, Guid[] body, IDispatcher d, CancellationToken ct) => d.SendAsync(new OrderStates(id, body), ct).ToHttpResult());
        workspaces.MapGet("/{id:guid}/tasks", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetTasks(id), ct).ToHttpResult());
        workspaces.MapPost("/{id:guid}/tasks", (Guid id, TaskRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateTask(id, body), ct).ToHttpResult());

        var tasks = app.MapGroup("/api/v1/tasks").WithTags("Workspaces").RequireAuthorization();
        tasks.MapPut("/{id:guid}", (Guid id, TaskRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateTask(id, body), ct).ToHttpResult());
        tasks.MapPost("/{id:guid}/move", (Guid id, MoveTaskRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new MoveTask(id, body), ct).ToHttpResult());
        tasks.MapPost("/{id:guid}/links", (Guid id, LinkAssetsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new LinkTaskAssets(id, body), ct).ToHttpResult());
        workspaces.MapPost("/{id:guid}/finalize", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new FinalizeWorkspace(id), ct).ToHttpResult());
    }
}

internal sealed class WorkspacePermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(WorkspacePermissions.GroupName, "Workspaces")
            .Add(WorkspacePermissions.Create, "Create workspaces")
            .Add(WorkspacePermissions.ManageTemplates, "Create and edit workspace templates", WorkspacePermissions.Create)
            .Add(WorkspacePermissions.Finalize, "Finalise a workspace into its folder", NodePermissions.Read);
}

/// <summary>Which role of a tenant is the workspace owner or collaborator role; renaming the role keeps it.</summary>
public sealed class WorkspaceRoleBinding : Coworkee.Domain.Entity, Coworkee.Domain.IMultiTenant
{
    public required string Key { get; set; }

    public Guid RoleId { get; set; }

    public Guid TenantId { get; set; }
}

internal sealed class WorkspaceModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceRoleBinding>(binding =>
        {
            binding.ToTable("WorkspaceRoleBindings", "sm");
            binding.Property(b => b.Key).HasMaxLength(20);
            binding.HasIndex(b => new { b.TenantId, b.Key }).IsUnique();
        });

        modelBuilder.Entity<WorkflowState>(state =>
        {
            state.ToTable("WorkflowStates", "sm");
            state.Ignore(s => s.RealtimeTopics);
            state.Property(s => s.Name).HasMaxLength(100);
            state.Property(s => s.AllowedTargets).HasColumnType("jsonb");
            state.HasIndex(s => s.WorkspaceId);
            state.HasOne<Node>().WithMany().HasForeignKey(s => s.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>The owner and collaborator roles of a tenant, made on first use; admins may rename them or change their permissions like any role.</summary>
internal sealed class WorkspaceRoleStore(CoworkeeDbContext db, ICurrentUser currentUser)
{
    private static readonly Dictionary<string, (string Name, string[] Permissions)> Definitions = new()
    {
        [WorkspaceRoles.Owner] = ("Workspace owner",
            [NodePermissions.Read, NodePermissions.Create, NodePermissions.Update, NodePermissions.Delete, NodePermissions.ManagePermissions, AssetPermissions.Download, WorkspacePermissions.Finalize]),
        [WorkspaceRoles.Collaborator] = ("Workspace collaborator", [NodePermissions.Read, NodePermissions.Create, NodePermissions.Update, AssetPermissions.Download]),
    };

    private Dictionary<string, Guid>? _roles;

    /// <summary>The role, made if the tenant has none yet (for commands, which save).</summary>
    public async Task<Guid> RoleIdAsync(string role, CancellationToken cancellationToken) => (await RolesAsync(create: true, cancellationToken))[role];

    /// <summary>Role key per existing role id, for reading grants back; makes nothing.</summary>
    public async Task<Dictionary<Guid, string>> KeysAsync(CancellationToken cancellationToken) =>
        (await RolesAsync(create: false, cancellationToken)).ToDictionary(r => r.Value, r => r.Key);

    // ponytail: two first workspaces of a tenant at the same moment can race on the role's unique name; the loser gets an error and retries
    private async Task<Dictionary<string, Guid>> RolesAsync(bool create, CancellationToken cancellationToken)
    {
        if (_roles is not null && (!create || _roles.Count == Definitions.Count))
        {
            return _roles;
        }

        var tenantId = currentUser.TenantId ?? throw new InvalidOperationException("Workspaces belong to an organisation.");
        _roles = [];
        foreach (var (key, (name, permissions)) in Definitions)
        {
            var binding = db.Set<WorkspaceRoleBinding>().Local.FirstOrDefault(b => b.TenantId == tenantId && b.Key == key)
                ?? await db.Set<WorkspaceRoleBinding>().AsNoTracking().SingleOrDefaultAsync(b => b.TenantId == tenantId && b.Key == key, cancellationToken);
            if (binding is null && !create)
            {
                continue;
            }

            if (binding is null)
            {
                // a role an admin made with this name is not taken over: its permissions are unknown
                var taken = await db.Set<Role>().AnyAsync(r => r.TenantId == tenantId && r.NormalizedName == name.ToUpperInvariant(), cancellationToken);
                var roleName = taken ? name + " (workspaces)" : name;
                var role = new Role { Name = roleName, NormalizedName = roleName.ToUpperInvariant(), TenantId = tenantId, Description = "Given per workspace; change its permissions to change what members may do." };
                db.Add(role);
                db.AddRange(permissions.Select(p => new PermissionGrant { Name = p, ProviderType = PermissionProviderType.Role, ProviderKey = role.Id, TenantId = tenantId }));
                binding = new WorkspaceRoleBinding { Key = key, RoleId = role.Id, TenantId = tenantId };
                db.Add(binding);
            }

            _roles[key] = binding.RoleId;
        }

        return _roles;
    }
}

public sealed record GetWorkspaces(bool Templates) : IQuery<Result<IReadOnlyList<WorkspaceDto>>>;

[RequiresPermission(WorkspacePermissions.Create)]
public sealed record CreateWorkspace(CreateWorkspaceRequest Request) : ICommand<Result<WorkspaceDto>>;

public abstract record WorkspaceRequest(Guid Id, string RequiredPermission) : IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;
}

public sealed record GetWorkspace(Guid Id) : WorkspaceRequest(Id, NodePermissions.Read), IQuery<Result<WorkspaceDto>>;

public sealed record UpdateWorkspace(Guid Id, UpdateWorkspaceRequest Request) : WorkspaceRequest(Id, NodePermissions.ManagePermissions), ICommand<Result<WorkspaceDto>>;

public sealed record GetWorkspaceMembers(Guid Id) : WorkspaceRequest(Id, NodePermissions.Read), IQuery<Result<IReadOnlyList<WorkspaceMemberDto>>>;

/// <summary>Users of the organisation an owner may add, found by e-mail or name (at most 20).</summary>
public sealed record GetMemberCandidates(Guid Id, string? Search) : WorkspaceRequest(Id, NodePermissions.ManagePermissions), IQuery<Result<IReadOnlyList<WorkspaceMemberDto>>>;

public sealed record AddWorkspaceMember(Guid Id, AddWorkspaceMemberRequest Request) : WorkspaceRequest(Id, NodePermissions.ManagePermissions), ICommand<Result>;

public sealed record RemoveWorkspaceMember(Guid Id, Guid UserId) : WorkspaceRequest(Id, NodePermissions.ManagePermissions), ICommand<Result>;

public sealed record GetWorkspaceItems(Guid Id) : WorkspaceRequest(Id, NodePermissions.Read), IQuery<Result<IReadOnlyList<WorkspaceItemDto>>>;

public sealed record LinkAssets(Guid Id, LinkAssetsRequest Request) : WorkspaceRequest(Id, NodePermissions.Update), ICommand<Result>;

public sealed record UnlinkAsset(Guid Id, Guid AssetId) : WorkspaceRequest(Id, NodePermissions.Update), ICommand<Result>;

public sealed record FinalizeWorkspace(Guid Id) : WorkspaceRequest(Id, WorkspacePermissions.Finalize), ICommand<Result<FinalizeResultDto>>;

internal sealed class WorkspaceHandlers(CoworkeeDbContext db, ICurrentUser currentUser, NodeAccess access, NodeMapper mapper, WorkspaceRoleStore roles, TimeProvider clock)
    : IHandler<GetWorkspaces, Result<IReadOnlyList<WorkspaceDto>>>,
      IHandler<CreateWorkspace, Result<WorkspaceDto>>,
      IHandler<GetWorkspace, Result<WorkspaceDto>>,
      IHandler<UpdateWorkspace, Result<WorkspaceDto>>,
      IHandler<GetWorkspaceMembers, Result<IReadOnlyList<WorkspaceMemberDto>>>,
      IHandler<GetMemberCandidates, Result<IReadOnlyList<WorkspaceMemberDto>>>,
      IHandler<AddWorkspaceMember, Result>,
      IHandler<RemoveWorkspaceMember, Result>,
      IHandler<GetWorkspaceItems, Result<IReadOnlyList<WorkspaceItemDto>>>,
      IHandler<LinkAssets, Result>,
      IHandler<UnlinkAsset, Result>,
      IHandler<FinalizeWorkspace, Result<FinalizeResultDto>>
{
    private const int MaxLinks = 500;

    private static readonly Error NotFound = Error.NotFound("workspaces.not_found", "The workspace does not exist.");

    public async Task<Result<IReadOnlyList<WorkspaceDto>>> HandleAsync(GetWorkspaces query, CancellationToken cancellationToken)
    {
        var workspaces = db.Set<Workspace>().AsNoTracking().Where(w => w.IsTemplate == query.Templates);

        // templates are offered to everyone who may create workspaces; workspaces only to their members
        if (!(query.Templates && await access.IsGlobalAsync(WorkspacePermissions.Create, cancellationToken)) && !await access.IsGlobalAsync(NodePermissions.Read, cancellationToken))
        {
            var granted = (await access.GrantedNodesAsync(NodePermissions.Read, cancellationToken)).ToList();
            workspaces = workspaces.Where(w => granted.Contains(w.Id));
        }

        var list = await workspaces.OrderBy(w => w.Name).ToListAsync(cancellationToken);
        var myRoles = await MyRolesAsync(list.Select(w => w.Id).ToList(), cancellationToken);
        var ids = list.Select(w => w.Id).ToList();
        var counts = await db.Set<Node>().AsNoTracking().Where(n => n.ParentId != null && ids.Contains(n.ParentId.Value) && !(n is WorkTask)).GroupBy(n => n.ParentId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        IReadOnlyList<WorkspaceDto> dtos = list.Select(w => Map(w, counts.GetValueOrDefault(w.Id), myRoles.GetValueOrDefault(w.Id))).ToList();
        return Result<IReadOnlyList<WorkspaceDto>>.Success(dtos);
    }

    public async Task<Result<WorkspaceDto>> HandleAsync(CreateWorkspace command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Error.Validation("Name", "A name of up to 200 characters is required.");
        }

        if (request.IsTemplate && !await access.IsGlobalAsync(WorkspacePermissions.ManageTemplates, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "You may not create templates.");
        }

        if (currentUser is not { UserId: { } userId, TenantId: not null })
        {
            return Error.Forbidden("auth.forbidden", "Workspaces are created by users of an organisation.");
        }

        Workspace? template = null;
        if (request.TemplateId is { } templateId
            && (template = await db.Set<Workspace>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == templateId && w.IsTemplate, cancellationToken)) is null)
        {
            return Error.Validation("TemplateId", "The template does not exist.");
        }

        var kind = template?.WorkspaceKind ?? request.Kind;
        if (!WorkspaceKinds.All.Contains(kind))
        {
            return Error.Validation("Kind", $"Use one of: {string.Join(", ", WorkspaceKinds.All)}.");
        }

        var folderId = request.FinalizationFolderId ?? template?.FinalizationFolderId;
        if (request.FinalizationFolderId is { } chosen && await FolderProblemAsync(chosen, cancellationToken) is { } folderProblem)
        {
            return folderProblem;
        }

        if (await SetProblemAsync(request.RequiredSetId, cancellationToken) is { } setProblem)
        {
            return setProblem;
        }

        var workspace = new Workspace
        {
            Name = request.Name.Trim(),
            NodeTypeId = NodeType.WorkspaceTypeId,
            WorkspaceKind = kind,
            IsTemplate = request.IsTemplate,
            TemplateId = template?.Id,
            FinalizationFolderId = folderId,
            CopyMetadataToAssets = template?.CopyMetadataToAssets ?? request.CopyMetadataToAssets,
            RequiredSetId = request.RequiredSetId ?? template?.RequiredSetId,
            Metadata = template is null ? [] : template.Metadata.ToDictionary(p => p.Key, p => p.Value.ToList()),
        };
        workspace.PlaceUnder(null);
        workspace.MarkChanged("created");
        db.Add(workspace);
        await WorkflowStates.AddAsync(db, workspace.Id, template?.Id, cancellationToken);

        // the creator owns it; the collaborators of a template (users or groups) become collaborators of every workspace made from it, its owners do not
        var ownerRole = await roles.RoleIdAsync(WorkspaceRoles.Owner, cancellationToken);
        var collaboratorRole = await roles.RoleIdAsync(WorkspaceRoles.Collaborator, cancellationToken);
        db.Add(new ResourcePermission { ResourceType = NodePermissions.ResourceType, ResourceId = workspace.Id, PrincipalType = PrincipalType.User, PrincipalId = userId, RoleId = ownerRole });
        if (template is not null)
        {
            var copied = await db.Set<ResourcePermission>().AsNoTracking()
                .Where(p => p.ResourceType == NodePermissions.ResourceType && p.ResourceId == template.Id && p.RoleId == collaboratorRole
                    && !(p.PrincipalType == PrincipalType.User && p.PrincipalId == userId))
                .ToListAsync(cancellationToken);
            db.AddRange(copied.Select(p => new ResourcePermission
            {
                ResourceType = NodePermissions.ResourceType, ResourceId = workspace.Id, PrincipalType = p.PrincipalType, PrincipalId = p.PrincipalId, RoleId = p.RoleId,
            }));
        }

        return Map(workspace, 0, WorkspaceRoles.Owner);
    }

    public async Task<Result<WorkspaceDto>> HandleAsync(GetWorkspace query, CancellationToken cancellationToken)
    {
        if (await db.Set<Workspace>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == query.Id, cancellationToken) is not { } workspace)
        {
            return NotFound;
        }

        var (children, linked) = await VisibleItemsAsync(workspace.Id, cancellationToken);
        var count = children.Count + linked.Count;
        return Map(workspace, count, (await MyRolesAsync([workspace.Id], cancellationToken)).GetValueOrDefault(workspace.Id)) with
        {
            CanManage = await access.CanAsync(NodePermissions.ManagePermissions, workspace.Id, cancellationToken),
            CanFinalize = await access.CanAsync(WorkspacePermissions.Finalize, workspace.Id, cancellationToken),
        };
    }

    public async Task<Result<WorkspaceDto>> HandleAsync(UpdateWorkspace command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (await db.Set<Workspace>().SingleOrDefaultAsync(w => w.Id == command.Id, cancellationToken) is not { } workspace)
        {
            return NotFound;
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Error.Validation("Name", "A name of up to 200 characters is required.");
        }

        if (workspace.IsTemplate && !await access.IsGlobalAsync(WorkspacePermissions.ManageTemplates, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "You may not change templates.");
        }

        if (request.FinalizationFolderId is { } folder && folder != workspace.FinalizationFolderId && await FolderProblemAsync(folder, cancellationToken) is { } folderProblem)
        {
            return folderProblem;
        }

        if (request.RequiredSetId != workspace.RequiredSetId && await SetProblemAsync(request.RequiredSetId, cancellationToken) is { } setProblem)
        {
            return setProblem;
        }

        workspace.Name = request.Name.Trim();
        workspace.FinalizationFolderId = request.FinalizationFolderId;
        workspace.CopyMetadataToAssets = request.CopyMetadataToAssets;
        workspace.RequiredSetId = request.RequiredSetId;
        workspace.MarkChanged("updated");
        return Map(workspace, 0, (await MyRolesAsync([workspace.Id], cancellationToken)).GetValueOrDefault(workspace.Id));
    }

    public async Task<Result<IReadOnlyList<WorkspaceMemberDto>>> HandleAsync(GetWorkspaceMembers query, CancellationToken cancellationToken)
    {
        if (!await db.Set<Workspace>().AnyAsync(w => w.Id == query.Id, cancellationToken))
        {
            return NotFound;
        }

        var keys = await roles.KeysAsync(cancellationToken);
        var roleIds = keys.Keys.ToList();
        var members = await (from grant in db.Set<ResourcePermission>().AsNoTracking()
                             join user in db.Set<User>().AsNoTracking() on grant.PrincipalId equals user.Id
                             where grant.ResourceType == NodePermissions.ResourceType && grant.ResourceId == query.Id && grant.PrincipalType == PrincipalType.User
                                 && roleIds.Contains(grant.RoleId)
                             select new { user.Id, user.Email, user.FirstName, user.LastName, grant.RoleId }).ToListAsync(cancellationToken);

        // someone with both roles is listed once, as owner
        IReadOnlyList<WorkspaceMemberDto> list = members.GroupBy(m => m.Id)
            .Select(g => g.OrderBy(m => keys[m.RoleId] == WorkspaceRoles.Owner ? 0 : 1).First())
            .Select(m => new WorkspaceMemberDto(m.Id, m.Email ?? string.Empty, string.Join(' ', new[] { m.FirstName, m.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))), keys[m.RoleId]))
            .OrderBy(m => m.Role != WorkspaceRoles.Owner).ThenBy(m => m.Email).ToList();
        return Result<IReadOnlyList<WorkspaceMemberDto>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<WorkspaceMemberDto>>> HandleAsync(GetMemberCandidates query, CancellationToken cancellationToken)
    {
        // a search, not a directory: at least three characters
        if (query.Search is not { Length: >= 3 } search || !await db.Set<Workspace>().AnyAsync(w => w.Id == query.Id, cancellationToken))
        {
            return Result<IReadOnlyList<WorkspaceMemberDto>>.Success([]);
        }

        var users = db.Set<User>().AsNoTracking().Where(u => u.TenantId == currentUser.TenantId && u.IsActive);
        {
            var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            users = users.Where(u => EF.Functions.ILike(u.Email!, pattern) || EF.Functions.ILike(u.FirstName ?? string.Empty, pattern) || EF.Functions.ILike(u.LastName ?? string.Empty, pattern));
        }

        IReadOnlyList<WorkspaceMemberDto> found = (await users.OrderBy(u => u.Email).Take(20).Select(u => new { u.Id, u.Email, u.FirstName, u.LastName }).ToListAsync(cancellationToken))
            .Select(u => new WorkspaceMemberDto(u.Id, u.Email ?? string.Empty, string.Join(' ', new[] { u.FirstName, u.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))), string.Empty))
            .ToList();
        return Result<IReadOnlyList<WorkspaceMemberDto>>.Success(found);
    }

    public async Task<Result> HandleAsync(AddWorkspaceMember command, CancellationToken cancellationToken)
    {
        if (!WorkspaceRoles.All.Contains(command.Request.Role))
        {
            return Error.Validation("Role", $"Use one of: {string.Join(", ", WorkspaceRoles.All)}.");
        }

        if (!await db.Set<Workspace>().AnyAsync(w => w.Id == command.Id, cancellationToken))
        {
            return NotFound;
        }

        if (currentUser.TenantId is null || !await db.Set<User>().AnyAsync(u => u.Id == command.Request.UserId && u.TenantId == currentUser.TenantId, cancellationToken))
        {
            return Error.Validation("UserId", "The user does not exist.");
        }

        // one role per member: changing the role replaces the old grant
        var keys = await roles.KeysAsync(cancellationToken);
        var roleIds = keys.Keys.ToList();
        var existing = await db.Set<ResourcePermission>().Where(p => p.ResourceType == NodePermissions.ResourceType && p.ResourceId == command.Id
            && p.PrincipalType == PrincipalType.User && p.PrincipalId == command.Request.UserId && roleIds.Contains(p.RoleId)).ToListAsync(cancellationToken);
        var roleId = await roles.RoleIdAsync(command.Request.Role, cancellationToken);
        var ownerRole = await roles.RoleIdAsync(WorkspaceRoles.Owner, cancellationToken);
        if (roleId != ownerRole && existing.Any(p => p.RoleId == ownerRole)
            && !await db.Set<ResourcePermission>().AnyAsync(p => p.ResourceType == NodePermissions.ResourceType && p.ResourceId == command.Id && p.RoleId == ownerRole
                && !(p.PrincipalType == PrincipalType.User && p.PrincipalId == command.Request.UserId), cancellationToken))
        {
            return Error.Conflict("workspaces.last_owner", "Make someone else owner first.");
        }

        db.RemoveRange(existing.Where(p => p.RoleId != roleId));
        if (existing.Any(p => p.RoleId == roleId))
        {
            return Result.Success();
        }

        db.Add(new ResourcePermission { ResourceType = NodePermissions.ResourceType, ResourceId = command.Id, PrincipalType = PrincipalType.User, PrincipalId = command.Request.UserId, RoleId = roleId });
        return Result.Success();
    }

    public async Task<Result> HandleAsync(RemoveWorkspaceMember command, CancellationToken cancellationToken)
    {
        if (!await db.Set<Workspace>().AnyAsync(w => w.Id == command.Id, cancellationToken))
        {
            return NotFound;
        }

        var keys = await roles.KeysAsync(cancellationToken);
        var ownerRole = await roles.RoleIdAsync(WorkspaceRoles.Owner, cancellationToken);
        var roleIds = keys.Keys.ToList();
        var grants = db.Set<ResourcePermission>().Where(p => p.ResourceType == NodePermissions.ResourceType && p.ResourceId == command.Id && p.PrincipalType == PrincipalType.User && roleIds.Contains(p.RoleId));
        var removed = await grants.Where(p => p.PrincipalId == command.UserId).ToListAsync(cancellationToken);
        if (removed.Count == 0)
        {
            return Error.NotFound("workspaces.member_not_found", "The user is no member of this workspace.");
        }

        // a workspace keeps at least one owner
        if (removed.Any(p => p.RoleId == ownerRole) && !await grants.AnyAsync(p => p.RoleId == ownerRole && p.PrincipalId != command.UserId, cancellationToken))
        {
            return Error.Conflict("workspaces.last_owner", "Make someone else owner first.");
        }

        db.RemoveRange(removed);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<WorkspaceItemDto>>> HandleAsync(GetWorkspaceItems query, CancellationToken cancellationToken)
    {
        if (!await db.Set<Workspace>().AnyAsync(w => w.Id == query.Id, cancellationToken))
        {
            return NotFound;
        }

        var (children, linked) = await VisibleItemsAsync(query.Id, cancellationToken);
        var mapped = await mapper.MapAsync(children, cancellationToken);
        var mappedLinks = await mapper.MapAsync(linked, cancellationToken);
        IReadOnlyList<WorkspaceItemDto> items = [.. mapped.Select(n => new WorkspaceItemDto(n, false)), .. mappedLinks.Select(n => new WorkspaceItemDto(n, true))];
        return Result<IReadOnlyList<WorkspaceItemDto>>.Success(items);
    }

    /// <summary>Children and linked assets the caller may read: children with their own permissions and linked assets keep their rights.</summary>
    private async Task<(IReadOnlyList<Node> Children, IReadOnlyList<Asset> Linked)> VisibleItemsAsync(Guid id, CancellationToken cancellationToken)
    {
        var children = await db.Set<Node>().AsNoTracking().Where(n => n.ParentId == id && !(n is WorkTask)).OrderBy(n => n.Name).ToListAsync(cancellationToken);
        var linkedIds = db.Set<Edge>().Where(e => e.EdgeTypeId == EdgeType.LinkId && e.FromNodeId == id).Select(e => e.ToNodeId);
        var linked = await db.Set<Asset>().AsNoTracking().Where(a => linkedIds.Contains(a.Id)).OrderBy(a => a.Name).ToListAsync(cancellationToken);
        return (await AssetSelection.WithPermissionAsync(db, access, children, NodePermissions.Read, cancellationToken),
            await AssetSelection.WithPermissionAsync(db, access, linked, NodePermissions.Read, cancellationToken));
    }

    public async Task<Result> HandleAsync(LinkAssets command, CancellationToken cancellationToken)
    {
        if (command.Request.AssetIds.Count is 0 or > MaxLinks)
        {
            return Error.Validation("AssetIds", $"Link 1 to {MaxLinks} assets at once.");
        }

        if (await db.Set<Workspace>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == command.Id, cancellationToken) is not { } workspace)
        {
            return NotFound;
        }

        if (workspace.WorkspaceKind == WorkspaceKinds.Upload)
        {
            return Error.Validation("Id", "Upload workspaces hold their own files; use a collection to link assets.");
        }

        // only what the caller may read can be linked
        var ids = command.Request.AssetIds.Distinct().ToList();
        var assets = await db.Set<Asset>().AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
        var readable = await AssetSelection.WithPermissionAsync(db, access, assets, NodePermissions.Read, cancellationToken);
        if (readable.Count != ids.Count)
        {
            return Error.Validation("AssetIds", "Some assets do not exist or are not visible to you.");
        }

        var already = await db.Set<Edge>().Where(e => e.EdgeTypeId == EdgeType.LinkId && e.FromNodeId == workspace.Id && ids.Contains(e.ToNodeId)).Select(e => e.ToNodeId).ToListAsync(cancellationToken);
        db.AddRange(ids.Except(already).Select(id => new Edge { EdgeTypeId = EdgeType.LinkId, FromNodeId = workspace.Id, ToNodeId = id }));
        return Result.Success();
    }

    public async Task<Result> HandleAsync(UnlinkAsset command, CancellationToken cancellationToken)
    {
        var deleted = await db.Set<Edge>().Where(e => e.EdgeTypeId == EdgeType.LinkId && e.FromNodeId == command.Id && e.ToNodeId == command.AssetId).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0 ? Result.Success() : Error.NotFound("workspaces.link_not_found", "The asset is not linked here.");
    }

    public async Task<Result<FinalizeResultDto>> HandleAsync(FinalizeWorkspace command, CancellationToken cancellationToken)
    {
        if (await db.Set<Workspace>().SingleOrDefaultAsync(w => w.Id == command.Id && !w.IsTemplate, cancellationToken) is not { } workspace)
        {
            return NotFound;
        }

        if (workspace.FinalizedAt is not null)
        {
            return Error.Conflict("workspaces.finalized", "The workspace is finalised already.");
        }

        if (workspace.WorkspaceKind == WorkspaceKinds.Collection)
        {
            return Error.Validation("Id", "Collections only link assets; there is nothing to finalise.");
        }

        if (workspace.FinalizationFolderId is not { } folderId || await db.Set<Folder>().SingleOrDefaultAsync(f => f.Id == folderId, cancellationToken) is not { } folder)
        {
            return Error.Validation("FinalizationFolderId", "Choose the folder the files go to first.");
        }

        if (!await access.CanAsync(NodePermissions.Create, folder.Id, cancellationToken))
        {
            return Error.Forbidden("auth.forbidden", "You may not add files to the target folder.");
        }

        if (workspace.RequiredSetId is { } setId && await db.Set<PropertySet>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == setId, cancellationToken) is { } set)
        {
            var missing = set.Items.Where(i => i.FillMode == FillModes.Mandatory && !(workspace.Metadata.TryGetValue(i.DefinitionKey, out var values) && values.Count > 0))
                .Select(i => i.DefinitionKey).ToList();
            if (missing.Count > 0)
            {
                return Error.Validation("Metadata", $"Fill in {string.Join(", ", missing)} first.");
            }
        }

        // ponytail: the whole content is loaded and moved in memory (one save); a set-based rebase like MoveNode when workspaces hold many thousand nodes
        var prefix = workspace.Path;
        // tasks stay on the board
        var content = await db.Set<Node>().Where(n => n.Path.StartsWith(prefix) && n.Id != workspace.Id && !(n is WorkTask)).OrderBy(n => n.Depth).ToListAsync(cancellationToken);
        var byId = content.ToDictionary(n => n.Id);
        foreach (var node in content)
        {
            // parents come first, so every node is placed under its already moved parent; subfolders move with their content
            node.PlaceUnder(node.ParentId == workspace.Id ? folder : byId[node.ParentId!.Value]);
            if (node.ParentId == folder.Id)
            {
                node.MarkChanged(NodeChangeKinds.Moved);
            }
        }

        if (workspace.CopyMetadataToAssets)
        {
            // only properties assets may carry and users may edit; the asset's own values win
            var copyable = (await db.Set<PropertyDefinition>().AsNoTracking().Where(d => d.Editable).Select(d => new { d.Key, d.NodeTypeKeys }).ToListAsync(cancellationToken))
                .Where(d => d.NodeTypeKeys.Count == 0 || d.NodeTypeKeys.Contains(NodeTypeKeys.Asset)).Select(d => d.Key).ToHashSet();
            foreach (var asset in content.OfType<Asset>())
            {
                foreach (var (key, values) in workspace.Metadata.Where(p => copyable.Contains(p.Key) && !asset.Metadata.ContainsKey(p.Key)))
                {
                    asset.Metadata[key] = values.ToList();
                }
            }
        }

        workspace.FinalizedAt = clock.GetUtcNow();
        workspace.MarkChanged("finalized");
        return new FinalizeResultDto(content.Count(n => n is Asset && n.ParentId == folder.Id), folder.Id);
    }

    /// <summary>The finalisation folder must exist and take the caller's files.</summary>
    private async Task<Error?> FolderProblemAsync(Guid folderId, CancellationToken cancellationToken) =>
        await db.Set<Folder>().AnyAsync(f => f.Id == folderId, cancellationToken) && await access.CanAsync(NodePermissions.Create, folderId, cancellationToken)
            ? null
            : Error.Validation("FinalizationFolderId", "Choose a folder you may add files to.");

    private async Task<Error?> SetProblemAsync(Guid? setId, CancellationToken cancellationToken) =>
        setId is not { } id || await db.Set<PropertySet>().AnyAsync(s => s.Id == id && s.NodeTypeKey == NodeTypeKeys.Workspace, cancellationToken)
            ? null
            : Error.Validation("RequiredSetId", "Choose a property set for workspaces.");

    /// <summary>The caller's role per workspace, owner before collaborator.</summary>
    private async Task<Dictionary<Guid, string>> MyRolesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || ids.Count == 0)
        {
            return [];
        }

        var keys = await roles.KeysAsync(cancellationToken);
        var roleIds = keys.Keys.ToList();
        var grants = await db.Set<ResourcePermission>().AsNoTracking()
            .Where(p => p.ResourceType == NodePermissions.ResourceType && ids.Contains(p.ResourceId) && p.PrincipalType == PrincipalType.User && p.PrincipalId == userId && roleIds.Contains(p.RoleId))
            .ToListAsync(cancellationToken);
        return grants.GroupBy(g => g.ResourceId).ToDictionary(g => g.Key, g => g.Any(p => keys[p.RoleId] == WorkspaceRoles.Owner) ? WorkspaceRoles.Owner : WorkspaceRoles.Collaborator);
    }

    private static WorkspaceDto Map(Workspace w, int count, string? role) => new(
        w.Id, w.Name, w.WorkspaceKind, w.IsTemplate, w.TemplateId, w.FinalizationFolderId, w.CopyMetadataToAssets, w.RequiredSetId, w.FinalizedAt, w.CreatedAt, count, role);
}
