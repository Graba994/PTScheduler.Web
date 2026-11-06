using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class MeetingsSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentCost",
                table: "TrainerMeetings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentTools",
                table: "TrainerMeetings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FollowUpAt",
                table: "TrainerMeetings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterestedFeatures",
                table: "TrainerMeetings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Objections",
                table: "TrainerMeetings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "TrainerMeetings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanFit",
                table: "TrainerMeetings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Presented",
                table: "TrainerMeetings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TrainerType",
                table: "TrainerMeetings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentCost",
                table: "TrainerMeetings");

            migrationBuilder.DropColumn(
                name: "CurrentTools",
                table: "TrainerMeetings");

            migrationBuilder.DropColumn(
                name: "FollowUpAt",
                table: "TrainerMeetings");

            migrationBuilder.DropColumn(
                name: "InterestedFeatures",
                table: "TrainerMeetings");

            migrationBuilder.DropColumn(
                name: "Objections",
                table: "TrainerMeetings");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "TrainerMeetings");

            migrationBuilder.DropColumn(
                name: "PlanFit",
                table: "TrainerMeetings");

            migrationBuilder.DropColumn(
                name: "Presented",
                table: "TrainerMeetings");

            migrationBuilder.DropColumn(
                name: "TrainerType",
                table: "TrainerMeetings");
        }
    }
}
