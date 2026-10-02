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
        graph.GetNodeAsync(_id, Arg.Any<CancellationToken>()).Returns(new NodeDetailDto(
            new NodeDto(_id, null, "workspace", "workspace", "Campaign", new Dictionary<string, string>(), 0, false, DateTimeOffset.UtcNow, null), [], false));
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

    [Fact]
    public async Task The_board_shows_columns_offers_only_allowed_drops_and_adds_tasks()
    {
        var todo = new StateDto(Guid.CreateVersion7(), "To do", 0, true, false, false, []);
        var done = new StateDto(Guid.CreateVersion7(), "Done", 1, false, true, true, []);
        _api.GetStatesAsync(_id, Arg.Any<CancellationToken>()).Returns([todo, done]);
        var task = new TaskDto(Guid.CreateVersion7(), _id, "Brief", null, null, todo.Id, [], 0, 1, DateTimeOffset.UtcNow, null, []);
        _api.GetTasksAsync(_id, Arg.Any<CancellationToken>()).Returns([task]);

        var board = Render<TaskBoard>(p => p.Add(b => b.WorkspaceId, _id).Add(b => b.IsOwner, false));

        board.WaitForAssertion(() => board.Find("[data-state='To do']").TextContent.ShouldContain("Brief"));
        board.Instance.CanMove(task, done.Id).ShouldBeFalse("only owners put tasks into done");
        board.Instance.CanMove(task, todo.Id).ShouldBeTrue();
        board.Find("input[data-testid='new-task'], [data-testid='new-task'] input").Input("Visuals");
        await board.Find("[data-testid='add-task']").ClickAsync(new());
        await _api.Received(1).CreateTaskAsync(_id, Arg.Is<TaskRequest>(r => r.Title == "Visuals"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Comments_are_listed_as_plain_text_and_written()
    {
        _api.GetCommentsAsync(_id, Arg.Any<CancellationToken>()).Returns([
            new CommentDto(Guid.CreateVersion7(), _id, Guid.CreateVersion7(), "Ada", "<b>bold?</b>", [], DateTimeOffset.UtcNow, null, false)]);
        var panel = Render<CommentsPanel>(p => p.Add(c => c.NodeId, _id));

        panel.WaitForAssertion(() => panel.Find("[data-testid='comments']").TextContent.ShouldContain("<b>bold?</b>"));
        panel.FindAll("[data-testid='comments'] b").ShouldBeEmpty("user text is never markup");
        panel.Find("textarea[data-testid='comment-text'], [data-testid='comment-text'] textarea").Input("Looks good");
        await panel.Find("[data-testid='add-comment']").ClickAsync(new());
        await _api.Received(1).AddCommentAsync(_id, Arg.Is<CommentRequest>(r => r.Text == "Looks good"), Arg.Any<CancellationToken>());
    }

    private WorkspaceDto Workspace(string name, string? role) =>
        new(_id, name, WorkspaceKinds.Upload, false, null, null, false, null, null, DateTimeOffset.UtcNow, 0, role, role == WorkspaceRoles.Owner, role == WorkspaceRoles.Owner);
}
