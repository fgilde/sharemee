using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Knowledge;
using Shareme.Web.Client.Knowledge;
using Shareme.Web.Client.Pages;

namespace Shareme.Web.Client.Tests;

public sealed class PageViewTests : BunitContext
{
    private readonly IPagesApi _api = Substitute.For<IPagesApi>();
    private readonly PageDto _page = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Onboarding", "# Hi\n\n**Read** it.", "<h1>Hi</h1><p><strong>Read</strong> it.</p>", 3, DateTimeOffset.UtcNow, null, true);

    public PageViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(NSubstitute.Substitute.For<Shareme.Web.Client.Knowledge.IIntranetApi>());
        MudBlazor.Extensions.ServiceCollectionExtensions.AddMudExtensions(Services);
        Services.AddSingleton(_api);
        AddAuthorization().SetAuthorized("Ada");
        _api.GetAsync(_page.Id, Arg.Any<CancellationToken>()).Returns(_page);
        _api.UpdateAsync(default, default!, default).ReturnsForAnyArgs(_page with { Version = 4, Markdown = "changed" });
    }

    [Fact]
    public async Task Shows_the_rendered_page_and_saves_edits_against_the_version_read()
    {
        var page = Render<PageView>(p => p.Add(v => v.Id, _page.Id));

        page.WaitForAssertion(() => page.Find("[data-testid='page-content'] strong").TextContent.ShouldBe("Read"));
        await page.Find("[data-testid='edit-page']").ClickAsync(new());
        page.Find("textarea[data-testid='page-edit-markdown'], [data-testid='page-edit-markdown'] textarea").Input("changed");
        await Task.Delay(600, Xunit.TestContext.Current.CancellationToken);
        await page.Find("[data-testid='save-page']").ClickAsync(new());

        await _api.Received(1).UpdateAsync(_page.Id, Arg.Is<PageUpdateRequest>(r => r.Markdown == "changed" && r.ExpectedVersion == 3 && r.Title == "Onboarding"), Arg.Any<CancellationToken>());
    }
}
