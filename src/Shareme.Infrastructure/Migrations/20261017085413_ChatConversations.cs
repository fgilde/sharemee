using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shareme.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChatConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_TenantId_ToUserId_ReadAt",
                schema: "cw",
                table: "ChatMessages");

            migrationBuilder.AlterColumn<Guid>(
                name: "ToUserId",
                schema: "cw",
                table: "ChatMessages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                schema: "cw",
                table: "ChatMessages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                schema: "cw",
                table: "ChatMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EditedAt",
                schema: "cw",
                table: "ChatMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Html",
                schema: "cw",
                table: "ChatMessages",
                type: "character varying(16000)",
                maxLength: 16000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplyToId",
                schema: "cw",
                table: "ChatMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatAttachments",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    BlobKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatConversations",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsGroup = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DirectKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMembers",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsOwner = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatReactions",
                schema: "cw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatReactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConversationId_SentAt",
                schema: "cw",
                table: "ChatMessages",
                columns: new[] { "ConversationId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_ConversationId_UploadedBy",
                schema: "cw",
                table: "ChatAttachments",
                columns: new[] { "ConversationId", "UploadedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_MessageId",
                schema: "cw",
                table: "ChatAttachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_TenantId_DirectKey",
                schema: "cw",
                table: "ChatConversations",
                columns: new[] { "TenantId", "DirectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMembers_ConversationId_UserId",
                schema: "cw",
                table: "ChatMembers",
                columns: new[] { "ConversationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMembers_TenantId_UserId",
                schema: "cw",
                table: "ChatMembers",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatReactions_MessageId_UserId_Emoji",
                schema: "cw",
                table: "ChatReactions",
                columns: new[] { "MessageId", "UserId", "Emoji" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatAttachments",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "ChatConversations",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "ChatMembers",
                schema: "cw");

            migrationBuilder.DropTable(
                name: "ChatReactions",
                schema: "cw");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ConversationId_SentAt",
                schema: "cw",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                schema: "cw",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "cw",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                schema: "cw",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "Html",
                schema: "cw",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ReplyToId",
                schema: "cw",
                table: "ChatMessages");

            migrationBuilder.AlterColumn<Guid>(
                name: "ToUserId",
                schema: "cw",
                table: "ChatMessages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_TenantId_ToUserId_ReadAt",
                schema: "cw",
                table: "ChatMessages",
                columns: new[] { "TenantId", "ToUserId", "ReadAt" });
        }
    }
}
