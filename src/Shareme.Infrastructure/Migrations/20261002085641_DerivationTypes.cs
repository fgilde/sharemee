using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DerivationTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                schema: "sm",
                table: "Derivations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Watermarks",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BlobKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Position = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Opacity = table.Column<int>(type: "integer", nullable: false),
                    ScalePercent = table.Column<int>(type: "integer", nullable: false),
                    TriggerProperty = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TriggerValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Watermarks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DerivationTypes",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Quality = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsOnDemand = table.Column<bool>(type: "boolean", nullable: false),
                    AllowManualUpload = table.Column<bool>(type: "boolean", nullable: false),
                    WatermarkId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssetTypeKeys = table.Column<string>(type: "jsonb", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DerivationTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DerivationTypes_Watermarks_WatermarkId",
                        column: x => x.WatermarkId,
                        principalSchema: "sm",
                        principalTable: "Watermarks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DerivationTypes_TenantId_Key",
                schema: "sm",
                table: "DerivationTypes",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DerivationTypes_WatermarkId",
                schema: "sm",
                table: "DerivationTypes",
                column: "WatermarkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DerivationTypes",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "Watermarks",
                schema: "sm");

            migrationBuilder.DropColumn(
                name: "IsManual",
                schema: "sm",
                table: "Derivations");
        }
    }
}
