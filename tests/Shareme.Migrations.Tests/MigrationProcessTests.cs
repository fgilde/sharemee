using System.Diagnostics;

namespace Shareme.Migrations.Tests;

public sealed class MigrationProcessTests
{
    [Fact]
    public async Task Unreachable_database_exits_with_failure_code()
    {
        var start = new ProcessStartInfo("dotnet", Path.Combine(AppContext.BaseDirectory, "Shareme.Migrations.dll"))
        {
            Environment =
            {
                ["ConnectionStrings__shareme"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2",
                ["DOTNET_ENVIRONMENT"] = "Production",
                ["Migrations__Attempts"] = "1",
            },
        };

        using var process = Process.Start(start)!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        process.ExitCode.ShouldNotBe(0);
    }
}
