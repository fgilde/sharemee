using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Workspaces;
using Shareme.Web.Client.Workspaces;

namespace Shareme.Web.Client.Pages;

public partial class PublicShare
{
    [Inject] private IWorkspacesApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private PublicShareDto? _share;
    private PublicShareItemDto? _selected;
    private bool _failed;

    [Parameter] public string Token { get; set; } = string.Empty;

    private IEnumerable<string> Bundles => (_share!.AllowOriginals ? new[] { DownloadKinds.Original } : []).Concat(_share.RenditionKinds.Where(k => k != DerivationKinds.Thumbnail));

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            _share = await Api.OpenShareAsync(Token);
        }
        catch (ApiException)
        {
            _failed = true;
        }
    }

    private static string? Thumbnail(PublicShareItemDto item) =>
        item.AvailableKinds.Contains(DerivationKinds.Thumbnail) ? DerivationKinds.Thumbnail : item.AvailableKinds.Contains(DerivationKinds.Preview) ? DerivationKinds.Preview : null;

    private async Task ZipAsync(string kind)
    {
        try
        {
            var package = await Api.PackShareAsync(Token, kind);
            Nav.NavigateTo(package.Url.TrimStart('/'), forceLoad: true);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }
}
