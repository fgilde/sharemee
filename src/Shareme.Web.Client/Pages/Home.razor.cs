using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;
using Shareme.Web.Client.Knowledge;

namespace Shareme.Web.Client.Pages;

public partial class Home
{
    [Inject] private IIntranetApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<string> _widgets = [];
    private IReadOnlyList<NewsSummaryDto> _news = [];
    private IReadOnlyList<NodeDto> _favorites = [], _recent = [];
    private IReadOnlyList<HomeTaskDto> _tasks = [];
    private bool _customizing;
    private string? _term;

    [CascadingParameter] private Task<AuthenticationState>? Auth { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Auth is null || !((await Auth).User.Identity?.IsAuthenticated ?? false))
        {
            return;
        }

        try
        {
            _widgets = [.. (await Api.GetLayoutAsync()).Widgets];
            await LoadWidgetsAsync();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or System.Text.Json.JsonException)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private async Task LoadWidgetsAsync()
    {
        // only what the layout shows is fetched, all at once
        var news = _widgets.Contains(HomeWidgets.News) ? Api.GetFeedAsync() : null;
        var favorites = _widgets.Contains(HomeWidgets.Favorites) ? Api.GetFavoritesAsync() : null;
        var recent = _widgets.Contains(HomeWidgets.Recent) ? Api.GetRecentAsync() : null;
        var tasks = _widgets.Contains(HomeWidgets.Tasks) || _widgets.Contains(HomeWidgets.Due) ? Api.GetTasksAsync() : null;
        _news = news is null ? [] : await news;
        _favorites = favorites is null ? [] : await favorites;
        _recent = recent is null ? [] : await recent;
        _tasks = tasks is null ? [] : await tasks;
    }

    private static string Title(string widget) => widget switch
    {
        HomeWidgets.News => "News",
        HomeWidgets.Favorites => "Favorites",
        HomeWidgets.Recent => "Recently changed",
        HomeWidgets.Tasks => "My tasks",
        HomeWidgets.Due => "Due soon",
        _ => "Search",
    };

    private void Toggle(string widget, bool on)
    {
        if (on && !_widgets.Contains(widget)) _widgets.Add(widget);
        if (!on) _widgets.Remove(widget);
    }

    private void Move(string widget, int by)
    {
        var index = _widgets.IndexOf(widget);
        var target = Math.Clamp(index + by, 0, _widgets.Count - 1);
        (_widgets[index], _widgets[target]) = (_widgets[target], _widgets[index]);
    }

    private async Task SaveAsync(bool forTenant)
    {
        try
        {
            await Api.SaveLayoutAsync(_widgets, forTenant);
            _customizing = false;
            await LoadWidgetsAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task ResetAsync()
    {
        try
        {
            _widgets = [.. (await Api.SaveLayoutAsync(null)).Widgets];
            _customizing = false;
            await LoadWidgetsAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private Task SearchKeyAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter" && !string.IsNullOrWhiteSpace(_term))
        {
            Nav.NavigateTo($"/find?q={Uri.EscapeDataString(_term.Trim())}");
        }

        return Task.CompletedTask;
    }
}
