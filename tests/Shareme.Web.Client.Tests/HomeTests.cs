using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;
using Shareme.Web.Client.Knowledge;
using Shareme.Web.Client.Pages;

namespace Shareme.Web.Client.Tests;

public sealed class HomeTests : BunitContext
{
    private readonly IIntranetApi _api = Substitute.For<IIntranetApi>();

    public HomeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_api);
        AddAuthorization().SetAuthorized("Ada");
        _api.GetLayoutAsync(default).ReturnsForAnyArgs(new HomeLayoutDto([HomeWidgets.News, HomeWidgets.Favorites], false));
        _api.GetFeedAsync(default, default).ReturnsForAnyArgs([new NewsSummaryDto(Guid.CreateVersion7(), "Office closed Friday", "We are closed.", DateTimeOffset.UtcNow, true)]);
        _api.GetFavoritesAsync(default).ReturnsForAnyArgs([new NodeDto(Guid.CreateVersion7(), null, "page", "page", "Handbook", new Dictionary<string, string>(), 0, false, DateTimeOffset.UtcNow, null)]);
    }

    [Fact]
    public async Task Shows_the_widgets_of_the_layout_in_order_and_saves_changes()
    {
        var page = Render<Home>();

        page.WaitForAssertion(() => page.FindAll("[data-widget]").Select(w => w.GetAttribute("data-widget")).ShouldBe([HomeWidgets.News, HomeWidgets.Favorites]));
        page.WaitForAssertion(() => page.Find("[data-widget='news']").TextContent.ShouldContain("Office closed Friday"));
        page.Find("[data-widget='favorites'] a").GetAttribute("href").ShouldEndWith("/pages/" + (await _api.GetFavoritesAsync())[0].Id);

        await page.Find("[data-testid='customize-home'] button, button[data-testid='customize-home']").ClickAsync(new());
        await page.Find("[data-testid='save-layout']").ClickAsync(new());
        await _api.Received(1).SaveLayoutAsync(Arg.Is<IReadOnlyList<string>>(w => w.SequenceEqual(new[] { HomeWidgets.News, HomeWidgets.Favorites })), false, Arg.Any<CancellationToken>());
    }
}
