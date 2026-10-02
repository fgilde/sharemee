using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutomationData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimedAt",
                schema: "sm",
                table: "RuleJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DataJobs",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DryRun = table.Column<bool>(type: "boolean", nullable: false),
                    NodeIds = table.Column<string>(type: "jsonb", nullable: false),
                    Mutations = table.Column<string>(type: "jsonb", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    BlobKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Executed = table.Column<int>(type: "integer", nullable: false),
                    Skipped = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Entries = table.Column<string>(type: "jsonb", nullable: false),
                    StartedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataJobs_StartedBy_CreatedAt",
                schema: "sm",
                table: "DataJobs",
                columns: new[] { "StartedBy", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataJobs",
                schema: "sm");

            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                schema: "sm",
                table: "RuleJobs");
        }
    }
}
