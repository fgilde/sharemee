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
        assets.MapPost("/{id:guid}/download-link", (Guid id, int? version, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateDownloadLink(id, version), ct).ToHttpResult());

        app.MapGet("/api/v1/blobs/{token}", async (string token, HttpResponse response, BlobLinkResolver resolver, CancellationToken ct) =>
            await resolver.ResolveAsync(token, ct) is { } download ? File(response, download) : Results.NotFound()).WithTags("Assets").AllowAnonymous();
    }

    /// <summary>Always an attachment, never sniffed or cached, with ranges so broken downloads resume.</summary>
    private static IResult File(HttpResponse response, BlobDownload download)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "private, no-store";
        return Results.File(
            download.Content, download.MimeType, download.FileName, download.LastModified,
            download.Sha256 is { } sha ? new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{sha}\"") : null,
            enableRangeProcessing: true);
    }
}

internal sealed class AssetPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group("Assets", "Assets").Add(AssetPermissions.Download, "Download originals", NodePermissions.Read);
}
