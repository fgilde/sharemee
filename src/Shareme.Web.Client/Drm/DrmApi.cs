using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Drm;

namespace Shareme.Web.Client.Drm;

public interface IDrmApi
{
    Task<IReadOnlyList<BillingContactDto>> GetContactsAsync(CancellationToken cancellationToken = default);

    Task<BillingContactDto> SaveContactAsync(Guid? id, BillingContactRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RightsOwnerDto>> GetOwnersAsync(CancellationToken cancellationToken = default);

    Task<RightsOwnerDto> SaveOwnerAsync(Guid? id, RightsOwnerRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContextGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default);

    Task<ContextGroupDto> SaveGroupAsync(Guid? id, ContextGroupRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PriceListDto>> GetPriceListsAsync(CancellationToken cancellationToken = default);

    Task<PriceListDto> SavePriceListAsync(Guid? id, PriceListRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LicenseDto>> GetLicensesAsync(CancellationToken cancellationToken = default);

    Task<LicenseDto> SaveLicenseAsync(Guid? id, LicenseRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BuyoutDto>> GetBuyoutsAsync(Guid licenseId, CancellationToken cancellationToken = default);

    Task<BuyoutDto> SaveBuyoutAsync(Guid licenseId, Guid? id, BuyoutRequest request, CancellationToken cancellationToken = default);

    Task<LicenseDto> AddAssetsAsync(Guid licenseId, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default);

    /// <summary>Deletes one of billing-contacts, rights-owners, context-groups, price-lists, licenses (path segment).</summary>
    Task DeleteAsync(string collection, Guid id, CancellationToken cancellationToken = default);

    Task DeleteBuyoutAsync(Guid licenseId, Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LicenseDto>> GetAssetLicensesAsync(Guid assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LicenseOfferDto>> SearchOffersAsync(OfferSearchRequest request, CancellationToken cancellationToken = default);

    Task<PurchaseDto> PurchaseAsync(PurchaseRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseDto>> GetPurchasesAsync(CancellationToken cancellationToken = default);
}

internal sealed class DrmApi(HttpClient http) : ApiClientBase(http), IDrmApi
{
    private const string Base = "api/v1/drm";

    private static string Url(string collection, Guid? id) => $"{Base}/{collection}" + (id is { } i ? $"/{i}" : string.Empty);

    private Task<T> SaveAsync<T>(string collection, Guid? id, object request, CancellationToken cancellationToken) =>
        SendAsync<T>(id is null ? HttpMethod.Post : HttpMethod.Put, Url(collection, id), request, cancellationToken);

    public async Task<IReadOnlyList<BillingContactDto>> GetContactsAsync(CancellationToken cancellationToken = default) => await GetAsync<BillingContactDto[]>(Url("billing-contacts", null), cancellationToken);

    public Task<BillingContactDto> SaveContactAsync(Guid? id, BillingContactRequest request, CancellationToken cancellationToken = default) => SaveAsync<BillingContactDto>("billing-contacts", id, request, cancellationToken);

    public async Task<IReadOnlyList<RightsOwnerDto>> GetOwnersAsync(CancellationToken cancellationToken = default) => await GetAsync<RightsOwnerDto[]>(Url("rights-owners", null), cancellationToken);

    public Task<RightsOwnerDto> SaveOwnerAsync(Guid? id, RightsOwnerRequest request, CancellationToken cancellationToken = default) => SaveAsync<RightsOwnerDto>("rights-owners", id, request, cancellationToken);

    public async Task<IReadOnlyList<ContextGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default) => await GetAsync<ContextGroupDto[]>(Url("context-groups", null), cancellationToken);

    public Task<ContextGroupDto> SaveGroupAsync(Guid? id, ContextGroupRequest request, CancellationToken cancellationToken = default) => SaveAsync<ContextGroupDto>("context-groups", id, request, cancellationToken);

    public async Task<IReadOnlyList<PriceListDto>> GetPriceListsAsync(CancellationToken cancellationToken = default) => await GetAsync<PriceListDto[]>(Url("price-lists", null), cancellationToken);

    public Task<PriceListDto> SavePriceListAsync(Guid? id, PriceListRequest request, CancellationToken cancellationToken = default) => SaveAsync<PriceListDto>("price-lists", id, request, cancellationToken);

    public async Task<IReadOnlyList<LicenseDto>> GetLicensesAsync(CancellationToken cancellationToken = default) => await GetAsync<LicenseDto[]>(Url("licenses", null), cancellationToken);

    public Task<LicenseDto> SaveLicenseAsync(Guid? id, LicenseRequest request, CancellationToken cancellationToken = default) => SaveAsync<LicenseDto>("licenses", id, request, cancellationToken);

    public async Task<IReadOnlyList<BuyoutDto>> GetBuyoutsAsync(Guid licenseId, CancellationToken cancellationToken = default) => await GetAsync<BuyoutDto[]>($"{Base}/licenses/{licenseId}/buyouts", cancellationToken);

    public Task<BuyoutDto> SaveBuyoutAsync(Guid licenseId, Guid? id, BuyoutRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<BuyoutDto>(id is null ? HttpMethod.Post : HttpMethod.Put, $"{Base}/licenses/{licenseId}/buyouts" + (id is { } i ? $"/{i}" : string.Empty), request, cancellationToken);

    public Task<LicenseDto> AddAssetsAsync(Guid licenseId, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default) =>
        SendAsync<LicenseDto>(HttpMethod.Post, $"{Base}/licenses/{licenseId}/assets", new LicenseAssetsRequest(assetIds), cancellationToken);

    public Task DeleteAsync(string collection, Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, Url(collection, id), null, cancellationToken);

    public Task DeleteBuyoutAsync(Guid licenseId, Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"{Base}/licenses/{licenseId}/buyouts/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<LicenseDto>> GetAssetLicensesAsync(Guid assetId, CancellationToken cancellationToken = default) => await GetAsync<LicenseDto[]>($"{Base}/assets/{assetId}/licenses", cancellationToken);

    public async Task<IReadOnlyList<LicenseOfferDto>> SearchOffersAsync(OfferSearchRequest request, CancellationToken cancellationToken = default) =>
        await SendAsync<LicenseOfferDto[]>(HttpMethod.Post, $"{Base}/offers/search", request, cancellationToken);

    public Task<PurchaseDto> PurchaseAsync(PurchaseRequest request, CancellationToken cancellationToken = default) => SendAsync<PurchaseDto>(HttpMethod.Post, $"{Base}/purchases", request, cancellationToken);

    public async Task<IReadOnlyList<PurchaseDto>> GetPurchasesAsync(CancellationToken cancellationToken = default) => await GetAsync<PurchaseDto[]>($"{Base}/purchases", cancellationToken);
}
