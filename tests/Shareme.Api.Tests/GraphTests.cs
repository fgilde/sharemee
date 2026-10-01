using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;
using Coworkee.Testing;
using Shareme.Contracts.Graph;

namespace Shareme.Api.Tests;

public sealed class GraphTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await api.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Root_exists_and_lists_created_folders()
    {
        var root = await RootAsync();

        var folder = await CreateAsync(root.Id, "Marketing");

        root.ParentId.ShouldBeNull();
        (await ChildrenAsync(Admin, root.Id)).Select(c => c.Id).ShouldContain(folder.Id);
        folder.Type.ShouldBe(NodeTypeKeys.Folder);
    }

    [Fact]
    public async Task Detail_carries_the_breadcrumb()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var b = await CreateAsync(a.Id, "B");

        var detail = await Admin.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{b.Id}", Ct);

        detail!.Breadcrumb.Select(c => c.Name).ShouldBe(["Root", "A", "B"]);
    }

    [Fact]
    public async Task Moving_a_folder_moves_its_subtree()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var b = await CreateAsync(a.Id, "B");
        var c = await CreateAsync(b.Id, "C");
        var d = await CreateAsync(root.Id, "D");

        (await Admin.PostAsJsonAsync($"/api/v1/nodes/{b.Id}/move", new MoveNodeRequest(d.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Admin.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{c.Id}", Ct))!.Breadcrumb.Select(x => x.Name).ShouldBe(["Root", "D", "B", "C"]);
        (await ChildrenAsync(Admin, a.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_folder_cannot_move_into_its_own_subtree()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var b = await CreateAsync(a.Id, "B");

        (await Admin.PostAsJsonAsync($"/api/v1/nodes/{a.Id}/move", new MoveNodeRequest(b.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Admin.PostAsJsonAsync($"/api/v1/nodes/{root.Id}/move", new MoveNodeRequest(a.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Deleting_moves_the_subtree_to_the_recycle_bin_and_restore_brings_it_back()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var b = await CreateAsync(a.Id, "B");

        (await Admin.DeleteAsync($"/api/v1/nodes/{a.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ChildrenAsync(Admin, root.Id)).ShouldNotContain(n => n.Id == a.Id);
        (await Admin.GetAsync($"/api/v1/nodes/{b.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RecycleBinAsync()).Select(i => i.Id).ShouldBe([a.Id]);

        (await Admin.PostAsync($"/api/v1/recycle-bin/{a.Id}/restore", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Admin.GetAsync($"/api/v1/nodes/{b.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RecycleBinAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Purging_removes_the_subtree_for_good()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        await CreateAsync(a.Id, "B");
        (await Admin.DeleteAsync($"/api/v1/nodes/{a.Id}", Ct)).EnsureSuccessStatusCode();

        (await Admin.DeleteAsync($"/api/v1/recycle-bin/{a.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RecycleBinAsync()).ShouldBeEmpty();
        (await Admin.PostAsync($"/api/v1/recycle-bin/{a.Id}/restore", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_grant_on_a_folder_reaches_its_descendants_only()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var b = await CreateAsync(a.Id, "B");
        var d = await CreateAsync(root.Id, "D");
        var bob = await UserWithFolderRoleAsync(a.Id, NodePermissions.Read);

        (await bob.GetAsync($"/api/v1/nodes/{b.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bob.GetAsync($"/api/v1/nodes/{d.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bob.GetAsync($"/api/v1/nodes/{root.Id}/children", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bob.GetFromJsonAsync<NodeDto[]>("/api/v1/nodes/entry-points", Ct))!.Select(n => n.Id).ShouldBe([a.Id]);
    }

    [Fact]
    public async Task Interrupted_inheritance_hides_a_child_from_inherited_readers()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var secret = await CreateAsync(a.Id, "Secret");
        var open = await CreateAsync(a.Id, "Open");
        var bob = await UserWithFolderRoleAsync(a.Id, NodePermissions.Read);

        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{secret.Id}/inheritance", new InheritanceRequest(Interrupt: true), Ct)).EnsureSuccessStatusCode();

        (await bob.GetAsync($"/api/v1/nodes/{secret.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ChildrenAsync(bob, a.Id)).Select(c => c.Id).ShouldBe([open.Id]);
    }

    [Fact]
    public async Task Readers_cannot_create_rename_or_delete()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var bob = await UserWithFolderRoleAsync(a.Id, NodePermissions.Read);

        (await bob.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(a.Id, "Mine"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bob.PutAsJsonAsync($"/api/v1/nodes/{a.Id}", new UpdateNodeRequest("Renamed"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bob.DeleteAsync($"/api/v1/nodes/{a.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Links_connect_nodes_and_can_be_removed()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var b = await CreateAsync(root.Id, "B");

        var link = await (await Admin.PostAsJsonAsync($"/api/v1/nodes/{a.Id}/edges", new CreateEdgeRequest(EdgeTypeKeys.Link, b.Id), Ct)).Content.ReadFromJsonAsync<EdgeDto>(Ct);

        (await Admin.GetFromJsonAsync<EdgeDto[]>($"/api/v1/nodes/{b.Id}/edges", Ct))!.ShouldContain(e => e.Id == link!.Id && e.FromNodeId == a.Id);
        (await Admin.PostAsJsonAsync($"/api/v1/nodes/{a.Id}/edges", new CreateEdgeRequest(EdgeTypeKeys.Link, b.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Admin.DeleteAsync($"/api/v1/edges/{link!.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin.GetFromJsonAsync<EdgeDto[]>($"/api/v1/nodes/{b.Id}/edges", Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Renaming_is_audited()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");

        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{a.Id}", new UpdateNodeRequest("Alpha"), Ct)).EnsureSuccessStatusCode();

        (await Admin.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{a.Id}", Ct))!.Node.Name.ShouldBe("Alpha");
        var audit = await Admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit?entityId={a.Id}", Ct);
        audit!.Items.Select(e => e.Action).ShouldBe(["Updated", "Created"]);
    }

    [Fact]
    public async Task Subscribers_are_notified_about_changes_in_the_subtree()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var (bob, _) = await UserAsync();
        await GrantAsync(a.Id, bob.UserId, NodePermissions.Read);
        var client = api.As(bob.UserId, _setup.TenantId);
        (await client.PutAsync($"/api/v1/nodes/{a.Id}/subscription", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{a.Id}", Ct))!.IsSubscribed.ShouldBeTrue();

        await CreateAsync(a.Id, "Fresh");

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while ((await client.GetFromJsonAsync<UnreadCountDto>("/api/v1/notifications/unread-count", Ct))!.Count == 0)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "no notification arrived");
            await Task.Delay(250, Ct);
        }
    }

    [Fact]
    public async Task Restoring_an_earlier_bin_entry_follows_a_later_move_of_its_ancestor()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var b = await CreateAsync(a.Id, "B");
        var c = await CreateAsync(b.Id, "C");
        var d = await CreateAsync(root.Id, "D");
        var bob = await UserWithFolderRoleAsync(a.Id, NodePermissions.Read);
        (await Admin.DeleteAsync($"/api/v1/nodes/{c.Id}", Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync($"/api/v1/nodes/{b.Id}/move", new MoveNodeRequest(d.Id), Ct)).EnsureSuccessStatusCode();

        (await Admin.PostAsync($"/api/v1/recycle-bin/{c.Id}/restore", null, Ct)).EnsureSuccessStatusCode();

        (await Admin.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{c.Id}", Ct))!.Breadcrumb.Select(x => x.Name).ShouldBe(["Root", "D", "B", "C"]);
        (await bob.GetAsync($"/api/v1/nodes/{c.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Purging_also_removes_earlier_bin_entries_inside_the_subtree()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var x = await CreateAsync(a.Id, "X");
        (await Admin.DeleteAsync($"/api/v1/nodes/{x.Id}", Ct)).EnsureSuccessStatusCode();
        (await Admin.DeleteAsync($"/api/v1/nodes/{a.Id}", Ct)).EnsureSuccessStatusCode();

        (await Admin.DeleteAsync($"/api/v1/recycle-bin/{a.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RecycleBinAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_breadcrumb_starts_at_the_first_readable_folder()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var b = await CreateAsync(a.Id, "B");
        var bob = await UserWithFolderRoleAsync(a.Id, NodePermissions.Read);

        (await bob.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{b.Id}", Ct))!.Breadcrumb.Select(x => x.Name).ShouldBe(["A", "B"]);
    }

    [Fact]
    public async Task A_grant_below_an_interrupted_folder_is_an_entry_point()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var b = await CreateAsync(a.Id, "B");
        var c = await CreateAsync(b.Id, "C");
        var (bob, client) = await UserAsync();
        await GrantAsync(a.Id, bob.UserId, NodePermissions.Read);
        await GrantAsync(c.Id, bob.UserId, NodePermissions.Read);

        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{b.Id}/inheritance", new InheritanceRequest(Interrupt: true), Ct)).EnsureSuccessStatusCode();

        (await client.GetFromJsonAsync<NodeDto[]>("/api/v1/nodes/entry-points", Ct))!.Select(n => n.Id).ShouldBe([a.Id, c.Id]);
        (await client.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{c.Id}", Ct))!.Breadcrumb.Select(x => x.Name).ShouldBe(["C"]);
    }

    [Fact]
    public async Task Relations_to_unreadable_nodes_are_hidden()
    {
        var root = await RootAsync();
        var a = await CreateAsync(root.Id, "A");
        var secret = await CreateAsync(root.Id, "Secret");
        (await Admin.PostAsJsonAsync($"/api/v1/nodes/{secret.Id}/edges", new CreateEdgeRequest(EdgeTypeKeys.Link, a.Id), Ct)).EnsureSuccessStatusCode();
        var bob = await UserWithFolderRoleAsync(a.Id, NodePermissions.Read);

        (await bob.GetFromJsonAsync<EdgeDto[]>($"/api/v1/nodes/{a.Id}/edges", Ct))!.ShouldBeEmpty();
        (await Admin.GetFromJsonAsync<EdgeDto[]>($"/api/v1/nodes/{a.Id}/edges", Ct))!.ShouldHaveSingleItem();
    }

    private async Task<NodeDto> RootAsync() => (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!;

    private async Task<NodeDto> CreateAsync(Guid parentId, string name)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(parentId, name), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NodeDto>(Ct))!;
    }

    private static async Task<IReadOnlyList<NodeDto>> ChildrenAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{id}/children?pageSize=100", Ct))!.Items;

    private async Task<IReadOnlyList<RecycleBinItemDto>> RecycleBinAsync() =>
        (await Admin.GetFromJsonAsync<PagedResult<RecycleBinItemDto>>("/api/v1/recycle-bin", Ct))!.Items;

    private async Task<HttpClient> UserWithFolderRoleAsync(Guid folder, params string[] permissions)
    {
        var (user, _) = await UserAsync();
        await GrantAsync(folder, user.UserId, permissions);
        return api.As(user.UserId, _setup.TenantId);
    }

    private async Task<((Guid UserId, string Email) User, HttpClient Client)> UserAsync()
    {
        var email = $"{Guid.NewGuid():N}@acme.test";
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        return ((user.Id, email), api.As(user.Id, _setup.TenantId));
    }

    private async Task GrantAsync(Guid folder, Guid user, params string[] permissions)
    {
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Folder role " + Guid.NewGuid().ToString("N")[..6], null), Ct))
            .Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/{NodePermissions.ResourceType}/{folder}",
            new GrantResourcePermissionRequest(PrincipalType.User, user, role), Ct)).EnsureSuccessStatusCode();
    }
}
