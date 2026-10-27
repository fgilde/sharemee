using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GroupTreeAndImpersonation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Groups_TenantId_Name",
                schema: "cw",
                table: "Groups");

            migrationBuilder.AddColumn<Guid>(
                name: "ImpersonatorId",
                schema: "cw",
                table: "OutboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                schema: "cw",
                table: "Groups",
                type: "character varying(95)",
                maxLength: 95,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                schema: "cw",
                table: "Groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImpersonatorId",
                schema: "cw",
                table: "AuditEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GroupSelections",
                schema: "cw",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupSelections", x => new { x.UserId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_GroupSelections_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "cw",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupSelections_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "cw",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_ParentId",
                schema: "cw",
                table: "Groups",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_TenantId_Code",
                schema: "cw",
                table: "Groups",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_TenantId_ParentId_Name",
                schema: "cw",
                table: "Groups",
                columns: new[] { "TenantId", "ParentId", "Name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_GroupSelections_GroupId",
                schema: "cw",
                table: "GroupSelections",
                column: "GroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Groups_ParentId",
                schema: "cw",
                table: "Groups",
                column: "ParentId",
                principalSchema: "cw",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_Groups_ParentId",
                schema: "cw",
                table: "Groups");

            migrationBuilder.DropTable(
                name: "GroupSelections",
                schema: "cw");

            migrationBuilder.DropIndex(
                name: "IX_Groups_ParentId",
                schema: "cw",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_TenantId_Code",
                schema: "cw",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_TenantId_ParentId_Name",
                schema: "cw",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ImpersonatorId",
                schema: "cw",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Code",
                schema: "cw",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ParentId",
                schema: "cw",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ImpersonatorId",
                schema: "cw",
                table: "AuditEntries");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_TenantId_Name",
                schema: "cw",
                table: "Groups",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }
    }
}
