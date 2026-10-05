using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Graph;
using Shareme.Web.Client.Graph;

namespace Shareme.Web.Client.Pages;

public partial class NodeTypesAdmin
{
    [Inject] private ISharemeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Shareme.Web.Client.Search.ISearchApi SearchApi { get; set; } = null!;

    private IReadOnlyList<Shareme.Contracts.Search.ReindexRunDto> _runs = [];

    private async Task ReindexAsync()
    {
        try
        {
            await SearchApi.StartReindexAsync();

            // follows the run for a while; the list stays correct on the next visit anyway
            for (var poll = 0; poll < 60; poll++)
            {
                _runs = await SearchApi.GetReindexRunsAsync();
                StateHasChanged();
                if (_runs is [{ State: not ("queued" or "running") }, ..])
                {
                    break;
                }

                await Task.Delay(2000);
            }
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private IReadOnlyList<NodeTypeDto> _types = [];
    private string? _key, _name, _icon;

    protected override async Task OnInitializedAsync()
    {
        _types = await Api.GetNodeTypesAsync();
        try
        {
            _runs = await SearchApi.GetReindexRunsAsync();
        }
        catch (ApiException)
        {
            // only administrators see the runs
        }
    }

    private async Task CreateAsync()
    {
        try
        {
            await Api.CreateNodeTypeAsync(new CreateNodeTypeRequest(_key ?? string.Empty, _name ?? string.Empty, _icon));
            _key = _name = _icon = null;
            _types = await Api.GetNodeTypesAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message, Severity.Error);
        }
    }
}
