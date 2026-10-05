namespace Shareme.Contracts.Configuration;

/// <summary>The section admins edit under Configuration; the properties are generated from appconfig.json.</summary>
public partial class SharemeConfiguration
{
    public const string Section = "Shareme";

    public const string Title = "Sharemee";

    /// <summary>appconfig.json as shipped: the defaults below appsettings and environment.</summary>
    public static Stream Defaults() =>
        typeof(SharemeConfiguration).Assembly.GetManifestResourceStream("Shareme.appconfig.json")
        ?? throw new InvalidOperationException("appconfig.json is not embedded.");
}

public partial class Archives
{
    /// <summary>Bytes; above int (the generator would infer one from the number).</summary>
    public long MaxUnpackedBytes { get; set; }
}
