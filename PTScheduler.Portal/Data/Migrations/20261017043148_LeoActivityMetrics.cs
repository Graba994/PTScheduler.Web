using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class LeoActivityMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveClients30",
                table: "TenantMetricSnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Cancelled7",
                table: "TenantMetricSnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Messages30",
                table: "TenantMetricSnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoShow7",
                table: "TenantMetricSnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Plans",
                table: "TenantMetricSnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Revenue30",
                table: "TenantMetricSnapshots",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sessions7",
                table: "TenantMetricSnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Upcoming7",
                table: "TenantMetricSnapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Workouts30",
                table: "TenantMetricSnapshots",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveClients30",
                table: "TenantMetricSnapshots");

            migrationBuilder.DropColumn(
                name: "Cancelled7",
                table: "TenantMetricSnapshots");

            migrationBuilder.DropColumn(
                name: "Messages30",
                table: "TenantMetricSnapshots");

            migrationBuilder.DropColumn(
                name: "NoShow7",
                table: "TenantMetricSnapshots");

            migrationBuilder.DropColumn(
                name: "Plans",
                table: "TenantMetricSnapshots");

            migrationBuilder.DropColumn(
                name: "Revenue30",
                table: "TenantMetricSnapshots");

            migrationBuilder.DropColumn(
                name: "Sessions7",
                table: "TenantMetricSnapshots");

            migrationBuilder.DropColumn(
                name: "Upcoming7",
                table: "TenantMetricSnapshots");

            migrationBuilder.DropColumn(
                name: "Workouts30",
                table: "TenantMetricSnapshots");
        }
    }
}
