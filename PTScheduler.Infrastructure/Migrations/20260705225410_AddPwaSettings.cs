using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPwaSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PwaBannerBody",
                table: "AppBrandings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PwaBannerButton",
                table: "AppBrandings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PwaBannerEnabled",
                table: "AppBrandings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PwaBannerTitle",
                table: "AppBrandings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PwaShortName",
                table: "AppBrandings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PwaBannerBody",
                table: "AppBrandings");

            migrationBuilder.DropColumn(
                name: "PwaBannerButton",
                table: "AppBrandings");

            migrationBuilder.DropColumn(
                name: "PwaBannerEnabled",
                table: "AppBrandings");

            migrationBuilder.DropColumn(
                name: "PwaBannerTitle",
                table: "AppBrandings");

            migrationBuilder.DropColumn(
                name: "PwaShortName",
                table: "AppBrandings");
        }
    }
}
