using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Metadata;
using Shareme.Web.Client.Metadata;

namespace Shareme.Web.Client.Tests;

public sealed class MetadataEditorTests : BunitContext
{
    private readonly IMetadataApi _api = Substitute.For<IMetadataApi>();
    private readonly Guid _node = Guid.CreateVersion7();
    private readonly Guid _parent = Guid.CreateVersion7();

    public MetadataEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(NSubstitute.Substitute.For<Coworkee.Client.Blazor.Localization.ILocalizationApi>());
        Services.AddScoped<Coworkee.Client.Blazor.Localization.CoworkeeLocalizer>();
        Services.AddSingleton(_api);
        _api.GetDefinitionsAsync("folder", Arg.Any<CancellationToken>()).Returns(
        [
            Definition("note", "Note"),
            Definition("client", "Client") with { InheritToChildren = true },
        ]);
        _api.GetGroupsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _api.GetLanguagesAsync(Arg.Any<CancellationToken>()).Returns([new DataLanguageDto("en", "English", true)]);
        _api.GetContextsAsync(Arg.Any<CancellationToken>()).Returns([new UserContextDto("default", "Default", true)]);
        _api.GetSetsAsync("folder", Arg.Any<CancellationToken>()).Returns([]);
        _api.GetNodeMetadataAsync(_node, Arg.Any<CancellationToken>()).Returns(new NodeMetadataDto(_node, "folder", new Dictionary<string, EffectivePropertyDto>
        {
            ["note"] = new([new MetadataValue(V: JsonSerializer.SerializeToElement("hello"))], null),
            ["client"] = new([new MetadataValue(V: JsonSerializer.SerializeToElement("Acme"))], _parent),
        }));
    }

    [Fact]
    public void Shows_own_values_and_inherited_ones_as_hint()
    {
        var editor = Render<MetadataEditor>(p => p.Add(e => e.NodeId, _node));

        editor.WaitForAssertion(() => editor.Find("[data-property='note'] input").GetAttribute("value").ShouldBe("hello"));
        editor.Find("[data-property='client']").TextContent.ShouldContain("Inherited: Acme");
    }

    [Fact]
    public async Task Saving_patches_the_edited_values()
    {
        var editor = Render<MetadataEditor>(p => p.Add(e => e.NodeId, _node));
        editor.WaitForElement("[data-property='note'] input").Change("changed");

        await editor.Find("[data-testid='save-metadata']").ClickAsync(new());

        await _api.Received(1).SetNodeMetadataAsync(_node, Arg.Is<IReadOnlyDictionary<string, List<MetadataValue>?>>(values =>
            values["note"]!.Single().V!.Value.GetString() == "changed" && !values.ContainsKey("client")), Arg.Any<CancellationToken>());
    }

    private static PropertyDefinitionDto Definition(string key, string name) => new(
        Guid.CreateVersion7(), key, null, name, null, PropertyValueTypes.String, ListModes.None, false, false, false, false, true, false, SearchModes.None,
        false, false, false, [], new PropertyValidation(), 0, []);
}
