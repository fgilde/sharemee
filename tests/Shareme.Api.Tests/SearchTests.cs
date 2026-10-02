using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Contracts.Search;

namespace Shareme.Api.Tests;

public sealed class SearchTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await api.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Finds_by_name_and_metadata_with_facets_and_labels()
    {
        var color = await DefineAsync(new PropertyDefinitionRequest("color", "Color", ListMode: ListModes.Single, Searchable: SearchModes.Both, Facet: true));
        (await Admin.PutAsJsonAsync($"/api/v1/metadata/definitions/{color.Id}/list-values", new[] { new PropertyListValueDto("blue", "Blue", 0, true), new PropertyListValueDto("red", "Red", 1, true) }, Ct))
            .EnsureSuccessStatusCode();
        var brand = await FolderAsync("Brand");
        var ocean = await FolderAsync("Ocean campaign", brand);
        var fire = await FolderAsync("Fire campaign", brand);
        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{ocean}/metadata", new Dictionary<string, List<MetadataValue>> { ["color"] = [new(List: "blue")] }, Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{fire}/metadata", new Dictionary<string, List<MetadataValue>> { ["color"] = [new(List: "red")] }, Ct)).EnsureSuccessStatusCode();

        var found = await EventuallyAsync(Admin, new SearchRequest("campaign", FolderId: brand, Facets: [SearchFields.Meta("color")]), r => r.Total == 2 && r.Facets[SearchFields.Meta("color")].Count == 2);
        found.Facets[SearchFields.Meta("color")].ShouldContain(new FacetValueDto("blue", 1, "Blue"));

        var blue = await SearchAsync(Admin, new SearchRequest(Filters: [new(SearchFields.Meta("color"), SearchOperators.Is, ["blue"])], FolderId: brand));
        blue.Items.Select(i => i.Id).ShouldBe([ocean]);
        (await SearchAsync(Admin, new SearchRequest("Blue", FolderId: brand))).Items.Select(i => i.Id).ShouldBe([ocean]);
        (await SearchAsync(Admin, new SearchRequest(FolderId: brand, Sort: [new(SearchFields.Name)]))).Items.Select(i => i.Name).ShouldBe(["Fire campaign", "Ocean campaign"]);
    }

    [Fact]
    public async Task Readers_only_find_what_they_may_read_and_lose_it_when_access_ends()
    {
        var open = await FolderAsync("Open area");
        var secret = await FolderAsync("Secret area");
        var openDoc = await FolderAsync("Plan open", open);
        await FolderAsync("Plan secret", secret);
        var (bob, _, grant) = await ReaderAsync(open);

        var visible = await EventuallyAsync(bob, new SearchRequest("plan"), r => r.Total == 1);
        visible.Items.Single().Id.ShouldBe(openDoc);

        (await Admin.DeleteAsync($"/api/v1/identity/resource-permissions/{NodePermissions.ResourceType}/{open}/{grant}", Ct)).EnsureSuccessStatusCode();
        await EventuallyAsync(bob, new SearchRequest("plan"), r => r.Total == 0);
    }

    [Fact]
    public async Task Deleting_moving_and_restoring_update_the_index()
    {
        var archive = await FolderAsync("Archive");
        var desk = await FolderAsync("Desk");
        var item = await FolderAsync("Quarterly report", desk);
        await EventuallyAsync(Admin, new SearchRequest("quarterly", FolderId: desk), r => r.Total == 1);

        (await Admin.PostAsJsonAsync($"/api/v1/nodes/{desk}/move", new MoveNodeRequest(archive), Ct)).EnsureSuccessStatusCode();
        await EventuallyAsync(Admin, new SearchRequest("quarterly", FolderId: archive), r => r.Total == 1);

        (await Admin.DeleteAsync($"/api/v1/nodes/{desk}", Ct)).EnsureSuccessStatusCode();
        await EventuallyAsync(Admin, new SearchRequest("quarterly"), r => r.Total == 0);

        (await Admin.PostAsync($"/api/v1/recycle-bin/{desk}/restore", null, Ct)).EnsureSuccessStatusCode();
        (await EventuallyAsync(Admin, new SearchRequest("quarterly"), r => r.Total == 1)).Items.Single().Id.ShouldBe(item);
    }

    [Fact]
    public async Task Pages_follow_the_cursor_and_suggestions_complete_names()
    {
        var box = await FolderAsync("Box");
        foreach (var name in new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" })
        {
            await FolderAsync(name, box);
        }

        var first = await EventuallyAsync(Admin, new SearchRequest(FolderId: box, Sort: [new(SearchFields.Name)], Size: 3), r => r.Total == 5);
        first.Items.Select(i => i.Name).ShouldBe(["Alpha", "Bravo", "Charlie"]);
        var next = await SearchAsync(Admin, new SearchRequest(FolderId: box, Sort: [new(SearchFields.Name)], Size: 3, Cursor: first.Cursor));
        next.Items.Select(i => i.Name).ShouldBe(["Delta", "Echo"]);

        var suggestions = await (await Admin.PostAsJsonAsync("/api/v1/search/suggest", new SuggestRequest("char", box), Ct)).Content.ReadFromJsonAsync<SuggestionDto[]>(Ct);
        suggestions!.Select(s => s.Name).ShouldBe(["Charlie"]);
    }

    [Fact]
    public async Task Unknown_fields_and_operators_are_rejected()
    {
        (await Admin.PostAsJsonAsync("/api/v1/search", new SearchRequest(Filters: [new("nope", SearchOperators.Is, ["x"])]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/search", new SearchRequest(Filters: [new(SearchFields.Name, "like", ["x"])]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Saved_searches_belong_to_their_owner_until_shared()
    {
        var (bob, _, _) = await ReaderAsync(await FolderAsync("Anything"));
        var saved = await (await Admin.PostAsJsonAsync("/api/v1/saved-searches", new SaveSearchRequest("Blue things", new SearchRequest("blue")), Ct)).Content.ReadFromJsonAsync<SavedSearchDto>(Ct);

        (await Admin.GetFromJsonAsync<SavedSearchDto[]>("/api/v1/saved-searches", Ct))!.Single().Request.Term.ShouldBe("blue");
        (await bob.GetFromJsonAsync<SavedSearchDto[]>("/api/v1/saved-searches", Ct))!.ShouldBeEmpty();
        (await bob.DeleteAsync($"/api/v1/saved-searches/{saved!.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await Admin.PutAsJsonAsync($"/api/v1/saved-searches/{saved.Id}", new SaveSearchRequest("Blue things", new SearchRequest("blue"), Shared: true), Ct)).EnsureSuccessStatusCode();

        var shared = (await bob.GetFromJsonAsync<SavedSearchDto[]>("/api/v1/saved-searches", Ct))!.Single();
        shared.IsOwner.ShouldBeFalse();
        shared.IsShared.ShouldBeTrue();
        (await bob.PutAsJsonAsync($"/api/v1/saved-searches/{saved.Id}", new SaveSearchRequest("Mine now", new SearchRequest("x")), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.DeleteAsync($"/api/v1/saved-searches/{saved.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Facet_selections_keep_other_values_selectable()
    {
        var color = await DefineAsync(new PropertyDefinitionRequest("tint", "Tint", ListMode: ListModes.Single, Searchable: SearchModes.Filter, Facet: true));
        (await Admin.PutAsJsonAsync($"/api/v1/metadata/definitions/{color.Id}/list-values", new[] { new PropertyListValueDto("blue", "Blue", 0, true), new PropertyListValueDto("red", "Red", 1, true) }, Ct))
            .EnsureSuccessStatusCode();
        var box = await FolderAsync("Tints");
        foreach (var (name, value) in new[] { ("One", "blue"), ("Two", "red") })
        {
            var node = await FolderAsync(name, box);
            (await Admin.PutAsJsonAsync($"/api/v1/nodes/{node}/metadata", new Dictionary<string, List<MetadataValue>> { ["tint"] = [new(List: value)] }, Ct)).EnsureSuccessStatusCode();
        }

        await EventuallyAsync(Admin, new SearchRequest(FolderId: box, Filters: [new(SearchFields.Meta("tint"), SearchOperators.HasValue)]), r => r.Total == 2);
        var picked = await SearchAsync(Admin, new SearchRequest(FolderId: box, Facets: [SearchFields.Meta("tint")], Filters: [new(SearchFields.Meta("tint"), SearchOperators.Is, ["blue"])]));

        picked.Items.Select(i => i.Name).ShouldBe(["One"]);
        picked.Facets[SearchFields.Meta("tint")].Select(f => f.Value).ShouldBe(["blue", "red"], ignoreOrder: true);
    }

    [Fact]
    public async Task Odd_input_is_answered_with_bad_request_not_a_server_error()
    {
        (await SearchAsync(Admin, new SearchRequest(Filters: [new(SearchFields.Name, SearchOperators.Contains, ["*"])]))).Total.ShouldBe(0);
        (await Admin.PostAsJsonAsync("/api/v1/search", new SearchRequest(Cursor: "tampered"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/search", new SearchRequest(new string('x', 600)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/search", new SearchRequest(Filters: [new(SearchFields.Size, SearchOperators.Between, ["1"])]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Group_grants_count_and_a_role_losing_its_read_permission_hides_its_folders()
    {
        var team = await FolderAsync("Team room");
        var doc = await FolderAsync("Team agenda", team);
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var group = (await (await Admin.PostAsJsonAsync("/api/v1/identity/groups", new GroupRequest("Team " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/groups/{group}/members", new IdListRequest([user.Id]), Ct)).EnsureSuccessStatusCode();
        var role = await RoleAsync(NodePermissions.Read);
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/{NodePermissions.ResourceType}/{team}",
            new GrantResourcePermissionRequest(PrincipalType.Group, group, role), Ct)).EnsureSuccessStatusCode();
        var member = api.As(user.Id, _setup.TenantId);

        (await EventuallyAsync(member, new SearchRequest("agenda"), r => r.Total == 1)).Items.Single().Id.ShouldBe(doc);

        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([]), Ct)).EnsureSuccessStatusCode();

        await EventuallyAsync(member, new SearchRequest("agenda"), r => r.Items.Count == 0);
        (await member.PostAsync("/api/v1/search/reindex", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_reindex_rebuilds_the_index_and_reports()
    {
        var folder = await FolderAsync("Reindexed folder");
        await EventuallyAsync(Admin, new SearchRequest("reindexed"), r => r.Total == 1);

        var run = await (await Admin.PostAsync("/api/v1/search/reindex", null, Ct)).Content.ReadFromJsonAsync<ReindexRunDto>(Ct);

        var deadline = DateTime.UtcNow.AddSeconds(60);
        ReindexRunDto current;
        do
        {
            await Task.Delay(500, Ct);
            current = (await Admin.GetFromJsonAsync<ReindexRunDto[]>("/api/v1/search/reindex", Ct))!.Single(r => r.Id == run!.Id);
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the reindex did not finish");
        }
        while (current.State is "queued" or "running");

        current.State.ShouldBe("completed", current.Error);
        current.Indexed.ShouldBeGreaterThan(0);
        (await SearchAsync(Admin, new SearchRequest("reindexed"))).Items.Single().Id.ShouldBe(folder);
    }

    private async Task<SearchResponse> SearchAsync(HttpClient client, SearchRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/search", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(Ct))!;
    }

    /// <summary>Indexing runs in the background; waits until the result matches.</summary>
    private async Task<SearchResponse> EventuallyAsync(HttpClient client, SearchRequest request, Func<SearchResponse, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (true)
        {
            var result = await SearchAsync(client, request);
            if (done(result))
            {
                return result;
            }

            DateTime.UtcNow.ShouldBeLessThan(deadline, $"search for '{request.Term}' did not reach the expected result, last total {result.Total}");
            await Task.Delay(300, Ct);
        }
    }

    private async Task<PropertyDefinitionDto> DefineAsync(PropertyDefinitionRequest request)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PropertyDefinitionDto>(Ct))!;
    }

    private async Task<Guid> FolderAsync(string name, Guid? parent = null)
    {
        parent ??= (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        var response = await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(parent.Value, name), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
    }

    private async Task<Guid> RoleAsync(params string[] permissions)
    {
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Role " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return role;
    }

    private async Task<(HttpClient Client, Guid UserId, Guid Grant)> ReaderAsync(Guid folder)
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var role = await RoleAsync(NodePermissions.Read);
        var grant = await (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/{NodePermissions.ResourceType}/{folder}",
            new GrantResourcePermissionRequest(PrincipalType.User, user.Id, role), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (api.As(user.Id, _setup.TenantId), user.Id, grant.ValueKind == JsonValueKind.Object ? grant.GetProperty("id").GetGuid() : grant.GetGuid());
    }
}
