using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CoworkeeLib042 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                schema: "cw",
                table: "Roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "cw",
                table: "Notifications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DoneAt",
                schema: "cw",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Mailed",
                schema: "cw",
                table: "Notifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                schema: "cw",
                table: "Groups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOwner",
                schema: "cw",
                table: "GroupMembers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                schema: "cw",
                table: "DataTransferJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccessTokens",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SingleUse = table.Column<bool>(type: "boolean", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExportSchedules",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntitySet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Query = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    Recipients = table.Column<List<string>>(type: "text[]", nullable: false),
                    Groups = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Weekday = table.Column<int>(type: "integer", nullable: true),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: true),
                    Every = table.Column<int>(type: "integer", nullable: false),
                    Week = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationFollows",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationFollows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Scope = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    InApp = table.Column<bool>(type: "boolean", nullable: false),
                    Email = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingNotificationMails",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    Arguments = table.Column<List<string>>(type: "text[]", nullable: true),
                    Link = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MailModel = table.Column<string>(type: "text", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingNotificationMails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_DoneAt_Category",
                schema: "cw",
                table: "Notifications",
                columns: new[] { "UserId", "DoneAt", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_TenantId_IsDefault",
                schema: "cw",
                table: "Groups",
                columns: new[] { "TenantId", "IsDefault" },
                unique: true,
                filter: "\"IsDefault\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_ExpiresAt",
                schema: "cw",
                table: "AccessTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_Hash",
                schema: "cw",
                table: "AccessTokens",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExportSchedules_NextRunAt",
                schema: "cw",
                table: "ExportSchedules",
                column: "NextRunAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExportSchedules_TenantId_UserId",
                schema: "cw",
                table: "ExportSchedules",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationFollows_EntityType_EntityId",
                schema: "cw",
                table: "NotificationFollows",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationFollows_UserId_EntityType_EntityId",
                schema: "cw",
                table: "NotificationFollows",
                columns: new[] { "UserId", "EntityType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_Type",
                schema: "cw",
                table: "NotificationPreferences",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId_Type",
                schema: "cw",
                table: "NotificationPreferences",
                columns: new[] { "UserId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingNotificationMails_DueAt",
                schema: "cw",
                table: "PendingNotificationMails",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_PendingNotificationMails_UserId_Type_Key",
                schema: "cw",
                table: "PendingNotificationMails",
                columns: new[] { "UserId", "Type", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessTokens",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "ExportSchedules",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "NotificationFollows",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "NotificationPreferences",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "PendingNotificationMails",
                schema: "cw");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_DoneAt_Category",
                schema: "cw",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Groups_TenantId_IsDefault",
                schema: "cw",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                schema: "cw",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "cw",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "DoneAt",
                schema: "cw",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Mailed",
                schema: "cw",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                schema: "cw",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "IsOwner",
                schema: "cw",
                table: "GroupMembers");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                schema: "cw",
                table: "DataTransferJobs");
        }
    }
}
