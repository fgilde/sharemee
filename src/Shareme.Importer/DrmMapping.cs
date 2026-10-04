using Microsoft.Data.SqlClient;
using Shareme.Contracts.Drm;
using Shareme.Drm;
using Shareme.Graph;

namespace Shareme.Importer;

public sealed record BillingContactRow(long NodeId, string Identifier, string Name, string? Email, int Status);

public sealed record RightsOwnerRow(long Id, long NodeId, string Identifier, string Name, long? BillingContactNodeId, int Status);

public sealed record LicenseRow(long Id, long NodeId, string Identifier, string? TranslationIdentifier, int Status, DateTime? PeriodStart, DateTime? PeriodEnd);

public sealed record LicenseRightRow(long LicenseNodeId, string Right);

public sealed record ContextGroupRow(long Id, string Identifier, string? TranslationIdentifier);

public sealed record ContextGroupMemberRow(long GroupId, long UserContextId);

public sealed record PriceListRow(long Id, string Identifier, string Name, string? Description, int Status);

public sealed record PriceListEntryRow(long PriceListId, long? UserContextId, long? GroupId, decimal Percentage);

public sealed record BuyoutRow(
    long Id, string Identifier, long RightsOwnerId, long PriceListId, long LicenseId, string? Currency, decimal Basis, decimal Fee, DateTime? From, DateTime? To);

public sealed record SourceDrm(
    IReadOnlyList<BillingContactRow> BillingContacts, IReadOnlyList<RightsOwnerRow> RightsOwners, IReadOnlyList<LicenseRow> Licenses,
    IReadOnlyList<LicenseRightRow> Rights, IReadOnlyList<ContextGroupRow> ContextGroups, IReadOnlyList<ContextGroupMemberRow> ContextGroupMembers,
    IReadOnlyList<PriceListRow> PriceLists, IReadOnlyList<PriceListEntryRow> PriceListEntries, IReadOnlyList<BuyoutRow> Buyouts)
{
    public static readonly SourceDrm Empty = new([], [], [], [], [], [], [], [], []);

    /// <summary>Licensing of the source tenant: contacts, owners and licenses are its nodes; price lists and groups only as far as its buyouts use them.</summary>
    public static async Task<SourceDrm> LoadAsync(SqlConnection connection, long tenantId, CancellationToken cancellationToken)
    {
        async Task<List<T>> Query<T>(string sql, Func<SqlDataReader, T> map)
        {
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 600 };
            command.Parameters.AddWithValue("@tenant", tenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
        }

        const string TenantNodes = "select NodeId from MAM.Node where TenantId = @tenant and IsDeleted = 0";
        const string TenantLicenses = $"select LicenseId from MAM.License where NodeId in ({TenantNodes})";
        const string UsedPriceLists = $"select PriceListId from MAM.Buyout where LicenseId in ({TenantLicenses})";
        return new SourceDrm(
            await Query($"select b.NodeId, n.Identifier, b.Name, b.EmailAddress, b.Status from MAM.BillingContact b join MAM.Node n on n.NodeId = b.NodeId where b.NodeId in ({TenantNodes}) order by b.BillingContactId",
                r => new BillingContactRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.Text(3), r.GetInt32(4))),
            await Query(
                $"""
                select o.RightsOwnerId, o.NodeId, n.Identifier, o.Name, b.NodeId, o.Status
                from MAM.RightsOwner o join MAM.Node n on n.NodeId = o.NodeId left join MAM.BillingContact b on b.BillingContactId = o.BillingContactId
                where o.NodeId in ({TenantNodes}) order by o.RightsOwnerId
                """,
                r => new RightsOwnerRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.Long(4), r.GetInt32(5))),
            await Query(
                $"""
                select l.LicenseId, l.NodeId, n.Identifier, l.TranslationIdentifier, l.Status, l.PeriodStart, l.PeriodEnd
                from MAM.License l join MAM.Node n on n.NodeId = l.NodeId where l.NodeId in ({TenantNodes}) order by l.LicenseId
                """,
                r => new LicenseRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.Text(3), r.GetInt32(4), r.IsDBNull(5) ? null : r.GetDateTime(5), r.IsDBNull(6) ? null : r.GetDateTime(6))),
            await Query(
                $"""
                select p.NodeId, coalesce(lv.Identifier, v.ValueText)
                from MAM.PropertyValue v
                join MAM.Property p on p.PropertyId = v.PropertyId
                join MAM.PropertyDefinition d on d.PropertyDefinitionId = p.PropertyDefinitionId and d.Identifier = 'license_rights'
                left join MAM.PropertyListValue lv on lv.PropertyListValueId = v.PropertyListValueId
                where v.IsDeleted = 0 and p.IsDeleted = 0 and p.NodeId in ({TenantNodes}) order by v.PropertyValueId
                """,
                r => new LicenseRightRow(r.GetInt64(0), r.Text(1) ?? string.Empty)),
            await Query($"select UserContextGroupId, Identifier, TranslationIdentifier from MAM.UserContextGroup order by UserContextGroupId",
                r => new ContextGroupRow(r.GetInt64(0), r.GetString(1), r.Text(2))),
            await Query("select UserContextGroupId, UserContextId from MAM.UserContextGroupUserContext order by UserContextGroupUserContext",
                r => new ContextGroupMemberRow(r.GetInt64(0), r.GetInt64(1))),
            await Query($"select PriceListId, Identifier, Name, Description, Status from MAM.PriceList where PriceListId in ({UsedPriceLists}) order by PriceListId",
                r => new PriceListRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.Text(3), r.GetInt32(4))),
            await Query($"select PriceListId, UserContextId, UserContextGroupId, Percentage from MAM.PriceListUserContext where PriceListId in ({UsedPriceLists}) order by PriceListUserContextId",
                r => new PriceListEntryRow(r.GetInt64(0), r.Long(1), r.Long(2), r.GetDecimal(3))),
            await Query(
                $"""
                select b.BuyoutId, b.Identifier, b.RightsOwnerId, b.PriceListId, b.LicenseId, c.ShortName, b.Basis, b.AdditionalFeePercentage, b.FromDate, b.ToDate
                from MAM.Buyout b left join MAM.Currency c on c.CurrencyId = b.CurrencyId where b.LicenseId in ({TenantLicenses}) order by b.BuyoutId
                """,
                r => new BuyoutRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4), r.Text(5), r.GetDecimal(6), r.GetDecimal(7),
                    r.IsDBNull(8) ? null : r.GetDateTime(8), r.IsDBNull(9) ? null : r.GetDateTime(9))));
    }
}

public sealed record DrmPlan(
    IReadOnlyList<BillingContact> BillingContacts, IReadOnlyList<RightsOwner> RightsOwners, IReadOnlyList<License> Licenses, IReadOnlyList<LicenseAsset> LicenseAssets,
    IReadOnlyList<ContextGroup> ContextGroups, IReadOnlyList<PriceList> PriceLists, IReadOnlyList<Buyout> Buyouts)
{
    public static readonly DrmPlan Empty = new([], [], [], [], [], [], []);
}

/// <summary>Licensing: Sharedien keeps contacts, owners and licenses as nodes with their own tables; Shareme as entities of the DRM module.</summary>
public sealed class DrmMapper(SourceDrm source, Guid tenantId, ImportReport report, Func<string?, string, string> name)
{
    private const int Active = 1;

    /// <param name="licensedAssets">LicenseDrm edges: license node to asset node.</param>
    public DrmPlan Map(IReadOnlyList<(long License, long Asset)> licensedAssets, IReadOnlyDictionary<long, Node> nodeOf, IReadOnlyDictionary<long, string> contextKeyOf)
    {
        var contacts = new Dictionary<long, BillingContact>();
        foreach (var row in source.BillingContacts)
        {
            if (row.Email is not { Length: > 0 } email)
            {
                report.Problem("billing_contact_without_email", "MAM.BillingContact", row.NodeId);
                continue;
            }

            contacts[row.NodeId] = Entity(new BillingContact { Name = Cap(row.Name, 200), Email = Cap(email, 320), IsActive = row.Status == Active }, "MAM.Node", row.NodeId, row.Identifier);
        }

        var owners = new Dictionary<long, RightsOwner>();
        foreach (var row in source.RightsOwners)
        {
            owners[row.Id] = Entity(new RightsOwner
            {
                Name = Cap(row.Name, 200),
                BillingContactId = row.BillingContactNodeId is { } contact && contacts.TryGetValue(contact, out var c) ? c.Id : null,
                IsActive = row.Status == Active,
            }, "MAM.Node", row.NodeId, row.Identifier);
        }

        var rights = source.Rights.Where(r => r.Right.Length > 0).ToLookup(r => r.LicenseNodeId, r => Cap(r.Right, 100));
        var licenses = new Dictionary<long, License>();
        var licenseByNode = new Dictionary<long, License>();
        foreach (var row in source.Licenses)
        {
            var license = Entity(new License
            {
                Name = name(row.TranslationIdentifier, row.Identifier),
                Rights = [.. rights[row.NodeId].Distinct()],
                ValidFrom = row.PeriodStart is { } from ? DateOnly.FromDateTime(from) : null,
                ValidUntil = row.PeriodEnd is { } until ? DateOnly.FromDateTime(until) : null,
                Status = row.Status == Active ? LicenseStatuses.Active : LicenseStatuses.Inactive,
            }, "MAM.Node", row.NodeId, row.Identifier);
            licenses[row.Id] = license;
            licenseByNode[row.NodeId] = license;
        }

        var licenseAssets = new List<LicenseAsset>();
        foreach (var (licenseNode, assetNode) in licensedAssets.Distinct())
        {
            if (!licenseByNode.TryGetValue(licenseNode, out var license) || !nodeOf.TryGetValue(assetNode, out var node) || node is not Asset)
            {
                report.Skip("license links to nodes that are no imported assets");
                continue;
            }

            var link = new LicenseAsset { LicenseId = license.Id, AssetId = node.Id, TenantId = tenantId };
            Ids.Set(link, SourceIds.Derived(tenantId, "LicenseAsset:" + licenseNode, assetNode));
            licenseAssets.Add(link);
        }

        var groupMembers = source.ContextGroupMembers.ToLookup(m => m.GroupId, m => m.UserContextId);
        var usedGroups = source.PriceListEntries.Select(e => e.GroupId).OfType<long>().ToHashSet();
        var groups = new Dictionary<long, ContextGroup>();
        foreach (var row in source.ContextGroups.Where(g => usedGroups.Contains(g.Id)))
        {
            groups[row.Id] = Entity(new ContextGroup
            {
                Name = name(row.TranslationIdentifier, row.Identifier),
                ContextKeys = [.. groupMembers[row.Id].Where(contextKeyOf.ContainsKey).Select(c => contextKeyOf[c]).Distinct()],
            }, "MAM.UserContextGroup", row.Id, row.Identifier);
        }

        var entries = source.PriceListEntries.ToLookup(e => e.PriceListId);
        var priceLists = new Dictionary<long, PriceList>();
        foreach (var row in source.PriceLists)
        {
            var list = Entity(new PriceList { Name = Cap(row.Name, 200), Description = row.Description, IsActive = row.Status == Active }, "MAM.PriceList", row.Id, row.Identifier);
            foreach (var entry in entries[row.Id])
            {
                if (entry.GroupId is { } group && groups.TryGetValue(group, out var g))
                {
                    list.Entries.Add(new PriceListEntry(null, g.Id, entry.Percentage));
                }
                else if (entry.UserContextId is { } context && contextKeyOf.TryGetValue(context, out var key))
                {
                    list.Entries.Add(new PriceListEntry(key, null, entry.Percentage));
                }
                else
                {
                    report.Problem("price_entry_without_context", "MAM.PriceListUserContext", row.Id);
                }
            }

            priceLists[row.Id] = list;
        }

        var buyouts = new List<Buyout>();
        foreach (var row in source.Buyouts)
        {
            if (!licenses.TryGetValue(row.LicenseId, out var license) || !owners.TryGetValue(row.RightsOwnerId, out var owner) || !priceLists.TryGetValue(row.PriceListId, out var list))
            {
                report.Problem("buyout_without_license_owner_or_price_list", "MAM.Buyout", row.Id);
                continue;
            }

            buyouts.Add(Entity(new Buyout
            {
                LicenseId = license.Id,
                RightsOwnerId = owner.Id,
                PriceListId = list.Id,
                Currency = (row.Currency ?? "EUR").ToUpperInvariant(),
                Basis = row.Basis,
                FeePercentage = row.Fee,
                ValidFrom = row.From is { } from ? DateOnly.FromDateTime(from) : null,
                ValidUntil = row.To is { } until ? DateOnly.FromDateTime(until) : null,
            }, "MAM.Buyout", row.Id, row.Identifier));
        }

        report.Mapped["billing contacts"] = contacts.Count;
        report.Mapped["rights owners"] = owners.Count;
        report.Mapped["licenses"] = licenses.Count;
        report.Mapped["licensed assets"] = licenseAssets.Count;
        report.Mapped["price lists"] = priceLists.Count;
        report.Mapped["buyouts"] = buyouts.Count;
        return new DrmPlan([.. contacts.Values], [.. owners.Values], [.. licenses.Values], licenseAssets, [.. groups.Values], [.. priceLists.Values], buyouts);
    }

    private T Entity<T>(T entity, string table, long sourceId, string? identifier)
        where T : Coworkee.Domain.Entity
    {
        Ids.Set(entity, SourceIds.For(tenantId, table, sourceId, identifier));
        if (entity is Coworkee.Domain.IMultiTenant multiTenant)
        {
            multiTenant.TenantId = tenantId;
        }

        return entity;
    }

    private static string Cap(string text, int length) => text.Length > length ? text[..length] : text;
}
