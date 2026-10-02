using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;

namespace Shareme.Api.Tests;

public sealed class AiTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _folder;

    public async ValueTask InitializeAsync()
    {
        _setup = await api.SetupAsync();
        var subject = await DefineAsync(new PropertyDefinitionRequest("subject", "Subject", ListMode: ListModes.Multi));
        (await Admin.PutAsJsonAsync($"/api/v1/metadata/definitions/{subject.Id}/list-values",
            new[] { new PropertyListValueDto("beach", "Beach", 0, true), new PropertyListValueDto("sand", "Sand", 1, true), new PropertyListValueDto("forest", "Forest", 2, true) }, Ct)).EnsureSuccessStatusCode();
        await DefineAsync(new PropertyDefinitionRequest("keywords", "Keywords", MultiValue: true));
        await DefineAsync(new PropertyDefinitionRequest("caption", "Caption", Localizable: true));
        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        _folder = (await (await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(root, "Photos"), Ct)).Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Tags_become_suggestions_mapped_to_list_values_and_are_applied_after_review()
    {
        var definition = await SaveAsync(new ExtractionDefinitionRequest("Subjects", AiProviders.OpenAi, "subject", Endpoint: "https://openai.test/v1", Model: "vision-test", ApiKey: "sk-secret"));
        var listed = await Admin.GetStringAsync("/api/v1/extractions", Ct);
        listed.ShouldNotContain("sk-secret");
        JsonSerializer.Deserialize<ExtractionDefinitionDto[]>(listed, JsonSerializerOptions.Web)!.Single().HasApiKey.ShouldBeTrue();
        var asset = await ImageAsync("beach.png");

        var run = await RunAsync(definition.Id, asset);
        (run.Executed, run.Failed).ShouldBe((1, 0), JsonSerializer.Serialize(run.Entries));
        var request = FakeAi.Instance.Requests.Last(r => r.Request.RequestUri!.Host == "openai.test");
        request.Request.Headers.Authorization!.ToString().ShouldBe("Bearer sk-secret");
        request.Body.ShouldContain("data:image/");
        request.Body.ShouldContain("beach, sand, forest");

        var suggestion = (await Admin.GetFromJsonAsync<ExtractionSuggestionDto[]>("/api/v1/extraction-suggestions", Ct))!.Single();
        suggestion.Values.Select(v => v.List).ShouldBe(["beach", "sand"]);
        suggestion.Findings.Select(f => f.Value).ShouldBe(["Beach", "Sand"]);
        (await Metadata(asset)).Properties.ShouldNotContainKey("subject");

        (await Admin.PostAsync($"/api/v1/extraction-suggestions/{suggestion.Id}/apply", null, Ct)).EnsureSuccessStatusCode();
        (await Metadata(asset)).Properties["subject"].Values.Select(v => v.List).ShouldBe(["beach", "sand"]);
        (await Admin.GetFromJsonAsync<ExtractionSuggestionDto[]>("/api/v1/extraction-suggestions", Ct))!.ShouldBeEmpty();
        (await Admin.PostAsync($"/api/v1/extraction-suggestions/{suggestion.Id}/reject", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Descriptions_are_written_right_away_in_the_data_language()
    {
        var definition = await SaveAsync(new ExtractionDefinitionRequest("Captions", AiProviders.Anthropic, "caption", AiModes.Description, Endpoint: "https://anthropic.test", ApiKey: "ant-key", AutoApply: true));
        var asset = await ImageAsync("caption.png");

        var run = await RunAsync(definition.Id, asset);
        run.Executed.ShouldBe(1, JsonSerializer.Serialize(run.Entries));
        var value = (await Metadata(asset)).Properties["caption"].Values.Single();
        (value.Lang, value.V!.Value.GetString()).ShouldBe(("en", "A sunny beach."));
        var request = FakeAi.Instance.Requests.Last(r => r.Request.RequestUri!.Host == "anthropic.test");
        request.Request.Headers.GetValues("x-api-key").Single().ShouldBe("ant-key");
        request.Body.ShouldContain(AiExtractorModel);
    }

    [Fact]
    public async Task Rules_run_extractions_on_new_files_and_provider_errors_are_reported()
    {
        var vision = await SaveAsync(new ExtractionDefinitionRequest("Vision", AiProviders.AzureVision, "keywords", Endpoint: "https://vision.test", ApiKey: "azure-key", AutoApply: true));
        (await Admin.PostAsJsonAsync("/api/v1/rules", new RuleRequest("Tag new files",
            [new RuleCondition(RuleConditionKinds.Trigger, RuleOperators.IsNewNode)], [new RuleAction(string.Empty, string.Empty, ExtractionId: vision.Id)], NodeTypeKeys: [NodeTypeKeys.Asset]), Ct))
            .EnsureSuccessStatusCode();

        // not rendered yet: the run continues later for it
        var asset = await ImageAsync("rule.png", rendered: false);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!(await Metadata(asset)).Properties.TryGetValue("keywords", out var keywords) || keywords.Values.Single().V!.Value.GetString() != "beach")
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the rule did not tag the file in time");
            await Task.Delay(500, Ct);
        }

        FakeAi.Instance.Requests.Last(r => r.Request.RequestUri!.Host == "vision.test").Request.RequestUri!.Query.ShouldContain("features=tags");

        var broken = await SaveAsync(new ExtractionDefinitionRequest("Broken", AiProviders.OpenAi, "keywords", Endpoint: "https://unknown.test/v1", Model: "x"));
        var failed = await RunAsync(broken.Id, asset);
        failed.Failed.ShouldBe(1);
        failed.Entries.Single().Message!.ShouldContain("401");
    }

    [Fact]
    public async Task Definitions_are_checked_and_need_the_permissions()
    {
        foreach (var request in new[]
        {
            new ExtractionDefinitionRequest("Plain", AiProviders.OpenAi, "keywords", Endpoint: "http://openai.test/v1", Model: "x"),
            new ExtractionDefinitionRequest("No endpoint", AiProviders.AzureVision, "keywords"),
            new ExtractionDefinitionRequest("No model", AiProviders.OpenAi, "keywords"),
            new ExtractionDefinitionRequest("Target", AiProviders.Anthropic, "missing"),
            new ExtractionDefinitionRequest("Threshold", AiProviders.Anthropic, "keywords", MinConfidence: 2),
        })
        {
            (await Admin.PostAsJsonAsync("/api/v1/extractions", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, request.Name);
        }

        var definition = await SaveAsync(new ExtractionDefinitionRequest("Kept", AiProviders.Anthropic, "keywords", ApiKey: "first"));
        (await Admin.PutAsJsonAsync($"/api/v1/extractions/{definition.Id}", new ExtractionDefinitionRequest("Renamed", AiProviders.Anthropic, "keywords"), Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<ExtractionDefinitionDto[]>("/api/v1/extractions", Ct))!.Single().ShouldSatisfyAllConditions(d => d.Name.ShouldBe("Renamed"), d => d.HasApiKey.ShouldBeTrue());

        // the key does not follow the definition to another address
        (await Admin.PutAsJsonAsync($"/api/v1/extractions/{definition.Id}", new ExtractionDefinitionRequest("Moved", AiProviders.Anthropic, "keywords", Endpoint: "https://elsewhere.test"), Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<ExtractionDefinitionDto[]>("/api/v1/extractions", Ct))!.Single().HasApiKey.ShouldBeFalse();

        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var member = api.As(user.Id, _setup.TenantId);
        (await member.GetAsync("/api/v1/extractions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync("/api/v1/extraction-runs", new ExtractionRunRequest(definition.Id, [Guid.NewGuid()]), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.DeleteAsync($"/api/v1/extractions/{definition.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private const string AiExtractorModel = "claude-opus-5-5";

    private async Task<DataJobDto> RunAsync(Guid definition, Guid asset)
    {
        var started = await Admin.PostAsJsonAsync("/api/v1/extraction-runs", new ExtractionRunRequest(definition, [asset]), Ct);
        started.StatusCode.ShouldBe(HttpStatusCode.OK, await started.Content.ReadAsStringAsync(Ct));
        var id = (await started.Content.ReadFromJsonAsync<DataJobDto>(Ct))!.Id;
        var deadline = DateTime.UtcNow.AddSeconds(90);
        DataJobDto job;
        while ((job = (await Admin.GetFromJsonAsync<DataJobDto>($"/api/v1/data-jobs/{id}", Ct))!).State is RuleJobStates.Queued or RuleJobStates.Running)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the extraction did not finish in time");
            await Task.Delay(250, Ct);
        }

        job.State.ShouldBe(RuleJobStates.Completed, job.Error);
        return job;
    }

    private async Task<ExtractionDefinitionDto> SaveAsync(ExtractionDefinitionRequest request)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/extractions", request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ExtractionDefinitionDto>(Ct))!;
    }

    private async Task<PropertyDefinitionDto> DefineAsync(PropertyDefinitionRequest request)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PropertyDefinitionDto>(Ct))!;
    }

    private async Task<Guid> ImageAsync(string name, bool rendered = true)
    {
        using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SandyBrown, 64, 48);
        var id = (await WorkspaceTests.UploadAsync(Admin, name, image.ToByteArray(ImageMagick.MagickFormat.Png), _folder)).AssetId;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (rendered && !(await Admin.GetFromJsonAsync<Shareme.Contracts.Assets.AssetDto>($"/api/v1/assets/{id}", Ct))!.Derivations.Any(d => d.Kind == "preview" && d.State == "ready"))
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the preview was not rendered in time");
            await Task.Delay(250, Ct);
        }

        return id;
    }

    private async Task<NodeMetadataDto> Metadata(Guid node) => (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{node}/metadata", Ct))!;
}
