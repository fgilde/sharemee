namespace Shareme.Contracts.Automation;

public static class AutomationPermissions
{
    public const string GroupName = "Automation";
    public const string ManageRules = "Automation.Rules";
}

public static class RuleConditionKinds
{
    /// <summary>Must hold for the rule to be looked at at all.</summary>
    public const string Filter = "filter";

    /// <summary>At least one must hold for the "then" actions; otherwise the "else" actions run.</summary>
    public const string Trigger = "trigger";
}

public static class RuleOperators
{
    public const string EqualTo = "equals";
    public const string NotEqualTo = "notEquals";
    public const string GreaterThan = "greaterThan";
    public const string GreaterThanOrEquals = "greaterThanOrEquals";
    public const string LessThan = "lessThan";
    public const string LessThanOrEquals = "lessThanOrEquals";
    public const string Contains = "contains";
    public const string Regex = "regex";
    public const string HasValue = "hasValue";
    public const string ValueChanged = "valueChanged";
    public const string IsNewVersion = "isNewVersion";
    public const string IsNewNode = "isNewNode";
    public const string HasRelation = "hasRelation";

    /// <summary>Operators that compare a property with a value.</summary>
    public static readonly IReadOnlySet<string> WithValue = new HashSet<string> { EqualTo, NotEqualTo, GreaterThan, GreaterThanOrEquals, LessThan, LessThanOrEquals, Contains, Regex };

    /// <summary>Operators that look at a property without a value.</summary>
    public static readonly IReadOnlySet<string> WithProperty = new HashSet<string> { HasValue, ValueChanged };

    public static readonly IReadOnlySet<string> All = new HashSet<string>([.. WithValue, .. WithProperty, IsNewVersion, IsNewNode, HasRelation]);
}

/// <summary>A filter or trigger; <see cref="Value"/> is the compared value, the edge type key for hasRelation.</summary>
public sealed record RuleCondition(string Kind, string Operator, string? PropertyKey = null, string? Value = null);

/// <summary>Writes the rendered template into a property, when the triggers matched (<see cref="OnMatch"/>) or when they did not.</summary>
public sealed record RuleAction(string PropertyKey, string Template, bool OnMatch = true, bool OnlyIfEmpty = false);

public sealed record RuleDto(Guid Id, string Name, bool IsActive, IReadOnlyList<string> NodeTypeKeys, IReadOnlyList<RuleCondition> Conditions, IReadOnlyList<RuleAction> Actions, int SortIndex);

public sealed record RuleRequest(string Name, IReadOnlyList<RuleCondition> Conditions, IReadOnlyList<RuleAction> Actions, IReadOnlyList<string>? NodeTypeKeys = null, bool IsActive = true, int SortIndex = 0);

/// <summary>What a rule would do to a node, without writing.</summary>
public sealed record RuleTestDto(bool FiltersMatched, bool TriggersMatched, IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Problems);

public static class RuleJobStates
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>Applies active rules (or the given ones) to nodes or to everything below a folder, afterwards.</summary>
public sealed record RuleJobRequest(IReadOnlyList<Guid>? NodeIds = null, Guid? FolderId = null, IReadOnlyList<Guid>? RuleIds = null);

public sealed record RuleJobDto(Guid Id, string State, int Total, int Matched, int Executed, int Skipped, int Failed, string? Error, IReadOnlyList<RuleJobEntryDto> Entries, DateTimeOffset CreatedAt);

public sealed record RuleJobEntryDto(Guid NodeId, string Result, string? Message);
