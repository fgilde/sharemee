using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Coworkee.Client;
using Coworkee.Contracts;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;
using Shareme.Contracts.Metadata;
using Shareme.Contracts.Search;

namespace Shareme.Client;

/// <summary>
/// Typed client for the Sharemee API. Give it an <see cref="HttpClient"/> whose base address is the API and whose
/// requests carry a bearer token (for example through <see cref="BearerTokenHandler"/>).
/// </summary>
public sealed class SharemeClient(HttpClient http) : CoworkeeApiClient(http)
{
    // nodes

    public Task<NodeDto> GetRootAsync(CancellationToken cancellationToken = default) => GetAsync<NodeDto>("api/v1/nodes/root", cancellationToken);

    public Task<NodeDto[]> GetEntryPointsAsync(CancellationToken cancellationToken = default) => GetAsync<NodeDto[]>("api/v1/nodes/entry-points", cancellationToken);

    public Task<NodeDetailDto> GetNodeAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<NodeDetailDto>($"api/v1/nodes/{id}", cancellationToken);

    public Task<PagedResult<NodeDto>> GetChildrenAsync(Guid id, int page = 1, int pageSize = 50, string? search = null, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<NodeDto>>($"api/v1/nodes/{id}/children?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? string.Empty)}", cancellationToken);

    public Task<NodeDto> CreateFolderAsync(Guid parentId, string name, CancellationToken cancellationToken = default) =>
        SendAsync<NodeDto>(HttpMethod.Post, "api/v1/nodes", new CreateNodeRequest(parentId, name), cancellationToken);

    public Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"api/v1/nodes/{id}", new UpdateNodeRequest(name), cancellationToken);

    public Task MoveAsync(Guid id, Guid targetParentId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/nodes/{id}/move", new MoveNodeRequest(targetParentId), cancellationToken);

    /// <summary>Moves the node to the recycle bin.</summary>
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"api/v1/nodes/{id}", null, cancellationToken);

    // metadata

    public Task<NodeMetadataDto> GetMetadataAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
        GetAsync<NodeMetadataDto>($"api/v1/nodes/{nodeId}/metadata", cancellationToken);

    /// <summary>Sets the listed properties; others keep their values. An empty list clears a property.</summary>
    public Task SetMetadataAsync(Guid nodeId, IReadOnlyDictionary<string, List<MetadataValue>> values, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Patch, $"api/v1/nodes/{nodeId}/metadata", values, cancellationToken);

    // search

    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SearchResponse>(HttpMethod.Post, "api/v1/search", request, cancellationToken);

    public Task<GlobalSearchDto> SearchEverythingAsync(string term, CancellationToken cancellationToken = default) =>
        GetAsync<GlobalSearchDto>($"api/v1/global-search?q={Uri.EscapeDataString(term)}", cancellationToken);

    // files

    public Task<AssetDto> GetAssetAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<AssetDto>($"api/v1/assets/{id}", cancellationToken);

    /// <summary>
    /// Uploads a file in chunks into a folder (or as a new version of <paramref name="assetId"/>) and waits until the API
    /// has stored it.
    /// </summary>
    public async Task<UploadResultDto> UploadAsync(Stream content, string fileName, Guid? folderId = null, Guid? assetId = null, CancellationToken cancellationToken = default)
    {
        var buffered = content.CanSeek ? content : await CopyAsync(content, cancellationToken);
        var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffered, cancellationToken));
        buffered.Position = 0;
        var session = await SendAsync<UploadSessionDto>(HttpMethod.Post, "api/v1/uploads", new StartUploadRequest(fileName, buffered.Length, folderId, assetId, sha256), cancellationToken);
        var chunk = new byte[session.ChunkSize];
        for (var index = 0; index < session.ChunkCount; index++)
        {
            var length = await buffered.ReadAtLeastAsync(chunk, chunk.Length, throwOnEndOfStream: false, cancellationToken);
            using var part = new ByteArrayContent(chunk, 0, length);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await Http.PutAsync($"api/v1/uploads/{session.Id}/chunks/{index}", part, cancellationToken);
            await EnsureAsync(response, cancellationToken);
        }

        session = await SendAsync<UploadSessionDto>(HttpMethod.Post, $"api/v1/uploads/{session.Id}/complete", null, cancellationToken);
        while (session.Result is null)
        {
            if (session.Error is { } error)
            {
                throw new CoworkeeApiException(HttpStatusCode.UnprocessableEntity, error, "upload.failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            session = await GetAsync<UploadSessionDto>($"api/v1/uploads/{session.Id}", cancellationToken);
        }

        return session.Result;
    }

    /// <summary>A short lived link to the file, or to an older <paramref name="version"/>.</summary>
    public Task<DownloadLinkDto> CreateDownloadLinkAsync(Guid assetId, int? version = null, CancellationToken cancellationToken = default) =>
        SendAsync<DownloadLinkDto>(HttpMethod.Post, $"api/v1/assets/{assetId}/download-link" + (version is { } v ? $"?version={v}" : string.Empty), null, cancellationToken);

    // knowledge base and intranet

    public Task<PageDto> GetPageAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<PageDto>($"api/v1/pages/{id}", cancellationToken);

    public Task<PageDto> CreatePageAsync(Guid parentId, string title, string markdown, CancellationToken cancellationToken = default) =>
        SendAsync<PageDto>(HttpMethod.Post, "api/v1/pages", new Shareme.Contracts.Knowledge.PageRequest(parentId, title, markdown), cancellationToken);

    /// <summary>Saves a new page version; <paramref name="expectedVersion"/> guards against overwriting someone else's edit.</summary>
    public Task<PageDto> UpdatePageAsync(Guid id, string title, string markdown, int expectedVersion, string? comment = null, CancellationToken cancellationToken = default) =>
        SendAsync<PageDto>(HttpMethod.Put, $"api/v1/pages/{id}", new PageUpdateRequest(title, markdown, expectedVersion, comment), cancellationToken);

    public Task<NewsSummaryDto[]> GetNewsAsync(int take = 10, CancellationToken cancellationToken = default) =>
        GetAsync<NewsSummaryDto[]>($"api/v1/news?take={take}", cancellationToken);

    public Task<PersonDto[]> FindPeopleAsync(string? search = null, int skip = 0, int take = 50, CancellationToken cancellationToken = default) =>
        GetAsync<PersonDto[]>($"api/v1/people?search={Uri.EscapeDataString(search ?? string.Empty)}&skip={skip}&take={take}", cancellationToken);

    private static async Task<MemoryStream> CopyAsync(Stream content, CancellationToken cancellationToken)
    {
        var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        return copy;
    }
}
