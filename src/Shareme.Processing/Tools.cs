using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Shareme.Processing;

/// <summary>External programs the renderers use; each can be configured under <c>Processing:Tools:{Name}</c> or is looked up on the PATH.</summary>
internal sealed class ProcessingTools(IConfiguration configuration, ILogger<ProcessingTools> logger)
{
    public const string FFmpeg = "FFmpeg";
    public const string FFprobe = "FFprobe";
    public const string Ghostscript = "Ghostscript";
    public const string LibreOffice = "LibreOffice";
    public const string ExifTool = "ExifTool";

    private static readonly Dictionary<string, string[]> Candidates = new()
    {
        [FFmpeg] = ["ffmpeg"],
        [FFprobe] = ["ffprobe"],
        [Ghostscript] = ["gs", "gswin64c", "gswin32c"],
        [LibreOffice] = ["soffice", "libreoffice", @"C:\Program Files\LibreOffice\program\soffice.exe"],
        [ExifTool] = ["exiftool"],
    };

    private readonly Dictionary<string, string?> _resolved = [];

    /// <summary>The full path of the tool, or null when it is not installed.</summary>
    public string? Find(string tool)
    {
        lock (_resolved)
        {
            if (!_resolved.TryGetValue(tool, out var path))
            {
                if (configuration[$"Processing:Tools:{tool}"] is { Length: > 0 } configured)
                {
                    path = Resolve(configured);
                    if (path is null)
                    {
                        // a typo would otherwise look exactly like "not installed"
                        logger.LogWarning("Processing:Tools:{Tool} is set to '{Path}', which does not exist; files needing it are not rendered.", tool, configured);
                    }
                }
                else
                {
                    path = Candidates[tool].Select(Resolve).FirstOrDefault(p => p is not null);
                }

                _resolved[tool] = path;
            }

            return path;
        }
    }

    private static string? Resolve(string name)
    {
        if (Path.IsPathRooted(name) || name.Contains('/') || name.Contains('\\'))
        {
            var full = Path.GetFullPath(name);
            return File.Exists(full) ? full : null;
        }

        var extensions = OperatingSystem.IsWindows() ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';').Prepend(string.Empty) : [string.Empty];
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, name + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

internal sealed record ToolResult(int ExitCode, string Output, string Error);

internal sealed class ToolFailedException(string message) : Exception(message);

/// <summary>Runs a tool with literal arguments (no shell), a time limit and captured output.</summary>
internal static class ToolRunner
{
    public static async Task<ToolResult> RunAsync(string executable, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new ToolFailedException($"{Path.GetFileName(executable)} could not be started.");
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(error, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // it ended on its own between the time limit and the kill
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new ToolFailedException($"{Path.GetFileName(executable)} did not finish within {timeout.TotalSeconds:0} s.");
        }

        process.WaitForExit();
        return new ToolResult(process.ExitCode, output.ToString(), error.ToString());
    }

    // ponytail: output is kept in memory up to 1 MB, enough for json metadata and error messages
    private static void Append(StringBuilder target, string? line)
    {
        if (line is not null && target.Length < 1_000_000)
        {
            target.AppendLine(line);
        }
    }
}
