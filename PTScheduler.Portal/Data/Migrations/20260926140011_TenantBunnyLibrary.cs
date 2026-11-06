using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantBunnyLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BunnyCdnHostname",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BunnyLibraryApiKey",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BunnyLibraryId",
                table: "Tenants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BunnyCdnHostname",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "BunnyLibraryApiKey",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "BunnyLibraryId",
                table: "Tenants");
        }
    }
}
