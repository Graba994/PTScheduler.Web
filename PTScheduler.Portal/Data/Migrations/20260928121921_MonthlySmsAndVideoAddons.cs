using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class MonthlySmsAndVideoAddons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 100,
                columns: new[] { "CreditAmount", "DefaultPrice", "Description", "Name", "PriceType", "Unit" },
                values: new object[] { 100, 19m, "100 SMS-ów więcej co miesiąc na przypomnienia o treningach. Doliczane do abonamentu — rezygnujesz, kiedy chcesz.", "SMS +100 miesięcznie", "monthly", "miesiąc" });

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 101,
                columns: new[] { "CreditAmount", "DefaultPrice", "Description", "Name", "PriceType", "Unit" },
                values: new object[] { 300, 49m, "300 SMS-ów więcej co miesiąc — dla studiów z dużą liczbą klientów. Doliczane do abonamentu.", "SMS +300 miesięcznie", "monthly", "miesiąc" });

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 102,
                columns: new[] { "CreditAmount", "DefaultPrice", "Description", "Name", "Unit" },
                values: new object[] { 100, 29m, "Na wyjątkowo pracowity miesiąc: SMS-y nie wygasają i schodzą dopiero po wyczerpaniu miesięcznego limitu.", "Jednorazowe doładowanie 100 SMS", "100 szt." });

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 110,
                columns: new[] { "DefaultPrice", "Description", "Name", "PriceType", "Unit" },
                values: new object[] { 15m, "10 GB więcej na lekcje i instruktaże wideo, dopóki dodatek jest aktywny. Doliczane do abonamentu.", "Wideo +10 GB miejsca", "monthly", "miesiąc" });

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 111,
                columns: new[] { "DefaultPrice", "Description", "Name", "PriceType", "Unit" },
                values: new object[] { 49m, "50 GB więcej na kursy wideo — dla rozbudowanej biblioteki ćwiczeń i programów. Doliczane do abonamentu.", "Wideo +50 GB miejsca", "monthly", "miesiąc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 100,
                columns: new[] { "CreditAmount", "DefaultPrice", "Description", "Name", "PriceType", "Unit" },
                values: new object[] { 50, 15m, "50 wiadomości SMS do wysyłania przypomnień klientom.", "Pakiet 50 SMS", "one_time", "50 szt." });

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 101,
                columns: new[] { "CreditAmount", "DefaultPrice", "Description", "Name", "PriceType", "Unit" },
                values: new object[] { 200, 50m, "200 wiadomości SMS — najlepsza wartość dla aktywnych trenerów.", "Pakiet 200 SMS", "one_time", "200 szt." });

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 102,
                columns: new[] { "CreditAmount", "DefaultPrice", "Description", "Name", "Unit" },
                values: new object[] { 500, 100m, "500 wiadomości SMS — dla dużych studiów treningowych.", "Pakiet 500 SMS", "500 szt." });

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 110,
                columns: new[] { "DefaultPrice", "Description", "Name", "PriceType", "Unit" },
                values: new object[] { 20m, "Rozszerzenie przestrzeni na kursy wideo o 10 GB.", "Dodatkowe 10 GB wideo", "one_time", "10 GB" });

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 111,
                columns: new[] { "DefaultPrice", "Description", "Name", "PriceType", "Unit" },
                values: new object[] { 80m, "Rozszerzenie przestrzeni na kursy wideo o 50 GB.", "Dodatkowe 50 GB wideo", "one_time", "50 GB" });
        }
    }
}
