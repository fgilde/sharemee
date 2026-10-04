using System.Net;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Shareme.Client;
using Shareme.Contracts.Metadata;

namespace Shareme.Api.Tests;

/// <summary>The .NET SDK against the real API: what an integration does first.</summary>
public sealed class ClientSdkTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await api.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creates_folders_uploads_files_sets_metadata_and_writes_pages()
    {
        var client = new SharemeClient(api.As(_setup.AdminUserId, _setup.TenantId));
        await client.SendAsync(HttpMethod.Post, "api/v1/metadata/definitions", new PropertyDefinitionRequest("caption", "Caption"), Ct);

        var root = await client.GetRootAsync(Ct);
        var folder = await client.CreateFolderAsync(root.Id, "From the SDK", Ct);
        using var file = new MemoryStream("hello sdk"u8.ToArray());
        var uploaded = await client.UploadAsync(file, "hello.txt", folder.Id, cancellationToken: Ct);
        (await client.GetAssetAsync(uploaded.AssetId, Ct)).CurrentVersion!.FileName.ShouldBe("hello.txt");
        (await client.GetChildrenAsync(folder.Id, cancellationToken: Ct)).Items.Single().Id.ShouldBe(uploaded.AssetId);

        await client.SetMetadataAsync(uploaded.AssetId, new Dictionary<string, List<MetadataValue>> { ["caption"] = [new(V: JsonSerializer.SerializeToElement("Hi"))] }, Ct);
        (await client.GetMetadataAsync(uploaded.AssetId, Ct)).Properties["caption"].Values.Single().V!.Value.GetString().ShouldBe("Hi");
        (await client.CreateDownloadLinkAsync(uploaded.AssetId, cancellationToken: Ct)).Url.ShouldNotBeNullOrWhiteSpace();

        var page = await client.CreatePageAsync(folder.Id, "Guide", "# Hello", Ct);
        (await client.UpdatePageAsync(page.Id, "Guide", "# Hello again", page.Version, cancellationToken: Ct)).Version.ShouldBe(page.Version + 1);

        var missing = await Should.ThrowAsync<SharemeApiException>(() => client.GetNodeAsync(Guid.NewGuid(), Ct));
        missing.Status.ShouldBe(HttpStatusCode.NotFound);
    }
}
