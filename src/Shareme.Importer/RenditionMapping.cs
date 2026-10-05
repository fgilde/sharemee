using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Shareme.Assets;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Graph;
using Shareme.Graph;

namespace Shareme.Importer;

public sealed record DerivationTypeRow(long Id, string Identifier, string? TranslationIdentifier, bool IsSystem);

public sealed record AssetTypeDerivationRow(string AssetType, long DerivationTypeId, bool IsDefault, bool AllowManualUpload, bool IsOnDemand);

public sealed record DerivationJobRow(long DerivationTypeId, string? Definition);

public sealed record DerivationRestrictionRow(long Id, long RoleId, int Action, long DerivationTypeId);

public sealed record SourceRenditions(
    IReadOnlyList<DerivationTypeRow> Types, IReadOnlyList<AssetTypeDerivationRow> AssetTypes, IReadOnlyList<DerivationJobRow> Jobs, IReadOnlyList<DerivationRestrictionRow> Restrictions)
{
    public static readonly SourceRenditions Empty = new([], [], [], []);

    public static async Task<SourceRenditions> LoadAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        async Task<List<T>> Query<T>(string sql, Func<SqlDataReader, T> map)
        {
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 600 };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
        }

        return new SourceRenditions(
            await Query("select DerivationTypeId, Identifier, TranslationIdentifier, IsSystem from MAM.DerivationType where IsDeleted = 0 order by DerivationTypeId",
                r => new DerivationTypeRow(r.GetInt64(0), r.GetString(1), r.Text(2), r.GetBoolean(3))),
            await Query(
                """
                select a.Identifier, d.DerivationTypeId, d.IsDefaultDerivation, d.AllowManualUpload, d.IsOnDemand
                from MAM.AssetTypeDerivationType d join MAM.AssetType a on a.AssetTypeId = d.AssetTypeId order by d.AssetTypeDerivationTypeId
                """,
                r => new AssetTypeDerivationRow(r.GetString(0), r.GetInt64(1), r.GetBoolean(2), r.GetBoolean(3), r.GetBoolean(4))),
            await Query("select DerivationTypeId, JobDefinition from MAM.DerivationJobDefinition order by DerivationJobDefinitionId",
                r => new DerivationJobRow(r.GetInt64(0), r.Text(1))),
            await Query("select DerivationTypeRestrictionId, RoleId, Action, DerivationTypeId from Admin.DerivationTypeRestriction order by DerivationTypeRestrictionId",
                r => new DerivationRestrictionRow(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.GetInt64(3))));
    }
}

public sealed record RenditionTypePlan(IReadOnlyList<DerivationType> Types, IReadOnlyList<Restriction> Restrictions)
{
    public static readonly RenditionTypePlan Empty = new([], []);
}

/// <summary>
/// Rendition types: Sharedien's system types become Shareme's built in kinds, the others tenant types (size taken from their
/// ImageMagick job, else 1600 pixels). Role restrictions on reading a type block that rendition for the tenant role.
/// </summary>
public sealed partial class RenditionMapper(SourceRenditions source, Guid tenantId, IReadOnlyDictionary<Guid, string> existingKeys, ImportReport report, Func<string?, string, string> name)
{
    private const int DefaultSize = 1600;

    private static readonly Dictionary<string, string?> SystemKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Thumbnail"] = DerivationKinds.Thumbnail,
        ["Screen"] = DerivationKinds.Preview,
        ["Thumbnail_Animated"] = DerivationKinds.Animated,
        ["Original"] = null, // the file itself: guarded by the download right, no rendition
    };

    private static readonly Dictionary<string, string> AssetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Image"] = AssetTypeKeys.Image,
        ["Movie"] = AssetTypeKeys.Video,
        ["Audio"] = AssetTypeKeys.Audio,
        ["Archive"] = AssetTypeKeys.Archive,
        ["CAD"] = AssetTypeKeys.Model,
        ["PDF"] = AssetTypeKeys.Document,
        ["MS Word"] = AssetTypeKeys.Document,
        ["MS Excel"] = AssetTypeKeys.Document,
        ["MS PowerPoint"] = AssetTypeKeys.Document,
        ["Adobe Illustrator"] = AssetTypeKeys.Document,
        ["Adobe InDesign"] = AssetTypeKeys.Document,
        ["Unknown"] = AssetTypeKeys.Other,
    };

    public RenditionTypePlan Map(IdentityPlan identity, Guid? adminRoleId)
    {
        var keyOf = new Dictionary<long, string?>();
        var types = new List<DerivationType>();
        var taken = new HashSet<string>([.. existingKeys.Values, .. DerivationKinds.System], StringComparer.Ordinal);
        var usage = source.AssetTypes.ToLookup(a => a.DerivationTypeId);
        var jobs = source.Jobs.ToLookup(j => j.DerivationTypeId, j => j.Definition);
        foreach (var row in source.Types)
        {
            if (SystemKinds.TryGetValue(row.Identifier, out var kind))
            {
                keyOf[row.Id] = kind;
                continue;
            }

            var id = SourceIds.Derived(tenantId, "MAM.DerivationType", row.Id);
            var key = existingKeys.TryGetValue(id, out var existing) ? existing : Unique(SharedienMapper.Slug(row.Identifier) is { } slug ? slug[..Math.Min(slug.Length, 45)] : $"rendition-{row.Id}", taken);
            var size = jobs[row.Id].Select(Size).FirstOrDefault(s => s is not null) ?? (DefaultSize, DefaultSize);
            var title = name(row.TranslationIdentifier, row.Identifier);
            var type = new DerivationType
            {
                Key = key,
                Name = title.Length > 200 ? title[..200] : title,
                Width = size.Width,
                Height = size.Height,
                IsDefault = usage[row.Id].Any(u => u.IsDefault),
                IsOnDemand = !usage[row.Id].Any() || usage[row.Id].Any(u => u.IsOnDemand),
                AllowManualUpload = usage[row.Id].Any(u => u.AllowManualUpload),
                AssetTypeKeys = [.. usage[row.Id].Select(u => AssetTypes.GetValueOrDefault(u.AssetType)).OfType<string>().Distinct()],
                TenantId = tenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            Ids.Set(type, id);
            types.Add(type);
            keyOf[row.Id] = key;
        }

        var restrictions = new Dictionary<(Guid, string), Restriction>();
        foreach (var row in source.Restrictions)
        {
            // AuthorizableAction: Read 0, View 4 and Any 5 keep the rendition from the role; creating one has no counterpart
            if (row.Action is not (0 or 4 or 5))
            {
                report.Skip("rendition restrictions on creating or changing");
                continue;
            }

            if (!keyOf.TryGetValue(row.DerivationTypeId, out var key))
            {
                report.Skip("rendition restrictions of deleted types");
                continue;
            }

            if (key is null)
            {
                report.Problem("restriction_on_original", "Admin.DerivationTypeRestriction", row.Id); // the download right governs originals
                continue;
            }

            if (SharedienMapper.RestrictedRole(identity, adminRoleId, row.RoleId, "Admin.DerivationTypeRestriction", row.Id, report) is { } role && !restrictions.ContainsKey((role, key)))
            {
                var restriction = new Restriction { RoleId = role, Kind = RestrictionKinds.BlockedRendition, Value = key, TenantId = tenantId, CreatedAt = DateTimeOffset.UtcNow };
                Ids.Set(restriction, SourceIds.Derived(tenantId, "Admin.DerivationTypeRestriction:" + key, BitConverter.ToInt64(role.ToByteArray())));
                restrictions[(role, key)] = restriction;
            }
        }

        report.Mapped["rendition types"] = types.Count;
        report.Mapped["rendition restrictions"] = restrictions.Count;
        return new RenditionTypePlan(types, [.. restrictions.Values]);
    }

    /// <summary>The target size of an ImageMagick resize ("-resize 800x600" or "-thumbnail 400x400>"), if the job has one.</summary>
    internal static (int Width, int Height)? Size(string? definition) =>
        definition is not null && ResizeArgument().Match(definition) is { Success: true } match
            ? (Math.Clamp(int.Parse(match.Groups[1].Value), 16, RenditionLimits.MaxEdge), Math.Clamp(int.Parse(match.Groups[2].Value), 16, RenditionLimits.MaxEdge))
            : null;

    private static string Unique(string key, HashSet<string> taken)
    {
        var candidate = key;
        for (var i = 2; !taken.Add(candidate); i++)
        {
            candidate = $"{key}-{i}";
        }

        return candidate;
    }

    [GeneratedRegex(@"-(?:resize|thumbnail|scale)\s+(\d{2,5})x(\d{2,5})")]
    private static partial Regex ResizeArgument();
}
