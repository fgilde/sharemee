using System.ComponentModel;
using Coworkee.Ai;
using Coworkee.Application.Authorization;
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
// Queries are read only tools; commands change data and run only straight from a user message.

public sealed record AssistantSearch([property: Description("Words to look for in names, content and people.")] string Query) : IQuery<Result<AssistantSearchDto>>;

public sealed record AssistantGetItem([property: Description("Id of the folder, file, page or news item.")] Guid Id) : IQuery<Result<NodeDetailDto>>;

public sealed record AssistantListChildren(
    [property: Description("Id of the folder; leave empty for the top level folders of the user.")] Guid? Id = null,
    [property: Description("How many items to return, 1 to 50.")] int Take = 25) : IQuery<Result<PagedResult<NodeDto>>>;

public sealed record AssistantCreateFolder(Guid ParentId, string Name) : ICommand<Result<NodeDto>>;

public sealed record AssistantGetMetadata(Guid Id) : IQuery<Result<NodeMetadataDto>>;

public sealed record AssistantSetMetadata(
    Guid Id,
    [property: Description("Property key to values, for example {\"title\":[{\"v\":\"Summer\",\"lang\":\"en\"}]} or {\"category\":[{\"list\":\"<entry key>\"}]}. Listed properties are replaced, others stay; an empty list clears one.")]
    Dictionary<string, List<MetadataValue>?> Values) : ICommand<Result>;

public sealed record AssistantReadPage(Guid Id) : IQuery<Result<PageDto>>;

public sealed record AssistantReadNews(Guid Id) : IQuery<Result<NewsDto>>;

public sealed record AssistantCreatePage(Guid ParentId, string Title, [property: Description("Content as Markdown.")] string Markdown) : ICommand<Result<PageDto>>;

public sealed record AssistantNews([property: Description("How many news items, 1 to 20.")] int Take = 10) : IQuery<Result<IReadOnlyList<NewsSummaryDto>>>;

[RequiresPermission(PeoplePermissions.View)]
public sealed record AssistantFindPeople([property: Description("Name, department or location; empty lists everybody.")] string? Search = null) : IQuery<Result<IReadOnlyList<AssistantPersonDto>>>;

/// <summary>A person as the model sees it: no phone, no about text, no avatar.</summary>
public sealed record AssistantPersonDto(Guid UserId, string DisplayName, string? Email, string? Department, string? Position, string? Location)
{
    public static AssistantPersonDto From(PersonDto p) => new(p.UserId, p.DisplayName, p.Email, p.Department, p.Position, p.Location);
}

public sealed record AssistantSearchDto(IReadOnlyList<NodeDto> Nodes, long NodeTotal, IReadOnlyList<AssistantPersonDto> People);

internal sealed class AssistantToolHandlers(IDispatcher dispatcher) :
    IHandler<AssistantSearch, Result<AssistantSearchDto>>,
    IHandler<AssistantGetItem, Result<NodeDetailDto>>,
    IHandler<AssistantListChildren, Result<PagedResult<NodeDto>>>,
    IHandler<AssistantCreateFolder, Result<NodeDto>>,
    IHandler<AssistantGetMetadata, Result<NodeMetadataDto>>,
    IHandler<AssistantSetMetadata, Result>,
    IHandler<AssistantReadPage, Result<PageDto>>,
    IHandler<AssistantReadNews, Result<NewsDto>>,
    IHandler<AssistantCreatePage, Result<PageDto>>,
    IHandler<AssistantNews, Result<IReadOnlyList<NewsSummaryDto>>>,
    IHandler<AssistantFindPeople, Result<IReadOnlyList<AssistantPersonDto>>>
{
    public async Task<Result<AssistantSearchDto>> HandleAsync(AssistantSearch query, CancellationToken cancellationToken)
    {
        var found = await dispatcher.SendAsync(new GlobalSearch(query.Query), cancellationToken);
        return found.IsSuccess
            ? new AssistantSearchDto(found.Value.Nodes, found.Value.NodeTotal, [.. found.Value.People.Select(AssistantPersonDto.From)])
            : found.Error!;
    }

    public Task<Result<NodeDetailDto>> HandleAsync(AssistantGetItem query, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new GetNode(query.Id), cancellationToken);

    public async Task<Result<PagedResult<NodeDto>>> HandleAsync(AssistantListChildren query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, 50);
        if (query.Id is { } id)
        {
            return await dispatcher.SendAsync(new GetChildren(id, new Coworkee.Contracts.PageRequest(1, take), null), cancellationToken);
        }

        // the folders the user may enter, as in the browser; never creates the root
        var entries = await dispatcher.SendAsync(new GetEntryPoints(), cancellationToken);
        return entries.IsSuccess
            ? new PagedResult<NodeDto>([.. entries.Value.Take(take)], entries.Value.Count, 1, take)
            : entries.Error!;
    }

    public Task<Result<NodeDto>> HandleAsync(AssistantCreateFolder command, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new CreateNode(new CreateNodeRequest(command.ParentId, command.Name)), cancellationToken);

    public Task<Result<NodeMetadataDto>> HandleAsync(AssistantGetMetadata query, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new GetNodeMetadata(query.Id), cancellationToken);

    public async Task<Result> HandleAsync(AssistantSetMetadata command, CancellationToken cancellationToken) =>
        command.Values is not { Count: > 0 }
            ? Error.Validation(nameof(command.Values), "Name at least one property with its values.")
            : await dispatcher.SendAsync(new SetNodeMetadata(command.Id, command.Values, Replace: false), cancellationToken);

    public async Task<Result<PageDto>> HandleAsync(AssistantReadPage query, CancellationToken cancellationToken)
    {
        var page = await dispatcher.SendAsync(new GetPage(query.Id), cancellationToken);
        return page.IsSuccess ? page.Value with { Html = string.Empty } : page;
    }

    public async Task<Result<NewsDto>> HandleAsync(AssistantReadNews query, CancellationToken cancellationToken)
    {
        var news = await dispatcher.SendAsync(new GetNews(query.Id), cancellationToken);
        return news.IsSuccess ? news.Value with { Html = string.Empty } : news;
    }

    public Task<Result<PageDto>> HandleAsync(AssistantCreatePage command, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new CreatePage(command.ParentId, new Contracts.Knowledge.PageRequest(command.ParentId, command.Title, command.Markdown)), cancellationToken);

    public Task<Result<IReadOnlyList<NewsSummaryDto>>> HandleAsync(AssistantNews query, CancellationToken cancellationToken) =>
        dispatcher.SendAsync(new GetNewsFeed(Math.Clamp(query.Take, 1, 20)), cancellationToken);

    public async Task<Result<IReadOnlyList<AssistantPersonDto>>> HandleAsync(AssistantFindPeople query, CancellationToken cancellationToken)
    {
        var people = await dispatcher.SendAsync(new GetPeople(query.Search, 0, 10), cancellationToken);
        return people.IsSuccess ? Result<IReadOnlyList<AssistantPersonDto>>.Success([.. people.Value.Select(AssistantPersonDto.From)]) : people.Error!;
    }
}

internal static class AssistantTools
{
    public static IServiceCollection AddAssistantTools(this IServiceCollection services) => services
        .AddAiTool<AssistantSearch>("search", "Searches folders, files, pages, news and people the user can see. Returns ids for the other tools.")
        .AddAiTool<AssistantGetItem>("get_item", "Gets a folder, file, page or news item with its path.")
        .AddAiTool<AssistantListChildren>("list_children", "Lists the items inside a folder, or the top level folders.")
        .AddAiTool<AssistantCreateFolder>("create_folder", "Creates a folder inside another folder.")
        .AddAiTool<AssistantGetMetadata>("get_metadata", "Gets the metadata properties of an item with their values and whether they are inherited.")
        .AddAiTool<AssistantSetMetadata>("set_metadata", "Sets metadata values of an item. Read the item's metadata first to learn the property keys.")
        .AddAiTool<AssistantReadPage>("read_page", "Reads a knowledge page as Markdown.")
        .AddAiTool<AssistantReadNews>("read_news", "Reads a news item as Markdown.")
        .AddAiTool<AssistantCreatePage>("create_page", "Creates a knowledge page inside a folder.")
        .AddAiTool<AssistantNews>("news", "Lists the latest news.")
        .AddAiTool<AssistantFindPeople>("find_people", "Finds colleagues in the people directory.");
}
