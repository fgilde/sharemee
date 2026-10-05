using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Contracts.Search;
using Shareme.Web.Client.Metadata;
using Shareme.Web.Client.Search;

namespace Shareme.Web.Client.Pages;

public partial class SearchPage
{
    [Inject] private ISearchApi Api { get; set; } = null!;

    [Inject] private IMetadataApi Metadata { get; set; } = null!;

    [Inject] private IDataApi Data { get; set; } = null!;

    [Inject] private MudBlazor.Extensions.Services.MudExFileService Files { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private string? _term;
    private bool _exact;
    private bool _allWords = true;
    private string _sort = "relevance";
    private readonly Dictionary<string, HashSet<string>> _selected = [];
    private SearchResponse? _result;
    private List<NodeDto> _items = [];
    private IReadOnlyList<SuggestionDto> _suggestions = [];
    private IReadOnlyList<SavedSearchDto> _savedSearches = [];
    private IReadOnlyList<PropertyDefinitionDto> _definitions = [];
    private SavedSearchDto? _saved;
    private string? _saveName;
    private bool _share;
    private (Guid?, string?) _loadedFor = (Guid.Empty, "\0");
    private bool _suggestionsClosed;

    [SupplyParameterFromQuery(Name = "folder")] public Guid? FolderId { get; set; }

    [SupplyParameterFromQuery(Name = "q")] public string? Query { get; set; }

    protected override Task OnInitializedAsync() => RunAsync(async () =>
    {
        _definitions = await Metadata.GetDefinitionsAsync();
        _savedSearches = await Api.GetSavedAsync();
    });

    /// <summary>Navigating to another query or folder searches again.</summary>
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedFor == (FolderId, Query))
        {
            return;
        }

        _loadedFor = (FolderId, Query);
        _term = Query;
        _selected.Clear();
        await SearchAsync(reset: true);
    }

    private bool _massEdit;

    private async Task ExportAsync()
    {
        try
        {
            var content = await Data.ExportAsync(new Shareme.Contracts.Automation.ExportRequest(NodeIds: _items.Select(i => i.Id).ToList()));
            await Files.DownloadContentAsync(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "search-metadata.xlsx");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private SearchRequest Request(string? cursor) => new(
        string.IsNullOrWhiteSpace(_term) ? null : _term.Trim(), _exact, _allWords,
        _selected.Where(s => s.Value.Count > 0).Select(s => new SearchFilterDto(s.Key, SearchOperators.Is, s.Value.ToList())).ToList(),
        [SearchFields.Type, SearchFields.AssetType, SearchFields.Extension, .. _definitions.Where(d => d.Facet).Select(d => SearchFields.Meta(d.Key))],
        _sort == "relevance" ? null : [new SearchSortDto(_sort, _sort != SearchFields.Name)],
        FolderId, Size: 50, Cursor: cursor);

    private Task SearchAsync(bool reset) => RunAsync(async () =>
    {
        _suggestions = [];
        _suggestionsClosed = true;
        var result = await Api.SearchAsync(Request(reset ? null : _result?.Cursor));
        _items = reset ? [.. result.Items] : [.. _items, .. result.Items];
        _result = result;
    });

    private Task SuggestAsync(string? text) => RunAsync(async () =>
    {
        // a search started with Enter closes the list; the debounce of the same typing must not reopen it
        if (_suggestionsClosed)
        {
            _suggestionsClosed = false;
            return;
        }

        _suggestions = text is { Length: >= 2 } ? await Api.SuggestAsync(new SuggestRequest(text, FolderId)) : [];
        await InvokeAsync(StateHasChanged);
    });

    private Task OnKeyDown(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter":
                return SearchAsync(reset: true);
            case "Escape":
                _suggestions = [];
                break;
            default:
                _suggestionsClosed = false;
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>The facets of the result, plus selected values the result no longer contains so they can be unticked.</summary>
    private IEnumerable<(string Field, IReadOnlyList<FacetValueDto> Values)> Facets()
    {
        var facets = _result?.Facets ?? new Dictionary<string, IReadOnlyList<FacetValueDto>>();
        foreach (var field in facets.Keys.Union(_selected.Where(s => s.Value.Count > 0).Select(s => s.Key)))
        {
            var values = facets.GetValueOrDefault(field) ?? [];
            var missing = (_selected.GetValueOrDefault(field) ?? []).Where(v => values.All(x => x.Value != v)).Select(v => new FacetValueDto(v, 0));
            yield return (field, [.. values, .. missing]);
        }
    }

    private Task ClearFiltersAsync()
    {
        _selected.Clear();
        return SearchAsync(reset: true);
    }

    private bool IsSelected(string field, string value) => _selected.TryGetValue(field, out var values) && values.Contains(value);

    private Task ToggleFacetAsync(string field, string value, bool on)
    {
        var values = _selected.TryGetValue(field, out var known) ? known : _selected[field] = [];
        if (on)
        {
            values.Add(value);
        }
        else
        {
            values.Remove(value);
        }

        return SearchAsync(reset: true);
    }

    private Task ApplySaved(SavedSearchDto? saved)
    {
        _saved = saved;
        if (saved is null)
        {
            return Task.CompletedTask;
        }

        var request = saved.Request;
        (_term, _exact, _allWords) = (request.Term, request.Exact, request.AllWords);
        _sort = request.Sort is [{ } first, ..] ? first.Field : "relevance";
        _selected.Clear();
        foreach (var filter in request.Filters ?? [])
        {
            _selected[filter.Field] = [.. filter.Values ?? []];
        }

        return SearchAsync(reset: true);
    }

    private Task SaveAsync() => RunAsync(async () =>
    {
        var saved = await Api.SaveAsync(null, new SaveSearchRequest(_saveName!.Trim(), Request(null), _share));
        _savedSearches = await Api.GetSavedAsync();
        _saved = _savedSearches.FirstOrDefault(s => s.Id == saved.Id);
        _saveName = null;
        Snackbar.Add("Search saved.", Severity.Success);
    });

    private Task DeleteSavedAsync() => RunAsync(async () =>
    {
        await Api.DeleteSavedAsync(_saved!.Id);
        _saved = null;
        _savedSearches = await Api.GetSavedAsync();
    });

    private void Open(SuggestionDto suggestion) => Nav.NavigateTo(suggestion.Semantic switch { "asset" => $"/assets/{suggestion.Id}", "workspace" => $"/workspaces/{suggestion.Id}", "page" => $"/pages/{suggestion.Id}", _ => $"/browse/{suggestion.Id}" });

    private string FacetTitle(string field) => field switch
    {
        SearchFields.Type => "Type",
        SearchFields.AssetType => "Kind of file",
        SearchFields.Extension => "Extension",
        _ when field.StartsWith(SearchFields.MetaPrefix, StringComparison.Ordinal) => _definitions.FirstOrDefault(d => SearchFields.Meta(d.Key) == field)?.Name ?? field,
        _ => field,
    };

    private static string Link(NodeDto node) => node.Semantic switch
    {
        "asset" => $"/assets/{node.Id}", "workspace" => $"/workspaces/{node.Id}", "task" => $"/workspaces/{node.ParentId}", "page" => $"/pages/{node.Id}", _ => $"/browse/{node.Id}",
    };

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message, Severity.Error);
        }
    }
}
