using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExerciseMuscleDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrimaryDetail",
                table: "Exercises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryDetail",
                table: "Exercises",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrimaryDetail",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "SecondaryDetail",
                table: "Exercises");
        }
    }
}
