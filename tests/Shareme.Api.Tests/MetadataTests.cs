using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;

namespace Shareme.Api.Tests;

public sealed class MetadataTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await api.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Definitions_are_created_with_their_list_values()
    {
        var definition = await DefineAsync(new PropertyDefinitionRequest("color", "Color", ListMode: ListModes.Single));

        (await Admin.PutAsJsonAsync($"/api/v1/metadata/definitions/{definition.Id}/list-values",
            new[] { new PropertyListValueDto("red", "Red", 0, true), new PropertyListValueDto("blue", "Blue", 1, true) }, Ct)).EnsureSuccessStatusCode();

        var listed = await Admin.GetFromJsonAsync<PropertyDefinitionDto[]>("/api/v1/metadata/definitions", Ct);
        listed!.Single(d => d.Key == "color").ListValues.Select(v => v.Key).ShouldBe(["red", "blue"]);
        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("color", "Again"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Values_are_checked_against_type_and_validation()
    {
        await DefineAsync(new PropertyDefinitionRequest("pages", "Pages", PropertyValueTypes.Integer, Validation: new PropertyValidation(Min: 1, Max: 999)));
        await DefineAsync(new PropertyDefinitionRequest("code", "Code", Validation: new PropertyValidation(Regex: "^[A-Z]{3}$")));
        var folder = await FolderAsync("A");

        (await PutAsync(folder, new() { ["pages"] = [Value(12)], ["code"] = [Value("ABC")] })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var errors = await ErrorsAsync(await PutAsync(folder, new() { ["pages"] = [Value(0)], ["code"] = [Value("abc")], ["nope"] = [Value("x")] }));
        errors.Keys.ShouldBe(["pages", "code", "nope"], ignoreOrder: true);
        (await ErrorsAsync(await PutAsync(folder, new() { ["pages"] = [Value("twelve")] }))).Keys.ShouldBe(["pages"]);
    }

    [Fact]
    public async Task Localized_values_need_a_known_language()
    {
        await DefineAsync(new PropertyDefinitionRequest("title", "Title", Localizable: true));
        var folder = await FolderAsync("A");

        (await PutAsync(folder, new() { ["title"] = [Value("Hello", lang: "en")] })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PutAsync(folder, new() { ["title"] = [Value("Hallo", lang: "de")] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(folder, new() { ["title"] = [Value("Hello")] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Admin.PostAsJsonAsync("/api/v1/metadata/languages", new DataLanguageDto("de", "Deutsch", false), Ct)).EnsureSuccessStatusCode();

        (await PutAsync(folder, new() { ["title"] = [Value("Hallo", lang: "de")] })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PutAsync(folder, new() { ["title"] = [Value("Hallo", lang: "de"), Value("Moin", lang: "de")] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_values_must_exist_unless_custom_values_are_allowed()
    {
        var closed = await DefineAsync(new PropertyDefinitionRequest("status", "Status", ListMode: ListModes.Multi));
        await DefineAsync(new PropertyDefinitionRequest("tags", "Tags", ListMode: ListModes.Multi, AllowCustomValues: true));
        (await Admin.PutAsJsonAsync($"/api/v1/metadata/definitions/{closed.Id}/list-values", new[] { new PropertyListValueDto("draft", "Draft", 0, true), new PropertyListValueDto("old", "Old", 1, false) }, Ct))
            .EnsureSuccessStatusCode();
        var folder = await FolderAsync("A");

        (await PutAsync(folder, new() { ["status"] = [new MetadataValue(List: "draft")], ["tags"] = [Value("anything")] })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PutAsync(folder, new() { ["status"] = [new MetadataValue(List: "missing")] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(folder, new() { ["status"] = [new MetadataValue(List: "old")] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(folder, new() { ["status"] = [Value("free text")] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Inherited_values_come_from_the_nearest_ancestor_until_overridden()
    {
        await DefineAsync(new PropertyDefinitionRequest("client", "Client", InheritToChildren: true));
        await DefineAsync(new PropertyDefinitionRequest("note", "Note"));
        var a = await FolderAsync("A");
        var b = await FolderAsync("B", a);
        (await PutAsync(a, new() { ["client"] = [Value("Acme")], ["note"] = [Value("only on A")] })).EnsureSuccessStatusCode();

        var inherited = await MetadataAsync(b);
        inherited.Properties["client"].InheritedFrom.ShouldBe(a);
        inherited.Properties["client"].Values.Single().V!.Value.GetString().ShouldBe("Acme");
        inherited.Properties.ShouldNotContainKey("note");

        (await PutAsync(b, new() { ["client"] = [Value("Globex")] })).EnsureSuccessStatusCode();
        (await MetadataAsync(b)).Properties["client"].InheritedFrom.ShouldBeNull();
    }

    [Fact]
    public async Task Required_values_may_be_inherited_and_patch_changes_single_keys()
    {
        await DefineAsync(new PropertyDefinitionRequest("owner", "Owner", InheritToChildren: true, Validation: new PropertyValidation(Required: true)));
        await DefineAsync(new PropertyDefinitionRequest("note", "Note"));
        var a = await FolderAsync("A");
        var b = await FolderAsync("B", a);

        (await PutAsync(a, new() { ["note"] = [Value("x")] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(a, new() { ["owner"] = [Value("Ada")] })).EnsureSuccessStatusCode();
        (await PutAsync(b, new() { ["note"] = [Value("fine, owner comes from A")] })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await PatchAsync(a, new() { ["note"] = [Value("patched")] })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MetadataAsync(a)).Properties.Keys.ShouldBe(["note", "owner"], ignoreOrder: true);
        (await PatchAsync(a, new() { ["owner"] = null })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PatchAsync(a, new() { ["note"] = null })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MetadataAsync(a)).Properties.Keys.ShouldBe(["owner"]);
    }

    [Fact]
    public async Task Definitions_apply_to_their_node_types_only()
    {
        await DefineAsync(new PropertyDefinitionRequest("iso", "ISO", PropertyValueTypes.Integer, NodeTypeKeys: [NodeTypeKeys.Asset]));
        var folder = await FolderAsync("A");

        (await ErrorsAsync(await PutAsync(folder, new() { ["iso"] = [Value(100)] }))).Keys.ShouldBe(["iso"]);
    }

    [Fact]
    public async Task Readers_see_values_but_cannot_change_them_or_the_catalog()
    {
        await DefineAsync(new PropertyDefinitionRequest("note", "Note"));
        var folder = await FolderAsync("A");
        (await PutAsync(folder, new() { ["note"] = [Value("hi")] })).EnsureSuccessStatusCode();
        var bob = await ReaderAsync(folder);

        (await bob.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{folder}/metadata", Ct))!.Properties["note"].Values.Single().V!.Value.GetString().ShouldBe("hi");
        (await bob.PutAsJsonAsync($"/api/v1/nodes/{folder}/metadata", new Dictionary<string, List<MetadataValue>>(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bob.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("x", "X"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bob.GetAsync("/api/v1/metadata/definitions", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Property_sets_refer_to_known_definitions()
    {
        await DefineAsync(new PropertyDefinitionRequest("note", "Note"));

        (await Admin.PostAsJsonAsync("/api/v1/metadata/sets", new PropertySetRequest(NodeTypeKeys.Folder, "Bad", true, [new("missing")]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/metadata/sets", new PropertySetRequest(NodeTypeKeys.Folder, "Folders", true, [new("note", FillModes.Mandatory)]), Ct)).EnsureSuccessStatusCode();

        var sets = await Admin.GetFromJsonAsync<PropertySetDto[]>($"/api/v1/metadata/sets?nodeType={NodeTypeKeys.Folder}", Ct);
        sets!.Single().Items.Single().FillMode.ShouldBe(FillModes.Mandatory);
    }

    private static MetadataValue Value<T>(T value, string? lang = null) => new(lang, null, JsonSerializer.SerializeToElement(value));

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

    private Task<HttpResponseMessage> PutAsync(Guid node, Dictionary<string, List<MetadataValue>> values) =>
        Admin.PutAsJsonAsync($"/api/v1/nodes/{node}/metadata", values, Ct);

    private Task<HttpResponseMessage> PatchAsync(Guid node, Dictionary<string, List<MetadataValue>?> values) =>
        Admin.PatchAsJsonAsync($"/api/v1/nodes/{node}/metadata", values, Ct);

    private async Task<NodeMetadataDto> MetadataAsync(Guid node) => (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{node}/metadata", Ct))!;

    private static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    private async Task<HttpClient> ReaderAsync(Guid folder)
    {
        var email = $"{Guid.NewGuid():N}@acme.test";
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Reader " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([NodePermissions.Read]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/{NodePermissions.ResourceType}/{folder}",
            new GrantResourcePermissionRequest(PrincipalType.User, user.Id, role), Ct)).EnsureSuccessStatusCode();
        return api.As(user.Id, _setup.TenantId);
    }
}
