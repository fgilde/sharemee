using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components.Forms;
using Shareme.Contracts.Assets;
using Shareme.Web.Client.Graph;

namespace Shareme.Web.Client.Assets;

/// <summary>Uploads files one after another in chunks, retries failed chunks and waits for the server to finish each file.</summary>
public sealed class UploadQueue(ISharemeApi api)
{
    private const int Attempts = 3;
    private const long MaxFileSize = 20L * 1024 * 1024 * 1024;

    private readonly List<UploadItem> _items = [];
    private bool _running;

    public event Action? Changed;

    public IReadOnlyList<UploadItem> Items => _items;

    /// <summary>Time to wait before retry n (1 based) and between status polls; tests shorten it.</summary>
    public Func<int, TimeSpan> Delay { get; set; } = attempt => TimeSpan.FromSeconds(Math.Min(10, attempt));

    public void Enqueue(IEnumerable<IBrowserFile> files, Guid? folderId, Guid? assetId = null)
    {
        _items.AddRange(files.Select(file => new UploadItem(file, folderId, assetId)));
        Changed?.Invoke();
        if (!_running)
        {
            _ = RunAsync();
        }
    }

    public void ClearFinished()
    {
        _items.RemoveAll(i => i.State is UploadItemStates.Done or UploadItemStates.Failed);
        Changed?.Invoke();
    }

    /// <summary>Completes when every queued file has finished; mainly for tests.</summary>
    public async Task WhenIdleAsync()
    {
        while (_running || _items.Any(i => i.State == UploadItemStates.Queued))
        {
            await Task.Delay(20);
        }
    }

    private async Task RunAsync()
    {
        _running = true;
        try
        {
            while (_items.FirstOrDefault(i => i.State == UploadItemStates.Queued) is { } item)
            {
                await UploadAsync(item);
            }
        }
        finally
        {
            _running = false;
        }
    }

    private async Task UploadAsync(UploadItem item)
    {
        try
        {
            Set(item, UploadItemStates.Uploading);
            var session = await api.StartUploadAsync(new StartUploadRequest(item.Name, item.Size, item.AssetId is null ? item.FolderId : null, item.AssetId));
            var buffer = new byte[session.ChunkSize];
            await using var stream = item.File.OpenReadStream(MaxFileSize);
            for (var index = 0; index < session.ChunkCount; index++)
            {
                var length = await FillAsync(stream, buffer);
                await RetryAsync(() => api.PutChunkAsync(session.Id, index, buffer, length));
                item.Progress = (index + 1) / (double)session.ChunkCount;
                Changed?.Invoke();
            }

            Set(item, UploadItemStates.Processing);
            var state = await api.CompleteUploadAsync(session.Id);
            for (var poll = 1; state.State == UploadStates.Completing; poll++)
            {
                await Task.Delay(Delay(Math.Min(poll, 2)));
                state = await api.GetUploadAsync(session.Id);
            }

            item.AssetId ??= state.AssetId;
            item.Error = state.Error;
            Set(item, state.State == UploadStates.Completed ? UploadItemStates.Done : UploadItemStates.Failed);
        }
        catch (ApiException exception)
        {
            item.Error = exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message;
            Set(item, UploadItemStates.Failed);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            item.Error = exception.Message;
            Set(item, UploadItemStates.Failed);
        }
    }

    private async Task RetryAsync(Func<Task> send)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await send();
                return;
            }
            catch (Exception exception) when (attempt < Attempts && exception is HttpRequestException or ApiException { Status: >= 500 or 408 or 429 })
            {
                await Task.Delay(Delay(attempt));
            }
        }
    }

    private void Set(UploadItem item, string state)
    {
        item.State = state;
        Changed?.Invoke();
    }

    private static async Task<int> FillAsync(Stream stream, byte[] buffer)
    {
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total))) > 0)
        {
            total += read;
        }

        return total;
    }
}

public static class UploadItemStates
{
    public const string Queued = "queued";
    public const string Uploading = "uploading";
    public const string Processing = "processing";
    public const string Done = "done";
    public const string Failed = "failed";
}

public sealed class UploadItem(IBrowserFile file, Guid? folderId, Guid? assetId)
{
    internal IBrowserFile File { get; } = file;

    public string Name => File.Name;

    public long Size => File.Size;

    public Guid? FolderId { get; } = folderId;

    /// <summary>The asset for a new version, or the asset that was created once done.</summary>
    public Guid? AssetId { get; set; } = assetId;

    public double Progress { get; set; }

    public string State { get; set; } = UploadItemStates.Queued;

    public string? Error { get; set; }
}
