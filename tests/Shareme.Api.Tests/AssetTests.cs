using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Storage;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Assets;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Infrastructure;

namespace Shareme.Api.Tests;

public sealed class AssetTests(ApiFixture api) : IAsyncLifetime
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await api.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task A_chunked_upload_creates_an_asset_that_downloads_back()
    {
        var folder = await FolderAsync("Photos");
        var content = Png(9 * 1024 * 1024);

        var result = await UploadAsync(Admin, "beach.png", content, folder);

        var asset = (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{result.AssetId}", Ct))!;
        asset.AssetType.ShouldBe(AssetTypeKeys.Image);
        asset.Status.ShouldBe(AssetStatuses.Ready);
        asset.CurrentVersion!.Size.ShouldBe(content.Length);
        asset.CurrentVersion.MimeType.ShouldBe("image/png");
        asset.CurrentVersion.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(content)));

        using var download = await Admin.GetAsync($"/api/v1/assets/{result.AssetId}/download", Ct);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("beach.png");
        (await download.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(content);

        var children = await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{folder}/children", Ct);
        children!.Items.Single().Semantic.ShouldBe("asset");
    }

    [Fact]
    public async Task A_new_version_keeps_metadata_except_properties_marked_to_be_dropped()
    {
        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("caption", "Caption"), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("approved", "Approved", PropertyValueTypes.Boolean, IgnoreOnNewVersion: true), Ct)).EnsureSuccessStatusCode();
        var folder = await FolderAsync("Photos");
        var first = await UploadAsync(Admin, "logo.png", Png(1000), folder);
        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{first.AssetId}/metadata", new Dictionary<string, List<MetadataValue>>
        {
            ["caption"] = [new(V: JsonSerializer.SerializeToElement("Our logo"))],
            ["approved"] = [new(V: JsonSerializer.SerializeToElement(true))],
        }, Ct)).EnsureSuccessStatusCode();

        var second = await UploadAsync(Admin, "logo-v2.png", Png(2000), assetId: first.AssetId);

        second.AssetId.ShouldBe(first.AssetId);
        second.VersionNumber.ShouldBe(2);
        var asset = (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{first.AssetId}", Ct))!;
        asset.Versions.Select(v => v.Number).ShouldBe([2, 1]);
        asset.CurrentVersion!.FileName.ShouldBe("logo-v2.png");
        var metadata = (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{first.AssetId}/metadata", Ct))!;
        metadata.Properties.Keys.ShouldBe(["caption"]);
        using var old = await Admin.GetAsync($"/api/v1/assets/{first.AssetId}/download?version=1", Ct);
        (await old.Content.ReadAsByteArrayAsync(Ct)).Length.ShouldBe(1000);
    }

    [Fact]
    public async Task The_same_content_twice_is_flagged_as_duplicate()
    {
        var folder = await FolderAsync("Photos");
        var content = Png(5000);
        var first = await UploadAsync(Admin, "a.png", content, folder);

        var second = await UploadAsync(Admin, "b.png", content, folder);

        second.DuplicateOf.ShouldBe([first.AssetId]);
        (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{second.AssetId}", Ct))!.Status.ShouldBe(AssetStatuses.Duplicate);
        (await Admin.GetFromJsonAsync<EdgeDto[]>($"/api/v1/nodes/{second.AssetId}/edges", Ct))!
            .ShouldContain(e => e.Type == EdgeTypeKeys.Duplicate && e.ToNodeId == first.AssetId);
    }

    [Fact]
    public async Task Forbidden_types_wrong_content_missing_chunks_and_wrong_hashes_are_rejected()
    {
        var folder = await FolderAsync("Inbox");

        (await Admin.PostAsJsonAsync("/api/v1/uploads", new StartUploadRequest("setup.exe", 10, folder), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Admin.PostAsJsonAsync("/api/v1/uploads", new StartUploadRequest("setup.exe. ", 10, folder), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/uploads", new StartUploadRequest("invoice.exe.pdf", 10, folder), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var fake = await StartAsync(Admin, new StartUploadRequest("fake.png", 11, folder));
        await ChunkAsync(Admin, fake.Id, 0, Encoding.UTF8.GetBytes("hello world"));
        (await CompleteAsync(Admin, fake.Id)).State.ShouldBe(UploadStates.Failed);

        var partial = await StartAsync(Admin, new StartUploadRequest("big.png", 9 * 1024 * 1024, folder));
        await ChunkAsync(Admin, partial.Id, 0, Png(8 * 1024 * 1024));
        (await Admin.PostAsync($"/api/v1/uploads/{partial.Id}/complete", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var hashed = await StartAsync(Admin, new StartUploadRequest("hash.png", 100, folder, Sha256: new string('0', 64)));
        await ChunkAsync(Admin, hashed.Id, 0, Png(100));
        var failed = await CompleteAsync(Admin, hashed.Id);
        failed.State.ShouldBe(UploadStates.Failed);
        failed.Error.ShouldNotBeNull().ShouldContain("SHA-256");
        (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{failed.AssetId}", Ct))!.Status.ShouldBe(AssetStatuses.Failed);
    }

    [Fact]
    public async Task Uploads_and_downloads_need_their_permissions()
    {
        var folder = await FolderAsync("Private");
        var result = await UploadAsync(Admin, "secret.png", Png(500), folder);
        var reader = await UserWithFolderRoleAsync(folder, NodePermissions.Read);
        var downloader = await UserWithFolderRoleAsync(folder, NodePermissions.Read, AssetPermissions.Download);

        (await reader.PostAsJsonAsync("/api/v1/uploads", new StartUploadRequest("x.png", 10, folder), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"/api/v1/assets/{result.AssetId}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await downloader.GetAsync($"/api/v1/assets/{result.AssetId}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var session = await StartAsync(Admin, new StartUploadRequest("mine.png", 10, folder));
        using var foreign = new ByteArrayContent(Png(10));
        (await downloader.PutAsync($"/api/v1/uploads/{session.Id}/chunks/0", foreign, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Completing_twice_is_refused_and_downloads_support_ranges()
    {
        var folder = await FolderAsync("Ranges");
        var content = Png(1000);
        var session = await StartAsync(Admin, new StartUploadRequest("range.png", content.Length, folder));
        await ChunkAsync(Admin, session.Id, 0, content);

        var first = Admin.PostAsync($"/api/v1/uploads/{session.Id}/complete", null, Ct);
        var second = Admin.PostAsync($"/api/v1/uploads/{session.Id}/complete", null, Ct);
        var statuses = (await Task.WhenAll(first, second)).Select(r => r.StatusCode).ToList();
        statuses.ShouldContain(HttpStatusCode.OK);
        statuses.ShouldContain(HttpStatusCode.Conflict);
        var done = await WaitAsync(Admin, session.Id);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/assets/{done.AssetId}/download");
        request.Headers.Range = new RangeHeaderValue(10, 19);
        using var partial = await Admin.SendAsync(request, Ct);
        partial.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await partial.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(content[10..20]);
        partial.Headers.ETag.ShouldNotBeNull();
    }

    [Fact]
    public async Task Images_get_a_thumbnail_and_a_preview_other_files_are_not_rendered()
    {
        var folder = await FolderAsync("Renders");
        using var picture = new ImageMagick.MagickImage(ImageMagick.MagickColors.OrangeRed, 2400, 1200);
        var image = await UploadAsync(Admin, "wide.png", picture.ToByteArray(ImageMagick.MagickFormat.Png), folder);
        var archive = await UploadAsync(Admin, "bundle.zip", [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00], folder);
        var reader = await UserWithFolderRoleAsync(folder, NodePermissions.Read);

        var rendered = await DerivationsAsync(image.AssetId);
        rendered.Single(d => d.Kind == DerivationKinds.Thumbnail).ShouldBe(new DerivationDto(DerivationKinds.Thumbnail, DerivationStates.Ready, 320, 160, "image/webp"), new SizeIgnored());
        rendered.Single(d => d.Kind == DerivationKinds.Preview).ShouldBe(new DerivationDto(DerivationKinds.Preview, DerivationStates.Ready, 1600, 800, "image/webp"), new SizeIgnored());
        (await DerivationsAsync(archive.AssetId)).ShouldAllBe(d => d.State == DerivationStates.NotSupported);

        using var thumbnail = await reader.GetAsync($"/api/v1/assets/{image.AssetId}/derivations/{DerivationKinds.Thumbnail}", Ct);
        thumbnail.StatusCode.ShouldBe(HttpStatusCode.OK);
        thumbnail.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        using var webp = new ImageMagick.MagickImage(await thumbnail.Content.ReadAsByteArrayAsync(Ct));
        webp.Width.ShouldBe(320u);

        // lists carry a tag that names the thumbnail; it changes with new content and with a fresh render
        var listed = (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{folder}/children", Ct))!.Items;
        var tag = listed.Single(n => n.Id == image.AssetId).Thumbnail.ShouldNotBeNull();
        listed.Single(n => n.Id == archive.AssetId).Thumbnail.ShouldBeNull();
        (await Admin.PostAsync($"/api/v1/assets/{image.AssetId}/derivations/rerender", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DerivationsAsync(image.AssetId)).ShouldAllBe(d => d.State == DerivationStates.Ready);
        (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{image.AssetId}", Ct))!.RenditionTag.ShouldNotBeNull().ShouldNotBe(tag);
    }

    [Fact]
    public async Task Folder_listings_can_skip_assets()
    {
        var folder = await FolderAsync("Mixed");
        await UploadAsync(Admin, "file.png", Png(100), folder);
        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        var sub = (await (await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(folder, "Sub"), Ct)).Content.ReadFromJsonAsync<NodeDto>(Ct))!;

        var folders = (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{folder}/children?kind=folder", Ct))!.Items;

        folders.Select(f => f.Id).ShouldBe([sub.Id]);
        root.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Videos_get_a_poster_frame_and_a_playable_copy()
    {
        var ffmpeg = Tool("ffmpeg");
        Assert.SkipUnless(ffmpeg is not null, "ffmpeg is not installed");
        var clip = Path.Combine(Path.GetTempPath(), $"clip-{Guid.NewGuid():N}.mp4");
        await RunAsync(ffmpeg!, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=duration=2:size=640x360:rate=10", "-f", "lavfi", "-i", "sine=duration=2",
            "-shortest", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-y", clip);
        var folder = await FolderAsync("Clips");

        var bytes = await File.ReadAllBytesAsync(clip, Ct);
        File.Delete(clip);
        var video = await UploadAsync(Admin, "clip.mp4", bytes, folder);

        var rendered = await DerivationsAsync(video.AssetId);
        rendered.Single(d => d.Kind == DerivationKinds.Thumbnail).State.ShouldBe(DerivationStates.Ready);
        rendered.Single(d => d.Kind == DerivationKinds.Media).State.ShouldBe(DerivationStates.Ready);
        rendered.Single(d => d.Kind == DerivationKinds.Animated).MimeType.ShouldBe("image/gif");
        using (var gif = new ImageMagick.MagickImageCollection(await Admin.GetByteArrayAsync($"/api/v1/assets/{video.AssetId}/derivations/{DerivationKinds.Animated}", Ct)))
        {
            gif.Count.ShouldBeGreaterThan(1, "the gif is animated");
            gif[0].Width.ShouldBeLessThanOrEqualTo(240u);
        }

        using var media = await Admin.GetAsync($"/api/v1/assets/{video.AssetId}/derivations/{DerivationKinds.Media}", Ct);
        media.Content.Headers.ContentType!.MediaType.ShouldBe("video/mp4");
        (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{video.AssetId}", Ct))!.CurrentVersion!.TechnicalMetadata.ShouldNotBeNull().ShouldNotBeEmpty();

        // the poster frame is kept (but not listed), so a rendition asked for later comes from it
        rendered.ShouldNotContain(d => d.Kind == DerivationKinds.Still);
        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ShareMeDbContext>().Set<Derivation>().IgnoreQueryFilters()
                .SingleAsync(d => d.AssetId == video.AssetId && d.Kind == DerivationKinds.Still, Ct)).State.ShouldBe(DerivationStates.Ready);
        }

        (await Admin.PostAsJsonAsync("/api/v1/derivation-types", new DerivationTypeRequest("square", "Square", 100, 100), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsync($"/api/v1/assets/{video.AssetId}/derivations/square", null, Ct)).EnsureSuccessStatusCode();
        (await DerivationsAsync(video.AssetId)).Single(d => d.Kind == "square").ShouldSatisfyAllConditions(d => d.State.ShouldBe(DerivationStates.Ready), d => d.Width.ShouldBe(100));
    }

    [Fact]
    public async Task Custom_renditions_are_rendered_by_default_on_request_or_uploaded_by_hand_with_watermarks()
    {
        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("confidential", "Confidential", PropertyValueTypes.Boolean), Ct)).EnsureSuccessStatusCode();
        var always = await WatermarkAsync(new WatermarkRequest("Logo", "center", 100, 50));
        var confidential = await WatermarkAsync(new WatermarkRequest("Confidential", "center", 100, 50, "confidential"));
        (await Admin.PostAsJsonAsync("/api/v1/derivation-types", new DerivationTypeRequest("preview", "Taken", 100, 100), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/derivation-types", new DerivationTypeRequest("web", "Web", 100, 100, "gif"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/derivation-types", new DerivationTypeRequest(
            "web", "Web", 400, 400, DerivationFormats.Jpeg, IsDefault: true, WatermarkId: always, AssetTypeKeys: [AssetTypeKeys.Image]), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync("/api/v1/derivation-types", new DerivationTypeRequest("social", "Social", 200, 200, DerivationFormats.Png, WatermarkId: confidential), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync("/api/v1/derivation-types", new DerivationTypeRequest("print", "Print", 4000, 4000, IsOnDemand: false, AllowManualUpload: true), Ct)).EnsureSuccessStatusCode();
        var folder = await FolderAsync("Campaign");
        using var picture = new ImageMagick.MagickImage(ImageMagick.MagickColors.Red, 800, 400);
        var image = await UploadAsync(Admin, "red.png", picture.ToByteArray(ImageMagick.MagickFormat.Png), folder);
        var reader = await UserWithFolderRoleAsync(folder, NodePermissions.Read);

        var web = (await DerivationsAsync(image.AssetId)).Single(d => d.Kind == "web");
        web.ShouldBe(new DerivationDto("web", DerivationStates.Ready, 400, 200, "image/jpeg"), new SizeIgnored());
        (await CenterAsync(reader, image.AssetId, "web")).B.ShouldBeGreaterThan((byte)200, "the watermark covers the middle");

        (await (await Admin.PostAsync($"/api/v1/assets/{image.AssetId}/derivations/social", null, Ct)).Content.ReadFromJsonAsync<DerivationDto>(Ct))!.State.ShouldBe(DerivationStates.Pending);
        (await DerivationsAsync(image.AssetId)).Single(d => d.Kind == "social").ShouldBe(new DerivationDto("social", DerivationStates.Ready, 200, 100, "image/png"), new SizeIgnored());
        (await CenterAsync(reader, image.AssetId, "social")).R.ShouldBeGreaterThan((byte)200, "the asset is not confidential");
        (await Admin.PostAsync($"/api/v1/assets/{image.AssetId}/derivations/print", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // a watermark without its image fails the rendition instead of letting it out unprotected, and one in use cannot be removed
        var blank = (await (await Admin.PostAsJsonAsync("/api/v1/watermarks", new WatermarkRequest("Blank"), Ct)).Content.ReadFromJsonAsync<WatermarkDto>(Ct))!;
        (await Admin.PostAsJsonAsync("/api/v1/derivation-types", new DerivationTypeRequest("guarded", "Guarded", 100, 100, WatermarkId: blank.Id), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsync($"/api/v1/assets/{image.AssetId}/derivations/guarded", null, Ct)).EnsureSuccessStatusCode();
        (await DerivationsAsync(image.AssetId)).Single(d => d.Kind == "guarded").State.ShouldBe(DerivationStates.Failed);
        (await reader.GetAsync($"/api/v1/assets/{image.AssetId}/derivations/guarded", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.DeleteAsync($"/api/v1/watermarks/{blank.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var manual = new ImageMagick.MagickImage(ImageMagick.MagickColors.Green, 50, 50);
        var manualBytes = manual.ToByteArray(ImageMagick.MagickFormat.Png);
        (await Admin.PutAsync($"/api/v1/assets/{image.AssetId}/derivations/print", new ByteArrayContent(manualBytes), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsync($"/api/v1/assets/{image.AssetId}/derivations/print", new ByteArrayContent([0x50, 0x4B, 0x03, 0x04]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PutAsync($"/api/v1/assets/{image.AssetId}/derivations/web", new ByteArrayContent(manualBytes), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reader.PutAsync($"/api/v1/assets/{image.AssetId}/derivations/print", new ByteArrayContent(manualBytes), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // marked confidential: the rendition with the triggered watermark is made again on its own
        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{image.AssetId}/metadata", new Dictionary<string, List<MetadataValue>>
        {
            ["confidential"] = [new(V: JsonSerializer.SerializeToElement(true))],
        }, Ct)).EnsureSuccessStatusCode();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while ((await DerivationsAsync(image.AssetId)).Single(d => d.Kind == "social").State != DerivationStates.Ready || (await CenterAsync(reader, image.AssetId, "social")).B < 200)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the confidential watermark was not applied");
            await Task.Delay(250, Ct);
        }

        // rendering again keeps the hand made rendition; without a watermark it needs the download right
        (await Admin.PostAsync($"/api/v1/assets/{image.AssetId}/derivations/rerender", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DerivationsAsync(image.AssetId)).Single(d => d.Kind == "print").IsManual.ShouldBeTrue();
        (await reader.GetAsync($"/api/v1/assets/{image.AssetId}/derivations/print", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var download = await Admin.GetAsync($"/api/v1/assets/{image.AssetId}/derivations/print?download=true", Ct);
        download.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("red-print.png");
        (await download.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(manualBytes);
    }

    [Fact]
    public async Task Crops_feed_renditions_or_become_variants_with_the_source_metadata()
    {
        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("caption", "Caption"), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync("/api/v1/derivation-types", new DerivationTypeRequest("square", "Square", 200, 200, DerivationFormats.Png), Ct)).EnsureSuccessStatusCode();
        var folder = await FolderAsync("Crops");
        using var picture = new ImageMagick.MagickImage(ImageMagick.MagickColors.Red, 800, 400);
        using (var right = new ImageMagick.MagickImage(ImageMagick.MagickColors.Blue, 400, 400))
        {
            picture.Composite(right, 400, 0);
        }
        var image = await UploadAsync(Admin, "halves.png", picture.ToByteArray(ImageMagick.MagickFormat.Png), folder);
        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{image.AssetId}/metadata", new Dictionary<string, List<MetadataValue>>
        {
            ["caption"] = [new(V: JsonSerializer.SerializeToElement("Two halves"))],
        }, Ct)).EnsureSuccessStatusCode();
        await DerivationsAsync(image.AssetId);
        var reader = await UserWithFolderRoleAsync(folder, NodePermissions.Read);

        (await Admin.PutAsJsonAsync($"/api/v1/assets/{image.AssetId}/derivations/square/crop", new CropArea(0.5, 0, 0.6, 1), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reader.PutAsJsonAsync($"/api/v1/assets/{image.AssetId}/derivations/square/crop", new CropArea(0.5, 0, 0.5, 1), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.PutAsJsonAsync($"/api/v1/assets/{image.AssetId}/derivations/square/crop", new CropArea(0.5, 0, 0.5, 1), Ct)).EnsureSuccessStatusCode();
        (await DerivationsAsync(image.AssetId)).Single(d => d.Kind == "square").ShouldBe(new DerivationDto("square", DerivationStates.Ready, 200, 200, "image/png"), new SizeIgnored());
        (await CenterAsync(Admin, image.AssetId, "square")).B.ShouldBeGreaterThan((byte)200, "the right half was cropped");

        // rendering again keeps the crop
        (await Admin.PostAsync($"/api/v1/assets/{image.AssetId}/derivations/rerender", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DerivationsAsync(image.AssetId)).Single(d => d.Kind == "square").Width.ShouldBe(200);
        (await CenterAsync(Admin, image.AssetId, "square")).B.ShouldBeGreaterThan((byte)200);

        (await reader.PostAsJsonAsync($"/api/v1/assets/{image.AssetId}/variants", new CropVariantRequest(new CropArea(0, 0, 0.5, 1)), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var created = await (await Admin.PostAsJsonAsync($"/api/v1/assets/{image.AssetId}/variants", new CropVariantRequest(new CropArea(0, 0, 0.5, 1), "Left half.jpg"), Ct))
            .Content.ReadFromJsonAsync<CropVariantDto>(Ct);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        AssetDto variant;
        while ((variant = (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{created!.AssetId}", Ct))!).Status == AssetStatuses.Importing)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the variant was not created");
            await Task.Delay(250, Ct);
        }

        variant.Status.ShouldBe(AssetStatuses.Ready);
        variant.CurrentVersion!.FileName.ShouldBe("Left half.png");
        variant.Name.ShouldBe("Left half.png");
        using (var cropped = new ImageMagick.MagickImage(await Admin.GetByteArrayAsync($"/api/v1/assets/{variant.Id}/download", Ct)))
        {
            (cropped.Width, cropped.Height).ShouldBe((400u, 400u));
            cropped.GetPixels().GetPixel(200, 200).ToColor()!.R.ShouldBeGreaterThan((byte)200);
        }

        (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{variant.Id}/metadata", Ct))!.Properties.Keys.ShouldContain("caption");
        (await Admin.GetFromJsonAsync<EdgeDto[]>($"/api/v1/nodes/{image.AssetId}/edges", Ct))!
            .ShouldContain(e => e.Type == EdgeTypeKeys.Variant && e.FromNodeId == image.AssetId && e.ToNodeId == variant.Id);
        (await Admin.PostAsJsonAsync($"/api/v1/assets/{image.AssetId}/variants", new CropVariantRequest(new CropArea(0, 0, 0, 1)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // a downloader without the right to change the source cannot attach variants to it
        var downloader = await UserWithFolderRoleAsync(folder, NodePermissions.Read, AssetPermissions.Download, NodePermissions.Create);
        (await downloader.PostAsJsonAsync($"/api/v1/assets/{image.AssetId}/variants", new CropVariantRequest(new CropArea(0, 0, 0.5, 1)), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // an asset with its own permissions gets no variant the whole folder could see
        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{image.AssetId}/inheritance", new InheritanceRequest(true), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync($"/api/v1/assets/{image.AssetId}/variants", new CropVariantRequest(new CropArea(0, 0, 0.5, 1)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Several_assets_or_a_folder_download_as_one_zip_named_by_the_rule()
    {
        (await Admin.PostAsJsonAsync("/api/v1/metadata/definitions", new PropertyDefinitionRequest("caption", "Caption"), Ct)).EnsureSuccessStatusCode();
        var folder = await FolderAsync("Shoot");
        var day = (await (await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(folder, "Day 2"), Ct)).Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
        using var red = new ImageMagick.MagickImage(ImageMagick.MagickColors.Red, 60, 40);
        var redBytes = red.ToByteArray(ImageMagick.MagickFormat.Png);
        var first = await UploadAsync(Admin, "a.png", redBytes, folder);
        var second = await UploadAsync(Admin, "b.png", redBytes, day);
        (await Admin.PutAsJsonAsync($"/api/v1/nodes/{first.AssetId}/metadata", new Dictionary<string, List<MetadataValue>>
        {
            ["caption"] = [new(V: JsonSerializer.SerializeToElement("Beach"))],
        }, Ct)).EnsureSuccessStatusCode();
        await DerivationsAsync(first.AssetId);
        await DerivationsAsync(second.AssetId);

        (await Admin.PostAsJsonAsync("/api/v1/file-namings", new FileNamingRequest("Broken", "{nope}"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/file-namings", new FileNamingRequest("Shouting", "{Name}"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/file-namings", new FileNamingRequest("Caption", "{meta:caption}-{seq}", FileNamingCasings.Lower, IsDefault: true), Ct)).EnsureSuccessStatusCode();
        (await (await Admin.PostAsJsonAsync("/api/v1/file-namings/preview", new FileNamingPreviewRequest("{meta:caption}_{name}", [first.AssetId]), Ct))
            .Content.ReadFromJsonAsync<string[]>(Ct))!.ShouldBe(["Beach_a.png"]);

        var package = (await (await Admin.PostAsJsonAsync("/api/v1/downloads", new DownloadPackageRequest(FolderId: folder), Ct)).Content.ReadFromJsonAsync<DownloadPackageDto>(Ct))!;
        package.Count.ShouldBe(2);
        using var anonymous = api.Factory.CreateClient();
        using var zipResponse = await anonymous.GetAsync(package.Url, Ct);
        zipResponse.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(await zipResponse.Content.ReadAsByteArrayAsync(Ct))))
        {
            zip.Entries.Select(e => e.FullName).ShouldContain(name => System.Text.RegularExpressions.Regex.IsMatch(name, "^beach-00[12]\\.png$"));
            zip.Entries.Select(e => e.FullName).ShouldContain(name => System.Text.RegularExpressions.Regex.IsMatch(name, "^Day 2/-00[12]\\.png$"));
            using var entry = new MemoryStream();
            await using (var content = zip.Entries.First().Open())
            {
                await content.CopyToAsync(entry, Ct);
            }

            entry.ToArray().ShouldBe(redBytes);
        }

        // single downloads follow the default rule too
        using (var single = await Admin.GetAsync($"/api/v1/assets/{first.AssetId}/download", Ct))
        {
            single.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("beach-001.png");
        }

        // readers get renditions, originals need the download right
        var reader = await UserWithFolderRoleAsync(folder, NodePermissions.Read);
        (await reader.PostAsJsonAsync("/api/v1/downloads", new DownloadPackageRequest([first.AssetId, second.AssetId]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var thumbnails = (await (await reader.PostAsJsonAsync("/api/v1/downloads", new DownloadPackageRequest([first.AssetId, second.AssetId], Kind: DerivationKinds.Thumbnail), Ct))
            .Content.ReadFromJsonAsync<DownloadPackageDto>(Ct))!;
        thumbnails.Count.ShouldBe(2);
        (await anonymous.GetAsync($"/api/v1/downloads/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // titles go into ImageMagick literally, '@file' reads nothing
        using var sheet = await reader.PostAsJsonAsync("/api/v1/downloads/contact-sheet", new ContactSheetRequest(FolderId: folder, Title: "@appsettings.json 100%[w]"), Ct);
        sheet.StatusCode.ShouldBe(HttpStatusCode.OK);
        sheet.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        System.Text.Encoding.ASCII.GetString((await sheet.Content.ReadAsByteArrayAsync(Ct))[..5]).ShouldBe("%PDF-");
    }

    [Fact]
    public async Task Zip_archives_unpack_into_folders_without_junk_or_forbidden_files()
    {
        var folder = await FolderAsync("Archives");
        using var picture = new ImageMagick.MagickImage(ImageMagick.MagickColors.Green, 30, 30);
        using var bundle = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(bundle, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] content)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(content);
            }

            Add("photos/one.png", picture.ToByteArray(ImageMagick.MagickFormat.Png));
            Add("photos/deep/two.txt", "hello"u8.ToArray());
            Add("__MACOSX/photos/._one.png", [1, 2, 3]);
            Add(".DS_Store", [1, 2, 3]);
            Add("tool.exe", [0x4D, 0x5A]);
            Add("bad.png", [1, 2, 3, 4]);
            Add(string.Join('/', Enumerable.Repeat("deeper", 40)) + "/x.txt", "x"u8.ToArray());
        }

        var archive = await UploadAsync(Admin, "bundle.zip", bundle.ToArray(), folder);
        var other = await UploadAsync(Admin, "note.txt", "x"u8.ToArray(), folder);
        var reader = await UserWithFolderRoleAsync(folder, NodePermissions.Read);
        (await Admin.PostAsJsonAsync($"/api/v1/assets/{other.AssetId}/extract", new ExtractArchiveRequest(), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reader.PostAsJsonAsync($"/api/v1/assets/{archive.AssetId}/extract", new ExtractArchiveRequest(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var started = (await (await Admin.PostAsJsonAsync($"/api/v1/assets/{archive.AssetId}/extract", new ExtractArchiveRequest(), Ct)).Content.ReadFromJsonAsync<ArchiveExtractionDto>(Ct))!;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        ArchiveExtractionDto done;
        while ((done = (await Admin.GetFromJsonAsync<ArchiveExtractionDto>($"/api/v1/extractions/{started.Id}", Ct))!).State is ExtractionStates.Queued or ExtractionStates.Running)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the archive was not unpacked");
            await Task.Delay(250, Ct);
        }

        done.ShouldSatisfyAllConditions(d => d.State.ShouldBe(ExtractionStates.Completed), d => d.Extracted.ShouldBe(2), d => d.Filtered.ShouldBe(2), d => d.Failed.ShouldBe(3));
        done.Problems.ShouldContain(p => p.StartsWith("tool.exe"));
        var children = (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{folder}/children", Ct))!.Items;
        var photos = children.Single(c => c.Name == "photos");
        var inPhotos = (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{photos.Id}/children", Ct))!.Items;
        inPhotos.Select(c => c.Name).ShouldBe(["deep", "one.png"], ignoreOrder: true);
        (await DerivationsAsync(inPhotos.Single(c => c.Name == "one.png").Id)).Single(d => d.Kind == DerivationKinds.Thumbnail).State.ShouldBe(DerivationStates.Ready);
    }

    [Fact]
    public async Task Only_originals_that_cannot_run_anything_are_shown_inline()
    {
        var folder = await FolderAsync("Inline");
        var pdf = await UploadAsync(Admin, "doc.pdf", MinimalPdf(), folder);
        var archive = await UploadAsync(Admin, "bundle.zip", [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00], folder);
        var reader = await UserWithFolderRoleAsync(folder, NodePermissions.Read);

        using var view = await Admin.GetAsync($"/api/v1/assets/{pdf.AssetId}/view", Ct);
        view.StatusCode.ShouldBe(HttpStatusCode.OK);
        view.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        view.Content.Headers.ContentDisposition.ShouldBeNull();
        view.Headers.GetValues("Content-Security-Policy").Single().ShouldStartWith("sandbox");
        view.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        (await Admin.GetAsync($"/api/v1/assets/{archive.AssetId}/view", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reader.GetAsync($"/api/v1/assets/{pdf.AssetId}/view", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{pdf.AssetId}", Ct))!.CanDownload.ShouldBeTrue();
        (await reader.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{pdf.AssetId}", Ct))!.CanDownload.ShouldBeFalse();
    }

    private async Task<Guid> WatermarkAsync(WatermarkRequest request)
    {
        var watermark = (await (await Admin.PostAsJsonAsync("/api/v1/watermarks", request, Ct)).Content.ReadFromJsonAsync<WatermarkDto>(Ct))!;
        using var blue = new ImageMagick.MagickImage(ImageMagick.MagickColors.Blue, 100, 100);
        (await Admin.PutAsync($"/api/v1/watermarks/{watermark.Id}/image", new ByteArrayContent(blue.ToByteArray(ImageMagick.MagickFormat.Png)), Ct)).EnsureSuccessStatusCode();
        return watermark.Id;
    }

    private static async Task<ImageMagick.IMagickColor<byte>> CenterAsync(HttpClient client, Guid assetId, string kind)
    {
        using var response = await client.GetAsync($"/api/v1/assets/{assetId}/derivations/{kind}", Ct);
        response.EnsureSuccessStatusCode();
        using var image = new ImageMagick.MagickImage(await response.Content.ReadAsByteArrayAsync(Ct));
        return image.GetPixels().GetPixel((int)image.Width / 2, (int)image.Height / 2).ToColor()!;
    }

    /// <summary>Compares renditions without their byte size, which depends on the encoder.</summary>
    private sealed class SizeIgnored : IEqualityComparer<DerivationDto>
    {
        public bool Equals(DerivationDto? x, DerivationDto? y) => x! with { Size = 0 } == y! with { Size = 0 };

        public int GetHashCode(DerivationDto obj) => (obj with { Size = 0 }).GetHashCode();
    }

    [Fact]
    public async Task Audio_gets_a_waveform_and_an_mp3()
    {
        Assert.SkipUnless(Tool("ffmpeg") is not null, "ffmpeg is not installed");
        var folder = await FolderAsync("Sounds");

        var sound = await UploadAsync(Admin, "tone.wav", Wave(seconds: 1), folder);

        var rendered = await DerivationsAsync(sound.AssetId);
        rendered.ShouldAllBe(d => d.State == DerivationStates.Ready);
        using var media = await Admin.GetAsync($"/api/v1/assets/{sound.AssetId}/derivations/{DerivationKinds.Media}", Ct);
        media.Content.Headers.ContentType!.MediaType.ShouldBe("audio/mpeg");
    }

    [Fact]
    public async Task Models_are_shown_inline_and_rendered_by_blender()
    {
        var folder = await FolderAsync("Models");
        var model = await UploadAsync(Admin, "cube.stl", CubeStl(), folder);

        using var view = await Admin.GetAsync($"/api/v1/assets/{model.AssetId}/view", Ct);
        view.StatusCode.ShouldBe(HttpStatusCode.OK);
        view.Content.Headers.ContentType!.MediaType.ShouldBe("model/stl");
        (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{model.AssetId}", Ct))!.AssetType.ShouldBe(AssetTypeKeys.Model);

        var rendered = await DerivationsAsync(model.AssetId);
        if (Tool("blender") is null)
        {
            rendered.ShouldAllBe(d => d.State == DerivationStates.NotSupported);
            Assert.Skip("Blender is not installed");
        }

        rendered.Single(d => d.Kind == DerivationKinds.Preview).State.ShouldBe(DerivationStates.Ready);
    }

    [Fact]
    public async Task Models_that_refer_to_other_files_are_not_rendered()
    {
        var work = Directory.CreateTempSubdirectory("shareme-model-").FullName;
        try
        {
            var gltf = Path.Combine(work, "original.gltf");
            await File.WriteAllTextAsync(gltf, """{"asset":{"version":"2.0"},"buffers":[{"uri":"/dev/zero","byteLength":4}]}""", Ct);
            await Should.ThrowAsync<Shareme.Processing.ToolFailedException>(() => Shareme.Processing.ModelSource.SelfContainedAsync(gltf, work, Ct));

            var embedded = Path.Combine(work, "embedded.gltf");
            await File.WriteAllTextAsync(embedded, """{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,AAAAAA==","byteLength":4}]}""", Ct);
            (await Shareme.Processing.ModelSource.SelfContainedAsync(embedded, work, Ct)).ShouldBe(embedded);

            var obj = Path.Combine(work, "original.obj");
            await File.WriteAllLinesAsync(obj, ["mtllib /etc/passwd", "v 0 0 0", "v 1 0 0", "v 0 1 0", "f 1 2 3"], Ct);
            (await File.ReadAllTextAsync(await Shareme.Processing.ModelSource.SelfContainedAsync(obj, work, Ct), Ct)).ShouldNotContain("mtllib");
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>An ASCII STL cube of twelve triangles.</summary>
    private static byte[] CubeStl()
    {
        int[][] faces = [[0, 1, 2], [0, 2, 3], [4, 6, 5], [4, 7, 6], [0, 4, 5], [0, 5, 1], [1, 5, 6], [1, 6, 2], [2, 6, 7], [2, 7, 3], [3, 7, 4], [3, 4, 0]];
        int[][] corners = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0], [0, 0, 1], [1, 0, 1], [1, 1, 1], [0, 1, 1]];
        var stl = new System.Text.StringBuilder("solid cube\n");
        foreach (var face in faces)
        {
            stl.Append("facet normal 0 0 0\nouter loop\n");
            foreach (var corner in face)
            {
                stl.Append($"vertex {corners[corner][0]} {corners[corner][1]} {corners[corner][2]}\n");
            }

            stl.Append("endloop\nendfacet\n");
        }

        return System.Text.Encoding.ASCII.GetBytes(stl.Append("endsolid cube\n").ToString());
    }

    [Fact]
    public async Task Pdfs_render_their_first_page()
    {
        Assert.SkipUnless(Tool("gs", "gswin64c") is not null, "Ghostscript is not installed");
        var folder = await FolderAsync("Documents");

        var pdf = await UploadAsync(Admin, "page.pdf", MinimalPdf(), folder);

        (await DerivationsAsync(pdf.AssetId)).Single(d => d.Kind == DerivationKinds.Preview).State.ShouldBe(DerivationStates.Ready);
    }

    [Fact]
    public async Task Office_documents_render_through_libreoffice()
    {
        Assert.SkipUnless(Tool("soffice", "libreoffice") is not null && Tool("gs", "gswin64c") is not null, "LibreOffice or Ghostscript is not installed");
        var folder = await FolderAsync("Notes");

        var note = await UploadAsync(Admin, "note.txt", "Quarterly notes\nLine two"u8.ToArray(), folder);

        (await DerivationsAsync(note.AssetId)).Single(d => d.Kind == DerivationKinds.Thumbnail).State.ShouldBe(DerivationStates.Ready);
    }

    [Fact]
    public async Task Images_carry_technical_metadata()
    {
        var folder = await FolderAsync("Facts");
        using var picture = new ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 300, 200);

        var image = await UploadAsync(Admin, "facts.png", picture.ToByteArray(ImageMagick.MagickFormat.Png), folder);
        await DerivationsAsync(image.AssetId);

        var facts = (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{image.AssetId}", Ct))!.CurrentVersion!.TechnicalMetadata!;
        facts.Values.ShouldContain("300");
        facts.Values.ShouldContain("200");
    }

    private static string? Tool(params string[] names)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", string.Empty } : [string.Empty];
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => names.SelectMany(name => extensions.Select(extension => Path.Combine(directory, name + extension))))
            .FirstOrDefault(File.Exists);
    }

    private static async Task RunAsync(string executable, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo(executable) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        var error = await process.StandardError.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);
        process.ExitCode.ShouldBe(0, error);
    }

    /// <summary>A mono 16 bit PCM wave with a 440 Hz tone.</summary>
    private static byte[] Wave(int seconds)
    {
        const int rate = 8000;
        var samples = rate * seconds;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + (samples * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>A one page PDF with a line of text, offsets computed so the cross reference table is valid.</summary>
    private static byte[] MinimalPdf()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            "<< /Length 44 >>\nstream\nBT /F1 24 Tf 40 100 Td (Hello PDF) Tj ET\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private async Task<IReadOnlyList<DerivationDto>> DerivationsAsync(Guid assetId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            var derivations = (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{assetId}", Ct))!.Derivations;
            if (derivations.Count > 0 && derivations.All(d => d.State != DerivationStates.Pending))
            {
                return derivations;
            }

            DateTime.UtcNow.ShouldBeLessThan(deadline, "renditions were not produced in time");
            await Task.Delay(250, Ct);
        }
    }

    [Fact]
    public async Task Uploaded_files_are_found_by_their_file_name()
    {
        var folder = await FolderAsync("Findable");
        var result = await UploadAsync(Admin, "pixel.png", Png(200), folder);

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (true)
        {
            var found = await (await Admin.PostAsJsonAsync("/api/v1/search", new Shareme.Contracts.Search.SearchRequest("pixel"), Ct)).Content.ReadFromJsonAsync<Shareme.Contracts.Search.SearchResponse>(Ct);
            if (found!.Items.Any(i => i.Id == result.AssetId))
            {
                return;
            }

            DateTime.UtcNow.ShouldBeLessThan(deadline, "the upload was not found");
            await Task.Delay(300, Ct);
        }
    }

    [Fact]
    public async Task A_download_link_works_without_signing_in()
    {
        var folder = await FolderAsync("Shared");
        var content = Png(700);
        var result = await UploadAsync(Admin, "link.png", content, folder);

        var link = await (await Admin.PostAsync($"/api/v1/assets/{result.AssetId}/download-link", null, Ct)).Content.ReadFromJsonAsync<DownloadLinkDto>(Ct);

        using var anonymous = api.Factory.CreateClient();
        using var response = await anonymous.GetAsync(link!.Url, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(content);
        (await anonymous.GetAsync("/api/v1/blobs/not-a-token", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Purging_an_asset_removes_its_stored_files()
    {
        var folder = await FolderAsync("Trash me");
        var result = await UploadAsync(Admin, "gone.png", Png(300), folder);
        var key = await BlobKeyAsync(result.VersionId);
        (await Admin.DeleteAsync($"/api/v1/nodes/{folder}", Ct)).EnsureSuccessStatusCode();
        (await Admin.DeleteAsync($"/api/v1/recycle-bin/{folder}", Ct)).EnsureSuccessStatusCode();

        await using var scope = api.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StorageCleanupJob>().ExecuteAsync(Ct);

        (await scope.ServiceProvider.GetRequiredService<IBlobStorage>().ExistsAsync(key, Ct)).ShouldBeFalse();
    }

    private async Task<string> BlobKeyAsync(Guid versionId)
    {
        await using var scope = api.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShareMeDbContext>();
        return await db.Set<AssetVersion>().IgnoreQueryFilters().Where(v => v.Id == versionId).Select(v => v.BlobKey).SingleAsync(Ct);
    }

    private static byte[] Png(int size)
    {
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        PngHeader.CopyTo(bytes, 0);
        return bytes;
    }

    private static async Task<UploadSessionDto> StartAsync(HttpClient client, StartUploadRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/uploads", request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadSessionDto>(Ct))!;
    }

    private static async Task ChunkAsync(HttpClient client, Guid session, int index, byte[] bytes)
    {
        using var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await client.PutAsync($"/api/v1/uploads/{session}/chunks/{index}", body, Ct)).EnsureSuccessStatusCode();
    }

    private static async Task<UploadResultDto> UploadAsync(HttpClient client, string fileName, byte[] content, Guid? folder = null, Guid? assetId = null)
    {
        var session = await StartAsync(client, new StartUploadRequest(fileName, content.Length, folder, assetId, Convert.ToHexStringLower(SHA256.HashData(content))));
        for (var index = 0; index < session.ChunkCount; index++)
        {
            await ChunkAsync(client, session.Id, index, content.Skip(index * session.ChunkSize).Take(session.ChunkSize).ToArray());
        }

        var done = await CompleteAsync(client, session.Id);
        done.State.ShouldBe(UploadStates.Completed, done.Error);
        return done.Result!;
    }

    private static async Task<UploadSessionDto> CompleteAsync(HttpClient client, Guid session)
    {
        (await client.PostAsync($"/api/v1/uploads/{session}/complete", null, Ct)).EnsureSuccessStatusCode();
        return await WaitAsync(client, session);
    }

    private static async Task<UploadSessionDto> WaitAsync(HttpClient client, Guid session)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var state = (await client.GetFromJsonAsync<UploadSessionDto>($"/api/v1/uploads/{session}", Ct))!;
            if (state.State is UploadStates.Completed or UploadStates.Failed)
            {
                return state;
            }

            DateTime.UtcNow.ShouldBeLessThan(deadline, "the upload was not processed in time");
            await Task.Delay(250, Ct);
        }
    }

    private async Task<Guid> FolderAsync(string name)
    {
        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        var response = await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(root, name), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
    }

    private async Task<HttpClient> UserWithFolderRoleAsync(Guid folder, params string[] permissions)
    {
        var email = $"{Guid.NewGuid():N}@acme.test";
        var user = (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!;
        var role = (await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Role " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct))!;
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await Admin.PostAsJsonAsync($"/api/v1/identity/resource-permissions/{NodePermissions.ResourceType}/{folder}",
            new GrantResourcePermissionRequest(PrincipalType.User, user.Id, role), Ct)).EnsureSuccessStatusCode();
        return api.As(user.Id, _setup.TenantId);
    }
}
