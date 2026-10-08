using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Knowledge;
using Shareme.Web.Client.Knowledge;
using Shareme.Web.Client.Pages;

namespace Shareme.Web.Client.Tests;

public sealed class PeopleTests : BunitContext
{
    private readonly IPeopleApi _api = Substitute.For<IPeopleApi>();

    public PeopleTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(NSubstitute.Substitute.For<Coworkee.Client.Blazor.Localization.ILocalizationApi>());
        Services.AddScoped<Coworkee.Client.Blazor.Localization.CoworkeeLocalizer>();
        Services.AddSingleton(_api);
        AddAuthorization().SetAuthorized("Ada");
        _api.FindAsync(default, default, default, default).ReturnsForAnyArgs([new PersonDto(Guid.CreateVersion7(), "Grace Hopper", "grace@acme.test", "Research", "Rear admiral", null, "Arlington", null, false)]);
    }

    [Fact]
    public async Task Lists_people_and_saves_the_own_profile()
    {
        var page = Render<People>();

        page.WaitForAssertion(() => page.Find("[data-person='Grace Hopper']").TextContent.ShouldContain("Research"));
        page.Find("[data-person='Grace Hopper']").TextContent.ShouldContain("GH");
        await page.Find("[data-testid='edit-profile'] button, button[data-testid='edit-profile']").ClickAsync(new());
        await page.Find("[data-testid='save-profile']").ClickAsync(new());
        await _api.Received(1).SaveProfileAsync(Arg.Any<ProfileRequest>(), Arg.Any<CancellationToken>());
    }
}
