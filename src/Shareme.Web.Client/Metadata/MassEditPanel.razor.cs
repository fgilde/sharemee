using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Metadata;
using Shareme.Contracts.Search;

namespace Shareme.Web.Client.Metadata;

public partial class MassEditPanel
{
    [Inject] private IMetadataApi Metadata { get; set; } = null!;

    [Inject] private IDataApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private static readonly string[] Strategies = [MassEditStrategies.Overwrite, MassEditStrategies.Merge, MassEditStrategies.Delete];
    private IReadOnlyList<PropertyDefinitionDto> _definitions = [];
    private IReadOnlyList<DataLanguageDto> _languages = [];
    private readonly List<Row> _rows = [new()];
    private bool _dryRun;

    /// <summary>The nodes to change; otherwise every hit of <see cref="Search"/>.</summary>
    [Parameter] public IReadOnlyList<Guid>? NodeIds { get; set; }

    [Parameter] public SearchRequest? Search { get; set; }

    [Parameter] public EventCallback Closed { get; set; }

    private string Scope => NodeIds is { } ids ? $"{ids.Count} selected" : "all hits";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _definitions = (await Metadata.GetDefinitionsAsync()).Where(d => d.Editable && !d.IsSystem && !d.HasContext).ToList();
            _languages = await Metadata.GetLanguagesAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private PropertyDefinitionDto? Definition(Row row) => _definitions.FirstOrDefault(d => d.Key == row.Key);

    private async Task StartAsync()
    {
        var mutations = _rows.Where(r => r.Key is not null).Select(r => new PropertyMutation(
            r.Key!, r.Strategy, Texts: r.Strategy == MassEditStrategies.Delete ? null : Texts(r),
            Lang: Definition(r)?.Localizable == true ? r.Lang ?? _languages.FirstOrDefault(l => l.IsDefault)?.Code : null)).ToList();
        try
        {
            var job = await Api.MassEditAsync(new MassEditRequest(mutations, NodeIds, NodeIds is null ? Search : null, _dryRun));
            Nav.NavigateTo($"/data-jobs/{job.Id}");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    // only multi valued properties take several values, so a ";" in a single text stays
    private string[] Texts(Row row) => Definition(row) is { } definition && (definition.MultiValue || definition.ListMode == ListModes.Multi)
        ? (row.Text ?? string.Empty).Split(TableColumns.Separator.Trim(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : [row.Text ?? string.Empty];

    private sealed class Row
    {
        public string? Key { get; set; }
        public string Strategy { get; set; } = MassEditStrategies.Overwrite;
        public string? Text { get; set; }
        public string? Lang { get; set; }
    }
}
