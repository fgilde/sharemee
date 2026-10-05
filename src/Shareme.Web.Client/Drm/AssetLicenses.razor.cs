using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using Shareme.Contracts.Drm;

namespace Shareme.Web.Client.Drm;

public partial class AssetLicenses
{
    [Inject] private IDrmApi Api { get; set; } = null!;

    private IReadOnlyList<LicenseDto> _licenses = [];

    [Parameter, EditorRequired] public Guid AssetId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            _licenses = await Api.GetAssetLicensesAsync(AssetId);
        }
        catch (ApiException)
        {
            _licenses = [];
        }
    }
}
