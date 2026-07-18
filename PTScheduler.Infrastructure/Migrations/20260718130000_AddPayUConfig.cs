using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    public partial class AddPayUConfig : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PayUIsSandbox",
                table: "SiteSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PayUPosId",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayUClientId",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayUClientSecret",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayUSecondKey",
                table: "SiteSettings",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PayUIsSandbox", table: "SiteSettings");
            migrationBuilder.DropColumn(name: "PayUPosId", table: "SiteSettings");
            migrationBuilder.DropColumn(name: "PayUClientId", table: "SiteSettings");
            migrationBuilder.DropColumn(name: "PayUClientSecret", table: "SiteSettings");
            migrationBuilder.DropColumn(name: "PayUSecondKey", table: "SiteSettings");
        }
    }
}
