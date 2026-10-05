using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace Shareme.Web.Client.Assets;

public partial class UploadPanel : IDisposable
{
    [Inject] private UploadQueue Queue { get; set; } = null!;

    [Parameter] public Guid? FolderId { get; set; }

    [Parameter] public Guid? AssetId { get; set; }

    protected override void OnInitialized() => Queue.Changed += Refresh;

    public void Dispose() => Queue.Changed -= Refresh;

    private void Refresh() => InvokeAsync(StateHasChanged);

    private MudFileUpload<IReadOnlyList<IBrowserFile>>? _upload;

    private async Task OnFilesChanged(IReadOnlyList<IBrowserFile>? files)
    {
        if (files is { Count: > 0 })
        {
            Queue.Enqueue(files, FolderId, AssetId);
            if (_upload is not null)
            {
                await _upload.ClearAsync();
            }
        }
    }

    private static string Label(UploadItem item) => item.State switch
    {
        UploadItemStates.Queued => "waiting",
        UploadItemStates.Uploading => $"{item.Progress:P0}",
        UploadItemStates.Processing => "processing",
        UploadItemStates.Done => "done",
        _ => item.Error ?? "failed",
    };
}
