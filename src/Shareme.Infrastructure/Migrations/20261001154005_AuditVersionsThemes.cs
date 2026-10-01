using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditVersionsThemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                schema: "cw",
                table: "MailTemplateOverrides",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "EntitySnapshots",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntitySnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Themes",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    PaletteLight = table.Column<string>(type: "jsonb", nullable: false),
                    PaletteDark = table.Column<string>(type: "jsonb", nullable: false),
                    Typography = table.Column<string>(type: "jsonb", nullable: true),
                    LayoutProperties = table.Column<string>(type: "jsonb", nullable: true),
                    LogoSvg = table.Column<string>(type: "text", nullable: true),
                    CustomCss = table.Column<string>(type: "text", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Themes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantThemes",
                schema: "cw",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThemeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantThemes", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_TenantThemes_Themes_ThemeId",
                        column: x => x.ThemeId,
                        principalSchema: "cw",
                        principalTable: "Themes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntitySnapshots_EntityType_EntityId_Revision",
                schema: "cw",
                table: "EntitySnapshots",
                columns: new[] { "EntityType", "EntityId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantThemes_ThemeId",
                schema: "cw",
                table: "TenantThemes",
                column: "ThemeId");

            migrationBuilder.CreateIndex(
                name: "IX_Themes_TenantId_Name",
                schema: "cw",
                table: "Themes",
                columns: new[] { "TenantId", "Name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntitySnapshots",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "TenantThemes",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "Themes",
                schema: "cw");

            migrationBuilder.DropColumn(
                name: "Revision",
                schema: "cw",
                table: "MailTemplateOverrides");
        }
    }
}
