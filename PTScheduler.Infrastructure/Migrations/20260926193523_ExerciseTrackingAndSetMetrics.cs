using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExerciseTrackingAndSetMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DistanceMeters",
                table: "WorkoutSetLogs",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "WorkoutSetLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetDistanceMeters",
                table: "PlanExercises",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetDurationSeconds",
                table: "PlanExercises",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Tracking",
                table: "Exercises",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DistanceMeters",
                table: "WorkoutSetLogs");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "WorkoutSetLogs");

            migrationBuilder.DropColumn(
                name: "TargetDistanceMeters",
                table: "PlanExercises");

            migrationBuilder.DropColumn(
                name: "TargetDurationSeconds",
                table: "PlanExercises");

            migrationBuilder.DropColumn(
                name: "Tracking",
                table: "Exercises");
        }
    }
}
