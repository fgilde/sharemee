using System.Collections.Concurrent;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Shareme.AppHost.Tests;

/// <summary>Waits for resources and, when one fails, puts its own log into the failure instead of a bare timeout.</summary>
internal static class AppHostDiagnostics
{
    public static async Task WaitHealthyAsync(DistributedApplication app, IEnumerable<string> resources, CancellationToken cancellationToken)
    {
        var names = resources.ToList();
        var logs = names.ToDictionary(n => n, _ => new ConcurrentQueue<string>());
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loggers = app.Services.GetRequiredService<ResourceLoggerService>();
        foreach (var name in names)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var batch in loggers.WatchAsync(name).WithCancellation(watching.Token))
                    {
                        foreach (var line in batch)
                        {
                            logs[name].Enqueue(line.Content);
                            while (logs[name].Count > 200 && logs[name].TryDequeue(out _))
                            {
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, CancellationToken.None);
        }

        try
        {
            foreach (var name in names)
            {
                await app.ResourceNotifications.WaitForResourceHealthyAsync(name, cancellationToken).WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var output = string.Join(Environment.NewLine, names.Select(n => $"--- {n} ---{Environment.NewLine}{string.Join(Environment.NewLine, logs[n].TakeLast(80))}"));
            throw new InvalidOperationException($"{exception.Message}{Environment.NewLine}{output}", exception);
        }
        finally
        {
            await watching.CancelAsync();
        }
    }

    /// <summary>Stops the app when disposed but never waits forever: a hung shutdown must not hold the whole test run.</summary>
    public static IAsyncDisposable Guard(DistributedApplication app) => new Stopper(app);

    private sealed class Stopper(DistributedApplication app) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMinutes(2));
            }
            catch (TimeoutException)
            {
            }
        }
    }
}
