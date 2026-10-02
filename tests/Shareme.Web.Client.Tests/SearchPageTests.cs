using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Search;
using Shareme.Web.Client.Metadata;
using Shareme.Web.Client.Pages;
using Shareme.Web.Client.Search;

namespace Shareme.Web.Client.Tests;

public sealed class SearchPageTests : BunitContext
{
    private readonly ISearchApi _api = Substitute.For<ISearchApi>();
    private readonly Guid _hit = Guid.CreateVersion7();

    public SearchPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_api);
        var metadata = Substitute.For<IMetadataApi>();
        metadata.GetDefinitionsAsync(default, default).ReturnsForAnyArgs([]);
        Services.AddSingleton(metadata);
        AddAuthorization().SetAuthorized("Ada");
        _api.GetSavedAsync(Arg.Any<CancellationToken>()).Returns([]);
        _api.SearchAsync(Arg.Any<SearchRequest>(), Arg.Any<CancellationToken>()).Returns(new SearchResponse(
            [new NodeDto(_hit, null, NodeTypeKeys.Folder, "folder", "Ocean campaign", new Dictionary<string, string>(), 0, false, DateTimeOffset.UtcNow, null)],
            1,
            new Dictionary<string, IReadOnlyList<FacetValueDto>> { [SearchFields.Type] = [new FacetValueDto("folder", 1, "Folder")] },
            null));
    }

    [Fact]
    public void The_query_from_the_url_is_searched_and_hits_and_facets_shown()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("search?q=ocean");

        var page = Render<SearchPage>();

        page.WaitForAssertion(() => page.Find($"[data-hit='{_hit}']").TextContent.ShouldContain("Ocean campaign"));
        page.Find("[data-facet='type'][data-value='folder']").TextContent.ShouldContain("Folder (1)");
        _api.Received().SearchAsync(Arg.Is<SearchRequest>(r => r.Term == "ocean" && r.Facets!.Contains(SearchFields.Type)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Picking_a_facet_value_filters_the_search()
    {
        var page = Render<SearchPage>();
        page.WaitForElement("[data-facet='type'][data-value='folder'] input").Change(true);

        page.WaitForAssertion(() => _api.Received().SearchAsync(
            Arg.Is<SearchRequest>(r => r.Filters!.Any(f => f.Field == SearchFields.Type && f.Operator == SearchOperators.Is && f.Values!.Contains("folder"))), Arg.Any<CancellationToken>()));
    }
}
