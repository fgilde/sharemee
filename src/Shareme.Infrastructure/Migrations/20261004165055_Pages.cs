using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                schema: "sm",
                table: "Nodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Html",
                schema: "sm",
                table: "Nodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Markdown",
                schema: "sm",
                table: "Nodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageVersion",
                schema: "sm",
                table: "Nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Pinned",
                schema: "sm",
                table: "Nodes",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishAt",
                schema: "sm",
                table: "Nodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Text",
                schema: "sm",
                table: "Nodes",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PageVersions",
                schema: "sm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Markdown = table.Column<string>(type: "text", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageVersions_Nodes_PageId",
                        column: x => x.PageId,
                        principalSchema: "sm",
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "sm",
                table: "NodeTypes",
                columns: new[] { "Id", "Icon", "IsSystem", "Key", "Name", "Semantic", "TenantId" },
                values: new object[,]
                {
                    { new Guid("0190f000-0000-7000-8000-000000000005"), "article", true, "page", "Page", "Page", null },
                    { new Guid("0190f000-0000-7000-8000-000000000006"), "newspaper", true, "news", "News", "News", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageVersions_PageId_Number",
                schema: "sm",
                table: "PageVersions",
                columns: new[] { "PageId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PageVersions",
                schema: "sm");

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "NodeTypes",
                keyColumn: "Id",
                keyValue: new Guid("0190f000-0000-7000-8000-000000000005"));

            migrationBuilder.DeleteData(
                schema: "sm",
                table: "NodeTypes",
                keyColumn: "Id",
                keyValue: new Guid("0190f000-0000-7000-8000-000000000006"));

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Html",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Markdown",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "PageVersion",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Pinned",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "PublishAt",
                schema: "sm",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Text",
                schema: "sm",
                table: "Nodes");
        }
    }
}
