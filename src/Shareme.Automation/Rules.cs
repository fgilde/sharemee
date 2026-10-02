using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shareme.Assets;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Metadata;
using Shareme.Graph;
using Shareme.Metadata;

namespace Shareme.Automation;

/// <summary>Rules that fill properties when nodes change, live and as a job afterwards.</summary>
[DependsOn(typeof(ShareMeAssetsModule))]
public sealed class ShareMeAutomationModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(ShareMeAutomationModule).Assembly);
        context.Services.AddSingleton<IModelContributor, AutomationModelContributor>();
        context.Services.AddSingleton<IPermissionDefinitionContributor, AutomationPermissionDefinitions>();
        context.Services.AddScoped<RuleEngine>();
        context.Services.AddScoped<RuleJobRunner>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var rules = app.MapGroup("/api/v1/rules").WithTags("Rules").RequireAuthorization();
        rules.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRules(), ct).ToHttpResult());
        rules.MapPost("/", (RuleRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveRule(null, body), ct).ToHttpResult());
        rules.MapPut("/{id:guid}", (Guid id, RuleRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveRule(id, body), ct).ToHttpResult());
        rules.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteRule(id), ct).ToHttpResult());
        rules.MapPost("/{id:guid}/test/{nodeId:guid}", (Guid id, Guid nodeId, IDispatcher d, CancellationToken ct) => d.SendAsync(new TestRule(id, nodeId), ct).ToHttpResult());

        var jobs = app.MapGroup("/api/v1/rule-jobs").WithTags("Rules").RequireAuthorization();
        jobs.MapPost("/", (RuleJobRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new StartRuleJob(body), ct).ToHttpResult());
        jobs.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRuleJobs(), ct).ToHttpResult());
        jobs.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRuleJob(id), ct).ToHttpResult());
    }
}

public sealed class Rule : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    public List<string> NodeTypeKeys { get; set; } = [];

    public List<RuleCondition> Conditions { get; set; } = [];

    public List<RuleAction> Actions { get; set; } = [];

    public int SortIndex { get; set; }

    public Guid TenantId { get; set; }

    public bool AppliesTo(string typeKey) => NodeTypeKeys.Count == 0 || NodeTypeKeys.Contains(typeKey);
}

[NotAudited]
public sealed class RuleJob : Entity, IMultiTenant
{
    public List<Guid> NodeIds { get; set; } = [];

    public Guid? FolderId { get; set; }

    public List<Guid> RuleIds { get; set; } = [];

    public string State { get; set; } = RuleJobStates.Queued;

    public int Total { get; set; }

    public int Matched { get; set; }

    public int Executed { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public string? Error { get; set; }

    public List<RuleJobEntryDto> Entries { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public Guid TenantId { get; set; }
}

internal sealed class AutomationModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Rule>(rule =>
        {
            rule.ToTable("Rules", "sm");
            rule.Property(r => r.Name).HasMaxLength(200);
            rule.Property(r => r.NodeTypeKeys).HasColumnType("jsonb");
            Json(rule.Property(r => r.Conditions));
            Json(rule.Property(r => r.Actions));
        });

        modelBuilder.Entity<RuleJob>(job =>
        {
            job.ToTable("RuleJobs", "sm");
            job.Property(j => j.State).HasMaxLength(20);
            job.Property(j => j.Error).HasMaxLength(1000);
            job.Property(j => j.NodeIds).HasColumnType("jsonb");
            job.Property(j => j.RuleIds).HasColumnType("jsonb");
            Json(job.Property(j => j.Entries));
            job.HasIndex(j => j.CreatedAt);
        });
    }

    private static void Json<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<List<T>> property) =>
        property.HasColumnType("jsonb")
            .HasConversion(v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), s => JsonSerializer.Deserialize<List<T>>(s, (JsonSerializerOptions?)null) ?? new())
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<T>>(
                (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                v => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
}

internal sealed class AutomationPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(AutomationPermissions.GroupName, "Automation").Add(AutomationPermissions.ManageRules, "Manage rules and run them on existing content");
}

/// <summary>What happened to a node and why rules look at it now.</summary>
internal sealed record RuleContext(Node Node, string TypeKey, IReadOnlySet<string> ChangedKeys, bool IsNewVersion, bool IsNewNode);

internal sealed record RuleOutcome(bool FiltersMatched, bool TriggersMatched, Dictionary<string, string> Values, List<string> Problems);

/// <summary>Evaluates conditions, renders templates and writes through the regular metadata command, so the same checks and permissions apply.</summary>
internal sealed partial class RuleEngine(CoworkeeDbContext db, MetadataCatalog catalog, IDispatcher dispatcher, TimeProvider clock)
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    // arguments end at the first ")}", so patterns may contain parentheses
    [GeneratedRegex(@"\{(var\.[a-z]+|fnc\.([a-zA-Z]+)\((.*?)\))\}")]
    private static partial Regex Token();

    public async Task<Context?> ContextAsync(Guid nodeId, IReadOnlySet<string> changed, bool isNewVersion, bool isNewNode, CancellationToken cancellationToken)
    {
        if (await db.Set<Node>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == nodeId, cancellationToken) is not { } node)
        {
            return null;
        }

        var typeKey = await db.Set<NodeType>().Where(t => t.Id == node.NodeTypeId).Select(t => t.Key).SingleAsync(cancellationToken);
        return new Context(new RuleContext(node, typeKey, changed, isNewVersion, isNewNode));
    }

    public sealed record Context(RuleContext Rule);

    public async Task<RuleOutcome> EvaluateAsync(Rule rule, RuleContext context, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var filters = rule.Conditions.Where(c => c.Kind == RuleConditionKinds.Filter).ToList();
        var triggers = rule.Conditions.Where(c => c.Kind == RuleConditionKinds.Trigger).ToList();
        var filtersMatched = true;
        foreach (var filter in filters)
        {
            filtersMatched &= await HoldsAsync(filter, context, problems, cancellationToken);
        }

        var triggersMatched = triggers.Count == 0;
        foreach (var trigger in triggers)
        {
            triggersMatched |= await HoldsAsync(trigger, context, problems, cancellationToken);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (filtersMatched)
        {
            foreach (var action in rule.Actions.Where(a => a.OnMatch == triggersMatched))
            {
                if (action.OnlyIfEmpty && context.Node.Metadata.TryGetValue(action.PropertyKey, out var existing) && existing.Count > 0)
                {
                    continue;
                }

                values[action.PropertyKey] = await RenderAsync(action.Template, context.Node, problems, cancellationToken);
            }
        }

        return new RuleOutcome(filtersMatched, triggersMatched, values, problems);
    }

    /// <summary>Writes the outcome as the current user (whose change triggered the rule, or who started the job); null on success, else why not.</summary>
    public async Task<string?> WriteAsync(Node node, Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var definitions = (await catalog.DefinitionsAsync(cancellationToken)).ToDictionary(d => d.Key);
        var update = new Dictionary<string, List<MetadataValue>?>(StringComparer.Ordinal);
        foreach (var (key, text) in values)
        {
            update[key] = text.Length == 0 ? null : definitions.TryGetValue(key, out var definition) ? [ToValue(definition, text)] : [new MetadataValue(V: JsonSerializer.SerializeToElement(text))];
        }

        var result = await dispatcher.SendAsync(new SetNodeMetadata(node.Id, update, Replace: false, MetadataSources.Rule), cancellationToken);
        return result.IsSuccess ? null : result.Error!.Details is { Count: > 0 } details
            ? string.Join("; ", details.Select(d => $"{d.Key}: {string.Join(" ", d.Value)}"))
            : result.Error.Message;
    }

    private async Task<bool> HoldsAsync(RuleCondition condition, RuleContext context, List<string> problems, CancellationToken cancellationToken)
    {
        var values = condition.PropertyKey is { } key && context.Node.Metadata.TryGetValue(key, out var found) ? found.Select(Text).ToList() : [];
        var expected = condition.Value ?? string.Empty;
        switch (condition.Operator)
        {
            case RuleOperators.EqualTo:
                return values.Any(v => string.Equals(v, expected, StringComparison.OrdinalIgnoreCase));
            case RuleOperators.NotEqualTo:
                return !values.Any(v => string.Equals(v, expected, StringComparison.OrdinalIgnoreCase));
            case RuleOperators.Contains:
                return values.Any(v => v.Contains(expected, StringComparison.OrdinalIgnoreCase));
            case RuleOperators.Regex:
                try
                {
                    return values.Any(v => Regex.IsMatch(v, expected, RegexOptions.None, RegexTimeout));
                }
                catch (Exception exception) when (exception is RegexMatchTimeoutException or ArgumentException)
                {
                    problems.Add($"The pattern of '{condition.PropertyKey}' failed: {exception.Message}");
                    return false;
                }

            case RuleOperators.GreaterThan or RuleOperators.GreaterThanOrEquals or RuleOperators.LessThan or RuleOperators.LessThanOrEquals:
                return values.Any(v => Compare(v, expected) is { } order && condition.Operator switch
                {
                    RuleOperators.GreaterThan => order > 0,
                    RuleOperators.GreaterThanOrEquals => order >= 0,
                    RuleOperators.LessThan => order < 0,
                    _ => order <= 0,
                });
            case RuleOperators.HasValue:
                return values.Count > 0;
            case RuleOperators.ValueChanged:
                return condition.PropertyKey is { } changedKey && context.ChangedKeys.Contains(changedKey);
            case RuleOperators.IsNewVersion:
                return context.IsNewVersion;
            case RuleOperators.IsNewNode:
                return context.IsNewNode;
            case RuleOperators.HasRelation:
                var nodeId = context.Node.Id;
                return await (from edge in db.Set<Edge>()
                              join type in db.Set<EdgeType>() on edge.EdgeTypeId equals type.Id
                              where (edge.FromNodeId == nodeId || edge.ToNodeId == nodeId) && type.Key == expected
                              select edge.Id).AnyAsync(cancellationToken);
            default:
                problems.Add($"Unknown operator '{condition.Operator}'.");
                return false;
        }
    }

    /// <summary>Numbers, then dates, then text; null when one side is no number or date while the other is.</summary>
    private static int? Compare(string value, string expected)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var a) && decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var b))
        {
            return a.CompareTo(b);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var x) && DateTimeOffset.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var y))
        {
            return x.CompareTo(y);
        }

        return string.Compare(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> RenderAsync(string template, Node node, List<string> problems, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var pieces = new List<(int Index, int Length, string Text)>();
        foreach (Match match in Token().Matches(template))
        {
            string text;
            if (match.Groups[1].Value.StartsWith("var.", StringComparison.Ordinal))
            {
                text = match.Groups[1].Value switch
                {
                    "var.year" => now.ToString("yyyy", CultureInfo.InvariantCulture),
                    "var.month" => now.ToString("MM", CultureInfo.InvariantCulture),
                    "var.day" => now.ToString("dd", CultureInfo.InvariantCulture),
                    "var.hour" => now.ToString("HH", CultureInfo.InvariantCulture),
                    "var.minute" => now.ToString("mm", CultureInfo.InvariantCulture),
                    "var.second" => now.ToString("ss", CultureInfo.InvariantCulture),
                    "var.date" => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "var.name" => node.Name,
                    _ => Unknown(match.Value, problems),
                };
            }
            else
            {
                var args = match.Groups[3].Value.Split(',', 2, StringSplitOptions.TrimEntries);
                text = match.Groups[2].Value switch
                {
                    "find" => First(node, args[0]),
                    "findInAscendants" => await InAscendantsAsync(node, args[0], cancellationToken),
                    "regexExtract" when args.Length == 2 => Extract(First(node, args[0]), args[1], problems),
                    _ => Unknown(match.Value, problems),
                };
            }

            pieces.Add((match.Index, match.Length, text));
        }

        var result = template;
        foreach (var (index, length, text) in pieces.OrderByDescending(p => p.Index))
        {
            result = result[..index] + text + result[(index + length)..];
        }

        return result.Trim();
    }

    private static string First(Node node, string key) => node.Metadata.TryGetValue(key, out var values) && values.FirstOrDefault() is { } value ? Text(value) : string.Empty;

    /// <summary>The node's own value, else the nearest ancestor's.</summary>
    private async Task<string> InAscendantsAsync(Node node, string key, CancellationToken cancellationToken)
    {
        if (First(node, key) is { Length: > 0 } own)
        {
            return own;
        }

        var ids = node.PathIds.Take(node.PathIds.Count - 1).ToList();
        var ancestors = await db.Set<Node>().AsNoTracking().Where(n => ids.Contains(n.Id)).ToDictionaryAsync(n => n.Id, cancellationToken);
        foreach (var id in Enumerable.Reverse(ids))
        {
            if (ancestors.TryGetValue(id, out var ancestor) && First(ancestor, key) is { Length: > 0 } found)
            {
                return found;
            }
        }

        return string.Empty;
    }

    private static string Extract(string input, string pattern, List<string> problems)
    {
        try
        {
            var match = Regex.Match(input, pattern, RegexOptions.None, RegexTimeout);
            return !match.Success ? string.Empty : match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
        }
        catch (Exception exception) when (exception is RegexMatchTimeoutException or ArgumentException)
        {
            problems.Add($"regexExtract failed: {exception.Message}");
            return string.Empty;
        }
    }

    private static string Unknown(string token, List<string> problems)
    {
        problems.Add($"Unknown template token {token}.");
        return string.Empty;
    }

    internal static string Text(MetadataValue value) => value.List ?? value.V switch
    {
        { ValueKind: JsonValueKind.String } text => text.GetString() ?? string.Empty,
        { } other => other.GetRawText(),
        null => string.Empty,
    };

    /// <summary>The rendered text as a value of the property's type; the metadata checks report what does not fit.</summary>
    private static MetadataValue ToValue(PropertyDefinition definition, string text)
    {
        if (definition.ListMode != ListModes.None)
        {
            return new MetadataValue(List: text);
        }

        return definition.ValueType switch
        {
            PropertyValueTypes.Integer or PropertyValueTypes.Decimal when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) =>
                new MetadataValue(V: JsonSerializer.SerializeToElement(number)),
            PropertyValueTypes.Boolean when bool.TryParse(text, out var flag) => new MetadataValue(V: JsonSerializer.SerializeToElement(flag)),
            _ => new MetadataValue(V: JsonSerializer.SerializeToElement(text)),
        };
    }
}

/// <summary>Runs the tenant's active rules live: after metadata changes (not the rules' own), new versions and new nodes.</summary>
internal sealed class RuleTriggers(CoworkeeDbContext db, RuleEngine engine, ILogger<RuleTriggers> logger)
    : IDomainEventHandler<NodeMetadataChanged>, IDomainEventHandler<AssetVersionCreated>, IDomainEventHandler<NodeChanged>
{
    public Task HandleAsync(NodeMetadataChanged domainEvent, CancellationToken cancellationToken) =>
        domainEvent.Source == MetadataSources.Rule ? Task.CompletedTask : RunAsync(domainEvent.NodeId, domainEvent.Keys.ToHashSet(), false, false, cancellationToken);

    public async Task HandleAsync(AssetVersionCreated domainEvent, CancellationToken cancellationToken)
    {
        var number = await db.Set<AssetVersion>().Where(v => v.Id == domainEvent.VersionId).Select(v => v.Number).SingleOrDefaultAsync(cancellationToken);
        await RunAsync(domainEvent.AssetId, new HashSet<string>(), true, number == 1, cancellationToken);
    }

    // assets are new with their first version; other nodes when they are created
    public async Task HandleAsync(NodeChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.Kind == "created" && !await db.Set<Asset>().AnyAsync(a => a.Id == domainEvent.NodeId, cancellationToken))
        {
            await RunAsync(domainEvent.NodeId, new HashSet<string>(), false, true, cancellationToken);
        }
    }

    private async Task RunAsync(Guid nodeId, IReadOnlySet<string> changed, bool isNewVersion, bool isNewNode, CancellationToken cancellationToken)
    {
        var rules = await db.Set<Rule>().AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.SortIndex).ThenBy(r => r.Name).ToListAsync(cancellationToken);
        if (rules.Count == 0 || await engine.ContextAsync(nodeId, changed, isNewVersion, isNewNode, cancellationToken) is not { } context)
        {
            return;
        }

        foreach (var rule in rules.Where(r => r.AppliesTo(context.Rule.TypeKey)))
        {
            var outcome = await engine.EvaluateAsync(rule, context.Rule, cancellationToken);
            if (outcome.FiltersMatched && await engine.WriteAsync(context.Rule.Node, outcome.Values, cancellationToken) is { } problem)
            {
                logger.LogWarning("Rule {Rule} could not write node {NodeId}: {Problem}", rule.Name, nodeId, problem);
            }

            // later rules see what earlier ones wrote
            if (outcome.Values.Count > 0 && await engine.ContextAsync(nodeId, changed, isNewVersion, isNewNode, cancellationToken) is { } fresh)
            {
                context = fresh;
            }
        }
    }
}

[RequiresPermission(AutomationPermissions.ManageRules)]
public sealed record GetRules : IQuery<Result<IReadOnlyList<RuleDto>>>;

[RequiresPermission(AutomationPermissions.ManageRules)]
public sealed record SaveRule(Guid? Id, RuleRequest Request) : ICommand<Result<RuleDto>>;

[RequiresPermission(AutomationPermissions.ManageRules)]
public sealed record DeleteRule(Guid Id) : ICommand<Result>;

/// <summary>A dry run: what the rule would write to the node now.</summary>
[RequiresPermission(AutomationPermissions.ManageRules)]
public sealed record TestRule(Guid Id, Guid NodeId) : IQuery<Result<RuleTestDto>>;

[RequiresPermission(AutomationPermissions.ManageRules)]
public sealed record StartRuleJob(RuleJobRequest Request) : ICommand<Result<RuleJobDto>>;

[RequiresPermission(AutomationPermissions.ManageRules)]
public sealed record GetRuleJobs : IQuery<Result<IReadOnlyList<RuleJobDto>>>;

[RequiresPermission(AutomationPermissions.ManageRules)]
public sealed record GetRuleJob(Guid Id) : IQuery<Result<RuleJobDto>>;

internal sealed class RuleHandlers(CoworkeeDbContext db, RuleEngine engine, NodeAccess access, IBackgroundJobs jobs, TimeProvider clock)
    : IHandler<GetRules, Result<IReadOnlyList<RuleDto>>>,
      IHandler<SaveRule, Result<RuleDto>>,
      IHandler<DeleteRule, Result>,
      IHandler<TestRule, Result<RuleTestDto>>,
      IHandler<StartRuleJob, Result<RuleJobDto>>,
      IHandler<GetRuleJobs, Result<IReadOnlyList<RuleJobDto>>>,
      IHandler<GetRuleJob, Result<RuleJobDto>>
{
    private static readonly Error NotFound = Error.NotFound("rules.not_found", "The rule does not exist.");

    public async Task<Result<IReadOnlyList<RuleDto>>> HandleAsync(GetRules query, CancellationToken cancellationToken)
    {
        IReadOnlyList<RuleDto> rules = (await db.Set<Rule>().AsNoTracking().OrderBy(r => r.SortIndex).ThenBy(r => r.Name).ToListAsync(cancellationToken)).Select(Map).ToList();
        return Result<IReadOnlyList<RuleDto>>.Success(rules);
    }

    public async Task<Result<RuleDto>> HandleAsync(SaveRule command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Error.Validation("Name", "A name of up to 200 characters is required.");
        }

        if (request.Conditions.Count > 30 || request.Actions.Count is 0 or > 20)
        {
            return Error.Validation("Actions", "A rule has 1 to 20 actions and up to 30 conditions.");
        }

        foreach (var condition in request.Conditions)
        {
            if (condition.Kind is not (RuleConditionKinds.Filter or RuleConditionKinds.Trigger) || !RuleOperators.All.Contains(condition.Operator))
            {
                return Error.Validation("Conditions", $"Unknown condition '{condition.Kind} {condition.Operator}'.");
            }

            if ((RuleOperators.WithValue.Contains(condition.Operator) || RuleOperators.WithProperty.Contains(condition.Operator)) && string.IsNullOrWhiteSpace(condition.PropertyKey))
            {
                return Error.Validation("Conditions", $"'{condition.Operator}' needs a property.");
            }

            if (condition.Operator == RuleOperators.Regex)
            {
                try
                {
                    _ = new Regex(condition.Value ?? string.Empty, RegexOptions.None, TimeSpan.FromMilliseconds(200));
                }
                catch (ArgumentException)
                {
                    return Error.Validation("Conditions", "The pattern is not a valid regular expression.");
                }
            }
        }

        if (request.Actions.Any(a => string.IsNullOrWhiteSpace(a.PropertyKey) || a.Template is null || a.Template.Length > 2000))
        {
            return Error.Validation("Actions", "Every action names a property and a template of up to 2000 characters.");
        }

        Rule rule;
        if (command.Id is { } id)
        {
            if (await db.Set<Rule>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) is not { } existing)
            {
                return NotFound;
            }

            rule = existing;
        }
        else
        {
            rule = new Rule { Name = string.Empty };
            db.Add(rule);
        }

        rule.Name = request.Name.Trim();
        rule.IsActive = request.IsActive;
        rule.NodeTypeKeys = request.NodeTypeKeys?.Distinct().ToList() ?? [];
        rule.Conditions = request.Conditions.ToList();
        rule.Actions = request.Actions.ToList();
        rule.SortIndex = request.SortIndex;
        return Map(rule);
    }

    public async Task<Result> HandleAsync(DeleteRule command, CancellationToken cancellationToken)
    {
        if (await db.Set<Rule>().SingleOrDefaultAsync(r => r.Id == command.Id, cancellationToken) is not { } rule)
        {
            return NotFound;
        }

        db.Remove(rule);
        return Result.Success();
    }

    public async Task<Result<RuleTestDto>> HandleAsync(TestRule query, CancellationToken cancellationToken)
    {
        if (await db.Set<Rule>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == query.Id, cancellationToken) is not { } rule)
        {
            return NotFound;
        }

        if (!await access.CanAsync(Shareme.Contracts.Graph.NodePermissions.Read, query.NodeId, cancellationToken)
            || await engine.ContextAsync(query.NodeId, new HashSet<string>(), false, false, cancellationToken) is not { } context)
        {
            return Error.NotFound("nodes.not_found", "The node does not exist.");
        }

        var outcome = await engine.EvaluateAsync(rule, context.Rule, cancellationToken);
        if (!rule.AppliesTo(context.Rule.TypeKey))
        {
            outcome.Problems.Insert(0, $"The rule does not apply to nodes of type '{context.Rule.TypeKey}'.");
        }

        return new RuleTestDto(outcome.FiltersMatched, outcome.TriggersMatched, outcome.Values, outcome.Problems);
    }

    public async Task<Result<RuleJobDto>> HandleAsync(StartRuleJob command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        if ((request.NodeIds is { Count: > 0 }) == request.FolderId.HasValue || request.NodeIds is { Count: > RuleJobRunner.MaxNodes })
        {
            return Error.Validation("NodeIds", $"Choose up to {RuleJobRunner.MaxNodes} nodes or one folder.");
        }

        var job = new RuleJob { NodeIds = request.NodeIds?.Distinct().ToList() ?? [], FolderId = request.FolderId, RuleIds = request.RuleIds?.Distinct().ToList() ?? [], CreatedAt = clock.GetUtcNow() };
        db.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<RuleJobRunner, Guid>(job.Id);
        return Map(job);
    }

    public async Task<Result<IReadOnlyList<RuleJobDto>>> HandleAsync(GetRuleJobs query, CancellationToken cancellationToken)
    {
        IReadOnlyList<RuleJobDto> runs = (await db.Set<RuleJob>().AsNoTracking().OrderByDescending(j => j.CreatedAt).Take(20).ToListAsync(cancellationToken))
            .Select(j => Map(j) with { Entries = [] }).ToList();
        return Result<IReadOnlyList<RuleJobDto>>.Success(runs);
    }

    public async Task<Result<RuleJobDto>> HandleAsync(GetRuleJob query, CancellationToken cancellationToken) =>
        await db.Set<RuleJob>().AsNoTracking().SingleOrDefaultAsync(j => j.Id == query.Id, cancellationToken) is { } job
            ? Map(job)
            : Error.NotFound("rules.job_not_found", "The run does not exist.");

    internal static RuleDto Map(Rule r) => new(r.Id, r.Name, r.IsActive, r.NodeTypeKeys, r.Conditions, r.Actions, r.SortIndex);

    internal static RuleJobDto Map(RuleJob j) => new(j.Id, j.State, j.Total, j.Matched, j.Executed, j.Skipped, j.Failed, j.Error, j.Entries, j.CreatedAt);
}

/// <summary>Applies rules to existing nodes afterwards, as the user who started the run; one node's failure does not stop the others.</summary>
internal sealed class RuleJobRunner(CoworkeeDbContext db, RuleEngine engine, NodeAccess access, ILogger<RuleJobRunner> logger) : IBackgroundJob<Guid>
{
    public const int MaxNodes = 10_000;
    private const int MaxEntries = 500;

    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (await db.Set<RuleJob>().SingleOrDefaultAsync(j => j.Id == jobId && j.State == RuleJobStates.Queued, cancellationToken) is not { } job)
        {
            return;
        }

        job.State = RuleJobStates.Running;
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var rules = await db.Set<Rule>().AsNoTracking().Where(r => job.RuleIds.Count == 0 ? r.IsActive : job.RuleIds.Contains(r.Id))
                .OrderBy(r => r.SortIndex).ThenBy(r => r.Name).ToListAsync(cancellationToken);
            var nodeIds = job.FolderId is { } folderId
                ? await db.Set<Node>().Where(n => n.Path.StartsWith(db.Set<Node>().Where(f => f.Id == folderId).Select(f => f.Path).First()) && n.Id != folderId)
                    .OrderBy(n => n.Path).Select(n => n.Id).Take(MaxNodes).ToListAsync(cancellationToken)
                : job.NodeIds;
            job.Total = nodeIds.Count;
            foreach (var nodeId in nodeIds)
            {
                await RunAsync(job, rules, nodeId, cancellationToken);
            }

            job.State = RuleJobStates.Completed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Rule run {JobId} failed.", jobId);
            job.State = RuleJobStates.Failed;
            job.Error = "The run stopped because of an internal error.";
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RunAsync(RuleJob job, List<Rule> rules, Guid nodeId, CancellationToken cancellationToken)
    {
        if (!await access.CanAsync(Shareme.Contracts.Graph.NodePermissions.Read, nodeId, cancellationToken)
            || await engine.ContextAsync(nodeId, new HashSet<string>(), false, false, cancellationToken) is not { } context)
        {
            job.Skipped++;
            Note(job, nodeId, "skipped", "Not visible to you.");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var matched = false;
        var wrote = false;
        string? problem = null;
        foreach (var rule in rules.Where(r => r.AppliesTo(context.Rule.TypeKey)))
        {
            var outcome = await engine.EvaluateAsync(rule, context.Rule, cancellationToken);
            matched |= outcome.FiltersMatched && outcome.TriggersMatched;
            if (!outcome.FiltersMatched || outcome.Values.Count == 0)
            {
                continue;
            }

            if (await engine.WriteAsync(context.Rule.Node, outcome.Values, cancellationToken) is { } failure)
            {
                problem = $"{rule.Name}: {failure}";
                break;
            }

            wrote = true;
            context = await engine.ContextAsync(nodeId, new HashSet<string>(), false, false, cancellationToken) ?? context;
        }

        if (problem is not null)
        {
            job.Failed++;
            Note(job, nodeId, "failed", problem);
        }
        else if (wrote)
        {
            job.Executed++;
            Note(job, nodeId, "executed", null);
        }
        else
        {
            job.Skipped++;
        }

        if (matched)
        {
            job.Matched++;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Note(RuleJob job, Guid nodeId, string result, string? message)
    {
        if (job.Entries.Count < MaxEntries)
        {
            job.Entries.Add(new RuleJobEntryDto(nodeId, result, message is { Length: > 500 } ? message[..500] : message));
        }
    }
}
