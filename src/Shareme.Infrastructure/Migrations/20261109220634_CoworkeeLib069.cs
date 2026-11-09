using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CoworkeeLib069 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxSessions",
                schema: "cw",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RequestLog",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImpersonatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    QueryString = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Route = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RequestType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Exception = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RequestHeaders = table.Column<string>(type: "text", nullable: true),
                    ResponseHeaders = table.Column<string>(type: "text", nullable: true),
                    RequestBody = table.Column<string>(type: "text", nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityEvents",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImpersonatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Device = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Browser = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ImpersonatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    EndedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "cw",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestLog_At",
                schema: "cw",
                table: "RequestLog",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLog_Path",
                schema: "cw",
                table: "RequestLog",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLog_TenantId_At",
                schema: "cw",
                table: "RequestLog",
                columns: new[] { "TenantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestLog_UserId_At",
                schema: "cw",
                table: "RequestLog",
                columns: new[] { "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_At",
                schema: "cw",
                table: "SecurityEvents",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_TenantId_At",
                schema: "cw",
                table: "SecurityEvents",
                columns: new[] { "TenantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_UserId_At",
                schema: "cw",
                table: "SecurityEvents",
                columns: new[] { "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_TenantId_UserId",
                schema: "cw",
                table: "UserSessions",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                schema: "cw",
                table: "UserSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestLog",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "SecurityEvents",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "UserSessions",
                schema: "cw");

            migrationBuilder.DropColumn(
                name: "MaxSessions",
                schema: "cw",
                table: "Users");
        }
    }
}
