using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AiToolCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiToolCalls",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tool = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Input = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiToolCalls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiToolCalls_TenantId_At",
                schema: "cw",
                table: "AiToolCalls",
                columns: new[] { "TenantId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiToolCalls",
                schema: "cw");
        }
    }
}
