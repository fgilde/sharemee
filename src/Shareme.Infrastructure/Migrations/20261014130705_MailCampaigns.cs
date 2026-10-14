using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MailCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                schema: "cw",
                table: "OutgoingMails",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MailCampaigns",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TemplateName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecipientSource = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailRecipientLists",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailRecipientLists", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMails_CampaignId",
                schema: "cw",
                table: "OutgoingMails",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_MailCampaigns_TenantId_CreatedAt",
                schema: "cw",
                table: "MailCampaigns",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MailRecipientLists_TenantId_Name",
                schema: "cw",
                table: "MailRecipientLists",
                columns: new[] { "TenantId", "Name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailCampaigns",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "MailRecipientLists",
                schema: "cw");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingMails_CampaignId",
                schema: "cw",
                table: "OutgoingMails");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                schema: "cw",
                table: "OutgoingMails");
        }
    }
}
