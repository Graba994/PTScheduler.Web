using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBunnyVideoToLesson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BunnyVideoDurationSec",
                table: "Lessons",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BunnyVideoId",
                table: "Lessons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BunnyVideoSizeBytes",
                table: "Lessons",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BunnyVideoDurationSec",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "BunnyVideoId",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "BunnyVideoSizeBytes",
                table: "Lessons");
        }
    }
}
