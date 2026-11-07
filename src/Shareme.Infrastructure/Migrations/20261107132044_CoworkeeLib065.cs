using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CoworkeeLib065 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BuiltInId",
                schema: "cw",
                table: "Themes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCustomized",
                schema: "cw",
                table: "Themes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Themes_TenantId_BuiltInId",
                schema: "cw",
                table: "Themes",
                columns: new[] { "TenantId", "BuiltInId" },
                unique: true,
                filter: "\"BuiltInId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Themes_TenantId_BuiltInId",
                schema: "cw",
                table: "Themes");

            migrationBuilder.DropColumn(
                name: "BuiltInId",
                schema: "cw",
                table: "Themes");

            migrationBuilder.DropColumn(
                name: "IsCustomized",
                schema: "cw",
                table: "Themes");
        }
    }
}
