using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Graph;
using Shareme.Metadata;

namespace Shareme.Assets;

[DependsOn(typeof(ShareMeGraphModule), typeof(ShareMeMetadataModule), typeof(CoworkeeStorageModule), typeof(CoworkeeBackgroundJobsModule))]
public sealed class ShareMeAssetsModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(ShareMeAssetsModule).Assembly);
        services.AddSingleton<IModelContributor, AssetModelContributor>();
        services.AddSingleton<IModelContributor, RenditionModelContributor>();
        services.AddSingleton<IModelContributor, DownloadModelContributor>();
        services.AddScoped<DownloadPackageWriter>();
        services.AddSingleton<IModelContributor, ArchiveModelContributor>();
        services.AddScoped<ArchiveExtractionJob>();
        services.AddSingleton<IPermissionDefinitionContributor, AssetPermissionDefinitions>();
        services.AddScoped<INodePurgeListener, AssetPurgeListener>();
        services.AddScoped<BlobLinkResolver>();
        services.AddRecurringJob<StorageCleanupJob>(StorageCleanupJob.Id, "17 * * * *");
        services.AddScoped<ProcessUploadJob>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var uploads = app.MapGroup("/api/v1/uploads").WithTags("Uploads").RequireAuthorization();
        uploads.MapPost("/", (StartUploadRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new StartUpload(body), ct).ToHttpResult());
        uploads.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUpload(id), ct).ToHttpResult());
        uploads.MapPut("/{id:guid}/chunks/{index:int}", (Guid id, int index, HttpRequest request, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new PutUploadChunk(id, index, request.Body), ct).ToHttpResult());
        uploads.MapPost("/{id:guid}/complete", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new CompleteUpload(id), ct).ToHttpResult());

        var assets = app.MapGroup("/api/v1/assets").WithTags("Assets").RequireAuthorization();
        assets.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAsset(id), ct).ToHttpResult());
        assets.MapGet("/{id:guid}/download", async (Guid id, int? version, HttpResponse response, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync(new DownloadAsset(id, version), ct);
            return result.IsSuccess ? File(response, result.Value) : result.Error!.ToProblem();
        });
        var view = async (Guid id, int? version, HttpResponse response, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync(new DownloadAsset(id, version, Inline: true), ct);
            if (!result.IsSuccess)
            {
                return result.Error!.ToProblem();
            }

            // shown in the app's origin: nothing in it may run or load anything
            response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self' data:; media-src 'self'; style-src 'unsafe-inline'";
            return File(response, result.Value, inline: true);
        };
        assets.MapGet("/{id:guid}/view", view);
        var derivation = async (Guid id, string kind, int? version, string? v, bool? download, HttpResponse response, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync(new GetDerivation(id, kind, version), ct);
            if (!result.IsSuccess)
            {
                return result.Error!.ToProblem();
            }

            // a tagged url names one rendition forever, so browsers may keep it
            var file = File(response, result.Value, inline: download != true, cache: true);
            if (v is { Length: > 0 })
            {
                response.Headers.CacheControl = "private, max-age=31536000, immutable";
            }

            return file;
        };
        assets.MapGet("/{id:guid}/derivations/{kind}", derivation);

        // the file viewer asks with HEAD for type and size first
        assets.MapMethods("/{id:guid}/view", [HttpMethods.Head], view).ExcludeFromDescription();
        assets.MapMethods("/{id:guid}/derivations/{kind}", [HttpMethods.Head], derivation).ExcludeFromDescription();
        assets.MapPost("/{id:guid}/derivations/{kind}", (Guid id, string kind, IDispatcher d, CancellationToken ct) => d.SendAsync(new RequestDerivation(id, kind), ct).ToHttpResult());
        assets.MapPut("/{id:guid}/derivations/{kind}", (Guid id, string kind, HttpRequest request, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new UploadDerivation(id, kind, request.Body), ct).ToHttpResult())
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(RenditionLimits.MaxUpload));
        assets.MapPost("/{id:guid}/download-link", (Guid id, int? version, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateDownloadLink(id, version), ct).ToHttpResult());

        var types = app.MapGroup("/api/v1/derivation-types").WithTags("Renditions").RequireAuthorization();
        types.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetDerivationTypes(), ct).ToHttpResult());
        types.MapPost("/", (DerivationTypeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveDerivationType(null, body), ct).ToHttpResult());
        types.MapPut("/{id:guid}", (Guid id, DerivationTypeRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveDerivationType(id, body), ct).ToHttpResult());
        types.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteDerivationType(id), ct).ToHttpResult());

        var watermarks = app.MapGroup("/api/v1/watermarks").WithTags("Renditions").RequireAuthorization();
        watermarks.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetWatermarks(), ct).ToHttpResult());
        watermarks.MapPost("/", (WatermarkRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveWatermark(null, body), ct).ToHttpResult());
        watermarks.MapPut("/{id:guid}", (Guid id, WatermarkRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveWatermark(id, body), ct).ToHttpResult());
        watermarks.MapPut("/{id:guid}/image", (Guid id, HttpRequest request, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetWatermarkImage(id, request.Body), ct).ToHttpResult());
        watermarks.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteWatermark(id), ct).ToHttpResult());

        assets.MapPost("/{id:guid}/extract", (Guid id, ExtractArchiveRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new ExtractArchive(id, body), ct).ToHttpResult());
        app.MapGet("/api/v1/extractions/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetArchiveExtraction(id), ct).ToHttpResult()).WithTags("Assets").RequireAuthorization();

        var namings = app.MapGroup("/api/v1/file-namings").WithTags("Downloads").RequireAuthorization();
        namings.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetFileNamings(), ct).ToHttpResult());
        namings.MapPost("/", (FileNamingRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveFileNaming(null, body), ct).ToHttpResult());
        namings.MapPut("/{id:guid}", (Guid id, FileNamingRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveFileNaming(id, body), ct).ToHttpResult());
        namings.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteFileNaming(id), ct).ToHttpResult());
        namings.MapPost("/preview", (FileNamingPreviewRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new PreviewFileNaming(body), ct).ToHttpResult());

        var downloads = app.MapGroup("/api/v1/downloads").WithTags("Downloads");
        downloads.MapPost("/", (DownloadPackageRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateDownloadPackage(body), ct).ToHttpResult()).RequireAuthorization();

        // the random id is the authorization, like a blob link, so browsers can download it directly
        downloads.MapGet("/{id:guid}", async (Guid id, HttpContext context, DownloadPackageWriter writer, CancellationToken ct) =>
        {
            if (await writer.FindAsync(id, ct) is not { } package)
            {
                return Results.NotFound();
            }

            // ponytail: the async zip writer still writes small headers synchronously on a non-seekable body; the file data goes async
            if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>() is { } body)
            {
                body.AllowSynchronousIO = true;
            }

            var response = context.Response;
            response.ContentType = "application/zip";
            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.CacheControl = "private, no-store";
            var disposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(package.Name + ".zip");
            response.Headers.ContentDisposition = disposition.ToString();
            await writer.WriteAsync(package, response.Body, ct);
            return Results.Empty;
        }).AllowAnonymous();

        app.MapGet("/api/v1/blobs/{token}", async (string token, HttpResponse response, BlobLinkResolver resolver, CancellationToken ct) =>
            await resolver.ResolveAsync(token, ct) is { } download ? File(response, download) : Results.NotFound()).WithTags("Assets").AllowAnonymous();
    }

    /// <summary>Originals are never cached; renditions are our own files and may be cached briefly. Never sniffed, ranges supported.</summary>
    private static IResult File(HttpResponse response, BlobDownload download, bool inline = false, bool cache = false)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = cache ? "private, max-age=300" : "private, no-store";
        return Results.File(
            download.Content, download.MimeType, inline ? null : download.FileName, download.LastModified,
            download.Sha256 is { } sha ? new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{sha}\"") : null,
            enableRangeProcessing: true);
    }
}

internal sealed class AssetPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group("Assets", "Assets").Add(AssetPermissions.Download, "Download originals", NodePermissions.Read);
}
