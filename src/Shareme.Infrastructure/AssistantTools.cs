using System.ComponentModel;
using Coworkee.Ai;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Core.Results;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;
using Shareme.Contracts.Metadata;
using Shareme.Graph;
using Shareme.Knowledge;
using Shareme.Metadata;

namespace Shareme.Infrastructure;

// Assistant and MCP tools: small requests with model friendly inputs that forward to the API requests,
// so every call passes the same permission, restriction and validation checks as the API.

public sealed record AssistantSearch([property: Description("Words to look for in names, content and people.")] string Query) : IQuery<Result<GlobalSearchDto>>;

public sealed record AssistantGetItem([property: Description("Id of the folder, file, page or news item.")] Guid Id) : IQuery<Result<NodeDetailDto>>;

public sealed record AssistantListChildren(
    [property: Description("Id of the folder; leave empty for the root.")] Guid? Id = null,
    [property: Description("How many items to return, 1 to 100.")] int Take = 50) : ICommand<Result<PagedResult<NodeDto>>>;

public sealed record AssistantCreateFolder(Guid ParentId, string Name) : ICommand<Result<NodeDto>>;

public sealed record AssistantGetMetadata(Guid Id) : IQuery<Result<NodeMetadataDto>>;

public sealed record AssistantSetMetadata(
    Guid Id,
    [property: Description("Property key to values, for example {\"title\":[{\"v\":\"Summer\",\"lang\":\"en\"}]} or {\"category\":[{\"list\":\"<entry key>\"}]}. Listed properties are replaced, others stay; an empty list clears one.")]
    Dictionary<string, List<MetadataValue>?> Values) : ICommand<Result>;

public sealed record AssistantReadPage(Guid Id) : IQuery<Result<PageDto>>;

public sealed record AssistantCreatePage(Guid ParentId, string Title, [property: Description("Content as Markdown.")] string Markdown) : ICommand<Result<PageDto>>;

public sealed record AssistantNews([property: Description("How many news items, 1 to 20.")] int Take = 10) : IQuery<Result<IReadOnlyList<NewsSummaryDto>>>;

public sealed record AssistantFindPeople([property: Description("Name, department or location; empty lists everybody.")] string? Search = null) : IQuery<Result<IReadOnlyList<PersonDto>>>;

internal sealed class AssistantToolHandlers(IDispatcher dispatcher) :
    IHandler<AssistantSearch, Result<GlobalSearchDto>>,
    IHandler<AssistantGetItem, Result<NodeDetailDto>>,
    IHandler<AssistantListChildren, Result<PagedResult<NodeDto>>>,
    IHandler<AssistantCreateFolder, Result<NodeDto>>,
    IHandler<AssistantGetMetadata, Result<NodeMetadataDto>>,
    IHandler<AssistantSetMetadata, Result>,
    IHandler<AssistantReadPage, Result<PageDto>>,
    IHandler<AssistantCreatePage, Result<PageDto>>,
    IHandler<AssistantNews, Result<IReadOnlyList<NewsSummaryDto>>>,
    IHandler<AssistantFindPeople, Result<IReadOnlyList<PersonDto>>>
{
    public Task<Result<GlobalSearchDto>> HandleAsync(AssistantSearch query, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new GlobalSearch(query.Query), cancellationToken);

    public Task<Result<NodeDetailDto>> HandleAsync(AssistantGetItem query, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new GetNode(query.Id), cancellationToken);

    public async Task<Result<PagedResult<NodeDto>>> HandleAsync(AssistantListChildren command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        if (id is null)
        {
            var root = await dispatcher.SendAsync(new EnsureRoot(), cancellationToken);
            if (!root.IsSuccess)
            {
                return root.Error!;
            }

            id = root.Value.Id;
        }

        return await dispatcher.SendAsync(new GetChildren(id.Value, new Coworkee.Contracts.PageRequest(1, Math.Clamp(command.Take, 1, 100)), null), cancellationToken);
    }

    public Task<Result<NodeDto>> HandleAsync(AssistantCreateFolder command, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new CreateNode(new CreateNodeRequest(command.ParentId, command.Name)), cancellationToken);

    public Task<Result<NodeMetadataDto>> HandleAsync(AssistantGetMetadata query, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new GetNodeMetadata(query.Id), cancellationToken);

    public Task<Result> HandleAsync(AssistantSetMetadata command, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new SetNodeMetadata(command.Id, command.Values, Replace: false), cancellationToken);

    public async Task<Result<PageDto>> HandleAsync(AssistantReadPage query, CancellationToken cancellationToken)
    {
        var page = await dispatcher.SendAsync(new GetPage(query.Id), cancellationToken);
        return page.IsSuccess ? page.Value with { Html = string.Empty } : page;
    }

    public Task<Result<PageDto>> HandleAsync(AssistantCreatePage command, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new CreatePage(command.ParentId, new Contracts.Knowledge.PageRequest(command.ParentId, command.Title, command.Markdown)), cancellationToken);

    public Task<Result<IReadOnlyList<NewsSummaryDto>>> HandleAsync(AssistantNews query, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new GetNewsFeed(Math.Clamp(query.Take, 1, 20)), cancellationToken);

    public Task<Result<IReadOnlyList<PersonDto>>> HandleAsync(AssistantFindPeople query, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new GetPeople(query.Search, 0, 25), cancellationToken);
}

internal static class AssistantTools
{
    public static IServiceCollection AddAssistantTools(this IServiceCollection services) => services
        .AddAiTool<AssistantSearch>("search", "Searches folders, files, pages, news and people the user can see. Returns ids for the other tools.")
        .AddAiTool<AssistantGetItem>("get_item", "Gets a folder, file, page or news item with its path.")
        .AddAiTool<AssistantListChildren>("list_children", "Lists the items inside a folder.")
        .AddAiTool<AssistantCreateFolder>("create_folder", "Creates a folder inside another folder.")
        .AddAiTool<AssistantGetMetadata>("get_metadata", "Gets the metadata properties of an item with their values and whether they are inherited.")
        .AddAiTool<AssistantSetMetadata>("set_metadata", "Sets metadata values of an item. Read the item's metadata first to learn the property keys.")
        .AddAiTool<AssistantReadPage>("read_page", "Reads a knowledge page as Markdown.")
        .AddAiTool<AssistantCreatePage>("create_page", "Creates a knowledge page inside a folder.")
        .AddAiTool<AssistantNews>("news", "Lists the latest news.")
        .AddAiTool<AssistantFindPeople>("find_people", "Finds colleagues in the people directory.");
}
