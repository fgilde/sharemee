using System.Net;
using System.Text.RegularExpressions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;
using Shareme.Graph;
using Shareme.Metadata;

namespace Shareme.Knowledge;

/// <summary>Knowledge base pages in the node tree, with versions, diffs and restore.</summary>
[DependsOn(typeof(ShareMeMetadataModule))]
public sealed class ShareMeKnowledgeModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(ShareMeKnowledgeModule).Assembly);
        context.Services.AddSingleton<IModelContributor, KnowledgeModelContributor>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        IntranetEndpoints.Map(app);
        PeopleEndpoints.Map(app);
        var pages = app.MapGroup("/api/v1/pages").WithTags("Pages").RequireAuthorization();
        pages.MapPost("/", (PageRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreatePage(body.ParentId, body), ct).ToHttpResult());
        pages.MapPost("/preview", (PagePreviewRequest body) => body.Markdown is { Length: <= PageContent.MaxMarkdown } markdown
            ? Results.Ok(new PagePreviewDto(PageContent.Html(markdown)))
            : Results.BadRequest());
        pages.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPage(id), ct).ToHttpResult());
        pages.MapPut("/{id:guid}", (Guid id, PageUpdateRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdatePage(id, body), ct).ToHttpResult());
        pages.MapGet("/{id:guid}/versions", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPageVersions(id), ct).ToHttpResult());
        pages.MapGet("/{id:guid}/versions/{number:int}", (Guid id, int number, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPageVersion(id, number), ct).ToHttpResult());
        pages.MapGet("/{id:guid}/diff", (Guid id, int from, int to, IDispatcher d, CancellationToken ct) => d.SendAsync(new DiffPage(id, from, to), ct).ToHttpResult());
        pages.MapPost("/{id:guid}/versions/{number:int}/restore", (Guid id, int number, int expectedVersion, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RestorePageVersion(id, number, expectedVersion), ct).ToHttpResult());
    }
}

/// <summary>An earlier (or the current) content of a page; the page's history, so the audit trail does not copy it again.</summary>
[NotAudited]
public sealed class PageVersion : AuditedEntity, IMultiTenant
{
    public Guid PageId { get; set; }

    public int Number { get; set; }

    public required string Title { get; set; }

    public required string Markdown { get; set; }

    public string? Comment { get; set; }

    public Guid TenantId { get; set; }
}

internal sealed class KnowledgeModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>(profile =>
        {
            profile.ToTable("UserProfiles", "sm");
            profile.HasKey(p => p.UserId);
            foreach (var name in new[] { nameof(UserProfile.Department), nameof(UserProfile.Position), nameof(UserProfile.Phone), nameof(UserProfile.Location) })
            {
                profile.Property(name).HasMaxLength(200);
            }

            profile.Property(p => p.About).HasMaxLength(2000);
            profile.Property(p => p.AvatarBlobKey).HasMaxLength(300);
            profile.Property(p => p.AvatarMimeType).HasMaxLength(50);
        });
        modelBuilder.Entity<Favorite>(favorite =>
        {
            favorite.ToTable("Favorites", "sm");
            favorite.HasIndex(f => new { f.UserId, f.NodeId }).IsUnique();
            favorite.HasOne<Node>().WithMany().HasForeignKey(f => f.NodeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<HomeLayout>(layout =>
        {
            layout.ToTable("HomeLayouts", "sm");
            layout.Property(l => l.Widgets).HasColumnType("jsonb");
            layout.HasIndex(l => new { l.TenantId, l.UserId }).IsUnique().AreNullsDistinct(false);
        });
        modelBuilder.Entity<PageVersion>(version =>
        {
            version.ToTable("PageVersions", "sm");
            version.Property(v => v.Title).HasMaxLength(400);
            version.Property(v => v.Comment).HasMaxLength(500);
            version.HasIndex(v => new { v.PageId, v.Number }).IsUnique();
            version.HasOne<Node>().WithMany().HasForeignKey(v => v.PageId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Markdown to safe HTML (raw HTML in Markdown is not rendered, links and images only to http(s), mailto and relative addresses) and to plain text for search and diffs.</summary>
public static partial class PageContent
{
    public const int MaxMarkdown = 200_000;

    // no raw HTML, no generic attributes ({.class #id}), no media embeds
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml()
        .UsePipeTables().UseGridTables().UseEmphasisExtras().UseTaskLists().UseAutoLinks().UseListExtras().UseFootnotes().UseDefinitionLists().UseAbbreviations()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    [GeneratedRegex("<(br|/p|/h[1-6]|/li|/tr|/div|/blockquote|/pre)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    public static string Html(string markdown) => Sanitizer.Sanitize(Markdig.Markdown.ToHtml(markdown, Pipeline));

    public static string Text(string html) =>
        WebUtility.HtmlDecode(Tags().Replace(LineBreaks().Replace(html, "\n"), string.Empty)).Replace("\r", string.Empty).Trim();

    /// <summary>Images only from Shareme files or https; links to http(s), mail and paths of this app; never protocol relative.</summary>
    public static string? AllowedUrl(string tag, string url)
    {
        if (url.StartsWith("//", StringComparison.Ordinal) || url.StartsWith("\\", StringComparison.Ordinal))
        {
            return null;
        }

        if (tag.Equals("img", StringComparison.OrdinalIgnoreCase))
        {
            return url.StartsWith("/api/v1/assets/", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : null;
        }

        return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || url.StartsWith('/') || url.StartsWith('#')
            ? url
            : null;
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        // no styles, classes or ids: nothing that reaches into the app's own layout or elements
        foreach (var attribute in new[] { "style", "class", "id" })
        {
            sanitizer.AllowedAttributes.Remove(attribute);
        }

        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.FilterUrl += (_, e) => e.SanitizedUrl = e.SanitizedUrl is { } url ? AllowedUrl(e.Tag.TagName, url) : null;
        return sanitizer;
    }
}

/// <summary>A line diff (longest common subsequence) of two texts.</summary>
public static class LineDiff
{
    /// <summary>The changed middle may span this many line pairs; bigger changes show as removed and added.</summary>
    public const long MaxCells = 2_000_000;

    public static List<DiffLineDto> Compare(string before, string after)
    {
        var all = before.Split('\n');
        var other = after.Split('\n');

        // common start and end first, so a small edit in a long page stays small
        var start = 0;
        while (start < all.Length && start < other.Length && all[start] == other[start])
        {
            start++;
        }

        var end = 0;
        while (end < all.Length - start && end < other.Length - start && all[^(end + 1)] == other[^(end + 1)])
        {
            end++;
        }

        var head = all[..start].Select(l => new DiffLineDto(DiffKinds.Same, l));
        var tail = all[(all.Length - end)..].Select(l => new DiffLineDto(DiffKinds.Same, l));
        var a = all[start..(all.Length - end)];
        var b = other[start..(other.Length - end)];
        if ((long)a.Length * b.Length > MaxCells)
        {
            return [.. head, .. a.Select(l => new DiffLineDto(DiffKinds.Removed, l)), .. b.Select(l => new DiffLineDto(DiffKinds.Added, l)), .. tail];
        }

        return [.. head, .. Middle(a, b), .. tail];
    }

    private static List<DiffLineDto> Middle(string[] a, string[] b)
    {

        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = a[i] == b[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var lines = new List<DiffLineDto>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                lines.Add(new DiffLineDto(DiffKinds.Same, a[x++]));
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                lines.Add(new DiffLineDto(DiffKinds.Removed, a[x++]));
            }
            else
            {
                lines.Add(new DiffLineDto(DiffKinds.Added, b[y++]));
            }
        }

        lines.AddRange(a.Skip(x).Select(l => new DiffLineDto(DiffKinds.Removed, l)));
        lines.AddRange(b.Skip(y).Select(l => new DiffLineDto(DiffKinds.Added, l)));
        return lines;
    }
}

public sealed record CreatePage(Guid ParentId, PageRequest Request) : ICommand<Result<PageDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => ParentId;

    public string RequiredPermission => NodePermissions.Create;
}

public abstract record PageRequestBase(Guid Id, string Permission) : IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => Permission;
}

public sealed record GetPage(Guid Id) : PageRequestBase(Id, NodePermissions.Read), IQuery<Result<PageDto>>;

public sealed record UpdatePage(Guid Id, PageUpdateRequest Request) : PageRequestBase(Id, NodePermissions.Update), ICommand<Result<PageDto>>;

public sealed record GetPageVersions(Guid Id) : PageRequestBase(Id, NodePermissions.Read), IQuery<Result<IReadOnlyList<PageVersionDto>>>;

public sealed record GetPageVersion(Guid Id, int Number) : PageRequestBase(Id, NodePermissions.Read), IQuery<Result<PageVersionContentDto>>;

public sealed record DiffPage(Guid Id, int From, int To) : PageRequestBase(Id, NodePermissions.Read), IQuery<Result<IReadOnlyList<DiffLineDto>>>;

public sealed record RestorePageVersion(Guid Id, int Number, int ExpectedVersion) : PageRequestBase(Id, NodePermissions.Update), ICommand<Result<PageDto>>;

internal sealed class PageHandlers(CoworkeeDbContext db, NodeAccess access)
    : IHandler<CreatePage, Result<PageDto>>,
      IHandler<GetPage, Result<PageDto>>,
      IHandler<UpdatePage, Result<PageDto>>,
      IHandler<GetPageVersions, Result<IReadOnlyList<PageVersionDto>>>,
      IHandler<GetPageVersion, Result<PageVersionContentDto>>,
      IHandler<DiffPage, Result<IReadOnlyList<DiffLineDto>>>,
      IHandler<RestorePageVersion, Result<PageDto>>
{
    private static readonly Error NotFound = Error.NotFound("pages.not_found", "The page does not exist.");

    private static Error? Invalid(string? title, string? markdown) =>
        string.IsNullOrWhiteSpace(title) || title.Length > 400 || markdown is null || markdown.Length > PageContent.MaxMarkdown
            ? Error.Validation("Title", $"A title of up to 400 characters and up to {PageContent.MaxMarkdown / 1000} KB of text are required.")
            : null;

    public async Task<Result<PageDto>> HandleAsync(CreatePage command, CancellationToken cancellationToken)
    {
        if (Invalid(command.Request.Title, command.Request.Markdown) is { } invalid)
        {
            return invalid;
        }

        if (await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == command.ParentId, cancellationToken) is not { } parent)
        {
            return Error.NotFound("nodes.not_found", "The folder does not exist.");
        }

        // where files may go: folders, open workspaces (not templates, collections or finished ones), custom nodes
        if (!(parent.AcceptsFiles() || parent.Semantic == NodeSemantic.Custom))
        {
            return Error.Validation("ParentId", "Pages live in folders and open workspaces.");
        }

        var page = new Page { NodeTypeId = NodeType.PageTypeId, Name = command.Request.Title.Trim() };
        page.SortIndex = await db.Set<Node>().Where(n => n.ParentId == parent.Id).Select(n => (int?)n.SortIndex).MaxAsync(cancellationToken) + 1 ?? 0;
        page.PlaceUnder(parent);
        var content = new NodeContent { NodeId = page.Id };
        db.Add(content);
        Apply(page, content, command.Request.Title, command.Request.Markdown, null);
        page.MarkChanged("created");
        db.Add(page);
        await db.SaveChangesAsync(cancellationToken);
        return await MapAsync(page, content, cancellationToken);
    }

    public async Task<Result<PageDto>> HandleAsync(GetPage query, CancellationToken cancellationToken) =>
        await db.Set<Page>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == query.Id, cancellationToken) is { } page
            ? await MapAsync(page, await db.Set<NodeContent>().AsNoTracking().SingleOrDefaultAsync(c => c.NodeId == page.Id, cancellationToken) ?? new NodeContent { NodeId = page.Id }, cancellationToken)
            : NotFound;

    public async Task<Result<PageDto>> HandleAsync(UpdatePage command, CancellationToken cancellationToken)
    {
        if (Invalid(command.Request.Title, command.Request.Markdown) is { } invalid)
        {
            return invalid;
        }

        if (await db.Set<Page>().SingleOrDefaultAsync(p => p.Id == command.Id, cancellationToken) is not { } page)
        {
            return NotFound;
        }

        if (page.PageVersion != command.Request.ExpectedVersion)
        {
            return Changed(page.PageVersion);
        }

        var content = await db.Set<NodeContent>().SingleOrDefaultAsync(c => c.NodeId == page.Id, cancellationToken) ?? db.Add(new NodeContent { NodeId = page.Id }).Entity;
        if (page.Name == command.Request.Title.Trim() && content.Markdown == command.Request.Markdown)
        {
            return await MapAsync(page, content, cancellationToken);
        }

        Apply(page, content, command.Request.Title, command.Request.Markdown, command.Request.Comment);
        page.MarkChanged("content changed");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // a save at the same moment took this version number (unique index) or changed the page (row version)
            return Changed(command.Request.ExpectedVersion + 1);
        }

        return await MapAsync(page, content, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<PageVersionDto>>> HandleAsync(GetPageVersions query, CancellationToken cancellationToken)
    {
        if (!await db.Set<Page>().AnyAsync(p => p.Id == query.Id, cancellationToken))
        {
            return NotFound;
        }

        IReadOnlyList<PageVersionDto> versions = await db.Set<PageVersion>().AsNoTracking().Where(v => v.PageId == query.Id).OrderByDescending(v => v.Number)
            .Select(v => new PageVersionDto(v.Number, v.Title, v.Comment, v.CreatedBy, v.CreatedAt)).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<PageVersionDto>>.Success(versions);
    }

    public async Task<Result<PageVersionContentDto>> HandleAsync(GetPageVersion query, CancellationToken cancellationToken) =>
        await VersionAsync(query.Id, query.Number, cancellationToken) is { } v
            ? new PageVersionContentDto(v.Number, v.Title, v.Markdown, PageContent.Html(v.Markdown), v.Comment, v.CreatedAt)
            : Error.NotFound("pages.version_not_found", "The version does not exist.");

    public async Task<Result<IReadOnlyList<DiffLineDto>>> HandleAsync(DiffPage query, CancellationToken cancellationToken)
    {
        if (await VersionAsync(query.Id, query.From, cancellationToken) is not { } from || await VersionAsync(query.Id, query.To, cancellationToken) is not { } to)
        {
            return Error.NotFound("pages.version_not_found", "The version does not exist.");
        }

        IReadOnlyList<DiffLineDto> lines = LineDiff.Compare($"# {from.Title}\n{from.Markdown}", $"# {to.Title}\n{to.Markdown}");
        return Result<IReadOnlyList<DiffLineDto>>.Success(lines);
    }

    public async Task<Result<PageDto>> HandleAsync(RestorePageVersion command, CancellationToken cancellationToken)
    {
        if (await VersionAsync(command.Id, command.Number, cancellationToken) is not { } version)
        {
            return Error.NotFound("pages.version_not_found", "The version does not exist.");
        }

        return await HandleAsync(new UpdatePage(command.Id, new PageUpdateRequest(version.Title, version.Markdown, command.ExpectedVersion, $"Restored version {version.Number}")), cancellationToken);
    }

    private static Error Changed(int version) =>
        Error.Conflict("pages.changed", $"Someone saved version {version} in the meantime; reload and apply your changes again.");

    /// <summary>The new content goes onto the page and into a new version.</summary>
    private void Apply(Page page, NodeContent content, string title, string markdown, string? comment)
    {
        page.Name = title.Trim();
        content.Markdown = markdown;
        content.Html = PageContent.Html(markdown);
        content.Text = PageContent.Text(content.Html);
        page.PageVersion++;
        db.Add(new PageVersion { PageId = page.Id, Number = page.PageVersion, Title = page.Name, Markdown = markdown, Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()[..Math.Min(comment.Trim().Length, 500)] });
    }

    private Task<PageVersion?> VersionAsync(Guid pageId, int number, CancellationToken cancellationToken) =>
        db.Set<PageVersion>().AsNoTracking().SingleOrDefaultAsync(v => v.PageId == pageId && v.Number == number, cancellationToken);

    private async Task<PageDto> MapAsync(Page page, NodeContent content, CancellationToken cancellationToken) =>
        new(page.Id, page.ParentId, page.Name, content.Markdown, PageContent.Html(content.Markdown), page.PageVersion, page.ModifiedAt ?? page.CreatedAt, page.ModifiedBy ?? page.CreatedBy,
            await access.CanAsync(NodePermissions.Update, page.Id, cancellationToken));
}
