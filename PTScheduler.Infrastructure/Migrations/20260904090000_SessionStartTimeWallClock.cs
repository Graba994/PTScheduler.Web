using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <summary>
    /// Sessions.StartTime: timestamptz → timestamp without time zone.
    ///
    /// StartTime nigdy nie był instantem — kod zapisywał tam zegar ścienny
    /// z formularza, oznaczając go sztucznie jako UTC przez SpecifyKind, żeby
    /// przejść walidację Npgsql. Ta migracja doprowadza typ kolumny do tego,
    /// czym ta wartość faktycznie jest.
    ///
    /// WARTOŚCI SIĘ NIE ZMIENIAJĄ. Klauzula USING ... AT TIME ZONE 'UTC'
    /// odczytuje zapisany instant jako zegar ścienny w UTC, co odtwarza
    /// dokładnie tę liczbę, która została zapisana. Godziny sesji pozostają
    /// takie, jakie wpisał trener. Migracja jest w pełni odwracalna.
    /// </summary>
    public partial class SessionStartTimeWallClock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Jawne USING zamiast domyślnej konwersji: bez niego Postgres użyłby
            // bieżącego ustawienia TimeZone sesji, więc wynik zależałby od tego,
            // gdzie migracja jest uruchamiana. Z 'UTC' jest deterministyczny.
            migrationBuilder.Sql(@"
                ALTER TABLE ""Sessions""
                ALTER COLUMN ""StartTime"" TYPE timestamp without time zone
                USING ""StartTime"" AT TIME ZONE 'UTC';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Odwrotność: potraktuj zegar ścienny jako UTC i wróć do timestamptz.
            // Symetryczne do Up, więc również nie przesuwa wartości.
            migrationBuilder.Sql(@"
                ALTER TABLE ""Sessions""
                ALTER COLUMN ""StartTime"" TYPE timestamp with time zone
                USING ""StartTime"" AT TIME ZONE 'UTC';
            ");
        }
    }
}
