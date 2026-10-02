using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shareme.Assets;
using Shareme.Contracts.Automation;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Graph;
using Shareme.Metadata;
using Shareme.Search;

namespace Shareme.Automation;

/// <summary>A mass edit or an import, with its report.</summary>
[NotAudited]
public sealed class DataJob : Entity, IMultiTenant
{
    public required string Kind { get; set; }

    public string State { get; set; } = RuleJobStates.Queued;

    public bool DryRun { get; set; }

    public List<Guid> NodeIds { get; set; } = [];

    public List<PropertyMutation> Mutations { get; set; } = [];

    public Guid? FolderId { get; set; }

    public string? BlobKey { get; set; }

    public int Total { get; set; }

    public int Executed { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public string? Error { get; set; }

    public List<DataJobEntryDto> Entries { get; set; } = [];

    public Guid StartedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid TenantId { get; set; }
}

internal static class DataOperationsEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/v1/mass-edits", (MassEditRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new StartMassEdit(body), ct).ToHttpResult())
            .WithTags("Data").RequireAuthorization();
        app.MapPut("/api/v1/imports", (Guid? folderId, bool? dryRun, HttpRequest request, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new StartImport(folderId, dryRun ?? false, request.Body), ct).ToHttpResult()).WithTags("Data").RequireAuthorization();
        app.MapPost("/api/v1/exports", async (ExportRequest body, HttpResponse response, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.SendAsync(new ExportMetadata(body), ct);
            if (!result.IsSuccess)
            {
                return result.Error!.ToProblem();
            }

            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.CacheControl = "private, no-store";
            return Results.File(result.Value.Content, result.Value.MimeType, result.Value.FileName);
        }).WithTags("Data").RequireAuthorization();

        var jobs = app.MapGroup("/api/v1/data-jobs").WithTags("Data").RequireAuthorization();
        jobs.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetDataJobs(), ct).ToHttpResult());
        jobs.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetDataJob(id), ct).ToHttpResult());
    }
}

[RequiresPermission(DataPermissions.MassEdit)]
public sealed record StartMassEdit(MassEditRequest Request) : ICommand<Result<DataJobDto>>;

[RequiresPermission(DataPermissions.MassEdit)]
public sealed record StartImport(Guid? FolderId, bool DryRun, Stream Content) : ICommand<Result<DataJobDto>>;

/// <summary>Everyone may export what they can read.</summary>
public sealed record ExportMetadata(ExportRequest Request) : IQuery<Result<BlobDownload>>;

public sealed record GetDataJobs : IQuery<Result<IReadOnlyList<DataJobDto>>>;

public sealed record GetDataJob(Guid Id) : IQuery<Result<DataJobDto>>;

internal sealed class DataHandlers(
    CoworkeeDbContext db, MetadataCatalog catalog, NodeAccess access, ICurrentUser currentUser, IDispatcher dispatcher, IBlobStorage storage, IBackgroundJobs jobs,
    IConfiguration configuration, TimeProvider clock)
    : IHandler<StartMassEdit, Result<DataJobDto>>,
      IHandler<StartImport, Result<DataJobDto>>,
      IHandler<ExportMetadata, Result<BlobDownload>>,
      IHandler<GetDataJobs, Result<IReadOnlyList<DataJobDto>>>,
      IHandler<GetDataJob, Result<DataJobDto>>
{
    public async Task<Result<DataJobDto>> HandleAsync(StartMassEdit command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var mutations = request.Mutations.Where(m => m.Strategy != MassEditStrategies.Keep).ToList();
        if (mutations.Count is 0 or > 50 || mutations.Select(m => m.Key).Distinct().Count() != mutations.Count)
        {
            return Error.Validation("Mutations", "Change 1 to 50 properties, each once.");
        }

        var definitions = (await catalog.DefinitionsAsync(cancellationToken)).ToDictionary(d => d.Key);
        mutations = mutations.Select(m => m.Values is null && m.Texts is { Count: > 0 } texts && definitions.TryGetValue(m.Key, out var d)
            ? m with { Values = texts.Where(t => t.Trim().Length > 0).Select(t => MetadataText.Parse(d, t.Trim(), m.Lang, m.Ctx)).ToList(), Texts = null }
            : m).ToList();
        foreach (var mutation in mutations)
        {
            if (!MassEditStrategies.All.Contains(mutation.Strategy))
            {
                return Error.Validation("Mutations", $"Unknown strategy '{mutation.Strategy}'.");
            }

            if (!definitions.TryGetValue(mutation.Key, out var definition) || !definition.Editable || definition.IsSystem)
            {
                return Error.Validation("Mutations", $"'{mutation.Key}' is no editable property.");
            }

            if (mutation.Strategy is MassEditStrategies.Overwrite or MassEditStrategies.Merge && mutation.Values is not { Count: > 0 })
            {
                return Error.Validation("Mutations", $"'{mutation.Key}' needs values to {mutation.Strategy}.");
            }
        }

        var limit = await LimitAsync(cancellationToken);
        List<Guid> nodeIds;
        if (request.NodeIds is { Count: > 0 } ids)
        {
            nodeIds = ids.Distinct().ToList();
        }
        else if (request.Search is { } search)
        {
            nodeIds = [];
            string? cursor = null;
            do
            {
                var page = await dispatcher.SendAsync(new SearchNodes(search with { Size = 200, Cursor = cursor, Facets = null }), cancellationToken);
                if (!page.IsSuccess)
                {
                    return page.Error!;
                }

                if (page.Value.Total > limit)
                {
                    return TooMany(limit);
                }

                nodeIds.AddRange(page.Value.Items.Select(i => i.Id));
                cursor = page.Value.Items.Count == 0 ? null : page.Value.Cursor;
            }
            while (cursor is not null && nodeIds.Count < limit);
        }
        else
        {
            return Error.Validation("NodeIds", "Choose nodes or a search.");
        }

        if (nodeIds.Count > limit)
        {
            return TooMany(limit);
        }

        var job = new DataJob { Kind = DataJobKinds.MassEdit, DryRun = request.DryRun, NodeIds = nodeIds, Mutations = mutations, StartedBy = currentUser.UserId ?? Guid.Empty, CreatedAt = clock.GetUtcNow() };
        return await EnqueueAsync(job, cancellationToken);
    }

    public async Task<Result<DataJobDto>> HandleAsync(StartImport command, CancellationToken cancellationToken)
    {
        if (await RenditionLimits.ReadAsync(command.Content, DataLimits.MaxImportBytes, cancellationToken) is not { } bytes || Tables.Read(bytes) is not { Count: > 0 } rows)
        {
            return Error.Validation("Content", $"Upload an XLSX or CSV file of up to {DataLimits.MaxImportBytes / 1024 / 1024} MB with column names in the first row.");
        }

        var headers = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        if (!headers.Contains(TableColumns.Id) && !(headers.Contains(TableColumns.Name) && command.FolderId.HasValue))
        {
            return Error.Validation("Content", "The file needs an 'id' column, or a 'name' column and a folder to match file names in.");
        }

        if (command.FolderId is { } folderId && !await access.CanAsync(NodePermissions.Read, folderId, cancellationToken))
        {
            return Error.NotFound("nodes.not_found", "The folder does not exist.");
        }

        var limit = await LimitAsync(cancellationToken);
        if (rows.Count - 1 > limit)
        {
            return TooMany(limit);
        }

        var job = new DataJob { Kind = DataJobKinds.Import, DryRun = command.DryRun, FolderId = command.FolderId, StartedBy = currentUser.UserId ?? Guid.Empty, CreatedAt = clock.GetUtcNow() };
        job.BlobKey = $"imports/{job.Id}";
        await storage.PutAsync(job.BlobKey, new MemoryStream(bytes), "application/octet-stream", cancellationToken);
        return await EnqueueAsync(job, cancellationToken);
    }

    public async Task<Result<BlobDownload>> HandleAsync(ExportMetadata query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        if (request.Format is not (ExportFormats.Xlsx or ExportFormats.Csv) || (request.NodeIds is { Count: > 0 }) == request.FolderId.HasValue || request.NodeIds is { Count: > DataLimits.MaxNodes })
        {
            return Error.Validation("Format", $"Export up to {DataLimits.MaxNodes} nodes or one folder as xlsx or csv.");
        }

        List<Guid> ids;
        if (request.FolderId is { } folderId)
        {
            if (!await access.CanAsync(NodePermissions.Read, folderId, cancellationToken))
            {
                return Error.NotFound("nodes.not_found", "The folder does not exist.");
            }

            ids = await db.Set<Node>().Where(n => n.Path.StartsWith(db.Set<Node>().Where(f => f.Id == folderId).Select(f => f.Path).First()) && n.Id != folderId)
                .OrderBy(n => n.Path).Select(n => n.Id).Take(DataLimits.MaxNodes + 1).ToListAsync(cancellationToken);
            if (ids.Count > DataLimits.MaxNodes)
            {
                return Error.Validation("FolderId", $"The folder holds more than {DataLimits.MaxNodes} nodes.");
            }
        }
        else
        {
            ids = request.NodeIds!.Distinct().ToList();
        }

        // ponytail: one access check per node; a batched check when exports of thousands of nodes get slow
        var readable = new List<Guid>();
        foreach (var id in ids)
        {
            if (await access.CanAsync(NodePermissions.Read, id, cancellationToken))
            {
                readable.Add(id);
            }
        }

        var nodes = await db.Set<Node>().AsNoTracking().Where(n => readable.Contains(n.Id)).OrderBy(n => n.Path).ToListAsync(cancellationToken);
        var types = await db.Set<NodeType>().AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Key, cancellationToken);
        var ancestorIds = nodes.SelectMany(n => n.PathIds).Distinct().ToList();
        var names = await db.Set<Node>().AsNoTracking().Where(n => ancestorIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, n => n.Name, cancellationToken);
        var definitions = await catalog.DefinitionsAsync(cancellationToken);
        var language = (await catalog.LanguagesAsync(cancellationToken)).FirstOrDefault(l => l.IsDefault)?.Code ?? MetadataCatalog.FallbackLanguage.Code;
        var presentTypes = nodes.Select(n => types[n.NodeTypeId]).ToHashSet();

        // a column per property, language and context in use; unused editable properties get an empty column to fill in
        var columns = new List<(string Key, string? Lang, string? Ctx)>();
        foreach (var definition in definitions.Where(d => presentTypes.Any(d.AppliesTo)))
        {
            var used = nodes.SelectMany(n => n.Metadata.GetValueOrDefault(definition.Key) ?? []).Select(v => (definition.Key, v.Lang, v.Ctx)).Distinct().OrderBy(c => c.Lang).ThenBy(c => c.Ctx).ToList();
            if (used.Count == 0 && definition.Editable && !definition.IsSystem && !definition.HasContext)
            {
                used.Add((definition.Key, definition.Localizable ? language : null, null));
            }

            columns.AddRange(used);
        }

        var headers = new[] { TableColumns.Id, TableColumns.Name, TableColumns.Type, TableColumns.Path }.Concat(columns.Select(c => MetadataText.Column(c.Key, c.Lang, c.Ctx))).ToList();
        var rows = nodes.Select(node => (IReadOnlyList<string>)new[]
            {
                node.Id.ToString(), node.Name, types[node.NodeTypeId],
                string.Join("/", node.PathIds.Take(node.PathIds.Count - 1).Select(id => names.GetValueOrDefault(id, "?"))),
            }
            .Concat(columns.Select(c => string.Join(TableColumns.Separator, (node.Metadata.GetValueOrDefault(c.Key) ?? []).Where(v => v.Lang == c.Lang && v.Ctx == c.Ctx).Select(MetadataText.Text))))
            .ToList());
        var content = Tables.Write(request.Format, headers, rows);
        var mime = request.Format == ExportFormats.Csv ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return new BlobDownload(new MemoryStream(content), $"metadata-{clock.GetUtcNow():yyyyMMdd-HHmm}.{request.Format}", mime);
    }

    // a user sees the runs they started, with the nodes and messages only they were allowed to see
    public async Task<Result<IReadOnlyList<DataJobDto>>> HandleAsync(GetDataJobs query, CancellationToken cancellationToken)
    {
        IReadOnlyList<DataJobDto> runs = (await db.Set<DataJob>().AsNoTracking().Where(j => j.StartedBy == currentUser.UserId).OrderByDescending(j => j.CreatedAt).Take(20).ToListAsync(cancellationToken))
            .Select(j => Map(j) with { Entries = [] }).ToList();
        return Result<IReadOnlyList<DataJobDto>>.Success(runs);
    }

    public async Task<Result<DataJobDto>> HandleAsync(GetDataJob query, CancellationToken cancellationToken) =>
        await db.Set<DataJob>().AsNoTracking().SingleOrDefaultAsync(j => j.Id == query.Id && j.StartedBy == currentUser.UserId, cancellationToken) is { } job
            ? Map(job)
            : Error.NotFound("data.job_not_found", "The run does not exist.");

    internal static DataJobDto Map(DataJob j) => new(j.Id, j.Kind, j.State, j.DryRun, j.Total, j.Executed, j.Skipped, j.Failed, j.Error, j.Entries, j.CreatedAt);

    private static Error TooMany(int limit) => Error.Validation("NodeIds", $"You may change up to {limit} nodes at once.");

    private async Task<int> LimitAsync(CancellationToken cancellationToken) =>
        await access.IsGlobalAsync(DataPermissions.MassEditLarge, cancellationToken)
            ? DataLimits.MaxNodes
            : Math.Clamp(configuration.GetValue("Shareme:MassEdit:MaxNodes", DataLimits.DefaultNodes), 1, DataLimits.MaxNodes);

    private async Task<Result<DataJobDto>> EnqueueAsync(DataJob job, CancellationToken cancellationToken)
    {
        db.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<DataJobRunner, Guid>(job.Id);
        return Map(job);
    }
}

/// <summary>Runs mass edits and imports as the user who started them; one node's failure does not stop the others.</summary>
internal sealed class DataJobRunner(CoworkeeDbContext db, MetadataCatalog catalog, NodeAccess access, IBlobStorage storage, IServiceScopeFactory scopes, ILogger<DataJobRunner> logger)
    : IBackgroundJob<Guid>
{
    private const int MaxEntries = 1000;

    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (await db.Set<DataJob>().Where(j => j.Id == jobId && j.State == RuleJobStates.Queued)
                .ExecuteUpdateAsync(u => u.SetProperty(j => j.State, RuleJobStates.Running), cancellationToken) == 0)
        {
            return;
        }

        var job = await db.Set<DataJob>().SingleAsync(j => j.Id == jobId, cancellationToken);
        try
        {
            var definitions = (await catalog.DefinitionsAsync(cancellationToken)).ToDictionary(d => d.Key);
            if (job.Kind == DataJobKinds.MassEdit)
            {
                await MassEditAsync(job, definitions, cancellationToken);
            }
            else
            {
                await ImportAsync(job, definitions, cancellationToken);
            }

            job.State = RuleJobStates.Completed;
        }
        catch (OperationCanceledException)
        {
            await db.Set<DataJob>().Where(j => j.Id == jobId).ExecuteUpdateAsync(u => u.SetProperty(j => j.State, RuleJobStates.Queued), CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Data run {JobId} failed.", jobId);
            job.State = RuleJobStates.Failed;
            job.Error = "The run stopped because of an internal error.";
        }

        if (job.State != RuleJobStates.Running && job.BlobKey is { } key)
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MassEditAsync(DataJob job, Dictionary<string, PropertyDefinition> definitions, CancellationToken cancellationToken)
    {
        job.Total = job.NodeIds.Count;
        foreach (var nodeId in job.NodeIds)
        {
            await GuardAsync(job, nodeId, null, async () =>
            {
                if (await ReadableAsync(nodeId, cancellationToken) is not { } node)
                {
                    Skip(job, nodeId, null, "Not visible to you.");
                    return;
                }

                var update = new Dictionary<string, List<MetadataValue>?>(StringComparer.Ordinal);
                foreach (var mutation in job.Mutations)
                {
                    var current = node.Metadata.GetValueOrDefault(mutation.Key) ?? [];
                    var next = mutation.Strategy switch
                    {
                        MassEditStrategies.Overwrite => mutation.Values!.ToList(),
                        MassEditStrategies.Merge => Merge(definitions[mutation.Key], current, mutation.Values!),
                        _ => [],
                    };
                    if (!MetadataText.Same(current, next))
                    {
                        update[mutation.Key] = next.Count == 0 ? null : next;
                    }
                }

                await WriteAsync(job, node.Id, null, update, MetadataSources.MassEdit, _ => null, cancellationToken);
            });
        }
    }

    private async Task ImportAsync(DataJob job, Dictionary<string, PropertyDefinition> definitions, CancellationToken cancellationToken)
    {
        await using var stream = await storage.OpenReadAsync(job.BlobKey!, cancellationToken) ?? throw new InvalidOperationException("The uploaded file is gone.");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var rows = Tables.Read(buffer.ToArray()) ?? throw new InvalidOperationException("The uploaded file cannot be read.");
        var headers = rows[0];
        var id = Array.FindIndex(headers, h => h.Trim().Equals(TableColumns.Id, StringComparison.OrdinalIgnoreCase));
        var name = Array.FindIndex(headers, h => h.Trim().Equals(TableColumns.Name, StringComparison.OrdinalIgnoreCase));

        // property columns; unknown ones are reported once and left out
        var columns = new List<(int Index, string Header, PropertyDefinition Definition, string? Lang, string? Ctx)>();
        for (var index = 0; index < headers.Length; index++)
        {
            var header = headers[index].Trim();
            if (header.Length == 0 || header.ToLowerInvariant() is TableColumns.Id or TableColumns.Name or TableColumns.Type or TableColumns.Path)
            {
                continue;
            }

            if (MetadataText.ParseColumn(header) is { } column && definitions.TryGetValue(column.Key, out var definition))
            {
                columns.Add((index, header, definition, column.Lang, column.Ctx));
            }
            else
            {
                Note(job, new DataJobEntryDto(null, 1, header, DataJobResults.Skipped, "No such property; the column is left out."));
            }
        }

        var children = job.FolderId is { } folderId && name >= 0
            ? (await db.Set<Node>().AsNoTracking().Where(n => n.ParentId == folderId).Select(n => new { n.Id, n.Name }).ToListAsync(cancellationToken))
                .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList(), StringComparer.OrdinalIgnoreCase)
            : [];
        job.Total = rows.Count - 1;
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var number = r + 1;
            string Cell(int index) => index >= 0 && index < row.Length ? row[index] : string.Empty;
            await GuardAsync(job, null, number, async () =>
            {
                Guid? target = Guid.TryParse(Cell(id), out var parsed) ? parsed
                    : children.TryGetValue(Cell(name), out var matches) && matches.Count == 1 ? matches[0] : null;
                if (target is null)
                {
                    Fail(job, null, number, null, Cell(id).Length > 0 ? "The id is not valid." : children.ContainsKey(Cell(name)) ? $"More than one node is named '{Cell(name)}'." : $"No node is named '{Cell(name)}' in the folder.");
                    return;
                }

                if (await ReadableAsync(target.Value, cancellationToken) is not { } node)
                {
                    Fail(job, target, number, null, "The node does not exist or is not visible to you.");
                    return;
                }

                // empty cells leave values as they are; a filled cell replaces the values of its language and context
                var update = new Dictionary<string, List<MetadataValue>?>(StringComparer.Ordinal);
                foreach (var group in columns.Where(c => Cell(c.Index).Length > 0).GroupBy(c => c.Definition.Key))
                {
                    var definition = group.First().Definition;
                    var current = node.Metadata.GetValueOrDefault(definition.Key) ?? [];
                    var next = current.Where(v => !group.Any(c => c.Lang == v.Lang && c.Ctx == v.Ctx)).ToList();
                    foreach (var column in group)
                    {
                        var texts = MetadataText.IsMulti(definition) ? Cell(column.Index).Split(TableColumns.Separator.Trim(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [Cell(column.Index)];
                        next.AddRange(texts.Select(t => MetadataText.Parse(definition, t, column.Lang, column.Ctx)));
                    }

                    if (!MetadataText.Same(current, next))
                    {
                        update[definition.Key] = next;
                    }
                }

                await WriteAsync(job, node.Id, number, update, MetadataSources.Import,
                    key => string.Join(", ", columns.Where(c => c.Definition.Key == key).Select(c => c.Header)) is { Length: > 0 } names ? names : key, cancellationToken);
            });
        }
    }

    /// <summary>Single valued properties keep values in other languages and contexts; multi valued ones gain what they lack.</summary>
    private static List<MetadataValue> Merge(PropertyDefinition definition, List<MetadataValue> current, IReadOnlyList<MetadataValue> values) =>
        MetadataText.IsMulti(definition)
            ? [.. current, .. values.Where(v => !current.Any(c => MetadataText.Same([c], [v])))]
            : [.. current.Where(c => !values.Any(v => MetadataText.SameSlot(c, v))), .. values];

    private async Task WriteAsync(DataJob job, Guid nodeId, int? row, Dictionary<string, List<MetadataValue>?> update, string source, Func<string, string?> column, CancellationToken cancellationToken)
    {
        if (update.Count == 0)
        {
            Skip(job, nodeId, row, "Nothing to change.");
            return;
        }

        if (await MetadataWrites.WriteAsync(scopes, new SetNodeMetadata(nodeId, update, Replace: false, source, ValidateOnly: job.DryRun), cancellationToken) is not { } error)
        {
            job.Executed++;
            Note(job, new DataJobEntryDto(nodeId, row, null, DataJobResults.Executed, $"{(job.DryRun ? "Would change" : "Changed")} {string.Join(", ", update.Keys)}."));
            return;
        }

        job.Failed++;
        if (error.Details is { Count: > 0 } details)
        {
            foreach (var (key, messages) in details)
            {
                Note(job, new DataJobEntryDto(nodeId, row, column(key) ?? key, DataJobResults.Failed, string.Join(" ", messages)));
            }
        }
        else
        {
            Note(job, new DataJobEntryDto(nodeId, row, null, DataJobResults.Failed, error.Message));
        }
    }

    private async Task GuardAsync(DataJob job, Guid? nodeId, int? row, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Data run {JobId} failed on node {NodeId} row {Row}.", job.Id, nodeId, row);
            Fail(job, nodeId, row, null, "An internal error occurred.");
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Node?> ReadableAsync(Guid nodeId, CancellationToken cancellationToken) =>
        await access.CanAsync(NodePermissions.Read, nodeId, cancellationToken)
            ? await db.Set<Node>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == nodeId, cancellationToken)
            : null;

    private static void Skip(DataJob job, Guid? nodeId, int? row, string message)
    {
        job.Skipped++;
        Note(job, new DataJobEntryDto(nodeId, row, null, DataJobResults.Skipped, message));
    }

    private static void Fail(DataJob job, Guid? nodeId, int? row, string? column, string message)
    {
        job.Failed++;
        Note(job, new DataJobEntryDto(nodeId, row, column, DataJobResults.Failed, message));
    }

    private static void Note(DataJob job, DataJobEntryDto entry)
    {
        if (job.Entries.Count < MaxEntries)
        {
            job.Entries.Add(entry with { Message = entry.Message is { Length: > 500 } message ? message[..500] : entry.Message });
        }
    }
}
