namespace PTScheduler.Portal.Entities;

/// <summary>
/// Dzienny odczyt liczników z aplikacji trenera (klienci, treningi łącznie) — z różnic między dniami
/// liczymy trend aktywności (np. spadek liczby treningów przy ryzyku odejścia trenera).
/// </summary>
public class TenantMetricSnapshot
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public DateOnly Date { get; set; }
    public int Clients { get; set; }
    public int SessionsTotal { get; set; }
}
