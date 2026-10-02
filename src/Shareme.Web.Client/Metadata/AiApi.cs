using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Automation;

namespace Shareme.Web.Client.Metadata;

public interface IAiApi
{
    Task<IReadOnlyList<ExtractionDefinitionDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default);

    Task<ExtractionDefinitionDto> SaveAsync(Guid? id, ExtractionDefinitionRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DataJobDto> RunAsync(ExtractionRunRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExtractionSuggestionDto>> GetSuggestionsAsync(CancellationToken cancellationToken = default);

    Task DecideAsync(Guid id, bool apply, CancellationToken cancellationToken = default);
}

internal sealed class AiApi(HttpClient http) : ApiClientBase(http), IAiApi
{
    public async Task<IReadOnlyList<ExtractionDefinitionDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ExtractionDefinitionDto[]>("api/v1/extractions", cancellationToken);

    public Task<ExtractionDefinitionDto> SaveAsync(Guid? id, ExtractionDefinitionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ExtractionDefinitionDto>(id is null ? HttpMethod.Post : HttpMethod.Put, "api/v1/extractions" + (id is { } i ? $"/{i}" : string.Empty), request, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"api/v1/extractions/{id}", null, cancellationToken);

    public Task<DataJobDto> RunAsync(ExtractionRunRequest request, CancellationToken cancellationToken = default) => SendAsync<DataJobDto>(HttpMethod.Post, "api/v1/extraction-runs", request, cancellationToken);

    public async Task<IReadOnlyList<ExtractionSuggestionDto>> GetSuggestionsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<ExtractionSuggestionDto[]>("api/v1/extraction-suggestions", cancellationToken);

    public Task DecideAsync(Guid id, bool apply, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/extraction-suggestions/{id}/{(apply ? "apply" : "reject")}", null, cancellationToken);
}
