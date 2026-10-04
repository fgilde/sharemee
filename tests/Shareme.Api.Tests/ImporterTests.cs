using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Importer;
using Testcontainers.MsSql;

namespace Shareme.Api.Tests;

/// <summary>Imports a tiny synthetic Sharedien database (SQL Server container) and reads the result through the normal API.</summary>
public sealed class ImporterTests(ApiFixture api) : IAsyncLifetime
{
    private static readonly Guid TopFolder = new("1b0e5e34-2c4d-4f7a-9d1e-0a3b5c7d9e11");

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(Ct), SetupAsync());
        await using var connection = new SqlConnection(_sql.GetConnectionString());
        await connection.OpenAsync(Ct);
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(Schema, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync(Ct);
        }
    }

    private async Task SetupAsync() => _setup = await api.SetupAsync();

    public ValueTask DisposeAsync() => _sql.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    private ImportOptions Options(bool write) => new(write, _sql.GetConnectionString(), 7, api.ConnectionString, _setup.TenantId, null, int.MaxValue);

    [Fact]
    public async Task Imports_catalog_tree_and_metadata_once()
    {
        // the tenant already has German: it stays and is not imported again
        (await Admin.PostAsJsonAsync("/api/v1/metadata/languages", new DataLanguageDto("de", "Deutsch", true), Ct)).EnsureSuccessStatusCode();
        var plan = await SharedienImport.RunAsync(Options(write: false), Ct);
        plan.Written.ShouldBeEmpty();
        plan.Mapped["nodes"].ShouldBe(4);
        (await Admin.GetAsync($"/api/v1/nodes/{TopFolder}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var run = await SharedienImport.RunAsync(Options(write: true), Ct);

        run.Written["nodes"].ShouldBe(4);
        run.Problems.Select(p => (p.Code, p.Table, p.SourceId)).ShouldBe(
            [("multi_parent", "MAM.Edge", 106), ("invalid_value", "MAM.PropertyValue", 9), ("user_merged", "Admin.User", 2),
                ("unmapped_permission", "Admin.RoleObjectTypePermission", 9999), ("deferred_parent", "MAM.Node", 51), ("deferred_parent", "MAM.Node", 52),
                ("definition_without_types", "MAM.PropertyDefinition", 6)], ignoreOrder: true);
        var languages = (await Admin.GetFromJsonAsync<DataLanguageDto[]>("/api/v1/metadata/languages", Ct))!;
        languages.Select(l => l.Code).ShouldBe(["en", "de"], ignoreOrder: true);
        languages.Count(l => l.IsDefault).ShouldBe(1);
        (run.Written["users"], run.Written["roles"], run.Written["groups"], run.Written["node permissions"]).ShouldBe((1, 2, 1, 1));

        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!;
        var top = (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{root.Id}/children", Ct))!.Items;
        top.Select(n => n.Name).ShouldBe(["Marketing", "Widget"], ignoreOrder: true);
        top.Single(n => n.Name == "Marketing").Id.ShouldBe(TopFolder);
        var sub = (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{TopFolder}/children", Ct))!.Items.Single();
        var asset = (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{sub.Id}/children", Ct))!.Items.Single();
        asset.Name.ShouldBe("photo.jpg");

        var metadata = (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{asset.Id}/metadata", Ct))!;
        metadata.Properties["caption"].Values.Select(v => (v.Lang, v.V!.Value.GetString())).ShouldBe([("en", "Hello"), ("de", "Hallo")], ignoreOrder: true);
        metadata.Properties["rating"].Values.Single().V!.Value.GetInt64().ShouldBe(5);
        metadata.Properties["category"].Values.Single().List.ShouldBe("beach");
        metadata.Properties["shot-by"].Values.Single().V!.Value.GetString().ShouldBe("Ansel");
        (await Admin.GetFromJsonAsync<NodeTypeDto[]>("/api/v1/node-types", Ct))!.ShouldContain(t => t.Key == "product");

        // the imported reader sees the folder granted to the group, with what lies below, and nothing else
        var grace = new Guid("5f0c3d1a-8b2e-4c6d-9e7f-1a2b3c4d5e6f");
        var reader = api.As(grace, _setup.TenantId);
        (await reader.GetAsync($"/api/v1/nodes/{sub.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/nodes/{asset.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/nodes/{TopFolder}", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        var person = await Admin.GetFromJsonAsync<Shareme.Contracts.Knowledge.PersonDto>($"/api/v1/people/{grace}", Ct);
        (person!.DisplayName, person.Department).ShouldBe(("Grace Hopper", "Research"));

        var again = await SharedienImport.RunAsync(Options(write: true), Ct);
        again.Written.Values.ShouldAllBe(count => count == 0);

        // a second tenant gets its own derived ids, but a kept source GUID cannot live in two tenants
        var second = Guid.CreateVersion7();
        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>();
            db.Add(new Coworkee.Identity.Domain.Tenant { Name = "Second", Identifier = "second-" + second.ToString("N")[..8] });
            await db.SaveChangesAsync(Ct);
            second = db.ChangeTracker.Entries<Coworkee.Identity.Domain.Tenant>().Single().Entity.Id;
        }

        SourceIds.Derived(second, "MAM.Node", 11).ShouldNotBe(SourceIds.Derived(_setup.TenantId, "MAM.Node", 11));
        var clash = await Should.ThrowAsync<InvalidOperationException>(() => SharedienImport.RunAsync(Options(write: true) with { Tenant = second }, Ct));
        clash.Message.ShouldContain("another tenant");
        (await Should.ThrowAsync<InvalidOperationException>(() => SharedienImport.RunAsync(Options(write: true) with { Tenant = Guid.CreateVersion7() }, Ct)))
            .Message.ShouldContain("does not exist");
    }

    [Fact]
    public void Options_refuse_typos_and_missing_values()
    {
        ImportOptions.Parse(["run", "--source-tenant", "1", "--tenant", TopFolder.ToString()], "s", "t")!.Write.ShouldBeTrue();
        ImportOptions.Parse(["run", "--source-tenant", "1", "--tenat", TopFolder.ToString()], "s", "t").ShouldBeNull();
        ImportOptions.Parse(["run", "--source-tenant", "--tenant", TopFolder.ToString()], "s", "t").ShouldBeNull();
        ImportOptions.Parse(["run", "--source-tenant", "1", "--tenant", TopFolder.ToString(), "--max-problems", "x"], "s", "t").ShouldBeNull();
        ImportOptions.Parse(["run", "--source-tenant", "1", "--tenant", TopFolder.ToString()], null, "t").ShouldBeNull();
        ImportOptions.Parse(["delete", "--source-tenant", "1", "--tenant", TopFolder.ToString()], "s", "t").ShouldBeNull();
    }

    [Fact]
    public void Derived_ids_are_stable_version_5_uuids_and_guid_identifiers_stay()
    {
        var scope = Guid.CreateVersion7();
        var id = SourceIds.Derived(scope, "MAM.Node", 42);
        id.ShouldBe(SourceIds.Derived(scope, "MAM.Node", 42));
        id.ShouldNotBe(SourceIds.Derived(scope, "MAM.Node", 43));
        id.ToString()[14].ShouldBe('5');
        SourceIds.For(scope, "MAM.Node", 42, TopFolder.ToString()).ShouldBe(TopFolder);
        SourceIds.For(scope, "MAM.Node", 42, "file_name").ShouldBe(id);
        SharedienMapper.Slug("Größe & Gewicht.v2").ShouldBe("groe-gewicht-v2");
        SharedienMapper.LanguageCode("de-CH").ShouldBe("de");
    }

    private const string Schema =
        """
        create schema MAM
        GO
        create schema Admin
        GO
        create table Admin.[User] (UserId bigint primary key, Identifier nvarchar(200) not null, UserName nvarchar(850) not null, FirstName nvarchar(850) null, LastName nvarchar(850) null,
            EmailAddress nvarchar(320) null, Department nvarchar(850) null, PhoneNumber nvarchar(100) null, Status int not null, IsDeleted bit not null default 0, LastLoginDate datetime2 null, CreatedAt datetime2 null);
        create table Admin.UserGroup (UserGroupId bigint primary key, Identifier nvarchar(200) not null, Name nvarchar(850) not null, Description nvarchar(2048) null, IsDeleted bit not null default 0);
        create table Admin.UserGroupUser (UserGroupUserId bigint identity primary key, UserGroupId bigint not null, UserId bigint not null);
        create table Admin.Role (RoleId bigint primary key, Identifier nvarchar(200) not null, TranslationIdentifier nvarchar(80) null, Status int not null default 1, IsDeleted bit not null default 0, IsSystem bit not null default 0);
        create table Admin.RoleObjectTypePermission (RoleObjectTypePermissionId bigint identity primary key, RoleId bigint not null, Permission int not null);
        create table Admin.UserGroupToRoleMap (UserGroupToRoleMapId bigint identity primary key, TenantId bigint not null, RoleId bigint not null, UserGroupId bigint not null);
        create table Admin.NodePermission (NodePermissionId bigint primary key, Identifier nvarchar(200) not null, NodeId bigint not null, RoleId bigint not null, Status int not null default 0, UserGroupId bigint null, UserId bigint null);
        create table MAM.Language (LanguageId bigint primary key, Identifier nvarchar(200) not null, IsDefaultLanguage bit not null, IsDeleted bit not null default 0, TranslationIdentifier nvarchar(80) null);
        create table MAM.Translation (TranslationId bigint identity primary key, TranslationIdentifier nvarchar(80) not null, FieldName nvarchar(850) not null, LanguageId bigint not null, TranslatedValue nvarchar(max) null);
        create table MAM.NodeType (NodeTypeId bigint primary key, Identifier nvarchar(200) not null, NodeSemantic int not null, TranslationIdentifier nvarchar(200) null, IsSystem bit not null, IsDeleted bit not null default 0);
        create table MAM.EdgeType (EdgeTypeId bigint primary key, Identifier nvarchar(200) not null, EdgeSemantic int not null, TranslationIdentifier nvarchar(80) null, IsDirected bit not null, IsSystem bit not null, IsDeleted bit not null default 0);
        create table MAM.Node (NodeId bigint primary key, Identifier nvarchar(200) not null, NodeTypeId bigint not null, SequenceNr int not null default 0, TenantId bigint null, IsDeleted bit not null default 0,
            CreateDate datetime2 not null default getutcdate(), MutDate datetime2 null default getutcdate(), InterruptInheritance bit not null default 0, CreateUser nvarchar(200) null);
        create table MAM.Category (CategoryId bigint identity primary key, CategoryNodeId bigint not null, TranslationIdentifier nvarchar(80) null, SequenceNr int not null default 0);
        create table MAM.CustomNode (CustomNodeId bigint identity primary key, NodeId bigint not null, TranslationIdentifier nvarchar(80) null);
        create table MAM.AssetSet (AssetSetId bigint identity primary key, AssetSetNodeId bigint not null, AssetSetStatus int not null);
        create table MAM.AssetVersion (AssetVersionId bigint identity primary key, AssetVersionNodeId bigint not null, Created datetime2 not null default getutcdate(), OriginalFilename nvarchar(850) null, Version int not null);
        create table MAM.Edge (EdgeId bigint primary key, Identifier nvarchar(200) not null, FromNodeId bigint not null, ToNodeId bigint not null, EdgeTypeId bigint not null);
        create table MAM.PropertyGroup (PropertyGroupId bigint primary key, Identifier nvarchar(200) not null, TranslationIdentifier nvarchar(80) null, SequenceNr int not null default 0, IsDeleted bit not null default 0);
        create table MAM.PropertyDefinition (PropertyDefinitionId bigint primary key, Identifier nvarchar(200) not null, PropertyGroupId bigint null, TranslationIdentifier nvarchar(80) null, ValueType int not null,
            HasListValues bit not null default 0, Editable bit not null default 1, AllowMultipleValues bit not null default 0, AllowCustomListValue bit not null default 0, SearchOption int not null default 0,
            IsLanguageDependant bit not null default 0, IsSystemProperty bit not null default 0, IgnoreCopyingOnNewVersion bit not null default 0, IsDeleted bit not null default 0,
            IsSearchableFacet bit not null default 0, HasUserContext bit not null default 0, IsUrl bit not null default 0, SortKey int null);
        create table MAM.PropertyDefinitionNodeType (PropertyDefinitionNodeTypeId bigint identity primary key, PropertyDefinitionId bigint not null, NodeTypeId bigint not null);
        create table MAM.PropertyListValue (PropertyListValueId bigint primary key, Identifier nvarchar(200) not null, PropertyDefinitionId bigint not null, SortKey int null, ValueText nvarchar(1024) null,
            TranslationIdentifier nvarchar(80) null, IsDeleted bit not null default 0, Status int not null default 1);
        create table MAM.Property (PropertyId bigint primary key, NodeId bigint null, PropertyDefinitionId bigint not null, IsDeleted bit not null default 0);
        create table MAM.PropertyValue (PropertyValueId bigint primary key, PropertyId bigint not null, PropertyListValueId bigint null, ValueBool bit null, ValueInt bigint null, ValueDecimal decimal(18,10) null,
            ValueDate date null, ValueDateTime datetime2 null, ValueDateTimeDurationTicks bigint null, ValueText nvarchar(max) null, TranslationIdentifier nvarchar(80) null, IsDeleted bit not null default 0, UserContextId bigint null);
        GO
        insert Admin.[User] (UserId, Identifier, UserName, FirstName, LastName, EmailAddress, Department, Status) values
            (1, '5f0c3d1a-8b2e-4c6d-9e7f-1a2b3c4d5e6f', 'grace', 'Grace', 'Hopper', 'grace@acme.test', 'Research', 1),
            (2, 'admin-account', 'admin', 'Ada', 'Admin', 'admin@acme.test', null, 1), (3, 'gone', 'gone', null, null, 'gone@acme.test', null, 1);
        update Admin.[User] set IsDeleted = 1 where UserId = 3;
        insert Admin.UserGroup (UserGroupId, Identifier, Name) values (1, 'readers', 'Readers');
        insert Admin.UserGroupUser (UserGroupId, UserId) values (1, 1);
        insert Admin.Role (RoleId, Identifier) values (1, 'reader'), (2, 'tenant-reader');
        insert Admin.RoleObjectTypePermission (RoleId, Permission) values (1, 42), (1, 48), (1, 14), (1, 9999), (1, 2), (2, 75);
        insert Admin.UserGroupToRoleMap (TenantId, RoleId, UserGroupId) values (7, 2, 1), (8, 1, 1);
        insert Admin.NodePermission (NodePermissionId, Identifier, NodeId, RoleId, UserGroupId) values (1, 'np1', 11, 1, 1), (2, 'np2', 40, 1, 1);
        insert MAM.Language (LanguageId, Identifier, IsDefaultLanguage) values (1, 'en-US', 1), (2, 'de-DE', 0);
        insert MAM.Translation (TranslationIdentifier, FieldName, LanguageId, TranslatedValue) values
            ('cat1', 'Name', 1, 'Marketing'), ('cat1', 'Name', 2, 'Vertrieb'), ('cn1', 'Name', 1, 'Widget'), ('nt1', 'Name', 1, 'Product'),
            ('pd2', 'Name', 1, 'Category'), ('lv1', 'Name', 1, 'Beach'), ('pv1', 'TextValue', 1, 'Hello'), ('pv1', 'TextValue', 2, 'Hallo');
        insert MAM.NodeType (NodeTypeId, Identifier, NodeSemantic, TranslationIdentifier, IsSystem) values
            (1, 'Category', 1, null, 1), (2, 'AssetSet', 2, null, 1), (3, 'Version', 3, null, 1), (4, 'product', 1000, 'nt1', 0), (5, 'Workspace', 4, null, 1), (6, 'License', 10, null, 1);
        insert MAM.EdgeType (EdgeTypeId, Identifier, EdgeSemantic, TranslationIdentifier, IsDirected, IsSystem) values
            (1, 'ParentChild', 1, null, 1, 1), (2, 'Version', 2, null, 0, 1), (3, 'Link', 3, null, 0, 1), (4, 'uses', 0, null, 1, 0);
        insert MAM.Node (NodeId, Identifier, NodeTypeId, TenantId) values
            (10, '1b0e5e34-2c4d-4f7a-9d1e-0a3b5c7d9e11', 1, 7), (11, 'sub', 1, 7), (20, 'asset', 2, 7), (21, 'v1', 3, 7), (22, 'v2', 3, 7), (30, 'widget', 4, 7), (40, 'foreign', 1, 8), (50, 'workspace', 5, 7), (51, 'in-workspace', 1, 7), (52, 'below', 1, 7);
        insert MAM.Category (CategoryNodeId, TranslationIdentifier) values (10, 'cat1'), (11, null), (40, null), (51, null), (52, null);
        insert MAM.CustomNode (NodeId, TranslationIdentifier) values (30, 'cn1');
        insert MAM.AssetSet (AssetSetNodeId, AssetSetStatus) values (20, 1);
        insert MAM.AssetVersion (AssetVersionNodeId, OriginalFilename, Version) values (21, 'old.jpg', 1), (22, 'photo.jpg', 2);
        insert MAM.Edge values (100, 'e100', 10, 11, 1), (101, 'e101', 11, 20, 1), (102, 'e102', 20, 21, 2), (103, 'e103', 20, 22, 2), (104, 'e104', 30, 20, 4), (105, 'e105', 10, 20, 3), (106, 'e106', 10, 20, 1), (107, 'e107', 50, 51, 1), (108, 'e108', 51, 52, 1);
        insert MAM.PropertyGroup (PropertyGroupId, Identifier) values (1, 'general');
        insert MAM.PropertyDefinition (PropertyDefinitionId, Identifier, PropertyGroupId, TranslationIdentifier, ValueType, HasListValues, IsLanguageDependant, IsSystemProperty) values
            (1, 'caption', 1, null, 0, 0, 1, 0), (2, '7c9e6679-7425-40de-944b-e07fc1f90ae7', 1, 'pd2', 0, 1, 0, 0), (3, 'rating', 1, null, 5, 0, 0, 0), (4, 'file_size', null, null, 5, 0, 0, 1), (5, 'shot-by', 1, null, 0, 0, 0, 0), (6, 'licence-note', null, null, 0, 0, 0, 0);
        insert MAM.PropertyDefinitionNodeType (PropertyDefinitionId, NodeTypeId) values (1, 2), (2, 2), (3, 2), (3, 1), (5, 3), (6, 6);
        insert MAM.PropertyListValue (PropertyListValueId, Identifier, PropertyDefinitionId, SortKey, TranslationIdentifier) values (1, 'beach', 2, 1, 'lv1');
        insert MAM.Property values (1, 22, 1, 0), (2, 22, 2, 0), (3, 22, 3, 0), (4, 21, 3, 0), (5, 10, 3, 0), (6, 22, 4, 0), (7, 22, 5, 0);
        insert MAM.PropertyValue (PropertyValueId, PropertyId, PropertyListValueId, ValueInt, ValueText, TranslationIdentifier) values
            (1, 1, null, null, null, 'pv1'), (2, 2, 1, null, null, null), (3, 3, null, 5, null, null), (4, 4, null, 3, null, null), (9, 5, null, null, 'not a number', null), (6, 6, null, 1234, null, null), (7, 7, null, null, 'Ansel', null);
        """;
}
