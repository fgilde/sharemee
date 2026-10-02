using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;

namespace Shareme.Api.Tests;

public sealed class RestrictionTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _folder, _public, _confidential, _external;
    private HttpClient _user = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await api.SetupAsync();
        await DefineAsync(new PropertyDefinitionRequest("secret", "Secret"));
        await DefineAsync(new PropertyDefinitionRequest("status", "Status"));
        await DefineAsync(new PropertyDefinitionRequest("note", "Note"));
        var classification = await DefineAsync(new PropertyDefinitionRequest("class", "Class", ListMode: ListModes.Single));
        (await Admin.PutAsJsonAsync($"/api/v1/metadata/definitions/{classification.Id}/list-values",
            new[] { new PropertyListValueDto("public", "Public", 0, true), new PropertyListValueDto("confidential", "Confidential", 1, true) }, Ct)).EnsureSuccessStatusCode();

        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        _folder = await FolderAsync("Library", root);
        _public = await FolderAsync("open", _folder);
        _confidential = await FolderAsync("closed", _folder);
        foreach (var (node, value) in new[] { (_public, "public"), (_confidential, "confidential") })
        {
            (await Admin.PatchAsJsonAsync($"/api/v1/nodes/{node}/metadata", new Dictionary<string, List<MetadataValue>?>
            {
                ["class"] = [new MetadataValue(List: value)], ["secret"] = [Text("s1")], ["status"] = [Text("draft")],
            }, Ct)).EnsureSuccessStatusCode();
        }

        // an editor on the folder, and in the role the restrictions are for
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var editor = await RoleAsync("Editor", NodePermissions.Read, NodePermissions.Update, AssetPermissions.Download);
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/{NodePermissions.ResourceType}/{_folder}", new GrantResourcePermissionRequest(PrincipalType.User, user.Id, editor), Ct))
            .EnsureSuccessStatusCode();
        _external = await RoleAsync("External");
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([_external]), Ct)).EnsureSuccessStatusCode();
        _user = api.As(user.Id, _setup.TenantId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Hidden_properties_are_neither_shown_nor_changed_and_read_only_ones_stay()
    {
        await RestrictAsync(RestrictionKinds.HiddenProperty, "secret");
        await RestrictAsync(RestrictionKinds.ReadOnlyProperty, "status");

        var metadata = (await _user.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{_public}/metadata", Ct))!;
        metadata.Properties.ShouldNotContainKey("secret");
        metadata.Properties.ShouldContainKey("status");
        (await _user.GetFromJsonAsync<PropertyDefinitionDto[]>("/api/v1/metadata/definitions", Ct))!.Select(d => d.Key).ShouldNotContain("secret");

        (await _user.PatchAsJsonAsync($"/api/v1/nodes/{_public}/metadata", new Dictionary<string, List<MetadataValue>?> { ["secret"] = [Text("mine")] }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _user.PatchAsJsonAsync($"/api/v1/nodes/{_public}/metadata", new Dictionary<string, List<MetadataValue>?> { ["status"] = [Text("done")] }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // replacing everything keeps what the user may not change
        (await _user.PutAsJsonAsync($"/api/v1/nodes/{_public}/metadata", new Dictionary<string, List<MetadataValue>> { ["note"] = [Text("hello")], ["class"] = [new MetadataValue(List: "public")] }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var all = (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{_public}/metadata", Ct))!;
        all.Properties["secret"].Values.Single().V!.Value.GetString().ShouldBe("s1");
        all.Properties["status"].Values.Single().V!.Value.GetString().ShouldBe("draft");
        all.Properties["note"].Values.Single().V!.Value.GetString().ShouldBe("hello");

        var export = await (await _user.PostAsJsonAsync("/api/v1/exports", new ExportRequest(NodeIds: [_public], Format: ExportFormats.Csv), Ct)).Content.ReadAsStringAsync(Ct);
        export.ShouldNotContain("secret");
        export.ShouldContain("status");
    }

    [Fact]
    public async Task Nodes_with_a_hidden_list_value_are_out_of_reach_for_the_role_only()
    {
        await RestrictAsync(RestrictionKinds.HiddenListValue, "class", "confidential");

        var children = (await _user.GetFromJsonAsync<JsonElement>($"/api/v1/nodes/{_folder}/children", Ct)).GetProperty("items").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        children.ShouldBe(["open"]);
        (await _user.GetAsync($"/api/v1/nodes/{_confidential}", Ct)).IsSuccessStatusCode.ShouldBeFalse();
        (await _user.GetAsync($"/api/v1/nodes/{_confidential}/metadata", Ct)).IsSuccessStatusCode.ShouldBeFalse();
        (await _user.PatchAsJsonAsync($"/api/v1/nodes/{_confidential}/metadata", new Dictionary<string, List<MetadataValue>?> { ["note"] = [Text("x")] }, Ct)).IsSuccessStatusCode.ShouldBeFalse();
        (await _user.GetAsync($"/api/v1/nodes/{_public}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Admin.GetFromJsonAsync<JsonElement>($"/api/v1/nodes/{_folder}/children", Ct)).GetProperty("items").GetArrayLength().ShouldBe(2);
        (await Admin.GetAsync($"/api/v1/nodes/{_confidential}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Blocked_renditions_cannot_be_fetched()
    {
        await RestrictAsync(RestrictionKinds.BlockedRendition, value: DerivationKinds.Preview);
        using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Teal, 64, 48);
        var asset = (await WorkspaceTests.UploadAsync(Admin, "teal.png", image.ToByteArray(ImageMagick.MagickFormat.Png), _folder)).AssetId;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!(await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{asset}", Ct))!.Derivations.Any(d => d.Kind == DerivationKinds.Preview && d.State == DerivationStates.Ready))
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the preview was not rendered in time");
            await Task.Delay(250, Ct);
        }

        (await _user.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{asset}", Ct))!.Derivations.Select(d => d.Kind).ShouldNotContain(DerivationKinds.Preview);
        (await _user.GetAsync($"/api/v1/assets/{asset}/derivations/{DerivationKinds.Preview}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _user.PostAsJsonAsync("/api/v1/downloads", new DownloadPackageRequest([asset], Kind: DerivationKinds.Preview), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetAsync($"/api/v1/assets/{asset}/derivations/{DerivationKinds.Preview}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Restrictions_are_checked_and_managed_by_admins_and_runs_are_listed_per_user()
    {
        (await Admin.PostAsJsonAsync("/api/v1/restrictions", new RestrictionRequest(_external, RestrictionKinds.HiddenListValue, "class"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/restrictions", new RestrictionRequest(Guid.NewGuid(), RestrictionKinds.HiddenProperty, "secret"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var restriction = await RestrictAsync(RestrictionKinds.HiddenProperty, "secret");
        (await Admin.PostAsJsonAsync("/api/v1/restrictions", new RestrictionRequest(_external, RestrictionKinds.HiddenProperty, "secret"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await _user.GetAsync("/api/v1/restrictions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.DeleteAsync($"/api/v1/restrictions/{restriction.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin.GetFromJsonAsync<RestrictionDto[]>("/api/v1/restrictions", Ct))!.ShouldBeEmpty();

        var run = await (await Admin.PostAsJsonAsync("/api/v1/mass-edits", new MassEditRequest([new PropertyMutation("note", MassEditStrategies.Overwrite, Texts: ["x"])], NodeIds: [_public]), Ct))
            .Content.ReadFromJsonAsync<DataJobDto>(Ct);
        var overview = (await Admin.GetFromJsonAsync<JobsOverviewDto>("/api/v1/jobs", Ct))!;
        overview.Runs.ShouldContain(r => r.Id == run!.Id && r.Kind == DataJobKinds.MassEdit && r.Link == $"/data-jobs/{run.Id}");
        overview.DashboardUrl.ShouldBe("/admin/jobs");
        var mine = (await _user.GetFromJsonAsync<JobsOverviewDto>("/api/v1/jobs", Ct))!;
        mine.Runs.ShouldBeEmpty();
        mine.DashboardUrl.ShouldBeNull();
    }

    private async Task<RestrictionDto> RestrictAsync(string kind, string? key = null, string? value = null)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/restrictions", new RestrictionRequest(_external, kind, key, value), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<RestrictionDto>(Ct))!;
    }

    private async Task<Guid> RoleAsync(string name, params string[] permissions)
    {
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name + " " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return role;
    }

    private async Task<PropertyDefinitionDto> DefineAsync(PropertyDefinitionRequest request)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PropertyDefinitionDto>(Ct))!;
    }

    private async Task<Guid> FolderAsync(string name, Guid parent)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(parent, name), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
    }

    private static MetadataValue Text(string text) => new(V: JsonSerializer.SerializeToElement(text));
}
