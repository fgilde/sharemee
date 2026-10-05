using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Automation;
using Shareme.Web.Client.Metadata;

namespace Shareme.Web.Client.Pages;

public partial class RulesAdmin
{
    [Inject] private IRulesApi Api { get; set; } = null!;

    [Inject] private IAiApi Ai { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<RuleDto> _rules = [];
    private IReadOnlyList<ExtractionDefinitionDto> _extractions = [];
    private IReadOnlyList<RuleJobDto> _jobs = [];
    private RuleJobDto? _report;
    private RuleTestDto? _test;
    private Guid? _ruleId;
    private string? _name, _nodeTypes, _testNode, _jobFolder;
    private int _sortIndex;
    private bool _isActive = true;
    private List<ConditionForm> _conditions = [];
    private List<ActionForm> _actions = [new()];

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _rules = await Api.GetAsync();
            _jobs = await Api.GetJobsAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You may not manage rules." : exception.Message, Severity.Warning);
            return;
        }

        try
        {
            _extractions = await Ai.GetDefinitionsAsync();
        }
        catch (ApiException)
        {
            // without the AI permission rules only write templates
        }
    }

    private static bool NeedsProperty(string op) => RuleOperators.WithValue.Contains(op) || RuleOperators.WithProperty.Contains(op);

    private void Edit(RuleDto rule)
    {
        _ruleId = rule.Id;
        _name = rule.Name;
        _nodeTypes = string.Join(", ", rule.NodeTypeKeys);
        _sortIndex = rule.SortIndex;
        _isActive = rule.IsActive;
        _conditions = rule.Conditions.Select(c => new ConditionForm { Kind = c.Kind, Operator = c.Operator, PropertyKey = c.PropertyKey, Value = c.Value }).ToList();
        _actions = rule.Actions.Select(a => new ActionForm { PropertyKey = a.PropertyKey, Template = a.Template, OnMatch = a.OnMatch, OnlyIfEmpty = a.OnlyIfEmpty, ExtractionId = a.ExtractionId }).ToList();
        _test = null;
    }

    private void Reset()
    {
        _ruleId = null;
        _name = _nodeTypes = null;
        _sortIndex = 0;
        _isActive = true;
        _conditions = [];
        _actions = [new()];
        _test = null;
    }

    private Task SaveAsync() => RunAsync(async () =>
    {
        var types = (_nodeTypes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await Api.SaveAsync(_ruleId, new RuleRequest(
            _name ?? string.Empty,
            _conditions.Select(c => new RuleCondition(c.Kind, c.Operator, NeedsProperty(c.Operator) ? c.PropertyKey : null, c.Value)).ToList(),
            _actions.Select(a => new RuleAction(a.PropertyKey ?? string.Empty, a.Template ?? string.Empty, a.OnMatch, a.OnlyIfEmpty, a.ExtractionId)).ToList(),
            types, _isActive, _sortIndex));
        Reset();
    });

    private async Task TestAsync(Guid ruleId)
    {
        if (!Guid.TryParse(_testNode, out var nodeId))
        {
            Snackbar.Add("Enter the id of a node.", Severity.Warning);
            return;
        }

        try
        {
            _test = await Api.TestAsync(ruleId, nodeId);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private Task StartJobAsync()
    {
        if (!Guid.TryParse(_jobFolder, out var folderId))
        {
            Snackbar.Add("Enter the id of a folder.", Severity.Warning);
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            await Api.StartJobAsync(new RuleJobRequest(FolderId: folderId));
            Snackbar.Add("The run started.", Severity.Success);
        });
    }

    private async Task ReportAsync(Guid id)
    {
        try
        {
            _report = await Api.GetJobAsync(id);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private sealed class ConditionForm
    {
        public string Kind { get; set; } = RuleConditionKinds.Filter;
        public string Operator { get; set; } = RuleOperators.EqualTo;
        public string? PropertyKey { get; set; }
        public string? Value { get; set; }
    }

    private sealed class ActionForm
    {
        public string? PropertyKey { get; set; }
        public string? Template { get; set; }
        public bool OnMatch { get; set; } = true;
        public bool OnlyIfEmpty { get; set; }
        public Guid? ExtractionId { get; set; }
    }
}
