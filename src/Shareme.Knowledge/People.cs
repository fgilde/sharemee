using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Shareme.Assets;
using Shareme.Contracts.Knowledge;
using Shareme.Contracts.Search;
using Shareme.Search;
using IdentityUser = Coworkee.Identity.Domain.User;

namespace Shareme.Knowledge;

/// <summary>What a member tells about themselves in the people directory.</summary>
public sealed class UserProfile : IMultiTenant
{
    public Guid UserId { get; set; }

    public string? Department { get; set; }

    public string? Position { get; set; }

    public string? Phone { get; set; }

    public string? Location { get; set; }

    public string? About { get; set; }

    public string? AvatarBlobKey { get; set; }

    public string? AvatarMimeType { get; set; }

    public Guid TenantId { get; set; }
}

internal static class PeopleEndpoints
{
    public static void Map(WebApplication app)
    {
        var people = app.MapGroup("/api/v1/people").WithTags("People").RequireAuthorization();
        people.MapGet("/", (string? search, int? skip, int? take, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPeople(search, skip ?? 0, take ?? 50), ct).ToHttpResult());
        people.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPerson(id), ct).ToHttpResult());
        people.MapPut("/me", (ProfileRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveProfile(body), ct).ToHttpResult());
        people.MapPut("/me/avatar", (HttpRequest request, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetAvatar(request.Body), ct).ToHttpResult());
        people.MapDelete("/me/avatar", (IDispatcher d, CancellationToken ct) => d.SendAsync(new SetAvatar(null), ct).ToHttpResult());
        people.MapGet("/{id:guid}/avatar", async (Guid id, HttpResponse response, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync(new GetAvatar(id), ct);
            if (!result.IsSuccess)
            {
                return result.Error!.ToProblem();
            }

            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.CacheControl = "private, max-age=300";
            return Results.File(result.Value.Content, result.Value.MimeType);
        });

        app.MapGet("/api/v1/global-search", (string q, IDispatcher d, CancellationToken ct) => d.SendAsync(new GlobalSearch(q), ct).ToHttpResult()).WithTags("Search").RequireAuthorization();
    }
}

/// <summary>Active members of the current tenant, for every member.</summary>
public sealed record GetPeople(string? Search, int Skip, int Take) : IQuery<Result<IReadOnlyList<PersonDto>>>;

public sealed record GetPerson(Guid UserId) : IQuery<Result<PersonDto>>;

public sealed record SaveProfile(ProfileRequest Request) : ICommand<Result<PersonDto>>;

/// <summary>A PNG, JPEG or WebP of up to 2 MB; null removes the avatar.</summary>
public sealed record SetAvatar(Stream? Content) : ICommand<Result>;

public sealed record GetAvatar(Guid UserId) : IQuery<Result<BlobDownload>>;

public sealed record GlobalSearch(string Term) : IQuery<Result<GlobalSearchDto>>;

internal sealed class PeopleHandlers(CoworkeeDbContext db, ICurrentUser currentUser, IBlobStorage storage, IDispatcher dispatcher)
    : IHandler<GetPeople, Result<IReadOnlyList<PersonDto>>>,
      IHandler<GetPerson, Result<PersonDto>>,
      IHandler<SaveProfile, Result<PersonDto>>,
      IHandler<SetAvatar, Result>,
      IHandler<GetAvatar, Result<BlobDownload>>,
      IHandler<GlobalSearch, Result<GlobalSearchDto>>
{
    private const long MaxAvatar = 2L * 1024 * 1024;
    private static readonly Error NotFound = Error.NotFound("people.not_found", "The person does not exist.");

    private IQueryable<IdentityUser> Members => db.Set<IdentityUser>().AsNoTracking().Where(u => u.TenantId == currentUser.TenantId && u.IsActive);

    public async Task<Result<IReadOnlyList<PersonDto>>> HandleAsync(GetPeople query, CancellationToken cancellationToken)
    {
        if (query.Take is < 1 or > 200 || query.Skip < 0 || query.Search is { Length: > 200 })
        {
            return Error.Validation("Take", "Take 1 to 200 people with a search of up to 200 characters.");
        }

        return Result<IReadOnlyList<PersonDto>>.Success(await FindAsync(query.Search, query.Skip, query.Take, cancellationToken));
    }

    public async Task<Result<PersonDto>> HandleAsync(GetPerson query, CancellationToken cancellationToken) =>
        (await MapAsync(Members.Where(u => u.Id == query.UserId), cancellationToken)).FirstOrDefault() is { } person ? person : NotFound;

    public async Task<Result<PersonDto>> HandleAsync(SaveProfile command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        if (new[] { r.Department, r.Position, r.Phone, r.Location }.Any(v => v is { Length: > 200 }) || r.About is { Length: > 2000 })
        {
            return Error.Validation("About", "Fields take up to 200 characters, about up to 2000.");
        }

        var profile = await ProfileAsync(cancellationToken);
        (profile.Department, profile.Position, profile.Phone, profile.Location, profile.About) = (Trim(r.Department), Trim(r.Position), Trim(r.Phone), Trim(r.Location), Trim(r.About));
        await db.SaveChangesAsync(cancellationToken);
        return await HandleAsync(new GetPerson(currentUser.UserId!.Value), cancellationToken);
    }

    public async Task<Result> HandleAsync(SetAvatar command, CancellationToken cancellationToken)
    {
        var profile = await ProfileAsync(cancellationToken);
        if (command.Content is null)
        {
            if (profile.AvatarBlobKey is { } old)
            {
                await storage.DeleteAsync(old, cancellationToken);
            }

            (profile.AvatarBlobKey, profile.AvatarMimeType) = (null, null);
            return Result.Success();
        }

        // the content decides what it is, not what the browser claims
        var bytes = await RenditionLimits.ReadAsync(command.Content, MaxAvatar, cancellationToken);
        var mime = bytes is null ? null : RenditionLimits.Sniff(bytes);
        if (bytes is null || mime is not ("image/png" or "image/jpeg" or "image/webp"))
        {
            return Error.Validation("Content", "Use a PNG, JPEG or WebP image of up to 2 MB.");
        }

        var key = $"avatars/{currentUser.TenantId}/{currentUser.UserId}/{Guid.NewGuid():N}";
        await storage.PutAsync(key, new MemoryStream(bytes), mime, cancellationToken);
        if (profile.AvatarBlobKey is { } previous)
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        (profile.AvatarBlobKey, profile.AvatarMimeType) = (key, mime);
        return Result.Success();
    }

    public async Task<Result<BlobDownload>> HandleAsync(GetAvatar query, CancellationToken cancellationToken)
    {
        if (!await Members.AnyAsync(u => u.Id == query.UserId, cancellationToken)
            || await db.Set<UserProfile>().AsNoTracking().SingleOrDefaultAsync(p => p.UserId == query.UserId, cancellationToken) is not { AvatarBlobKey: { } key } profile
            || await storage.OpenReadAsync(key, cancellationToken) is not { } content)
        {
            return NotFound;
        }

        return new BlobDownload(content, "avatar", profile.AvatarMimeType ?? "image/png");
    }

    public async Task<Result<GlobalSearchDto>> HandleAsync(GlobalSearch query, CancellationToken cancellationToken)
    {
        var term = query.Term?.Trim() ?? string.Empty;
        if (term.Length is 0 or > 200)
        {
            return Error.Validation("Term", "Search for 1 to 200 characters.");
        }

        var nodes = await dispatcher.SendAsync(new SearchNodes(new SearchRequest(term, Size: 20)), cancellationToken);
        if (!nodes.IsSuccess)
        {
            return nodes.Error!;
        }

        return new GlobalSearchDto(nodes.Value.Items, nodes.Value.Total, await FindAsync(term, 0, 10, cancellationToken));
    }

    /// <summary>Members whose name, e-mail or profile matches, by name.</summary>
    private async Task<List<PersonDto>> FindAsync(string? search, int skip, int take, CancellationToken cancellationToken)
    {
        var members = Members;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            var profiles = db.Set<UserProfile>().Where(p => EF.Functions.ILike(p.Department ?? string.Empty, like) || EF.Functions.ILike(p.Position ?? string.Empty, like)
                                                            || EF.Functions.ILike(p.Location ?? string.Empty, like)).Select(p => p.UserId);
            members = members.Where(u => EF.Functions.ILike(u.FirstName ?? string.Empty, like) || EF.Functions.ILike(u.LastName ?? string.Empty, like)
                                         || EF.Functions.ILike(u.Email ?? string.Empty, like) || profiles.Contains(u.Id));
        }

        return await MapAsync(members.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.Email).Skip(skip).Take(take), cancellationToken);
    }

    private async Task<List<PersonDto>> MapAsync(IQueryable<IdentityUser> users, CancellationToken cancellationToken)
    {
        var list = await users.Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToListAsync(cancellationToken);
        var ids = list.Select(u => u.Id).ToList();
        var profiles = await db.Set<UserProfile>().AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, cancellationToken);
        return list.Select(u =>
        {
            var p = profiles.GetValueOrDefault(u.Id);
            var name = $"{u.FirstName} {u.LastName}".Trim();
            return new PersonDto(u.Id, name.Length > 0 ? name : u.Email ?? "?", u.Email, p?.Department, p?.Position, p?.Phone, p?.Location, p?.About, p?.AvatarBlobKey is not null);
        }).ToList();
    }

    private async Task<UserProfile> ProfileAsync(CancellationToken cancellationToken) =>
        await db.Set<UserProfile>().SingleOrDefaultAsync(p => p.UserId == currentUser.UserId, cancellationToken)
        ?? db.Add(new UserProfile { UserId = currentUser.UserId ?? Guid.Empty }).Entity;

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
