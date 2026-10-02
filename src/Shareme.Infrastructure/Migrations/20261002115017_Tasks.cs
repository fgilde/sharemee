using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Tasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssigneeIds",
                schema: "sm",
                table: "Nodes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                schema: "sm",
                table: "Nodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "sm",
                table: "Nodes",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                schema: "sm",
                table: "Nodes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StateId",
                schema: "sm",
                table: "Nodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkflowStates",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortIndex = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsDone = table.Column<bool>(type: "boolean", nullable: false),
                    OwnersOnly = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedTargets = table.Column<string>(type: "jsonb", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStates_Nodes_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "sm",
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // workspaces made before boards existed get the default columns
            migrationBuilder.Sql("""
                INSERT INTO sm."WorkflowStates" ("Id", "WorkspaceId", "Name", "SortIndex", "IsDefault", "IsDone", "OwnersOnly", "AllowedTargets", "TenantId")
                SELECT gen_random_uuid(), n."Id", d.name, d.sort, d.is_default, d.is_done, false, '[]', n."TenantId"
                FROM sm."Nodes" n CROSS JOIN (VALUES ('To do', 0, true, false), ('In progress', 1, false, false), ('Done', 2, false, true)) AS d(name, sort, is_default, is_done)
                WHERE n."Kind" = 'workspace';
                """);

            migrationBuilder.InsertData(
                schema: "sm",
                table: "NodeTypes",
                columns: new[] { "Id", "Icon", "IsSystem", "Key", "Name", "Semantic", "TenantId" },
                values: new object[] { new Guid("0190f000-0000-7000-8000-000000000004"), "task_alt", true, "task", "Task", "Task", null });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_StateId",
                schema: "sm",
                table: "Nodes",
                column: "StateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStates_WorkspaceId",
                schema: "sm",
                table: "WorkflowStates",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowStates",
                schema: "sm");

            migrationBuilder.DropIndex(
                name: "IX_Nodes_StateId",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "NodeTypes",
                keyColumn: "Id",
                keyValue: new Guid("0190f000-0000-7000-8000-000000000004"));

            migrationBuilder.DropColumn(
                name: "AssigneeIds",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "DueDate",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "StateId",
                schema: "sm",
                table: "Nodes");
        }
    }
}
