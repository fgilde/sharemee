using System.Security.Cryptography;
using System.Text;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shareme.Assets;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Workspaces;
using Shareme.Graph;

namespace Shareme.Workspaces;

/// <summary>A share of a workspace for people outside: what they may fetch and until when; it acts with the rights of the person who made it.</summary>
public sealed class Share : AuditedEntity, IMultiTenant
{
    public Guid WorkspaceId { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset ValidUntil { get; set; }

    public bool AllowOriginals { get; set; }

    public List<string> RenditionKinds { get; set; } = [];

    public Guid OwnerId { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>One recipient's link; only the hash of the token is kept, the token itself is in the mail.</summary>
[NotAudited]
public sealed class ShareRecipient : Entity, IMultiTenant
{
    public Guid ShareId { get; set; }

    public required string Email { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset? LastAccessAt { get; set; }

    public Guid TenantId { get; set; }
}

internal static class ShareTokens
{
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public const string MailTemplate = "Shares.Invitation";
}

internal sealed class ShareMailTemplates : IMailTemplateContributor
{
    public void Define(MailTemplateContext context) =>
        context.Add(new MailTemplateDefinition(ShareTokens.MailTemplate, "Share invitation",
            new { share = new { name = "Spring campaign", valid_until = "2026-12-31", url = "https://example.com/s/token" }, sender = "Ada Lovelace" },
            new Dictionary<string, MailTemplateContent>
            {
                ["en"] = new("{{ sender }} shared \"{{ share.name }}\" with you",
                    "<p>{{ sender }} shared <strong>{{ share.name }}</strong> with you.</p><p><a href=\"{{ share.url }}\">Open the files</a> (until {{ share.valid_until }}).</p>"),
                ["de"] = new("{{ sender }} hat „{{ share.name }}“ mit Ihnen geteilt",
                    "<p>{{ sender }} hat <strong>{{ share.name }}</strong> mit Ihnen geteilt.</p><p><a href=\"{{ share.url }}\">Dateien öffnen</a> (bis {{ share.valid_until }}).</p>"),
            }));
}

public sealed record GetShares(Guid Id) : WorkspaceRequest(Id, NodePermissions.ManagePermissions), IQuery<Result<IReadOnlyList<ShareDto>>>;

public sealed record CreateShare(Guid Id, ShareRequest Request) : WorkspaceRequest(Id, NodePermissions.ManagePermissions), ICommand<Result<ShareDto>>;

public sealed record RevokeShare(Guid ShareId) : ICommand<Result>;

public sealed record AddShareRecipient(Guid ShareId, ShareRecipientRequest Request) : ICommand<Result<ShareDto>>;

public sealed record RemoveShareRecipient(Guid ShareId, Guid RecipientId) : ICommand<Result>;

/// <summary>Anonymous: the token is the authorisation.</summary>
public sealed record OpenShare(string Token) : IQuery<Result<PublicShareDto>>;

public sealed record FetchSharedFile(string Token, Guid AssetId, string Kind) : IQuery<Result<BlobDownload>>;

public sealed record PackShare(string Token, string Kind) : ICommand<Result<DownloadPackageDto>>;

internal sealed class ShareHandlers(
    CoworkeeDbContext db, ICurrentUser currentUser, NodeAccess access, IMailSender mails, IUserDirectory users, IBlobStorage storage, IConfiguration configuration, TimeProvider clock)
    : IHandler<GetShares, Result<IReadOnlyList<ShareDto>>>,
      IHandler<CreateShare, Result<ShareDto>>,
      IHandler<RevokeShare, Result>,
      IHandler<AddShareRecipient, Result<ShareDto>>,
      IHandler<RemoveShareRecipient, Result>,
      IHandler<OpenShare, Result<PublicShareDto>>,
      IHandler<FetchSharedFile, Result<BlobDownload>>,
      IHandler<PackShare, Result<DownloadPackageDto>>
{
    private const int MaxRecipients = 50;

    private static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(365);

    private static readonly Error NotFound = Error.NotFound("shares.not_found", "The share does not exist.");

    // a closed or expired link looks the same as a wrong one
    private static readonly Error Gone = Error.NotFound("shares.unavailable", "This link is not valid (any more).");

    public async Task<Result<IReadOnlyList<ShareDto>>> HandleAsync(GetShares query, CancellationToken cancellationToken)
    {
        var shares = await db.Set<Share>().AsNoTracking().Where(s => s.WorkspaceId == query.Id).OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);
        var ids = shares.Select(s => s.Id).ToList();
        var recipients = (await db.Set<ShareRecipient>().AsNoTracking().Where(r => ids.Contains(r.ShareId)).ToListAsync(cancellationToken)).ToLookup(r => r.ShareId);
        IReadOnlyList<ShareDto> dtos = shares.Select(s => Map(s, recipients[s.Id])).ToList();
        return Result<IReadOnlyList<ShareDto>>.Success(dtos);
    }

    public async Task<Result<ShareDto>> HandleAsync(CreateShare command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (currentUser is not { UserId: { } userId, TenantId: not null })
        {
            return Error.Forbidden("auth.forbidden", "Shares are made by users of an organisation.");
        }

        if (await db.Set<Workspace>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == command.Id && !w.IsTemplate, cancellationToken) is not { } workspace)
        {
            return Error.NotFound("workspaces.not_found", "The workspace does not exist.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Error.Validation("Name", "A name of up to 200 characters is required.");
        }

        var now = clock.GetUtcNow();
        if (request.ValidUntil <= now || request.ValidUntil > now + MaxLifetime)
        {
            return Error.Validation("ValidUntil", "Choose an end within the next year.");
        }

        var kinds = request.RenditionKinds?.Distinct().ToList() ?? [DerivationKinds.Preview];
        if (kinds.Contains(DerivationKinds.Still) || kinds.Any(k => !DerivationKinds.System.Contains(k)) && !await KnownKindsAsync(kinds, cancellationToken))
        {
            return Error.Validation("RenditionKinds", "Choose existing renditions.");
        }

        if (!request.AllowOriginals && kinds.Count == 0)
        {
            return Error.Validation("RenditionKinds", "Share originals or at least one rendition.");
        }

        if (Emails(request.RecipientEmails) is not { Count: > 0 and <= MaxRecipients } emails)
        {
            return Error.Validation("RecipientEmails", $"Name 1 to {MaxRecipients} valid e-mail addresses.");
        }

        var share = new Share
        {
            WorkspaceId = workspace.Id, Name = request.Name.Trim(), ValidUntil = request.ValidUntil, AllowOriginals = request.AllowOriginals, RenditionKinds = kinds, OwnerId = userId,
        };
        db.Add(share);
        var recipients = new List<ShareRecipient>();
        foreach (var email in emails)
        {
            recipients.Add(await InviteAsync(share, email, cancellationToken));
        }

        return Map(share, recipients);
    }

    public async Task<Result> HandleAsync(RevokeShare command, CancellationToken cancellationToken)
    {
        if (await ManagedAsync(command.ShareId, cancellationToken) is not { } share)
        {
            return NotFound;
        }

        share.RevokedAt ??= clock.GetUtcNow();
        return Result.Success();
    }

    public async Task<Result<ShareDto>> HandleAsync(AddShareRecipient command, CancellationToken cancellationToken)
    {
        if (await ManagedAsync(command.ShareId, cancellationToken) is not { RevokedAt: null } share)
        {
            return NotFound;
        }

        var existing = await db.Set<ShareRecipient>().Where(r => r.ShareId == share.Id).ToListAsync(cancellationToken);
        if (Emails([command.Request.Email]) is not [var email] || existing.Count >= MaxRecipients)
        {
            return Error.Validation("Email", "A valid e-mail address is required (up to 50 recipients).");
        }

        if (existing.Any(r => r.Email == email))
        {
            return Error.Conflict("shares.recipient_exists", "This address has a link already.");
        }

        existing.Add(await InviteAsync(share, email, cancellationToken));
        return Map(share, existing);
    }

    public async Task<Result> HandleAsync(RemoveShareRecipient command, CancellationToken cancellationToken)
    {
        if (await ManagedAsync(command.ShareId, cancellationToken) is null
            || await db.Set<ShareRecipient>().SingleOrDefaultAsync(r => r.Id == command.RecipientId && r.ShareId == command.ShareId, cancellationToken) is not { } recipient)
        {
            return NotFound;
        }

        db.Remove(recipient);
        return Result.Success();
    }

    public async Task<Result<PublicShareDto>> HandleAsync(OpenShare query, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(query.Token, cancellationToken) is not { } resolved)
        {
            return Gone;
        }

        var (share, recipient) = resolved;
        recipient.LastAccessAt = clock.GetUtcNow();
        using (ActAs(share))
        {
            var assets = await SharedAssetsAsync(share, cancellationToken);
            var versionIds = assets.Select(a => a.CurrentVersionId).OfType<Guid>().ToList();
            var ready = (await db.Set<Derivation>().AsNoTracking().Where(d => versionIds.Contains(d.VersionId) && d.State == DerivationStates.Ready && share.RenditionKinds.Contains(d.Kind))
                .Select(d => new { d.VersionId, d.Kind }).ToListAsync(cancellationToken)).ToLookup(d => d.VersionId, d => d.Kind);
            var mimeTypes = await db.Set<AssetVersion>().AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.MimeType, cancellationToken);
            var downloadable = share.AllowOriginals
                ? (await AssetSelection.WithPermissionAsync(db, access, assets, AssetPermissions.Download, cancellationToken)).Select(a => a.Id).ToHashSet()
                : [];
            var items = assets.Select(a => new PublicShareItemDto(
                a.Id, a.Name, a.CurrentVersionId is { } v ? mimeTypes.GetValueOrDefault(v) : null,
                [.. downloadable.Contains(a.Id) ? [DownloadKinds.Original] : Array.Empty<string>(), .. a.CurrentVersionId is { } version ? ready[version] : []])).ToList();
            await db.SaveChangesAsync(cancellationToken);
            return new PublicShareDto(share.Name, share.ValidUntil, share.AllowOriginals, share.RenditionKinds, items);
        }
    }

    public async Task<Result<BlobDownload>> HandleAsync(FetchSharedFile query, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(query.Token, cancellationToken) is not { } resolved)
        {
            return Gone;
        }

        var share = resolved.Share;
        using (ActAs(share))
        {
            if ((await SharedAssetsAsync(share, cancellationToken)).FirstOrDefault(a => a.Id == query.AssetId) is not { CurrentVersionId: { } versionId } asset)
            {
                return Gone;
            }

            var version = await db.Set<AssetVersion>().AsNoTracking().SingleAsync(v => v.Id == versionId, cancellationToken);
            if (query.Kind == DownloadKinds.Original)
            {
                if (!share.AllowOriginals || !await access.CanAsync(AssetPermissions.Download, asset.Id, cancellationToken)
                    || await storage.OpenReadAsync(version.BlobKey, cancellationToken) is not { } original)
                {
                    return Gone;
                }

                return new BlobDownload(original, version.FileName, version.MimeType, version.Sha256, version.CreatedAt);
            }

            if (!share.RenditionKinds.Contains(query.Kind)
                || await db.Set<Derivation>().AsNoTracking().SingleOrDefaultAsync(d => d.VersionId == versionId && d.Kind == query.Kind && d.State == DerivationStates.Ready, cancellationToken)
                    is not { BlobKey: { } key } derivation
                || await storage.OpenReadAsync(key, cancellationToken) is not { } content)
            {
                return Gone;
            }

            return new BlobDownload(content, Path.GetFileNameWithoutExtension(version.FileName) + "-" + derivation.Kind + DerivationFormats.Extension(derivation.MimeType),
                derivation.MimeType ?? "image/webp", version.Sha256 + "-" + derivation.Kind, version.CreatedAt);
        }
    }

    public async Task<Result<DownloadPackageDto>> HandleAsync(PackShare command, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(command.Token, cancellationToken) is not { } resolved)
        {
            return Gone;
        }

        var share = resolved.Share;
        using (ActAs(share))
        {
            var original = command.Kind == DownloadKinds.Original;
            if (original ? !share.AllowOriginals : !share.RenditionKinds.Contains(command.Kind))
            {
                return Error.Validation("Kind", "This share does not include these files.");
            }

            var assets = await SharedAssetsAsync(share, cancellationToken);
            if (original)
            {
                assets = (await AssetSelection.WithPermissionAsync(db, access, assets, AssetPermissions.Download, cancellationToken)).ToList();
            }

            var versions = await AssetSelection.CurrentVersionsAsync(db, assets, cancellationToken);
            var versionIds = versions.Values.Select(v => v.Id).ToList();
            var derivations = original ? [] : await db.Set<Derivation>().AsNoTracking()
                .Where(d => versionIds.Contains(d.VersionId) && d.Kind == command.Kind && d.State == DerivationStates.Ready && d.BlobKey != null).ToDictionaryAsync(d => d.VersionId, cancellationToken);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<DownloadItem>();
            foreach (var asset in assets.Where(a => versions.ContainsKey(a.Id)))
            {
                var version = versions[asset.Id];
                if (original)
                {
                    items.Add(new DownloadItem(version.BlobKey, Unique(FileNames.Clean(version.FileName), taken)));
                }
                else if (derivations.TryGetValue(version.Id, out var derivation))
                {
                    items.Add(new DownloadItem(derivation.BlobKey!, Unique(FileNames.Clean(Path.GetFileNameWithoutExtension(version.FileName)) + DerivationFormats.Extension(derivation.MimeType), taken)));
                }
            }

            if (items.Count == 0)
            {
                return Error.Validation("Kind", "None of these files is ready yet.");
            }

            var package = new DownloadPackage
            {
                UserId = share.OwnerId,
                Name = FileNames.Clean(share.Name),
                Items = items,
                ExpiresAt = clock.GetUtcNow().AddHours(1),
            };
            db.Add(package);

            // saved as the owner: an anonymous save has no tenant for the package
            await db.SaveChangesAsync(cancellationToken);
            return new DownloadPackageDto($"/api/v1/downloads/{package.Token}", package.ExpiresAt, items.Count, 0);
        }
    }

    /// <summary>The share and recipient of an open, unexpired link whose workspace still exists.</summary>
    private async Task<(Share Share, ShareRecipient Recipient)?> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100)
        {
            return null;
        }

        var hash = ShareTokens.Hash(token);
        var now = clock.GetUtcNow();
        if (await db.Set<ShareRecipient>().IgnoreQueryFilters().SingleOrDefaultAsync(r => r.TokenHash == hash, cancellationToken) is not { } recipient
            || await db.Set<Share>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s => s.Id == recipient.ShareId && s.RevokedAt == null && s.ValidUntil > now, cancellationToken)
                is not { } share)
        {
            return null;
        }

        return (share, recipient);
    }

    /// <summary>Public requests run with the rights of the share's owner: what they can no longer read, the share no longer shows.</summary>
    private static IDisposable ActAs(Share share) => CurrentUserScope.Begin(new ImpersonatedUser(share.OwnerId, share.TenantId));

    /// <summary>The workspace's own assets and the linked ones the owner may read; nothing when the workspace is gone or the owner lost access.</summary>
    private async Task<List<Asset>> SharedAssetsAsync(Share share, CancellationToken cancellationToken)
    {
        if (!await access.CanAsync(NodePermissions.Read, share.WorkspaceId, cancellationToken))
        {
            return [];
        }

        var linkedIds = db.Set<Edge>().Where(e => e.EdgeTypeId == EdgeType.LinkId && e.FromNodeId == share.WorkspaceId).Select(e => e.ToNodeId);
        var assets = await db.Set<Asset>().AsNoTracking().Where(a => (a.ParentId == share.WorkspaceId || linkedIds.Contains(a.Id)) && a.CurrentVersionId != null)
            .OrderBy(a => a.Name).Take(2000).ToListAsync(cancellationToken);
        return (await AssetSelection.WithPermissionAsync(db, access, assets, NodePermissions.Read, cancellationToken)).ToList();
    }

    private async Task<Share?> ManagedAsync(Guid shareId, CancellationToken cancellationToken) =>
        await db.Set<Share>().SingleOrDefaultAsync(s => s.Id == shareId, cancellationToken) is { } share
        && await access.CanAsync(NodePermissions.ManagePermissions, share.WorkspaceId, cancellationToken)
            ? share
            : null;

    private async Task<ShareRecipient> InviteAsync(Share share, string email, CancellationToken cancellationToken)
    {
        var token = ShareTokens.New();
        var recipient = new ShareRecipient { ShareId = share.Id, Email = email, TokenHash = ShareTokens.Hash(token) };
        db.Add(recipient);
        var root = configuration["Coworkee:Notifications:PublicAppUrl"]?.TrimEnd('/') ?? string.Empty;
        var sender = currentUser.UserId is { } userId ? (await users.GetDisplayNamesAsync([userId], cancellationToken)).GetValueOrDefault(userId) : null;
        await mails.QueueAsync(email, ShareTokens.MailTemplate, new
        {
            share = new { name = share.Name, valid_until = share.ValidUntil.ToString("yyyy-MM-dd"), url = $"{root}/s/{token}" },
            sender = sender ?? "Someone",
        }, null, cancellationToken);
        return recipient;
    }

    private async Task<bool> KnownKindsAsync(List<string> kinds, CancellationToken cancellationToken)
    {
        var custom = kinds.Where(k => !DerivationKinds.System.Contains(k)).ToList();
        return await db.Set<DerivationType>().CountAsync(t => custom.Contains(t.Key), cancellationToken) == custom.Count;
    }

    private static List<string> Emails(IEnumerable<string> emails) =>
        emails.Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length is > 3 and <= 254 && System.Net.Mail.MailAddress.TryCreate(e, out var parsed) && parsed.Address == e).Distinct().ToList();

    private static string Unique(string name, HashSet<string> taken) => FileNames.Unique(name, Path.GetExtension(name).TrimStart('.'), taken);

    private static ShareDto Map(Share s, IEnumerable<ShareRecipient> recipients) => new(
        s.Id, s.WorkspaceId, s.Name, s.ValidUntil, s.AllowOriginals, s.RenditionKinds, recipients.Select(r => new ShareRecipientDto(r.Id, r.Email, r.LastAccessAt)).ToList(), s.CreatedAt, s.RevokedAt);
}
