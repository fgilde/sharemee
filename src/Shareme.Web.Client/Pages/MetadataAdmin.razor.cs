using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Metadata;
using Shareme.Web.Client.Metadata;

namespace Shareme.Web.Client.Pages;

public partial class MetadataAdmin
{
    [Inject] private IMetadataApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<PropertyDefinitionDto> _definitions = [];
    private IReadOnlyList<PropertyGroupDto> _groups = [];
    private IReadOnlyList<DataLanguageDto> _languages = [];
    private IReadOnlyList<UserContextDto> _contexts = [];
    private PropertyDefinitionDto? _editing;
    private DefinitionForm _form = new();
    private List<ListValueRow> _listValues = [];
    private string? _groupKey, _groupName, _languageCode, _languageName, _contextKey, _contextName;

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        _definitions = await Api.GetDefinitionsAsync();
        _groups = await Api.GetGroupsAsync();
        _languages = await Api.GetLanguagesAsync();
        _contexts = await Api.GetContextsAsync();
        if (_editing is not null)
        {
            _editing = _definitions.FirstOrDefault(d => d.Id == _editing.Id);
        }
    }

    private void Edit(PropertyDefinitionDto? definition)
    {
        _editing = definition;
        _form = definition is null ? new DefinitionForm() : DefinitionForm.From(definition);
        _listValues = definition?.ListValues.Select(v => new ListValueRow { Key = v.Key, Name = v.Name, IsActive = v.IsActive }).ToList() ?? [];
    }

    private Task SaveDefinitionAsync() => RunAsync(async () =>
    {
        var saved = await Api.SaveDefinitionAsync(_editing?.Id, _form.ToRequest());
        await ReloadAsync();
        Edit(_definitions.FirstOrDefault(d => d.Id == saved.Id));
    });

    private Task SaveListValuesAsync() => RunAsync(() =>
        Api.SetListValuesAsync(_editing!.Id, _listValues.Select((v, index) => new PropertyListValueDto(v.Key ?? string.Empty, v.Name ?? string.Empty, index, v.IsActive)).ToList()));

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            Snackbar.Add("Saved.", Severity.Success);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message, Severity.Error);
        }

        await ReloadAsync();
    }

    private static string Options(PropertyDefinitionDto d) => string.Join(", ", new[]
    {
        d.Validation.Required ? "required" : null,
        d.MultiValue ? "multiple" : null,
        d.Localizable ? "translatable" : null,
        d.HasContext ? "per context" : null,
        d.InheritToChildren ? "inherited" : null,
        d.IsSystem ? "system" : null,
        d.NodeTypeKeys.Count > 0 ? string.Join("/", d.NodeTypeKeys) : null,
    }.Where(o => o is not null));

    private sealed class ListValueRow
    {
        public string? Key { get; set; }

        public string? Name { get; set; }

        public bool IsActive { get; set; } = true;
    }

    private sealed class DefinitionForm
    {
        public string? Key { get; set; }

        public string? Name { get; set; }

        public string ValueType { get; set; } = PropertyValueTypes.String;

        public string ListMode { get; set; } = ListModes.None;

        public Guid? GroupId { get; set; }

        public List<string> NodeTypeKeys { get; set; } = [];

        public string? Regex { get; set; }

        public decimal? Min { get; set; }

        public decimal? Max { get; set; }

        public bool Required { get; set; }

        public bool MultiValue { get; set; }

        public bool Localizable { get; set; }

        public bool HasContext { get; set; }

        public bool InheritToChildren { get; set; }

        public bool AllowCustomValues { get; set; }

        public bool IgnoreOnNewVersion { get; set; }

        public string? Description { get; set; }

        public bool Editable { get; set; } = true;

        public string Searchable { get; set; } = SearchModes.None;

        public bool Facet { get; set; }

        public int SortIndex { get; set; }

        public static DefinitionForm From(PropertyDefinitionDto d) => new()
        {
            Key = d.Key, Name = d.Name, ValueType = d.ValueType, ListMode = d.ListMode, GroupId = d.GroupId, NodeTypeKeys = d.NodeTypeKeys.ToList(),
            Regex = d.Validation.Regex, Min = d.Validation.Min, Max = d.Validation.Max, Required = d.Validation.Required, MultiValue = d.MultiValue,
            Localizable = d.Localizable, HasContext = d.HasContext, InheritToChildren = d.InheritToChildren, AllowCustomValues = d.AllowCustomValues,
            IgnoreOnNewVersion = d.IgnoreOnNewVersion, Description = d.Description, Editable = d.Editable, Searchable = d.Searchable, Facet = d.Facet, SortIndex = d.SortIndex,
        };

        public PropertyDefinitionRequest ToRequest() => new(
            Key?.Trim() ?? string.Empty, Name?.Trim() ?? string.Empty, ValueType, GroupId, Description, ListMode, AllowCustomValues, MultiValue, Localizable, HasContext,
            Editable, Searchable, Facet, NodeTypeKeys: NodeTypeKeys, SortIndex: SortIndex, Validation: new PropertyValidation(Required, string.IsNullOrWhiteSpace(Regex) ? null : Regex, Min, Max), InheritToChildren: InheritToChildren,
            IgnoreOnNewVersion: IgnoreOnNewVersion);
    }
}
