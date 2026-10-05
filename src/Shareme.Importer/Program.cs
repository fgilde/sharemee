namespace Shareme.Importer;

internal static class ImporterProgram
{
    private const string Usage =
        """
        usage: shareme-import plan|run --source-tenant <id> --tenant <guid> [--report <file>] [--max-problems <n>]
                                     [--blobs-folder <StorageHandler root> | --blobs-container <Azure container>]
          source and target connections come from the environment, never the command line:
          SHAREME_IMPORT_SOURCE (SQL Server, read only), SHAREME_IMPORT_TARGET (Shareme Postgres),
          SHAREME_IMPORT_BLOBS (Azure storage connection for --blobs-container); the target storage is configured
          like Shareme itself (Coworkee__Storage__...)
        """;

    public static async Task<int> Main(string[] args)
    {
        if (ImportOptions.Parse(args, Environment.GetEnvironmentVariable("SHAREME_IMPORT_SOURCE"), Environment.GetEnvironmentVariable("SHAREME_IMPORT_TARGET")) is not { } options)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        var report = new ImportReport();
        try
        {
            await SharedienImport.RunAsync(options, report, cancel.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException exception)
        {
            // database messages can quote row values: name only the kind of failure, nothing was committed
            Console.Error.WriteLine($"Writing failed and nothing was committed ({(exception.InnerException as Npgsql.PostgresException)?.SqlState ?? "unknown"}).");
            return 1;
        }
        finally
        {
            Console.WriteLine(report.Summary());
            if (options.ReportPath is { } path)
            {
                await report.SaveAsync(path, CancellationToken.None);
            }
        }

        if (options.Write)
        {
            Console.WriteLine("Done. Restart Shareme (or wait five minutes for the permission cache); the search rebuild is queued (admin › search shows it).");
            Console.WriteLine("Rules came over switched off: review them (names ending in \"(import incomplete)\" lost parts) before switching them on.");
        }

        return report.Problems.Count > options.MaxProblems ? 1 : 0;
    }
}

public sealed record ImportOptions(bool Write, string Source, long SourceTenant, string Target, Guid Tenant, string? ReportPath, int MaxProblems)
{
    private static readonly HashSet<string> Flags = ["--source-tenant", "--tenant", "--report", "--max-problems", "--blobs-folder", "--blobs-container"];

    /// <summary>Where the original files come from; without it only the database moves.</summary>
    public ISourceBlobs? Blobs { get; init; }

    /// <summary>Null on any unknown flag, missing value or unparsable number, so a typo never runs an import.</summary>
    public static ImportOptions? Parse(string[] args, string? source, string? target)
    {
        if (args.Length == 0 || args[0] is not ("plan" or "run") || args.Length % 2 == 0 || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
        {
            if (!Flags.Contains(args[i]) || args[i + 1].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[i], args[i + 1]))
            {
                return null;
            }
        }

        var max = int.MaxValue;
        return long.TryParse(values.GetValueOrDefault("--source-tenant"), out var sourceTenant)
            && Guid.TryParse(values.GetValueOrDefault("--tenant"), out var tenant) && tenant != Guid.Empty
            && (!values.TryGetValue("--max-problems", out var limit) || int.TryParse(limit, out max))
            && !(values.ContainsKey("--blobs-folder") && values.ContainsKey("--blobs-container"))
            && (!values.ContainsKey("--blobs-container") || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHAREME_IMPORT_BLOBS")))
            ? new ImportOptions(args[0] == "run", source, sourceTenant, target, tenant, values.GetValueOrDefault("--report"), max)
            {
                Blobs = values.TryGetValue("--blobs-folder", out var folder) ? new FileSystemBlobs(folder)
                    : values.TryGetValue("--blobs-container", out var container) ? new AzureBlobs(Environment.GetEnvironmentVariable("SHAREME_IMPORT_BLOBS")!, container)
                    : null,
            }
            : null;
    }
}

public static class SharedienImport
{
    /// <summary>Reads the source tenant, maps it and, unless planning, writes what is not there yet in one transaction.</summary>
    public static async Task<ImportReport> RunAsync(ImportOptions options, ImportReport report, CancellationToken cancellationToken)
    {
        var source = await new SharedienSource(options.Source).LoadAsync(options.SourceTenant, cancellationToken);
        if (source.Nodes.Count == 0)
        {
            throw new InvalidOperationException($"The source tenant {options.SourceTenant} has no nodes.");
        }

        await using var writer = new ImportWriter(options.Target, options.Tenant);
        var plan = new SharedienMapper(source, await writer.TargetAsync(cancellationToken) with { WithFiles = options.Blobs is not null }, report).Map();
        if (options.Write && report.Problems.Count > options.MaxProblems)
        {
            throw new InvalidOperationException($"{report.Problems.Count} problems exceed --max-problems {options.MaxProblems}; nothing was written.");
        }

        if (!options.Write)
        {
            return report;
        }

        List<Shareme.Assets.AssetVersion> versions = [];
        if (options.Blobs is { } blobs)
        {
            // the writer only inserts: files can only come together with their assets, never for assets of an earlier run
            var existingAssets = await writer.ExistingNodesAsync(plan.Versions.Select(v => v.Asset.Id).Distinct(), cancellationToken);
            var existingVersions = await writer.ExistingVersionsAsync(plan.Versions.Select(v => v.VersionId), cancellationToken);
            foreach (var asset in plan.Versions.GroupBy(v => v.Asset.Id).Where(g => existingAssets.Contains(g.Key) && !g.Any(v => existingVersions.Contains(v.VersionId))))
            {
                report.Problem("asset_imported_without_files", "MAM.AssetVersion", asset.First().VersionNodeId);
            }

            var copier = new BlobCopier(blobs, writer.Storage, await writer.FileTypesAsync(cancellationToken), report);
            versions = await copier.CopyAsync([.. plan.Versions.Where(v => !existingAssets.Contains(v.Asset.Id))], existingVersions, options.Tenant, cancellationToken);
            await copier.CopyWatermarksAsync(plan.Renditions,
                await writer.ExistingAsync<Shareme.Assets.WatermarkDefinition>(plan.Renditions.Watermarks.Select(w => w.Id), cancellationToken), options.Tenant, cancellationToken);
        }

        plan.Renditions.DropWatermarksWithoutImage(report);

        if (report.Problems.Count > options.MaxProblems)
        {
            throw new InvalidOperationException($"{report.Problems.Count} problems exceed --max-problems {options.MaxProblems} after copying files; nothing was written (copied files are overwritten by the next run).");
        }

        await writer.WriteAsync(plan, versions, report, cancellationToken);
        try
        {
            report.Written["rendition requests"] = await writer.RequestRenditionsAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // the import is committed; the next run requests what is still missing
            report.Problem("renditions_not_requested", "import", 0);
        }

        try
        {
            await writer.RequestReindexAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            report.Problem("reindex_not_requested", "import", 0); // admin › search starts it by hand
        }

        return report;
    }

    public static Task<ImportReport> RunAsync(ImportOptions options, CancellationToken cancellationToken) => RunAsync(options, new ImportReport(), cancellationToken);
}
