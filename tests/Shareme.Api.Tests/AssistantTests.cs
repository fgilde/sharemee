using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;

namespace Shareme.Api.Tests;

public sealed class AssistantTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await api.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Mcp_client_creates_folders_and_sets_metadata_within_the_users_rights()
    {
        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("caption", "Caption"), Ct)).EnsureSuccessStatusCode();
        await using var admin = await McpAsync(Admin);
        var names = (await admin.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).ToList();
        names.ShouldContain("create_folder");
        names.ShouldContain("set_metadata");
        var tools = (await admin.ListToolsAsync(cancellationToken: Ct)).ToDictionary(t => t.Name, t => t.ProtocolTool.Annotations?.ReadOnlyHint);
        (tools["list_children"], tools["search"], tools["create_folder"], tools["set_metadata"]).ShouldBe((true, true, false, false));

        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        var created = await admin.CallToolAsync("create_folder", new Dictionary<string, object?> { ["parentId"] = root, ["name"] = "Summer 2026" }, cancellationToken: Ct);
        created.IsError.ShouldNotBe(true, Text(created));
        var folder = JsonSerializer.Deserialize<NodeDto>(Text(created), JsonSerializerOptions.Web)!;
        folder.Name.ShouldBe("Summer 2026");

        var top = await admin.CallToolAsync("list_children", new Dictionary<string, object?>(), cancellationToken: Ct);
        Text(top).ShouldContain(root.ToString());
        var children = await admin.CallToolAsync("list_children", new Dictionary<string, object?> { ["id"] = root }, cancellationToken: Ct);
        Text(children).ShouldContain("Summer 2026");
        var set = await admin.CallToolAsync("set_metadata", new Dictionary<string, object?>
        {
            ["id"] = folder.Id,
            ["values"] = new Dictionary<string, object> { ["caption"] = new[] { new { v = "Beach" } } },
        }, cancellationToken: Ct);
        set.IsError.ShouldNotBe(true, Text(set));
        var empty = await admin.CallToolAsync("set_metadata", new Dictionary<string, object?> { ["id"] = folder.Id }, cancellationToken: Ct);
        empty.IsError.ShouldBe(true);
        Text(empty).ShouldContain("Values");
        var metadata = (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{folder.Id}/metadata", Ct))!;
        metadata.Properties["caption"].Values.Single().V!.Value.GetString().ShouldBe("Beach");

        // a user with the assistant but without rights on the tree sees nothing and changes nothing
        var reader = await UserAsync(AiPermissions.Chat);
        await using var outsider = await McpAsync(api.As(reader, _setup.TenantId));
        var outsiderTools = (await outsider.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).ToList();
        outsiderTools.ShouldNotContain("find_people");
        Text(await outsider.CallToolAsync("list_children", new Dictionary<string, object?>(), cancellationToken: Ct)).ShouldNotContain("Summer 2026");
        var denied = await outsider.CallToolAsync("create_folder", new Dictionary<string, object?> { ["parentId"] = folder.Id, ["name"] = "Sneaky" }, cancellationToken: Ct);
        denied.IsError.ShouldBe(true);
        (await outsider.CallToolAsync("get_item", new Dictionary<string, object?> { ["id"] = folder.Id }, cancellationToken: Ct)).IsError.ShouldBe(true);

        var calls = (await Admin.GetFromJsonAsync<PagedResult<AiToolCallDto>>("/api/v1/ai/tool-calls?channel=mcp&pageSize=50", Ct))!;
        calls.Items.Count(c => c.Tool == "create_folder").ShouldBe(2);
        calls.Items.Single(c => c.UserId == reader && c.Tool == "create_folder").Succeeded.ShouldBeFalse();
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static Task<McpClient> McpAsync(HttpClient http) =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http), cancellationToken: Ct);

    private async Task<Guid> UserAsync(params string[] permissions)
    {
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", "Some", "One"), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Role " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user;
    }
}
