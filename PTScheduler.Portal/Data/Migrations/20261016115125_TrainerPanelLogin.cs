using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrainerPanelLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerPasswordHash",
                table: "Tenants",
                type: "text",
                nullable: true);

            // Zgłoszenia, które jeszcze czekają na start, mają skrót hasła w danych z kreatora — trener zaloguje się od razu.
            migrationBuilder.Sql("""
                UPDATE "Tenants" SET "OwnerPasswordHash" = ("SetupPayload"::jsonb ->> 'passwordHash')
                WHERE "SetupPayload" IS NOT NULL AND "SetupPayload" <> '' AND ("SetupPayload"::jsonb ->> 'passwordHash') IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerPasswordHash",
                table: "Tenants");
        }
    }
}
