using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Metadata;
using Shareme.Web.Client.Metadata;

namespace Shareme.Web.Client.Pages;

public partial class AiAdmin
{
    [Inject] private IAiApi Api { get; set; } = null!;

    [Inject] private IMetadataApi Metadata { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<ExtractionDefinitionDto> _definitions = [];
    private IReadOnlyList<ExtractionSuggestionDto> _suggestions = [];
    private IReadOnlyList<PropertyDefinitionDto> _properties = [];
    private Guid? _id;
    private bool _hasKey;
    private Form _form = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        try
        {
            _properties = (await Metadata.GetDefinitionsAsync()).Where(d => d.Editable && !d.IsSystem && !d.HasContext).ToList();
        }
        catch (ApiException)
        {
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            _definitions = await Api.GetDefinitionsAsync();
            _suggestions = await Api.GetSuggestionsAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You may not use AI tagging." : exception.Message, Severity.Warning);
        }
    }

    private void Edit(ExtractionDefinitionDto d)
    {
        _id = d.Id;
        _hasKey = d.HasApiKey;
        _form = new Form
        {
            Name = d.Name, Provider = d.Provider, Mode = d.Mode, Endpoint = d.Endpoint, Model = d.Model, TargetKey = d.TargetKey, InputKind = d.InputKind, MinConfidence = d.MinConfidence,
            Language = d.Language, AutoApply = d.AutoApply, AssetTypes = string.Join(", ", d.AssetTypeKeys), Prompt = d.Prompt,
            Mappings = string.Join("\n", d.Mappings.Select(m => $"{m.FromValue}={m.ToValue}")),
        };
    }

    private void Reset()
    {
        _id = null;
        _hasKey = false;
        _form = new();
    }

    private Task SaveAsync() => RunAsync(async () =>
    {
        var mappings = (_form.Mappings ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries)).Where(p => p.Length == 2).Select(p => new AiMapping(p[0], p[1])).ToList();
        await Api.SaveAsync(_id, new ExtractionDefinitionRequest(
            _form.Name ?? string.Empty, _form.Provider, _form.TargetKey ?? string.Empty, _form.Mode, _form.Endpoint, _form.Model,
            string.IsNullOrEmpty(_form.ApiKey) ? null : _form.ApiKey, _form.Prompt, _form.InputKind,
            (_form.AssetTypes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), mappings, _form.MinConfidence,
            string.IsNullOrWhiteSpace(_form.Language) ? null : _form.Language.Trim(), _form.AutoApply));
        Reset();
    });

    private Task DecideAsync(Guid id, bool apply) => RunAsync(() => Api.DecideAsync(id, apply));

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

    private sealed class Form
    {
        public string? Name { get; set; }
        public string Provider { get; set; } = AiProviders.Anthropic;
        public string Mode { get; set; } = AiModes.Tags;
        public string? Endpoint { get; set; }
        public string? Model { get; set; }
        public string? ApiKey { get; set; }
        public string? TargetKey { get; set; }
        public string InputKind { get; set; } = "preview";
        public decimal MinConfidence { get; set; } = 0.5m;
        public string? Language { get; set; }
        public bool AutoApply { get; set; }
        public string? AssetTypes { get; set; }
        public string? Mappings { get; set; }
        public string? Prompt { get; set; }
    }
}
