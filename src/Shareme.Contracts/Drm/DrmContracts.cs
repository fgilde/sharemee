namespace Shareme.Contracts.Drm;

public static class DrmPermissions
{
    public const string GroupName = "Drm";

    /// <summary>Master data: contacts, rights owners, context groups, price lists, licenses, buyouts, licensed assets.</summary>
    public const string Manage = "Drm.Manage";

    /// <summary>See license offers with their prices.</summary>
    public const string Offers = "Drm.Offers";

    public const string Purchase = "Drm.Purchase";

    /// <summary>See every purchase, not only one's own.</summary>
    public const string Purchases = "Drm.Purchases";
}

public static class LicenseStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Inactive = "inactive";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Draft, Active, Inactive };
}

public sealed record BillingContactDto(Guid Id, string Name, string Email, string? Address, string? Iban, string? Bic, bool IsActive);

public sealed record BillingContactRequest(string Name, string Email, string? Address = null, string? Iban = null, string? Bic = null, bool IsActive = true);

public sealed record RightsOwnerDto(Guid Id, string Name, string? Kind, Guid? BillingContactId, string? Address, bool IsActive);

public sealed record RightsOwnerRequest(string Name, string? Kind = null, Guid? BillingContactId = null, string? Address = null, bool IsActive = true);

/// <summary>User contexts (keys of the metadata catalog, such as markets) priced together.</summary>
public sealed record ContextGroupDto(Guid Id, string Name, IReadOnlyList<string> ContextKeys);

public sealed record ContextGroupRequest(string Name, IReadOnlyList<string> ContextKeys);

/// <summary>A percentage for one context or one context group.</summary>
public sealed record PriceListEntry(string? ContextKey, Guid? GroupId, decimal Percentage);

public sealed record PriceListDto(Guid Id, string Name, string? Description, bool IsActive, IReadOnlyList<PriceListEntry> Entries);

public sealed record PriceListRequest(string Name, string? Description = null, bool IsActive = true, IReadOnlyList<PriceListEntry>? Entries = null);

public sealed record LicenseDto(
    Guid Id, string Name, string? Description, IReadOnlyList<string> Rights, DateOnly? ValidFrom, DateOnly? ValidUntil, int PurchaseDays, string Status, int AssetCount);

public sealed record LicenseRequest(
    string Name, string? Description = null, IReadOnlyList<string>? Rights = null, DateOnly? ValidFrom = null, DateOnly? ValidUntil = null, int PurchaseDays = 365,
    string Status = LicenseStatuses.Draft);

/// <summary>What a rights owner gets for a license, in one currency, priced by a price list.</summary>
public sealed record BuyoutDto(Guid Id, Guid LicenseId, Guid RightsOwnerId, Guid PriceListId, string Currency, decimal Basis, decimal FeePercentage, DateOnly? ValidFrom, DateOnly? ValidUntil);

public sealed record BuyoutRequest(Guid RightsOwnerId, Guid PriceListId, string Currency, decimal Basis, decimal FeePercentage = 0, DateOnly? ValidFrom = null, DateOnly? ValidUntil = null);

public sealed record LicenseAssetsRequest(IReadOnlyList<Guid> AssetIds);

public sealed record PriceDto(string Currency, decimal Amount);

/// <summary>Offers for a context (or a context group); <see cref="Rights"/> must all be granted by a license.</summary>
public sealed record OfferSearchRequest(
    string? Term = null, string? ContextKey = null, Guid? GroupId = null, IReadOnlyList<string>? Rights = null, DateOnly? ValidAt = null, Guid? AssetId = null, int Skip = 0, int Take = 50);

public sealed record OfferAlternativeDto(Guid GroupId, string GroupName, IReadOnlyList<PriceDto> Prices);

public sealed record LicenseOfferDto(
    Guid LicenseId, string Name, string? Description, IReadOnlyList<string> Rights, DateOnly? ValidFrom, DateOnly? ValidUntil, int PurchaseDays, string? ContextKey, Guid? GroupId,
    IReadOnlyList<PriceDto> Prices, IReadOnlyList<OfferAlternativeDto> Alternatives, int AssetCount, Guid? PreviewAssetId);

public sealed record PurchaseRequest(Guid LicenseId, string? ContextKey = null, Guid? GroupId = null);

public sealed record PurchaseDto(
    Guid Id, string OrderId, Guid LicenseId, string LicenseName, string? ContextKey, Guid? GroupId, string? TargetName, Guid BuyerId, DateOnly StartsOn, DateOnly EndsOn, IReadOnlyList<PriceDto> Prices,
    IReadOnlyList<Guid> AssetIds, DateTimeOffset CreatedAt);
