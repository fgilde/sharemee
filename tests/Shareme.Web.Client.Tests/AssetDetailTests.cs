using Bunit;
using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Web.Client.Assets;
using Shareme.Web.Client.Graph;
using Shareme.Web.Client.Metadata;
using Shareme.Web.Client.Pages;

namespace Shareme.Web.Client.Tests;

public sealed class AssetDetailTests : BunitContext
{
    private readonly ISharemeApi _api = Substitute.For<ISharemeApi>();
    private readonly IRenditionsApi _renditions = Substitute.For<IRenditionsApi>();
    private readonly Guid _asset = Guid.CreateVersion7();
    private readonly Guid _folder = Guid.CreateVersion7();

    public AssetDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        MudBlazor.Extensions.ServiceCollectionExtensions.AddMudExtensions(Services);
        Services.AddSingleton(_api);
        _renditions.GetTypesAsync(default).ReturnsForAnyArgs([
            new DerivationTypeDto(Guid.CreateVersion7(), "social", "Social", 200, 200, DerivationFormats.Png, 85, false, true, false, null, []),
            new DerivationTypeDto(Guid.CreateVersion7(), "print", "Print", 4000, 4000, DerivationFormats.Jpeg, 90, false, false, true, null, [AssetTypeKeys.Image]),
            new DerivationTypeDto(Guid.CreateVersion7(), "slides", "Slides", 800, 800, DerivationFormats.WebP, 85, true, true, false, null, [AssetTypeKeys.Document]),
        ]);
        Services.AddSingleton(_renditions);
        var metadata = Substitute.For<IMetadataApi>();
        metadata.GetNodeMetadataAsync(default, default).ReturnsForAnyArgs(new NodeMetadataDto(_asset, "asset", new Dictionary<string, EffectivePropertyDto>()));
        metadata.GetDefinitionsAsync(default, default).ReturnsForAnyArgs([]);
        metadata.GetGroupsAsync(default).ReturnsForAnyArgs([]);
        metadata.GetLanguagesAsync(default).ReturnsForAnyArgs([]);
        metadata.GetContextsAsync(default).ReturnsForAnyArgs([]);
        metadata.GetSetsAsync(default, default).ReturnsForAnyArgs([]);
        Services.AddSingleton(metadata);
        Services.AddSingleton(Substitute.For<ICoworkeeApi>());
        Services.AddSingleton(new CoworkeeClientOptions());
        Services.AddSingleton(Substitute.For<IRealtimeConnection>());
        Services.AddScoped<RealtimeClient>();
        Services.AddScoped<UploadQueue>();
        AddAuthorization().SetAuthorized("Ada");
        var version = new AssetVersionDto(Guid.CreateVersion7(), 2, "logo.png", 2048, new string('a', 64), "image/png", "png", DateTimeOffset.UtcNow, null);
        _api.GetAssetAsync(_asset, Arg.Any<CancellationToken>()).Returns(new AssetDto(_asset, _folder, "logo.png", AssetTypeKeys.Image, AssetStatuses.Ready, version,
            [version, version with { Number = 1 }], [new DerivationDto(DerivationKinds.Preview, DerivationStates.Pending, null, null)], CanDownload: true));
        _api.GetNodeAsync(_asset, Arg.Any<CancellationToken>()).Returns(new NodeDetailDto(
            new NodeDto(_asset, _folder, NodeTypeKeys.Asset, "asset", "logo.png", new Dictionary<string, string>(), 0, false, DateTimeOffset.UtcNow, null),
            [new(_folder, "Brand"), new(_asset, "logo.png")], false));
        _api.GetEdgesAsync(_asset, Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public void Shows_the_file_facts_the_download_and_a_pending_preview()
    {
        var page = Render<AssetDetail>(p => p.Add(a => a.Id, _asset));

        page.WaitForAssertion(() => page.Find("[data-testid='asset-status']").TextContent.ShouldContain(AssetStatuses.Ready));
        page.Find("[data-testid='download']").GetAttribute("href").ShouldBe($"api/v1/assets/{_asset}/download");
        page.Find("[data-testid='preview']").TextContent.ShouldContain("being prepared");
        page.Markup.ShouldContain("2 KB");
        page.Find("[data-testid='breadcrumb']").TextContent.ShouldContain("Brand");
    }

    [Fact]
    public void Readers_without_the_download_right_get_neither_the_original_nor_its_download()
    {
        var pdf = new AssetVersionDto(Guid.CreateVersion7(), 1, "brief.pdf", 2048, new string('a', 64), "application/pdf", "pdf", DateTimeOffset.UtcNow, null);
        _api.GetAssetAsync(_asset, Arg.Any<CancellationToken>()).Returns(new AssetDto(_asset, _folder, "brief.pdf", AssetTypeKeys.Document, AssetStatuses.Ready, pdf, [pdf], []));

        var page = Render<AssetDetail>(p => p.Add(a => a.Id, _asset));

        page.WaitForAssertion(() => page.Find("[data-testid='asset-status']"));
        page.FindAll("[data-testid='download']").ShouldBeEmpty();
        page.FindAll("[data-testid='show-original']").ShouldBeEmpty();
        page.Markup.ShouldNotContain("/view");
    }

    [Fact]
    public void A_pdf_is_shown_as_itself_when_the_original_may_be_fetched()
    {
        var pdf = new AssetVersionDto(Guid.CreateVersion7(), 1, "brief.pdf", 2048, new string('a', 64), "application/pdf", "pdf", DateTimeOffset.UtcNow, null);
        _api.GetAssetAsync(_asset, Arg.Any<CancellationToken>()).Returns(new AssetDto(_asset, _folder, "brief.pdf", AssetTypeKeys.Document, AssetStatuses.Ready, pdf, [pdf], [], CanDownload: true));

        var page = Render<AssetDetail>(p => p.Add(a => a.Id, _asset));

        // the pdf viewer fetches the url itself, so the markup only shows which viewer is used
        page.WaitForAssertion(() => page.Find("[data-testid='preview']").InnerHtml.ShouldContain("mud-ex-file-display-pdf"));
        page.Find("[data-testid='show-original']");
    }

    [Fact]
    public async Task Renditions_of_the_asset_type_can_be_created_or_uploaded()
    {
        var page = Render<AssetDetail>(p => p.Add(a => a.Id, _asset));
        page.WaitForAssertion(() => page.FindAll(".mud-tab").Count.ShouldBeGreaterThan(2));
        await page.FindAll(".mud-tab").First(t => t.TextContent.Contains("Renditions")).ClickAsync(new());

        page.WaitForAssertion(() => page.Find("[data-rendition='social']"));
        page.FindAll("[data-rendition='slides']").ShouldBeEmpty();
        page.Find("[data-rendition='print']").InnerHtml.ShouldContain("Upload rendition");
        await page.Find("[data-rendition='social'] [data-testid='request-rendition']").ClickAsync(new());

        await _renditions.Received().RequestAsync(_asset, "social", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rendered_picture_can_be_cropped_into_its_renditions_or_a_variant()
    {
        var version = new AssetVersionDto(Guid.CreateVersion7(), 1, "logo.png", 2048, new string('a', 64), "image/png", "png", DateTimeOffset.UtcNow, null);
        _api.GetAssetAsync(_asset, Arg.Any<CancellationToken>()).Returns(new AssetDto(_asset, _folder, "logo.png", AssetTypeKeys.Image, AssetStatuses.Ready, version, [version],
            [new DerivationDto(DerivationKinds.Preview, DerivationStates.Ready, 1600, 800)], CanDownload: true));
        var page = Render<AssetDetail>(p => p.Add(a => a.Id, _asset));

        await page.WaitForElement("[data-testid='crop']").ClickAsync(new());

        page.WaitForAssertion(() => page.Find("[data-testid='crop-editor']").TextContent.ShouldContain("Nothing selected yet."));
        page.Find("[data-testid='apply-crop']").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task A_share_link_is_created_on_request()
    {
        _api.CreateDownloadLinkAsync(_asset, null, Arg.Any<CancellationToken>()).Returns(new DownloadLinkDto("/api/v1/blobs/token", DateTimeOffset.UtcNow.AddMinutes(15)));
        var page = Render<AssetDetail>(p => p.Add(a => a.Id, _asset));

        await page.WaitForElement("[data-testid='create-link']").ClickAsync(new());

        page.WaitForAssertion(() => page.Find("[data-testid='share-link']").TextContent.ShouldContain("/api/v1/blobs/token"));
    }
}
