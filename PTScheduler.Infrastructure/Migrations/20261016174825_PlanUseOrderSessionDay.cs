using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanUseOrderSessionDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "TrainingPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "TrainingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Use",
                table: "TrainingPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlanDayId",
                table: "Sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_PlanDayId",
                table: "Sessions",
                column: "PlanDayId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_PlanDays_PlanDayId",
                table: "Sessions",
                column: "PlanDayId",
                principalTable: "PlanDays",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_PlanDays_PlanDayId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_PlanDayId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "TrainingPlans");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "TrainingPlans");

            migrationBuilder.DropColumn(
                name: "Use",
                table: "TrainingPlans");

            migrationBuilder.DropColumn(
                name: "PlanDayId",
                table: "Sessions");
        }
    }
}
