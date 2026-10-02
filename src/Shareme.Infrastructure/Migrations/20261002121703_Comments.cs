using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Comments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Comments",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    MentionIds = table.Column<string>(type: "jsonb", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalSchema: "sm",
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDigests",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDigests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ReadAt_CreatedAt",
                schema: "cw",
                table: "Notifications",
                columns: new[] { "ReadAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_NodeId_CreatedAt",
                schema: "sm",
                table: "Comments",
                columns: new[] { "NodeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDigests_UserId",
                schema: "cw",
                table: "NotificationDigests",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Comments",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "NotificationDigests",
                schema: "cw");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_ReadAt_CreatedAt",
                schema: "cw",
                table: "Notifications");
        }
    }
}
