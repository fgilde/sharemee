using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shareme.Contracts.Metadata;

public static class MetadataPermissions
{
    public const string GroupName = "Metadata";
    public const string Manage = "Metadata.Manage";
}

public static class PropertyValueTypes
{
    public const string String = "string";
    public const string Text = "text";
    public const string RichText = "richtext";
    public const string Url = "url";
    public const string Date = "date";
    public const string DateTime = "datetime";
    public const string Time = "time";
    public const string Integer = "integer";
    public const string Decimal = "decimal";
    public const string Boolean = "boolean";
    public const string Duration = "duration";
    public const string Reference = "reference";

    public static readonly IReadOnlyList<string> All = [String, Text, RichText, Url, Date, DateTime, Time, Integer, Decimal, Boolean, Duration, Reference];
}

public static class ListModes
{
    public const string None = "none";
    public const string Single = "single";
    public const string Multi = "multi";
}

public static class SearchModes
{
    public const string None = "none";
    public const string Filter = "filter";
    public const string Fulltext = "fulltext";
    public const string Both = "both";
}

public static class FillModes
{
    public const string Optional = "optional";
    public const string Recommended = "recommended";
    public const string Mandatory = "mandatory";
}

/// <summary>One stored value of a property: optional data language, optional user context, the typed value and the list value key.</summary>
public sealed record MetadataValue(
    [property: JsonPropertyName("lang")] string? Lang = null,
    [property: JsonPropertyName("ctx")] string? Ctx = null,
    [property: JsonPropertyName("v")] JsonElement? V = null,
    [property: JsonPropertyName("list")] string? List = null);

public sealed record PropertyValidation(bool Required = false, string? Regex = null, decimal? Min = null, decimal? Max = null);

public sealed record PropertyGroupDto(Guid Id, string Key, string Name, int SortIndex);

public sealed record PropertyGroupRequest(string Key, string Name, int SortIndex = 0);

public sealed record PropertyListValueDto(string Key, string Name, int SortIndex, bool IsActive);

public sealed record PropertyDefinitionDto(
    Guid Id,
    string Key,
    Guid? GroupId,
    string Name,
    string? Description,
    string ValueType,
    string ListMode,
    bool AllowCustomValues,
    bool MultiValue,
    bool Localizable,
    bool HasContext,
    bool Editable,
    bool IsSystem,
    string Searchable,
    bool Facet,
    bool IgnoreOnNewVersion,
    bool InheritToChildren,
    IReadOnlyList<string> NodeTypeKeys,
    PropertyValidation Validation,
    int SortIndex,
    IReadOnlyList<PropertyListValueDto> ListValues);

public sealed record PropertyDefinitionRequest(
    string Key,
    string Name,
    string ValueType = PropertyValueTypes.String,
    Guid? GroupId = null,
    string? Description = null,
    string ListMode = ListModes.None,
    bool AllowCustomValues = false,
    bool MultiValue = false,
    bool Localizable = false,
    bool HasContext = false,
    bool Editable = true,
    string Searchable = SearchModes.None,
    bool Facet = false,
    bool IgnoreOnNewVersion = false,
    bool InheritToChildren = false,
    IReadOnlyList<string>? NodeTypeKeys = null,
    PropertyValidation? Validation = null,
    int SortIndex = 0);

public sealed record DataLanguageDto(string Code, string Name, bool IsDefault);

public sealed record UserContextDto(string Key, string Name, bool IsDefault);

public sealed record PropertySetItem(string DefinitionKey, string FillMode = FillModes.Optional);

public sealed record PropertySetCondition(string DefinitionKey, string ListValueKey);

public sealed record PropertySetDto(Guid Id, string NodeTypeKey, string Name, bool IsDefault, IReadOnlyList<PropertySetItem> Items, IReadOnlyList<PropertySetCondition> Conditions);

public sealed record PropertySetRequest(string NodeTypeKey, string Name, bool IsDefault, IReadOnlyList<PropertySetItem> Items, IReadOnlyList<PropertySetCondition>? Conditions = null);

/// <summary>Values of one property as seen on a node; <see cref="InheritedFrom"/> is set when they come from an ancestor.</summary>
public sealed record EffectivePropertyDto(IReadOnlyList<MetadataValue> Values, Guid? InheritedFrom);

public sealed record NodeMetadataDto(Guid NodeId, string NodeType, IReadOnlyDictionary<string, EffectivePropertyDto> Properties);
