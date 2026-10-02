using System.Globalization;
using System.Text.Json;
using ImageMagick;

namespace Shareme.Processing;

/// <summary>Turns a file of some media type into a still image (for thumbnail and preview) and, for time based media, a browser friendly copy.</summary>
internal interface IRenditionSource
{
    bool Supports(string mimeType);

    /// <summary>A path to an image the image pipeline can read, or null when the tool for it is not installed.</summary>
    Task<string?> StillAsync(string input, string workDirectory, CancellationToken cancellationToken);

    /// <summary>A playable copy (MP4, MP3) or null when this media has none or the tool is missing.</summary>
    Task<(string Path, string MimeType)?> MediaAsync(string input, string workDirectory, CancellationToken cancellationToken) => Task.FromResult<(string, string)?>(null);
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
            await ToolRunner.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-ss", at, "-i", input, "-frames:v", "1", "-y", still], workDirectory, TimeSpan.FromMinutes(2), cancellationToken);
            if (File.Exists(still) && new FileInfo(still).Length > 0)
            {
                return still;
            }
        }

        throw new ToolFailedException("No frame could be read from the video.");
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
            ["-hide_banner", "-loglevel", "error", "-i", input, "-t", "600", "-vf", "scale='min(1280,iw)':-2", "-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-pix_fmt", "yuv420p",
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
        var result = await ToolRunner.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-i", input, "-filter_complex", "showwavespic=s=1600x400:colors=0x3f51b5", "-frames:v", "1", "-y", wave],
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
        var result = await ToolRunner.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-i", input, "-vn", "-t", "1800", "-c:a", "libmp3lame", "-b:a", "128k", "-y", output],
            workDirectory, TimeSpan.FromMinutes(5), cancellationToken);
        return result.ExitCode == 0 && File.Exists(output) ? (output, "audio/mpeg") : throw new ToolFailedException("The audio preview could not be encoded: " + result.Error.Trim());
    }
}

/// <summary>PDF, PostScript and EPS through Ghostscript in safe mode: the first page as PNG.</summary>
internal sealed class PdfSource(ProcessingTools tools) : IRenditionSource
{
    public bool Supports(string mimeType) => mimeType is "application/pdf" or "application/postscript";

    public async Task<string?> StillAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        if (tools.Find(ProcessingTools.Ghostscript) is not { } gs)
        {
            return null;
        }

        var still = Path.Combine(workDirectory, "page.png");
        var result = await ToolRunner.RunAsync(gs,
            ["-dSAFER", "-dBATCH", "-dNOPAUSE", "-dQUIET", "-dEPSCrop", "-sDEVICE=png16m", "-r150", "-dFirstPage=1", "-dLastPage=1", "-dTextAlphaBits=4", "-dGraphicsAlphaBits=4", "-sOutputFile=" + still, input],
            workDirectory, TimeSpan.FromMinutes(2), cancellationToken);
        return result.ExitCode == 0 && File.Exists(still) ? still : throw new ToolFailedException("The document could not be rendered: " + result.Error.Trim());
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

    public bool Supports(string mimeType) => Types.Contains(mimeType);

    public async Task<string?> StillAsync(string input, string workDirectory, CancellationToken cancellationToken)
    {
        if (tools.Find(ProcessingTools.LibreOffice) is not { } office || tools.Find(ProcessingTools.Ghostscript) is null)
        {
            return null;
        }

        // a profile per call keeps parallel conversions from fighting over one LibreOffice user directory
        var profile = new Uri(Path.Combine(workDirectory, "profile")).AbsoluteUri;
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
                var result = await ToolRunner.RunAsync(exiftool, ["-json", "-n", "-G", input], workDirectory, TimeSpan.FromMinutes(1), cancellationToken);
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

            if (mimeType.StartsWith("image/", StringComparison.Ordinal))
            {
                var info = new MagickImageInfo(input);
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
