using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Metadata;

namespace Shareme.Web.Client.Metadata;

public interface IMetadataApi
{
    Task<IReadOnlyList<PropertyGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default);

    Task SaveGroupAsync(Guid? id, PropertyGroupRequest request, CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PropertyDefinitionDto>> GetDefinitionsAsync(string? nodeType = null, CancellationToken cancellationToken = default);

    Task<PropertyDefinitionDto> SaveDefinitionAsync(Guid? id, PropertyDefinitionRequest request, CancellationToken cancellationToken = default);

    Task DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetListValuesAsync(Guid definitionId, IReadOnlyList<PropertyListValueDto> values, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DataLanguageDto>> GetLanguagesAsync(CancellationToken cancellationToken = default);

    Task SaveLanguageAsync(DataLanguageDto language, CancellationToken cancellationToken = default);

    Task DeleteLanguageAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserContextDto>> GetContextsAsync(CancellationToken cancellationToken = default);

    Task SaveContextAsync(UserContextDto context, CancellationToken cancellationToken = default);

    Task DeleteContextAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PropertySetDto>> GetSetsAsync(string? nodeType = null, CancellationToken cancellationToken = default);

    Task<NodeMetadataDto> GetNodeMetadataAsync(Guid nodeId, CancellationToken cancellationToken = default);

    Task SetNodeMetadataAsync(Guid nodeId, IReadOnlyDictionary<string, List<MetadataValue>?> values, CancellationToken cancellationToken = default);
}

internal sealed class MetadataApi(HttpClient http) : ApiClientBase(http), IMetadataApi
{
    private const string Base = "api/v1/metadata";

    public async Task<IReadOnlyList<PropertyGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<PropertyGroupDto[]>($"{Base}/groups", cancellationToken);

    public Task SaveGroupAsync(Guid? id, PropertyGroupRequest request, CancellationToken cancellationToken = default) =>
        id is { } existing ? SendAsync(HttpMethod.Put, $"{Base}/groups/{existing}", request, cancellationToken) : SendAsync(HttpMethod.Post, $"{Base}/groups", request, cancellationToken);

    public Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"{Base}/groups/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<PropertyDefinitionDto>> GetDefinitionsAsync(string? nodeType = null, CancellationToken cancellationToken = default) =>
        await GetAsync<PropertyDefinitionDto[]>(nodeType is null ? $"{Base}/definitions" : $"{Base}/definitions?nodeType={Uri.EscapeDataString(nodeType)}", cancellationToken);

    public Task<PropertyDefinitionDto> SaveDefinitionAsync(Guid? id, PropertyDefinitionRequest request, CancellationToken cancellationToken = default) =>
        id is { } existing
            ? SendAsync<PropertyDefinitionDto>(HttpMethod.Put, $"{Base}/definitions/{existing}", request, cancellationToken)
            : SendAsync<PropertyDefinitionDto>(HttpMethod.Post, $"{Base}/definitions", request, cancellationToken);

    public Task DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"{Base}/definitions/{id}", null, cancellationToken);

    public Task SetListValuesAsync(Guid definitionId, IReadOnlyList<PropertyListValueDto> values, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Base}/definitions/{definitionId}/list-values", values, cancellationToken);

    public async Task<IReadOnlyList<DataLanguageDto>> GetLanguagesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<DataLanguageDto[]>($"{Base}/languages", cancellationToken);

    public Task SaveLanguageAsync(DataLanguageDto language, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{Base}/languages", language, cancellationToken);

    public Task DeleteLanguageAsync(string code, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Base}/languages/{Uri.EscapeDataString(code)}", null, cancellationToken);

    public async Task<IReadOnlyList<UserContextDto>> GetContextsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<UserContextDto[]>($"{Base}/contexts", cancellationToken);

    public Task SaveContextAsync(UserContextDto context, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{Base}/contexts", context, cancellationToken);

    public Task DeleteContextAsync(string key, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Base}/contexts/{Uri.EscapeDataString(key)}", null, cancellationToken);

    public async Task<IReadOnlyList<PropertySetDto>> GetSetsAsync(string? nodeType = null, CancellationToken cancellationToken = default) =>
        await GetAsync<PropertySetDto[]>(nodeType is null ? $"{Base}/sets" : $"{Base}/sets?nodeType={Uri.EscapeDataString(nodeType)}", cancellationToken);

    public Task<NodeMetadataDto> GetNodeMetadataAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
        GetAsync<NodeMetadataDto>($"api/v1/nodes/{nodeId}/metadata", cancellationToken);

    public Task SetNodeMetadataAsync(Guid nodeId, IReadOnlyDictionary<string, List<MetadataValue>?> values, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Patch, $"api/v1/nodes/{nodeId}/metadata", values, cancellationToken);
}
