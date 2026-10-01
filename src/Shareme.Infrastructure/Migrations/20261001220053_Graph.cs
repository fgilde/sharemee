using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Graph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sm");

            migrationBuilder.CreateTable(
                name: "EdgeTypes",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Semantic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsDirected = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllowedPairs = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgeTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NodeTypes",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Semantic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nodes",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Path = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Names = table.Column<string>(type: "jsonb", nullable: false),
                    SortIndex = table.Column<int>(type: "integer", nullable: false),
                    InterruptInheritance = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedWithId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Nodes_NodeTypes_NodeTypeId",
                        column: x => x.NodeTypeId,
                        principalSchema: "sm",
                        principalTable: "NodeTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Nodes_Nodes_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "sm",
                        principalTable: "Nodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Edges",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EdgeTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Edges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Edges_EdgeTypes_EdgeTypeId",
                        column: x => x.EdgeTypeId,
                        principalSchema: "sm",
                        principalTable: "EdgeTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Edges_Nodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalSchema: "sm",
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Edges_Nodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalSchema: "sm",
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NodeSubscriptions",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeSubscriptions_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalSchema: "sm",
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "sm",
                table: "EdgeTypes",
                columns: new[] { "Id", "AllowedPairs", "IsDirected", "IsSystem", "Key", "Name", "Semantic", "TenantId" },
                values: new object[,]
                {
                    { new Guid("0190f000-0000-7000-8000-000000000101"), "[\"folder:*\"]", true, true, "link", "Link", "Link", null },
                    { new Guid("0190f000-0000-7000-8000-000000000102"), "[\"asset:asset\"]", false, true, "duplicate", "Duplicate", "Duplicate", null },
                    { new Guid("0190f000-0000-7000-8000-000000000103"), "[]", true, true, "variant", "Variant", "Variant", null },
                    { new Guid("0190f000-0000-7000-8000-000000000104"), "[]", true, true, "translation", "Translation", "Translation", null }
                });

            migrationBuilder.InsertData(
                schema: "sm",
                table: "NodeTypes",
                columns: new[] { "Id", "Icon", "IsSystem", "Key", "Name", "Semantic", "TenantId" },
                values: new object[,]
                {
                    { new Guid("0190f000-0000-7000-8000-000000000001"), "folder", true, "folder", "Folder", "Folder", null },
                    { new Guid("0190f000-0000-7000-8000-000000000002"), "insert_drive_file", true, "asset", "Asset", "Asset", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Edges_EdgeTypeId_FromNodeId_ToNodeId",
                schema: "sm",
                table: "Edges",
                columns: new[] { "EdgeTypeId", "FromNodeId", "ToNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Edges_FromNodeId",
                schema: "sm",
                table: "Edges",
                column: "FromNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Edges_ToNodeId",
                schema: "sm",
                table: "Edges",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EdgeTypes_TenantId_Key",
                schema: "sm",
                table: "EdgeTypes",
                columns: new[] { "TenantId", "Key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_DeletedWithId",
                schema: "sm",
                table: "Nodes",
                column: "DeletedWithId");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_NodeTypeId",
                schema: "sm",
                table: "Nodes",
                column: "NodeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_ParentId",
                schema: "sm",
                table: "Nodes",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_Path",
                schema: "sm",
                table: "Nodes",
                column: "Path")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_Root",
                schema: "sm",
                table: "Nodes",
                column: "TenantId",
                unique: true,
                filter: "\"ParentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_TenantId_ParentId_SortIndex",
                schema: "sm",
                table: "Nodes",
                columns: new[] { "TenantId", "ParentId", "SortIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeSubscriptions_NodeId_UserId",
                schema: "sm",
                table: "NodeSubscriptions",
                columns: new[] { "NodeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeTypes_TenantId_Key",
                schema: "sm",
                table: "NodeTypes",
                columns: new[] { "TenantId", "Key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Edges",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "NodeSubscriptions",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "EdgeTypes",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "Nodes",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "NodeTypes",
                schema: "sm");
        }
    }
}
