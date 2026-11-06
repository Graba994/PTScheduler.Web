using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class MuscleMapLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MuscleMapLevel",
                table: "Plans",
                type: "text",
                nullable: false,
                defaultValue: "detailed");

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "pro",
                column: "MuscleMapLevel",
                value: "detailed");

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "start",
                column: "MuscleMapLevel",
                value: "basic");

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "starter",
                column: "MuscleMapLevel",
                value: "basic");

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "studio",
                column: "MuscleMapLevel",
                value: "detailed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MuscleMapLevel",
                table: "Plans");
        }
    }
}
