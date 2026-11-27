using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <summary>
    /// Per-kanałowe śledzenie przypomnień: ReminderEmailSentAt, ReminderSmsSentAt
    /// i ReminderAttempts na Session. Pozwala oznaczać wysyłkę e-maila i SMS
    /// niezależnie, dzięki czemu częściowa awaria jednego kanału nie powoduje
    /// ponownej wysyłki drugiego, oraz porzucić przypomnienie po limicie prób.
    /// </summary>
    public partial class AddReminderChannelTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReminderAttempts",
                table: "Sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderEmailSentAt",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSmsSentAt",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReminderAttempts", table: "Sessions");
            migrationBuilder.DropColumn(name: "ReminderEmailSentAt", table: "Sessions");
            migrationBuilder.DropColumn(name: "ReminderSmsSentAt", table: "Sessions");
        }
    }
}
