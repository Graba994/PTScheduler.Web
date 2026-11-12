using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LoginTitle",
                table: "AppBrandings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoginSubtitle",
                table: "AppBrandings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoginBackgroundPath",
                table: "AppBrandings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoginTitle",
                table: "AppBrandings");

            migrationBuilder.DropColumn(
                name: "LoginSubtitle",
                table: "AppBrandings");

            migrationBuilder.DropColumn(
                name: "LoginBackgroundPath",
                table: "AppBrandings");
        }
    }
}
