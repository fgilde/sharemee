using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Assets;
using Shareme.Web.Client.Assets;
using Shareme.Web.Client.Pages;

namespace Shareme.Web.Client.Tests;

public sealed class RenditionsAdminTests : BunitContext
{
    private readonly IRenditionsApi _api = Substitute.For<IRenditionsApi>();

    public RenditionsAdminTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(NSubstitute.Substitute.For<Coworkee.Client.Blazor.Localization.ILocalizationApi>());
        Services.AddScoped<Coworkee.Client.Blazor.Localization.CoworkeeLocalizer>();
        MudBlazor.Extensions.ServiceCollectionExtensions.AddMudExtensions(Services);
        Services.AddSingleton(NSubstitute.Substitute.For<IDownloadsApi>());
        Services.AddSingleton(_api);
        AddAuthorization().SetAuthorized("Ada");
        var watermark = new WatermarkDto(Guid.CreateVersion7(), "Logo", true, "southeast", 50, 25, "confidential", null);
        _api.GetWatermarksAsync(default).ReturnsForAnyArgs([watermark]);
        _api.GetTypesAsync(default).ReturnsForAnyArgs([
            new DerivationTypeDto(Guid.CreateVersion7(), "web", "Web", 1200, 800, DerivationFormats.Jpeg, 85, true, true, false, watermark.Id, [AssetTypeKeys.Image]),
        ]);
    }

    [Fact]
    public async Task Lists_types_with_their_watermark_and_creates_new_ones()
    {
        var page = Render<RenditionsAdmin>();

        page.WaitForAssertion(() => page.Find("[data-type='web']").TextContent.ShouldContain("Logo"));
        page.Find("[data-type='web']").TextContent.ShouldContain("always, on request");
        page.Find("[data-watermark='Logo']").TextContent.ShouldContain("confidential = true");

        page.Find("input[data-testid='type-key'], [data-testid='type-key'] input").Change("social");
        page.Find("input[data-testid='type-name'], [data-testid='type-name'] input").Change("Social");
        await page.Find("[data-testid='save-type']").ClickAsync(new());

        await _api.Received().SaveTypeAsync(null, Arg.Is<DerivationTypeRequest>(r => r.Key == "social" && r.Name == "Social" && r.Width == 1200 && r.AssetTypeKeys!.Count == 0), Arg.Any<CancellationToken>());
    }
}
