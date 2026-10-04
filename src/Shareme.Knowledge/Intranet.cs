using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;
using Shareme.Graph;

namespace Shareme.Knowledge;

/// <summary>A node a user marked to find it again quickly.</summary>
[NotAudited]
public sealed class Favorite : Entity, IMultiTenant
{
    public Guid UserId { get; set; }

    public Guid NodeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>The start page widgets of a user, or of the tenant when <see cref="UserId"/> is empty.</summary>
[NotAudited]
public sealed class HomeLayout : Entity, IMultiTenant
{
    public Guid? UserId { get; set; }

    public List<string> Widgets { get; set; } = [];

    public Guid TenantId { get; set; }
}

internal static class IntranetEndpoints
{
    public static void Map(WebApplication app)
    {
        var news = app.MapGroup("/api/v1/news").WithTags("News").RequireAuthorization();
        news.MapGet("/", (int? take, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetNewsFeed(take ?? 10), ct).ToHttpResult());
        news.MapPost("/", (NewsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateNews(body.ParentId, body), ct).ToHttpResult());
        news.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetNews(id), ct).ToHttpResult());
        news.MapPut("/{id:guid}", (Guid id, NewsUpdateRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateNews(id, body), ct).ToHttpResult());

        var favorites = app.MapGroup("/api/v1/favorites").WithTags("Favorites").RequireAuthorization();
        favorites.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetFavorites(), ct).ToHttpResult());
        favorites.MapPut("/{nodeId:guid}", (Guid nodeId, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetFavorite(nodeId, true), ct).ToHttpResult());
        favorites.MapDelete("/{nodeId:guid}", (Guid nodeId, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetFavorite(nodeId, false), ct).ToHttpResult());

        var home = app.MapGroup("/api/v1/home").WithTags("Home").RequireAuthorization();
        home.MapGet("/layout", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetHomeLayout(), ct).ToHttpResult());
        home.MapPut("/layout", (HomeLayoutRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveHomeLayout(body, false), ct).ToHttpResult());
        home.MapDelete("/layout", (IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveHomeLayout(null, false), ct).ToHttpResult());
        home.MapPut("/layout/default", (HomeLayoutRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveHomeLayout(body, true), ct).ToHttpResult());
        home.MapGet("/recent", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRecentNodes(), ct).ToHttpResult());
        home.MapGet("/tasks", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetMyTasks(), ct).ToHttpResult());
    }
}

public sealed record CreateNews(Guid ParentId, NewsRequest Request) : ICommand<Result<NewsDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => ParentId;

    public string RequiredPermission => NodePermissions.Create;
}

public sealed record GetNews(Guid Id) : PageRequestBase(Id, NodePermissions.Read), IQuery<Result<NewsDto>>;

public sealed record UpdateNews(Guid Id, NewsUpdateRequest Request) : PageRequestBase(Id, NodePermissions.Update), ICommand<Result<NewsDto>>;

/// <summary>Published news the user can read, pinned first, newest first.</summary>
public sealed record GetNewsFeed(int Take) : IQuery<Result<IReadOnlyList<NewsSummaryDto>>>;

public sealed record GetFavorites : IQuery<Result<IReadOnlyList<NodeDto>>>;

public sealed record SetFavorite(Guid NodeId, bool On) : PageRequestBase(NodeId, NodePermissions.Read), ICommand<Result>;

public sealed record GetHomeLayout : IQuery<Result<HomeLayoutDto>>;

/// <summary>Null widgets go back to the default; <see cref="ForTenant"/> sets the default (admins).</summary>
public sealed record SaveHomeLayout(HomeLayoutRequest? Request, bool ForTenant) : ICommand<Result<HomeLayoutDto>>;

public sealed record GetRecentNodes : IQuery<Result<IReadOnlyList<NodeDto>>>;

public sealed record GetMyTasks : IQuery<Result<IReadOnlyList<HomeTaskDto>>>;

internal sealed class IntranetHandlers(CoworkeeDbContext db, NodeAccess access, NodeMapper mapper, ICurrentUser currentUser, TimeProvider clock)
    : IHandler<CreateNews, Result<NewsDto>>,
      IHandler<GetNews, Result<NewsDto>>,
      IHandler<UpdateNews, Result<NewsDto>>,
      IHandler<GetNewsFeed, Result<IReadOnlyList<NewsSummaryDto>>>,
      IHandler<GetFavorites, Result<IReadOnlyList<NodeDto>>>,
      IHandler<SetFavorite, Result>,
      IHandler<GetHomeLayout, Result<HomeLayoutDto>>,
      IHandler<SaveHomeLayout, Result<HomeLayoutDto>>,
      IHandler<GetRecentNodes, Result<IReadOnlyList<NodeDto>>>,
      IHandler<GetMyTasks, Result<IReadOnlyList<HomeTaskDto>>>
{
    private const int Scan = 300;
    private const int MaxFavorites = 500;
    private static readonly Error NotFound = Error.NotFound("news.not_found", "The news item does not exist.");

    private Guid Me => currentUser.UserId ?? Guid.Empty;

    public async Task<Result<NewsDto>> HandleAsync(CreateNews command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        if (Invalid(r.Title, r.Markdown, r.PublishAt ?? clock.GetUtcNow(), r.ExpiresAt) is { } invalid)
        {
            return invalid;
        }

        if (await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == command.ParentId, cancellationToken) is not { } parent || !(parent.AcceptsFiles() || parent.Semantic == NodeSemantic.Custom))
        {
            return Error.Validation("ParentId", "News live in folders and open workspaces.");
        }

        var news = new News { NodeTypeId = NodeType.NewsTypeId, Name = r.Title.Trim(), PublishAt = r.PublishAt ?? clock.GetUtcNow(), ExpiresAt = r.ExpiresAt, Pinned = r.Pinned };
        news.PlaceUnder(parent);
        var content = new NodeContent { NodeId = news.Id };
        Write(content, r.Markdown);
        news.MarkChanged("created");
        db.AddRange(news, content);
        await db.SaveChangesAsync(cancellationToken);
        return await MapAsync(news, content, cancellationToken);
    }

    public async Task<Result<NewsDto>> HandleAsync(GetNews query, CancellationToken cancellationToken) =>
        await db.Set<News>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == query.Id, cancellationToken) is { } news
            ? await MapAsync(news, await db.Set<NodeContent>().AsNoTracking().SingleOrDefaultAsync(c => c.NodeId == news.Id, cancellationToken) ?? new NodeContent { NodeId = news.Id }, cancellationToken)
            : NotFound;

    public async Task<Result<NewsDto>> HandleAsync(UpdateNews command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        if (Invalid(r.Title, r.Markdown, r.PublishAt, r.ExpiresAt) is { } invalid)
        {
            return invalid;
        }

        if (await db.Set<News>().SingleOrDefaultAsync(n => n.Id == command.Id, cancellationToken) is not { } news)
        {
            return NotFound;
        }

        var content = await db.Set<NodeContent>().SingleOrDefaultAsync(c => c.NodeId == news.Id, cancellationToken) ?? db.Add(new NodeContent { NodeId = news.Id }).Entity;
        (news.Name, news.PublishAt, news.ExpiresAt, news.Pinned) = (r.Title.Trim(), r.PublishAt, r.ExpiresAt, r.Pinned);
        Write(content, r.Markdown);
        news.MarkChanged("content changed");
        await db.SaveChangesAsync(cancellationToken);
        return await MapAsync(news, content, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<NewsSummaryDto>>> HandleAsync(GetNewsFeed query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var take = Math.Clamp(query.Take, 1, 50);
        var candidates = await (await access.VisibleAsync(db.Set<News>().AsNoTracking(), cancellationToken))
            .Where(n => n.PublishAt <= now && (n.ExpiresAt == null || n.ExpiresAt > now))
            .OrderByDescending(n => n.Pinned).ThenByDescending(n => n.PublishAt).Take(Scan).ToListAsync(cancellationToken);
        var shown = await ReadableAsync(candidates, take, cancellationToken);
        var ids = shown.Select(n => n.Id).ToList();
        var texts = await db.Set<NodeContent>().AsNoTracking().Where(c => ids.Contains(c.NodeId)).ToDictionaryAsync(c => c.NodeId, c => c.Text, cancellationToken);
        IReadOnlyList<NewsSummaryDto> feed = shown.Select(n => new NewsSummaryDto(n.Id, n.Name, Teaser(texts.GetValueOrDefault(n.Id) ?? string.Empty), n.PublishAt, n.Pinned)).ToList();
        return Result<IReadOnlyList<NewsSummaryDto>>.Success(feed);
    }

    public async Task<Result<IReadOnlyList<NodeDto>>> HandleAsync(GetFavorites query, CancellationToken cancellationToken)
    {
        var ids = await db.Set<Favorite>().Where(f => f.UserId == Me).OrderByDescending(f => f.CreatedAt).Select(f => f.NodeId).Take(MaxFavorites).ToListAsync(cancellationToken);
        var nodes = (await (await access.VisibleAsync(db.Set<Node>().AsNoTracking().Where(n => ids.Contains(n.Id)), cancellationToken)).ToListAsync(cancellationToken))
            .OrderBy(n => ids.IndexOf(n.Id)).ToList();

        // favorites that are gone or out of reach now simply do not show
        return Result<IReadOnlyList<NodeDto>>.Success(await mapper.MapAsync(await ReadableAsync(nodes, MaxFavorites, cancellationToken), cancellationToken));
    }

    public async Task<Result> HandleAsync(SetFavorite command, CancellationToken cancellationToken)
    {
        var existing = await db.Set<Favorite>().SingleOrDefaultAsync(f => f.UserId == Me && f.NodeId == command.NodeId, cancellationToken);
        if (command.On && existing is null)
        {
            if (await db.Set<Favorite>().CountAsync(f => f.UserId == Me, cancellationToken) >= MaxFavorites)
            {
                return Error.Validation("NodeId", $"Up to {MaxFavorites} favorites.");
            }

            db.Add(new Favorite { UserId = Me, NodeId = command.NodeId, CreatedAt = clock.GetUtcNow() });
        }
        else if (!command.On && existing is not null)
        {
            db.Remove(existing);
        }

        return Result.Success();
    }

    public async Task<Result<HomeLayoutDto>> HandleAsync(GetHomeLayout query, CancellationToken cancellationToken)
    {
        var layouts = await db.Set<HomeLayout>().AsNoTracking().Where(l => l.UserId == Me || l.UserId == null).ToListAsync(cancellationToken);
        return layouts.FirstOrDefault(l => l.UserId == Me) is { } own
            ? new HomeLayoutDto(own.Widgets, false)
            : new HomeLayoutDto(layouts.FirstOrDefault(l => l.UserId == null)?.Widgets ?? [.. HomeWidgets.All], true);
    }

    public async Task<Result<HomeLayoutDto>> HandleAsync(SaveHomeLayout command, CancellationToken cancellationToken)
    {
        if (command.ForTenant && !await access.IsGlobalAsync(NodePermissions.Admin, cancellationToken))
        {
            return Error.Forbidden("home.forbidden", "Only administrators set the default start page.");
        }

        var widgets = command.Request?.Widgets.Where(w => w is not null).Distinct().ToList();
        if (widgets is not null && widgets.Any(w => !HomeWidgets.All.Contains(w)))
        {
            return Error.Validation("Widgets", $"Known widgets: {string.Join(", ", HomeWidgets.All)}.");
        }

        Guid? owner = command.ForTenant ? null : Me;
        var layout = await db.Set<HomeLayout>().SingleOrDefaultAsync(l => l.UserId == owner, cancellationToken);
        if (widgets is null)
        {
            if (layout is not null)
            {
                db.Remove(layout);
            }
        }
        else
        {
            layout ??= db.Add(new HomeLayout { UserId = owner }).Entity;
            layout.Widgets = widgets;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await HandleAsync(new GetHomeLayout(), cancellationToken);
    }

    public async Task<Result<IReadOnlyList<NodeDto>>> HandleAsync(GetRecentNodes query, CancellationToken cancellationToken)
    {
        var candidates = await (await access.VisibleAsync(db.Set<Node>().AsNoTracking().Where(n => !(n is WorkTask) && !(n is Workspace)), cancellationToken))
            .OrderByDescending(n => n.ModifiedAt ?? n.CreatedAt).Take(Scan).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<NodeDto>>.Success(await mapper.MapAsync(await ReadableAsync(candidates, 10, cancellationToken), cancellationToken));
    }

    public async Task<Result<IReadOnlyList<HomeTaskDto>>> HandleAsync(GetMyTasks query, CancellationToken cancellationToken)
    {
        var me = Me;
        var tasks = await (await access.VisibleAsync(db.Set<WorkTask>().AsNoTracking().Where(t => t.CompletedAt == null && t.AssigneeIds.Contains(me)), cancellationToken))
            .OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).Take(Scan).ToListAsync(cancellationToken);
        var readable = await ReadableAsync(tasks, 50, cancellationToken);
        var workspaceIds = readable.Select(t => t.ParentId).OfType<Guid>().Distinct().ToList();
        var names = await db.Set<Workspace>().AsNoTracking().Where(w => workspaceIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);
        IReadOnlyList<HomeTaskDto> list = readable.Select(t => new HomeTaskDto(t.Id, t.ParentId ?? Guid.Empty, names.GetValueOrDefault(t.ParentId ?? Guid.Empty, string.Empty), t.Name, t.DueDate)).ToList();
        return Result<IReadOnlyList<HomeTaskDto>>.Success(list);
    }

    /// <summary>The first nodes of the list the user may read, in order.</summary>
    private async Task<List<T>> ReadableAsync<T>(IReadOnlyList<T> nodes, int take, CancellationToken cancellationToken)
        where T : Node
    {
        if (await access.IsGlobalAsync(NodePermissions.Read, cancellationToken))
        {
            return nodes.Take(take).ToList();
        }

        var readable = new List<T>();
        foreach (var node in nodes)
        {
            if (readable.Count >= take)
            {
                break;
            }

            if (await access.CanAsync(NodePermissions.Read, node.Id, cancellationToken))
            {
                readable.Add(node);
            }
        }

        return readable;
    }

    private static Error? Invalid(string? title, string? markdown, DateTimeOffset publishAt, DateTimeOffset? expiresAt) =>
        string.IsNullOrWhiteSpace(title) || title.Length > 400 || markdown is null || markdown.Length > PageContent.MaxMarkdown || (expiresAt is { } expires && expires <= publishAt)
            ? Error.Validation("Title", "A title, up to 200 KB of text and an end after the start.")
            : null;

    private static void Write(NodeContent content, string markdown)
    {
        content.Markdown = markdown;
        content.Html = PageContent.Html(markdown);
        content.Text = PageContent.Text(content.Html);
    }

    private static string Teaser(string text) => text.Length <= 280 ? text : text[..280].TrimEnd() + "…";

    private async Task<NewsDto> MapAsync(News news, NodeContent content, CancellationToken cancellationToken) =>
        new(news.Id, news.ParentId, news.Name, content.Markdown, PageContent.Html(content.Markdown), news.PublishAt, news.ExpiresAt, news.Pinned, news.CreatedBy,
            await access.CanAsync(NodePermissions.Update, news.Id, cancellationToken));
}
