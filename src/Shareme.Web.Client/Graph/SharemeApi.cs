using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts;
using Shareme.Contracts.Graph;

namespace Shareme.Web.Client.Graph;

public interface ISharemeApi
{
    Task<NodeDto> GetRootAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NodeDto>> GetEntryPointsAsync(CancellationToken cancellationToken = default);

    Task<NodeDetailDto> GetNodeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<NodeDto>> GetChildrenAsync(Guid id, PageRequest page, string? sort = null, CancellationToken cancellationToken = default);

    Task<NodeDto> CreateNodeAsync(CreateNodeRequest request, CancellationToken cancellationToken = default);

    Task UpdateNodeAsync(Guid id, UpdateNodeRequest request, CancellationToken cancellationToken = default);

    Task MoveNodeAsync(Guid id, Guid targetParentId, CancellationToken cancellationToken = default);

    Task SetInheritanceAsync(Guid id, bool interrupt, CancellationToken cancellationToken = default);

    Task DeleteNodeAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetSubscriptionAsync(Guid id, bool on, CancellationToken cancellationToken = default);

    Task<PagedResult<RecycleBinItemDto>> GetRecycleBinAsync(PageRequest page, CancellationToken cancellationToken = default);

    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    Task PurgeAsync(Guid id, CancellationToken cancellationToken = default);
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
}

internal sealed class SharemeNavigation : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Browse", "/browse", MudBlazor.Icons.Material.Outlined.FolderOpen),
        new("Recycle bin", "/recycle-bin", MudBlazor.Icons.Material.Outlined.Delete),
    ];
}
