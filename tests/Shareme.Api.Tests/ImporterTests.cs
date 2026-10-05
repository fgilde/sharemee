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
        Blob("aaaa0000old", "the old photo"u8.ToArray());
        Blob("bbbb1111new", NewFile);

        // the importer writes into the same storage as the API under test
        Environment.SetEnvironmentVariable("Coworkee__Storage__FileSystem__Root", Path.Combine(Path.GetTempPath(), "shareme-api-tests-blobs"));
        var schema = Schema.Replace("__MD5__", Convert.ToBase64String(System.Security.Cryptography.MD5.HashData(NewFile)));
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(schema, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync(Ct);
        }
    }

    private async Task SetupAsync() => _setup = await api.SetupAsync();

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("Coworkee__Storage__FileSystem__Root", null);
        await _sql.DisposeAsync();
        Directory.Delete(_blobs, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    private static readonly byte[] NewFile = "the current photo"u8.ToArray();

    private readonly string _blobs = Path.Combine(Path.GetTempPath(), "sharedien-blobs-" + Guid.NewGuid().ToString("N"));

    private ImportOptions Options(bool write) =>
        new(write, _sql.GetConnectionString(), 7, api.ConnectionString, _setup.TenantId, null, int.MaxValue) { Blobs = new FileSystemBlobs(_blobs) };

    /// <summary>Sharedien's StorageHandler layout: root/id[0..2]/id[0..4]/id.</summary>
    private void Blob(string dataId, byte[] content)
    {
        var folder = Path.Combine(_blobs, dataId[..2], dataId[..4]);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, dataId), content);
    }

    [Fact]
    public async Task Imports_catalog_tree_and_metadata_once()
    {
        // the tenant already has German: it stays and is not imported again
        (await Admin.PostAsJsonAsync("/api/v1/metadata/languages", new DataLanguageDto("de", "Deutsch", true), Ct)).EnsureSuccessStatusCode();
        var plan = await SharedienImport.RunAsync(Options(write: false), Ct);
        plan.Written.ShouldBeEmpty();
        plan.Mapped["nodes"].ShouldBe(10);
        (await Admin.GetAsync($"/api/v1/nodes/{TopFolder}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var run = await SharedienImport.RunAsync(Options(write: true), Ct);

        run.Written["nodes"].ShouldBe(10);
        run.Problems.Select(p => (p.Code, p.Table, p.SourceId)).ShouldBe(
            [("multi_parent", "MAM.Edge", 106), ("invalid_value", "MAM.PropertyValue", 9), ("user_merged", "Admin.User", 2),
                ("unmapped_permission", "Admin.RoleObjectTypePermission", 9999), ("definition_without_types", "MAM.PropertyDefinition", 6), ("restriction_on_item_role", "Admin.PropertyDefinitionRestriction", 2), ("checksum_mismatch", "MAM.AssetVersion", 21), ("missing_blob", "MAM.AssetVersion", 59),
                ("restriction_not_on_item_role", "Admin.PropertyDefinitionRestriction", 1), ("restriction_on_original", "Admin.DerivationTypeRestriction", 2),
                ("restriction_not_on_item_role", "Admin.PropertyValueRestriction", 1), ("restriction_not_on_item_role", "Admin.DerivationTypeRestriction", 1),
                ("rule_condition_not_mapped", "Admin.RuleTrigger", 3), ("rule_action_not_mapped", "Admin.RuleAction", 2)], ignoreOrder: true);
        run.Written["files"].ShouldBe(2);
        run.Skipped["workspace containers"].ShouldBe(1);
        var languages = (await Admin.GetFromJsonAsync<DataLanguageDto[]>("/api/v1/metadata/languages", Ct))!;
        languages.Select(l => l.Code).ShouldBe(["en", "de"], ignoreOrder: true);
        languages.Count(l => l.IsDefault).ShouldBe(1);
        // the stranger of another tenant stays out; tenant roles carry no node rights, so "tenant-reader" gives People.View but no folders
        (run.Written["users"], run.Written["roles"], run.Written["groups"], run.Written["node permissions"]).ShouldBe((2, 4, 2, 2));
        (run.Written["workflow states"], run.Written["comments"], run.Written["workspace roles"]).ShouldBe((2, 1, 1));

        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!;
        var top = (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{root.Id}/children", Ct))!.Items;
        top.Select(n => n.Name).ShouldBe(["Marketing", "Widget"], ignoreOrder: true);
        top.Single(n => n.Name == "Marketing").Id.ShouldBe(TopFolder);
        var sub = (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{TopFolder}/children", Ct))!.Items.Single();
        var asset = (await Admin.GetFromJsonAsync<PagedResult<NodeDto>>($"/api/v1/nodes/{sub.Id}/children", Ct))!.Items.Single(n => n.Name == "photo.jpg");
        asset.Name.ShouldBe("photo.jpg");
        var file = (await Admin.GetFromJsonAsync<Shareme.Contracts.Assets.AssetDto>($"/api/v1/assets/{asset.Id}", Ct))!;
        file.Versions.Select(v => v.Number).ShouldBe([2, 1]);
        (file.CurrentVersion!.FileName, file.CurrentVersion.Size).ShouldBe(("photo.jpg", (long)NewFile.Length));
        file.Status.ShouldBe("ready");

        // the original sits in the API's storage: it downloads, and renditions are planned for the current version only
        var link = await (await Admin.PostAsync($"/api/v1/assets/{asset.Id}/download-link", null, Ct)).Content.ReadFromJsonAsync<Shareme.Contracts.Assets.DownloadLinkDto>(Ct);
        (await Admin.GetByteArrayAsync(link!.Url, Ct)).ShouldBe(NewFile);
        (await Admin.GetFromJsonAsync<Shareme.Contracts.Assets.AssetDto>($"/api/v1/assets/{SourceIds.Derived(_setup.TenantId, "MAM.Node", 58)}", Ct))!.Status.ShouldBe("failed");

        var metadata = (await Admin.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{asset.Id}/metadata", Ct))!;
        metadata.Properties["caption"].Values.Select(v => (v.Lang, v.V!.Value.GetString())).ShouldBe([("en", "Hello"), ("de", "Hallo")], ignoreOrder: true);
        metadata.Properties["rating"].Values.Single().V!.Value.GetInt64().ShouldBe(5);
        metadata.Properties["category"].Values.Single().List.ShouldBe("beach");
        metadata.Properties["shot-by"].Values.Single().V!.Value.GetString().ShouldBe("Ansel");
        metadata.Properties["claim"].Values.Single().Ctx.ShouldBe("b2b");
        (await Admin.GetFromJsonAsync<NodeTypeDto[]>("/api/v1/node-types", Ct))!.ShouldContain(t => t.Key == "product");

        // the imported reader sees the folder granted to the group, with what lies below, and nothing else
        var grace = new Guid("5f0c3d1a-8b2e-4c6d-9e7f-1a2b3c4d5e6f");
        var reader = api.As(grace, _setup.TenantId);
        (await reader.GetAsync($"/api/v1/nodes/{sub.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/nodes/{asset.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/nodes/{TopFolder}", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await reader.GetAsync("/api/v1/people", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var restricted = (await reader.GetFromJsonAsync<NodeMetadataDto>($"/api/v1/nodes/{asset.Id}/metadata", Ct))!;
        restricted.Properties.Keys.ShouldNotContain("rating"); // hidden for the tenant role of the reader's group
        restricted.Properties.Keys.ShouldContain("caption");
        var boss = api.As(SourceIds.Derived(_setup.TenantId, "Admin.User", 4), _setup.TenantId);
        (await boss.GetAsync($"/api/v1/nodes/{TopFolder}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var person = await Admin.GetFromJsonAsync<Shareme.Contracts.Knowledge.PersonDto>($"/api/v1/people/{grace}", Ct);
        (person!.DisplayName, person.Department).ShouldBe(("Grace Hopper", "Research"));

        // the workspace is a root of its own: its folder and task live in it, the task sits in its state, its file in the workspace
        var workspaceId = SourceIds.Derived(_setup.TenantId, "MAM.Node", 50);
        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Coworkee.Infrastructure.Persistence.CoworkeeDbContext>();
            var nodes = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Graph.Node>()), Ct);
            var workspace = nodes.OfType<Shareme.Graph.Workspace>().Single();
            (workspace.Id, workspace.Name, workspace.ParentId, workspace.WorkspaceKind).ShouldBe((workspaceId, "Campaign", (Guid?)null, "collaboration"));
            var task = nodes.OfType<Shareme.Graph.WorkTask>().Single();
            (task.ParentId, task.Name, task.AssigneeIds.Single()).ShouldBe(((Guid?)workspaceId, "Brief", grace));
            nodes.Single(n => n.Name == "in-workspace").ParentId.ShouldBe(workspaceId);
            nodes.Single(n => n.Id == SourceIds.Derived(_setup.TenantId, "MAM.Node", 56)).ParentId.ShouldBe(workspaceId);
            var states = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Workspaces.WorkflowState>()), Ct);
            var todo = states.Single(st => st.IsDefault);
            (todo.Name, task.StateId, todo.AllowedTargets.Single()).ShouldBe(("To do", (Guid?)todo.Id, states.Single(st => !st.IsDefault).Id));
            states.Single(st => !st.IsDefault).IsDone.ShouldBeTrue(); // nothing leads out of it
            nodes.OfType<Shareme.Graph.Workspace>().ShouldHaveSingleItem(); // the container stays out
            var derivations = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Assets.Derivation>()), Ct);
            derivations.Select(d => d.VersionId).Distinct().ShouldBe([SourceIds.Derived(_setup.TenantId, "MAM.Node", 22)]);

            // licensing: the license with its rights and period covers the photo; the buyout prices it through the partners' group
            var license = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Drm.License>()), Ct);
            (license.Name, license.Rights.Single(), license.Status, license.ValidUntil).ShouldBe(("Editorial", "print", "active", (DateOnly?)new DateOnly(2026, 12, 31)));
            (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Drm.LicenseAsset>()), Ct)).AssetId.ShouldBe(asset.Id);
            var buyout = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Drm.Buyout>()), Ct);
            (buyout.Currency, buyout.Basis, buyout.FeePercentage, buyout.LicenseId).ShouldBe(("EUR", 100m, 10m, license.Id));
            var priceList = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Drm.PriceList>()), Ct);
            priceList.Entries.Single().Percentage.ShouldBe(50m);
            var owner = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Drm.RightsOwner>()), Ct);
            owner.BillingContactId.ShouldNotBeNull();

            // rendition types: system types are Shareme's built in kinds, the others tenant types sized from their job
            var types = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Assets.DerivationType>()), Ct);
            var web = types.Single(t => t.Key == "web-large");
            (web.Width, web.Height, web.IsDefault, web.IsOnDemand, web.AllowManualUpload).ShouldBe((800, 600, true, false, true));
            web.AssetTypeKeys.ShouldBe(["image", "document"], ignoreOrder: true);
            (types.Single(t => t.Key == "print").Width, types.Count).ShouldBe((1600, 2));
            var blocked = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Graph.Restriction>()),
                x => x.Kind == Shareme.Contracts.Graph.RestrictionKinds.BlockedRendition, Ct);
            blocked.Value.ShouldBe("web-large");

            // the value blacklist hides nodes with "night" from the tenant readers
            var night = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Metadata.PropertyListValue>()), v => v.Key == "night", Ct);
            var hiddenValue = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Graph.Restriction>()),
                x => x.Kind == Shareme.Contracts.Graph.RestrictionKinds.HiddenListValue, Ct);
            (hiddenValue.RoleId, hiddenValue.Value).ShouldBe((blocked.RoleId, "night"));
            // rules arrive switched off, with template arguments renamed; what does not map is left out and the rule marked
            var rules = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Automation.Rule>()), Ct);
            rules.ShouldAllBe(x => !x.IsActive);
            var credit = rules.Single(x => x.Name == "credit");
            credit.NodeTypeKeys.ShouldHaveSingleItem();
            credit.Conditions.ShouldBe([new("filter", "isNewVersion"), new("trigger", "equals", "rating", "5")]);
            credit.Actions.ShouldBe([new("shot-by", "{fnc.findInAscendants(category)} {var.year}")]);
            var counter = rules.Single(x => x.Name == "counter (import incomplete)");
            (counter.Conditions.Count, counter.Actions.Count).ShouldBe((0, 0));
            hiddenValue.PropertyKey.ShouldBe((await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Set<Shareme.Metadata.PropertyDefinition>()), d => d.Id == night.DefinitionId, Ct)).Key);
        }

        (await reader.GetAsync($"/api/v1/nodes/{workspaceId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK); // owner through the workspace role

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
            EmailAddress nvarchar(320) null, Department nvarchar(850) null, PhoneNumber nvarchar(100) null, Status int not null, IsDeleted bit not null default 0, LastLoginDate datetime2 null, CreatedAt datetime2 null, ProviderSubjectId nvarchar(max) null);
        create table Admin.UserGroup (UserGroupId bigint primary key, Identifier nvarchar(200) not null, Name nvarchar(850) not null, Description nvarchar(2048) null, IsDeleted bit not null default 0);
        create table Admin.UserGroupUser (UserGroupUserId bigint identity primary key, UserGroupId bigint not null, UserId bigint not null);
        create table Admin.Role (RoleId bigint primary key, Identifier nvarchar(200) not null, TranslationIdentifier nvarchar(80) null, Status int not null default 1, IsDeleted bit not null default 0, IsSystem bit not null default 0);
        create table Admin.RoleObjectTypePermission (RoleObjectTypePermissionId bigint identity primary key, RoleId bigint not null, Permission int not null);
        create table Admin.UserGroupToRoleMap (UserGroupToRoleMapId bigint identity primary key, TenantId bigint not null, RoleId bigint not null, UserGroupId bigint not null);
        create table Admin.PropertyDefinitionRestriction (PropertyDefinitionRestrictionId bigint primary key, Identifier nvarchar(200) not null, RoleId bigint not null, Status int not null default 0, Action int not null, PropertyDefinitionId bigint not null);
        create table MAM.UserContext (UserContextId bigint primary key, Identifier nvarchar(200) not null, TranslationIdentifier nvarchar(80) null, SequenceNr int not null default 0, IsDefault bit not null default 0, IsDeleted bit not null default 0);
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
        create table MAM.BillingContact (BillingContactId bigint primary key, NodeId bigint not null, Name nvarchar(200) not null, EmailAddress nvarchar(200) null, Status int not null);
        create table MAM.RightsOwner (RightsOwnerId bigint primary key, NodeId bigint not null, Name nvarchar(200) not null, BillingContactId bigint null, Status int not null);
        create table MAM.License (LicenseId bigint primary key, NodeId bigint not null, TranslationIdentifier nvarchar(80) null, Status int not null, PeriodStart datetime2 null, PeriodEnd datetime2 null);
        create table MAM.UserContextGroup (UserContextGroupId bigint primary key, Identifier nvarchar(200) not null, TranslationIdentifier nvarchar(80) null);
        create table MAM.UserContextGroupUserContext (UserContextGroupUserContext bigint identity primary key, UserContextGroupId bigint not null, UserContextId bigint not null);
        create table MAM.PriceList (PriceListId bigint primary key, Identifier nvarchar(200) not null, Name nvarchar(200) not null, Description nvarchar(max) null, Status int not null);
        create table MAM.PriceListUserContext (PriceListUserContextId bigint identity primary key, PriceListId bigint not null, UserContextId bigint null, UserContextGroupId bigint null, Percentage decimal(18,0) not null);
        create table MAM.Currency (CurrencyId bigint primary key, ShortName nvarchar(200) not null);
        create table MAM.Buyout (BuyoutId bigint primary key, Identifier nvarchar(200) not null, RightsOwnerId bigint not null, PriceListId bigint not null, LicenseId bigint not null, CurrencyId bigint null,
            Basis decimal(18,0) not null, AdditionalFeePercentage decimal(18,0) not null, FromDate datetime2 null, ToDate datetime2 null);
        create table MAM.DerivationType (DerivationTypeId bigint primary key, Identifier nvarchar(200) not null, TranslationIdentifier nvarchar(80) null, IsSystem bit not null default 1, IsDeleted bit not null default 0);
        create table MAM.AssetType (AssetTypeId bigint primary key, Identifier nvarchar(200) not null);
        create table MAM.AssetTypeDerivationType (AssetTypeDerivationTypeId bigint identity primary key, AssetTypeId bigint not null, DerivationTypeId bigint not null,
            IsDefaultDerivation bit not null, AllowManualUpload bit not null, IsOnDemand bit not null);
        create table MAM.DerivationJobDefinition (DerivationJobDefinitionId bigint identity primary key, DerivationTypeId bigint not null, JobDefinition nvarchar(max) null);
        create table Admin.PropertyValueRestriction (PropertyValueRestrictionId bigint primary key, RoleId bigint not null, Action int not null, PropertyId bigint not null);
        create table Admin.[Rule] (RuleId bigint primary key, Identifier nvarchar(200) not null, TranslationIdentifier nvarchar(80) null);
        create table Admin.RuleNodeType (RuleNodeTypeId bigint identity primary key, RuleId bigint not null, NodeTypeId bigint not null);
        create table Admin.RuleTrigger (RuleTriggerId bigint primary key, RuleId bigint not null, IsFilter bit not null, TriggerOperator int not null, PropertyId bigint null, AdditionalData varchar(max) null);
        create table Admin.RuleAction (RuleActionId bigint primary key, RuleId bigint not null, PropertyId bigint null, OnTriggerMatched bit not null, Template varchar(max) null);
        create table Admin.DerivationTypeRestriction (DerivationTypeRestrictionId bigint primary key, RoleId bigint not null, Action int not null, DerivationTypeId bigint not null);
        create table MAM.AssetDerivation (AssetDerivationId bigint identity primary key, AssetVersionId bigint not null, DerivationTypeId bigint not null, DataId nvarchar(200) null, FileSize bigint null, FileHash nvarchar(850) null);
        create table MAM.Workspace (WorkspaceId bigint identity primary key, WorkspaceNodeId bigint not null, TranslationIdentifier nvarchar(80) null, IsTemplate bit not null default 0,
            FinalizationCategoryId bigint null, CopyPropertiesToAssets bit not null default 0, WorkspaceType int not null);
        create table MAM.State (StateId bigint identity primary key, StateNodeId bigint not null, SequenceNr int null, TranslationIdentifier nvarchar(80) null, IsDefault bit not null default 0);
        create table MAM.Task (TaskId bigint identity primary key, NodeId bigint not null, Name nvarchar(400) null, Description nvarchar(max) null, DueDate date null, AssignedUserId bigint null, Status int not null, SequenceNr int null);
        create table MAM.Comment (CommentId bigint identity primary key, NodeId bigint not null, Text nvarchar(max) null, CreatedBy nvarchar(200) null, CreatedAt datetime2 null);
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
            (2, 'admin-account', 'admin', 'Ada', 'Admin', 'admin@acme.test', null, 1), (3, 'gone', 'gone', null, null, 'gone@acme.test', null, 1),
            (4, 'boss', 'boss', 'Big', 'Boss', 'boss@acme.test', null, 1), (5, 'stranger', 'stranger', null, null, 'stranger@other.test', null, 1);
        update Admin.[User] set IsDeleted = 1 where UserId = 3;
        insert Admin.UserGroup (UserGroupId, Identifier, Name) values (1, 'readers', 'Readers'), (2, 'admins', 'Admins'), (3, 'elsewhere', 'Elsewhere');
        insert Admin.UserGroupUser (UserGroupId, UserId) values (1, 1), (2, 4), (2, 2), (3, 5);
        insert Admin.Role (RoleId, Identifier) values (1, 'reader'), (2, 'tenant-reader'), (3, 'globaladmin');
        insert Admin.Role (RoleId, Identifier, IsSystem) values (4, 'workspaceOwner', 1);
        insert Admin.RoleObjectTypePermission (RoleId, Permission) values (1, 42), (1, 48), (1, 14), (1, 9999), (1, 2), (2, 75), (2, 48), (2, 181), (3, 101);
        insert Admin.UserGroupToRoleMap (TenantId, RoleId, UserGroupId) values (7, 2, 1), (8, 1, 1), (7, 3, 2), (8, 2, 3);
        insert Admin.NodePermission (NodePermissionId, Identifier, NodeId, RoleId, UserGroupId) values (1, 'np1', 11, 1, 1), (2, 'np2', 40, 1, 1);
        insert Admin.NodePermission (NodePermissionId, Identifier, NodeId, RoleId, UserId) values (3, 'np3', 50, 4, 1);
        insert Admin.PropertyDefinitionRestriction (PropertyDefinitionRestrictionId, Identifier, RoleId, Action, PropertyDefinitionId) values (1, 'r1', 2, 0, 3), (2, 'r2', 1, 2, 1);
        insert MAM.UserContext (UserContextId, Identifier, IsDefault) values (1, 'b2b', 1);
        insert MAM.Language (LanguageId, Identifier, IsDefaultLanguage) values (1, 'en-US', 1), (2, 'de-DE', 0);
        insert MAM.Translation (TranslationIdentifier, FieldName, LanguageId, TranslatedValue) values
            ('cat1', 'Name', 1, 'Marketing'), ('cat1', 'Name', 2, 'Vertrieb'), ('cn1', 'Name', 1, 'Widget'), ('nt1', 'Name', 1, 'Product'),
            ('pd2', 'Name', 1, 'Category'), ('li1', 'Name', 1, 'Editorial'), ('ws1', 'Name', 1, 'Campaign'), ('st1', 'Name', 1, 'To do'), ('lv1', 'Name', 1, 'Beach'), ('pv1', 'TextValue', 1, 'Hello'), ('pv1', 'TextValue', 2, 'Hallo');
        insert MAM.NodeType (NodeTypeId, Identifier, NodeSemantic, TranslationIdentifier, IsSystem) values
            (1, 'Category', 1, null, 1), (2, 'AssetSet', 2, null, 1), (3, 'Version', 3, null, 1), (4, 'product', 1000, 'nt1', 0), (5, 'Workspace', 4, null, 1), (6, 'License', 10, null, 1), (7, 'State', 7, null, 1), (8, 'Task', 5, null, 1), (9, 'Comment', 6, null, 1), (10, 'BillingContact', 8, null, 1), (11, 'RightsOwner', 9, null, 1), (12, 'License', 10, null, 1);
        insert MAM.EdgeType (EdgeTypeId, Identifier, EdgeSemantic, TranslationIdentifier, IsDirected, IsSystem) values
            (1, 'ParentChild', 1, null, 1, 1), (2, 'Version', 2, null, 0, 1), (3, 'Link', 3, null, 0, 1), (4, 'uses', 0, null, 1, 0), (5, 'Transition', 9, null, 1, 1), (6, 'LicenseDrm', 10, null, 1, 1);
        insert MAM.Node (NodeId, Identifier, NodeTypeId, TenantId) values
            (10, '1b0e5e34-2c4d-4f7a-9d1e-0a3b5c7d9e11', 1, 7), (11, 'sub', 1, 7), (20, 'asset', 2, 7), (21, 'v1', 3, 7), (22, 'v2', 3, 7), (30, 'widget', 4, 7), (40, 'foreign', 1, 8), (50, 'workspace', 5, 7), (51, 'in-workspace', 1, 7), (52, 'below', 1, 7), (53, 'todo', 7, 7), (54, 'done', 7, 7), (55, 'brief', 8, 7), (56, 'draft', 2, 7), (57, 'remark', 9, 7), (58, 'lost-file', 2, 7), (59, 'lost-v1', 3, 7), (61, 'container', 5, 7), (62, 'container-state', 7, 7), (70, 'billing', 10, 7), (71, 'owner', 11, 7), (72, 'licence', 12, 7);
        insert MAM.BillingContact values (1, 70, 'Accounts', 'billing@owner.test', 1);
        insert MAM.RightsOwner values (1, 71, 'Photo agency', 1, 1);
        insert MAM.License values (1, 72, 'li1', 1, '2026-01-01', '2026-12-31');
        insert MAM.UserContextGroup values (1, 'partners', null);
        insert MAM.UserContextGroupUserContext (UserContextGroupId, UserContextId) values (1, 1);
        insert MAM.PriceList values (1, 'standard', 'Standard', null, 1);
        insert MAM.PriceListUserContext (PriceListId, UserContextGroupId, Percentage) values (1, 1, 50);
        insert MAM.Currency values (1, 'eur');
        insert MAM.Buyout values (1, 'bo1', 1, 1, 1, 1, 100, 10, null, null);
        insert MAM.Workspace (WorkspaceNodeId, TranslationIdentifier, WorkspaceType) values (50, 'ws1', 2), (61, null, 2);
        insert MAM.State (StateNodeId, SequenceNr, TranslationIdentifier, IsDefault) values (53, 1, 'st1', 1), (54, 2, null, 0), (62, 1, null, 1);
        insert MAM.Task (NodeId, Name, AssignedUserId, Status) values (55, 'Brief', 1, 0);
        insert MAM.Comment (NodeId, Text, CreatedBy) values (57, 'Looks good', 'grace');
        insert MAM.Category (CategoryNodeId, TranslationIdentifier) values (10, 'cat1'), (11, null), (40, null), (51, null), (52, null);
        insert MAM.CustomNode (NodeId, TranslationIdentifier) values (30, 'cn1');
        insert MAM.AssetSet (AssetSetNodeId, AssetSetStatus) values (20, 1), (56, 1), (58, 1);
        insert MAM.AssetVersion (AssetVersionNodeId, OriginalFilename, Version) values (21, 'old.jpg', 1), (22, 'photo.jpg', 2), (59, 'gone.jpg', 1);
        insert MAM.DerivationType (DerivationTypeId, Identifier) values (1, 'Original'), (2, 'Thumbnail'), (3, 'Screen');
        insert MAM.DerivationType (DerivationTypeId, Identifier, IsSystem) values (4, 'Web Large', 0), (5, 'Print', 0);
        insert MAM.DerivationType (DerivationTypeId, Identifier, IsSystem, IsDeleted) values (6, 'Old', 0, 1);
        insert MAM.AssetType values (1, 'Image'), (2, 'PDF');
        insert MAM.AssetTypeDerivationType (AssetTypeId, DerivationTypeId, IsDefaultDerivation, AllowManualUpload, IsOnDemand) values (1, 4, 1, 0, 0), (2, 4, 0, 1, 0);
        insert MAM.DerivationJobDefinition (DerivationTypeId, JobDefinition) values (4, '{"Steps":[{"Type":"ImageMagickConvert","Arguments":"-resize 800x600 -quality 80"}]}');
        insert Admin.DerivationTypeRestriction values (1, 2, 0, 4), (2, 2, 0, 1), (3, 3, 0, 4), (4, 2, 1, 3), (5, 2, 0, 6);
        insert MAM.AssetDerivation (AssetVersionId, DerivationTypeId, DataId, FileHash) values
            (1, 1, 'aaaa0000old', 'not-the-md5'), (2, 1, 'bbbb1111new', '__MD5__'), (2, 2, 'cccc2222thumb', null), (3, 1, 'dddd3333missing', null);
        insert MAM.Edge values (100, 'e100', 10, 11, 1), (101, 'e101', 11, 20, 1), (102, 'e102', 20, 21, 2), (103, 'e103', 20, 22, 2), (104, 'e104', 30, 20, 4), (105, 'e105', 10, 20, 3), (106, 'e106', 10, 20, 1), (107, 'e107', 50, 51, 1), (108, 'e108', 51, 52, 1), (109, 'e109', 50, 53, 1), (110, 'e110', 50, 54, 1), (111, 'e111', 53, 55, 1), (112, 'e112', 55, 56, 1),
            (113, 'e113', 55, 57, 1), (114, 'e114', 53, 54, 5), (115, 'e115', 11, 58, 1), (116, 'e116', 58, 59, 2), (117, 'e117', 61, 62, 1), (118, 'e118', 62, 50, 1), (119, 'e119', 72, 20, 6);
        insert MAM.PropertyGroup (PropertyGroupId, Identifier) values (1, 'general');
        insert MAM.PropertyDefinition (PropertyDefinitionId, Identifier, PropertyGroupId, TranslationIdentifier, ValueType, HasListValues, IsLanguageDependant, IsSystemProperty) values
            (1, 'caption', 1, null, 0, 0, 1, 0), (2, '7c9e6679-7425-40de-944b-e07fc1f90ae7', 1, 'pd2', 0, 1, 0, 0), (3, 'rating', 1, null, 5, 0, 0, 0), (4, 'file_size', null, null, 5, 0, 0, 1), (5, 'shot-by', 1, null, 0, 0, 0, 0), (6, 'licence-note', null, null, 0, 0, 0, 0), (7, 'claim', 1, null, 0, 0, 0, 0), (8, 'license_rights', null, null, 0, 1, 0, 1);
        update MAM.PropertyDefinition set HasUserContext = 1 where PropertyDefinitionId = 7;
        insert MAM.PropertyDefinitionNodeType (PropertyDefinitionId, NodeTypeId) values (1, 2), (2, 2), (3, 2), (3, 1), (5, 3), (6, 6), (7, 3);
        insert MAM.PropertyListValue (PropertyListValueId, Identifier, PropertyDefinitionId, SortKey, TranslationIdentifier) values (1, 'beach', 2, 1, 'lv1'), (2, 'print', 8, 1, null), (3, 'night', 2, 2, null);
        insert MAM.Property values (1, 22, 1, 0), (2, 22, 2, 0), (3, 22, 3, 0), (4, 21, 3, 0), (5, 10, 3, 0), (6, 22, 4, 0), (7, 22, 5, 0), (8, 22, 7, 0), (9, 72, 8, 0), (10, null, 2, 0), (11, null, 3, 0), (12, null, 5, 0), (13, null, 3, 0);
        insert MAM.PropertyValue (PropertyValueId, PropertyId, PropertyListValueId, ValueInt, ValueText, TranslationIdentifier) values
            (1, 1, null, null, null, 'pv1'), (2, 2, 1, null, null, null), (3, 3, null, 5, null, null), (4, 4, null, 3, null, null), (9, 5, null, null, 'not a number', null), (6, 6, null, 1234, null, null), (7, 7, null, null, 'Ansel', null);
        insert MAM.PropertyValue (PropertyValueId, PropertyId, ValueText, UserContextId) values (8, 8, 'Best in class', 1);
        insert MAM.PropertyValue (PropertyValueId, PropertyId, PropertyListValueId) values (10, 9, 2), (11, 10, 3);
        insert MAM.PropertyValue (PropertyValueId, PropertyId, ValueInt) values (12, 11, 5);
        insert Admin.[Rule] values (1, 'credit', null), (2, 'counter', null);
        insert Admin.RuleNodeType (RuleId, NodeTypeId) values (1, 2);
        insert Admin.RuleTrigger values (1, 1, 1, 8, null, null), (2, 1, 0, 0, 11, null), (3, 2, 0, 7, null, 'x');
        insert Admin.RuleAction values (1, 1, 12, 1, '{fnc.findInAscendants(7c9e6679-7425-40de-944b-e07fc1f90ae7)} {var.year}'), (2, 2, 13, 1, '{fnc.find(rating).increment}');
        insert Admin.PropertyValueRestriction values (1, 2, 0, 10), (2, 2, 2, 10), (3, 3, 0, 10);
        """;
}
