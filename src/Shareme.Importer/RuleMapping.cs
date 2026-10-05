using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Shareme.Automation;
using Shareme.Contracts.Automation;
using Shareme.Metadata;
using RuleAction = Shareme.Contracts.Automation.RuleAction;

namespace Shareme.Importer;

public sealed record RuleRow(long Id, string Identifier, string? TranslationIdentifier);

public sealed record RuleNodeTypeRow(long RuleId, long NodeTypeId);

/// <summary>A trigger or filter; its property is one without node, holding the definition and the value to compare with.</summary>
public sealed record RuleTriggerRow(
    long Id, long RuleId, bool IsFilter, int Operator, long? DefinitionId, long? ListValueId, bool? Bool, long? Int, decimal? Decimal, DateTime? Date, DateTime? DateTime, string? Text);

public sealed record RuleActionRow(long Id, long RuleId, long? DefinitionId, bool OnMatch, string? Template);

public sealed record SourceRules(IReadOnlyList<RuleRow> Rules, IReadOnlyList<RuleNodeTypeRow> NodeTypes, IReadOnlyList<RuleTriggerRow> Triggers, IReadOnlyList<RuleActionRow> Actions)
{
    public static readonly SourceRules Empty = new([], [], [], []);

    public static async Task<SourceRules> LoadAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        async Task<List<T>> Query<T>(string sql, Func<SqlDataReader, T> map)
        {
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 600 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
        }

        return new SourceRules(
            await Query("select RuleId, Identifier, TranslationIdentifier from Admin.[Rule] order by RuleId", r => new RuleRow(r.GetInt64(0), r.GetString(1), r.Text(2))),
            await Query("select RuleId, NodeTypeId from Admin.RuleNodeType order by RuleNodeTypeId", r => new RuleNodeTypeRow(r.GetInt64(0), r.GetInt64(1))),
            await Query(
                """
                select t.RuleTriggerId, t.RuleId, t.IsFilter, t.TriggerOperator, p.PropertyDefinitionId,
                       v.PropertyListValueId, v.ValueBool, v.ValueInt, v.ValueDecimal, v.ValueDate, v.ValueDateTime, v.ValueText
                from Admin.RuleTrigger t
                left join MAM.Property p on p.PropertyId = t.PropertyId and p.IsDeleted = 0
                outer apply (select top 1 * from MAM.PropertyValue x where x.PropertyId = p.PropertyId and x.IsDeleted = 0 order by x.PropertyValueId) v
                order by t.RuleTriggerId
                """,
                r => new RuleTriggerRow(r.GetInt64(0), r.GetInt64(1), r.GetBoolean(2), r.GetInt32(3), r.Long(4), r.Long(5), r.IsDBNull(6) ? null : r.GetBoolean(6), r.Long(7),
                    r.IsDBNull(8) ? null : r.GetDecimal(8), r.IsDBNull(9) ? null : r.GetDateTime(9), r.IsDBNull(10) ? null : r.GetDateTime(10), r.Text(11))),
            await Query(
                """
                select a.RuleActionId, a.RuleId, p.PropertyDefinitionId, a.OnTriggerMatched, a.Template
                from Admin.RuleAction a left join MAM.Property p on p.PropertyId = a.PropertyId and p.IsDeleted = 0
                order by a.RuleActionId
                """,
                r => new RuleActionRow(r.GetInt64(0), r.GetInt64(1), r.Long(2), r.GetBoolean(3), r.Text(4))));
    }
}

/// <summary>
/// Rules come over inactive: Sharedien's template language matches Shareme's except for chained functions (increment,
/// write), and property arguments are renamed to Shareme keys. What does not map is left out, the rule marked incomplete
/// and the gap reported, so an admin finishes it before switching it on.
/// </summary>
public sealed partial class RuleMapper(SourceRules source, Guid tenantId, ImportReport report, Func<string?, string, string> name)
{
    private const string Incomplete = " (import incomplete)";

    // Sharedien TriggerOperator: Equals 0, NotEquals 1, GreaterThan 2, LessThan 3, GreaterThanOrEquals 4, LessThanOrEquals 5,
    // Contains 6, HasRelationType 7, IsNewVersion 8, ValueChanged 9, IsNewCategory 10, RegexMatch 11
    private static readonly Dictionary<int, string> Operators = new()
    {
        [0] = RuleOperators.EqualTo, [1] = RuleOperators.NotEqualTo, [2] = RuleOperators.GreaterThan, [3] = RuleOperators.LessThan,
        [4] = RuleOperators.GreaterThanOrEquals, [5] = RuleOperators.LessThanOrEquals, [6] = RuleOperators.Contains, [8] = RuleOperators.IsNewVersion,
        [9] = RuleOperators.ValueChanged, [10] = RuleOperators.IsNewNode, [11] = RuleOperators.Regex,
    };

    /// <param name="definitions">Imported or existing definitions by Sharedien id.</param>
    /// <param name="definitionKeys">Shareme keys by Sharedien definition identifier, for template arguments.</param>
    public List<Rule> Map(IReadOnlyDictionary<long, string> typeKeys, IReadOnlyDictionary<long, PropertyDefinition> definitions,
        IReadOnlyDictionary<string, string> definitionKeys, IReadOnlyDictionary<long, string> listKeys)
    {
        var nodeTypes = source.NodeTypes.ToLookup(t => t.RuleId);
        var triggers = source.Triggers.ToLookup(t => t.RuleId);
        var actions = source.Actions.ToLookup(a => a.RuleId);
        var rules = new List<Rule>();
        foreach (var row in source.Rules)
        {
            var complete = true;
            var conditions = new List<RuleCondition>();
            foreach (var trigger in triggers[row.Id])
            {
                if (Condition(trigger, definitions, listKeys) is { } condition)
                {
                    conditions.Add(condition);
                }
                else
                {
                    complete = false;
                    report.Problem("rule_condition_not_mapped", "Admin.RuleTrigger", trigger.Id);
                }
            }

            var mapped = new List<RuleAction>();
            foreach (var action in actions[row.Id])
            {
                if (Action(action, definitions, definitionKeys) is { } done)
                {
                    mapped.Add(done);
                }
                else
                {
                    complete = false;
                    report.Problem("rule_action_not_mapped", "Admin.RuleAction", action.Id);
                }
            }

            var title = name(row.TranslationIdentifier, row.Identifier);
            var rule = new Rule
            {
                Name = complete ? Cap(title, 200) : Cap(title, 200 - Incomplete.Length) + Incomplete,
                IsActive = false,
                NodeTypeKeys = [.. nodeTypes[row.Id].Select(t => typeKeys.GetValueOrDefault(t.NodeTypeId)).OfType<string>().Distinct()],
                Conditions = conditions,
                Actions = mapped,
                SortIndex = rules.Count,
                TenantId = tenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            Ids.Set(rule, SourceIds.For(tenantId, "Admin.Rule", row.Id, row.Identifier));
            rules.Add(rule);
        }

        report.Mapped["rules"] = rules.Count;
        return rules;
    }

    private static RuleCondition? Condition(RuleTriggerRow row, IReadOnlyDictionary<long, PropertyDefinition> definitions, IReadOnlyDictionary<long, string> listKeys)
    {
        if (!Operators.TryGetValue(row.Operator, out var op))
        {
            return null; // HasRelationType: Sharedien keeps the relation in AdditionalData
        }

        var kind = row.IsFilter ? RuleConditionKinds.Filter : RuleConditionKinds.Trigger;
        if (op is RuleOperators.IsNewVersion or RuleOperators.IsNewNode)
        {
            return new RuleCondition(kind, op);
        }

        if (row.DefinitionId is not { } definitionId || !definitions.TryGetValue(definitionId, out var definition))
        {
            return null;
        }

        if (op == RuleOperators.ValueChanged)
        {
            return new RuleCondition(kind, op, definition.Key);
        }

        // compared as Shareme renders values (MetadataText.Text): list key, text, invariant numbers, true/false, ISO dates
        var value = row.ListValueId is { } listValue ? listKeys.GetValueOrDefault(listValue)
            : row.Text ?? (row.Int?.ToString(CultureInfo.InvariantCulture) ?? row.Decimal?.ToString(CultureInfo.InvariantCulture)
                ?? (row.Bool is { } flag ? (flag ? "true" : "false") : null) ?? row.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? row.DateTime?.ToString("O", CultureInfo.InvariantCulture));
        return value is null || (op == RuleOperators.Regex && !RuleTemplates.ValidPattern(value)) ? null : new RuleCondition(kind, op, definition.Key, value);
    }

    private static RuleAction? Action(RuleActionRow row, IReadOnlyDictionary<long, PropertyDefinition> definitions, IReadOnlyDictionary<string, string> definitionKeys)
    {
        // the same targets SaveRule accepts: rules write one value without language or context
        if (row.DefinitionId is not { } definitionId || !definitions.TryGetValue(definitionId, out var target) || target.Localizable || target.HasContext || !target.Editable || target.IsSystem)
        {
            return null;
        }

        var unknown = false;
        var template = PropertyArgument().Replace(row.Template ?? string.Empty, m =>
        {
            if (definitionKeys.TryGetValue(m.Groups[2].Value.Trim(), out var key))
            {
                return $"fnc.{m.Groups[1].Value}({key})";
            }

            unknown = true;
            return m.Value;
        });
        return unknown || template.Length > 2000 || RuleTemplates.Problems(template).Any() ? null : new RuleAction(target.Key, template, row.OnMatch);
    }

    private static string Cap(string text, int length) => text.Length > length ? text[..length] : text;

    [GeneratedRegex(@"fnc\.(find|findInAscendants)\(([^)]*)\)")]
    private static partial Regex PropertyArgument();
}
