using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Renditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TechnicalMetadata",
                schema: "sm",
                table: "AssetVersions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.InsertData(
                schema: "sm",
                table: "FileTypes",
                columns: new[] { "Extension", "AssetTypeKey", "ForbiddenForUpload", "MagicNumbers", "MimeType" },
                values: new object[,]
                {
                    { "ai", "image", false, "[\"25504446\",\"25215053\"]", "application/pdf" },
                    { "eps", "image", false, "[\"25215053\",\"C5D0D3C6\"]", "application/postscript" },
                    { "odp", "document", false, "[\"504B0304\"]", "application/vnd.oasis.opendocument.presentation" },
                    { "ods", "document", false, "[\"504B0304\"]", "application/vnd.oasis.opendocument.spreadsheet" },
                    { "odt", "document", false, "[\"504B0304\"]", "application/vnd.oasis.opendocument.text" },
                    { "rtf", "document", false, "[\"7B5C727466\"]", "application/rtf" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "ai");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "eps");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "odp");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "ods");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "odt");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "rtf");

            migrationBuilder.DropColumn(
                name: "TechnicalMetadata",
                schema: "sm",
                table: "AssetVersions");
        }
    }
}
