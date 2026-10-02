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
            // "animated" became a system rendition; a custom type of that name keeps working under another key
            migrationBuilder.Sql("""UPDATE sm."Derivations" SET "Kind" = 'animated-custom' WHERE "Kind" = 'animated';""");
            migrationBuilder.Sql("""UPDATE sm."DerivationTypes" SET "Key" = 'animated-custom' WHERE "Key" = 'animated';""");

            // files uploaded before their type was known become 3D models (rerender then makes their previews)
            migrationBuilder.Sql("""
                UPDATE sm."AssetVersions" SET "MimeType" = CASE "Extension"
                    WHEN 'glb' THEN 'model/gltf-binary' WHEN 'gltf' THEN 'model/gltf+json' WHEN 'stl' THEN 'model/stl' WHEN 'obj' THEN 'model/obj'
                    WHEN 'fbx' THEN 'application/vnd.autodesk.fbx' WHEN '3mf' THEN 'model/3mf' ELSE 'model/ply' END
                WHERE "Extension" IN ('glb', 'gltf', 'stl', 'obj', 'fbx', '3mf', 'ply');
                UPDATE sm."Nodes" n SET "AssetTypeKey" = 'model'
                FROM sm."AssetVersions" v WHERE v."Id" = n."CurrentVersionId" AND v."Extension" IN ('glb', 'gltf', 'stl', 'obj', 'fbx', '3mf', 'ply');
                """);

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
