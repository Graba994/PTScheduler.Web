using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <summary>
    /// Włącza optymistyczną współbieżność na SessionPackage przez systemową
    /// kolumnę Postgresa <c>xmin</c> (UseXminAsConcurrencyToken).
    ///
    /// Migracja jest CELOWO pusta: <c>xmin</c> istnieje w każdej tabeli Postgresa
    /// jako kolumna systemowa, więc nie ma nic do dodania w schemacie. Zmiana
    /// dotyczy wyłącznie modelu EF (token współbieżności), a ta migracja istnieje
    /// tylko po to, żeby snapshot pozostał zsynchronizowany z modelem.
    /// </summary>
    public partial class SessionPackageConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Brak operacji — xmin to kolumna systemowa, nie wymaga DDL.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Brak operacji.
        }
    }
}
