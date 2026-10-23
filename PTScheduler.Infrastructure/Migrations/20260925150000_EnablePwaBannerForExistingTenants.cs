using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <summary>
    /// AddPwaSettings dodało kolumnę PwaBannerEnabled z domyślnym false, więc każdy tenant,
    /// którego wiersz brandingu istniał przed tą migracją, miał baner instalacji PWA po cichu
    /// wyłączony (encja domyślnie ma true). Włączamy go wszystkim — kto chce, wyłączy w
    /// Wygląd aplikacji → Baner instalacji PWA.
    /// </summary>
    public partial class EnablePwaBannerForExistingTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"AppBrandings\" SET \"PwaBannerEnabled\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Brak cofnięcia — poprzedni stan wynikał z błędu, nie z wyboru użytkownika.
        }
    }
}
