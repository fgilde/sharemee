using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ThemeOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                schema: "cw",
                table: "Themes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Options",
                schema: "cw",
                table: "Themes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Shadows",
                schema: "cw",
                table: "Themes",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublished",
                schema: "cw",
                table: "Themes");

            migrationBuilder.DropColumn(
                name: "Options",
                schema: "cw",
                table: "Themes");

            migrationBuilder.DropColumn(
                name: "Shadows",
                schema: "cw",
                table: "Themes");
        }
    }
}
