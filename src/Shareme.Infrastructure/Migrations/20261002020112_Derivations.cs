using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Derivations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RenditionTag",
                schema: "sm",
                table: "Nodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Derivations",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BlobKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PlannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Derivations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Derivations_AssetVersions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "sm",
                        principalTable: "AssetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Derivations_AssetId",
                schema: "sm",
                table: "Derivations",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Derivations_BlobKey",
                schema: "sm",
                table: "Derivations",
                column: "BlobKey");

            migrationBuilder.CreateIndex(
                name: "IX_Derivations_State_PlannedAt",
                schema: "sm",
                table: "Derivations",
                columns: new[] { "State", "PlannedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Derivations_VersionId_Kind",
                schema: "sm",
                table: "Derivations",
                columns: new[] { "VersionId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Derivations",
                schema: "sm");

            migrationBuilder.DropColumn(
                name: "RenditionTag",
                schema: "sm",
                table: "Nodes");
        }
    }
}
