using Bunit;
using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Client.Blazor.Security;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Contracts.Workspaces;
using Shareme.Web.Client.Assets;
using Shareme.Web.Client.Graph;
using Shareme.Web.Client.Metadata;
using Shareme.Web.Client.Pages;
using Shareme.Web.Client.Workspaces;

namespace Shareme.Web.Client.Tests;

public sealed class WorkspaceTests : BunitContext
{
    private readonly IWorkspacesApi _api = Substitute.For<IWorkspacesApi>();
    private readonly Guid _id = Guid.CreateVersion7();

    public WorkspaceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        MudBlazor.Extensions.ServiceCollectionExtensions.AddMudExtensions(Services);
        Services.AddSingleton(_api);
        var graph = Substitute.For<ISharemeApi>();
        graph.GetRootAsync(default).ReturnsForAnyArgs(new NodeDto(Guid.CreateVersion7(), null, "folder", "folder", "Root", new Dictionary<string, string>(), 0, false, DateTimeOffset.UtcNow, null));
        Services.AddSingleton(graph);
        var metadata = Substitute.For<IMetadataApi>();
        metadata.GetNodeMetadataAsync(default, default).ReturnsForAnyArgs(new NodeMetadataDto(_id, "workspace", new Dictionary<string, EffectivePropertyDto>()));
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
        AddAuthorization().SetAuthorized("Ada").SetPolicies(PermissionPolicy.For(WorkspacePermissions.Create));
        _api.GetAsync(false, Arg.Any<CancellationToken>()).Returns([Workspace("Campaign", WorkspaceRoles.Owner)]);
        _api.GetAsync(true, Arg.Any<CancellationToken>()).Returns([Workspace("Agency job", null) with { IsTemplate = true }]);
        _api.GetAsync(_id, Arg.Any<CancellationToken>()).Returns(Workspace("Campaign", WorkspaceRoles.Owner));
        _api.GetItemsAsync(_id, Arg.Any<CancellationToken>()).Returns([]);
        _api.GetMembersAsync(_id, Arg.Any<CancellationToken>()).Returns([new WorkspaceMemberDto(Guid.CreateVersion7(), "ada@acme.test", "Ada", WorkspaceRoles.Owner)]);
    }

    [Fact]
    public async Task Workspaces_are_listed_and_created_from_a_template()
    {
        _api.CreateAsync(default!, default).ReturnsForAnyArgs(Workspace("New", WorkspaceRoles.Owner));
        var page = Render<Shareme.Web.Client.Pages.Workspaces>();

        page.WaitForAssertion(() => page.Find("[data-workspace='Campaign']").TextContent.ShouldContain(WorkspaceRoles.Owner));
        page.Find("input[data-testid='workspace-name'], [data-testid='workspace-name'] input").Change("Spring");
        await page.Find("[data-testid='create-workspace']").ClickAsync(new());

        await _api.Received(1).CreateAsync(Arg.Is<CreateWorkspaceRequest>(r => r.Name == "Spring" && !r.IsTemplate), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Owners_see_members_and_can_finalise()
    {
        var page = Render<WorkspaceDetail>(p => p.Add(w => w.Id, _id));

        page.WaitForAssertion(() => page.Find("[data-testid='workspace-title']").TextContent.ShouldBe("Campaign"));
        page.Find("[data-testid='finalize']");
        page.Markup.ShouldContain("Nothing here yet.");
    }

    [Fact]
    public void Collaborators_cannot_finalise()
    {
        _api.GetAsync(_id, Arg.Any<CancellationToken>()).Returns(Workspace("Campaign", WorkspaceRoles.Collaborator));
        var page = Render<WorkspaceDetail>(p => p.Add(w => w.Id, _id));

        page.WaitForAssertion(() => page.Find("[data-testid='workspace-title']"));
        page.FindAll("[data-testid='finalize']").ShouldBeEmpty();
    }

    private WorkspaceDto Workspace(string name, string? role) =>
        new(_id, name, WorkspaceKinds.Upload, false, null, null, false, null, null, DateTimeOffset.UtcNow, 0, role);
}
