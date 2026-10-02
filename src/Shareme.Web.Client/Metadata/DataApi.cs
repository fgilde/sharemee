using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Automation;

namespace Shareme.Web.Client.Metadata;

public interface IDataApi
{
    Task<DataJobDto> MassEditAsync(MassEditRequest request, CancellationToken cancellationToken = default);

    Task<DataJobDto> ImportAsync(Stream content, Guid? folderId, bool dryRun, CancellationToken cancellationToken = default);

    Task<byte[]> ExportAsync(ExportRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DataJobDto>> GetJobsAsync(CancellationToken cancellationToken = default);

    Task<DataJobDto> GetJobAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class DataApi(HttpClient http) : ApiClientBase(http), IDataApi
{
    public Task<DataJobDto> MassEditAsync(MassEditRequest request, CancellationToken cancellationToken = default) => SendAsync<DataJobDto>(HttpMethod.Post, "api/v1/mass-edits", request, cancellationToken);

    public async Task<DataJobDto> ImportAsync(Stream content, Guid? folderId, bool dryRun, CancellationToken cancellationToken = default)
    {
        var body = new StreamContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendContentAsync(HttpMethod.Put, $"api/v1/imports?dryRun={(dryRun ? "true" : "false")}" + (folderId is { } id ? $"&folderId={id}" : string.Empty), body, cancellationToken);
        return (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<DataJobDto>(response.Content, cancellationToken))!;
    }

    public async Task<byte[]> ExportAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(HttpMethod.Post, "api/v1/exports", request, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DataJobDto>> GetJobsAsync(CancellationToken cancellationToken = default) => await GetAsync<DataJobDto[]>("api/v1/data-jobs", cancellationToken);

    public Task<DataJobDto> GetJobAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<DataJobDto>($"api/v1/data-jobs/{id}", cancellationToken);
}
