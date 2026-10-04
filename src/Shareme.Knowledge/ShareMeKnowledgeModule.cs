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
        var pages = app.MapGroup("/api/v1/pages").WithTags("Pages").RequireAuthorization();
        pages.MapPost("/", (PageRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreatePage(body.ParentId, body), ct).ToHttpResult());
        pages.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPage(id), ct).ToHttpResult());
        pages.MapPut("/{id:guid}", (Guid id, PageUpdateRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdatePage(id, body), ct).ToHttpResult());
        pages.MapGet("/{id:guid}/versions", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPageVersions(id), ct).ToHttpResult());
        pages.MapGet("/{id:guid}/versions/{number:int}", (Guid id, int number, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPageVersion(id, number), ct).ToHttpResult());
        pages.MapGet("/{id:guid}/diff", (Guid id, int from, int to, IDispatcher d, CancellationToken ct) => d.SendAsync(new DiffPage(id, from, to), ct).ToHttpResult());
        pages.MapPost("/{id:guid}/versions/{number:int}/restore", (Guid id, int number, int expectedVersion, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RestorePageVersion(id, number, expectedVersion), ct).ToHttpResult());
    }
}

/// <summary>An earlier (or the current) content of a page.</summary>
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
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<PageVersion>(version =>
        {
            version.ToTable("PageVersions", "sm");
            version.Property(v => v.Title).HasMaxLength(400);
            version.Property(v => v.Comment).HasMaxLength(500);
            version.HasIndex(v => new { v.PageId, v.Number }).IsUnique();
            version.HasOne<Node>().WithMany().HasForeignKey(v => v.PageId).OnDelete(DeleteBehavior.Cascade);
        });
}

/// <summary>Markdown to safe HTML (raw HTML in Markdown is not rendered, links and images only to http(s), mailto and relative addresses) and to plain text for search and diffs.</summary>
public static partial class PageContent
{
    public const int MaxMarkdown = 500_000;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UseAdvancedExtensions().Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    [GeneratedRegex("<(br|/p|/h[1-6]|/li|/tr|/div|/blockquote|/pre)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    public static string Html(string markdown) => Sanitizer.Sanitize(Markdig.Markdown.ToHtml(markdown, Pipeline));

    public static string Text(string html) =>
        WebUtility.HtmlDecode(Tags().Replace(LineBreaks().Replace(html, "\n"), string.Empty)).Replace("\r", string.Empty).Trim();

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedAttributes.Remove("style");
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
        {
            sanitizer.AllowedSchemes.Add(scheme);
        }

        sanitizer.AllowedAttributes.Add("id");
        return sanitizer;
    }
}

/// <summary>A line diff (longest common subsequence) of two texts.</summary>
public static class LineDiff
{
    public const int MaxLines = 5000;

    public static List<DiffLineDto> Compare(string before, string after)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');
        if (a.Length > MaxLines || b.Length > MaxLines)
        {
            return [.. a.Select(l => new DiffLineDto(DiffKinds.Removed, l)), .. b.Select(l => new DiffLineDto(DiffKinds.Added, l))];
        }

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

        if (parent.Semantic is not (NodeSemantic.Folder or NodeSemantic.Workspace or NodeSemantic.Custom))
        {
            return Error.Validation("ParentId", "Pages live in folders and workspaces.");
        }

        var page = new Page { NodeTypeId = NodeType.PageTypeId, Name = command.Request.Title.Trim() };
        page.SortIndex = await db.Set<Node>().Where(n => n.ParentId == parent.Id).Select(n => (int?)n.SortIndex).MaxAsync(cancellationToken) + 1 ?? 0;
        page.PlaceUnder(parent);
        Apply(page, command.Request.Title, command.Request.Markdown, null);
        page.MarkChanged("created");
        db.Add(page);
        await db.SaveChangesAsync(cancellationToken);
        return await MapAsync(page, cancellationToken);
    }

    public async Task<Result<PageDto>> HandleAsync(GetPage query, CancellationToken cancellationToken) =>
        await db.Set<Page>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == query.Id, cancellationToken) is { } page ? await MapAsync(page, cancellationToken) : NotFound;

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
            return Error.Conflict("pages.changed", $"Someone saved version {page.PageVersion} in the meantime; reload and apply your changes again.");
        }

        if (page.Name == command.Request.Title.Trim() && page.Markdown == command.Request.Markdown)
        {
            return await MapAsync(page, cancellationToken);
        }

        Apply(page, command.Request.Title, command.Request.Markdown, command.Request.Comment);
        page.MarkChanged("content changed");
        await db.SaveChangesAsync(cancellationToken);
        return await MapAsync(page, cancellationToken);
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

    /// <summary>The new content goes onto the page and into a new version.</summary>
    private void Apply(Page page, string title, string markdown, string? comment)
    {
        page.Name = title.Trim();
        page.Markdown = markdown;
        page.Html = PageContent.Html(markdown);
        page.Text = PageContent.Text(page.Html);
        page.PageVersion++;
        db.Add(new PageVersion { PageId = page.Id, Number = page.PageVersion, Title = page.Name, Markdown = markdown, Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()[..Math.Min(comment.Trim().Length, 500)] });
    }

    private Task<PageVersion?> VersionAsync(Guid pageId, int number, CancellationToken cancellationToken) =>
        db.Set<PageVersion>().AsNoTracking().SingleOrDefaultAsync(v => v.PageId == pageId && v.Number == number, cancellationToken);

    private async Task<PageDto> MapAsync(Page page, CancellationToken cancellationToken) =>
        new(page.Id, page.ParentId, page.Name, page.Markdown, page.Html, page.PageVersion, page.ModifiedAt ?? page.CreatedAt, page.ModifiedBy ?? page.CreatedBy,
            await access.CanAsync(NodePermissions.Update, page.Id, cancellationToken));
}
