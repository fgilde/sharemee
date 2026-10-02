using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Metadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                schema: "sm",
                table: "Nodes",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "DataLanguages",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataLanguages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PropertyGroups",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortIndex = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PropertySets",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeTypeKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Items = table.Column<string>(type: "jsonb", nullable: false),
                    Conditions = table.Column<string>(type: "jsonb", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertySets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserContexts",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserContexts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PropertyDefinitions",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ValueType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ListMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AllowCustomValues = table.Column<bool>(type: "boolean", nullable: false),
                    MultiValue = table.Column<bool>(type: "boolean", nullable: false),
                    Localizable = table.Column<bool>(type: "boolean", nullable: false),
                    HasContext = table.Column<bool>(type: "boolean", nullable: false),
                    Editable = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Searchable = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Facet = table.Column<bool>(type: "boolean", nullable: false),
                    IgnoreOnNewVersion = table.Column<bool>(type: "boolean", nullable: false),
                    InheritToChildren = table.Column<bool>(type: "boolean", nullable: false),
                    NodeTypeKeys = table.Column<string>(type: "jsonb", nullable: false),
                    Validation = table.Column<string>(type: "jsonb", nullable: false),
                    SortIndex = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyDefinitions_PropertyGroups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "sm",
                        principalTable: "PropertyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PropertyListValues",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortIndex = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyListValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyListValues_PropertyDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "sm",
                        principalTable: "PropertyDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataLanguages_TenantId_Code",
                schema: "sm",
                table: "DataLanguages",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyDefinitions_GroupId",
                schema: "sm",
                table: "PropertyDefinitions",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyDefinitions_TenantId_Key",
                schema: "sm",
                table: "PropertyDefinitions",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyGroups_TenantId_Key",
                schema: "sm",
                table: "PropertyGroups",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyListValues_DefinitionId_Key",
                schema: "sm",
                table: "PropertyListValues",
                columns: new[] { "DefinitionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertySets_TenantId_NodeTypeKey",
                schema: "sm",
                table: "PropertySets",
                columns: new[] { "TenantId", "NodeTypeKey" });

            migrationBuilder.CreateIndex(
                name: "IX_UserContexts_TenantId_Key",
                schema: "sm",
                table: "UserContexts",
                columns: new[] { "TenantId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataLanguages",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "PropertyListValues",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "PropertySets",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "UserContexts",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "PropertyDefinitions",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "PropertyGroups",
                schema: "sm");

            migrationBuilder.DropColumn(
                name: "Metadata",
                schema: "sm",
                table: "Nodes");
        }
    }
}
