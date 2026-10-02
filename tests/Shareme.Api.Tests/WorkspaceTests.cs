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
        (await owner.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members", new AddWorkspaceMemberRequest(ownerId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict, "the last owner cannot step down");

        // settings belong to owners: a collaborator cannot drop the folder or the required set
        (await member.PutAsJsonAsync($"/api/v1/workspaces/{workspace.Id}", new UpdateWorkspaceRequest("Hijacked"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.GetFromJsonAsync<WorkspaceMemberDto[]>($"/api/v1/workspaces/{workspace.Id}/candidates?search=ac", Ct))!.ShouldBeEmpty("searches need three characters");

        // collaborators upload into the workspace; the folder tree does not show it
        var upload = await UploadAsync(member, "draft.txt", "hello"u8.ToArray(), workspace.Id);
        var sub = (await (await member.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(workspace.Id, "Drafts"), Ct)).Content.ReadFromJsonAsync<NodeDto>(Ct))!;
        var nested = await UploadAsync(member, "nested.txt", "nested"u8.ToArray(), sub.Id);
        (await member.GetFromJsonAsync<WorkspaceItemDto[]>($"/api/v1/workspaces/{workspace.Id}/items", Ct))!.Select(i => i.Node.Id).ShouldBe([sub.Id, upload.AssetId], ignoreOrder: true);
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
        (await Admin.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{nested.AssetId}", Ct))!.Breadcrumb.Select(c => c.Name).ShouldContain("Drafts", "subfolders move with their content");
        (await Admin.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{sub.Id}", Ct))!.Node.ParentId.ShouldBe(target);
        (await owner.PostAsJsonAsync("/api/v1/uploads", new StartUploadRequest("late.txt", 4, workspace.Id), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, "finalised workspaces take no files");
        (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{upload.AssetId}/metadata", Ct))!.Properties.Keys.ShouldContain("caption");
        (await owner.GetFromJsonAsync<WorkspaceDto>($"/api/v1/workspaces/{workspace.Id}", Ct))!.FinalizedAt.ShouldNotBeNull();
        (await owner.PostAsync($"/api/v1/workspaces/{workspace.Id}/finalize", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Content_deleted_inside_a_workspace_is_restored_into_it_never_into_the_folder_tree()
    {
        var (owner, _) = await UserAsync(WorkspacePermissions.Create);
        var workspace = (await (await owner.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest("Secret"), Ct)).Content.ReadFromJsonAsync<WorkspaceDto>(Ct))!;
        var sub = (await (await owner.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(workspace.Id, "Inner"), Ct)).Content.ReadFromJsonAsync<NodeDto>(Ct))!;
        var file = await UploadAsync(owner, "secret.txt", "secret"u8.ToArray(), sub.Id);

        (await owner.DeleteAsync($"/api/v1/nodes/{file.AssetId}", Ct)).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/v1/nodes/{sub.Id}", Ct)).EnsureSuccessStatusCode();
        (await owner.PostAsync($"/api/v1/recycle-bin/{file.AssetId}/restore", null, Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<NodeDetailDto>($"/api/v1/nodes/{file.AssetId}", Ct))!.Node.ParentId.ShouldBe(workspace.Id);
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

    [Fact]
    public async Task Tasks_move_across_the_board_along_its_transitions()
    {
        var (owner, _) = await UserAsync(WorkspacePermissions.Create);
        var (member, memberId) = await UserAsync();
        var (stranger, strangerId) = await UserAsync();
        var workspace = (await (await owner.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest("Launch"), Ct)).Content.ReadFromJsonAsync<WorkspaceDto>(Ct))!;
        (await owner.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members", new AddWorkspaceMemberRequest(memberId), Ct)).EnsureSuccessStatusCode();
        var states = (await member.GetFromJsonAsync<StateDto[]>($"/api/v1/workspaces/{workspace.Id}/states", Ct))!;
        states.Select(s => s.Name).ShouldBe(["To do", "In progress", "Done"]);
        var (todo, doing, done) = (states[0], states[1], states[2]);

        // only owners close tasks, and to-do tasks go through in progress
        (await member.PutAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/states/{done.Id}", new StateRequest("Done", IsDone: true, OwnersOnly: true), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.PutAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/states/{done.Id}", new StateRequest("Done", IsDone: true, OwnersOnly: true), Ct)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/states/{todo.Id}", new StateRequest("To do", IsDefault: true, AllowedTargets: [doing.Id]), Ct)).EnsureSuccessStatusCode();

        (await owner.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/tasks", new TaskRequest("Nope", AssigneeIds: [strangerId]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await stranger.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/tasks", new TaskRequest("Nope"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var first = await TaskAsync(owner, workspace.Id, new TaskRequest("Brief", "Write it", new DateOnly(2026, 11, 1), AssigneeIds: [memberId]));
        var second = await TaskAsync(member, workspace.Id, new TaskRequest("Visuals"));
        var third = await TaskAsync(member, workspace.Id, new TaskRequest("Copy"));
        first.StateId.ShouldBe(todo.Id);
        first.AssigneeIds.ShouldBe([memberId]);

        // reordering within the column
        (await MoveAsync(member, third, todo.Id, 0)).EnsureSuccessStatusCode();
        (await member.GetFromJsonAsync<TaskDto[]>($"/api/v1/workspaces/{workspace.Id}/tasks", Ct))!.Where(t => t.StateId == todo.Id).OrderBy(t => t.SortIndex).Select(t => t.Title)
            .ShouldBe(["Copy", "Brief", "Visuals"]);

        (await MoveAsync(member, first, done.Id, 0)).StatusCode.ShouldBe(HttpStatusCode.Conflict, "to do only leads to in progress");
        var moved = await (await MoveAsync(member, first, doing.Id, 0)).Content.ReadFromJsonAsync<TaskDto>(Ct);
        (await MoveAsync(member, first, doing.Id, 0, first.Version)).StatusCode.ShouldBe(HttpStatusCode.Conflict, "the version the client saw is outdated");
        (await MoveAsync(member, moved!, done.Id, 0)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var closed = (await (await MoveAsync(owner, moved!, done.Id, 0)).Content.ReadFromJsonAsync<TaskDto>(Ct))!;

        // the answer carries the version for the next move
        (await MoveAsync(owner, closed, done.Id, 0, closed.Version)).StatusCode.ShouldBe(HttpStatusCode.OK);
        closed.CompletedAt.ShouldNotBeNull();

        // tasks are no items, and removing a column keeps its tasks
        (await owner.GetFromJsonAsync<WorkspaceItemDto[]>($"/api/v1/workspaces/{workspace.Id}/items", Ct))!.ShouldBeEmpty();
        (await owner.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/states/{doing.Id}", Ct)).EnsureSuccessStatusCode();
        (await owner.GetFromJsonAsync<TaskDto[]>($"/api/v1/workspaces/{workspace.Id}/tasks", Ct))!.Count(t => t.StateId == todo.Id).ShouldBe(2);
        (await owner.GetFromJsonAsync<StateDto[]>($"/api/v1/workspaces/{workspace.Id}/states", Ct))!.Single(s => s.Id == todo.Id).AllowedTargets.ShouldBe([todo.Id], "its only way out is gone, so it has none");

        // someone assigned before who has left does not block saving
        (await owner.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{memberId}", Ct)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/tasks/{first.Id}", new TaskRequest("Brief v2", AssigneeIds: [memberId]), Ct)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/tasks/{second.Id}", new TaskRequest("Visuals v2", AssigneeIds: [memberId]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, "new assignees must be members");
        (await stranger.PutAsJsonAsync($"/api/v1/tasks/{second.Id}", new TaskRequest("Hijack"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Comments_mentions_assignments_and_moves_reach_the_people_concerned()
    {
        var (owner, ownerId) = await UserAsync(WorkspacePermissions.Create);
        var (member, memberId) = await UserAsync();
        var (stranger, strangerId) = await UserAsync();
        var workspace = (await (await owner.PostAsJsonAsync("/api/v1/workspaces", new CreateWorkspaceRequest("Launch"), Ct)).Content.ReadFromJsonAsync<WorkspaceDto>(Ct))!;
        (await owner.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members", new AddWorkspaceMemberRequest(memberId), Ct)).EnsureSuccessStatusCode();
        await NotifiedAsync(member, "workspace.member");

        var task = await TaskAsync(owner, workspace.Id, new TaskRequest("Brief", AssigneeIds: [memberId]));
        await NotifiedAsync(member, "task.assigned");

        // the member answers on the task and mentions the owner, who watches the workspace but is told once, as mentioned
        (await member.PostAsJsonAsync($"/api/v1/nodes/{task.Id}/comments", new CommentRequest("Draft is up", [strangerId]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var comment = (await (await member.PostAsJsonAsync($"/api/v1/nodes/{task.Id}/comments", new CommentRequest("Draft is up", [ownerId]), Ct)).Content.ReadFromJsonAsync<CommentDto>(Ct))!;
        await NotifiedAsync(owner, "comment.mention");
        (await owner.GetFromJsonAsync<PagedResult<Coworkee.Contracts.Notifications.NotificationDto>>("/api/v1/notifications", Ct))!.Items.ShouldNotContain(n => n.Type == "comment");
        (await stranger.GetAsync($"/api/v1/nodes/{task.Id}/comments", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.GetFromJsonAsync<CommentDto[]>($"/api/v1/nodes/{task.Id}/comments", Ct))!.Single().CanEdit.ShouldBeTrue("owners moderate");

        (await member.PutAsJsonAsync($"/api/v1/comments/{comment.Id}", new CommentRequest("Draft v2 is up", [ownerId]), Ct)).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/comments/{comment.Id}", new CommentRequest("Not mine"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound, "only authors edit");
        var mine = (await (await owner.PostAsJsonAsync($"/api/v1/nodes/{workspace.Id}/comments", new CommentRequest("Kick-off monday"), Ct)).Content.ReadFromJsonAsync<CommentDto>(Ct))!;
        (await member.DeleteAsync($"/api/v1/comments/{mine.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/comments/{comment.Id}", Ct)).EnsureSuccessStatusCode();

        // assignees learn where their task went
        var states = (await owner.GetFromJsonAsync<StateDto[]>($"/api/v1/workspaces/{workspace.Id}/states", Ct))!;
        (await MoveAsync(owner, task, states[1].Id, 0)).EnsureSuccessStatusCode();
        (await NotifiedAsync(member, "task.moved")).Title.ShouldContain("In progress");
    }

    private static async Task<Coworkee.Contracts.Notifications.NotificationDto> NotifiedAsync(HttpClient client, string type)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var list = (await client.GetFromJsonAsync<PagedResult<Coworkee.Contracts.Notifications.NotificationDto>>("/api/v1/notifications", Ct))!;
            if (list.Items.FirstOrDefault(n => n.Type == type) is { } found)
            {
                return found;
            }

            DateTime.UtcNow.ShouldBeLessThan(deadline, $"no {type} notification arrived");
            await Task.Delay(250, Ct);
        }
    }

    private static async Task<TaskDto> TaskAsync(HttpClient client, Guid workspace, TaskRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspace}/tasks", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDto>(Ct))!;
    }

    /// <summary>Moves with the version the board shows now, or with the given (old) one.</summary>
    private static async Task<HttpResponseMessage> MoveAsync(HttpClient client, TaskDto task, Guid state, int index, uint? version = null)
    {
        version ??= (await client.GetFromJsonAsync<TaskDto[]>($"/api/v1/workspaces/{task.WorkspaceId}/tasks", Ct))!.Single(t => t.Id == task.Id).Version;
        return await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/move", new MoveTaskRequest(state, index, version.Value), Ct);
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
