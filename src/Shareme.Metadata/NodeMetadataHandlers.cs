using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Graph;

namespace Shareme.Metadata;

public sealed record GetNodeMetadata(Guid Id) : IQuery<Result<NodeMetadataDto>>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Read;
}

/// <summary>Replace sets the whole own value set; otherwise only the given keys change and a null list removes a key.</summary>
public sealed record SetNodeMetadata(Guid Id, IReadOnlyDictionary<string, List<MetadataValue>?> Values, bool Replace) : ICommand<Result>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Update;
}

internal sealed class NodeMetadataHandlers(CoworkeeDbContext db, MetadataCatalog catalog)
    : IHandler<GetNodeMetadata, Result<NodeMetadataDto>>, IHandler<SetNodeMetadata, Result>
{
    public async Task<Result<NodeMetadataDto>> HandleAsync(GetNodeMetadata query, CancellationToken cancellationToken)
    {
        if (await db.Set<Node>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == query.Id, cancellationToken) is not { } node)
        {
            return Error.NotFound("nodes.not_found", "The node does not exist.");
        }

        var typeKey = await TypeKeyAsync(node, cancellationToken);
        var definitions = (await catalog.DefinitionsAsync(cancellationToken)).Where(d => d.AppliesTo(typeKey)).ToDictionary(d => d.Key);
        var properties = node.Metadata.Where(p => definitions.ContainsKey(p.Key) && p.Value.Count > 0)
            .ToDictionary(p => p.Key, p => new EffectivePropertyDto(p.Value, null));
        foreach (var (key, inherited) in await InheritedAsync(node, definitions.Values, cancellationToken))
        {
            properties.TryAdd(key, inherited);
        }

        return new NodeMetadataDto(node.Id, typeKey, properties);
    }

    public async Task<Result> HandleAsync(SetNodeMetadata command, CancellationToken cancellationToken)
    {
        if (await db.Set<Node>().SingleOrDefaultAsync(n => n.Id == command.Id, cancellationToken) is not { } node)
        {
            return Error.NotFound("nodes.not_found", "The node does not exist.");
        }

        var typeKey = await TypeKeyAsync(node, cancellationToken);
        var all = (await catalog.DefinitionsAsync(cancellationToken)).ToDictionary(d => d.Key);
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Fail(string key, string message) => (errors.TryGetValue(key, out var list) ? list : errors[key] = []).Add(message);

        // keys without a definition are dropped, values of read only properties are kept as they are
        var next = node.Metadata.Where(p => all.TryGetValue(p.Key, out var d) && (!command.Replace || !IsWritable(d))).ToDictionary(p => p.Key, p => p.Value);
        foreach (var (key, values) in command.Values)
        {
            if (!all.TryGetValue(key, out var definition) || !definition.AppliesTo(typeKey))
            {
                Fail(key, $"No property '{key}' applies to '{typeKey}'.");
                continue;
            }

            var current = node.Metadata.GetValueOrDefault(key) ?? [];
            if (!IsWritable(definition))
            {
                if (JsonSerializer.Serialize(values ?? []) != JsonSerializer.Serialize(current))
                {
                    Fail(key, "This property cannot be edited.");
                }

                continue;
            }

            if (values is null or { Count: 0 })
            {
                next.Remove(key);
            }
            else
            {
                next[key] = values;
            }
        }

        var languages = (await catalog.LanguagesAsync(cancellationToken)).Select(l => l.Code).ToHashSet();
        var contexts = (await catalog.ContextsAsync(cancellationToken)).Select(c => c.Key).ToHashSet();
        var references = new Dictionary<Guid, string>();
        foreach (var key in command.Values.Keys.Where(next.ContainsKey))
        {
            foreach (var message in Check(all[key], next[key], languages, contexts, references))
            {
                Fail(key, message);
            }
        }

        if (references.Count > 0)
        {
            var ids = references.Keys.ToList();
            var existing = await db.Set<Node>().Where(n => ids.Contains(n.Id)).Select(n => n.Id).ToListAsync(cancellationToken);
            foreach (var missing in ids.Except(existing))
            {
                Fail(references[missing], $"The referenced node '{missing}' does not exist.");
            }
        }

        var required = all.Values.Where(d => d.AppliesTo(typeKey) && d.Validation.Required && (command.Replace || command.Values.ContainsKey(d.Key)) && !next.ContainsKey(d.Key)).ToList();
        if (required.Count > 0)
        {
            var inherited = await InheritedAsync(node, required, cancellationToken);
            foreach (var definition in required.Where(d => !inherited.ContainsKey(d.Key)))
            {
                Fail(definition.Key, "A value is required.");
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
        }

        node.Metadata = next;
        node.MarkChanged("updated");
        return Result.Success();
    }

    private static bool IsWritable(PropertyDefinition definition) => definition.Editable && !definition.IsSystem;

    private async Task<string> TypeKeyAsync(Node node, CancellationToken cancellationToken) =>
        await db.Set<NodeType>().Where(t => t.Id == node.NodeTypeId).Select(t => t.Key).SingleAsync(cancellationToken);

    /// <summary>Values of the nearest ancestor for each inheriting definition.</summary>
    private async Task<Dictionary<string, EffectivePropertyDto>> InheritedAsync(Node node, IEnumerable<PropertyDefinition> definitions, CancellationToken cancellationToken)
    {
        var inheriting = definitions.Where(d => d.InheritToChildren).Select(d => d.Key).ToHashSet();
        var result = new Dictionary<string, EffectivePropertyDto>(StringComparer.Ordinal);
        var ancestors = node.PathIds.SkipLast(1).ToList();
        if (inheriting.Count == 0 || ancestors.Count == 0)
        {
            return result;
        }

        var metadata = await db.Set<Node>().AsNoTracking().Where(n => ancestors.Contains(n.Id)).Select(n => new { n.Id, n.Metadata }).ToDictionaryAsync(n => n.Id, n => n.Metadata, cancellationToken);
        foreach (var ancestor in Enumerable.Reverse(ancestors).Where(metadata.ContainsKey))
        {
            foreach (var (key, values) in metadata[ancestor].Where(p => inheriting.Contains(p.Key) && p.Value.Count > 0))
            {
                result.TryAdd(key, new EffectivePropertyDto(values, ancestor));
            }
        }

        return result;
    }

    private static IEnumerable<string> Check(PropertyDefinition definition, List<MetadataValue> values, HashSet<string> languages, HashSet<string> contexts, Dictionary<Guid, string> references)
    {
        if (definition.ListMode != ListModes.Multi && !definition.MultiValue && values.GroupBy(v => (v.Lang, v.Ctx)).Any(g => g.Count() > 1))
        {
            yield return "Only one value per language and context is allowed.";
        }

        foreach (var value in values)
        {
            if (definition.Localizable && value.Lang is null)
            {
                yield return "A data language is required.";
            }
            else if (definition.Localizable && !languages.Contains(value.Lang!))
            {
                yield return $"Unknown data language '{value.Lang}'.";
            }
            else if (!definition.Localizable && value.Lang is not null)
            {
                yield return "This property is not translatable.";
            }

            if (definition.HasContext && value.Ctx is null)
            {
                yield return "A user context is required.";
            }
            else if (definition.HasContext && !contexts.Contains(value.Ctx!))
            {
                yield return $"Unknown user context '{value.Ctx}'.";
            }
            else if (!definition.HasContext && value.Ctx is not null)
            {
                yield return "This property has no user context.";
            }

            if (definition.ListMode == ListModes.None && value.List is not null)
            {
                yield return "This property has no list values.";
            }
            else if (value.List is { } listKey)
            {
                if (!definition.ListValues.Any(v => v.Key == listKey && v.IsActive))
                {
                    yield return $"'{listKey}' is not an active list value.";
                }

                continue;
            }
            else if (definition.ListMode != ListModes.None && !definition.AllowCustomValues)
            {
                yield return "Choose one of the list values.";
                continue;
            }

            if (CheckValue(definition, value.V, references) is { } problem)
            {
                yield return problem;
            }
        }
    }

    private static string? CheckValue(PropertyDefinition definition, JsonElement? element, Dictionary<Guid, string> references)
    {
        if (element is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } v)
        {
            return "A value is required.";
        }

        var rule = definition.Validation;
        var text = v.ValueKind == JsonValueKind.String ? v.GetString()! : null;
        switch (definition.ValueType)
        {
            case PropertyValueTypes.String or PropertyValueTypes.Text or PropertyValueTypes.RichText or PropertyValueTypes.Url:
                if (text is null)
                {
                    return "Text is expected.";
                }

                if (definition.ValueType == PropertyValueTypes.Url && !(Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
                {
                    return "An absolute http or https address is expected.";
                }

                if (rule.Regex is { } pattern && !Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                {
                    return "The value does not match the required pattern.";
                }

                return OutOfRange(text.Length, rule, "characters");
            case PropertyValueTypes.Date:
                return text is not null && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? null : "A date (yyyy-MM-dd) is expected.";
            case PropertyValueTypes.DateTime:
                return text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ? null : "An ISO 8601 date and time is expected.";
            case PropertyValueTypes.Time:
                return text is not null && TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out _) ? null : "A time (HH:mm) is expected.";
            case PropertyValueTypes.Integer:
                return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var integer) ? OutOfRange(integer, rule) : "A whole number is expected.";
            case PropertyValueTypes.Decimal:
                return v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var number) ? OutOfRange(number, rule) : "A number is expected.";
            case PropertyValueTypes.Boolean:
                return v.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "true or false is expected.";
            case PropertyValueTypes.Duration:
                return text is not null && TryDuration(text) ? null : "An ISO 8601 duration (for example PT1H30M) is expected.";
            case PropertyValueTypes.Reference:
                if (text is null || !Guid.TryParse(text, out var id))
                {
                    return "A node id is expected.";
                }

                references[id] = definition.Key;
                return null;
            default:
                return "Unknown value type.";
        }
    }

    private static string? OutOfRange(decimal value, PropertyValidation rule, string unit = "") =>
        (rule.Min is { } min && value < min) || (rule.Max is { } max && value > max)
            ? $"Expected between {rule.Min?.ToString(CultureInfo.InvariantCulture) ?? "-"} and {rule.Max?.ToString(CultureInfo.InvariantCulture) ?? "-"} {unit}".TrimEnd() + "."
            : null;

    private static bool TryDuration(string text)
    {
        try
        {
            XmlConvert.ToTimeSpan(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
