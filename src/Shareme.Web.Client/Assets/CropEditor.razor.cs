using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components;
using Shareme.Contracts.Assets;

namespace Shareme.Web.Client.Assets;

public partial class CropEditor
{
    [Inject] private IRenditionsApi Renditions { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    private const string AsVariant = "variant";

    internal static readonly (string Label, double? Ratio)[] Ratios = [("Free", null), ("1:1", 1), ("4:3", 4.0 / 3), ("3:2", 1.5), ("16:9", 16.0 / 9), ("3:4", 0.75), ("9:16", 9.0 / 16)];

    private System.Drawing.RectangleF? _selected;
    private string _ratio = "Free";
    private string _target = AsVariant;
    private string? _name;

    [Parameter, EditorRequired] public AssetDto Asset { get; set; } = null!;

    /// <summary>Rendition types of the asset type that a crop may feed.</summary>
    [Parameter] public IReadOnlyList<DerivationTypeDto> Types { get; set; } = [];

    [Parameter] public EventCallback OnClose { get; set; }

    private DerivationDto? Preview => Asset.Derivations.FirstOrDefault(d => d.Kind == DerivationKinds.Preview);

    /// <summary>The selection in fractions of the preview, narrowed to the chosen ratio.</summary>
    internal CropArea? Area => _selected is { } s && Preview is { Width: > 0, Height: > 0 } p ? Fit(s, p.Width!.Value, p.Height!.Value, Ratios.First(r => r.Label == _ratio).Ratio) : null;

    private string SizeText => Area is { } a && Preview is { Width: { } w, Height: { } h }
        ? $"{Math.Round(a.Width * w)} × {Math.Round(a.Height * h)} px of the preview, from {Math.Round(a.Left * 100)} % / {Math.Round(a.Top * 100)} %"
        : "Nothing selected yet.";

    internal void OnSelected(ImageAreaSelectedArgs args)
    {
        _selected = args.Area;
        StateHasChanged();
    }

    internal static CropArea? Fit(System.Drawing.RectangleF area, int width, int height, double? ratio)
    {
        var left = Math.Clamp(area.X, 0, width);
        var top = Math.Clamp(area.Y, 0, height);
        double w = Math.Clamp(Math.Min(area.Right, width) - left, 0, width - left), h = Math.Clamp(Math.Min(area.Bottom, height) - top, 0, height - top);
        if (w < 1 || h < 1)
        {
            return null;
        }

        if (ratio is { } r)
        {
            var (fitted, fittedHeight) = w / h > r ? (h * r, h) : (w, w / r);
            left += (float)((w - fitted) / 2);
            top += (float)((h - fittedHeight) / 2);
            (w, h) = (fitted, fittedHeight);
        }

        return new CropArea(left / width, top / height, w / width, h / height);
    }

    private async Task ApplyAsync()
    {
        if (Area is not { } area)
        {
            return;
        }

        try
        {
            if (_target == AsVariant)
            {
                var variant = await Renditions.CreateVariantAsync(Asset.Id, new CropVariantRequest(area, _name));
                Snackbar.Add("The variant is being created.", Severity.Success);
                Nav.NavigateTo($"/assets/{variant.AssetId}");
            }
            else
            {
                await Renditions.CropAsync(Asset.Id, _target, area);
                Snackbar.Add("The rendition is being created from the crop.", Severity.Success);
                await OnClose.InvokeAsync();
            }
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You do not have permission for this." : exception.Message, Severity.Warning);
        }
    }
}
