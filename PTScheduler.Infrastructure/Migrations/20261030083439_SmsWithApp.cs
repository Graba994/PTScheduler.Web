using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SmsWithApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SmsWithApp",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmsWithApp",
                table: "NotificationPreferences");
        }
    }
}
