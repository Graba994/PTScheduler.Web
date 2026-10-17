using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AssistantBriefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TrainerBriefSentAt",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MorningBrief",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MorningBriefHour",
                table: "NotificationPreferences",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MorningBriefSentOn",
                table: "NotificationPreferences",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PushBeforeSession",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PushMorningBrief",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrainerBriefSentAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "MorningBrief",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "MorningBriefHour",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "MorningBriefSentOn",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "PushBeforeSession",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "PushMorningBrief",
                table: "NotificationPreferences");
        }
    }
}
