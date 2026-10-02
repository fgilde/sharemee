using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Workspaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Nodes_Root",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.AddColumn<bool>(
                name: "CopyMetadataToAssets",
                schema: "sm",
                table: "Nodes",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinalizationFolderId",
                schema: "sm",
                table: "Nodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinalizedAt",
                schema: "sm",
                table: "Nodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTemplate",
                schema: "sm",
                table: "Nodes",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequiredSetId",
                schema: "sm",
                table: "Nodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                schema: "sm",
                table: "Nodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkspaceKind",
                schema: "sm",
                table: "Nodes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.InsertData(
                schema: "sm",
                table: "NodeTypes",
                columns: new[] { "Id", "Icon", "IsSystem", "Key", "Name", "Semantic", "TenantId" },
                values: new object[] { new Guid("0190f000-0000-7000-8000-000000000003"), "workspaces", true, "workspace", "Workspace", "Workspace", null });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_Root",
                schema: "sm",
                table: "Nodes",
                column: "TenantId",
                unique: true,
                filter: "\"ParentId\" IS NULL AND \"Kind\" = 'folder'");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_TenantId_IsTemplate",
                schema: "sm",
                table: "Nodes",
                columns: new[] { "TenantId", "IsTemplate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Nodes_Root",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropIndex(
                name: "IX_Nodes_TenantId_IsTemplate",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "NodeTypes",
                keyColumn: "Id",
                keyValue: new Guid("0190f000-0000-7000-8000-000000000003"));

            migrationBuilder.DropColumn(
                name: "CopyMetadataToAssets",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "FinalizationFolderId",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "IsTemplate",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "RequiredSetId",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "WorkspaceKind",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_Root",
                schema: "sm",
                table: "Nodes",
                column: "TenantId",
                unique: true,
                filter: "\"ParentId\" IS NULL");
        }
    }
}
