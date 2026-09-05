using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <summary>
    /// Indeksy na Sessions dla zapytań filtrujących po dacie rozpoczęcia —
    /// dotąd tabela miała tylko indeksy kluczy obcych, a StartTime jest głównym
    /// filtrem zakresowym kalendarza, statystyk i przypomnień.
    /// </summary>
    public partial class AddSessionStartTimeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sessions_StartTime",
                table: "Sessions",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TrainerUserId_StartTime",
                table: "Sessions",
                columns: new[] { "TrainerUserId", "StartTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Sessions_StartTime", table: "Sessions");
            migrationBuilder.DropIndex(name: "IX_Sessions_TrainerUserId_StartTime", table: "Sessions");
        }
    }
}
