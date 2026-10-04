using Microsoft.Data.SqlClient;

namespace Shareme.Importer;

// Rows of the Sharedien 3 database, only the columns the importer maps. Names follow the source tables.

public sealed record LanguageRow(long Id, string Identifier, bool IsDefault, string? TranslationIdentifier = null);

public sealed record TranslationRow(string TranslationIdentifier, string FieldName, long LanguageId, string Value);

public sealed record NodeTypeRow(long Id, string Identifier, int Semantic, string? TranslationIdentifier, bool IsSystem);

public sealed record EdgeTypeRow(long Id, string Identifier, int Semantic, string? TranslationIdentifier, bool IsDirected, bool IsSystem);

public sealed record NodeRow(
    long Id, string Identifier, long NodeTypeId, int SequenceNr, DateTime CreateDate, DateTime? MutDate, bool InterruptInheritance,
    string? TranslationIdentifier, int? AssetSetStatus, string? CreateUser = null);

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

public sealed record UserRow(
    long Id, string Identifier, string UserName, string? FirstName, string? LastName, string? Email, string? Department, string? Phone,
    int Status, DateTime? LastLoginDate, DateTime? CreatedAt, bool IsExternal = false);

public sealed record GroupRow(long Id, string Identifier, string Name, string? Description);

public sealed record GroupUserRow(long GroupId, long UserId);

public sealed record RoleRow(long Id, string Identifier, string? TranslationIdentifier, bool IsSystem);

public sealed record RolePermissionRow(long RoleId, int Permission);

public sealed record GroupRoleRow(long GroupId, long RoleId);

public sealed record NodePermissionRow(long Id, string Identifier, long NodeId, long RoleId, long? GroupId, long? UserId);

public sealed record SourceIdentity(
    IReadOnlyList<UserRow> Users,
    IReadOnlyList<GroupRow> Groups,
    IReadOnlyList<GroupUserRow> GroupUsers,
    IReadOnlyList<RoleRow> Roles,
    IReadOnlyList<RolePermissionRow> RolePermissions,
    IReadOnlyList<GroupRoleRow> GroupRoles,
    IReadOnlyList<NodePermissionRow> NodePermissions)
{
    public static readonly SourceIdentity Empty = new([], [], [], [], [], [], []);
}

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
    IReadOnlyList<ValueRow> Values,
    SourceIdentity? Identity = null);

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
            await Query("select LanguageId, Identifier, IsDefaultLanguage, TranslationIdentifier from MAM.Language where IsDeleted = 0 order by LanguageId",
                r => new LanguageRow(r.GetInt64(0), r.GetString(1), r.GetBoolean(2), r.Text(3))),
            await Query("select TranslationIdentifier, FieldName, LanguageId, TranslatedValue from MAM.Translation where TranslatedValue is not null order by TranslationId",
                r => new TranslationRow(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3))),
            await Query("select NodeTypeId, Identifier, NodeSemantic, TranslationIdentifier, IsSystem from MAM.NodeType where IsDeleted = 0 order by NodeTypeId",
                r => new NodeTypeRow(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.Text(3), r.GetBoolean(4))),
            await Query("select EdgeTypeId, Identifier, EdgeSemantic, TranslationIdentifier, IsDirected, IsSystem from MAM.EdgeType where IsDeleted = 0 order by EdgeTypeId",
                r => new EdgeTypeRow(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.Text(3), r.GetBoolean(4), r.GetBoolean(5))),
            await Query(
                """
                select n.NodeId, n.Identifier, n.NodeTypeId, n.SequenceNr, n.CreateDate, n.MutDate, n.InterruptInheritance,
                       coalesce(c.TranslationIdentifier, cn.TranslationIdentifier), a.AssetSetStatus, n.CreateUser
                from MAM.Node n
                left join MAM.Category c on c.CategoryNodeId = n.NodeId
                left join MAM.CustomNode cn on cn.NodeId = n.NodeId
                left join MAM.AssetSet a on a.AssetSetNodeId = n.NodeId
                where n.TenantId = @tenant and n.IsDeleted = 0
                order by n.NodeId
                """,
                r => new NodeRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt32(3), r.GetDateTime(4), r.IsDBNull(5) ? null : r.GetDateTime(5), r.GetBoolean(6), r.Text(7), r.Int(8), r.Text(9))),
            await Query($"select EdgeId, Identifier, FromNodeId, ToNodeId, EdgeTypeId from MAM.Edge where FromNodeId in ({TenantNodes}) and ToNodeId in ({TenantNodes}) order by EdgeId",
                r => new EdgeRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4))),
            await Query(
                $"""
                select e.FromNodeId, v.AssetVersionNodeId, v.Version, v.OriginalFilename, v.Created
                from MAM.AssetVersion v
                join MAM.Edge e on e.ToNodeId = v.AssetVersionNodeId
                join MAM.EdgeType t on t.EdgeTypeId = e.EdgeTypeId and t.EdgeSemantic = 2
                where v.AssetVersionNodeId in ({TenantNodes})
                order by v.AssetVersionId
                """,
                r => new VersionRow(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.Text(3), r.GetDateTime(4))),
            await Query("select PropertyGroupId, Identifier, TranslationIdentifier, SequenceNr from MAM.PropertyGroup where IsDeleted = 0 order by PropertyGroupId",
                r => new PropertyGroupRow(r.GetInt64(0), r.GetString(1), r.Text(2), r.GetInt32(3))),
            await Query(
                """
                select PropertyDefinitionId, Identifier, PropertyGroupId, TranslationIdentifier, ValueType, HasListValues, Editable, AllowMultipleValues,
                       AllowCustomListValue, SearchOption, IsLanguageDependant, IgnoreCopyingOnNewVersion, IsSearchableFacet, HasUserContext, IsUrl, SortKey
                from MAM.PropertyDefinition where IsDeleted = 0 and IsSystemProperty = 0
                order by PropertyDefinitionId
                """,
                r => new PropertyDefinitionRow(r.GetInt64(0), r.GetString(1), r.Long(2), r.Text(3), r.GetInt32(4), r.GetBoolean(5), r.GetBoolean(6), r.GetBoolean(7),
                    r.GetBoolean(8), r.GetInt32(9), r.GetBoolean(10), r.GetBoolean(11), r.GetBoolean(12), r.GetBoolean(13), r.GetBoolean(14), r.Int(15) ?? 0)),
            await Query("select PropertyDefinitionId, NodeTypeId from MAM.PropertyDefinitionNodeType order by PropertyDefinitionNodeTypeId",
                r => new DefinitionNodeTypeRow(r.GetInt64(0), r.GetInt64(1))),
            await Query("select PropertyListValueId, Identifier, PropertyDefinitionId, SortKey, TranslationIdentifier, ValueText, Status from MAM.PropertyListValue where IsDeleted = 0 order by PropertyListValueId",
                r => new ListValueRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.Int(3) ?? 0, r.Text(4), r.Text(5), r.GetInt32(6) == 1)),
            await Query(
                $"""
                select v.PropertyValueId, p.NodeId, p.PropertyDefinitionId, v.PropertyListValueId, v.ValueBool, v.ValueInt, v.ValueDecimal, v.ValueDate,
                       v.ValueDateTime, v.ValueDateTimeDurationTicks, v.ValueText, v.TranslationIdentifier, v.UserContextId
                from MAM.PropertyValue v
                join MAM.Property p on p.PropertyId = v.PropertyId
                join MAM.PropertyDefinition d on d.PropertyDefinitionId = p.PropertyDefinitionId
                where v.IsDeleted = 0 and p.IsDeleted = 0 and d.IsSystemProperty = 0 and p.NodeId in ({TenantNodes})
                order by v.PropertyValueId
                """,
                r => new ValueRow(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.Long(3), r.IsDBNull(4) ? null : r.GetBoolean(4), r.Long(5),
                    r.IsDBNull(6) ? null : r.GetDecimal(6), r.IsDBNull(7) ? null : r.GetDateTime(7), r.IsDBNull(8) ? null : r.GetDateTime(8), r.Long(9),
                    r.Text(10), r.Text(11), r.Long(12))),
            new SourceIdentity(
                await Query(
                    """
                    select UserId, Identifier, UserName, FirstName, LastName, EmailAddress, Department, PhoneNumber, Status, LastLoginDate, CreatedAt,
                        case when ProviderSubjectId is null or ProviderSubjectId = '' then 0 else 1 end
                    from Admin.[User] where IsDeleted = 0
                    order by UserId
                    """,
                    r => new UserRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.Text(3), r.Text(4), r.Text(5), r.Text(6), r.Text(7), r.GetInt32(8),
                        r.IsDBNull(9) ? null : r.GetDateTime(9), r.IsDBNull(10) ? null : r.GetDateTime(10), r.GetInt32(11) == 1)),
                await Query("select UserGroupId, Identifier, Name, Description from Admin.UserGroup where IsDeleted = 0 order by UserGroupId",
                    r => new GroupRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.Text(3))),
                await Query("select UserGroupId, UserId from Admin.UserGroupUser order by UserGroupUserId", r => new GroupUserRow(r.GetInt64(0), r.GetInt64(1))),
                await Query("select RoleId, Identifier, TranslationIdentifier, IsSystem from Admin.Role where IsDeleted = 0 and Status = 1 order by RoleId",
                    r => new RoleRow(r.GetInt64(0), r.GetString(1), r.Text(2), r.GetBoolean(3))),
                await Query("select RoleId, Permission from Admin.RoleObjectTypePermission order by RoleObjectTypePermissionId", r => new RolePermissionRow(r.GetInt64(0), r.GetInt32(1))),
                await Query("select UserGroupId, RoleId from Admin.UserGroupToRoleMap where TenantId = @tenant order by UserGroupToRoleMapId", r => new GroupRoleRow(r.GetInt64(0), r.GetInt64(1))),
                await Query(
                    $"""
                    select NodePermissionId, Identifier, NodeId, RoleId, UserGroupId, UserId
                    from Admin.NodePermission where NodeId in ({TenantNodes})
                    order by NodePermissionId
                    """,
                    r => new NodePermissionRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.Long(4), r.Long(5)))));
    }
}

public static class ReaderExtensions
{
    public static string? Text(this SqlDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetString(i);

    public static long? Long(this SqlDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt64(i);

    public static int? Int(this SqlDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);
}
