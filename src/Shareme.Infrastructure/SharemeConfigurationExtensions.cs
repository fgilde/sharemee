using Coworkee.Settings;
using Microsoft.Extensions.Configuration;
using Shareme.Contracts.Configuration;

namespace Shareme.Infrastructure;

public static class SharemeConfigurationExtensions
{
    /// <summary>
    /// The app configuration of every Sharemee service: appconfig.json as the lowest layer, appsettings and environment above
    /// it, and what admins changed under Configuration on top (read from the database, shared by all services).
    /// </summary>
    public static IConfigurationBuilder AddSharemeConfiguration(this IConfigurationBuilder configuration) =>
        configuration
            .AddCoworkeeAppConfigurationDefaults(SharemeConfiguration.Defaults(), SharemeConfiguration.Section)
            .AddCoworkeeDatabaseConfiguration(ShareMeInfrastructureModule.ConnectionStringName);
}
