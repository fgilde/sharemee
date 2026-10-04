using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;
using Shareme.Contracts.Workspaces;

namespace Shareme.Api.Tests;

public sealed class IntranetTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _folder;

    public async ValueTask InitializeAsync()
    {
        _setup = await api.SetupAsync();
        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        _folder = (await (await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(root, "Company"), Ct)).Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_feed_shows_published_readable_news_pinned_first()
    {
        var now = DateTimeOffset.UtcNow;
        var old = await NewsAsync(new NewsRequest(_folder, "Canteen menu", "Soup **today**.", now.AddDays(-2)));
        var pinned = await NewsAsync(new NewsRequest(_folder, "Office closed Friday", "We are closed.", now.AddDays(-3), Pinned: true));
        await NewsAsync(new NewsRequest(_folder, "Future", "Not yet.", now.AddDays(2)));
        await NewsAsync(new NewsRequest(_folder, "Expired", "Gone.", now.AddDays(-5), now.AddDays(-1)));
        old.Html.ShouldContain("<strong>today</strong>");
        (await Admin.PostAsJsonAsync("/api/v1/news", new NewsRequest(_folder, "Bad", "x", now, now.AddDays(-1)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var feed = (await Admin.GetFromJsonAsync<NewsSummaryDto[]>("/api/v1/news", Ct))!;
        feed.Select(n => n.Title).ShouldBe(["Office closed Friday", "Canteen menu"]);
        feed[1].Teaser.ShouldBe("Soup today.");

        var updated = await (await Admin.PutAsJsonAsync($"/api/v1/news/{old.Id}", new NewsUpdateRequest("Canteen menu", "Pasta.", old.PublishAt, null, true), Ct)).Content.ReadFromJsonAsync<NewsDto>(Ct);
        updated!.Pinned.ShouldBeTrue();

        var user = await UserAsync();
        var stranger = api.As(user, _setup.TenantId);
        (await stranger.GetFromJsonAsync<NewsSummaryDto[]>("/api/v1/news", Ct))!.ShouldBeEmpty();
        (await stranger.GetAsync($"/api/v1/news/{pinned.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Favorites_recent_nodes_tasks_and_the_start_page_layout_are_per_user()
    {
        var page = (await (await Admin.PostAsJsonAsync("/api/v1/pages", new PageRequest(_folder, "Handbook", "text"), Ct)).Content.ReadFromJsonAsync<PageDto>(Ct))!;
        (await Admin.PutAsync($"/api/v1/favorites/{page.Id}", null, Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsync($"/api/v1/favorites/{_folder}", null, Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsync($"/api/v1/favorites/{_folder}", null, Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<NodeDto[]>("/api/v1/favorites", Ct))!.Select(n => n.Id).ShouldBe([_folder, page.Id]);
        (await Admin.DeleteAsync($"/api/v1/favorites/{page.Id}", Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<NodeDto[]>("/api/v1/favorites", Ct))!.Select(n => n.Id).ShouldBe([_folder]);

        (await Admin.GetFromJsonAsync<NodeDto[]>("/api/v1/home/recent", Ct))!.First().Id.ShouldBe(page.Id);

        var workspace = (await (await Admin.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest("Launch"), Ct)).Content.ReadFromJsonAsync<WorkspaceDto>(Ct))!;
        var task = await Admin.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/tasks", new TaskRequest("Write copy", DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), AssigneeIds: [_setup.AdminUserId]), Ct);
        task.StatusCode.ShouldBe(HttpStatusCode.OK, await task.Content.ReadAsStringAsync(Ct));
        var mine = (await Admin.GetFromJsonAsync<HomeTaskDto[]>("/api/v1/home/tasks", Ct))!.Single();
        (mine.Title, mine.WorkspaceName).ShouldBe(("Write copy", "Launch"));

        var layout = (await Admin.GetFromJsonAsync<HomeLayoutDto>("/api/v1/home/layout", Ct))!;
        layout.IsDefault.ShouldBeTrue();
        layout.Widgets.ShouldBe(HomeWidgets.All);
        (await Admin.PutAsJsonAsync("/api/v1/home/layout/default", new HomeLayoutRequest([HomeWidgets.News, HomeWidgets.Search]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync("/api/v1/home/layout", new HomeLayoutRequest([HomeWidgets.Tasks]), Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<HomeLayoutDto>("/api/v1/home/layout", Ct))!.Widgets.ShouldBe([HomeWidgets.Tasks]);
        (await Admin.PutAsJsonAsync("/api/v1/home/layout", new HomeLayoutRequest(["weather"]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var other = api.As(await UserAsync(), _setup.TenantId);
        (await other.GetFromJsonAsync<HomeLayoutDto>("/api/v1/home/layout", Ct))!.Widgets.ShouldBe([HomeWidgets.News, HomeWidgets.Search]);
        (await other.PutAsJsonAsync("/api/v1/home/layout/default", new HomeLayoutRequest([HomeWidgets.Tasks]), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await other.PutAsync($"/api/v1/favorites/{_folder}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await other.GetFromJsonAsync<NodeDto[]>("/api/v1/home/recent", Ct))!.ShouldBeEmpty();
    }

    private async Task<NewsDto> NewsAsync(NewsRequest request)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/news", request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<NewsDto>(Ct))!;
    }

    private async Task<Guid> UserAsync() =>
        (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
}
