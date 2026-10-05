using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using Shareme.Contracts.Assets;
using Shareme.Web.Client.Assets;

namespace Shareme.Web.Client.Pages;

public partial class RenditionsAdmin
{
    [Inject] private IRenditionsApi Api { get; set; } = null!;

    [Inject] private IDownloadsApi Downloads { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<FileNamingDto> _namings = [];
    private Guid? _namingId;
    private NamingForm _naming = new();

    private Task SaveNamingAsync() => RunAsync(async () =>
    {
        await Downloads.SaveNamingAsync(_namingId, new FileNamingRequest(_naming.Name ?? string.Empty, _naming.Format, _naming.Casing, _naming.IsDefault));
        _namingId = null;
        _naming = new();
    });

    private sealed class NamingForm
    {
        public string? Name { get; set; }
        public string Format { get; set; } = FileNamingTokens.DefaultFormat;
        public string Casing { get; set; } = FileNamingCasings.None;
        public bool IsDefault { get; set; }
    }

    private IReadOnlyList<DerivationTypeDto> _types = [];
    private IReadOnlyList<WatermarkDto> _watermarks = [];
    private Guid? _typeId, _watermarkId;
    private TypeForm _type = new();
    private WatermarkForm _watermark = new();

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _types = await Api.GetTypesAsync();
            _watermarks = await Api.GetWatermarksAsync();
            _namings = await Downloads.GetNamingsAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You may not manage renditions." : exception.Message, Severity.Warning);
        }
    }

    private static IEnumerable<string> Modes(DerivationTypeDto type)
    {
        if (type.IsDefault) yield return "always";
        if (type.IsOnDemand) yield return "on request";
        if (type.AllowManualUpload) yield return "by hand";
    }

    private void Edit(DerivationTypeDto type)
    {
        _typeId = type.Id;
        _type = new TypeForm
        {
            Key = type.Key, Name = type.Name, Width = type.Width, Height = type.Height, Format = type.Format, Quality = type.Quality, IsDefault = type.IsDefault,
            IsOnDemand = type.IsOnDemand, AllowManualUpload = type.AllowManualUpload, WatermarkId = type.WatermarkId, AssetTypes = string.Join(", ", type.AssetTypeKeys),
        };
    }

    private void Edit(WatermarkDto watermark)
    {
        _watermarkId = watermark.Id;
        _watermark = new WatermarkForm
        {
            Name = watermark.Name, Position = watermark.Position, Opacity = watermark.Opacity, ScalePercent = watermark.ScalePercent,
            TriggerProperty = watermark.TriggerProperty, TriggerValue = watermark.TriggerValue,
        };
    }

    private Task SaveTypeAsync() => RunAsync(async () =>
    {
        var keys = (_type.AssetTypes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await Api.SaveTypeAsync(_typeId, new DerivationTypeRequest(
            _type.Key ?? string.Empty, _type.Name ?? string.Empty, _type.Width, _type.Height, _type.Format, _type.Quality, _type.IsDefault, _type.IsOnDemand, _type.AllowManualUpload,
            _type.WatermarkId, keys));
        _typeId = null;
        _type = new();
    });

    private Task SaveWatermarkAsync() => RunAsync(async () =>
    {
        await Api.SaveWatermarkAsync(_watermarkId, new WatermarkRequest(
            _watermark.Name ?? string.Empty, _watermark.Position, _watermark.Opacity, _watermark.ScalePercent, _watermark.TriggerProperty, _watermark.TriggerValue));
        _watermarkId = null;
        _watermark = new();
    });

    private Task UploadImageAsync(Guid id, IBrowserFile? file) => file is null ? Task.CompletedTask : RunAsync(async () =>
    {
        await using var content = file.OpenReadStream(5L * 1024 * 1024);
        await Api.SetWatermarkImageAsync(id, content);
    });

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private sealed class TypeForm
    {
        public string? Key { get; set; }
        public string? Name { get; set; }
        public int Width { get; set; } = 1200;
        public int Height { get; set; } = 1200;
        public string Format { get; set; } = DerivationFormats.WebP;
        public int Quality { get; set; } = 85;
        public bool IsDefault { get; set; }
        public bool IsOnDemand { get; set; } = true;
        public bool AllowManualUpload { get; set; }
        public Guid? WatermarkId { get; set; }
        public string? AssetTypes { get; set; }
    }

    private sealed class WatermarkForm
    {
        public string? Name { get; set; }
        public string Position { get; set; } = "southeast";
        public int Opacity { get; set; } = 50;
        public int ScalePercent { get; set; } = 25;
        public string? TriggerProperty { get; set; }
        public string? TriggerValue { get; set; }
    }
}
