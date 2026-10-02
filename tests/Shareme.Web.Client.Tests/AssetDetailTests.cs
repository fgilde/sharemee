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
    private readonly Guid _asset = Guid.CreateVersion7();
    private readonly Guid _folder = Guid.CreateVersion7();

    public AssetDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_api);
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
            [version, version with { Number = 1 }], [new DerivationDto(DerivationKinds.Preview, DerivationStates.Pending, null, null)]));
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
    public async Task A_share_link_is_created_on_request()
    {
        _api.CreateDownloadLinkAsync(_asset, null, Arg.Any<CancellationToken>()).Returns(new DownloadLinkDto("/api/v1/blobs/token", DateTimeOffset.UtcNow.AddMinutes(15)));
        var page = Render<AssetDetail>(p => p.Add(a => a.Id, _asset));

        await page.WaitForElement("[data-testid='create-link']").ClickAsync(new());

        page.WaitForAssertion(() => page.Find("[data-testid='share-link']").TextContent.ShouldContain("/api/v1/blobs/token"));
    }
}
