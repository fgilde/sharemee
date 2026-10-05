using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Shareme.Web.Client.Knowledge;

public partial class FavoriteButton
{
    [Inject] private IIntranetApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private bool _on;

    [Parameter, EditorRequired] public Guid NodeId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            var asked = NodeId;
            var on = await Api.IsFavoriteAsync(asked);
            if (asked == NodeId)
            {
                _on = on;
            }
        }
        catch (ApiException)
        {
            _on = false;
        }
    }

    private async Task ToggleAsync(bool on)
    {
        try
        {
            await Api.SetFavoriteAsync(NodeId, on);
            _on = on;
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }
}
