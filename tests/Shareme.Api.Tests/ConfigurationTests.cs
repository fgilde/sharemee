using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shareme.Contracts.Configuration;

namespace Shareme.Api.Tests;

public sealed class ConfigurationTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await api.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_generated_configuration_starts_from_appconfig_and_changes_apply_at_once()
    {
        (await Admin.GetFromJsonAsync<AppConfigurationDto[]>("/api/v1/configuration", Ct))!
            .ShouldContain(new AppConfigurationDto(SharemeConfiguration.Section, SharemeConfiguration.Title));
        var shown = (await Admin.GetFromJsonAsync<AppConfigurationValuesDto>("/api/v1/configuration/Shareme", Ct))!;
        var values = shown.Values.Deserialize<SharemeConfiguration>(JsonSerializerOptions.Web)!;
        // appconfig.json is the lowest layer: the test host's own setting for shares wins over it
        (values.MassEdit.MaxNodes, values.Shares.RequestsPerMinute, values.Ai.AllowPrivateNetworks).ShouldBe((1000, 100, false));
        shown.Defaults.GetProperty("shares").GetProperty("requestsPerMinute").GetInt32().ShouldBe(100);

        try
        {
            values.MassEdit.MaxNodes = 5;
            (await Admin.PutAsJsonAsync("/api/v1/configuration/Shareme", values, Ct)).EnsureSuccessStatusCode();

            api.Factory.Services.GetRequiredService<IConfiguration>()["Shareme:MassEdit:MaxNodes"].ShouldBe("5");
            api.Factory.Services.GetRequiredService<IOptionsMonitor<SharemeConfiguration>>().CurrentValue.MassEdit.MaxNodes.ShouldBe(5);
        }
        finally
        {
            (await Admin.DeleteAsync("/api/v1/configuration/Shareme", Ct)).EnsureSuccessStatusCode();
        }

        api.Factory.Services.GetRequiredService<IConfiguration>()["Shareme:MassEdit:MaxNodes"].ShouldBe("1000");
    }
}
