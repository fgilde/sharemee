using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Contracts.Metadata;
using Shareme.Graph;

namespace Shareme.Metadata;

[DependsOn(typeof(ShareMeGraphModule))]
public sealed class ShareMeMetadataModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(ShareMeMetadataModule).Assembly);
        services.AddSingleton<IModelContributor, MetadataModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, MetadataPermissionDefinitions>();
        services.AddScoped<MetadataCatalog>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var metadata = app.MapGroup("/api/v1/metadata").WithTags("Metadata").RequireAuthorization();
        metadata.MapGet("/groups", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPropertyGroups(), ct).ToHttpResult());
        metadata.MapPost("/groups", (PropertyGroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SavePropertyGroup(null, body), ct).ToHttpResult());
        metadata.MapPut("/groups/{id:guid}", (Guid id, PropertyGroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SavePropertyGroup(id, body), ct).ToHttpResult());
        metadata.MapDelete("/groups/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeletePropertyGroup(id), ct).ToHttpResult());

        metadata.MapGet("/definitions", (string? nodeType, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPropertyDefinitions(nodeType), ct).ToHttpResult());
        metadata.MapPost("/definitions", (PropertyDefinitionRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SavePropertyDefinition(null, body), ct).ToHttpResult());
        metadata.MapPut("/definitions/{id:guid}", (Guid id, PropertyDefinitionRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SavePropertyDefinition(id, body), ct).ToHttpResult());
        metadata.MapDelete("/definitions/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeletePropertyDefinition(id), ct).ToHttpResult());
        metadata.MapPut("/definitions/{id:guid}/list-values", (Guid id, PropertyListValueDto[] body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetPropertyListValues(id, body), ct).ToHttpResult());

        metadata.MapGet("/languages", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetDataLanguages(), ct).ToHttpResult());
        metadata.MapPost("/languages", (DataLanguageDto body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveDataLanguage(body), ct).ToHttpResult());
        metadata.MapDelete("/languages/{code}", (string code, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteDataLanguage(code), ct).ToHttpResult());

        metadata.MapGet("/contexts", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUserContexts(), ct).ToHttpResult());
        metadata.MapPost("/contexts", (UserContextDto body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveUserContext(body), ct).ToHttpResult());
        metadata.MapDelete("/contexts/{key}", (string key, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteUserContext(key), ct).ToHttpResult());

        metadata.MapGet("/sets", (string? nodeType, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPropertySets(nodeType), ct).ToHttpResult());
        metadata.MapPost("/sets", (PropertySetRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SavePropertySet(null, body), ct).ToHttpResult());
        metadata.MapPut("/sets/{id:guid}", (Guid id, PropertySetRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SavePropertySet(id, body), ct).ToHttpResult());
        metadata.MapDelete("/sets/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeletePropertySet(id), ct).ToHttpResult());

        var nodes = app.MapGroup("/api/v1/nodes").WithTags("Nodes").RequireAuthorization();
        nodes.MapGet("/{id:guid}/metadata", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetNodeMetadata(id), ct).ToHttpResult());
        nodes.MapPut("/{id:guid}/metadata", (Guid id, Dictionary<string, List<MetadataValue>?> body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetNodeMetadata(id, body, Replace: true), ct).ToHttpResult());
        nodes.MapPatch("/{id:guid}/metadata", (Guid id, Dictionary<string, List<MetadataValue>?> body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetNodeMetadata(id, body, Replace: false), ct).ToHttpResult());
    }
}

internal sealed class MetadataPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(MetadataPermissions.GroupName, "Metadata")
            .Add(MetadataPermissions.Manage, "Manage properties, list values, languages, contexts and sets");
}
