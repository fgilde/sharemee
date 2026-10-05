using System.Globalization;
using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Metadata;

namespace Shareme.Web.Client.Metadata;

public partial class MetadataEditor
{
    [Inject] private IMetadataApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<Field>? _fields;
    private IReadOnlyDictionary<string, string[]> _errors = new Dictionary<string, string[]>();
    private Dictionary<string, string> _saved = [];

    [Parameter, EditorRequired] public Guid NodeId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        _fields = null;
        _errors = new Dictionary<string, string[]>();
        try
        {
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            _fields = [];
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task LoadAsync()
    {
        var metadata = await Api.GetNodeMetadataAsync(NodeId);
        var definitions = await Api.GetDefinitionsAsync(metadata.NodeType);
        var groups = (await Api.GetGroupsAsync()).ToDictionary(g => g.Id);
        var languages = await Api.GetLanguagesAsync();
        var contexts = await Api.GetContextsAsync();
        var set = Choose(await Api.GetSetsAsync(metadata.NodeType), metadata);
        var fillModes = set?.Items.GroupBy(i => i.DefinitionKey).ToDictionary(g => g.Key, g => g.First().FillMode);

        _fields = [];
        foreach (var definition in definitions.Where(d => fillModes is null || fillModes.ContainsKey(d.Key)))
        {
            var group = definition.GroupId is { } groupId && groups.TryGetValue(groupId, out var g) ? g : new PropertyGroupDto(Guid.Empty, string.Empty, string.Empty, -1);
            var property = metadata.Properties.GetValueOrDefault(definition.Key);
            var mandatory = definition.Validation.Required || fillModes?.GetValueOrDefault(definition.Key) == FillModes.Mandatory;
            foreach (var lang in definition.Localizable ? languages.Select(l => (string?)l.Code) : [null])
            {
                foreach (var ctx in definition.HasContext ? contexts.Select(c => (string?)c.Key) : [null])
                {
                    var own = property is { InheritedFrom: null } ? property.Values.Where(v => v.Lang == lang && v.Ctx == ctx).ToList() : [];
                    var inherited = property is { InheritedFrom: not null } ? property.Values.Where(v => v.Lang == lang && v.Ctx == ctx).ToList() : [];
                    var field = new Field(definition, group, lang, ctx, mandatory, !definition.Editable || definition.IsSystem)
                    {
                        Helper = inherited.Count > 0 ? "Inherited: " + string.Join(", ", inherited.Select(v => Display(definition, v))) : definition.Description,
                    };
                    Load(field, own);
                    _fields.Add(field);
                }
            }
        }

        _saved = Snapshot();
    }

    /// <summary>The editable values per key as they would be sent, to send only what the user changed.</summary>
    private Dictionary<string, string> Snapshot() =>
        _fields!.Where(f => !f.ReadOnly).GroupBy(f => f.Definition.Key).ToDictionary(g => g.Key, g => JsonSerializer.Serialize(g.SelectMany(ToValues).ToList()));

    private async Task SaveAsync()
    {
        var current = Snapshot();
        var values = _fields!.Where(f => !f.ReadOnly && current[f.Definition.Key] != _saved.GetValueOrDefault(f.Definition.Key)).GroupBy(f => f.Definition.Key)
            .ToDictionary(g => g.Key, g => (List<MetadataValue>?)g.SelectMany(ToValues).ToList());
        try
        {
            if (values.Count > 0)
            {
                await Api.SetNodeMetadataAsync(NodeId, values);
            }

            _saved = current;
            _errors = new Dictionary<string, string[]>();
            Snackbar.Add("Metadata saved.", Severity.Success);
        }
        catch (ApiException exception) when (exception.Errors is { } errors)
        {
            _errors = errors;
        }
        catch (ApiException exception) when (exception.Status == 403)
        {
            Snackbar.Add("You may not edit this.", Severity.Warning);
        }
        catch (ApiException exception) when (exception.Status == 409)
        {
            Snackbar.Add("Someone else changed this meanwhile. Reload and try again.", Severity.Warning);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private static PropertySetDto? Choose(IReadOnlyList<PropertySetDto> sets, NodeMetadataDto metadata) =>
        sets.FirstOrDefault(s => s.Conditions.Count > 0 && s.Conditions.All(c =>
            metadata.Properties.TryGetValue(c.DefinitionKey, out var p) && p.Values.Any(v => v.List == c.ListValueKey)))
        ?? sets.FirstOrDefault(s => s.IsDefault && s.Conditions.Count == 0);

    private static string Editor(PropertyDefinitionDto definition) => definition.ListMode != ListModes.None ? "list" : definition.ValueType switch
    {
        PropertyValueTypes.Boolean => "boolean",
        PropertyValueTypes.Integer or PropertyValueTypes.Decimal => "number",
        PropertyValueTypes.Date => "date",
        _ => "text",
    };

    private static string? Placeholder(string valueType) => valueType switch
    {
        PropertyValueTypes.DateTime => "2026-10-02T14:30:00Z",
        PropertyValueTypes.Time => "14:30",
        PropertyValueTypes.Duration => "PT1H30M",
        PropertyValueTypes.Url => "https://",
        PropertyValueTypes.Reference => "node id",
        _ => null,
    };

    private static void Load(Field field, List<MetadataValue> values)
    {
        field.ListKeys = values.Where(v => v.List is not null).Select(v => v.List!).ToList();
        var value = values.FirstOrDefault(v => v.List is null)?.V;
        if (value is not { } v)
        {
            return;
        }

        switch (v.ValueKind)
        {
            case JsonValueKind.True or JsonValueKind.False:
                field.Bool = v.GetBoolean();
                break;
            case JsonValueKind.Number:
                field.Number = v.GetDecimal();
                break;
            case JsonValueKind.String when field.Definition.ValueType == PropertyValueTypes.Date:
                field.Date = DateTime.TryParseExact(v.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
                break;
            case JsonValueKind.String:
                field.Text = v.GetString();
                break;
        }
    }

    private static IEnumerable<MetadataValue> ToValues(Field field)
    {
        foreach (var key in field.ListKeys)
        {
            yield return new MetadataValue(field.Lang, field.Ctx, null, key);
        }

        JsonElement? value = Editor(field.Definition) switch
        {
            "boolean" when field.Bool is { } b => JsonSerializer.SerializeToElement(b),
            "number" when field.Number is { } n => field.Definition.ValueType == PropertyValueTypes.Integer
                ? JsonSerializer.SerializeToElement(decimal.ToInt64(decimal.Truncate(n)))
                : JsonSerializer.SerializeToElement(n),
            "date" when field.Date is { } d => JsonSerializer.SerializeToElement(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            "text" or "list" when !string.IsNullOrWhiteSpace(field.Text) => JsonSerializer.SerializeToElement(field.Text.Trim()),
            _ => null,
        };
        if (value is not null)
        {
            yield return new MetadataValue(field.Lang, field.Ctx, value);
        }
    }

    private static string Display(PropertyDefinitionDto definition, MetadataValue value) =>
        value.List is { } key ? definition.ListValues.FirstOrDefault(v => v.Key == key)?.Name ?? key : value.V?.ToString() ?? string.Empty;

    private sealed class Field(PropertyDefinitionDto definition, PropertyGroupDto group, string? lang, string? ctx, bool mandatory, bool readOnly)
    {
        public PropertyDefinitionDto Definition { get; } = definition;

        public PropertyGroupDto Group { get; } = group;

        public string? Lang { get; } = lang;

        public string? Ctx { get; } = ctx;

        public bool Mandatory { get; } = mandatory;

        public bool ReadOnly { get; } = readOnly;

        public string Label => Definition.Name + (Lang is null ? string.Empty : $" ({Lang})") + (Ctx is null ? string.Empty : $" [{Ctx}]");

        public string? Helper { get; init; }

        public string? Text { get; set; }

        public bool? Bool { get; set; }

        public decimal? Number { get; set; }

        public DateTime? Date { get; set; }

        public List<string> ListKeys { get; set; } = [];
    }
}
