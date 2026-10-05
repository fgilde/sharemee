using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Graph;
using Shareme.Web.Client.Graph;

namespace Shareme.Web.Client.Pages;

public partial class RecycleBin
{
    [Inject] private ISharemeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<RecycleBinItemDto> _items = [];

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync() => _items = (await Api.GetRecycleBinAsync(new PageRequest(1, 200))).Items;

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }

        await ReloadAsync();
    }
}
