using Microsoft.Data.SqlClient;

namespace Shareme.Importer;

// Rows of the Sharedien 3 database, only the columns the importer maps. Names follow the source tables.

public sealed record LanguageRow(long Id, string Identifier, bool IsDefault, string? TranslationIdentifier = null);

public sealed record TranslationRow(string TranslationIdentifier, string FieldName, long LanguageId, string Value);

public sealed record NodeTypeRow(long Id, string Identifier, int Semantic, string? TranslationIdentifier, bool IsSystem);

public sealed record EdgeTypeRow(long Id, string Identifier, int Semantic, string? TranslationIdentifier, bool IsDirected, bool IsSystem);

public sealed record NodeRow(
    long Id, string Identifier, long NodeTypeId, int SequenceNr, DateTime CreateDate, DateTime MutDate, bool InterruptInheritance,
    string? TranslationIdentifier, int? AssetSetStatus);

public sealed record EdgeRow(long Id, string Identifier, long FromNodeId, long ToNodeId, long EdgeTypeId);

public sealed record VersionRow(long AssetNodeId, long VersionNodeId, int Version, string? OriginalFilename, DateTime Created);

public sealed record PropertyGroupRow(long Id, string Identifier, string? TranslationIdentifier, int SequenceNr);

public sealed record PropertyDefinitionRow(
    long Id, string Identifier, long? GroupId, string? TranslationIdentifier, int ValueType, bool HasListValues, bool Editable,
    bool AllowMultipleValues, bool AllowCustomListValue, int SearchOption, bool IsLanguageDependant, bool IgnoreCopyingOnNewVersion,
    bool IsSearchableFacet, bool HasUserContext, bool IsUrl, int SortKey);

public sealed record DefinitionNodeTypeRow(long DefinitionId, long NodeTypeId);

public sealed record ListValueRow(long Id, string Identifier, long DefinitionId, int SortKey, string? TranslationIdentifier, string? Text, bool IsActive);

public sealed record ValueRow(
    long Id, long NodeId, long DefinitionId, long? ListValueId, bool? Bool, long? Int, decimal? Decimal, DateTime? Date, DateTime? DateTime,
    long? DurationTicks, string? Text, string? TranslationIdentifier, long? UserContextId);

public sealed record SourceData(
    IReadOnlyList<LanguageRow> Languages,
    IReadOnlyList<TranslationRow> Translations,
    IReadOnlyList<NodeTypeRow> NodeTypes,
    IReadOnlyList<EdgeTypeRow> EdgeTypes,
    IReadOnlyList<NodeRow> Nodes,
    IReadOnlyList<EdgeRow> Edges,
    IReadOnlyList<VersionRow> Versions,
    IReadOnlyList<PropertyGroupRow> PropertyGroups,
    IReadOnlyList<PropertyDefinitionRow> PropertyDefinitions,
    IReadOnlyList<DefinitionNodeTypeRow> DefinitionNodeTypes,
    IReadOnlyList<ListValueRow> ListValues,
    IReadOnlyList<ValueRow> Values);

/// <summary>Reads one source tenant, read only. Deleted rows and system property definitions stay behind.</summary>
public sealed class SharedienSource(string connectionString)
{
    public async Task<SourceData> LoadAsync(long tenantId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

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
        return new SourceData(
            await Query("select LanguageId, Identifier, IsDefaultLanguage, TranslationIdentifier from MAM.Language where IsDeleted = 0",
                r => new LanguageRow(r.GetInt64(0), r.GetString(1), r.GetBoolean(2), r.Text(3))),
            await Query("select TranslationIdentifier, FieldName, LanguageId, TranslatedValue from MAM.Translation where TranslatedValue is not null",
                r => new TranslationRow(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3))),
            await Query("select NodeTypeId, Identifier, NodeSemantic, TranslationIdentifier, IsSystem from MAM.NodeType where IsDeleted = 0",
                r => new NodeTypeRow(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.Text(3), r.GetBoolean(4))),
            await Query("select EdgeTypeId, Identifier, EdgeSemantic, TranslationIdentifier, IsDirected, IsSystem from MAM.EdgeType where IsDeleted = 0",
                r => new EdgeTypeRow(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.Text(3), r.GetBoolean(4), r.GetBoolean(5))),
            await Query(
                """
                select n.NodeId, n.Identifier, n.NodeTypeId, n.SequenceNr, n.CreateDate, n.MutDate, n.InterruptInheritance,
                       coalesce(c.TranslationIdentifier, cn.TranslationIdentifier), a.AssetSetStatus
                from MAM.Node n
                left join MAM.Category c on c.CategoryNodeId = n.NodeId
                left join MAM.CustomNode cn on cn.NodeId = n.NodeId
                left join MAM.AssetSet a on a.AssetSetNodeId = n.NodeId
                where n.TenantId = @tenant and n.IsDeleted = 0
                """,
                r => new NodeRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt32(3), r.GetDateTime(4), r.GetDateTime(5), r.GetBoolean(6), r.Text(7), r.Int(8))),
            await Query($"select EdgeId, Identifier, FromNodeId, ToNodeId, EdgeTypeId from MAM.Edge where FromNodeId in ({TenantNodes}) and ToNodeId in ({TenantNodes})",
                r => new EdgeRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4))),
            await Query(
                $"""
                select e.FromNodeId, v.AssetVersionNodeId, v.Version, v.OriginalFilename, v.Created
                from MAM.AssetVersion v
                join MAM.Edge e on e.ToNodeId = v.AssetVersionNodeId
                join MAM.EdgeType t on t.EdgeTypeId = e.EdgeTypeId and t.EdgeSemantic = 2
                where v.AssetVersionNodeId in ({TenantNodes})
                """,
                r => new VersionRow(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.Text(3), r.GetDateTime(4))),
            await Query("select PropertyGroupId, Identifier, TranslationIdentifier, SequenceNr from MAM.PropertyGroup where IsDeleted = 0",
                r => new PropertyGroupRow(r.GetInt64(0), r.GetString(1), r.Text(2), r.GetInt32(3))),
            await Query(
                """
                select PropertyDefinitionId, Identifier, PropertyGroupId, TranslationIdentifier, ValueType, HasListValues, Editable, AllowMultipleValues,
                       AllowCustomListValue, SearchOption, IsLanguageDependant, IgnoreCopyingOnNewVersion, IsSearchableFacet, HasUserContext, IsUrl, SortKey
                from MAM.PropertyDefinition where IsDeleted = 0 and IsSystemProperty = 0
                """,
                r => new PropertyDefinitionRow(r.GetInt64(0), r.GetString(1), r.Long(2), r.Text(3), r.GetInt32(4), r.GetBoolean(5), r.GetBoolean(6), r.GetBoolean(7),
                    r.GetBoolean(8), r.GetInt32(9), r.GetBoolean(10), r.GetBoolean(11), r.GetBoolean(12), r.GetBoolean(13), r.GetBoolean(14), r.Int(15) ?? 0)),
            await Query("select PropertyDefinitionId, NodeTypeId from MAM.PropertyDefinitionNodeType",
                r => new DefinitionNodeTypeRow(r.GetInt64(0), r.GetInt64(1))),
            await Query("select PropertyListValueId, Identifier, PropertyDefinitionId, SortKey, TranslationIdentifier, ValueText, Status from MAM.PropertyListValue where IsDeleted = 0",
                r => new ListValueRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.Int(3) ?? 0, r.Text(4), r.Text(5), r.GetInt32(6) == 1)),
            await Query(
                $"""
                select v.PropertyValueId, p.NodeId, p.PropertyDefinitionId, v.PropertyListValueId, v.ValueBool, v.ValueInt, v.ValueDecimal, v.ValueDate,
                       v.ValueDateTime, v.ValueDateTimeDurationTicks, v.ValueText, v.TranslationIdentifier, v.UserContextId
                from MAM.PropertyValue v
                join MAM.Property p on p.PropertyId = v.PropertyId
                where v.IsDeleted = 0 and p.IsDeleted = 0 and p.NodeId in ({TenantNodes})
                """,
                r => new ValueRow(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.Long(3), r.IsDBNull(4) ? null : r.GetBoolean(4), r.Long(5),
                    r.IsDBNull(6) ? null : r.GetDecimal(6), r.IsDBNull(7) ? null : r.GetDateTime(7), r.IsDBNull(8) ? null : r.GetDateTime(8), r.Long(9),
                    r.Text(10), r.Text(11), r.Long(12))));
    }
}

public static class ReaderExtensions
{
    public static string? Text(this SqlDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetString(i);

    public static long? Long(this SqlDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt64(i);

    public static int? Int(this SqlDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);
}
