using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Assets;

namespace Shareme.Web.Client.Assets;

public interface IRenditionsApi
{
    Task<IReadOnlyList<DerivationTypeDto>> GetTypesAsync(CancellationToken cancellationToken = default);

    Task SaveTypeAsync(Guid? id, DerivationTypeRequest request, CancellationToken cancellationToken = default);

    Task DeleteTypeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WatermarkDto>> GetWatermarksAsync(CancellationToken cancellationToken = default);

    Task<WatermarkDto> SaveWatermarkAsync(Guid? id, WatermarkRequest request, CancellationToken cancellationToken = default);

    Task SetWatermarkImageAsync(Guid id, Stream content, CancellationToken cancellationToken = default);

    Task DeleteWatermarkAsync(Guid id, CancellationToken cancellationToken = default);

    Task RequestAsync(Guid assetId, string kind, CancellationToken cancellationToken = default);

    Task UploadAsync(Guid assetId, string kind, Stream content, CancellationToken cancellationToken = default);

    Task CropAsync(Guid assetId, string kind, CropArea area, CancellationToken cancellationToken = default);

    Task<CropVariantDto> CreateVariantAsync(Guid assetId, CropVariantRequest request, CancellationToken cancellationToken = default);
}

internal sealed class RenditionsApi(HttpClient http) : ApiClientBase(http), IRenditionsApi
{
    public async Task<IReadOnlyList<DerivationTypeDto>> GetTypesAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<DerivationTypeDto[]>("api/v1/derivation-types", cancellationToken);

    public Task SaveTypeAsync(Guid? id, DerivationTypeRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, "api/v1/derivation-types" + (id is { } i ? $"/{i}" : string.Empty), request, cancellationToken);

    public Task DeleteTypeAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"api/v1/derivation-types/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<WatermarkDto>> GetWatermarksAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<WatermarkDto[]>("api/v1/watermarks", cancellationToken);

    public Task<WatermarkDto> SaveWatermarkAsync(Guid? id, WatermarkRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<WatermarkDto>(id is null ? HttpMethod.Post : HttpMethod.Put, "api/v1/watermarks" + (id is { } i ? $"/{i}" : string.Empty), request, cancellationToken);

    public Task SetWatermarkImageAsync(Guid id, Stream content, CancellationToken cancellationToken = default) => PutStreamAsync($"api/v1/watermarks/{id}/image", content, cancellationToken);

    public Task DeleteWatermarkAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"api/v1/watermarks/{id}", null, cancellationToken);

    public Task RequestAsync(Guid assetId, string kind, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/assets/{assetId}/derivations/{Uri.EscapeDataString(kind)}", null, cancellationToken);

    public Task UploadAsync(Guid assetId, string kind, Stream content, CancellationToken cancellationToken = default) =>
        PutStreamAsync($"api/v1/assets/{assetId}/derivations/{Uri.EscapeDataString(kind)}", content, cancellationToken);

    public Task CropAsync(Guid assetId, string kind, CropArea area, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"api/v1/assets/{assetId}/derivations/{Uri.EscapeDataString(kind)}/crop", area, cancellationToken);

    public Task<CropVariantDto> CreateVariantAsync(Guid assetId, CropVariantRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CropVariantDto>(HttpMethod.Post, $"api/v1/assets/{assetId}/variants", request, cancellationToken);

    private async Task PutStreamAsync(string url, Stream content, CancellationToken cancellationToken)
    {
        var body = new StreamContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendContentAsync(HttpMethod.Put, url, body, cancellationToken);
    }
}

public interface IDownloadsApi
{
    Task<IReadOnlyList<FileNamingDto>> GetNamingsAsync(CancellationToken cancellationToken = default);

    Task SaveNamingAsync(Guid? id, FileNamingRequest request, CancellationToken cancellationToken = default);

    Task DeleteNamingAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> PreviewNamingAsync(FileNamingPreviewRequest request, CancellationToken cancellationToken = default);

    Task<DownloadPackageDto> CreatePackageAsync(DownloadPackageRequest request, CancellationToken cancellationToken = default);

    Task<byte[]> ContactSheetAsync(ContactSheetRequest request, CancellationToken cancellationToken = default);

    Task<ArchiveExtractionDto> ExtractAsync(Guid assetId, ExtractArchiveRequest request, CancellationToken cancellationToken = default);
}

internal sealed class DownloadsApi(HttpClient http) : ApiClientBase(http), IDownloadsApi
{
    public async Task<IReadOnlyList<FileNamingDto>> GetNamingsAsync(CancellationToken cancellationToken = default) => await GetAsync<FileNamingDto[]>("api/v1/file-namings", cancellationToken);

    public Task SaveNamingAsync(Guid? id, FileNamingRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, "api/v1/file-namings" + (id is { } i ? $"/{i}" : string.Empty), request, cancellationToken);

    public Task DeleteNamingAsync(Guid id, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"api/v1/file-namings/{id}", null, cancellationToken);

    public async Task<IReadOnlyList<string>> PreviewNamingAsync(FileNamingPreviewRequest request, CancellationToken cancellationToken = default) =>
        await SendAsync<string[]>(HttpMethod.Post, "api/v1/file-namings/preview", request, cancellationToken);

    public Task<DownloadPackageDto> CreatePackageAsync(DownloadPackageRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<DownloadPackageDto>(HttpMethod.Post, "api/v1/downloads", request, cancellationToken);

    public async Task<byte[]> ContactSheetAsync(ContactSheetRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(HttpMethod.Post, "api/v1/downloads/contact-sheet", request, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public Task<ArchiveExtractionDto> ExtractAsync(Guid assetId, ExtractArchiveRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ArchiveExtractionDto>(HttpMethod.Post, $"api/v1/assets/{assetId}/extract", request, cancellationToken);
}
