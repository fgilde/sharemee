using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shareme.Contracts.Metadata;

namespace Shareme.Metadata;

public sealed record GetPropertyGroups : IQuery<Result<IReadOnlyList<PropertyGroupDto>>>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record SavePropertyGroup(Guid? Id, PropertyGroupRequest Request) : ICommand<Result<PropertyGroupDto>>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record DeletePropertyGroup(Guid Id) : ICommand<Result>;

public sealed record GetPropertyDefinitions(string? NodeType) : IQuery<Result<IReadOnlyList<PropertyDefinitionDto>>>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record SavePropertyDefinition(Guid? Id, PropertyDefinitionRequest Request) : ICommand<Result<PropertyDefinitionDto>>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record DeletePropertyDefinition(Guid Id) : ICommand<Result>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record SetPropertyListValues(Guid DefinitionId, IReadOnlyList<PropertyListValueDto> Values) : ICommand<Result>;

public sealed record GetDataLanguages : IQuery<Result<IReadOnlyList<DataLanguageDto>>>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record SaveDataLanguage(DataLanguageDto Language) : ICommand<Result>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record DeleteDataLanguage(string Code) : ICommand<Result>;

public sealed record GetUserContexts : IQuery<Result<IReadOnlyList<UserContextDto>>>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record SaveUserContext(UserContextDto Context) : ICommand<Result>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record DeleteUserContext(string Key) : ICommand<Result>;

public sealed record GetPropertySets(string? NodeType) : IQuery<Result<IReadOnlyList<PropertySetDto>>>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record SavePropertySet(Guid? Id, PropertySetRequest Request) : ICommand<Result<PropertySetDto>>;

[RequiresPermission(MetadataPermissions.Manage)]
public sealed record DeletePropertySet(Guid Id) : ICommand<Result>;

internal static class CatalogErrors
{
    public static readonly Error NotFound = Error.NotFound("metadata.not_found", "The entry does not exist.");
    public static readonly Error SystemIsFixed = Error.Conflict("metadata.system", "System entries cannot be changed or deleted.");

    public static Error? Key(string? key, string field = "Key") =>
        string.IsNullOrWhiteSpace(key) || key.Length > 100 || !key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_')
            ? Error.Validation(field, "Use lower case letters, digits, '-' or '_' (up to 100 characters).")
            : null;

    public static Error? Name(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Length > 200 ? Error.Validation("Name", "A name of up to 200 characters is required.") : null;
}

/// <summary>The tenant's metadata catalog; without own languages or contexts a single virtual default applies.</summary>
internal sealed class MetadataCatalog(CoworkeeDbContext db)
{
    public static readonly DataLanguageDto FallbackLanguage = new("en", "English", true);
    public static readonly UserContextDto FallbackContext = new("default", "Default", true);

    public async Task<IReadOnlyList<DataLanguageDto>> LanguagesAsync(CancellationToken cancellationToken)
    {
        var languages = await db.Set<DataLanguage>().AsNoTracking().OrderByDescending(l => l.IsDefault).ThenBy(l => l.Code)
            .Select(l => new DataLanguageDto(l.Code, l.Name, l.IsDefault)).ToListAsync(cancellationToken);
        return languages.Count == 0 ? [FallbackLanguage] : languages;
    }

    public async Task<IReadOnlyList<UserContextDto>> ContextsAsync(CancellationToken cancellationToken)
    {
        var contexts = await db.Set<UserContext>().AsNoTracking().OrderByDescending(c => c.IsDefault).ThenBy(c => c.Key)
            .Select(c => new UserContextDto(c.Key, c.Name, c.IsDefault)).ToListAsync(cancellationToken);
        return contexts.Count == 0 ? [FallbackContext] : contexts;
    }

    public Task<List<PropertyDefinition>> DefinitionsAsync(CancellationToken cancellationToken) =>
        db.Set<PropertyDefinition>().AsNoTracking().Include(d => d.ListValues).OrderBy(d => d.SortIndex).ThenBy(d => d.Key).ToListAsync(cancellationToken);

    public static PropertyDefinitionDto Map(PropertyDefinition d) => new(
        d.Id, d.Key, d.GroupId, d.Name, d.Description, d.ValueType, d.ListMode, d.AllowCustomValues, d.MultiValue, d.Localizable, d.HasContext,
        d.Editable, d.IsSystem, d.Searchable, d.Facet, d.IgnoreOnNewVersion, d.InheritToChildren, d.NodeTypeKeys, d.Validation, d.SortIndex,
        d.ListValues.OrderBy(v => v.SortIndex).ThenBy(v => v.Key).Select(v => new PropertyListValueDto(v.Key, v.Name, v.SortIndex, v.IsActive)).ToList());
}

internal sealed class CatalogHandlers(CoworkeeDbContext db, MetadataCatalog catalog, Shareme.Graph.Restrictions restrictions)
    : IHandler<GetPropertyGroups, Result<IReadOnlyList<PropertyGroupDto>>>,
      IHandler<SavePropertyGroup, Result<PropertyGroupDto>>,
      IHandler<DeletePropertyGroup, Result>,
      IHandler<GetPropertyDefinitions, Result<IReadOnlyList<PropertyDefinitionDto>>>,
      IHandler<SavePropertyDefinition, Result<PropertyDefinitionDto>>,
      IHandler<DeletePropertyDefinition, Result>,
      IHandler<SetPropertyListValues, Result>,
      IHandler<GetDataLanguages, Result<IReadOnlyList<DataLanguageDto>>>,
      IHandler<SaveDataLanguage, Result>,
      IHandler<DeleteDataLanguage, Result>,
      IHandler<GetUserContexts, Result<IReadOnlyList<UserContextDto>>>,
      IHandler<SaveUserContext, Result>,
      IHandler<DeleteUserContext, Result>,
      IHandler<GetPropertySets, Result<IReadOnlyList<PropertySetDto>>>,
      IHandler<SavePropertySet, Result<PropertySetDto>>,
      IHandler<DeletePropertySet, Result>
{
    public async Task<Result<IReadOnlyList<PropertyGroupDto>>> HandleAsync(GetPropertyGroups query, CancellationToken cancellationToken)
    {
        IReadOnlyList<PropertyGroupDto> groups = await db.Set<PropertyGroup>().AsNoTracking().OrderBy(g => g.SortIndex).ThenBy(g => g.Key)
            .Select(g => new PropertyGroupDto(g.Id, g.Key, g.Name, g.SortIndex)).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<PropertyGroupDto>>.Success(groups);
    }

    public async Task<Result<PropertyGroupDto>> HandleAsync(SavePropertyGroup command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if ((CatalogErrors.Key(request.Key) ?? CatalogErrors.Name(request.Name)) is { } invalid)
        {
            return invalid;
        }

        if (await db.Set<PropertyGroup>().AnyAsync(g => g.Key == request.Key && g.Id != command.Id, cancellationToken))
        {
            return Error.Conflict("metadata.exists", "A group with this key already exists.");
        }

        PropertyGroup? group;
        if (command.Id is { } id)
        {
            group = await db.Set<PropertyGroup>().SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
            if (group is null)
            {
                return CatalogErrors.NotFound;
            }

            group.Key = request.Key;
            group.Name = request.Name.Trim();
        }
        else
        {
            group = new PropertyGroup { Key = request.Key, Name = request.Name.Trim() };
            db.Add(group);
        }

        group.SortIndex = request.SortIndex;
        return new PropertyGroupDto(group.Id, group.Key, group.Name, group.SortIndex);
    }

    public async Task<Result> HandleAsync(DeletePropertyGroup command, CancellationToken cancellationToken)
    {
        if (await db.Set<PropertyGroup>().SingleOrDefaultAsync(g => g.Id == command.Id, cancellationToken) is not { } group)
        {
            return CatalogErrors.NotFound;
        }

        db.Remove(group);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PropertyDefinitionDto>>> HandleAsync(GetPropertyDefinitions query, CancellationToken cancellationToken)
    {
        var definitions = await catalog.DefinitionsAsync(cancellationToken);
        var hidden = (await restrictions.CurrentAsync(cancellationToken)).HiddenProperties;
        IReadOnlyList<PropertyDefinitionDto> mapped = definitions.Where(d => (query.NodeType is null || d.AppliesTo(query.NodeType)) && !hidden.Contains(d.Key)).Select(MetadataCatalog.Map).ToList();
        return Result<IReadOnlyList<PropertyDefinitionDto>>.Success(mapped);
    }

    public async Task<Result<PropertyDefinitionDto>> HandleAsync(SavePropertyDefinition command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        PropertyDefinition? definition;
        if (command.Id is { } id)
        {
            definition = await db.Set<PropertyDefinition>().Include(d => d.ListValues).SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (definition is null)
            {
                return CatalogErrors.NotFound;
            }

            if (definition.IsSystem)
            {
                return CatalogErrors.SystemIsFixed;
            }

            if (definition.Key != request.Key || definition.ValueType != request.ValueType)
            {
                return Error.Validation("Key", "Key and value type cannot change once values may exist.");
            }
        }
        else
        {
            if (await db.Set<PropertyDefinition>().AnyAsync(d => d.Key == request.Key, cancellationToken))
            {
                return Error.Conflict("metadata.exists", "A property with this key already exists.");
            }

            definition = new PropertyDefinition { Key = request.Key, Name = string.Empty, ValueType = request.ValueType };
            db.Add(definition);
        }

        if (request.GroupId is { } groupId && !await db.Set<PropertyGroup>().AnyAsync(g => g.Id == groupId, cancellationToken))
        {
            return Error.Validation("GroupId", "The group does not exist.");
        }

        definition.Name = request.Name.Trim();
        definition.GroupId = request.GroupId;
        definition.Description = request.Description;
        definition.ListMode = request.ListMode;
        definition.AllowCustomValues = request.AllowCustomValues;
        definition.MultiValue = request.MultiValue;
        definition.Localizable = request.Localizable;
        definition.HasContext = request.HasContext;
        definition.Editable = request.Editable;
        definition.Searchable = request.Searchable;
        definition.Facet = request.Facet;
        definition.IgnoreOnNewVersion = request.IgnoreOnNewVersion;
        definition.InheritToChildren = request.InheritToChildren;
        definition.NodeTypeKeys = request.NodeTypeKeys?.Distinct().ToList() ?? [];
        definition.Validation = request.Validation ?? new PropertyValidation();
        definition.SortIndex = request.SortIndex;
        return MetadataCatalog.Map(definition);
    }

    public async Task<Result> HandleAsync(DeletePropertyDefinition command, CancellationToken cancellationToken)
    {
        if (await db.Set<PropertyDefinition>().SingleOrDefaultAsync(d => d.Id == command.Id, cancellationToken) is not { } definition)
        {
            return CatalogErrors.NotFound;
        }

        if (definition.IsSystem)
        {
            return CatalogErrors.SystemIsFixed;
        }

        // ponytail: values stay in Node.Metadata and are ignored without a definition; a cleanup job can strip them when storage matters
        db.Remove(definition);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(SetPropertyListValues command, CancellationToken cancellationToken)
    {
        var definition = await db.Set<PropertyDefinition>().Include(d => d.ListValues).SingleOrDefaultAsync(d => d.Id == command.DefinitionId, cancellationToken);
        if (definition is null)
        {
            return CatalogErrors.NotFound;
        }

        if (definition.ListMode == ListModes.None)
        {
            return Error.Validation("ListMode", "This property has no list values.");
        }

        foreach (var value in command.Values)
        {
            if ((CatalogErrors.Key(value.Key) ?? CatalogErrors.Name(value.Name)) is { } invalid)
            {
                return invalid;
            }
        }

        if (command.Values.GroupBy(v => v.Key).Any(g => g.Count() > 1))
        {
            return Error.Validation("Key", "List value keys must be unique.");
        }

        var wanted = command.Values.ToDictionary(v => v.Key);
        foreach (var removed in definition.ListValues.Where(v => !wanted.ContainsKey(v.Key)).ToList())
        {
            definition.ListValues.Remove(removed);
            db.Remove(removed);
        }

        foreach (var value in command.Values)
        {
            var existing = definition.ListValues.SingleOrDefault(v => v.Key == value.Key);
            if (existing is null)
            {
                existing = new PropertyListValue { DefinitionId = definition.Id, Key = value.Key, Name = string.Empty };
                definition.ListValues.Add(existing);
                db.Add(existing);
            }

            existing.Name = value.Name.Trim();
            existing.SortIndex = value.SortIndex;
            existing.IsActive = value.IsActive;
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<DataLanguageDto>>> HandleAsync(GetDataLanguages query, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<DataLanguageDto>>.Success(await catalog.LanguagesAsync(cancellationToken));

    public async Task<Result> HandleAsync(SaveDataLanguage command, CancellationToken cancellationToken)
    {
        var request = command.Language;
        var codeError = request.Code is { Length: > 20 } ? Error.Validation("Code", "Up to 20 characters.") : CatalogErrors.Key(request.Code, "Code");
        if ((codeError ?? CatalogErrors.Name(request.Name)) is { } invalid)
        {
            return invalid;
        }

        var languages = await db.Set<DataLanguage>().ToListAsync(cancellationToken);
        if (languages.Count == 0 && request.Code != MetadataCatalog.FallbackLanguage.Code)
        {
            // the first own language turns the virtual default into a real row so existing values stay valid
            languages.Add(new DataLanguage { Code = MetadataCatalog.FallbackLanguage.Code, Name = MetadataCatalog.FallbackLanguage.Name, IsDefault = true });
            db.Add(languages[0]);
        }

        var language = languages.SingleOrDefault(l => l.Code == request.Code);
        if (language is null)
        {
            language = new DataLanguage { Code = request.Code, Name = string.Empty };
            languages.Add(language);
            db.Add(language);
        }

        language.Name = request.Name.Trim();
        if (request.IsDefault || languages.Count == 1)
        {
            languages.ForEach(l => l.IsDefault = l == language);
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteDataLanguage command, CancellationToken cancellationToken)
    {
        if (await db.Set<DataLanguage>().SingleOrDefaultAsync(l => l.Code == command.Code, cancellationToken) is not { } language)
        {
            return CatalogErrors.NotFound;
        }

        if (language.IsDefault)
        {
            return Error.Conflict("metadata.default", "Choose another default language first.");
        }

        db.Remove(language);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<UserContextDto>>> HandleAsync(GetUserContexts query, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<UserContextDto>>.Success(await catalog.ContextsAsync(cancellationToken));

    public async Task<Result> HandleAsync(SaveUserContext command, CancellationToken cancellationToken)
    {
        var request = command.Context;
        if ((CatalogErrors.Key(request.Key) ?? CatalogErrors.Name(request.Name)) is { } invalid)
        {
            return invalid;
        }

        var contexts = await db.Set<UserContext>().ToListAsync(cancellationToken);
        if (contexts.Count == 0 && request.Key != MetadataCatalog.FallbackContext.Key)
        {
            contexts.Add(new UserContext { Key = MetadataCatalog.FallbackContext.Key, Name = MetadataCatalog.FallbackContext.Name, IsDefault = true });
            db.Add(contexts[0]);
        }

        var context = contexts.SingleOrDefault(c => c.Key == request.Key);
        if (context is null)
        {
            context = new UserContext { Key = request.Key, Name = string.Empty };
            contexts.Add(context);
            db.Add(context);
        }

        context.Name = request.Name.Trim();
        if (request.IsDefault || contexts.Count == 1)
        {
            contexts.ForEach(c => c.IsDefault = c == context);
        }

        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteUserContext command, CancellationToken cancellationToken)
    {
        if (await db.Set<UserContext>().SingleOrDefaultAsync(c => c.Key == command.Key, cancellationToken) is not { } context)
        {
            return CatalogErrors.NotFound;
        }

        if (context.IsDefault)
        {
            return Error.Conflict("metadata.default", "Choose another default context first.");
        }

        db.Remove(context);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PropertySetDto>>> HandleAsync(GetPropertySets query, CancellationToken cancellationToken)
    {
        var sets = db.Set<PropertySet>().AsNoTracking();
        if (query.NodeType is { } nodeType)
        {
            sets = sets.Where(s => s.NodeTypeKey == nodeType);
        }

        IReadOnlyList<PropertySetDto> mapped = (await sets.OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<PropertySetDto>>.Success(mapped);
    }

    public async Task<Result<PropertySetDto>> HandleAsync(SavePropertySet command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (CatalogErrors.Name(request.Name) is { } invalid)
        {
            return invalid;
        }

        var definitions = (await catalog.DefinitionsAsync(cancellationToken)).ToDictionary(d => d.Key);
        var conditions = request.Conditions ?? [];
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var duplicate in request.Items.GroupBy(i => i.DefinitionKey).Where(g => g.Count() > 1))
        {
            errors[duplicate.Key] = ["Each property may appear only once in a set."];
        }

        foreach (var item in request.Items)
        {
            if (!definitions.TryGetValue(item.DefinitionKey, out var definition) || !definition.AppliesTo(request.NodeTypeKey))
            {
                errors[item.DefinitionKey] = [$"No property '{item.DefinitionKey}' applies to '{request.NodeTypeKey}'."];
            }
            else if (item.FillMode is not (FillModes.Optional or FillModes.Recommended or FillModes.Mandatory))
            {
                errors[item.DefinitionKey] = ["Unknown fill mode."];
            }
        }

        foreach (var condition in conditions)
        {
            if (!definitions.TryGetValue(condition.DefinitionKey, out var definition) || definition.ListValues.All(v => v.Key != condition.ListValueKey))
            {
                errors[condition.DefinitionKey] = [$"The condition refers to an unknown list value '{condition.ListValueKey}'."];
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        PropertySet? set;
        if (command.Id is { } id)
        {
            set = await db.Set<PropertySet>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (set is null)
            {
                return CatalogErrors.NotFound;
            }
        }
        else
        {
            set = new PropertySet { NodeTypeKey = request.NodeTypeKey, Name = string.Empty };
            db.Add(set);
        }

        set.NodeTypeKey = request.NodeTypeKey;
        set.Name = request.Name.Trim();
        set.IsDefault = request.IsDefault;
        set.Items = request.Items.ToList();
        set.Conditions = conditions.ToList();
        if (set.IsDefault)
        {
            await db.Set<PropertySet>().Where(s => s.NodeTypeKey == set.NodeTypeKey && s.Id != set.Id && s.IsDefault)
                .ForEachAsync(s => s.IsDefault = false, cancellationToken);
        }

        return Map(set);
    }

    public async Task<Result> HandleAsync(DeletePropertySet command, CancellationToken cancellationToken)
    {
        if (await db.Set<PropertySet>().SingleOrDefaultAsync(s => s.Id == command.Id, cancellationToken) is not { } set)
        {
            return CatalogErrors.NotFound;
        }

        db.Remove(set);
        return Result.Success();
    }

    private static Error? Validate(PropertyDefinitionRequest request)
    {
        if ((CatalogErrors.Key(request.Key) ?? CatalogErrors.Name(request.Name)) is { } invalid)
        {
            return invalid;
        }

        if (!PropertyValueTypes.All.Contains(request.ValueType))
        {
            return Error.Validation("ValueType", $"Use one of: {string.Join(", ", PropertyValueTypes.All)}.");
        }

        if (request.ListMode is not (ListModes.None or ListModes.Single or ListModes.Multi))
        {
            return Error.Validation("ListMode", "Use none, single or multi.");
        }

        if (request.Searchable is not (SearchModes.None or SearchModes.Filter or SearchModes.Fulltext or SearchModes.Both))
        {
            return Error.Validation("Searchable", "Use none, filter, fulltext or both.");
        }

        if (request.Validation is { Min: { } min, Max: { } max } && min > max)
        {
            return Error.Validation("Validation.Min", "Min must not be greater than Max.");
        }

        if (request.Validation?.Regex is { } pattern)
        {
            if (pattern.Length > 500)
            {
                return Error.Validation("Validation.Regex", "Patterns may have up to 500 characters.");
            }

            try
            {
                _ = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                return Error.Validation("Validation.Regex", "The pattern is not a valid regular expression.");
            }
        }

        return request.Description is { Length: > 2000 } ? Error.Validation("Description", "Up to 2000 characters.") : null;
    }

    private static PropertySetDto Map(PropertySet set) => new(set.Id, set.NodeTypeKey, set.Name, set.IsDefault, set.Items, set.Conditions);
}
