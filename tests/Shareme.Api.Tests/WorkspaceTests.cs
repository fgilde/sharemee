using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Contracts.Workspaces;

namespace Shareme.Api.Tests;

public sealed class WorkspaceTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await api.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Members_reach_a_workspace_through_its_grants_and_finalise_it_into_a_folder()
    {
        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("caption", "Caption"), Ct)).EnsureSuccessStatusCode();
        var target = await FolderAsync("Approved");
        var (owner, ownerId) = await UserAsync(WorkspacePermissions.Create);
        var (member, memberId) = await UserAsync();
        await GrantOnNodeAsync(target, ownerId, NodePermissions.Read, NodePermissions.Create);

        var workspace = (await (await owner.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest("Campaign", WorkspaceKinds.Upload, FinalizationFolderId: target, CopyMetadataToAssets: true), Ct)).Content.ReadFromJsonAsync<WorkspaceDto>(Ct))!;
        workspace.MyRole.ShouldBe(WorkspaceRoles.Owner);
        (await member.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest("Nope"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // not a member yet: nothing to see
        (await member.GetFromJsonAsync<WorkspaceDto[]>("/api/v1/workspaces", Ct))!.ShouldBeEmpty();
        (await member.GetAsync($"/api/v1/workspaces/{workspace.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await owner.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members", new AddWorkspaceMemberRequest(memberId), Ct)).EnsureSuccessStatusCode();
        (await member.GetFromJsonAsync<WorkspaceDto[]>("/api/v1/workspaces", Ct))!.Single().MyRole.ShouldBe(WorkspaceRoles.Collaborator);
        var members = (await member.GetFromJsonAsync<WorkspaceMemberDto[]>($"/api/v1/workspaces/{workspace.Id}/members", Ct))!;
        members.Select(m => (m.UserId, m.Role)).ShouldBe([(ownerId, WorkspaceRoles.Owner), (memberId, WorkspaceRoles.Collaborator)], ignoreOrder: true);
        (await member.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members", new AddWorkspaceMemberRequest(memberId, WorkspaceRoles.Owner), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{ownerId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // collaborators upload into the workspace; the folder tree does not show it
        var upload = await UploadAsync(member, "draft.txt", "hello"u8.ToArray(), workspace.Id);
        (await member.GetFromJsonAsync<WorkspaceItemDto[]>($"/api/v1/workspaces/{workspace.Id}/items", Ct))!.Single().Node.Id.ShouldBe(upload.AssetId);
        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{root}/children", Ct))!.Items.ShouldNotContain(n => n.Id == workspace.Id);
        (await member.GetFromJsonAsync<NodeDto[]>("/api/v1/nodes/entry-points", Ct))!.ShouldBeEmpty();

        (await owner.PutAsJsonAsync($"/api/v1/nodes/{workspace.Id}/metadata", new Dictionary<string, List<MetadataValue>>
        {
            ["caption"] = [new(V: JsonSerializer.SerializeToElement("Spring"))],
        }, Ct)).EnsureSuccessStatusCode();
        (await member.PostAsync($"/api/v1/workspaces/{workspace.Id}/finalize", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var finalized = (await (await owner.PostAsync($"/api/v1/workspaces/{workspace.Id}/finalize", null, Ct)).Content.ReadFromJsonAsync<FinalizeResultDto>(Ct))!;
        finalized.Moved.ShouldBe(1);
        (await Admin.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{upload.AssetId}", Ct))!.Node.ParentId.ShouldBe(target);
        (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{upload.AssetId}/metadata", Ct))!.Properties.Keys.ShouldContain("caption");
        (await owner.GetFromJsonAsync<WorkspaceDto>($"/api/v1/workspaces/{workspace.Id}", Ct))!.FinalizedAt.ShouldNotBeNull();
        (await owner.PostAsync($"/api/v1/workspaces/{workspace.Id}/finalize", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Templates_pass_on_metadata_and_members_and_collections_show_only_readable_assets()
    {
        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("client", "Client"), Ct)).EnsureSuccessStatusCode();
        var (creator, creatorId) = await UserAsync(WorkspacePermissions.Create);
        var (reviewer, reviewerId) = await UserAsync();
        (await creator.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest("Template", IsTemplate: true), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var template = (await (await Admin.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest("Agency job", WorkspaceKinds.Collection, IsTemplate: true), Ct))
            .Content.ReadFromJsonAsync<WorkspaceDto>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{template.Id}/metadata", new Dictionary<string, List<MetadataValue>>
        {
            ["client"] = [new(V: JsonSerializer.SerializeToElement("Acme"))],
        }, Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync($"/api/v1/workspaces/{template.Id}/members", new AddWorkspaceMemberRequest(reviewerId), Ct)).EnsureSuccessStatusCode();

        (await creator.GetFromJsonAsync<WorkspaceDto[]>("/api/v1/workspaces?templates=true", Ct))!.Select(t => t.Name).ShouldBe(["Agency job"]);
        var job = (await (await creator.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest("Acme spring", TemplateId: template.Id), Ct)).Content.ReadFromJsonAsync<WorkspaceDto>(Ct))!;
        job.Kind.ShouldBe(WorkspaceKinds.Collection);
        job.TemplateId.ShouldBe(template.Id);
        (await creator.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{job.Id}/metadata", Ct))!.Properties.Keys.ShouldContain("client");
        (await reviewer.GetFromJsonAsync<WorkspaceDto[]>("/api/v1/workspaces", Ct))!.Select(w => w.Name).ShouldBe(["Acme spring"]);

        // the creator links an asset only they may read; the reviewer does not see it
        var folder = await FolderAsync("Library");
        await GrantOnNodeAsync(folder, creatorId, NodePermissions.Read);
        var asset = await UploadAsync(Admin, "logo.txt", "logo"u8.ToArray(), folder);
        (await reviewer.PostAsJsonAsync($"/api/v1/workspaces/{job.Id}/links", new LinkAssetsRequest([asset.AssetId]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await creator.PostAsJsonAsync($"/api/v1/workspaces/{job.Id}/links", new LinkAssetsRequest([asset.AssetId]), Ct)).EnsureSuccessStatusCode();
        (await creator.GetFromJsonAsync<WorkspaceItemDto[]>($"/api/v1/workspaces/{job.Id}/items", Ct))!.Single().IsLinked.ShouldBeTrue();
        (await reviewer.GetFromJsonAsync<WorkspaceItemDto[]>($"/api/v1/workspaces/{job.Id}/items", Ct))!.ShouldBeEmpty();
        (await creator.DeleteAsync($"/api/v1/workspaces/{job.Id}/links/{asset.AssetId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<(HttpClient Client, Guid UserId)> UserAsync(params string[] permissions)
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        if (permissions.Length > 0)
        {
            var role = await RoleAsync(permissions);
            (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        }

        return (api.As(user.Id, _setup.TenantId), user.Id);
    }

    private async Task<Guid> RoleAsync(string[] permissions)
    {
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Role " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return role;
    }

    private async Task GrantOnNodeAsync(Guid node, Guid userId, params string[] permissions) =>
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/{NodePermissions.ResourceType}/{node}",
            new GrantResourcePermissionRequest(PrincipalType.User, userId, await RoleAsync(permissions)), Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> FolderAsync(string name)
    {
        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        var response = await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(root, name), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
    }

    private static async Task<UploadResultDto> UploadAsync(HttpClient client, string fileName, byte[] content, Guid folder)
    {
        var start = await client.PostAsJsonAsync("/api/v1/uploads", new StartUploadRequest(fileName, content.Length, folder, null, Convert.ToHexStringLower(SHA256.HashData(content))), Ct);
        start.EnsureSuccessStatusCode();
        var session = (await start.Content.ReadFromJsonAsync<UploadSessionDto>(Ct))!;
        using (var body = new ByteArrayContent(content))
        {
            body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            (await client.PutAsync($"/api/v1/uploads/{session.Id}/chunks/0", body, Ct)).EnsureSuccessStatusCode();
        }

        (await client.PostAsync($"/api/v1/uploads/{session.Id}/complete", null, Ct)).EnsureSuccessStatusCode();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var state = (await client.GetFromJsonAsync<UploadSessionDto>($"/api/v1/uploads/{session.Id}", Ct))!;
            if (state.State is UploadStates.Completed or UploadStates.Failed)
            {
                state.State.ShouldBe(UploadStates.Completed, state.Error);
                return state.Result!;
            }

            DateTime.UtcNow.ShouldBeLessThan(deadline, "the upload was not processed in time");
            await Task.Delay(250, Ct);
        }
    }
}
