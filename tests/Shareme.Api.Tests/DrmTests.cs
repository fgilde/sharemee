using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Contracts.Drm;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;

namespace Shareme.Api.Tests;

public sealed class DrmTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _folder;

    public async ValueTask InitializeAsync()
    {
        _setup = await api.SetupAsync();
        foreach (var (key, name) in new[] { ("dach", "DACH"), ("us", "USA") })
        {
            (await Admin.PostAsJsonAsync("/api/v1/metadata/contexts", new UserContextDto(key, name, false), Ct)).EnsureSuccessStatusCode();
        }

        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        _folder = (await (await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(root, "Licensed"), Ct)).Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Offers_are_priced_per_context_and_purchases_send_mails()
    {
        var (license, assets) = await LicenseAsync();

        var dach = (await OffersAsync(new OfferSearchRequest(ContextKey: "dach"))).Single();
        dach.Prices.ShouldBe([new PriceDto("EUR", 1320.00m), new PriceDto("USD", 600.00m)]);
        dach.AssetCount.ShouldBe(2);
        dach.PreviewAssetId.ShouldNotBeNull();
        var europe = dach.Alternatives.Single();
        (europe.GroupName, europe.Prices[0]).ShouldBe(("Europe", new PriceDto("EUR", 880.00m)));
        (await OffersAsync(new OfferSearchRequest(ContextKey: "us"))).Single().Prices.ShouldBe([new PriceDto("EUR", 1100.00m), new PriceDto("USD", 500.00m)]);
        (await OffersAsync(new OfferSearchRequest(Rights: ["web"], Term: "spring"))).ShouldHaveSingleItem();
        (await OffersAsync(new OfferSearchRequest(Rights: ["video"]))).ShouldBeEmpty();
        (await OffersAsync(new OfferSearchRequest(AssetId: assets[0]))).ShouldHaveSingleItem();
        (await OffersAsync(new OfferSearchRequest(ValidAt: new DateOnly(2001, 1, 1)))).ShouldBeEmpty();
        (await Admin.GetFromJsonAsync<LicenseDto[]>($"/api/v1/drm/assets/{assets[0]}/licenses", Ct))!.Single().Id.ShouldBe(license.Id);

        var response = await Admin.PostAsJsonAsync("/api/v1/drm/purchases", new PurchaseRequest(license.Id, ContextKey: "dach"), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var purchase = (await response.Content.ReadFromJsonAsync<PurchaseDto>(Ct))!;
        purchase.OrderId.ShouldMatch(@"^P-\d{8}-[A-Z2-9]{6}$");
        purchase.Prices.ShouldBe(dach.Prices);
        (purchase.EndsOn.DayNumber - purchase.StartsOn.DayNumber).ShouldBe(30);
        purchase.AssetIds.ShouldBe(assets, ignoreOrder: true);
        (await Admin.GetFromJsonAsync<PurchaseDto[]>("/api/v1/drm/purchases", Ct))!.Single().Id.ShouldBe(purchase.Id);

        var mails = await MailsAsync(purchase.OrderId, 3);
        mails.ShouldContain(m => m.To == "billing@studio.test" && m.TemplateName == "Drm.PurchaseBillingContact" && m.HtmlBody.Contains("Studio North"));
        mails.ShouldContain(m => m.To == "licensing@acme.test" && m.TemplateName == "Drm.PurchaseAdmin");
        mails.ShouldContain(m => m.TemplateName == "Drm.PurchaseCustomer" && m.HtmlBody.Contains("1,320.00 EUR"));
    }

    [Fact]
    public async Task Master_data_is_checked_and_licensing_needs_its_permissions()
    {
        var (license, _) = await LicenseAsync();
        var owners = (await Admin.GetFromJsonAsync<RightsOwnerDto[]>("/api/v1/drm/rights-owners", Ct))!;
        (await Admin.DeleteAsync($"/api/v1/drm/rights-owners/{owners.Single().Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Admin.PostAsJsonAsync($"/api/v1/drm/licenses/{license.Id}/buyouts", new BuyoutRequest(owners.Single().Id, Guid.NewGuid(), "EUR", 10), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/drm/licenses", new LicenseRequest("Odd", ValidFrom: new DateOnly(2026, 5, 1), ValidUntil: new DateOnly(2026, 4, 1)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/drm/context-groups", new ContextGroupRequest("Nowhere", ["mars"]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/drm/billing-contacts", new BillingContactRequest("No mail", "nope"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/drm/purchases", new PurchaseRequest(license.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var draft = await (await Admin.PostAsJsonAsync("/api/v1/drm/licenses", new LicenseRequest("Draft"), Ct)).Content.ReadFromJsonAsync<LicenseDto>(Ct);
        (await Admin.PostAsJsonAsync("/api/v1/drm/purchases", new PurchaseRequest(draft!.Id, ContextKey: "dach"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var member = api.As(user.Id, _setup.TenantId);
        (await member.PostAsJsonAsync("/api/v1/drm/offers/search", new OfferSearchRequest(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync("/api/v1/drm/purchases", new PurchaseRequest(license.Id, ContextKey: "dach"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.GetAsync("/api/v1/drm/licenses", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.GetFromJsonAsync<PurchaseDto[]>("/api/v1/drm/purchases", Ct))!.ShouldBeEmpty();

        // a buyer with the purchase right sees offers without the files they cannot read, and only their own purchases
        var buyer = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Buyer " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{buyer}", new NameListRequest([DrmPermissions.Purchase]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([buyer]), Ct)).EnsureSuccessStatusCode();
        var offer = (await (await member.PostAsJsonAsync("/api/v1/drm/offers/search", new OfferSearchRequest(ContextKey: "us"), Ct)).Content.ReadFromJsonAsync<LicenseOfferDto[]>(Ct))!.Single();
        (offer.AssetCount, offer.PreviewAssetId).ShouldBe((0, null));
        (await member.PostAsJsonAsync("/api/v1/drm/purchases", new PurchaseRequest(license.Id, ContextKey: "us"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.GetFromJsonAsync<PurchaseDto[]>("/api/v1/drm/purchases", Ct))!.ShouldHaveSingleItem().AssetIds.ShouldBeEmpty();
    }

    private async Task<(LicenseDto License, List<Guid> Assets)> LicenseAsync()
    {
        var contact = await PostAsync<BillingContactDto>("/api/v1/drm/billing-contacts", new BillingContactRequest("Studio North billing", "billing@studio.test", Iban: "de89 3704 0044 0532 0130 00"));
        contact.Iban.ShouldBe("DE89370400440532013000");
        var owner = await PostAsync<RightsOwnerDto>("/api/v1/drm/rights-owners", new RightsOwnerRequest("Studio North", "agency", contact.Id));
        var europe = await PostAsync<ContextGroupDto>("/api/v1/drm/context-groups", new ContextGroupRequest("Europe", ["dach"]));
        var prices = await PostAsync<PriceListDto>("/api/v1/drm/price-lists", new PriceListRequest("Standard", Entries: [new PriceListEntry("dach", null, 120), new PriceListEntry(null, europe.Id, 80)]));
        var license = await PostAsync<LicenseDto>("/api/v1/drm/licenses", new LicenseRequest("Spring campaign", "Images of the spring campaign", ["web", "print"], ValidFrom: new DateOnly(2026, 1, 1), PurchaseDays: 30, Status: LicenseStatuses.Active));
        await PostAsync<BuyoutDto>($"/api/v1/drm/licenses/{license.Id}/buyouts", new BuyoutRequest(owner.Id, prices.Id, "eur", 1000, 10));
        await PostAsync<BuyoutDto>($"/api/v1/drm/licenses/{license.Id}/buyouts", new BuyoutRequest(owner.Id, prices.Id, "USD", 500));
        await PostAsync<BuyoutDto>($"/api/v1/drm/licenses/{license.Id}/buyouts", new BuyoutRequest(owner.Id, prices.Id, "USD", 9999, ValidUntil: new DateOnly(2020, 1, 1)));

        var assets = new List<Guid>();
        foreach (var name in new[] { "a.txt", "b.txt" })
        {
            assets.Add((await WorkspaceTests.UploadAsync(Admin, name, "licensed"u8.ToArray(), _folder)).AssetId);
        }

        var assigned = await PostAsync<LicenseDto>($"/api/v1/drm/licenses/{license.Id}/assets", new LicenseAssetsRequest(assets));
        assigned.AssetCount.ShouldBe(2);
        return (assigned, assets);
    }

    private async Task<LicenseOfferDto[]> OffersAsync(OfferSearchRequest request)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/drm/offers/search", request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LicenseOfferDto[]>(Ct))!;
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Admin.PostAsJsonAsync(url, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private async Task<List<Coworkee.Mailing.OutgoingMail>> MailsAsync(string orderId, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            await using (var scope = api.Factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Shareme.Infrastructure.ShareMeDbContext>();
                var mails = await db.Set<Coworkee.Mailing.OutgoingMail>().IgnoreQueryFilters().Where(m => m.Subject.Contains(orderId)).ToListAsync(Ct);
                if (mails.Count >= count)
                {
                    return mails;
                }
            }

            DateTime.UtcNow.ShouldBeLessThan(deadline, "the purchase mails were not queued");
            await Task.Delay(200, Ct);
        }
    }
}
