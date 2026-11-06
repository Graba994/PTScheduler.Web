using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PushClientActivity",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true); // nowe kanały włączone także dla istniejących użytkowników

            migrationBuilder.AddColumn<bool>(
                name: "PushMessages",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true); // nowe kanały włączone także dla istniejących użytkowników

            migrationBuilder.AddColumn<bool>(
                name: "PushPackages",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true); // nowe kanały włączone także dla istniejących użytkowników

            migrationBuilder.AddColumn<bool>(
                name: "PushReminders",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true); // nowe kanały włączone także dla istniejących użytkowników

            migrationBuilder.AddColumn<bool>(
                name: "PushSessions",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true); // nowe kanały włączone także dla istniejących użytkowników

            migrationBuilder.AddColumn<bool>(
                name: "PushTrainerMessages",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true); // nowe kanały włączone także dla istniejących użytkowników

            migrationBuilder.AddColumn<bool>(
                name: "SmsReminders",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true); // nowe kanały włączone także dla istniejących użytkowników

            migrationBuilder.AddColumn<bool>(
                name: "SmsTrainerMessages",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true); // nowe kanały włączone także dla istniejących użytkowników

            migrationBuilder.AddColumn<DateTime>(
                name: "AvatarUpdatedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalendarFeedToken",
                table: "AspNetUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CalendarFeedToken",
                table: "AspNetUsers",
                column: "CalendarFeedToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CalendarFeedToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PushClientActivity",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "PushMessages",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "PushPackages",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "PushReminders",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "PushSessions",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "PushTrainerMessages",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "SmsReminders",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "SmsTrainerMessages",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "AvatarUpdatedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CalendarFeedToken",
                table: "AspNetUsers");
        }
    }
}
