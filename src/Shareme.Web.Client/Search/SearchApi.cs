using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Search;

namespace Shareme.Web.Client.Search;

public interface ISearchApi
{
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SuggestionDto>> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedSearchDto>> GetSavedAsync(CancellationToken cancellationToken = default);

    Task<SavedSearchDto> SaveAsync(Guid? id, SaveSearchRequest request, CancellationToken cancellationToken = default);

    Task DeleteSavedAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ReindexRunDto> StartReindexAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReindexRunDto>> GetReindexRunsAsync(CancellationToken cancellationToken = default);
}

internal sealed class SearchApi(HttpClient http) : ApiClientBase(http), ISearchApi
{
    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SearchResponse>(HttpMethod.Post, "api/v1/search", request, cancellationToken);

    public async Task<IReadOnlyList<SuggestionDto>> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken = default) =>
        await SendAsync<SuggestionDto[]>(HttpMethod.Post, "api/v1/search/suggest", request, cancellationToken);

    public async Task<IReadOnlyList<SavedSearchDto>> GetSavedAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<SavedSearchDto[]>("api/v1/saved-searches", cancellationToken);

    public Task<SavedSearchDto> SaveAsync(Guid? id, SaveSearchRequest request, CancellationToken cancellationToken = default) =>
        id is { } existing
            ? SendAsync<SavedSearchDto>(HttpMethod.Put, $"api/v1/saved-searches/{existing}", request, cancellationToken)
            : SendAsync<SavedSearchDto>(HttpMethod.Post, "api/v1/saved-searches", request, cancellationToken);

    public Task DeleteSavedAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"api/v1/saved-searches/{id}", null, cancellationToken);

    public Task<ReindexRunDto> StartReindexAsync(CancellationToken cancellationToken = default) => SendAsync<ReindexRunDto>(HttpMethod.Post, "api/v1/search/reindex", null, cancellationToken);

    public async Task<IReadOnlyList<ReindexRunDto>> GetReindexRunsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ReindexRunDto[]>("api/v1/search/reindex", cancellationToken);
}
