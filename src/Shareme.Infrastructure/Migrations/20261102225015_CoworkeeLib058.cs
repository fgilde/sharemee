using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CoworkeeLib058 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SendingSince",
                schema: "cw",
                table: "OutgoingMails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MailOwner",
                schema: "cw",
                table: "DataTransferJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ClientEntityTouches",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TouchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEntityTouches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMails_Status",
                schema: "cw",
                table: "OutgoingMails",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEntityTouches_EntityType_TouchedAt",
                schema: "cw",
                table: "ClientEntityTouches",
                columns: new[] { "EntityType", "TouchedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientEntityTouches",
                schema: "cw");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingMails_Status",
                schema: "cw",
                table: "OutgoingMails");

            migrationBuilder.DropColumn(
                name: "SendingSince",
                schema: "cw",
                table: "OutgoingMails");

            migrationBuilder.DropColumn(
                name: "MailOwner",
                schema: "cw",
                table: "DataTransferJobs");
        }
    }
}
