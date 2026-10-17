using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ThemeNameIndexChatThumbnails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThumbnailKey",
                schema: "cw",
                table: "ChatAttachments",
                type: "character varying(520)",
                maxLength: 520,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Themes_Name",
                schema: "cw",
                table: "Themes",
                column: "Name",
                unique: true,
                filter: "\"TenantId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Themes_Name",
                schema: "cw",
                table: "Themes");

            migrationBuilder.DropColumn(
                name: "ThumbnailKey",
                schema: "cw",
                table: "ChatAttachments");
        }
    }
}
