using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Knowledge;
using Shareme.Web.Client.Knowledge;

namespace Shareme.Web.Client.Pages;

public partial class PageView
{
    [Inject] private IPagesApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private PageDto? _page;
    private IReadOnlyList<PageVersionDto> _versions = [];
    private IReadOnlyList<DiffLineDto>? _diff;
    private int _diffFrom;
    private bool _editing, _historyShown;
    private string? _title, _markdown, _comment;
    private string _preview = string.Empty;

    [Parameter] public Guid Id { get; set; }

    private bool _history
    {
        get => _historyShown;
        set
        {
            _historyShown = value;
            if (value)
            {
                _ = LoadVersionsAsync();
            }
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        // another page: nothing of the previous one stays
        (_editing, _diff, _versions, _historyShown) = (false, null, [], false);
        try
        {
            _page = await Api.GetAsync(Id);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            _versions = await Api.GetVersionsAsync(Id);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }

        StateHasChanged();
    }

    private void Edit()
    {
        (_title, _markdown, _comment, _editing, _preview) = (_page!.Title, _page.Markdown, null, true, _page.Html);
    }

    private async Task PreviewAsync()
    {
        try
        {
            _preview = await Api.PreviewAsync(_markdown ?? string.Empty);
        }
        catch (ApiException)
        {
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            _page = await Api.UpdateAsync(Id, new PageUpdateRequest(_title ?? string.Empty, _markdown ?? string.Empty, _page!.Version, _comment));
            _editing = false;
            if (_history)
            {
                await LoadVersionsAsync();
            }
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, exception.Status == 409 ? Severity.Warning : Severity.Error);
        }
    }

    private async Task DiffAsync(int from)
    {
        try
        {
            _diff = await Api.DiffAsync(Id, from, _page!.Version);
            _diffFrom = from;
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private async Task RestoreAsync(int number)
    {
        try
        {
            _page = await Api.RestoreAsync(Id, number, _page!.Version);
            _diff = null;
            await LoadVersionsAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private static string Style(string kind) => kind switch
    {
        DiffKinds.Added => "background:rgba(46,160,67,.2)",
        DiffKinds.Removed => "background:rgba(248,81,73,.2);text-decoration:line-through",
        _ => string.Empty,
    };

    private static string Prefix(string kind) => kind switch { DiffKinds.Added => "+ ", DiffKinds.Removed => "- ", _ => "  " };
}
