using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Automation;

namespace Shareme.Web.Client.Metadata;

public interface IRulesApi
{
    Task<IReadOnlyList<RuleDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<RuleDto> SaveAsync(Guid? id, RuleRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RuleTestDto> TestAsync(Guid id, Guid nodeId, CancellationToken cancellationToken = default);

    Task<RuleJobDto> StartJobAsync(RuleJobRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuleJobDto>> GetJobsAsync(CancellationToken cancellationToken = default);

    Task<RuleJobDto> GetJobAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class RulesApi(HttpClient http) : ApiClientBase(http), IRulesApi
{
    public async Task<IReadOnlyList<RuleDto>> GetAsync(CancellationToken cancellationToken = default) => await GetAsync<RuleDto[]>("api/v1/rules", cancellationToken);

    public Task<RuleDto> SaveAsync(Guid? id, RuleRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<RuleDto>(id is null ? HttpMethod.Post : HttpMethod.Put, "api/v1/rules" + (id is { } i ? $"/{i}" : string.Empty), request, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"api/v1/rules/{id}", null, cancellationToken);

    public Task<RuleTestDto> TestAsync(Guid id, Guid nodeId, CancellationToken cancellationToken = default) =>
        SendAsync<RuleTestDto>(HttpMethod.Post, $"api/v1/rules/{id}/test/{nodeId}", null, cancellationToken);

    public Task<RuleJobDto> StartJobAsync(RuleJobRequest request, CancellationToken cancellationToken = default) => SendAsync<RuleJobDto>(HttpMethod.Post, "api/v1/rule-jobs", request, cancellationToken);

    public async Task<IReadOnlyList<RuleJobDto>> GetJobsAsync(CancellationToken cancellationToken = default) => await GetAsync<RuleJobDto[]>("api/v1/rule-jobs", cancellationToken);

    public Task<RuleJobDto> GetJobAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<RuleJobDto>($"api/v1/rule-jobs/{id}", cancellationToken);
}
