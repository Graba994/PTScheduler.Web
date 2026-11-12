using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCancellationPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LateCancellationPolicy",
                table: "TrainerConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "NoShowChargesSession",
                table: "TrainerConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLateCancellation",
                table: "Sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PackageRefunded",
                table: "Sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Dotychczas każde odwołanie zwracało sesję do pakietu.
            migrationBuilder.Sql(
                "UPDATE \"Sessions\" SET \"PackageRefunded\" = TRUE WHERE \"Status\" = 2 AND \"PackageId\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LateCancellationPolicy",
                table: "TrainerConfigs");

            migrationBuilder.DropColumn(
                name: "NoShowChargesSession",
                table: "TrainerConfigs");

            migrationBuilder.DropColumn(
                name: "IsLateCancellation",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PackageRefunded",
                table: "Sessions");
        }
    }
}
