using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Drm;
using Shareme.Contracts.Metadata;
using Shareme.Web.Client.Drm;
using Shareme.Web.Client.Metadata;

namespace Shareme.Web.Client.Pages;

public partial class Licenses
{
    [Inject] private IDrmApi Api { get; set; } = null!;

    [Inject] private IMetadataApi Metadata { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<UserContextDto> _contexts = [];
    private IReadOnlyList<ContextGroupDto> _groups = [];
    private IReadOnlyList<LicenseOfferDto> _offers = [];
    private string? _term, _rights, _target;

    [SupplyParameterFromQuery(Name = "asset")] public Guid? AssetId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _contexts = await Metadata.GetContextsAsync();
            _groups = await Api.GetGroupsAsync();
        }
        catch (ApiException)
        {
        }

        await SearchAsync();
    }

    private Task TargetChanged(string? target)
    {
        _target = target;
        return SearchAsync();
    }

    private (string? Context, Guid? Group) Target() => _target switch
    {
        ['c', ':', .. var key] => (key, null),
        ['g', ':', .. var id] => (null, Guid.Parse(id)),
        _ => (null, null),
    };

    private async Task SearchAsync()
    {
        var (context, group) = Target();
        try
        {
            var rights = (_rights ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _offers = await Api.SearchOffersAsync(new OfferSearchRequest(_term, context, group, rights.Length == 0 ? null : rights, AssetId: AssetId));
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You may not see license offers." : exception.Message, Severity.Warning);
        }
    }

    private async Task BuyAsync(LicenseOfferDto offer)
    {
        var (context, group) = Target();
        try
        {
            var purchase = await Api.PurchaseAsync(new PurchaseRequest(offer.LicenseId, context, group));
            Snackbar.Add($"Bought {purchase.LicenseName}, order {purchase.OrderId}.", Severity.Success);
            Nav.NavigateTo("/purchases");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    internal static string Format(IReadOnlyList<PriceDto> prices) =>
        prices.Count == 0 ? "No price" : string.Join(" · ", prices.Select(p => $"{p.Amount:N2} {p.Currency}"));
}
