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
        rendered.Single(d => d.Kind == DerivationKinds.Thumbnail).ShouldBe(new DerivationDto(DerivationKinds.Thumbnail, DerivationStates.Ready, 320, 160));
        rendered.Single(d => d.Kind == DerivationKinds.Preview).ShouldBe(new DerivationDto(DerivationKinds.Preview, DerivationStates.Ready, 1600, 800));
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
        using var media = await Admin.GetAsync($"/api/v1/assets/{video.AssetId}/derivations/{DerivationKinds.Media}", Ct);
        media.Content.Headers.ContentType!.MediaType.ShouldBe("video/mp4");
        (await Admin.GetFromJsonAsync<AssetDto>($"/api/v1/assets/{video.AssetId}", Ct))!.CurrentVersion!.TechnicalMetadata.ShouldNotBeNull().ShouldNotBeEmpty();
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
