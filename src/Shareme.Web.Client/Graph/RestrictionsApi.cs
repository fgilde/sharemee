using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Graph;

namespace Shareme.Web.Client.Graph;

public interface IRestrictionsApi
{
    Task<IReadOnlyList<RestrictionDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<RestrictionDto> AddAsync(RestrictionRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class RestrictionsApi(HttpClient http) : ApiClientBase(http), IRestrictionsApi
{
    public async Task<IReadOnlyList<RestrictionDto>> GetAsync(CancellationToken cancellationToken = default) => await GetAsync<RestrictionDto[]>("api/v1/restrictions", cancellationToken);

    public Task<RestrictionDto> AddAsync(RestrictionRequest request, CancellationToken cancellationToken = default) => SendAsync<RestrictionDto>(HttpMethod.Post, "api/v1/restrictions", request, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"api/v1/restrictions/{id}", null, cancellationToken);
}
