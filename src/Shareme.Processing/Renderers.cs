using System.Globalization;
using System.Text.Json;
using ImageMagick;

namespace Shareme.Processing;

/// <summary>Turns a file of some media type into a still image (for thumbnail and preview) and, for time based media, a browser friendly copy.</summary>
/// <summary>Image types read directly by the image library, always with the coder pinned from the checked file type.</summary>
internal static class ImageFormats
{
    public static readonly IReadOnlyDictionary<string, MagickFormat> ByMimeType = new Dictionary<string, MagickFormat>
    {
        ["image/jpeg"] = MagickFormat.Jpeg,
        ["image/png"] = MagickFormat.Png,
        ["image/gif"] = MagickFormat.Gif,
        ["image/webp"] = MagickFormat.WebP,
        ["image/tiff"] = MagickFormat.Tiff,
        ["image/bmp"] = MagickFormat.Bmp,
        ["image/vnd.adobe.photoshop"] = MagickFormat.Psd,
        ["image/heic"] = MagickFormat.Heic,
    };
}

internal interface IRenditionSource
{
    bool Supports(string mimeType);

    /// <summary>A path to an image the image pipeline can read, or null when the tool for it is not installed.</summary>
    Task<string?> StillAsync(string input, string workDirectory, CancellationToken cancellationToken);

    /// <summary>A playable copy (MP4, MP3) or null when this media has none or the tool is missing.</summary>
    Task<(string Path, string MimeType)?> MediaAsync(string input, string workDirectory, CancellationToken cancellationToken) => Task.FromResult<(string, string)?>(null);

    /// <summary>A short looping GIF, or null when this media has none or the tool is missing.</summary>
    Task<string?> AnimatedAsync(string input, string workDirectory, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    /// <summary>Pages rendered onto a fixed canvas get their empty margins cut off.</summary>
    bool TrimStill => false;
}

/// <summary>3D meshes rendered headless by Blender (as Sharedien does for FBX): camera fitted to the meshes, a sun light, transparent background.</summary>
internal sealed class ModelSource(ProcessingTools tools) : IRenditionSource
{
    // ponytail: FBX can name textures anywhere on the host and cannot be rewritten safely; it renders once Blender runs in a sandbox
    private static readonly HashSet<string> Types = ["model/gltf-binary", "model/gltf+json", "model/stl", "model/obj", "model/ply"];

    // importers of Blender 4.1+ first, older names as fallback; the input path and output path come after "--"
    private const string Script = """
        import bpy, math, sys
        source, output = sys.argv[sys.argv.index("--") + 1:][:2]
        bpy.ops.wm.read_factory_settings(use_empty=True)
        ext = source.lower().rsplit(".", 1)[-1]
        importers = {"glb": ["import_scene.gltf"], "gltf": ["import_scene.gltf"], "fbx": ["import_scene.fbx"],
                     "obj": ["wm.obj_import", "import_scene.obj"], "stl": ["wm.stl_import", "import_mesh.stl"], "ply": ["wm.ply_import", "import_mesh.ply"]}
        for name in importers[ext]:
            group, operator = name.split(".")
            try:
                getattr(getattr(bpy.ops, group), operator)(filepath=source)
                break
            except AttributeError:
                continue
        else:
            sys.exit("No importer for ." + ext)
        scene = bpy.context.scene
        meshes = [o for o in scene.objects if o.type == "MESH"]
        if not meshes:
            sys.exit("The file holds no meshes.")
        camera = bpy.data.objects.new("camera", bpy.data.cameras.new("camera"))
        scene.collection.objects.link(camera)
        scene.camera = camera
        camera.rotation_euler = (math.radians(65), 0, math.radians(45))
        for o in scene.objects:
            o.select_set(o in meshes)
        bpy.ops.view3d.camera_to_view_selected()
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        sun.data.energy = 3
        sun.rotation_euler = (math.radians(50), 0, math.radians(30))
        scene.collection.objects.link(sun)
        scene.render.engine = "CYCLES"
        scene.cycles.device = "CPU"
        scene.cycles.samples = 16
        scene.render.film_transparent = True
        scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1600, 1200, 100
        scene.render.image_settings.file_format = "PNG"
        scene.render.image_settings.color_mode = "RGBA"
        scene.render.filepath = output
        bpy.ops.render.render(write_still=True)
        """;

    public bool Supports(string mimeType) => Types.Contains(mimeType);

    public async Task<string?> StillAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        if (tools.Find(ProcessingTools.Blender) is not { } blender || Path.GetExtension(input) is not { Length: > 1 })
        {
            return null;
        }

        // an upload never comes with sidecar files: references to other files would read the host (or a network share)
        input = await SelfContainedAsync(input, workDirectory, cancellationToken);

        var script = Path.Combine(workDirectory, "render.py");
        await File.WriteAllTextAsync(script, Script, cancellationToken);
        var still = Path.Combine(workDirectory, "model.png");

        // factory settings: no user add-ons or startup scripts run
        var result = await ToolRunner.RunAsync(blender, ["-b", "--factory-startup", "-Y", "-noaudio", "-t", "2", "--python-exit-code", "2", "--python", script, "--", input, still],
            workDirectory, TimeSpan.FromMinutes(5), cancellationToken);
        return result.ExitCode == 0 && File.Exists(still) ? still : throw new ToolFailedException("The model could not be rendered: " + result.Error.Trim());
    }

    /// <summary>glTF only with embedded data (data: URIs or the GLB buffer), OBJ without its material library; anything else refers to no other files.</summary>
    internal static async Task<string> SelfContainedAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        switch (Path.GetExtension(input).ToLowerInvariant())
        {
            case ".obj":
                var cleaned = Path.Combine(workDirectory, "model.obj");
                var lines = (await File.ReadAllLinesAsync(input, cancellationToken)).Where(line => !line.TrimStart().StartsWith("mtllib", StringComparison.OrdinalIgnoreCase));
                await File.WriteAllLinesAsync(cleaned, lines, cancellationToken);
                return cleaned;
            case ".gltf":
                EnsureEmbedded(await File.ReadAllBytesAsync(input, cancellationToken));
                return input;
            case ".glb":
                var bytes = await File.ReadAllBytesAsync(input, cancellationToken);

                // 12 byte header, then the JSON chunk: length, type "JSON", data
                var length = bytes.Length >= 20 ? BitConverter.ToInt32(bytes, 12) : -1;
                if (length < 0 || 20 + (long)length > bytes.Length)
                {
                    throw new ToolFailedException("The model is not a valid GLB file.");
                }

                EnsureEmbedded(bytes.AsSpan(20, length).ToArray());
                return input;
            default:
                return input;
        }
    }

    private static void EnsureEmbedded(byte[] json)
    {
        System.Text.Json.Nodes.JsonNode? document;
        try
        {
            document = System.Text.Json.Nodes.JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ToolFailedException("The model is not valid glTF.");
        }

        var uris = new[] { "buffers", "images" }.SelectMany(list => document?[list] as System.Text.Json.Nodes.JsonArray ?? [])
            .Select(item => item?["uri"]?.GetValue<string>()).OfType<string>();
        if (uris.Any(uri => !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ToolFailedException("The model refers to other files; upload it as a single .glb with embedded textures.");
        }
    }
}

internal sealed class VideoSource(ProcessingTools tools) : IRenditionSource
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);

    public bool Supports(string mimeType) => mimeType.StartsWith("video/", StringComparison.Ordinal);

    public async Task<string?> StillAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        if (tools.Find(ProcessingTools.FFmpeg) is not { } ffmpeg)
        {
            return null;
        }

        var still = Path.Combine(workDirectory, "still.png");

        // a second in avoids black first frames; very short clips fall back to the first frame
        foreach (var at in new[] { "1", "0" })
        {
            await ToolRunner.RunAsync(ffmpeg, ["-nostdin", "-hide_banner", "-loglevel", "error", "-threads", "2", "-ss", at, "-i", input, "-frames:v", "1", "-y", still], workDirectory, TimeSpan.FromMinutes(2), cancellationToken);
            if (File.Exists(still) && new FileInfo(still).Length > 0)
            {
                return still;
            }
        }

        throw new ToolFailedException("No frame could be read from the video.");
    }

    /// <summary>One frame per second of the first 16 seconds, shown for 0.75 s each, 240 px, own palette (as Sharedien's animated thumbnail).</summary>
    public async Task<string?> AnimatedAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        if (tools.Find(ProcessingTools.FFmpeg) is not { } ffmpeg)
        {
            return null;
        }

        var output = Path.Combine(workDirectory, "animated.gif");
        var result = await ToolRunner.RunAsync(ffmpeg,
            ["-nostdin", "-hide_banner", "-loglevel", "error", "-threads", "2", "-t", "16", "-i", input,
             "-vf", "fps=1,setpts=0.75*PTS,scale=240:240:force_original_aspect_ratio=decrease:flags=lanczos,split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse",
             "-r", "4/3", "-loop", "0", "-an", "-y", output],
            workDirectory, TimeSpan.FromMinutes(3), cancellationToken);
        return result.ExitCode == 0 && File.Exists(output) ? output : throw new ToolFailedException("The animated preview could not be made: " + result.Error.Trim());
    }

    public async Task<(string Path, string MimeType)?> MediaAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        if (tools.Find(ProcessingTools.FFmpeg) is not { } ffmpeg)
        {
            return null;
        }

        // ponytail: one 720p H.264 preview capped at ten minutes; adaptive streaming renditions come later
        var output = Path.Combine(workDirectory, "preview.mp4");
        var result = await ToolRunner.RunAsync(ffmpeg,
            ["-nostdin", "-hide_banner", "-loglevel", "error", "-threads", "2", "-i", input, "-t", "600",
             "-vf", "scale=w='min(1280,iw)':h='min(1280,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2", "-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-pix_fmt", "yuv420p",
             "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", "-y", output],
            workDirectory, Limit, cancellationToken);
        return result.ExitCode == 0 && File.Exists(output) ? (output, "video/mp4") : throw new ToolFailedException("The video preview could not be encoded: " + result.Error.Trim());
    }
}

internal sealed class AudioSource(ProcessingTools tools) : IRenditionSource
{
    public bool Supports(string mimeType) => mimeType.StartsWith("audio/", StringComparison.Ordinal);

    public async Task<string?> StillAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        if (tools.Find(ProcessingTools.FFmpeg) is not { } ffmpeg)
        {
            return null;
        }

        var wave = Path.Combine(workDirectory, "wave.png");
        var result = await ToolRunner.RunAsync(ffmpeg, ["-nostdin", "-hide_banner", "-loglevel", "error", "-threads", "2", "-i", input, "-filter_complex", "showwavespic=s=1600x400:colors=0x3f51b5", "-frames:v", "1", "-y", wave],
            workDirectory, TimeSpan.FromMinutes(2), cancellationToken);
        return result.ExitCode == 0 && File.Exists(wave) ? wave : throw new ToolFailedException("No waveform could be drawn: " + result.Error.Trim());
    }

    public async Task<(string Path, string MimeType)?> MediaAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        if (tools.Find(ProcessingTools.FFmpeg) is not { } ffmpeg)
        {
            return null;
        }

        var output = Path.Combine(workDirectory, "preview.mp3");
        var result = await ToolRunner.RunAsync(ffmpeg, ["-nostdin", "-hide_banner", "-loglevel", "error", "-threads", "2", "-i", input, "-vn", "-t", "1800", "-c:a", "libmp3lame", "-b:a", "128k", "-y", output],
            workDirectory, TimeSpan.FromMinutes(5), cancellationToken);
        return result.ExitCode == 0 && File.Exists(output) ? (output, "audio/mpeg") : throw new ToolFailedException("The audio preview could not be encoded: " + result.Error.Trim());
    }
}

/// <summary>PDF, PostScript and EPS through Ghostscript in safe mode: the first page as PNG.</summary>
internal sealed class PdfSource(ProcessingTools tools) : IRenditionSource
{
    public bool Supports(string mimeType) => mimeType is "application/pdf" or "application/postscript";

    public bool TrimStill => true;

    public async Task<string?> StillAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        if (tools.Find(ProcessingTools.Ghostscript) is not { } gs)
        {
            return null;
        }

        var still = Path.Combine(workDirectory, "page.png");
        var result = await ToolRunner.RunAsync(gs,
            // a fixed canvas bounds the output whatever the page size claims; the margins are trimmed afterwards
            ["-dSAFER", "-dBATCH", "-dNOPAUSE", "-dQUIET", "-dEPSCrop", "-sDEVICE=png16m", "-g2000x2000", "-dFitPage", "-dPDFFitPage", "-dFirstPage=1", "-dLastPage=1",
             "-dTextAlphaBits=4", "-dGraphicsAlphaBits=4", "-sOutputFile=" + still, input],
            workDirectory, TimeSpan.FromMinutes(2), cancellationToken);
        return result.ExitCode == 0 && File.Exists(still) ? still : throw new ToolFailedException("The document could not be rendered: " + (result.Error + result.Output).Trim());
    }
}

/// <summary>Office and text documents through LibreOffice to PDF, then the PDF's first page.</summary>
internal sealed class OfficeSource(ProcessingTools tools, PdfSource pdf) : IRenditionSource
{
    private static readonly HashSet<string> Types =
    [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/msword", "application/vnd.ms-excel", "application/vnd.ms-powerpoint",
        "application/vnd.oasis.opendocument.text", "application/vnd.oasis.opendocument.spreadsheet", "application/vnd.oasis.opendocument.presentation",
        "application/rtf", "text/plain", "text/csv", "text/markdown",
    ];

    // documents must not reach the network (linked images, OLE links) nor run macros: all proxies point at a dead local port
    private const string LockedDownProfile = """
        <?xml version="1.0" encoding="UTF-8"?>
        <oor:items xmlns:oor="http://openoffice.org/2001/registry" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
        <item oor:path="/org.openoffice.Inet/Settings"><prop oor:name="ooInetProxyType" oor:op="fuse"><value>1</value></prop></item>
        <item oor:path="/org.openoffice.Inet/Settings"><prop oor:name="ooInetNoProxy" oor:op="fuse"><value></value></prop></item>
        <item oor:path="/org.openoffice.Inet/Settings"><prop oor:name="ooInetHTTPProxyName" oor:op="fuse"><value>127.0.0.1</value></prop></item>
        <item oor:path="/org.openoffice.Inet/Settings"><prop oor:name="ooInetHTTPProxyPort" oor:op="fuse"><value>1</value></prop></item>
        <item oor:path="/org.openoffice.Inet/Settings"><prop oor:name="ooInetHTTPSProxyName" oor:op="fuse"><value>127.0.0.1</value></prop></item>
        <item oor:path="/org.openoffice.Inet/Settings"><prop oor:name="ooInetHTTPSProxyPort" oor:op="fuse"><value>1</value></prop></item>
        <item oor:path="/org.openoffice.Inet/Settings"><prop oor:name="ooInetFTPProxyName" oor:op="fuse"><value>127.0.0.1</value></prop></item>
        <item oor:path="/org.openoffice.Inet/Settings"><prop oor:name="ooInetFTPProxyPort" oor:op="fuse"><value>1</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="DisableMacrosExecution" oor:op="fuse"><value>true</value></prop></item>
        </oor:items>
        """;

    public bool Supports(string mimeType) => Types.Contains(mimeType);

    public bool TrimStill => true;

    public async Task<string?> StillAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        if (tools.Find(ProcessingTools.LibreOffice) is not { } office || tools.Find(ProcessingTools.Ghostscript) is null)
        {
            return null;
        }

        // a profile per call keeps parallel conversions apart and carries the locked down settings
        var profileDirectory = Path.Combine(workDirectory, "profile");
        Directory.CreateDirectory(Path.Combine(profileDirectory, "user"));
        await File.WriteAllTextAsync(Path.Combine(profileDirectory, "user", "registrymodifications.xcu"), LockedDownProfile, cancellationToken);
        var profile = new Uri(profileDirectory).AbsoluteUri;
        var result = await ToolRunner.RunAsync(office, ["--headless", "--norestore", "--nolockcheck", $"-env:UserInstallation={profile}", "--convert-to", "pdf", "--outdir", workDirectory, input],
            workDirectory, TimeSpan.FromMinutes(3), cancellationToken);
        var converted = Path.Combine(workDirectory, Path.GetFileNameWithoutExtension(input) + ".pdf");
        return result.ExitCode == 0 && File.Exists(converted)
            ? await pdf.StillAsync(converted, workDirectory, cancellationToken)
            : throw new ToolFailedException("The document could not be converted: " + (result.Error + result.Output).Trim());
    }
}

/// <summary>Reads technical metadata (dimensions, duration, codecs, camera data) with ExifTool, or FFprobe and the image library when ExifTool is missing.</summary>
internal sealed class TechnicalMetadataReader(ProcessingTools tools)
{
    private const int MaxEntries = 200;
    private const int MaxValueLength = 500;

    public async Task<Dictionary<string, string>> ReadAsync(string input, string mimeType, string workDirectory, CancellationToken cancellationToken)
    {
        try
        {
            if (tools.Find(ProcessingTools.ExifTool) is { } exiftool)
            {
                var result = await ToolRunner.RunAsync(exiftool, ["-json", "-n", "-G", "-api", "largefilesupport=1", input], workDirectory, TimeSpan.FromMinutes(1), cancellationToken);
                if (result.ExitCode == 0 && JsonDocument.Parse(result.Output).RootElement is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > 0)
                {
                    return Flatten(array[0].EnumerateObject().Where(p => !p.Name.StartsWith("File:", StringComparison.Ordinal) && !p.Name.StartsWith("ExifTool:", StringComparison.Ordinal) && p.Name != "SourceFile"));
                }
            }

            if ((mimeType.StartsWith("video/", StringComparison.Ordinal) || mimeType.StartsWith("audio/", StringComparison.Ordinal)) && tools.Find(ProcessingTools.FFprobe) is { } ffprobe)
            {
                var result = await ToolRunner.RunAsync(ffprobe, ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", input], workDirectory, TimeSpan.FromMinutes(1), cancellationToken);
                return result.ExitCode == 0 ? FromProbe(JsonDocument.Parse(result.Output).RootElement) : [];
            }

            if (ImageFormats.ByMimeType.TryGetValue(mimeType, out var format))
            {
                var info = new MagickImageInfo(input, new MagickReadSettings { Format = format });
                return new Dictionary<string, string>
                {
                    ["Image:Width"] = info.Width.ToString(CultureInfo.InvariantCulture),
                    ["Image:Height"] = info.Height.ToString(CultureInfo.InvariantCulture),
                    ["Image:Format"] = info.Format.ToString(),
                    ["Image:ColorSpace"] = info.ColorSpace.ToString(),
                };
            }
        }
        catch (Exception exception) when (exception is JsonException or ToolFailedException or MagickException)
        {
            // metadata is a bonus; renditions go on without it
        }

        return [];
    }

    private static Dictionary<string, string> FromProbe(JsonElement probe)
    {
        var values = new List<JsonProperty>();
        if (probe.TryGetProperty("format", out var format))
        {
            values.AddRange(format.EnumerateObject().Where(p => p.Name is "duration" or "bit_rate" or "format_long_name").Select(p => p));
        }

        var result = Flatten(values.Select(p => p), "Format:");
        if (probe.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var kind = stream.TryGetProperty("codec_type", out var type) ? type.GetString() : "stream";
                foreach (var property in stream.EnumerateObject().Where(p => p.Name is "codec_name" or "width" or "height" or "sample_rate" or "channels" or "r_frame_rate" or "bit_rate"))
                {
                    result.TryAdd($"{kind}:{property.Name}", Text(property.Value));
                }
            }
        }

        return result;
    }

    private static Dictionary<string, string> Flatten(IEnumerable<JsonProperty> properties, string prefix = "")
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (result.Count >= MaxEntries || property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                continue;
            }

            var text = Text(property.Value);
            if (text.StartsWith("(Binary data", StringComparison.Ordinal) || text.StartsWith("base64:", StringComparison.Ordinal))
            {
                continue;
            }

            result[prefix + property.Name] = text.Length > MaxValueLength ? text[..MaxValueLength] : text;
        }

        return result;
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
}
