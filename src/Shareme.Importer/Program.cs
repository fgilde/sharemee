namespace Shareme.Importer;

internal static class ImporterProgram
{
    public static async Task<int> Main(string[] args)
    {
        // shareme-import plan|run --source <sql server> --source-tenant <id> --target <postgres> --tenant <guid> [--report <file>] [--max-problems <n>]
        if (args.Length == 0 || args[0] is not ("plan" or "run") || ImportOptions.Parse(args) is not { } options)
        {
            Console.Error.WriteLine("usage: shareme-import plan|run --source <sql server connection> --source-tenant <id> --target <postgres connection> --tenant <guid> [--report <file>] [--max-problems <n>]");
            return 2;
        }

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        var report = await SharedienImport.RunAsync(options, cancel.Token);
        Console.WriteLine(report.Summary());
        if (options.ReportPath is { } path)
        {
            await report.SaveAsync(path, cancel.Token);
        }

        return report.Problems.Count > options.MaxProblems ? 1 : 0;
    }
}

public sealed record ImportOptions(bool Write, string Source, long SourceTenant, string Target, Guid Tenant, string? ReportPath, int MaxProblems)
{
    public static ImportOptions? Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i + 1 < args.Length; i += 2)
        {
            values[args[i]] = args[i + 1];
        }

        return values.TryGetValue("--source", out var source) && values.TryGetValue("--target", out var target)
            && long.TryParse(values.GetValueOrDefault("--source-tenant"), out var sourceTenant) && Guid.TryParse(values.GetValueOrDefault("--tenant"), out var tenant)
            ? new ImportOptions(args[0] == "run", source, sourceTenant, target, tenant, values.GetValueOrDefault("--report"),
                int.TryParse(values.GetValueOrDefault("--max-problems"), out var max) ? max : int.MaxValue)
            : null;
    }
}

public static class SharedienImport
{
    /// <summary>Reads the source tenant, maps it and, unless planning, writes what is not there yet.</summary>
    public static async Task<ImportReport> RunAsync(ImportOptions options, CancellationToken cancellationToken)
    {
        var report = new ImportReport();
        var source = await new SharedienSource(options.Source).LoadAsync(options.SourceTenant, cancellationToken);
        await using var writer = new ImportWriter(options.Target, options.Tenant);
        var plan = new SharedienMapper(source, await writer.TargetAsync(options.Write, cancellationToken), report).Map();
        if (options.Write)
        {
            await writer.WriteAsync(plan, report, cancellationToken);
        }

        return report;
    }
}
