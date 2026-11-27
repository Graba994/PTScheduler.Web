namespace PTScheduler.Portal.Entities;

/// <summary>
/// Dzienny odczyt liczników z aplikacji trenera (klienci, treningi łącznie) — z różnic między dniami
/// liczymy trend aktywności (np. spadek liczby treningów przy ryzyku odejścia trenera).
/// Pola z „7”/„30” to okna z aplikacji (ostatnie 7/30 dni) — puste przy starszej wersji aplikacji.
/// Same sumy: żadnych danych klientów trenera.
/// </summary>
public class TenantMetricSnapshot
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public DateOnly Date { get; set; }
    public int Clients { get; set; }
    public int SessionsTotal { get; set; }
    /// <summary>Klienci z ukończonym treningiem w ostatnich 30 dniach.</summary>
    public int? ActiveClients30 { get; set; }
    /// <summary>Wizyty w ostatnich 7 dniach (odbyte i jeszcze nieoznaczone).</summary>
    public int? Sessions7 { get; set; }
    public int? Cancelled7 { get; set; }
    public int? NoShow7 { get; set; }
    /// <summary>Zaplanowane wizyty na najbliższe 7 dni.</summary>
    public int? Upcoming7 { get; set; }
    /// <summary>Opłacone zamówienia klientów u trenera w ostatnich 30 dniach (zł).</summary>
    public decimal? Revenue30 { get; set; }
    /// <summary>Zapisane treningi (klient × dzień) w ostatnich 30 dniach.</summary>
    public int? Workouts30 { get; set; }
    public int? Messages30 { get; set; }
    public int? Plans { get; set; }
}
