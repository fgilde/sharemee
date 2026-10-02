using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Coworkee.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Contracts.Graph;

namespace Shareme.Graph;

[DependsOn(typeof(CoworkeeIdentityModule), typeof(CoworkeeNotificationsModule))]
public sealed class ShareMeGraphModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(ShareMeGraphModule).Assembly);
        services.AddSingleton<IModelContributor, GraphModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, GraphPermissionDefinitions>();
        services.AddScoped<IResourceHierarchy, NodeHierarchy>();
        services.AddScoped<NodeAccess>();
        services.AddScoped<RestrictionStore>();
        services.AddScoped<IResourceRestriction, NodeRestriction>();
        services.AddScoped(provider => new Restrictions(
            provider.GetRequiredService<RestrictionStore>(), provider.GetRequiredService<IPermissionChecker>(), provider.GetRequiredService<Coworkee.Core.Security.ICurrentUser>()));
        services.AddScoped<NodeMapper>();
        services.AddScoped<IRealtimeTopicAuthorizer, ChildrenTopicAuthorizer>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var nodes = app.MapGroup("/api/v1/nodes").WithTags("Nodes").RequireAuthorization();
        nodes.MapGet("/root", (IDispatcher d, CancellationToken ct) => d.SendAsync(new EnsureRoot(), ct).ToHttpResult());
        nodes.MapGet("/entry-points", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetEntryPoints(), ct).ToHttpResult());
        nodes.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetNode(id), ct).ToHttpResult());
        nodes.MapGet("/{id:guid}/children", (Guid id, [AsParameters] PageRequest page, string? sort, string? kind, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetChildren(id, page, sort, kind), ct).ToHttpResult());
        nodes.MapPost("/", (CreateNodeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateNode(body), ct).ToHttpResult());
        nodes.MapPut("/{id:guid}", (Guid id, UpdateNodeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateNode(id, body), ct).ToHttpResult());
        nodes.MapPost("/{id:guid}/move", (Guid id, MoveNodeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new MoveNode(id, body.TargetParentId), ct).ToHttpResult());
        nodes.MapPut("/{id:guid}/inheritance", (Guid id, InheritanceRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetInheritance(id, body.Interrupt), ct).ToHttpResult());
        nodes.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteNode(id), ct).ToHttpResult());
        nodes.MapPut("/{id:guid}/subscription", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new Subscribe(id, true), ct).ToHttpResult());
        nodes.MapDelete("/{id:guid}/subscription", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new Subscribe(id, false), ct).ToHttpResult());
        nodes.MapGet("/{id:guid}/edges", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetEdges(id), ct).ToHttpResult());
        nodes.MapPost("/{id:guid}/edges", (Guid id, CreateEdgeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateEdge(id, body), ct).ToHttpResult());

        var edges = app.MapGroup("/api/v1/edges").WithTags("Nodes").RequireAuthorization();
        edges.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteEdge(id), ct).ToHttpResult());

        var bin = app.MapGroup("/api/v1/recycle-bin").WithTags("Recycle bin").RequireAuthorization();
        bin.MapGet("/", ([AsParameters] PageRequest page, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRecycleBin(page), ct).ToHttpResult());
        bin.MapPost("/{id:guid}/restore", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new RestoreNode(id), ct).ToHttpResult());
        bin.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new PurgeNode(id), ct).ToHttpResult());

        var restrictions = app.MapGroup("/api/v1/restrictions").WithTags("Restrictions").RequireAuthorization();
        restrictions.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRestrictions(), ct).ToHttpResult());
        restrictions.MapPost("/", (RestrictionRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveRestriction(body), ct).ToHttpResult());
        restrictions.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteRestriction(id), ct).ToHttpResult());

        var types = app.MapGroup("/api/v1/node-types").WithTags("Nodes").RequireAuthorization();
        types.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetNodeTypes(), ct).ToHttpResult());
        types.MapPost("/", (CreateNodeTypeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateNodeType(body), ct).ToHttpResult());
    }
}

internal sealed class GraphPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(NodePermissions.GroupName, "Folders and assets")
            .Add(NodePermissions.Read, "Read")
            .Add(NodePermissions.Create, "Create inside", NodePermissions.Read)
            .Add(NodePermissions.Update, "Edit", NodePermissions.Read)
            .Add(NodePermissions.Move, "Move", NodePermissions.Read)
            .Add(NodePermissions.Delete, "Delete", NodePermissions.Read)
            .Add(NodePermissions.ManagePermissions, "Manage permissions and inheritance", NodePermissions.Read)
            .Add(NodePermissions.Admin, "Administer node types and the recycle bin", NodePermissions.Read);
}
