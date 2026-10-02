using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;

namespace Shareme.Api.Tests;

public sealed class RuleTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await api.SetupAsync();
        foreach (var key in new[] { "status", "label", "verdict", "counter", "brand", "code" })
        {
            (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest(key, key), Ct)).EnsureSuccessStatusCode();
        }

        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("pages", "Pages", PropertyValueTypes.Integer), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("title", "Title", Localizable: true), Ct)).EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Rules_run_live_with_then_and_else_branches_and_do_not_trigger_themselves()
    {
        await RuleAsync(new RuleRequest("Label",
            [new RuleCondition(RuleConditionKinds.Trigger, RuleOperators.ValueChanged, "status")],
            [new RuleAction("label", "{fnc.find(status)}-{var.year}")]));
        await RuleAsync(new RuleRequest("Verdict",
            [new RuleCondition(RuleConditionKinds.Filter, RuleOperators.HasValue, "status"), new RuleCondition(RuleConditionKinds.Trigger, RuleOperators.EqualTo, "status", "done")],
            [new RuleAction("verdict", "yes"), new RuleAction("verdict", "no", OnMatch: false)]));
        await RuleAsync(new RuleRequest("Counter",
            [new RuleCondition(RuleConditionKinds.Trigger, RuleOperators.ValueChanged, "counter")],
            [new RuleAction("counter", "{fnc.find(counter)}x")]));
        var folder = await FolderAsync("A");

        await PatchAsync(folder, new() { ["status"] = [Text("done")], ["counter"] = [Text("a")] });
        await EventuallyAsync(folder, m => Get(m, "label") == $"done-{DateTime.UtcNow.Year}" && Get(m, "verdict") == "yes" && Get(m, "counter") == "ax");

        await PatchAsync(folder, new() { ["status"] = [Text("open")] });
        await EventuallyAsync(folder, m => Get(m, "label") == $"open-{DateTime.UtcNow.Year}" && Get(m, "verdict") == "no");

        await Task.Delay(TimeSpan.FromSeconds(3), Ct);
        Get(await MetadataAsync(folder), "counter").ShouldBe("ax");
    }

    [Fact]
    public async Task New_nodes_and_new_assets_trigger_rules_for_their_types()
    {
        await RuleAsync(new RuleRequest("New",
            [new RuleCondition(RuleConditionKinds.Trigger, RuleOperators.IsNewNode)],
            [new RuleAction("label", "new {var.name}")]));
        await RuleAsync(new RuleRequest("Folders only",
            [new RuleCondition(RuleConditionKinds.Trigger, RuleOperators.IsNewNode)],
            [new RuleAction("verdict", "folder")], NodeTypeKeys: [NodeTypeKeys.Folder]));
        var folder = await FolderAsync("Brand");
        var upload = await WorkspaceTests.UploadAsync(Admin, "logo.txt", "logo"u8.ToArray(), folder);

        await EventuallyAsync(folder, m => Get(m, "label") == "new Brand" && Get(m, "verdict") == "folder");
        await EventuallyAsync(upload.AssetId, m => Get(m, "label") == "new logo.txt");
        Get(await MetadataAsync(upload.AssetId), "verdict").ShouldBeNull();
    }

    [Fact]
    public async Task A_dry_run_renders_templates_without_writing()
    {
        var rule = await RuleAsync(new RuleRequest("Code",
            [new RuleCondition(RuleConditionKinds.Filter, RuleOperators.Regex, "code", @"^PRJ-\d+")],
            [new RuleAction("label", @"{fnc.findInAscendants(brand)}/{fnc.regexExtract(code, (\d+))}"), new RuleAction("code", "x", OnlyIfEmpty: true)], IsActive: false));
        var brand = await FolderAsync("Brand");
        await PatchAsync(brand, new() { ["brand"] = [Text("ACME")] });
        var project = await FolderAsync("Project", brand);
        await PatchAsync(project, new() { ["code"] = [Text("PRJ-123-X")] });

        var test = await (await Admin.PostAsync($"/api/v1/rules/{rule.Id}/test/{project}", null, Ct)).Content.ReadFromJsonAsync<RuleTestDto>(Ct);

        test!.FiltersMatched.ShouldBeTrue();
        test.Values.ShouldBe(new Dictionary<string, string> { ["label"] = "ACME/123" });
        test.Problems.ShouldBeEmpty();
        Get(await MetadataAsync(project), "label").ShouldBeNull();
        (await Admin.PostAsync($"/api/v1/rules/{rule.Id}/test/{brand}", null, Ct)).Content.ReadFromJsonAsync<RuleTestDto>(Ct).Result!.FiltersMatched.ShouldBeFalse();
    }

    [Fact]
    public async Task Rule_jobs_apply_rules_afterwards_and_report_per_node()
    {
        var root = await FolderAsync("Run");
        var ok = await FolderAsync("ok", root);
        var bad = await FolderAsync("bad", root);
        var empty = await FolderAsync("empty", root);
        await PatchAsync(ok, new() { ["status"] = [Text("fine")] });
        await PatchAsync(bad, new() { ["status"] = [Text("bad")] });
        await RuleAsync(new RuleRequest("Label", [new RuleCondition(RuleConditionKinds.Filter, RuleOperators.HasValue, "status")], [new RuleAction("label", "{fnc.find(status)}!")]));
        await RuleAsync(new RuleRequest("Pages", [new RuleCondition(RuleConditionKinds.Filter, RuleOperators.EqualTo, "status", "bad")], [new RuleAction("pages", "abc")], SortIndex: 1));

        await RuleAsync(new RuleRequest("Live only", [new RuleCondition(RuleConditionKinds.Trigger, RuleOperators.ValueChanged, "status")], [new RuleAction("verdict", "changed"), new RuleAction("verdict", "not changed", OnMatch: false)]));
        var started = await (await Admin.PostAsJsonAsync("/api/v1/rule-jobs", new RuleJobRequest(FolderId: root), Ct)).Content.ReadFromJsonAsync<RuleJobDto>(Ct);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        RuleJobDto job;
        while ((job = (await Admin.GetFromJsonAsync<RuleJobDto>($"/api/v1/rule-jobs/{started!.Id}", Ct))!).State is RuleJobStates.Queued or RuleJobStates.Running)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the run did not finish in time");
            await Task.Delay(250, Ct);
        }

        job.State.ShouldBe(RuleJobStates.Completed, job.Error);
        (job.Total, job.Matched, job.Executed, job.Skipped, job.Failed).ShouldBe((3, 2, 1, 1, 1));
        job.Entries.Single(e => e.NodeId == bad).Message!.ShouldContain("pages");
        Get(await MetadataAsync(ok), "label").ShouldBe("fine!");
        Get(await MetadataAsync(empty), "label").ShouldBeNull();
        Get(await MetadataAsync(empty), "verdict").ShouldBeNull();
        Get(await MetadataAsync(bad), "label").ShouldBe("bad!");
        job.Entries.Single(e => e.NodeId == bad).Message!.ShouldContain("Written by Label");
        (await Admin.GetFromJsonAsync<RuleJobDto[]>("/api/v1/rule-jobs", Ct))!.Select(j => j.Id).ShouldContain(job.Id);
    }

    [Fact]
    public async Task Rules_are_checked_and_need_the_permission()
    {
        (await Admin.PostAsJsonAsync("/api/v1/rules", new RuleRequest("Bad", [new RuleCondition(RuleConditionKinds.Filter, RuleOperators.Regex, "code", "(")], [new RuleAction("label", "x")]), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/rules", new RuleRequest("None", [], []), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/rules", new RuleRequest("Odd", [new RuleCondition("sometimes", RuleOperators.HasValue, "code")], [new RuleAction("label", "x")]), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        foreach (var broken in new[] { "{var.yeer}", "{fnc.nope(code)}", "{fnc.regexExtract(code, ()}", "{fnc.find(code" })
        {
            (await Admin.PostAsJsonAsync("/api/v1/rules", new RuleRequest("Broken", [], [new RuleAction("label", broken)]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, broken);
        }

        foreach (var target in new[] { "title", "missing" })
        {
            (await Admin.PostAsJsonAsync("/api/v1/rules", new RuleRequest("Target", [], [new RuleAction(target, "x")]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, target);
        }

        var live = await RuleAsync(new RuleRequest("Live", [new RuleCondition(RuleConditionKinds.Trigger, RuleOperators.IsNewNode)], [new RuleAction("label", "x")], IsActive: false));
        (await Admin.PostAsJsonAsync("/api/v1/rule-jobs", new RuleJobRequest(NodeIds: [Guid.NewGuid()], RuleIds: [live.Id]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.DeleteAsync($"/api/v1/rules/{live.Id}", Ct)).EnsureSuccessStatusCode();

        var rule = await RuleAsync(new RuleRequest("Fine", [], [new RuleAction("label", "x")]));
        (await Admin.PutAsJsonAsync($"/api/v1/rules/{rule.Id}", new RuleRequest("Renamed", [], [new RuleAction("label", "y")], IsActive: false), Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<RuleDto[]>("/api/v1/rules", Ct))!.Single().ShouldSatisfyAllConditions(r => r.Name.ShouldBe("Renamed"), r => r.IsActive.ShouldBeFalse());

        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var member = api.As(user.Id, _setup.TenantId);
        (await member.GetAsync("/api/v1/rules", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync("/api/v1/rule-jobs", new RuleJobRequest(FolderId: Guid.NewGuid()), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await Admin.DeleteAsync($"/api/v1/rules/{rule.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin.GetFromJsonAsync<RuleDto[]>("/api/v1/rules", Ct))!.ShouldBeEmpty();
    }

    private async Task<RuleDto> RuleAsync(RuleRequest request)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/rules", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RuleDto>(Ct))!;
    }

    private async Task<Guid> FolderAsync(string name, Guid? parent = null)
    {
        parent ??= (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        var response = await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(parent.Value, name), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
    }

    private async Task PatchAsync(Guid node, Dictionary<string, List<MetadataValue>?> values) =>
        (await Admin.PatchAsJsonAsync($"/api/v1/nodes/{node}/metadata", values, Ct)).EnsureSuccessStatusCode();

    private async Task<NodeMetadataDto> MetadataAsync(Guid node) => (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{node}/metadata", Ct))!;

    private async Task EventuallyAsync(Guid node, Func<NodeMetadataDto, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        NodeMetadataDto metadata;
        while (!done(metadata = await MetadataAsync(node)))
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"the rules did not run in time: {JsonSerializer.Serialize(metadata.Properties)}");
            await Task.Delay(250, Ct);
        }
    }

    private static string? Get(NodeMetadataDto metadata, string key) =>
        metadata.Properties.TryGetValue(key, out var property) && property.InheritedFrom is null && property.Values.FirstOrDefault()?.V is { } value ? value.GetString() : null;

    private static MetadataValue Text(string text) => new(V: JsonSerializer.SerializeToElement(text));
}
