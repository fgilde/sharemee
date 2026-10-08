using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Automation;
using Shareme.Web.Client.Metadata;
using Shareme.Web.Client.Pages;

namespace Shareme.Web.Client.Tests;

public sealed class RulesAdminTests : BunitContext
{
    private readonly IRulesApi _api = Substitute.For<IRulesApi>();
    private readonly RuleDto _rule = new(Guid.CreateVersion7(), "Label", true, [],
        [new RuleCondition(RuleConditionKinds.Trigger, RuleOperators.ValueChanged, "status")], [new RuleAction("label", "{fnc.find(status)}")], 0);

    public RulesAdminTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(NSubstitute.Substitute.For<Coworkee.Client.Blazor.Localization.ILocalizationApi>());
        Services.AddScoped<Coworkee.Client.Blazor.Localization.CoworkeeLocalizer>();
        MudBlazor.Extensions.ServiceCollectionExtensions.AddMudExtensions(Services);
        Services.AddSingleton(_api);
        Services.AddSingleton(Substitute.For<IAiApi>());
        AddAuthorization().SetAuthorized("Ada");
        _api.GetAsync(default).ReturnsForAnyArgs([_rule]);
        _api.GetJobsAsync(default).ReturnsForAnyArgs([new RuleJobDto(Guid.CreateVersion7(), RuleJobStates.Completed, 3, 2, 1, 1, 1, null, [], DateTimeOffset.UtcNow)]);
    }

    [Fact]
    public async Task Lists_rules_and_runs_and_saves_an_edited_rule()
    {
        var page = Render<RulesAdmin>();

        page.WaitForAssertion(() => page.Find("[data-rule='Label']").TextContent.ShouldContain("trigger: status valueChanged"));
        page.Find("[data-testid='jobs'] tbody tr").TextContent.ShouldContain("completed");

        await page.Find("[data-rule='Label'] button[aria-label='Edit']").ClickAsync(new());
        page.FindAll("[data-testid='condition']").Count.ShouldBe(1);
        page.Find("input[data-testid='rule-name'], [data-testid='rule-name'] input").Change("Renamed");
        await page.Find("[data-testid='save-rule']").ClickAsync(new());

        await _api.Received().SaveAsync(_rule.Id, Arg.Is<RuleRequest>(r => r.Name == "Renamed" && r.Conditions.Single().PropertyKey == "status" && r.Actions.Single().Template == "{fnc.find(status)}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_dry_run_shows_what_would_be_written()
    {
        var node = Guid.CreateVersion7();
        _api.TestAsync(_rule.Id, node, Arg.Any<CancellationToken>()).Returns(new RuleTestDto(true, false, new Dictionary<string, string> { ["label"] = "draft" }, []));
        var page = Render<RulesAdmin>();
        page.WaitForAssertion(() => page.Find("[data-rule='Label']"));

        await page.Find("[data-rule='Label'] button[aria-label='Edit']").ClickAsync(new());
        page.Find("input[data-testid='test-node'], [data-testid='test-node'] input").Change(node.ToString());
        await page.Find("[data-testid='test-rule']").ClickAsync(new());

        page.WaitForAssertion(() => page.Find("[data-testid='test-result']").TextContent.ShouldContain("else"));
        page.Find("[data-testid='test-result']").TextContent.ShouldContain("label = draft");
    }
}
