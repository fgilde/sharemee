using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Assets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetTypeKey",
                schema: "sm",
                table: "Nodes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentVersionId",
                schema: "sm",
                table: "Nodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "sm",
                table: "Nodes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetTypes",
                schema: "sm",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DownloadOriginalAlways = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTypes", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AssetVersions",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Extension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BlobKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    MetadataSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetVersions_Nodes_AssetId",
                        column: x => x.AssetId,
                        principalSchema: "sm",
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrphanBlobs",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrphanBlobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UploadSessions",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ChunkSize = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileTypes",
                schema: "sm",
                columns: table => new
                {
                    Extension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssetTypeKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ForbiddenForUpload = table.Column<bool>(type: "boolean", nullable: false),
                    MagicNumbers = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileTypes", x => x.Extension);
                    table.ForeignKey(
                        name: "FK_FileTypes_AssetTypes_AssetTypeKey",
                        column: x => x.AssetTypeKey,
                        principalSchema: "sm",
                        principalTable: "AssetTypes",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "sm",
                table: "AssetTypes",
                columns: new[] { "Key", "DownloadOriginalAlways", "Name" },
                values: new object[,]
                {
                    { "archive", true, "Archive" },
                    { "audio", false, "Audio" },
                    { "document", false, "Document" },
                    { "image", false, "Image" },
                    { "other", true, "Other" },
                    { "video", false, "Video" }
                });

            migrationBuilder.InsertData(
                schema: "sm",
                table: "FileTypes",
                columns: new[] { "Extension", "AssetTypeKey", "ForbiddenForUpload", "MagicNumbers", "MimeType" },
                values: new object[,]
                {
                    { "7z", "archive", false, "[\"377ABCAF271C\"]", "application/x-7z-compressed" },
                    { "avi", "video", false, "[\"52494646\"]", "video/x-msvideo" },
                    { "bat", "other", true, "[]", "application/octet-stream" },
                    { "bmp", "image", false, "[\"424D\"]", "image/bmp" },
                    { "cmd", "other", true, "[]", "application/octet-stream" },
                    { "com", "other", true, "[]", "application/octet-stream" },
                    { "cpl", "other", true, "[]", "application/octet-stream" },
                    { "csv", "document", false, "[]", "text/csv" },
                    { "dll", "other", true, "[]", "application/octet-stream" },
                    { "doc", "document", false, "[\"D0CF11E0A1B11AE1\"]", "application/msword" },
                    { "docx", "document", false, "[\"504B0304\"]", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
                    { "exe", "other", true, "[]", "application/octet-stream" },
                    { "flac", "audio", false, "[\"664C6143\"]", "audio/flac" },
                    { "gif", "image", false, "[\"474946383761\",\"474946383961\"]", "image/gif" },
                    { "gz", "archive", false, "[\"1F8B\"]", "application/gzip" },
                    { "heic", "image", false, "[]", "image/heic" },
                    { "hta", "other", true, "[]", "application/octet-stream" },
                    { "jar", "other", true, "[]", "application/octet-stream" },
                    { "jpeg", "image", false, "[\"FFD8FF\"]", "image/jpeg" },
                    { "jpg", "image", false, "[\"FFD8FF\"]", "image/jpeg" },
                    { "js", "other", true, "[]", "application/octet-stream" },
                    { "json", "document", false, "[]", "application/json" },
                    { "m4a", "audio", false, "[]", "audio/mp4" },
                    { "md", "document", false, "[]", "text/markdown" },
                    { "mkv", "video", false, "[\"1A45DFA3\"]", "video/x-matroska" },
                    { "mov", "video", false, "[]", "video/quicktime" },
                    { "mp3", "audio", false, "[]", "audio/mpeg" },
                    { "mp4", "video", false, "[]", "video/mp4" },
                    { "msi", "other", true, "[]", "application/octet-stream" },
                    { "ogg", "audio", false, "[\"4F676753\"]", "audio/ogg" },
                    { "pdf", "document", false, "[\"25504446\"]", "application/pdf" },
                    { "png", "image", false, "[\"89504E470D0A1A0A\"]", "image/png" },
                    { "ppt", "document", false, "[\"D0CF11E0A1B11AE1\"]", "application/vnd.ms-powerpoint" },
                    { "pptx", "document", false, "[\"504B0304\"]", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
                    { "ps1", "other", true, "[]", "application/octet-stream" },
                    { "psd", "image", false, "[\"38425053\"]", "image/vnd.adobe.photoshop" },
                    { "rar", "archive", false, "[\"526172211A07\"]", "application/vnd.rar" },
                    { "scr", "other", true, "[]", "application/octet-stream" },
                    { "sh", "other", true, "[]", "application/octet-stream" },
                    { "svg", "image", false, "[]", "image/svg+xml" },
                    { "tar", "archive", false, "[]", "application/x-tar" },
                    { "tif", "image", false, "[\"49492A00\",\"4D4D002A\"]", "image/tiff" },
                    { "tiff", "image", false, "[\"49492A00\",\"4D4D002A\"]", "image/tiff" },
                    { "txt", "document", false, "[]", "text/plain" },
                    { "vbs", "other", true, "[]", "application/octet-stream" },
                    { "wav", "audio", false, "[\"52494646\"]", "audio/wav" },
                    { "webm", "video", false, "[\"1A45DFA3\"]", "video/webm" },
                    { "webp", "image", false, "[\"52494646\"]", "image/webp" },
                    { "xls", "document", false, "[\"D0CF11E0A1B11AE1\"]", "application/vnd.ms-excel" },
                    { "xlsx", "document", false, "[\"504B0304\"]", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
                    { "zip", "archive", false, "[\"504B0304\"]", "application/zip" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetVersions_AssetId_Number",
                schema: "sm",
                table: "AssetVersions",
                columns: new[] { "AssetId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetVersions_BlobKey",
                schema: "sm",
                table: "AssetVersions",
                column: "BlobKey");

            migrationBuilder.CreateIndex(
                name: "IX_AssetVersions_TenantId_Sha256",
                schema: "sm",
                table: "AssetVersions",
                columns: new[] { "TenantId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_FileTypes_AssetTypeKey",
                schema: "sm",
                table: "FileTypes",
                column: "AssetTypeKey");

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_ExpiresAt",
                schema: "sm",
                table: "UploadSessions",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetVersions",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "FileTypes",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "OrphanBlobs",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "UploadSessions",
                schema: "sm");

            migrationBuilder.DropTable(
                name: "AssetTypes",
                schema: "sm");

            migrationBuilder.DropColumn(
                name: "AssetTypeKey",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "CurrentVersionId",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "sm",
                table: "Nodes");
        }
    }
}
