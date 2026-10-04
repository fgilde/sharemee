using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Knowledge;

namespace Shareme.Web.Client.Knowledge;

public interface IPagesApi
{
    Task<PageDto> CreateAsync(PageRequest request, CancellationToken cancellationToken = default);

    Task<PageDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PageDto> UpdateAsync(Guid id, PageUpdateRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PageVersionDto>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiffLineDto>> DiffAsync(Guid id, int from, int to, CancellationToken cancellationToken = default);

    Task<PageDto> RestoreAsync(Guid id, int number, int expectedVersion, CancellationToken cancellationToken = default);

    Task<string> PreviewAsync(string markdown, CancellationToken cancellationToken = default);
}

internal sealed class PagesApi(HttpClient http) : ApiClientBase(http), IPagesApi
{
    public Task<PageDto> CreateAsync(PageRequest request, CancellationToken cancellationToken = default) => SendAsync<PageDto>(HttpMethod.Post, "api/v1/pages", request, cancellationToken);

    public Task<PageDto> GetAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<PageDto>($"api/v1/pages/{id}", cancellationToken);

    public Task<PageDto> UpdateAsync(Guid id, PageUpdateRequest request, CancellationToken cancellationToken = default) => SendAsync<PageDto>(HttpMethod.Put, $"api/v1/pages/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<PageVersionDto>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default) => await GetAsync<PageVersionDto[]>($"api/v1/pages/{id}/versions", cancellationToken);

    public async Task<IReadOnlyList<DiffLineDto>> DiffAsync(Guid id, int from, int to, CancellationToken cancellationToken = default) =>
        await GetAsync<DiffLineDto[]>($"api/v1/pages/{id}/diff?from={from}&to={to}", cancellationToken);

    public Task<PageDto> RestoreAsync(Guid id, int number, int expectedVersion, CancellationToken cancellationToken = default) =>
        SendAsync<PageDto>(HttpMethod.Post, $"api/v1/pages/{id}/versions/{number}/restore?expectedVersion={expectedVersion}", null, cancellationToken);

    public async Task<string> PreviewAsync(string markdown, CancellationToken cancellationToken = default) =>
        (await SendAsync<PagePreviewDto>(HttpMethod.Post, "api/v1/pages/preview", new PagePreviewRequest(markdown), cancellationToken)).Html;
}
