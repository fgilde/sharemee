using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Drm;
using Shareme.Contracts.Metadata;
using Shareme.Web.Client.Drm;
using Shareme.Web.Client.Metadata;
using Shareme.Web.Client.Pages;

namespace Shareme.Web.Client.Tests;

public sealed class LicensesTests : BunitContext
{
    private readonly IDrmApi _api = Substitute.For<IDrmApi>();
    private readonly LicenseOfferDto _offer = new(Guid.CreateVersion7(), "Spring campaign", "Spring images", ["web"], null, null, 30, null, null,
        [new PriceDto("EUR", 1320m)], [], 2, null);

    public LicensesTests()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_api);
        var metadata = Substitute.For<IMetadataApi>();
        metadata.GetContextsAsync(default).ReturnsForAnyArgs([new UserContextDto("dach", "DACH", false)]);
        Services.AddSingleton(metadata);
        AddAuthorization().SetAuthorized("Ada");
        _api.GetGroupsAsync(default).ReturnsForAnyArgs([]);
        _api.SearchOffersAsync(default!, default).ReturnsForAnyArgs([_offer]);
        _api.PurchaseAsync(default!, default).ReturnsForAnyArgs(new PurchaseDto(Guid.CreateVersion7(), "P-20261004-ABCDEF", _offer.LicenseId, _offer.Name, "dach", null, "DACH", Guid.Empty,
            new DateOnly(2026, 10, 4), new DateOnly(2026, 11, 3), _offer.Prices, [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Offers_show_their_prices_and_need_a_context_to_be_bought()
    {
        var page = Render<Licenses>();

        page.WaitForAssertion(() => page.Find("[data-offer='Spring campaign']").TextContent.ShouldContain("1,320.00 EUR"));
        page.Find("[data-offer='Spring campaign'] button[data-testid='buy'], [data-offer='Spring campaign'] [data-testid='buy']").HasAttribute("disabled").ShouldBeTrue();
    }
}
