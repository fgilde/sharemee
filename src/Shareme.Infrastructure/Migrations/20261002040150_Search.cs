using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Search : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReindexRuns",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Indexed = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StartedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReindexRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedSearches",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Request = table.Column<string>(type: "jsonb", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedSearches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedSearches_TenantId_OwnerId",
                schema: "sm",
                table: "SavedSearches",
                columns: new[] { "TenantId", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReindexRuns",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "SavedSearches",
                schema: "sm");
        }
    }
}
