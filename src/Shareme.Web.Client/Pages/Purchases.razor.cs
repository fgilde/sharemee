using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Drm;
using Shareme.Web.Client.Drm;

namespace Shareme.Web.Client.Pages;

public partial class Purchases
{
    [Inject] private IDrmApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<PurchaseDto> _purchases = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _purchases = await Api.GetPurchasesAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }
}
