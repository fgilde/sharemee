using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;
using Shareme.Contracts.Search;

namespace Shareme.Api.Tests;

public sealed class PageTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _folder;

    public async ValueTask InitializeAsync()
    {
        _setup = await api.SetupAsync();
        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        _folder = (await (await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(root, "Handbook"), Ct)).Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Pages_render_safe_html_keep_versions_and_compare_them()
    {
        var page = await CreateAsync("Onboarding", "# Welcome\n\nRead the **handbook**.\n\n<script>alert(1)</script>\n\n[bad](javascript:alert(1)) [good](https://example.com)");
        page.Version.ShouldBe(1);
        page.Html.ShouldContain("<strong>handbook</strong>");
        page.Html.ShouldNotContain("<script");
        page.Html.ShouldNotContain("javascript:");
        page.Html.ShouldContain("href=\"https://example.com\"");
        page.CanEdit.ShouldBeTrue();

        var second = await UpdateAsync(page.Id, new PageUpdateRequest("Onboarding", "# Welcome\n\nRead the **handbook** first.\n\nThen meet the team.", 1, "Team added"));
        second.Version.ShouldBe(2);
        (await Admin.PutAsJsonAsync($"/api/v1/pages/{page.Id}", new PageUpdateRequest("Onboarding", "stale edit", 1), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var versions = (await Admin.GetFromJsonAsync<PageVersionDto[]>($"/api/v1/pages/{page.Id}/versions", Ct))!;
        versions.Select(v => (v.Number, v.Comment)).ShouldBe([(2, "Team added"), (1, (string?)null)]);
        var diff = (await Admin.GetFromJsonAsync<DiffLineDto[]>($"/api/v1/pages/{page.Id}/diff?from=1&to=2", Ct))!;
        diff.ShouldContain(new DiffLineDto(DiffKinds.Added, "Then meet the team."));
        diff.ShouldContain(new DiffLineDto(DiffKinds.Removed, "Read the **handbook**."));
        diff.ShouldContain(new DiffLineDto(DiffKinds.Same, "# Welcome"));

        var restored = (await (await Admin.PostAsync($"/api/v1/pages/{page.Id}/versions/1/restore?expectedVersion=2", null, Ct)).Content.ReadFromJsonAsync<PageDto>(Ct))!;
        (restored.Version, restored.Markdown).ShouldBe((3, page.Markdown));
        (await Admin.GetFromJsonAsync<PageVersionContentDto>($"/api/v1/pages/{page.Id}/versions/2", Ct))!.Markdown.ShouldContain("meet the team");

        var children = await Admin.GetFromJsonAsync<JsonElement>($"/api/v1/nodes/{_folder}/children", Ct);
        children.GetProperty("items").EnumerateArray().Single().GetProperty("semantic").GetString().ShouldBe("page");
        (await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(page.Id, "inside"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Markdown_cannot_smuggle_script_styles_or_foreign_images()
    {
        var page = await CreateAsync("Tricks", string.Join(Environment.NewLine + Environment.NewLine,
            "<javascript:alert(1)>", "![a](javascript:alert(1))", "![b](http://tracker.test/p.png)", "![c](https://cdn.test/ok.png)", "![d](/api/v1/assets/x/derivations/preview)",
            "Overlay {.mud-overlay #app onclick=alert(1)}", "[p](//evil.test/x)", "[e](javascript&#58;alert(1))", "[m](mailto:a@b.test)"));
        // shown as text at most, never as a link, image or attribute
        page.Html.ShouldNotMatch(@"(href|src)=""[^""]*(javascript|tracker\.test|evil\.test)");
        page.Html.ShouldNotMatch("<[^>]+(class|id|onclick)=");
        page.Html.ShouldContain("src=\"https://cdn.test/ok.png\"");
        page.Html.ShouldContain("src=\"/api/v1/assets/x/derivations/preview\"");
        page.Html.ShouldContain("href=\"mailto:a@b.test\"");

        var preview = await (await Admin.PostAsJsonAsync("/api/v1/pages/preview", new PagePreviewRequest("<img src=x onerror=alert(1)> **ok**"), Ct)).Content.ReadFromJsonAsync<PagePreviewDto>(Ct);
        preview!.Html.ShouldNotMatch("<img[^>]*onerror");
        preview.Html.ShouldContain("<strong>ok</strong>");

        // two saves from the same version: one wins, the other is told
        var saves = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Admin.PutAsJsonAsync($"/api/v1/pages/{page.Id}", new PageUpdateRequest("Tricks", $"edit {i}", 1), Ct)));
        saves.Select(r => r.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{page.Id}", new UpdateNodeRequest("Renamed"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Pages_are_found_by_their_text_and_follow_node_permissions()
    {
        var page = await CreateAsync("Travel policy", "Trains are preferred over planes for trips under 600 km.");
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while ((await (await Admin.PostAsJsonAsync("/api/v1/search", new SearchRequest("trains"), Ct)).Content.ReadFromJsonAsync<SearchResponse>(Ct))!.Items.All(i => i.Id != page.Id))
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the page was not found by its text");
            await Task.Delay(500, Ct);
        }

        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var stranger = api.As(user.Id, _setup.TenantId);
        (await stranger.GetAsync($"/api/v1/pages/{page.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.PostAsJsonAsync("/api/v1/pages", new PageRequest(_folder, "Mine", "text"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var reader = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Reader " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{reader}", new NameListRequest([NodePermissions.Read]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/{NodePermissions.ResourceType}/{_folder}", new GrantResourcePermissionRequest(PrincipalType.User, user.Id, reader), Ct))
            .EnsureSuccessStatusCode();
        var read = (await stranger.GetFromJsonAsync<PageDto>($"/api/v1/pages/{page.Id}", Ct))!;
        read.CanEdit.ShouldBeFalse();
        (await stranger.PutAsJsonAsync($"/api/v1/pages/{page.Id}", new PageUpdateRequest("Changed", "x", read.Version), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.PostAsJsonAsync("/api/v1/pages", new PageRequest(_folder, " ", "text"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<PageDto> CreateAsync(string title, string markdown)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/pages", new PageRequest(_folder, title, markdown), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PageDto>(Ct))!;
    }

    private async Task<PageDto> UpdateAsync(Guid id, PageUpdateRequest request)
    {
        var response = await Admin.PutAsJsonAsync($"/api/v1/pages/{id}", request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PageDto>(Ct))!;
    }
}
