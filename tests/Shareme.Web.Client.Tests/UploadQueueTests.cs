using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components.Forms;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shareme.Contracts.Assets;
using Shareme.Web.Client.Assets;
using Shareme.Web.Client.Graph;

namespace Shareme.Web.Client.Tests;

public sealed class UploadQueueTests
{
    private readonly ISharemeApi _api = Substitute.For<ISharemeApi>();
    private readonly Guid _folder = Guid.CreateVersion7();
    private readonly Guid _session = Guid.CreateVersion7();
    private readonly Guid _asset = Guid.CreateVersion7();

    [Fact]
    public async Task Files_are_sent_in_chunks_failed_chunks_are_retried_and_the_result_is_awaited()
    {
        _api.StartUploadAsync(Arg.Any<StartUploadRequest>(), Arg.Any<CancellationToken>()).Returns(Session(UploadStates.Open, chunkSize: 4, chunkCount: 3));
        var calls = 0;
        _api.PutChunkAsync(_session, 1, Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1 ? Task.FromException(new HttpRequestException("flaky")) : Task.CompletedTask);
        _api.CompleteUploadAsync(_session, Arg.Any<CancellationToken>()).Returns(Session(UploadStates.Completing));
        _api.GetUploadAsync(_session, Arg.Any<CancellationToken>()).Returns(Session(UploadStates.Completing), Session(UploadStates.Completed));
        var queue = new UploadQueue(_api) { Delay = _ => TimeSpan.Zero };

        queue.Enqueue([new FakeFile("photo.png", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10])], _folder);
        await queue.WhenIdleAsync();

        var item = queue.Items.Single();
        item.State.ShouldBe(UploadItemStates.Done);
        item.AssetId.ShouldBe(_asset);
        item.Progress.ShouldBe(1);
        await _api.Received(1).StartUploadAsync(Arg.Is<StartUploadRequest>(r => r.FileName == "photo.png" && r.Size == 10 && r.TargetFolderId == _folder), Arg.Any<CancellationToken>());
        await _api.Received(2).PutChunkAsync(_session, 1, Arg.Any<byte[]>(), 4, Arg.Any<CancellationToken>());
        await _api.Received(1).PutChunkAsync(_session, 2, Arg.Any<byte[]>(), 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejected_files_end_as_failed_with_the_reason_and_the_queue_goes_on()
    {
        _api.StartUploadAsync(Arg.Is<StartUploadRequest>(r => r.FileName == "setup.exe"), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(400, "validation", new Dictionary<string, string[]> { ["FileName"] = ["Files of type '.exe' cannot be uploaded."] }));
        _api.StartUploadAsync(Arg.Is<StartUploadRequest>(r => r.FileName == "ok.txt"), Arg.Any<CancellationToken>()).Returns(Session(UploadStates.Open, chunkSize: 8, chunkCount: 1));
        _api.CompleteUploadAsync(_session, Arg.Any<CancellationToken>()).Returns(Session(UploadStates.Failed, error: "The content is broken."));
        var queue = new UploadQueue(_api) { Delay = _ => TimeSpan.Zero };

        queue.Enqueue([new FakeFile("setup.exe", [1]), new FakeFile("ok.txt", [1, 2])], _folder);
        await queue.WhenIdleAsync();

        queue.Items[0].State.ShouldBe(UploadItemStates.Failed);
        queue.Items[0].Error.ShouldBe("Files of type '.exe' cannot be uploaded.");
        queue.Items[1].State.ShouldBe(UploadItemStates.Failed);
        queue.Items[1].Error.ShouldBe("The content is broken.");
    }

    [Fact]
    public async Task An_unexpected_error_fails_only_that_file()
    {
        _api.StartUploadAsync(Arg.Any<StartUploadRequest>(), Arg.Any<CancellationToken>()).Returns(Session(UploadStates.Open, chunkSize: 8, chunkCount: 1));
        _api.PutChunkAsync(_session, 0, Arg.Any<byte[]>(), 1, Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("boom")));
        _api.CompleteUploadAsync(_session, Arg.Any<CancellationToken>()).Returns(Session(UploadStates.Completed));
        var queue = new UploadQueue(_api) { Delay = _ => TimeSpan.Zero };

        queue.Enqueue([new FakeFile("one.txt", [1]), new FakeFile("two.txt", [1, 2])], _folder);
        await queue.WhenIdleAsync();

        queue.Items[0].State.ShouldBe(UploadItemStates.Failed);
        queue.Items[0].Error.ShouldBe("boom");
        queue.Items[1].State.ShouldBe(UploadItemStates.Done);
    }

    private UploadSessionDto Session(string state, int chunkSize = 4, int chunkCount = 3, string? error = null) =>
        new(_session, chunkSize, chunkCount, [], DateTimeOffset.UtcNow.AddHours(1), state, _asset,
            state == UploadStates.Completed ? new UploadResultDto(_asset, Guid.CreateVersion7(), 1, []) : null, error);

    private sealed class FakeFile(string name, byte[] content) : IBrowserFile
    {
        public string Name => name;

        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;

        public long Size => content.Length;

        public string ContentType => "application/octet-stream";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream(content);
    }
}
