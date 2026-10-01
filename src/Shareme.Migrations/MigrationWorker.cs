using Microsoft.EntityFrameworkCore;
using Shareme.Infrastructure;

namespace Shareme.Migrations;

internal sealed class MigrationWorker(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShareMeDbContext>().Database.MigrateAsync(stoppingToken);
        lifetime.StopApplication();
    }
}
