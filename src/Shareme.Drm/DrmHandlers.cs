using System.Globalization;
using System.Security.Cryptography;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shareme.Assets;
using Shareme.Contracts.Drm;
using Shareme.Contracts.Graph;
using Shareme.Graph;
using Shareme.Metadata;

namespace Shareme.Drm;

public static class DrmObjects
{
    public const string BillingContact = "billingContact";
    public const string RightsOwner = "rightsOwner";
    public const string ContextGroup = "contextGroup";
    public const string PriceList = "priceList";
    public const string License = "license";
    public const string Buyout = "buyout";
}

[RequiresPermission(DrmPermissions.Manage)]
public sealed record GetBillingContacts : IQuery<Result<IReadOnlyList<BillingContactDto>>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record SaveBillingContact(Guid? Id, BillingContactRequest Request) : ICommand<Result<BillingContactDto>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record GetRightsOwners : IQuery<Result<IReadOnlyList<RightsOwnerDto>>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record SaveRightsOwner(Guid? Id, RightsOwnerRequest Request) : ICommand<Result<RightsOwnerDto>>;

/// <summary>Groups are offered as alternatives to buyers, so those who see offers may list them.</summary>
[RequiresPermission(DrmPermissions.Offers)]
public sealed record GetContextGroups : IQuery<Result<IReadOnlyList<ContextGroupDto>>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record SaveContextGroup(Guid? Id, ContextGroupRequest Request) : ICommand<Result<ContextGroupDto>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record GetPriceLists : IQuery<Result<IReadOnlyList<PriceListDto>>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record SavePriceList(Guid? Id, PriceListRequest Request) : ICommand<Result<PriceListDto>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record GetLicenses : IQuery<Result<IReadOnlyList<LicenseDto>>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record SaveLicense(Guid? Id, LicenseRequest Request) : ICommand<Result<LicenseDto>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record GetLicenseAssets(Guid Id) : IQuery<Result<IReadOnlyList<Guid>>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record ChangeLicenseAssets(Guid Id, IReadOnlyList<Guid> AssetIds, bool Add) : ICommand<Result<LicenseDto>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record GetBuyouts(Guid LicenseId) : IQuery<Result<IReadOnlyList<BuyoutDto>>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record SaveBuyout(Guid LicenseId, Guid? Id, BuyoutRequest Request) : ICommand<Result<BuyoutDto>>;

[RequiresPermission(DrmPermissions.Manage)]
public sealed record DeleteDrmObject(string Kind, Guid Id) : ICommand<Result>;

/// <summary>The active licenses of an asset, for everyone who can read it.</summary>
public sealed record GetAssetLicenses(Guid AssetId) : IQuery<Result<IReadOnlyList<LicenseDto>>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => AssetId;

    public string RequiredPermission => NodePermissions.Read;
}

[RequiresPermission(DrmPermissions.Offers)]
public sealed record SearchOffers(OfferSearchRequest Request) : IQuery<Result<IReadOnlyList<LicenseOfferDto>>>;

[RequiresPermission(DrmPermissions.Purchase)]
public sealed record CreatePurchase(PurchaseRequest Request) : ICommand<Result<PurchaseDto>>;

/// <summary>Own purchases, or all with <see cref="DrmPermissions.Purchases"/>.</summary>
public sealed record GetPurchases : IQuery<Result<IReadOnlyList<PurchaseDto>>>;

public sealed record GetPurchase(Guid Id) : IQuery<Result<PurchaseDto>>;

internal sealed class MasterDataHandlers(CoworkeeDbContext db, MetadataCatalog catalog, NodeAccess access)
    : IHandler<GetBillingContacts, Result<IReadOnlyList<BillingContactDto>>>,
      IHandler<SaveBillingContact, Result<BillingContactDto>>,
      IHandler<GetRightsOwners, Result<IReadOnlyList<RightsOwnerDto>>>,
      IHandler<SaveRightsOwner, Result<RightsOwnerDto>>,
      IHandler<GetContextGroups, Result<IReadOnlyList<ContextGroupDto>>>,
      IHandler<SaveContextGroup, Result<ContextGroupDto>>,
      IHandler<GetPriceLists, Result<IReadOnlyList<PriceListDto>>>,
      IHandler<SavePriceList, Result<PriceListDto>>,
      IHandler<GetLicenses, Result<IReadOnlyList<LicenseDto>>>,
      IHandler<SaveLicense, Result<LicenseDto>>,
      IHandler<GetLicenseAssets, Result<IReadOnlyList<Guid>>>,
      IHandler<ChangeLicenseAssets, Result<LicenseDto>>,
      IHandler<GetBuyouts, Result<IReadOnlyList<BuyoutDto>>>,
      IHandler<SaveBuyout, Result<BuyoutDto>>,
      IHandler<DeleteDrmObject, Result>,
      IHandler<GetAssetLicenses, Result<IReadOnlyList<LicenseDto>>>
{
    private const int MaxAssetsPerCall = 1000;

    private static Error NotFound(string what) => Error.NotFound("drm.not_found", $"The {what} does not exist.");

    private static bool TooLong(string? value, int length) => value is { } v && v.Length > length;

    public async Task<Result<IReadOnlyList<BillingContactDto>>> HandleAsync(GetBillingContacts query, CancellationToken cancellationToken)
    {
        IReadOnlyList<BillingContactDto> list = (await db.Set<BillingContact>().AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<BillingContactDto>>.Success(list);
    }

    public async Task<Result<BillingContactDto>> HandleAsync(SaveBillingContact command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        if (string.IsNullOrWhiteSpace(r.Name) || TooLong(r.Name, 200) || !System.Net.Mail.MailAddress.TryCreate(r.Email?.Trim(), out _) || TooLong(r.Email, 254)
            || TooLong(r.Address, 1000) || TooLong(r.Iban, 40) || TooLong(r.Bic, 20))
        {
            return Error.Validation("Email", "A name and a valid e-mail address are required.");
        }

        var contact = command.Id is { } id ? await db.Set<BillingContact>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken) : null;
        if (command.Id is not null && contact is null)
        {
            return NotFound("billing contact");
        }

        contact ??= db.Add(new BillingContact { Name = string.Empty, Email = string.Empty }).Entity;
        (contact.Name, contact.Email, contact.Address, contact.Iban, contact.Bic, contact.IsActive) =
            (r.Name.Trim(), r.Email!.Trim(), Trim(r.Address), Trim(r.Iban)?.Replace(" ", string.Empty).ToUpperInvariant(), Trim(r.Bic)?.ToUpperInvariant(), r.IsActive);
        return Map(contact);
    }

    public async Task<Result<IReadOnlyList<RightsOwnerDto>>> HandleAsync(GetRightsOwners query, CancellationToken cancellationToken)
    {
        IReadOnlyList<RightsOwnerDto> list = (await db.Set<RightsOwner>().AsNoTracking().OrderBy(o => o.Name).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<RightsOwnerDto>>.Success(list);
    }

    public async Task<Result<RightsOwnerDto>> HandleAsync(SaveRightsOwner command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        if (string.IsNullOrWhiteSpace(r.Name) || TooLong(r.Name, 200) || TooLong(r.Kind, 100) || TooLong(r.Address, 1000))
        {
            return Error.Validation("Name", "A name of up to 200 characters is required.");
        }

        if (r.BillingContactId is { } contactId && !await db.Set<BillingContact>().AnyAsync(c => c.Id == contactId, cancellationToken))
        {
            return Error.Validation("BillingContactId", "The billing contact does not exist.");
        }

        var owner = command.Id is { } id ? await db.Set<RightsOwner>().SingleOrDefaultAsync(o => o.Id == id, cancellationToken) : null;
        if (command.Id is not null && owner is null)
        {
            return NotFound("rights owner");
        }

        owner ??= db.Add(new RightsOwner { Name = string.Empty }).Entity;
        (owner.Name, owner.Kind, owner.BillingContactId, owner.Address, owner.IsActive) = (r.Name.Trim(), Trim(r.Kind), r.BillingContactId, Trim(r.Address), r.IsActive);
        return Map(owner);
    }

    public async Task<Result<IReadOnlyList<ContextGroupDto>>> HandleAsync(GetContextGroups query, CancellationToken cancellationToken)
    {
        IReadOnlyList<ContextGroupDto> list = (await db.Set<ContextGroup>().AsNoTracking().OrderBy(g => g.Name).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<ContextGroupDto>>.Success(list);
    }

    public async Task<Result<ContextGroupDto>> HandleAsync(SaveContextGroup command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        var known = (await catalog.ContextsAsync(cancellationToken)).Select(c => c.Key).ToHashSet();
        if (string.IsNullOrWhiteSpace(r.Name) || TooLong(r.Name, 200) || r.ContextKeys is not { Count: > 0 } keys || keys.Any(k => !known.Contains(k)))
        {
            return Error.Validation("ContextKeys", "A name and one or more known contexts are required.");
        }

        var group = command.Id is { } id ? await db.Set<ContextGroup>().SingleOrDefaultAsync(g => g.Id == id, cancellationToken) : null;
        if (command.Id is not null && group is null)
        {
            return NotFound("context group");
        }

        group ??= db.Add(new ContextGroup { Name = string.Empty }).Entity;
        (group.Name, group.ContextKeys) = (r.Name.Trim(), keys.Distinct().ToList());
        return Map(group);
    }

    public async Task<Result<IReadOnlyList<PriceListDto>>> HandleAsync(GetPriceLists query, CancellationToken cancellationToken)
    {
        IReadOnlyList<PriceListDto> list = (await db.Set<PriceList>().AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<PriceListDto>>.Success(list);
    }

    public async Task<Result<PriceListDto>> HandleAsync(SavePriceList command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        var entries = r.Entries?.ToList() ?? [];
        var known = (await catalog.ContextsAsync(cancellationToken)).Select(c => c.Key).ToHashSet();
        var groups = await db.Set<ContextGroup>().Select(g => g.Id).ToListAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(r.Name) || TooLong(r.Name, 200) || TooLong(r.Description, 2000) || entries.Count > 500
            || entries.Any(e => (e.ContextKey is null) == (e.GroupId is null) || e.Percentage is < 0 or > 10000
                                || (e.ContextKey is { } key && !known.Contains(key)) || (e.GroupId is { } g && !groups.Contains(g)))
            || entries.GroupBy(e => (e.ContextKey, e.GroupId)).Any(g => g.Count() > 1))
        {
            return Error.Validation("Entries", "A name, and one percentage (0 to 10000) per known context or group.");
        }

        var list = command.Id is { } id ? await db.Set<PriceList>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) : null;
        if (command.Id is not null && list is null)
        {
            return NotFound("price list");
        }

        list ??= db.Add(new PriceList { Name = string.Empty }).Entity;
        (list.Name, list.Description, list.IsActive, list.Entries) = (r.Name.Trim(), Trim(r.Description), r.IsActive, entries);
        return Map(list);
    }

    public async Task<Result<IReadOnlyList<LicenseDto>>> HandleAsync(GetLicenses query, CancellationToken cancellationToken)
    {
        var licenses = await db.Set<License>().AsNoTracking().OrderBy(l => l.Name).ToListAsync(cancellationToken);
        var counts = await db.Set<LicenseAsset>().GroupBy(a => a.LicenseId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        IReadOnlyList<LicenseDto> list = licenses.Select(l => Map(l, counts.GetValueOrDefault(l.Id))).ToList();
        return Result<IReadOnlyList<LicenseDto>>.Success(list);
    }

    public async Task<Result<LicenseDto>> HandleAsync(SaveLicense command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        var rights = r.Rights?.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList() ?? [];
        if (string.IsNullOrWhiteSpace(r.Name) || TooLong(r.Name, 200) || TooLong(r.Description, 4000) || rights.Count > 50 || rights.Any(x => x.Length > 100)
            || !LicenseStatuses.All.Contains(r.Status) || r.PurchaseDays is < 1 or > 36500 || (r.ValidFrom is { } from && r.ValidUntil is { } until && until < from))
        {
            return Error.Validation("Name", "A name, a known status, 1 to 36500 purchase days and a period that ends after it starts.");
        }

        var license = command.Id is { } id ? await db.Set<License>().SingleOrDefaultAsync(l => l.Id == id, cancellationToken) : null;
        if (command.Id is not null && license is null)
        {
            return NotFound("license");
        }

        license ??= db.Add(new License { Name = string.Empty }).Entity;
        (license.Name, license.Description, license.Rights, license.ValidFrom, license.ValidUntil, license.PurchaseDays, license.Status) =
            (r.Name.Trim(), Trim(r.Description), rights, r.ValidFrom, r.ValidUntil, r.PurchaseDays, r.Status);
        var count = command.Id is { } existing ? await db.Set<LicenseAsset>().CountAsync(a => a.LicenseId == existing, cancellationToken) : 0;
        return Map(license, count);
    }

    public async Task<Result<IReadOnlyList<Guid>>> HandleAsync(GetLicenseAssets query, CancellationToken cancellationToken)
    {
        if (!await db.Set<License>().AnyAsync(l => l.Id == query.Id, cancellationToken))
        {
            return NotFound("license");
        }

        // managers see the licensed assets they can read
        var ids = await db.Set<LicenseAsset>().Where(a => a.LicenseId == query.Id).Select(a => a.AssetId).ToListAsync(cancellationToken);
        var assets = await db.Set<Asset>().AsNoTracking().Where(a => ids.Contains(a.Id)).OrderBy(a => a.Name).ToListAsync(cancellationToken);
        IReadOnlyList<Guid> readable = (await AssetSelection.WithPermissionAsync(db, access, assets, NodePermissions.Read, cancellationToken)).Select(a => a.Id).ToList();
        return Result<IReadOnlyList<Guid>>.Success(readable);
    }

    public async Task<Result<LicenseDto>> HandleAsync(ChangeLicenseAssets command, CancellationToken cancellationToken)
    {
        if (await db.Set<License>().SingleOrDefaultAsync(l => l.Id == command.Id, cancellationToken) is not { } license)
        {
            return NotFound("license");
        }

        if (command.AssetIds is not { Count: > 0 and <= MaxAssetsPerCall })
        {
            return Error.Validation("AssetIds", $"Choose 1 to {MaxAssetsPerCall} assets.");
        }

        var ids = command.AssetIds.Distinct().ToList();
        var assets = await db.Set<Asset>().AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
        var readable = (await AssetSelection.WithPermissionAsync(db, access, assets, NodePermissions.Read, cancellationToken)).Select(a => a.Id).ToHashSet();
        if (readable.Count != ids.Count)
        {
            return Error.Validation("AssetIds", "Some of the assets do not exist or are not visible to you.");
        }

        var present = await db.Set<LicenseAsset>().Where(a => a.LicenseId == license.Id && ids.Contains(a.AssetId)).ToListAsync(cancellationToken);
        if (command.Add)
        {
            db.AddRange(ids.Except(present.Select(p => p.AssetId)).Select(id => new LicenseAsset { LicenseId = license.Id, AssetId = id }));
        }
        else
        {
            db.RemoveRange(present);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Map(license, await db.Set<LicenseAsset>().CountAsync(a => a.LicenseId == license.Id, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<BuyoutDto>>> HandleAsync(GetBuyouts query, CancellationToken cancellationToken)
    {
        IReadOnlyList<BuyoutDto> list = (await db.Set<Buyout>().AsNoTracking().Where(b => b.LicenseId == query.LicenseId).OrderBy(b => b.CreatedAt).ToListAsync(cancellationToken))
            .Select(Map).ToList();
        return Result<IReadOnlyList<BuyoutDto>>.Success(list);
    }

    public async Task<Result<BuyoutDto>> HandleAsync(SaveBuyout command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        var currency = r.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper) || r.Basis is < 0 or > 1_000_000_000 || r.FeePercentage is < 0 or > 1000
            || (r.ValidFrom is { } from && r.ValidUntil is { } until && until < from))
        {
            return Error.Validation("Currency", "A three letter currency code, a basis and fee that are not negative and a period that ends after it starts.");
        }

        if (!await db.Set<License>().AnyAsync(l => l.Id == command.LicenseId, cancellationToken))
        {
            return NotFound("license");
        }

        if (!await db.Set<RightsOwner>().AnyAsync(o => o.Id == r.RightsOwnerId, cancellationToken) || !await db.Set<PriceList>().AnyAsync(p => p.Id == r.PriceListId, cancellationToken))
        {
            return Error.Validation("RightsOwnerId", "The rights owner or the price list does not exist.");
        }

        var buyout = command.Id is { } id ? await db.Set<Buyout>().SingleOrDefaultAsync(b => b.Id == id && b.LicenseId == command.LicenseId, cancellationToken) : null;
        if (command.Id is not null && buyout is null)
        {
            return NotFound("buyout");
        }

        buyout ??= db.Add(new Buyout { LicenseId = command.LicenseId, Currency = currency }).Entity;
        (buyout.RightsOwnerId, buyout.PriceListId, buyout.Currency, buyout.Basis, buyout.FeePercentage, buyout.ValidFrom, buyout.ValidUntil) =
            (r.RightsOwnerId, r.PriceListId, currency, r.Basis, r.FeePercentage, r.ValidFrom, r.ValidUntil);
        return Map(buyout);
    }

    public async Task<Result> HandleAsync(DeleteDrmObject command, CancellationToken cancellationToken)
    {
        // what others still refer to stays, so prices and contacts never point into nothing
        var id = command.Id;
        object? entity = command.Kind switch
        {
            DrmObjects.BillingContact => await db.Set<BillingContact>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            DrmObjects.RightsOwner => await db.Set<RightsOwner>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            DrmObjects.ContextGroup => await db.Set<ContextGroup>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            DrmObjects.PriceList => await db.Set<PriceList>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            DrmObjects.License => await db.Set<License>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            DrmObjects.Buyout => await db.Set<Buyout>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken),
            _ => null,
        };
        if (entity is null)
        {
            return NotFound(command.Kind);
        }

        var inUse = command.Kind switch
        {
            DrmObjects.BillingContact => await db.Set<RightsOwner>().AnyAsync(o => o.BillingContactId == id, cancellationToken),
            DrmObjects.RightsOwner => await db.Set<Buyout>().AnyAsync(b => b.RightsOwnerId == id, cancellationToken),
            DrmObjects.PriceList => await db.Set<Buyout>().AnyAsync(b => b.PriceListId == id, cancellationToken),
            DrmObjects.ContextGroup => (await db.Set<PriceList>().AsNoTracking().Select(p => p.Entries).ToListAsync(cancellationToken)).Any(e => e.Any(x => x.GroupId == id)),
            _ => false,
        };
        if (inUse)
        {
            return Error.Conflict("drm.in_use", "Other licensing data still refers to it.");
        }

        db.Remove(entity);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<LicenseDto>>> HandleAsync(GetAssetLicenses query, CancellationToken cancellationToken)
    {
        var ids = await db.Set<LicenseAsset>().Where(a => a.AssetId == query.AssetId).Select(a => a.LicenseId).ToListAsync(cancellationToken);
        IReadOnlyList<LicenseDto> list = (await db.Set<License>().AsNoTracking().Where(l => ids.Contains(l.Id) && l.Status == LicenseStatuses.Active).OrderBy(l => l.Name).ToListAsync(cancellationToken))
            .Select(l => Map(l, 0)).ToList();
        return Result<IReadOnlyList<LicenseDto>>.Success(list);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static BillingContactDto Map(BillingContact c) => new(c.Id, c.Name, c.Email, c.Address, c.Iban, c.Bic, c.IsActive);

    internal static RightsOwnerDto Map(RightsOwner o) => new(o.Id, o.Name, o.Kind, o.BillingContactId, o.Address, o.IsActive);

    internal static ContextGroupDto Map(ContextGroup g) => new(g.Id, g.Name, g.ContextKeys);

    internal static PriceListDto Map(PriceList p) => new(p.Id, p.Name, p.Description, p.IsActive, p.Entries);

    internal static LicenseDto Map(License l, int assets) => new(l.Id, l.Name, l.Description, l.Rights, l.ValidFrom, l.ValidUntil, l.PurchaseDays, l.Status, assets);

    internal static BuyoutDto Map(Buyout b) => new(b.Id, b.LicenseId, b.RightsOwnerId, b.PriceListId, b.Currency, b.Basis, b.FeePercentage, b.ValidFrom, b.ValidUntil);
}

/// <summary>Prices of licenses for a context or a context group, from their valid buyouts and price lists.</summary>
internal sealed class Offers(CoworkeeDbContext db, MetadataCatalog catalog, NodeAccess access, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>The context or group asked for; neither means list prices (100 %).</summary>
    public async Task<Result<(string? Context, ContextGroup? Group)>> TargetAsync(string? contextKey, Guid? groupId, CancellationToken cancellationToken)
    {
        if (contextKey is not null && groupId is not null)
        {
            return Error.Validation("ContextKey", "Choose a context or a context group.");
        }

        if (contextKey is not null && !(await catalog.ContextsAsync(cancellationToken)).Any(c => c.Key == contextKey))
        {
            return Error.Validation("ContextKey", $"There is no context '{contextKey}'.");
        }

        ContextGroup? group = null;
        if (groupId is { } id && (group = await db.Set<ContextGroup>().AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, cancellationToken)) is null)
        {
            return Error.Validation("GroupId", "The context group does not exist.");
        }

        return (contextKey, group);
    }

    public async Task<List<LicenseOfferDto>> ForAsync(IReadOnlyList<License> licenses, string? contextKey, ContextGroup? group, DateOnly day, CancellationToken cancellationToken)
    {
        var ids = licenses.Select(l => l.Id).ToList();
        var buyouts = (await db.Set<Buyout>().AsNoTracking().Where(b => ids.Contains(b.LicenseId)).ToListAsync(cancellationToken)).Where(b => b.IsValidOn(day)).ToList();
        var priceListIds = buyouts.Select(b => b.PriceListId).Distinct().ToList();
        var priceLists = await db.Set<PriceList>().AsNoTracking().Where(p => priceListIds.Contains(p.Id) && p.IsActive).ToDictionaryAsync(p => p.Id, cancellationToken);
        var groups = await db.Set<ContextGroup>().AsNoTracking().ToListAsync(cancellationToken);
        var licensed = await db.Set<LicenseAsset>().Where(a => ids.Contains(a.LicenseId)).Select(a => new { a.LicenseId, a.AssetId }).ToListAsync(cancellationToken);
        var assetIds = licensed.Select(a => a.AssetId).Distinct().ToList();
        var assets = await db.Set<Asset>().AsNoTracking().Where(a => assetIds.Contains(a.Id)).OrderBy(a => a.Name).ToListAsync(cancellationToken);

        // ponytail: one access pass over all licensed assets of the page; per license counts when pages get large
        var readable = (await AssetSelection.WithPermissionAsync(db, access, assets, NodePermissions.Read, cancellationToken)).Select(a => a.Id).ToList();
        var offers = new List<LicenseOfferDto>();
        foreach (var license in licenses)
        {
            var own = buyouts.Where(b => b.LicenseId == license.Id && priceLists.ContainsKey(b.PriceListId)).ToList();
            var inLicense = licensed.Where(a => a.LicenseId == license.Id).Select(a => a.AssetId).ToHashSet();
            var visible = readable.Where(inLicense.Contains).ToList();
            var alternatives = contextKey is null
                ? []
                : groups.Where(g => g.ContextKeys.Contains(contextKey)).Select(g => new OfferAlternativeDto(g.Id, g.Name, Prices(own, priceLists, null, g, groups))).ToList();
            offers.Add(new LicenseOfferDto(license.Id, license.Name, license.Description, license.Rights, license.ValidFrom, license.ValidUntil, license.PurchaseDays, contextKey, group?.Id,
                Prices(own, priceLists, contextKey, group, groups), alternatives, visible.Count, visible.FirstOrDefault() is var first && first != Guid.Empty ? first : null));
        }

        return offers;
    }

    /// <summary>Basis × (1 + fee) × percentage of the context (or group), summed per currency.</summary>
    internal static List<PriceDto> Prices(List<Buyout> buyouts, IReadOnlyDictionary<Guid, PriceList> priceLists, string? contextKey, ContextGroup? group, List<ContextGroup> groups) =>
        buyouts.GroupBy(b => b.Currency)
            .Select(c => new PriceDto(c.Key, decimal.Round(c.Sum(b => b.Basis * (1 + b.FeePercentage / 100) * Percentage(priceLists[b.PriceListId], contextKey, group, groups) / 100), 2)))
            .OrderBy(p => p.Currency)
            .ToList();

    /// <summary>The entry for the context, else the highest of groups holding it; for a group its own entry; else 100 %.</summary>
    internal static decimal Percentage(PriceList list, string? contextKey, ContextGroup? group, List<ContextGroup> groups)
    {
        if (group is not null)
        {
            return list.Entries.FirstOrDefault(e => e.GroupId == group.Id)?.Percentage ?? 100;
        }

        if (contextKey is null)
        {
            return 100;
        }

        if (list.Entries.FirstOrDefault(e => e.ContextKey == contextKey) is { } entry)
        {
            return entry.Percentage;
        }

        var holding = groups.Where(g => g.ContextKeys.Contains(contextKey)).Select(g => g.Id).ToHashSet();
        return list.Entries.Where(e => e.GroupId is { } g && holding.Contains(g)).Select(e => (decimal?)e.Percentage).Max() ?? 100;
    }
}

internal sealed class OfferHandlers(
    CoworkeeDbContext db, Offers offers, NodeAccess access, ICurrentUser currentUser, IUserDirectory users, IMailSender mails, IConfiguration configuration, TimeProvider clock)
    : IHandler<SearchOffers, Result<IReadOnlyList<LicenseOfferDto>>>,
      IHandler<CreatePurchase, Result<PurchaseDto>>,
      IHandler<GetPurchases, Result<IReadOnlyList<PurchaseDto>>>,
      IHandler<GetPurchase, Result<PurchaseDto>>
{
    private const int MaxPurchasedAssets = 10_000;

    public async Task<Result<IReadOnlyList<LicenseOfferDto>>> HandleAsync(SearchOffers query, CancellationToken cancellationToken)
    {
        var r = query.Request;
        if (r.Take is < 1 or > 100 || r.Skip < 0 || r.Term is { Length: > 200 })
        {
            return Error.Validation("Take", "Take 1 to 100 offers.");
        }

        var target = await offers.TargetAsync(r.ContextKey, r.GroupId, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error!;
        }

        var day = r.ValidAt ?? offers.Today;
        var licenses = db.Set<License>().AsNoTracking().Where(l => l.Status == LicenseStatuses.Active && (l.ValidFrom == null || l.ValidFrom <= day) && (l.ValidUntil == null || day <= l.ValidUntil));
        if (!string.IsNullOrWhiteSpace(r.Term))
        {
            var term = "%" + r.Term.Trim() + "%";
            licenses = licenses.Where(l => EF.Functions.ILike(l.Name, term) || (l.Description != null && EF.Functions.ILike(l.Description, term)));
        }

        if (r.AssetId is { } assetId)
        {
            licenses = licenses.Where(l => db.Set<LicenseAsset>().Any(a => a.LicenseId == l.Id && a.AssetId == assetId));
        }

        var page = await licenses.OrderBy(l => l.Name).ToListAsync(cancellationToken);
        if (r.Rights is { Count: > 0 } rights)
        {
            page = page.Where(l => rights.All(l.Rights.Contains)).ToList();
        }

        IReadOnlyList<LicenseOfferDto> result = await offers.ForAsync(page.Skip(r.Skip).Take(r.Take).ToList(), target.Value.Context, target.Value.Group, day, cancellationToken);
        return Result<IReadOnlyList<LicenseOfferDto>>.Success(result);
    }

    public async Task<Result<PurchaseDto>> HandleAsync(CreatePurchase command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        var target = await offers.TargetAsync(r.ContextKey, r.GroupId, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error!;
        }

        if (r.ContextKey is null && r.GroupId is null)
        {
            return Error.Validation("ContextKey", "Choose the context or context group the license is for.");
        }

        var today = offers.Today;
        if (await db.Set<License>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == r.LicenseId && l.Status == LicenseStatuses.Active, cancellationToken) is not { } license || !license.IsValidOn(today))
        {
            return Error.NotFound("drm.not_found", "The license is not on offer.");
        }

        // the price is computed now, never taken from the request
        var offer = (await offers.ForAsync([license], target.Value.Context, target.Value.Group, today, cancellationToken)).Single();
        if (offer.Prices.Count == 0)
        {
            return Error.Validation("LicenseId", "The license has no price for now.");
        }

        var ids = await db.Set<LicenseAsset>().Where(a => a.LicenseId == license.Id).Select(a => a.AssetId).ToListAsync(cancellationToken);
        var assets = await db.Set<Asset>().AsNoTracking().Where(a => ids.Contains(a.Id)).OrderBy(a => a.Name).Take(MaxPurchasedAssets).ToListAsync(cancellationToken);
        var included = (await AssetSelection.WithPermissionAsync(db, access, assets, NodePermissions.Read, cancellationToken)).Select(a => a.Id).ToList();
        var ends = today.AddDays(license.PurchaseDays);
        var purchase = new Purchase
        {
            OrderId = $"P-{today:yyyyMMdd}-{RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 6)}",
            LicenseId = license.Id, LicenseName = license.Name, ContextKey = target.Value.Context, GroupId = target.Value.Group?.Id, BuyerId = currentUser.UserId ?? Guid.Empty,
            StartsOn = today, EndsOn = license.ValidUntil is { } until && until < ends ? until : ends, Prices = [.. offer.Prices], AssetIds = included,
        };
        db.Add(purchase);
        await db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(purchase, target.Value.Group?.Name ?? target.Value.Context!, cancellationToken);
        return Map(purchase);
    }

    public async Task<Result<IReadOnlyList<PurchaseDto>>> HandleAsync(GetPurchases query, CancellationToken cancellationToken)
    {
        var all = await access.IsGlobalAsync(DrmPermissions.Purchases, cancellationToken);
        IReadOnlyList<PurchaseDto> list = (await db.Set<Purchase>().AsNoTracking().Where(p => all || p.BuyerId == currentUser.UserId).OrderByDescending(p => p.CreatedAt).Take(500)
            .ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<PurchaseDto>>.Success(list);
    }

    public async Task<Result<PurchaseDto>> HandleAsync(GetPurchase query, CancellationToken cancellationToken)
    {
        var all = await access.IsGlobalAsync(DrmPermissions.Purchases, cancellationToken);
        return await db.Set<Purchase>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == query.Id && (all || p.BuyerId == currentUser.UserId), cancellationToken) is { } purchase
            ? Map(purchase)
            : Error.NotFound("drm.purchase_not_found", "The purchase does not exist.");
    }

    /// <summary>The buyer, the billing contacts of the rights owners in the price and the configured administration addresses.</summary>
    private async Task NotifyAsync(Purchase purchase, string context, CancellationToken cancellationToken)
    {
        var buyer = (await users.GetDisplayNamesAsync([purchase.BuyerId], cancellationToken)).GetValueOrDefault(purchase.BuyerId) ?? "A customer";
        var model = new
        {
            purchase = new
            {
                order = purchase.OrderId, license = purchase.LicenseName, context, starts = purchase.StartsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ends = purchase.EndsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                prices = string.Join(", ", purchase.Prices.Select(p => $"{p.Amount.ToString("N2", CultureInfo.InvariantCulture)} {p.Currency}")), assets = purchase.AssetIds.Count,
            },
            buyer,
            owner = string.Empty,
        };
        if (await users.GetEmailAsync(purchase.BuyerId, cancellationToken) is { } email)
        {
            await mails.QueueAsync(email, DrmMails.Customer, model, null, cancellationToken);
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var ownerIds = (await db.Set<Buyout>().AsNoTracking().Where(b => b.LicenseId == purchase.LicenseId).ToListAsync(cancellationToken)).Where(b => b.IsValidOn(today)).Select(b => b.RightsOwnerId).Distinct().ToList();
        var contacts = await (from owner in db.Set<RightsOwner>().AsNoTracking()
                              join contact in db.Set<BillingContact>() on owner.BillingContactId equals contact.Id
                              where ownerIds.Contains(owner.Id) && contact.IsActive
                              select new { contact.Email, owner.Name }).ToListAsync(cancellationToken);
        foreach (var contact in contacts.DistinctBy(c => c.Email))
        {
            await mails.QueueAsync(contact.Email, DrmMails.BillingContact, model with { owner = contact.Name }, null, cancellationToken);
        }

        foreach (var admin in (configuration["Shareme:Drm:PurchaseEmails"] ?? string.Empty).Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await mails.QueueAsync(admin, DrmMails.Admin, model, null, cancellationToken);
        }
    }

    private static PurchaseDto Map(Purchase p) =>
        new(p.Id, p.OrderId, p.LicenseId, p.LicenseName, p.ContextKey, p.GroupId, p.BuyerId, p.StartsOn, p.EndsOn, p.Prices, p.AssetIds, p.CreatedAt);
}
