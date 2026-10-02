using Shareme.Contracts.Metadata;
using Shareme.Contracts.Search;

namespace Shareme.Contracts.Automation;

public static class DataPermissions
{
    /// <summary>Mass edit and import, up to the configured number of nodes per run.</summary>
    public const string MassEdit = "Automation.MassEdit";

    /// <summary>Raises the number of nodes per run to <see cref="DataLimits.MaxNodes"/>.</summary>
    public const string MassEditLarge = "Automation.MassEditLarge";
}

public static class DataLimits
{
    public const int MaxNodes = 10_000;
    public const int DefaultNodes = 1_000;
    public const long MaxImportBytes = 20L * 1024 * 1024;
}

public static class MassEditStrategies
{
    public const string Keep = "keep";

    /// <summary>The given values replace all values of the property.</summary>
    public const string Overwrite = "overwrite";

    /// <summary>The given values are added; single valued properties keep their values in other languages and contexts.</summary>
    public const string Merge = "merge";

    public const string Delete = "delete";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Keep, Overwrite, Merge, Delete };
}

/// <summary>Values as stored, or as <see cref="Texts"/> read like table cells (in the given language and context).</summary>
public sealed record PropertyMutation(string Key, string Strategy, IReadOnlyList<MetadataValue>? Values = null, IReadOnlyList<string>? Texts = null, string? Lang = null, string? Ctx = null);

/// <summary>Changes the nodes, or every hit of the search (as far as the limit goes).</summary>
public sealed record MassEditRequest(IReadOnlyList<PropertyMutation> Mutations, IReadOnlyList<Guid>? NodeIds = null, SearchRequest? Search = null, bool DryRun = false);

public static class DataJobKinds
{
    public const string MassEdit = "massEdit";
    public const string Import = "import";
    public const string Extraction = "extraction";
}

public static class DataJobResults
{
    public const string Executed = "executed";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
}

public sealed record DataJobDto(Guid Id, string Kind, string State, bool DryRun, int Total, int Executed, int Skipped, int Failed, string? Error, IReadOnlyList<DataJobEntryDto> Entries, DateTimeOffset CreatedAt);

/// <summary>One line of a report; <see cref="Row"/> and <see cref="Column"/> point into the imported file.</summary>
public sealed record DataJobEntryDto(Guid? NodeId, int? Row, string? Column, string Result, string? Message);

public static class ExportFormats
{
    public const string Xlsx = "xlsx";
    public const string Csv = "csv";
}

/// <summary>Exports the nodes, or everything below the folder; one column per property, language and context.</summary>
public sealed record ExportRequest(IReadOnlyList<Guid>? NodeIds = null, Guid? FolderId = null, string Format = ExportFormats.Xlsx);

/// <summary>Columns of exported and imported tables besides the properties.</summary>
public static class TableColumns
{
    public const string Id = "id";
    public const string Name = "name";
    public const string Type = "type";
    public const string Path = "path";

    /// <summary>Values of multi valued properties in one cell.</summary>
    public const string Separator = "; ";
}

public static class JobKinds
{
    public const string Rules = "rules";
    public const string Archive = "archive";
    public const string Reindex = "reindex";
}

/// <summary>One run of any kind; <see cref="Done"/> counts what was written or extracted, <see cref="Link"/> leads to its report.</summary>
public sealed record JobSummaryDto(Guid Id, string Kind, string State, int Total, int Done, int Failed, string? Error, DateTimeOffset CreatedAt, string Link);

/// <summary><see cref="DashboardUrl"/> is the Hangfire dashboard, for those allowed to see it.</summary>
public sealed record JobsOverviewDto(IReadOnlyList<JobSummaryDto> Runs, string? DashboardUrl);
