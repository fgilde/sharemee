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

/// <summary>Replace sets the whole own value set; otherwise only the given keys change and a null list removes a key. <paramref name="Source"/> tells listeners who changed it (rules do not react to their own writes); <paramref name="ValidateOnly"/> only checks, for dry runs.</summary>
public sealed record SetNodeMetadata(Guid Id, IReadOnlyDictionary<string, List<MetadataValue>?> Values, bool Replace, string Source = MetadataSources.User, bool ValidateOnly = false) : ICommand<Result>, IResourceRequest
{
    public string ResourceType => NodePermissions.ResourceType;

    public Guid ResourceId => Id;

    public string RequiredPermission => NodePermissions.Update;
}

public static class MetadataSources
{
    public const string User = "user";
    public const string Rule = "rule";
    public const string Import = "import";
    public const string MassEdit = "massEdit";
    public const string Ai = "ai";
}

/// <summary>Own values of a node changed; <see cref="Keys"/> are the properties whose values differ now.</summary>
public sealed record NodeMetadataChanged(Guid NodeId, IReadOnlyList<string> Keys, string Source) : Coworkee.Domain.IDomainEvent;

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
        var languages = (await catalog.LanguagesAsync(cancellationToken)).Select(l => l.Code).ToHashSet();
        var contexts = (await catalog.ContextsAsync(cancellationToken)).Select(c => c.Key).ToHashSet();
        var references = new Dictionary<Guid, string>();
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
                continue;
            }

            if (values.Any(v => v is null))
            {
                Fail(key, "Empty entries are not allowed.");
                continue;
            }

            var normalized = new List<MetadataValue>();
            foreach (var message in Check(definition, values, current, languages, contexts, references, normalized))
            {
                Fail(key, message);
            }

            next[key] = normalized;
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

        // read only values are written by the system (P1c mirroring), so Required does not apply to them here
        var required = all.Values.Where(d => d.AppliesTo(typeKey) && IsWritable(d) && d.Validation.Required && (command.Replace || command.Values.ContainsKey(d.Key)) && !next.ContainsKey(d.Key)).ToList();
        if (required.Count > 0)
        {
            var inherited = await InheritedAsync(node, required, cancellationToken);
            foreach (var definition in required.Where(d => !inherited.ContainsKey(d.Key)))
            {
                Fail(definition.Key, "A value is required.");
            }
        }

        var serialized = JsonSerializer.Serialize(next);
        if (serialized.Length > MaxMetadataLength)
        {
            Fail("Metadata", $"All values together may take up to {MaxMetadataLength / 1024} KB.");
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
        }

        if (command.ValidateOnly || serialized == JsonSerializer.Serialize(node.Metadata))
        {
            return Result.Success();
        }

        var changed = next.Keys.Union(node.Metadata.Keys)
            .Where(k => JsonSerializer.Serialize(next.GetValueOrDefault(k)) != JsonSerializer.Serialize(node.Metadata.GetValueOrDefault(k))).ToList();
        node.Metadata = next;
        node.MarkChanged("metadata changed");
        node.Publish(new NodeMetadataChanged(node.Id, changed, command.Source));
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

    private const int MaxMetadataLength = 1024 * 1024;

    /// <summary>Validates the values of one property and adds their canonical form to <paramref name="normalized"/>.</summary>
    private static IEnumerable<string> Check(
        PropertyDefinition definition, List<MetadataValue> values, List<MetadataValue> current, HashSet<string> languages, HashSet<string> contexts,
        Dictionary<Guid, string> references, List<MetadataValue> normalized)
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
                continue;
            }

            if (value.List is { } listKey)
            {
                if (value.V is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
                {
                    yield return "A list value carries no own value.";
                }
                else if (!definition.ListValues.Any(v => v.Key == listKey && v.IsActive) && !current.Any(c => c.List == listKey && c.Lang == value.Lang && c.Ctx == value.Ctx))
                {
                    // deactivated values stop new assignments but stay valid where they are already used
                    yield return $"'{listKey}' is not an active list value.";
                }

                normalized.Add(value with { V = null });
                continue;
            }

            if (definition.ListMode != ListModes.None && !definition.AllowCustomValues)
            {
                yield return "Choose one of the list values.";
                continue;
            }

            var (problem, canonical) = CheckValue(definition, value.V, references);
            if (problem is not null)
            {
                yield return problem;
                continue;
            }

            normalized.Add(value with { V = canonical });
        }
    }

    /// <summary>Returns an error or the value in the form it is stored and searched in.</summary>
    private static (string? Problem, JsonElement? Value) CheckValue(PropertyDefinition definition, JsonElement? element, Dictionary<Guid, string> references)
    {
        if (element is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } v)
        {
            return ("A value is required.", null);
        }

        var rule = definition.Validation;
        var text = v.ValueKind == JsonValueKind.String ? v.GetString()! : null;
        switch (definition.ValueType)
        {
            case PropertyValueTypes.String or PropertyValueTypes.Text or PropertyValueTypes.RichText or PropertyValueTypes.Url:
                if (text is null)
                {
                    return ("Text is expected.", null);
                }

                var limit = definition.ValueType switch
                {
                    PropertyValueTypes.String => 1_000,
                    PropertyValueTypes.Url => 2_048,
                    PropertyValueTypes.Text => 64_000,
                    _ => 512_000,
                };
                if (text.Length > limit)
                {
                    return ($"Up to {limit} characters are allowed.", null);
                }

                if (definition.ValueType == PropertyValueTypes.Url && !(Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
                {
                    return ("An absolute http or https address is expected.", null);
                }

                if (rule.Regex is { } pattern && !Matches(text, pattern))
                {
                    return ("The value does not match the required pattern.", null);
                }

                return (OutOfRange(text.Length, rule, "characters"), v);
            case PropertyValueTypes.Date:
                return text is not null && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? (null, v)
                    : ("A date (yyyy-MM-dd) is expected.", null);
            case PropertyValueTypes.DateTime:
                return text is not null && DateTimeOffset.TryParseExact(text, ["yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "yyyy-MM-ddTHH:mmK"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
                    ? (null, JsonSerializer.SerializeToElement(moment.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)))
                    : ("An ISO 8601 date and time with offset is expected.", null);
            case PropertyValueTypes.Time:
                return text is not null && TimeOnly.TryParseExact(text, ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                    ? (null, JsonSerializer.SerializeToElement(time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)))
                    : ("A time (HH:mm) is expected.", null);
            case PropertyValueTypes.Integer:
                return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var integer) ? (OutOfRange(integer, rule), v) : ("A whole number is expected.", null);
            case PropertyValueTypes.Decimal:
                return v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var number) ? (OutOfRange(number, rule), v) : ("A number is expected.", null);
            case PropertyValueTypes.Boolean:
                return v.ValueKind is JsonValueKind.True or JsonValueKind.False ? (null, v) : ("true or false is expected.", null);
            case PropertyValueTypes.Duration:
                return text is not null && TryDuration(text, out var duration)
                    ? (null, JsonSerializer.SerializeToElement(XmlConvert.ToString(duration)))
                    : ("An ISO 8601 duration of days, hours, minutes or seconds (for example PT1H30M) is expected.", null);
            case PropertyValueTypes.Reference:
                if (text is null || !Guid.TryParse(text, out var id))
                {
                    return ("A node id is expected.", null);
                }

                references[id] = definition.Key;
                return (null, JsonSerializer.SerializeToElement(id.ToString()));
            default:
                return ("Unknown value type.", null);
        }
    }

    /// <summary>Admin supplied patterns run without backtracking where possible and give up after 100 ms otherwise.</summary>
    internal static bool Matches(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
        }
        catch (NotSupportedException)
        {
            try
            {
                return Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string? OutOfRange(decimal value, PropertyValidation rule, string unit = "") =>
        (rule.Min is { } min && value < min) || (rule.Max is { } max && value > max)
            ? $"Expected between {rule.Min?.ToString(CultureInfo.InvariantCulture) ?? "-"} and {rule.Max?.ToString(CultureInfo.InvariantCulture) ?? "-"} {unit}".TrimEnd() + "."
            : null;

    // years and months have no fixed length, so durations stick to days and below
    private static bool TryDuration(string text, out TimeSpan duration)
    {
        duration = default;
        if (text.Contains('Y') || (text.IndexOf('T') is var t && text.IndexOf('M') is var m && m >= 0 && (t < 0 || m < t)))
        {
            return false;
        }

        try
        {
            duration = XmlConvert.ToTimeSpan(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
