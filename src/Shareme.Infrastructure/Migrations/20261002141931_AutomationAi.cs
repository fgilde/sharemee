using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutomationAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempt",
                schema: "sm",
                table: "DataJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ExtractionId",
                schema: "sm",
                table: "DataJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentJobId",
                schema: "sm",
                table: "DataJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExtractionDefinitions",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProtectedApiKey = table.Column<string>(type: "text", nullable: true),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Prompt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    InputKind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssetTypeKeys = table.Column<string>(type: "jsonb", nullable: false),
                    TargetKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Mappings = table.Column<string>(type: "jsonb", nullable: false),
                    MinConfidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    Language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AutoApply = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionSuggestions",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Findings = table.Column<string>(type: "jsonb", nullable: false),
                    Values = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionSuggestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionSuggestions_NodeId_DefinitionId",
                schema: "sm",
                table: "ExtractionSuggestions",
                columns: new[] { "NodeId", "DefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionSuggestions_State_CreatedAt",
                schema: "sm",
                table: "ExtractionSuggestions",
                columns: new[] { "State", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtractionDefinitions",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "ExtractionSuggestions",
                schema: "sm");

            migrationBuilder.DropColumn(
                name: "Attempt",
                schema: "sm",
                table: "DataJobs");

            migrationBuilder.DropColumn(
                name: "ExtractionId",
                schema: "sm",
                table: "DataJobs");

            migrationBuilder.DropColumn(
                name: "ParentJobId",
                schema: "sm",
                table: "DataJobs");
        }
    }
}
