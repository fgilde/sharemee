using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Coworkee.Application.Messaging;
using Coworkee.Core;
using Coworkee.Core.Results;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Metadata;
using Shareme.Metadata;

namespace Shareme.Automation;

/// <summary>Metadata values as text, for templates, conditions and tables.</summary>
internal static partial class MetadataText
{
    [GeneratedRegex(@"^([A-Za-z0-9_.\-]+)(?:\[([^\]]+)\])?(?:@(.+))?$")]
    private static partial Regex ColumnPattern();

    public static string Text(MetadataValue value) => value.List ?? value.V switch
    {
        { ValueKind: JsonValueKind.String } text => text.GetString() ?? string.Empty,
        { } other => other.GetRawText(),
        null => string.Empty,
    };

    public static bool IsMulti(PropertyDefinition definition) => definition.MultiValue || definition.ListMode == ListModes.Multi;

    /// <summary>The text as a value of the property's type; the metadata checks report what does not fit.</summary>
    public static MetadataValue Parse(PropertyDefinition definition, string text, string? lang = null, string? ctx = null)
    {
        if (definition.ListMode != ListModes.None && (!definition.AllowCustomValues || definition.ListValues.Any(v => v.Key == text)))
        {
            return new MetadataValue(Lang: lang, Ctx: ctx, List: text);
        }

        JsonElement value = definition.ValueType switch
        {
            PropertyValueTypes.Integer when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) => JsonSerializer.SerializeToElement(whole),
            PropertyValueTypes.Decimal when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => JsonSerializer.SerializeToElement(number),
            PropertyValueTypes.Boolean when bool.TryParse(text, out var flag) => JsonSerializer.SerializeToElement(flag),
            _ => JsonSerializer.SerializeToElement(text),
        };
        return new MetadataValue(Lang: lang, Ctx: ctx, V: value);
    }

    public static string Column(string key, string? lang, string? ctx) => key + (lang is null ? string.Empty : $"[{lang}]") + (ctx is null ? string.Empty : $"@{ctx}");

    public static (string Key, string? Lang, string? Ctx)? ParseColumn(string header) =>
        ColumnPattern().Match(header.Trim()) is { Success: true } match
            ? (match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : null, match.Groups[3].Success ? match.Groups[3].Value : null)
            : null;

    public static bool SameSlot(MetadataValue a, MetadataValue b) => a.Lang == b.Lang && a.Ctx == b.Ctx;

    public static bool Same(IReadOnlyList<MetadataValue>? a, IReadOnlyList<MetadataValue>? b) =>
        JsonSerializer.Serialize(a ?? []) == JsonSerializer.Serialize(b ?? []);
}

/// <summary>Writes through <see cref="SetNodeMetadata"/> in a scope of its own: a failed save stays out of the caller's context, and nothing throws.</summary>
internal static class MetadataWrites
{
    public static async Task<Error?> WriteAsync(IServiceScopeFactory scopes, SetNodeMetadata command, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, cancellationToken);
            return result.IsSuccess ? null : result.Error;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception is ConcurrencyConflictException
                ? Error.Conflict("nodes.conflict", "The node was changed at the same time.")
                : Error.Unexpected("metadata.not_saved", "The values could not be saved.");
        }
    }

    public static string Describe(Error error) => error.Details is { Count: > 0 } details
        ? string.Join("; ", details.Select(d => $"{d.Key}: {string.Join(" ", d.Value)}"))
        : error.Message;
}

/// <summary>Reads and writes tables as XLSX or CSV; the first row holds the column names.</summary>
internal static class Tables
{
    public const int MaxRows = DataLimits.MaxNodes + 1;
    public const int MaxColumns = 500;
    // ponytail: ClosedXML holds the whole sheet in memory; a streaming reader when imports get bigger than this
    private const long MaxUnpacked = 40L * 1024 * 1024;

    public static byte[] Write(string format, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        if (format == ExportFormats.Csv)
        {
            var text = new StringBuilder();
            foreach (var row in rows.Prepend(headers))
            {
                text.AppendJoin(',', row.Select(Quote)).Append("\r\n");
            }

            return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Metadata");
        var index = 1;
        foreach (var row in rows.Prepend(headers))
        {
            for (var column = 0; column < row.Count; column++)
            {
                // always text, so nothing in a value turns into a formula
                sheet.Cell(index, column + 1).SetValue(row[column]);
            }

            index++;
        }

        sheet.Row(1).Style.Font.Bold = true;
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    /// <summary>The rows of the first sheet or of the CSV; null when the file is neither or too big.</summary>
    public static List<string[]>? Read(byte[] content)
    {
        try
        {
            return content is [0x50, 0x4B, ..] ? ReadXlsx(content) : ReadCsv(content);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            return null;
        }
    }

    private static List<string[]>? ReadXlsx(byte[] content)
    {
        using (var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read))
        {
            if (zip.Entries.Sum(e => e.Length) > MaxUnpacked)
            {
                return null;
            }
        }

        using var workbook = new XLWorkbook(new MemoryStream(content));
        var sheet = workbook.Worksheets.First();
        if (sheet.LastRowUsed() is not { } last)
        {
            return [];
        }

        if (last.RowNumber() > MaxRows || sheet.LastColumnUsed()!.ColumnNumber() > MaxColumns)
        {
            return null;
        }

        var width = sheet.LastColumnUsed()!.ColumnNumber();
        var rows = new List<string[]>();
        for (var row = 1; row <= last.RowNumber(); row++)
        {
            rows.Add(Enumerable.Range(1, width).Select(column => Cell(sheet.Cell(row, column))).ToArray());
        }

        return rows;
    }

    private static string Cell(IXLCell cell)
    {
        // the values Excel saved, so formulas are not evaluated here
        var value = cell.CachedValue;
        return (value.Type switch
        {
            XLDataType.Number => value.GetNumber() is var number && number % 1 == 0 && Math.Abs(number) < 1e15
                ? ((long)number).ToString(CultureInfo.InvariantCulture)
                : value.GetNumber().ToString(CultureInfo.InvariantCulture),
            XLDataType.Boolean => value.GetBoolean() ? "true" : "false",
            XLDataType.DateTime => value.GetDateTime() is var date && date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            XLDataType.Blank => string.Empty,
            _ => value.ToString(CultureInfo.InvariantCulture),
        }).Trim();
    }

    /// <summary>Comma or semicolon separated (as Excel writes it in many locales), quoted as in RFC 4180.</summary>
    private static List<string[]>? ReadCsv(byte[] content)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(content).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            // Excel saves CSV in the ANSI code page in many locales
            text = Encoding.Latin1.GetString(content);
        }

        var firstLine = text.Split('\n', 2)[0];
        var separator = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"' && cell.Length == 0)
            {
                quoted = wasQuoted = true;
            }
            else if (c == separator)
            {
                row.Add(Unquote(cell, ref wasQuoted));
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                // blank lines stay as empty rows, so row numbers match the lines
                row.Add(Unquote(cell, ref wasQuoted));
                rows.Add([.. row]);

                row.Clear();
                if (rows.Count > MaxRows)
                {
                    return null;
                }
            }
            else
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(Unquote(cell, ref wasQuoted));
            rows.Add([.. row]);
        }

        return rows.Count > MaxRows || rows.Any(r => r.Length > MaxColumns) ? null : rows;
    }

    /// <summary>Quoted cells keep their spaces; the guard <see cref="Quote"/> puts in front of formula characters is removed.</summary>
    private static string Unquote(StringBuilder cell, ref bool wasQuoted)
    {
        var value = wasQuoted ? cell.ToString() : cell.ToString().Trim();
        cell.Clear();
        wasQuoted = false;
        return value.Length > 1 && value[0] == '\'' && "=+-@\t\r".Contains(value[1]) ? value[1..] : value;
    }

    /// <summary>Quoted when needed; values that Excel would run as a formula get a leading apostrophe.</summary>
    private static string Quote(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', ';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
