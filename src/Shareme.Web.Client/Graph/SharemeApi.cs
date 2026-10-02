using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;

namespace Shareme.Web.Client.Graph;

public interface ISharemeApi
{
    Task<NodeDto> GetRootAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NodeDto>> GetEntryPointsAsync(CancellationToken cancellationToken = default);

    Task<NodeDetailDto> GetNodeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<NodeDto>> GetChildrenAsync(Guid id, PageRequest page, string? sort = null, CancellationToken cancellationToken = default);

    Task<PagedResult<NodeDto>> GetFoldersAsync(Guid id, PageRequest page, CancellationToken cancellationToken = default);

    Task<NodeDto> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default);

    Task UpdateNodeAsync(Guid id, UpdateNodeRequest request, CancellationToken cancellationToken = default);

    Task MoveNodeAsync(Guid id, Guid targetParentId, CancellationToken cancellationToken = default);

    Task SetInheritanceAsync(Guid id, bool interrupt, CancellationToken cancellationToken = default);

    Task DeleteNodeAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetSubscriptionAsync(Guid id, bool on, CancellationToken cancellationToken = default);

    Task<PagedResult<RecycleBinItemDto>> GetRecycleBinAsync(PageRequest page, CancellationToken cancellationToken = default);

    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    Task PurgeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AssetDto> GetAssetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<UploadSessionDto> StartUploadAsync(StartUploadRequest request, CancellationToken cancellationToken = default);

    Task PutChunkAsync(Guid session, int index, byte[] data, int length, CancellationToken cancellationToken = default);

    Task<UploadSessionDto> CompleteUploadAsync(Guid session, CancellationToken cancellationToken = default);

    Task<UploadSessionDto> GetUploadAsync(Guid session, CancellationToken cancellationToken = default);

    Task<DownloadLinkDto> CreateDownloadLinkAsync(Guid assetId, int? version = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EdgeDto>> GetEdgesAsync(Guid nodeId, CancellationToken cancellationToken = default);

    Task CreateEdgeAsync(Guid fromNodeId, CreateEdgeRequest request, CancellationToken cancellationToken = default);

    Task DeleteEdgeAsync(Guid edgeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NodeTypeDto>> GetNodeTypesAsync(CancellationToken cancellationToken = default);

    Task CreateNodeTypeAsync(CreateNodeTypeRequest request, CancellationToken cancellationToken = default);
}

public static class AssetUrls
{
    public static string Derivation(Guid assetId, string kind, string? tag = null) => $"api/v1/assets/{assetId}/derivations/{kind}" + (tag is null ? string.Empty : $"?v={tag}");

    public static string DownloadDerivation(Guid assetId, string kind) => $"api/v1/assets/{assetId}/derivations/{kind}?download=true";

    /// <summary>The original shown in the browser; only for <see cref="InlineTypes"/>.</summary>
    public static string View(Guid assetId) => $"api/v1/assets/{assetId}/view";

    public static string Download(Guid assetId, int? version = null) => $"api/v1/assets/{assetId}/download" + (version is { } v ? $"?version={v}" : string.Empty);
}

internal sealed class SharemeApi(HttpClient http) : ApiClientBase(http), ISharemeApi
{
    private const string Nodes = "api/v1/nodes";

    public Task<NodeDto> GetRootAsync(CancellationToken cancellationToken = default) => GetAsync<NodeDto>($"{Nodes}/root", cancellationToken);

    public async Task<IReadOnlyList<NodeDto>> GetEntryPointsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<NodeDto[]>($"{Nodes}/entry-points", cancellationToken);

    public Task<NodeDetailDto> GetNodeAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<NodeDetailDto>($"{Nodes}/{id}", cancellationToken);

    public Task<PagedResult<NodeDto>> GetChildrenAsync(Guid id, PageRequest page, string? sort = null, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<NodeDto>>(
            $"{Nodes}/{id}/children?page={page.Page}&pageSize={page.PageSize}&search={Uri.EscapeDataString(page.Search ?? string.Empty)}&sort={sort ?? NodeSorts.Name}",
            cancellationToken);

    public Task<PagedResult<NodeDto>> GetFoldersAsync(Guid id, PageRequest page, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<NodeDto>>($"{Nodes}/{id}/children?page={page.Page}&pageSize={page.PageSize}&kind={NodeTypeKeys.Folder}", cancellationToken);

    public Task<NodeDto> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<NodeDto>(HttpMethod.Post, Nodes, request, cancellationToken);

    public Task UpdateNodeAsync(Guid id, UpdateNodeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Nodes}/{id}", request, cancellationToken);

    public Task MoveNodeAsync(Guid id, Guid targetParentId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Nodes}/{id}/move", new MoveNodeRequest(targetParentId), cancellationToken);

    public Task SetInheritanceAsync(Guid id, bool interrupt, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Nodes}/{id}/inheritance", new InheritanceRequest(interrupt), cancellationToken);

    public Task DeleteNodeAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"{Nodes}/{id}", null, cancellationToken);

    public Task SetSubscriptionAsync(Guid id, bool on, CancellationToken cancellationToken = default) =>
        SendAsync(on ? HttpMethod.Put : HttpMethod.Delete, $"{Nodes}/{id}/subscription", null, cancellationToken);

    public Task<PagedResult<RecycleBinItemDto>> GetRecycleBinAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<RecycleBinItemDto>>($"api/v1/recycle-bin?page={page.Page}&pageSize={page.PageSize}", cancellationToken);

    public Task RestoreAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"api/v1/recycle-bin/{id}/restore", null, cancellationToken);

    public Task PurgeAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"api/v1/recycle-bin/{id}", null, cancellationToken);

    public Task<AssetDto> GetAssetAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<AssetDto>($"api/v1/assets/{id}", cancellationToken);

    public Task<UploadSessionDto> StartUploadAsync(StartUploadRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<UploadSessionDto>(HttpMethod.Post, "api/v1/uploads", request, cancellationToken);

    public async Task PutChunkAsync(Guid session, int index, byte[] data, int length, CancellationToken cancellationToken = default)
    {
        var content = new ByteArrayContent(data, 0, length);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendContentAsync(HttpMethod.Put, $"api/v1/uploads/{session}/chunks/{index}", content, cancellationToken);
    }

    public Task<UploadSessionDto> CompleteUploadAsync(Guid session, CancellationToken cancellationToken = default) =>
        SendAsync<UploadSessionDto>(HttpMethod.Post, $"api/v1/uploads/{session}/complete", null, cancellationToken);

    public Task<UploadSessionDto> GetUploadAsync(Guid session, CancellationToken cancellationToken = default) => GetAsync<UploadSessionDto>($"api/v1/uploads/{session}", cancellationToken);

    public Task<DownloadLinkDto> CreateDownloadLinkAsync(Guid assetId, int? version = null, CancellationToken cancellationToken = default) =>
        SendAsync<DownloadLinkDto>(HttpMethod.Post, $"api/v1/assets/{assetId}/download-link" + (version is { } v ? $"?version={v}" : string.Empty), null, cancellationToken);

    public async Task<IReadOnlyList<EdgeDto>> GetEdgesAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
        await GetAsync<EdgeDto[]>($"{Nodes}/{nodeId}/edges", cancellationToken);

    public Task CreateEdgeAsync(Guid fromNodeId, CreateEdgeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Nodes}/{fromNodeId}/edges", request, cancellationToken);

    public Task DeleteEdgeAsync(Guid edgeId, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"api/v1/edges/{edgeId}", null, cancellationToken);

    public async Task<IReadOnlyList<NodeTypeDto>> GetNodeTypesAsync(CancellationToken cancellationToken = default) => await GetAsync<NodeTypeDto[]>("api/v1/node-types", cancellationToken);

    public Task CreateNodeTypeAsync(CreateNodeTypeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/v1/node-types", request, cancellationToken);
}

internal sealed class SharemeNavigation : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Browse", "/browse", MudBlazor.Icons.Material.Outlined.FolderOpen),
        new("Search", "/search", MudBlazor.Icons.Material.Outlined.Search),
        new("Workspaces", "/workspaces", MudBlazor.Icons.Material.Outlined.Workspaces),
        new("Data runs", "/data-jobs", MudBlazor.Icons.Material.Outlined.TableView),
        new("Recycle bin", "/recycle-bin", MudBlazor.Icons.Material.Outlined.Delete),
        new("Metadata", "/admin/metadata", MudBlazor.Icons.Material.Outlined.Label, Shareme.Contracts.Metadata.MetadataPermissions.Manage),
        new("Rules", "/admin/rules", MudBlazor.Icons.Material.Outlined.Rule, Shareme.Contracts.Automation.AutomationPermissions.ManageRules),
        new("AI tagging", "/admin/ai", MudBlazor.Icons.Material.Outlined.AutoAwesome, Shareme.Contracts.Automation.AiPermissions.Run),
        new("Node types", "/admin/node-types", MudBlazor.Icons.Material.Outlined.Category, NodePermissions.Admin),
        new("Renditions", "/admin/renditions", MudBlazor.Icons.Material.Outlined.PhotoSizeSelectLarge, NodePermissions.Admin),
    ];
}
