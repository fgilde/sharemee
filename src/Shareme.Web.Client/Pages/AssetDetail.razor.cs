using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Web.Client.Assets;
using Shareme.Web.Client.Graph;

namespace Shareme.Web.Client.Pages;

public partial class AssetDetail
{
    [Inject] private ISharemeApi Api { get; set; } = null!;

    [Inject] private IRenditionsApi Renditions { get; set; } = null!;

    [Inject] private IDownloadsApi Downloads { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private AssetDto? _asset;
    private NodeDetailDto? _detail;
    private IReadOnlyList<EdgeDto> _edges = [];
    private List<BreadcrumbItem> _breadcrumb = [];
    private DownloadLinkDto? _link;
    private IReadOnlyList<DerivationTypeDto> _types = [];
    private bool _showOriginal;
    private bool _cropping;

    [Parameter] public Guid Id { get; set; }

    private DerivationDto? Preview => _asset?.Derivations.FirstOrDefault(d => d.Kind == DerivationKinds.Preview);

    private DerivationDto? Media => _asset?.Derivations.FirstOrDefault(d => d.Kind == DerivationKinds.Media);

    /// <summary>Only originals that cannot run anything are shown in the page.</summary>
    private bool CanView => _asset is { CanDownload: true, CurrentVersion.MimeType: { } mimeType } && InlineTypes.All.Contains(mimeType);

    private IEnumerable<(string Kind, string Name, DerivationTypeDto? Type)> RenditionRows =>
        _asset!.Derivations.Where(d => DerivationKinds.System.Contains(d.Kind)).Select(d => (d.Kind, d.Kind, (DerivationTypeDto?)null))
            .Concat(_types.Where(t => t.AssetTypeKeys.Count == 0 || t.AssetTypeKeys.Contains(_asset.AssetType)).Select(t => (t.Key, t.Name, (DerivationTypeDto?)t)));

    private IReadOnlyList<DerivationTypeDto> CropTypes => _types.Where(t => t.AssetTypeKeys.Count == 0 || t.AssetTypeKeys.Contains(_asset!.AssetType)).ToList();

    private string MediaType => _asset?.CurrentVersion?.MimeType.StartsWith("audio/", StringComparison.Ordinal) == true ? "audio/mpeg" : "video/mp4";

    private string PreviewText => Preview?.State switch
    {
        DerivationStates.Pending => "The preview is being prepared…",
        DerivationStates.Failed => "No preview could be created.",
        _ when _asset?.Status == AssetStatuses.Importing => "The file is being imported…",
        _ => "No preview for this file type.",
    };

    protected override async Task OnParametersSetAsync()
    {
        _asset = null;
        _link = null;
        _cropping = false;
        await ReloadAsync();

        // a page, text or mesh shows best as itself; images and media start with their renditions
        _showOriginal = CanView && (_asset?.CurrentVersion?.MimeType is "application/pdf" or "text/plain" or "text/csv" || _asset?.AssetType == AssetTypeKeys.Model);
        try
        {
            _types = await Renditions.GetTypesAsync();
        }
        catch (ApiException)
        {
            _types = [];
        }
    }

    private async Task ReloadAsync()
    {
        try
        {
            _asset = await Api.GetAssetAsync(Id);
            _detail = await Api.GetNodeAsync(Id);
            _edges = await Api.GetEdgesAsync(Id);
            _breadcrumb = _detail.Breadcrumb.Select((c, i) => new BreadcrumbItem(c.Name, i == _detail.Breadcrumb.Count - 1 ? null : $"/browse/{c.Id}", disabled: i == _detail.Breadcrumb.Count - 1)).ToList();
        }
        catch (ApiException exception) when (exception.Status is 403 or 404)
        {
            Snackbar.Add("This asset is not available.", Severity.Warning);
            Nav.NavigateTo("/browse");
            return;
        }

        await InvokeAsync(StateHasChanged);
    }

    private Task UploadRenditionAsync(string kind, IBrowserFile? file) => file is null ? Task.CompletedTask : RunAsync(async () =>
    {
        await using var content = file.OpenReadStream(100L * 1024 * 1024);
        await Renditions.UploadAsync(_asset!.Id, kind, content);
    });

    private Task ExtractAsync() => RunAsync(async () =>
    {
        var run = await Downloads.ExtractAsync(_asset!.Id, new ExtractArchiveRequest());
        Snackbar.Add("The archive is being unpacked into this folder.", Severity.Info);

        // follows the run for a while and reports how it ended
        for (var poll = 0; poll < 120 && run.State is ExtractionStates.Queued or ExtractionStates.Running; poll++)
        {
            await Task.Delay(2000);
            run = await Downloads.GetExtractionAsync(run.Id);
        }

        if (run.State == ExtractionStates.Completed)
        {
            Snackbar.Add($"Unpacked {run.Extracted} files" + (run.Failed + run.Filtered > 0 ? $", {run.Filtered} skipped, {run.Failed} refused: {string.Join("; ", run.Problems.Take(3))}" : "."), run.Failed > 0 ? Severity.Warning : Severity.Success);
        }
        else if (run.State == ExtractionStates.Failed)
        {
            Snackbar.Add(run.Error ?? "The archive could not be unpacked.", Severity.Error);
        }
    });

    private Task CreateLinkAsync() => RunAsync(async () => _link = await Api.CreateDownloadLinkAsync(_asset!.Id));

    private Task SetInheritanceAsync(bool interrupt) => RunAsync(() => Api.SetInheritanceAsync(_asset!.Id, interrupt));

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            await ReloadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You do not have permission for this." : exception.Message, exception.Status == 403 ? Severity.Warning : Severity.Error);
        }
    }

    private static Color StatusColor(string status) => status switch
    {
        AssetStatuses.Ready => Color.Success,
        AssetStatuses.Duplicate => Color.Warning,
        AssetStatuses.Failed => Color.Error,
        _ => Color.Info,
    };

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };
}
