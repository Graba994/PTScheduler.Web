using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarFeedToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalendarFeedToken",
                table: "TrainerConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainerConfigs_CalendarFeedToken",
                table: "TrainerConfigs",
                column: "CalendarFeedToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrainerConfigs_CalendarFeedToken",
                table: "TrainerConfigs");

            migrationBuilder.DropColumn(
                name: "CalendarFeedToken",
                table: "TrainerConfigs");
        }
    }
}
