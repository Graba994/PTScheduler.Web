using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsAndCancellationWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CancellationWindowHours",
                table: "TrainerConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    SessionBooked = table.Column<bool>(type: "boolean", nullable: false),
                    SessionCancelledByTrainer = table.Column<bool>(type: "boolean", nullable: false),
                    SessionRescheduled = table.Column<bool>(type: "boolean", nullable: false),
                    PackageAssigned = table.Column<bool>(type: "boolean", nullable: false),
                    ClientCancelledSession = table.Column<bool>(type: "boolean", nullable: false),
                    NewClientPending = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiringPackages = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId",
                table: "NotificationPreferences",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "CancellationWindowHours",
                table: "TrainerConfigs");
        }
    }
}
