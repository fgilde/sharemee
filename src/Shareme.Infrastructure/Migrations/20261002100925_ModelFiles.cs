using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "sm",
                table: "AssetTypes",
                columns: new[] { "Key", "DownloadOriginalAlways", "Name" },
                values: new object[] { "model", false, "3D model" });

            migrationBuilder.InsertData(
                schema: "sm",
                table: "FileTypes",
                columns: new[] { "Extension", "AssetTypeKey", "ForbiddenForUpload", "MagicNumbers", "MimeType" },
                values: new object[,]
                {
                    { "3mf", "model", false, "[\"504B0304\"]", "model/3mf" },
                    { "fbx", "model", false, "[]", "application/vnd.autodesk.fbx" },
                    { "glb", "model", false, "[\"676C5446\"]", "model/gltf-binary" },
                    { "gltf", "model", false, "[]", "model/gltf+json" },
                    { "obj", "model", false, "[]", "model/obj" },
                    { "ply", "model", false, "[\"706C79\"]", "model/ply" },
                    { "stl", "model", false, "[]", "model/stl" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "3mf");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "fbx");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "glb");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "gltf");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "obj");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "ply");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "FileTypes",
                keyColumn: "Extension",
                keyValue: "stl");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "AssetTypes",
                keyColumn: "Key",
                keyValue: "model");
        }
    }
}
