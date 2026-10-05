using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Web.Client.Assistant;

namespace Shareme.Web.Client.Pages;

public partial class AiToolCalls
{
    [Inject] private IAssistantApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private const int PageSize = 50;
    private Coworkee.Contracts.PagedResult<AiToolCallDto>? _page;
    private int _pageNumber = 1;
    private string? _search;

    protected override Task OnInitializedAsync() => LoadAsync();

    private Task ReloadAsync()
    {
        _pageNumber = 1;
        return LoadAsync();
    }

    private Task PageAsync(int page)
    {
        _pageNumber = page;
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _page = await Api.GetToolCallsAsync(_pageNumber, PageSize, _search);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }
}
