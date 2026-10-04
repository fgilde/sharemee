using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;

namespace Shareme.Web.Client.Knowledge;

public interface IIntranetApi
{
    Task<IReadOnlyList<NewsSummaryDto>> GetFeedAsync(int take = 10, CancellationToken cancellationToken = default);

    Task<NewsDto> GetNewsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<NewsDto> CreateNewsAsync(NewsRequest request, CancellationToken cancellationToken = default);

    Task<NewsDto> UpdateNewsAsync(Guid id, NewsUpdateRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NodeDto>> GetFavoritesAsync(CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(Guid nodeId, bool on, CancellationToken cancellationToken = default);

    Task<HomeLayoutDto> GetLayoutAsync(CancellationToken cancellationToken = default);

    Task<HomeLayoutDto> SaveLayoutAsync(IReadOnlyList<string>? widgets, bool forTenant = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NodeDto>> GetRecentAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HomeTaskDto>> GetTasksAsync(CancellationToken cancellationToken = default);
}

internal sealed class IntranetApi(HttpClient http) : ApiClientBase(http), IIntranetApi
{
    public async Task<IReadOnlyList<NewsSummaryDto>> GetFeedAsync(int take = 10, CancellationToken cancellationToken = default) => await GetAsync<NewsSummaryDto[]>($"api/v1/news?take={take}", cancellationToken);

    public Task<NewsDto> GetNewsAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<NewsDto>($"api/v1/news/{id}", cancellationToken);

    public Task<NewsDto> CreateNewsAsync(NewsRequest request, CancellationToken cancellationToken = default) => SendAsync<NewsDto>(HttpMethod.Post, "api/v1/news", request, cancellationToken);

    public Task<NewsDto> UpdateNewsAsync(Guid id, NewsUpdateRequest request, CancellationToken cancellationToken = default) => SendAsync<NewsDto>(HttpMethod.Put, $"api/v1/news/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<NodeDto>> GetFavoritesAsync(CancellationToken cancellationToken = default) => await GetAsync<NodeDto[]>("api/v1/favorites", cancellationToken);

    public Task SetFavoriteAsync(Guid nodeId, bool on, CancellationToken cancellationToken = default) => SendAsync(on ? HttpMethod.Put : HttpMethod.Delete, $"api/v1/favorites/{nodeId}", null, cancellationToken);

    public Task<HomeLayoutDto> GetLayoutAsync(CancellationToken cancellationToken = default) => GetAsync<HomeLayoutDto>("api/v1/home/layout", cancellationToken);

    public async Task<HomeLayoutDto> SaveLayoutAsync(IReadOnlyList<string>? widgets, bool forTenant = false, CancellationToken cancellationToken = default)
    {
        if (widgets is null)
        {
            await SendAsync(HttpMethod.Delete, "api/v1/home/layout", null, cancellationToken);
            return await GetLayoutAsync(cancellationToken);
        }

        return await SendAsync<HomeLayoutDto>(HttpMethod.Put, forTenant ? "api/v1/home/layout/default" : "api/v1/home/layout", new HomeLayoutRequest(widgets), cancellationToken);
    }

    public async Task<IReadOnlyList<NodeDto>> GetRecentAsync(CancellationToken cancellationToken = default) => await GetAsync<NodeDto[]>("api/v1/home/recent", cancellationToken);

    public async Task<IReadOnlyList<HomeTaskDto>> GetTasksAsync(CancellationToken cancellationToken = default) => await GetAsync<HomeTaskDto[]>("api/v1/home/tasks", cancellationToken);
}
