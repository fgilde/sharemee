using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;

namespace Shareme.Api.Tests;

public sealed class DataTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await api.SetupAsync();
        (await Admin.PostAsJsonAsync("/api/v1/metadata/languages", new DataLanguageDto("de", "Deutsch", false), Ct)).EnsureSuccessStatusCode();
        foreach (var request in new[]
        {
            new PropertyDefinitionRequest("status", "Status"),
            new PropertyDefinitionRequest("note", "Note"),
            new PropertyDefinitionRequest("tags", "Tags", ListMode: ListModes.Multi, AllowCustomValues: true),
            new PropertyDefinitionRequest("title", "Title", Localizable: true),
            new PropertyDefinitionRequest("pages", "Pages", PropertyValueTypes.Integer),
        })
        {
            (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", request, Ct)).EnsureSuccessStatusCode();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Mass_edits_overwrite_merge_and_delete_after_a_dry_run()
    {
        var root = await FolderAsync("Mass");
        var a = await FolderAsync("a", root);
        var b = await FolderAsync("b", root);
        foreach (var node in new[] { a, b })
        {
            await PatchAsync(node, new() { ["status"] = [Text("draft")], ["tags"] = [Text("one")], ["title"] = [Text("Title", "en")], ["note"] = [Text("old")] });
        }

        MassEditRequest Edit(bool dryRun) => new(
        [
            new PropertyMutation("status", MassEditStrategies.Overwrite, [Text("done")]),
            new PropertyMutation("tags", MassEditStrategies.Merge, [Text("one"), Text("two")]),
            new PropertyMutation("title", MassEditStrategies.Merge, [Text("Titel", "de")]),
            new PropertyMutation("note", MassEditStrategies.Delete),
            new PropertyMutation("pages", MassEditStrategies.Keep),
        ], NodeIds: [a, b], DryRun: dryRun);

        var dry = await RunAsync(await Admin.PostAsJsonAsync("/api/v1/mass-edits", Edit(true), Ct));
        (dry.DryRun, dry.Total, dry.Executed, dry.Failed).ShouldBe((true, 2, 2, 0));
        Values(await MetadataAsync(a), "status").ShouldBe(["draft"]);

        var run = await RunAsync(await Admin.PostAsJsonAsync("/api/v1/mass-edits", Edit(false), Ct));
        (run.Executed, run.Failed).ShouldBe((2, 0));
        var metadata = await MetadataAsync(b);
        Values(metadata, "status").ShouldBe(["done"]);
        Values(metadata, "tags").ShouldBe(["one", "two"]);
        Values(metadata, "title").ShouldBe(["en:Title", "de:Titel"]);
        metadata.Properties.ShouldNotContainKey("note");

        var again = await RunAsync(await Admin.PostAsJsonAsync("/api/v1/mass-edits", Edit(false), Ct));
        (again.Executed, again.Skipped).ShouldBe((0, 2));

        var german = await RunAsync(await Admin.PostAsJsonAsync("/api/v1/mass-edits", new MassEditRequest(
            [new PropertyMutation("title", MassEditStrategies.Overwrite, Texts: ["Neu"], Lang: "de")], NodeIds: [a]), Ct));
        german.Executed.ShouldBe(1);
        Values(await MetadataAsync(a), "title").ShouldBe(["en:Title", "de:Neu"]);
        (await Admin.PostAsJsonAsync("/api/v1/mass-edits", new MassEditRequest([new PropertyMutation("status", MassEditStrategies.Overwrite, Texts: ["x"], Lang: "de")], NodeIds: [a]), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.GetFromJsonAsync<DataJobDto[]>("/api/v1/data-jobs", Ct))!.Select(j => j.Id).ShouldContain(run.Id);
    }

    [Fact]
    public async Task Mass_edits_report_values_that_do_not_fit_and_need_the_permission()
    {
        var folder = await FolderAsync("Pages");
        var run = await RunAsync(await Admin.PostAsJsonAsync("/api/v1/mass-edits",
            new MassEditRequest([new PropertyMutation("pages", MassEditStrategies.Overwrite, [Text("many")])], NodeIds: [folder, Guid.NewGuid()]), Ct));
        (run.Failed, run.Skipped).ShouldBe((1, 1));
        run.Entries.Single(e => e.NodeId == folder).Column.ShouldBe("pages");

        var typed = await RunAsync(await Admin.PostAsJsonAsync("/api/v1/mass-edits",
            new MassEditRequest([new PropertyMutation("pages", MassEditStrategies.Overwrite, Texts: ["12"]), new PropertyMutation("tags", MassEditStrategies.Overwrite, Texts: ["a", "b"])], NodeIds: [folder]), Ct));
        typed.Executed.ShouldBe(1, JsonSerializer.Serialize(typed.Entries));
        (await MetadataAsync(folder)).Properties["pages"].Values.Single().V!.Value.GetInt64().ShouldBe(12);
        Values(await MetadataAsync(folder), "tags").ShouldBe(["a", "b"]);

        (await Admin.PostAsJsonAsync("/api/v1/mass-edits", new MassEditRequest([new PropertyMutation("nope", MassEditStrategies.Delete)], NodeIds: [folder]), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/mass-edits", new MassEditRequest([new PropertyMutation("status", MassEditStrategies.Delete)], NodeIds: Enumerable.Range(0, DataLimits.MaxNodes + 1).Select(_ => Guid.NewGuid()).ToList()), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var member = api.As(user.Id, _setup.TenantId);
        (await member.PostAsJsonAsync("/api/v1/mass-edits", new MassEditRequest([new PropertyMutation("status", MassEditStrategies.Delete)], NodeIds: [folder]), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.GetFromJsonAsync<DataJobDto[]>("/api/v1/data-jobs", Ct))!.ShouldBeEmpty();
        (await member.GetAsync($"/api/v1/data-jobs/{run.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Exports_have_a_column_per_property_and_language_and_import_back()
    {
        var root = await FolderAsync("Export");
        var a = await FolderAsync("a", root);
        await PatchAsync(a, new() { ["status"] = [Text("=1+1")], ["tags"] = [Text("one"), Text("two")], ["title"] = [Text("Title", "en"), Text("Titel", "de")] });

        var csv = await ExportAsync(new ExportRequest(FolderId: root, Format: ExportFormats.Csv));
        var lines = Encoding.UTF8.GetString(csv).TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var headers = lines[0].Split(',');
        headers[..4].ShouldBe(["id", "name", "type", "path"]);
        headers.ShouldContain("title[de]");
        headers.ShouldContain("title[en]");
        lines[1].ShouldContain("'=1+1");
        lines[1].ShouldContain("one; two");
        lines[1].ShouldContain("Export");

        using var workbook = new XLWorkbook(new MemoryStream(await ExportAsync(new ExportRequest(NodeIds: [a]))));
        var sheet = workbook.Worksheets.First();
        var column = sheet.Row(1).CellsUsed().Single(c => c.GetString() == "title[de]").Address.ColumnNumber;
        sheet.Cell(2, column).GetString().ShouldBe("Titel");
        sheet.Cell(2, column).SetValue("Neuer Titel");
        using var changed = new MemoryStream();
        workbook.SaveAs(changed);

        var run = await RunAsync(await Admin.PutAsync("/api/v1/imports", new ByteArrayContent(changed.ToArray()), Ct));
        (run.Executed, run.Failed).ShouldBe((1, 0), JsonSerializer.Serialize(run.Entries));
        var metadata = await MetadataAsync(a);
        Values(metadata, "title").ShouldBe(["en:Title", "de:Neuer Titel"], ignoreOrder: true);
        Values(metadata, "status").ShouldBe(["=1+1"]);
        Values(metadata, "tags").ShouldBe(["one", "two"]);

        var unchanged = await RunAsync(await Admin.PutAsync("/api/v1/imports", new ByteArrayContent(changed.ToArray()), Ct));
        (unchanged.Executed, unchanged.Skipped).ShouldBe((0, 1), JsonSerializer.Serialize(unchanged.Entries));
    }

    [Fact]
    public async Task Imports_match_file_names_in_the_folder_and_report_per_row_and_column()
    {
        var root = await FolderAsync("Import");
        var a = await FolderAsync("a", root);
        var b = await FolderAsync("b", root);
        var csv = $"name;id;status;title[de];pages;nope\r\na;;imported;;;x\r\n;{b};;Neu;;\r\nmissing;;x;;;\r\na;;;;many;\r\n";

        var dry = await RunAsync(await Admin.PutAsync($"/api/v1/imports?folderId={root}&dryRun=true", new StringContent(csv, Encoding.UTF8), Ct));
        (dry.Total, dry.Executed, dry.Failed).ShouldBe((4, 2, 2), JsonSerializer.Serialize(dry.Entries));
        dry.Entries.ShouldContain(e => e.Row == 1 && e.Column == "nope" && e.Result == DataJobResults.Skipped);
        dry.Entries.ShouldContain(e => e.Row == 4 && e.Result == DataJobResults.Failed && e.Message!.Contains("missing"));
        dry.Entries.ShouldContain(e => e.Row == 5 && e.Column == "pages" && e.Result == DataJobResults.Failed);
        (await MetadataAsync(a)).Properties.ShouldNotContainKey("status");

        var run = await RunAsync(await Admin.PutAsync($"/api/v1/imports?folderId={root}", new StringContent(csv, Encoding.UTF8), Ct));
        (run.Executed, run.Failed).ShouldBe((2, 2));
        Values(await MetadataAsync(a), "status").ShouldBe(["imported"]);
        Values(await MetadataAsync(b), "title").ShouldBe(["de:Neu"]);

        (await Admin.PutAsync("/api/v1/imports", new StringContent("status\r\nx\r\n", Encoding.UTF8), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PutAsync("/api/v1/imports", new ByteArrayContent([0x50, 0x4B, 1, 2, 3]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<DataJobDto> RunAsync(HttpResponseMessage started)
    {
        started.StatusCode.ShouldBe(HttpStatusCode.OK, await started.Content.ReadAsStringAsync(Ct));
        var id = (await started.Content.ReadFromJsonAsync<DataJobDto>(Ct))!.Id;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        DataJobDto job;
        while ((job = (await Admin.GetFromJsonAsync<DataJobDto>($"/api/v1/data-jobs/{id}", Ct))!).State is RuleJobStates.Queued or RuleJobStates.Running)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the run did not finish in time");
            await Task.Delay(250, Ct);
        }

        job.State.ShouldBe(RuleJobStates.Completed, job.Error);
        return job;
    }

    private async Task<byte[]> ExportAsync(ExportRequest request)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/exports", request, Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(Ct);
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

    private static List<string> Values(NodeMetadataDto metadata, string key) =>
        metadata.Properties[key].Values.Select(v => (v.Lang is null ? string.Empty : v.Lang + ":") + (v.List ?? v.V!.Value.ToString())).ToList();

    private static MetadataValue Text(string text, string? lang = null) => new(Lang: lang, V: JsonSerializer.SerializeToElement(text));
}
